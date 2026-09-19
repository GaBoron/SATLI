export interface AchievementTranslator {
  translateAchievement<T>(appId: string | number, value: T, apiNameHint?: string): T;
}

const GROUP_KEYS = [
  'vecAchieved',
  'vecAchievedHidden',
  'vecHighlight',
  'vecUnachieved',
  'achieved',
  'hidden',
  'unachieved',
] as const;

export function translateAchievementResponse(
  translator: AchievementTranslator,
  appId: string | number,
  result: any,
): any {
  if (!Array.isArray(result?.data?.rgAchievements)) {
    return result;
  }
  return {
    ...result,
    data: {
      ...result.data,
      rgAchievements: translateAchievementCollection(
        translator,
        appId,
        result.data.rgAchievements,
      ),
    },
  };
}

export function translateAchievementGroups(
  translator: AchievementTranslator,
  appId: string | number,
  groups: any,
): any {
  if (!groups || typeof groups !== 'object' || Array.isArray(groups)) {
    return groups;
  }
  let changed = false;
  const translated = { ...groups };
  for (const key of GROUP_KEYS) {
    const value = groups[key];
    if (!Array.isArray(value) && !isRecord(value)) {
      continue;
    }
    const replacement = translateAchievementCollection(translator, appId, value);
    if (replacement !== value) {
      translated[key] = replacement;
      changed = true;
    }
  }
  return changed ? translated : groups;
}

export function translateAchievementNotification(
  translator: AchievementTranslator,
  notification: any,
): any {
  const appId = notification?.unAppID ?? notification?.appId ?? notification?.appid;
  const achievement = notification?.achievement;
  if (!appId || !achievement) {
    return notification;
  }
  const grouped = translateAchievementGroups(translator, appId, achievement);
  const translated = grouped !== achievement
    ? grouped
    : translator.translateAchievement(appId, achievement);
  return translated === achievement
    ? notification
    : { ...notification, achievement: translated };
}

export function translateAchievementCollection(
  translator: AchievementTranslator,
  appId: string | number,
  value: any,
): any {
  if (Array.isArray(value)) {
    let changed = false;
    const translated = value.map((achievement) => {
      const replacement = translator.translateAchievement(appId, achievement);
      changed = changed || replacement !== achievement;
      return replacement;
    });
    return changed ? translated : value;
  }
  if (!isRecord(value)) {
    return value;
  }
  let changed = false;
  const translated: Record<string, unknown> = { ...value };
  for (const [apiName, achievement] of Object.entries(value)) {
    const replacement = translator.translateAchievement(appId, achievement, apiName);
    translated[apiName] = replacement;
    changed = changed || replacement !== achievement;
  }
  return changed ? translated : value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}
