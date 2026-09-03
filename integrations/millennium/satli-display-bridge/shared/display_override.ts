export interface TranslationText {
  name: string;
  description: string;
}

interface BridgeAchievement {
  translations: Record<string, TranslationText>;
  sources?: TranslationText[];
}

interface BridgeApp {
  achievements: Record<string, BridgeAchievement>;
}

export interface BridgeSnapshot {
  version: number;
  generated_at: string;
  apps: Record<string, BridgeApp>;
}

export interface OverrideMetrics {
  appCount: number;
  sourceCount: number;
  replacedCount: number;
}

type SnapshotLoader = () => Promise<unknown>;
type LanguageLoader = () => Promise<unknown>;
interface AppliedValue {
  original: string;
  replacement: string;
}

const ATTRIBUTES = ['aria-label', 'title'] as const;

export class DisplayOverrideController {
  private observer?: MutationObserver;
  private timer?: number;
  private replacements = new Map<string, string>();
  private targets = new Map<string, Map<string, TranslationText>>();
  private appliedText = new Map<Text, AppliedValue>();
  private appliedAttributes = new Map<Element, Map<string, AppliedValue>>();
  private lastPayload = '';
  private lastError = '';
  private replacedCount = 0;

  constructor(
    private readonly root: Document,
    private readonly loadSnapshot: SnapshotLoader,
    private readonly loadLanguage: LanguageLoader,
    private readonly onMetrics?: (metrics: OverrideMetrics) => void,
  ) {}

  async start(): Promise<void> {
    await this.refresh();
    this.observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'characterData' && mutation.target.parentElement) {
          this.applyToNode(mutation.target.parentElement);
        }
        if (mutation.type === 'attributes' && mutation.target instanceof Element) {
          this.applyAttributes(mutation.target);
        }
        for (const node of mutation.addedNodes) {
          this.applyToNode(node);
        }
      }
    });
    // WebKit preloads run at document start, before documentElement necessarily
    // exists. Observe the Document itself so the later <html> insertion is also
    // handled instead of aborting the controller during startup.
    this.observer.observe(this.root, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: [...ATTRIBUTES],
    });
    this.timer = window.setInterval(() => void this.refresh(), 2000);
  }

  stop(): void {
    this.observer?.disconnect();
    if (this.timer !== undefined) {
      window.clearInterval(this.timer);
    }
    this.replacements.clear();
    this.targets.clear();
    this.restoreAppliedValues();
  }

  translateAchievement<T>(appId: string | number, value: T): T {
    if (!value || typeof value !== 'object') {
      return value;
    }
    const record = value as Record<string, unknown>;
    const apiName = firstString(record, ['strID', 'id', 'achievement_name']);
    const target = apiName ? this.targets.get(String(appId))?.get(apiName) : undefined;
    if (!target) {
      return value;
    }
    const translated = { ...record };
    replaceStringField(translated, 'strName', target.name);
    replaceStringField(translated, 'strDescription', target.description);
    replaceStringField(translated, 'name', target.name);
    replaceStringField(translated, 'desc', target.description);
    replaceStringField(translated, 'title', target.name);
    replaceStringField(translated, 'description', target.description);
    return translated as T;
  }

  private async refresh(): Promise<void> {
    try {
      const [rawPayload, rawLanguage] = await Promise.all([
        this.loadSnapshot(),
        this.loadLanguage(),
      ]);
      const { snapshot, serialized: payload } = parseSnapshot(rawPayload);
      const language = parseLanguage(rawLanguage);
      const identity = `${language}\n${payload}`;
      if (identity === this.lastPayload) {
        // Steam's library renderer can update an already-mounted activity view
        // without producing mutations visible to the plugin observer. Keep the
        // existing two-second refresh useful as a bounded reconciliation pass.
        this.applyToNode(this.root);
        this.publishMetrics(snapshot);
        return;
      }
      if (snapshot.version !== 1 || typeof snapshot.apps !== 'object') {
        throw new Error('unsupported bridge format');
      }
      this.lastPayload = identity;
      this.lastError = '';
      this.replacements.clear();
      this.targets.clear();
      this.restoreAppliedValues();
      this.replacements = buildReplacementMap(snapshot, language);
      this.targets = buildAchievementTargets(snapshot, language);
      this.replacedCount = 0;
      this.applyToNode(this.root);
      this.publishMetrics(snapshot);
    } catch (error) {
      const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
      if (message !== this.lastError) {
        console.warn('SATLI display bridge refresh failed', error);
        this.lastError = message;
      }
    }
  }

  private applyToNode(node: Node): void {
    if (this.replacements.size === 0) {
      return;
    }
    if (node.nodeType === Node.TEXT_NODE) {
      this.applyTextNode(node as Text);
      return;
    }
    if (node.nodeType === Node.DOCUMENT_NODE) {
      const documentElement = (node as Document).documentElement;
      if (documentElement) {
        this.applyToNode(documentElement);
      }
      return;
    }
    if (!(node instanceof Element)) {
      return;
    }
    this.applyAttributes(node);
    const walker = this.root.createTreeWalker(node, NodeFilter.SHOW_TEXT);
    let current = walker.nextNode();
    while (current) {
      this.applyTextNode(current as Text);
      current = walker.nextNode();
    }
    for (const element of node.querySelectorAll('[aria-label], [title]')) {
      this.applyAttributes(element);
    }
  }

  private applyTextNode(node: Text): void {
    const value = node.nodeValue ?? '';
    const applied = this.appliedText.get(node);
    if (applied?.replacement === value) {
      return;
    }
    if (applied) {
      this.appliedText.delete(node);
    }
    const trimmed = value.trim();
    const replacement = this.replacements.get(trimmed);
    if (!replacement || replacement === trimmed) {
      return;
    }
    const start = value.indexOf(trimmed);
    const rendered = `${value.slice(0, start)}${replacement}${value.slice(start + trimmed.length)}`;
    this.appliedText.set(node, { original: value, replacement: rendered });
    node.nodeValue = rendered;
    this.replacedCount++;
  }

  private applyAttributes(element: Element): void {
    for (const attribute of ATTRIBUTES) {
      const value = element.getAttribute(attribute);
      const applied = this.appliedAttributes.get(element)?.get(attribute);
      if (applied?.replacement === value) {
        continue;
      }
      if (applied) {
        this.appliedAttributes.get(element)?.delete(attribute);
      }
      const replacement = value ? this.replacements.get(value.trim()) : undefined;
      if (replacement && replacement !== value) {
        const attributes = this.appliedAttributes.get(element) ?? new Map<string, AppliedValue>();
        attributes.set(attribute, { original: value!, replacement });
        this.appliedAttributes.set(element, attributes);
        element.setAttribute(attribute, replacement);
        this.replacedCount++;
      }
    }
  }

  private restoreAppliedValues(): void {
    for (const [node, applied] of this.appliedText) {
      if (node.nodeValue === applied.replacement) {
        node.nodeValue = applied.original;
      }
    }
    this.appliedText.clear();
    for (const [element, attributes] of this.appliedAttributes) {
      for (const [attribute, applied] of attributes) {
        if (element.getAttribute(attribute) === applied.replacement) {
          element.setAttribute(attribute, applied.original);
        }
      }
    }
    this.appliedAttributes.clear();
  }

  private publishMetrics(snapshot: BridgeSnapshot): void {
    this.onMetrics?.({
      appCount: Object.keys(snapshot.apps).length,
      sourceCount: this.replacements.size,
      replacedCount: this.replacedCount,
    });
  }
}

function parseSnapshot(raw: unknown): { snapshot: BridgeSnapshot; serialized: string } {
  const value = unwrapFfiValue(raw);
  if (typeof value === 'string') {
    return { snapshot: JSON.parse(value) as BridgeSnapshot, serialized: value };
  }
  if (value && typeof value === 'object') {
    return { snapshot: value as BridgeSnapshot, serialized: JSON.stringify(value) };
  }
  throw new TypeError(`unexpected bridge payload type: ${typeof value}`);
}

function parseLanguage(raw: unknown): string {
  const value = unwrapFfiValue(raw);
  if (typeof value !== 'string') {
    throw new TypeError(`unexpected Steam language type: ${typeof value}`);
  }
  return normalizeLanguage(value);
}

function unwrapFfiValue(value: unknown): unknown {
  if (!value || typeof value !== 'object') {
    return value;
  }
  const record = value as Record<string, unknown>;
  for (const key of ['returnValue', 'returnJson', 'value']) {
    if (Object.prototype.hasOwnProperty.call(record, key)) {
      return record[key];
    }
  }
  return value;
}

function buildReplacementMap(
  snapshot: BridgeSnapshot,
  language: string,
): Map<string, string> {
  const candidates = new Map<string, Set<string>>();
  for (const app of Object.values(snapshot.apps)) {
    for (const achievement of Object.values(app.achievements ?? {})) {
      const target = achievement.translations?.[language];
      if (!target) {
        continue;
      }
      const sources = achievement.sources ?? Object.values(achievement.translations);
      for (const source of sources) {
        addCandidate(candidates, source.name, target.name);
        addCandidate(candidates, source.description, target.description);
      }
    }
  }
  const result = new Map<string, string>();
  for (const [source, targets] of candidates) {
    if (targets.size === 1) {
      result.set(source, targets.values().next().value as string);
    }
  }
  return result;
}

function buildAchievementTargets(
  snapshot: BridgeSnapshot,
  language: string,
): Map<string, Map<string, TranslationText>> {
  const apps = new Map<string, Map<string, TranslationText>>();
  for (const [appId, app] of Object.entries(snapshot.apps)) {
    const achievements = new Map<string, TranslationText>();
    for (const [apiName, achievement] of Object.entries(app.achievements ?? {})) {
      const target = achievement.translations?.[language];
      if (target) {
        achievements.set(apiName, target);
      }
    }
    apps.set(appId, achievements);
  }
  return apps;
}

function firstString(record: Record<string, unknown>, keys: string[]): string | undefined {
  for (const key of keys) {
    if (typeof record[key] === 'string' && record[key]) {
      return record[key] as string;
    }
  }
  return undefined;
}

function replaceStringField(
  record: Record<string, unknown>,
  key: string,
  replacement: string,
): void {
  if (typeof record[key] === 'string' && replacement) {
    record[key] = replacement;
  }
}

function addCandidate(
  candidates: Map<string, Set<string>>,
  source: string,
  target: string,
): void {
  const normalizedSource = source?.trim();
  const normalizedTarget = target?.trim();
  if (!normalizedSource || !normalizedTarget || normalizedSource === normalizedTarget) {
    return;
  }
  const targets = candidates.get(normalizedSource) ?? new Set<string>();
  targets.add(normalizedTarget);
  candidates.set(normalizedSource, targets);
}

function normalizeLanguage(language: string): string {
  const normalized = language.toLowerCase().replaceAll('-', '').replaceAll('_', '');
  if (normalized === 'zhcn' || normalized === 'zhhans' || normalized === 'simplifiedchinese') {
    return 'schinese';
  }
  if (normalized === 'zhtw' || normalized === 'zhhant' || normalized === 'traditionalchinese') {
    return 'tchinese';
  }
  return language.toLowerCase();
}
