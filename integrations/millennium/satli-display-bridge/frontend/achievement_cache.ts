import {
  type AchievementTranslator,
  translateAchievementGroups,
} from './achievement_payload';

export function translateCachedAppDetails(
  translator: AchievementTranslator,
  appId: string | number,
  result: any,
): any {
  if (typeof result !== 'string') {
    return result;
  }
  try {
    const cache = JSON.parse(result);
    if (!Array.isArray(cache)) {
      return result;
    }
    let changed = false;
    const translated = cache.map((entry: any) => {
      if (!Array.isArray(entry) || typeof entry[0] !== 'string') {
        return entry;
      }
      const value = entry[1];
      if (!value || typeof value !== 'object' || !('data' in value)) {
        return entry;
      }
      const data = translateCachedField(translator, appId, entry[0], value.data);
      if (data === value.data) {
        return entry;
      }
      changed = true;
      return [entry[0], { ...value, data }];
    });
    return changed ? JSON.stringify(translated) : result;
  } catch (error) {
    console.warn('SATLI could not translate Steam cached app details', error);
    return result;
  }
}

export function translateLoadedAppDetailsCache(
  translator: AchievementTranslator,
  appId: string | number,
  field: unknown,
  result: any,
): any {
  if (typeof field !== 'string') {
    return result;
  }
  try {
    return translateCachedField(translator, appId, field, result);
  } catch (error) {
    console.warn(`SATLI could not translate Steam library ${field} cache`, error);
    return result;
  }
}

export function translateAchievementMapCache(
  translator: AchievementTranslator,
  data: string,
): string {
  const appMaps = JSON.parse(data);
  if (!Array.isArray(appMaps)) {
    return data;
  }
  let changed = false;
  const translatedApps = appMaps.map((appEntry: any) => {
    if (!Array.isArray(appEntry) || !Array.isArray(appEntry[1])) {
      return appEntry;
    }
    const appId = appEntry[0];
    const translatedAchievements = appEntry[1].map((achievementEntry: any) => {
      if (!Array.isArray(achievementEntry)) {
        return achievementEntry;
      }
      const translated = translator.translateAchievement(
        appId,
        achievementEntry[1],
        String(achievementEntry[0]),
      );
      if (translated === achievementEntry[1]) {
        return achievementEntry;
      }
      changed = true;
      return [achievementEntry[0], translated];
    });
    return [appEntry[0], translatedAchievements];
  });
  if (!changed) {
    return data;
  }
  console.debug('SATLI translated Steam achievement activity cache');
  return JSON.stringify(translatedApps);
}

function translateCachedField(
  translator: AchievementTranslator,
  appId: string | number,
  field: string,
  data: any,
): any {
  if (field === 'achievementmap' && typeof data === 'string') {
    return translateAchievementMapCache(translator, data);
  }
  if (field === 'achievements') {
    return translateAchievementGroups(translator, appId, data);
  }
  return data;
}
