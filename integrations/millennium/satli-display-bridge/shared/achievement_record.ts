export interface AchievementTranslation {
  name: string;
  description: string;
}

const API_NAME_FIELDS = [
  'strID',
  'id',
  'achievement_name',
  'apiName',
  'apiname',
  'api_name',
  'internal_name',
] as const;

const NAME_FIELDS = [
  'strName',
  'name',
  'title',
  'displayName',
  'display_name',
  'localized_name',
] as const;

const DESCRIPTION_FIELDS = [
  'strDescription',
  'description',
  'desc',
  'displayDescription',
  'display_description',
  'localized_desc',
] as const;

export function readAchievementApiName(
  value: Record<string, unknown>,
  hint?: string,
): string | undefined {
  for (const field of API_NAME_FIELDS) {
    if (typeof value[field] === 'string' && value[field]) {
      return value[field] as string;
    }
  }
  return hint || undefined;
}

export function applyAchievementTranslation<T>(
  value: T,
  translation: AchievementTranslation,
): T {
  if (!value || typeof value !== 'object') {
    return value;
  }
  const source = value as Record<string, unknown>;
  const translated = { ...source };
  let changed = false;
  for (const field of NAME_FIELDS) {
    changed = replaceStringField(translated, field, translation.name) || changed;
  }
  for (const field of DESCRIPTION_FIELDS) {
    changed = replaceStringField(translated, field, translation.description) || changed;
  }
  return (changed ? translated : value) as T;
}

function replaceStringField(
  value: Record<string, unknown>,
  field: string,
  replacement: string,
): boolean {
  if (typeof value[field] !== 'string' || !replacement || value[field] === replacement) {
    return false;
  }
  value[field] = replacement;
  return true;
}
