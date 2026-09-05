import { DisplayOverrideController } from '../shared/display_override';

type AnyFunction = (...args: any[]) => any;
type MutableApi = Record<string, AnyFunction>;

export function installSteamApiOverrides(
  controller: DisplayOverrideController,
): () => void {
  const cleanups: Array<() => void> = [];
  const apps = SteamClient.Apps as unknown as MutableApi;

  wrapPromiseResult(apps, 'GetMyAchievementsForApp', cleanups, (args, result) =>
    translateAchievementResponse(controller, args[0], result));
  wrapPromiseResult(apps, 'GetFriendAchievementsForApp', cleanups, (args, result) =>
    translateAchievementResponse(controller, args[0], result));
  wrapPromiseResult(apps, 'GetAchievementsInTimeRange', cleanups, (args, result) =>
    Array.isArray(result)
      ? result.map(value => controller.translateAchievement(args[0], value))
      : result);
  wrapPromiseResult(apps, 'GetCachedAppDetails', cleanups, (_args, result) =>
    translateCachedAppDetails(controller, result));
  wrapRegistration(apps, 'RegisterForAppDetails', cleanups, controller);

  let attachedAppDetailsCache: MutableApi | undefined;
  const attachAppDetailsCache = () => {
    const appDetailsCache = (window as any).appDetailsCache as MutableApi | undefined;
    if (!appDetailsCache || appDetailsCache === attachedAppDetailsCache) {
      return;
    }
    attachedAppDetailsCache = appDetailsCache;
    wrapPromiseResult(appDetailsCache, 'GetCachedDataForApp', cleanups, (args, result) =>
      args[1] === 'achievementmap' && typeof result === 'string'
        ? translateAchievementMapCache(controller, result)
        : result,
      'Steam library cache');
    console.debug('SATLI attached to Steam library achievement cache');
  };
  attachAppDetailsCache();
  const cacheTimer = window.setInterval(attachAppDetailsCache, 1000);
  cleanups.push(() => window.clearInterval(cacheTimer));

  const gameSessions = SteamClient.GameSessions as unknown as MutableApi;
  wrapAchievementNotifications(
    gameSessions,
    'RegisterForAchievementNotification',
    cleanups,
    controller,
  );

  return () => {
    for (const cleanup of cleanups.reverse()) {
      cleanup();
    }
  };
}

function wrapAchievementNotifications(
  api: MutableApi,
  name: string,
  cleanups: Array<() => void>,
  controller: DisplayOverrideController,
): void {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find SteamClient.GameSessions.${name}`);
    return;
  }
  const wrapped = (callback: (notification: any) => void) =>
    original.call(api, (notification: any) => {
      const appId = notification?.unAppID;
      const achievement = notification?.achievement;
      callback(appId && achievement
        ? {
            ...notification,
            achievement: controller.translateAchievement(appId, achievement),
          }
        : notification);
    });
  try {
    api[name] = wrapped;
    cleanups.push(() => {
      if (api[name] === wrapped) api[name] = original;
    });
  } catch (error) {
    console.warn(`SATLI could not override SteamClient.GameSessions.${name}`, error);
  }
}

function wrapPromiseResult(
  api: MutableApi,
  name: string,
  cleanups: Array<() => void>,
  transform: (args: any[], result: any) => any,
  apiLabel = 'SteamClient.Apps',
): void {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find ${apiLabel}.${name}`);
    return;
  }
  const wrapped = async (...args: any[]) => transform(args, await original.apply(api, args));
  try {
    api[name] = wrapped;
    cleanups.push(() => {
      if (api[name] === wrapped) api[name] = original;
    });
  } catch (error) {
    console.warn(`SATLI could not override ${apiLabel}.${name}`, error);
  }
}

function wrapRegistration(
  api: MutableApi,
  name: string,
  cleanups: Array<() => void>,
  controller: DisplayOverrideController,
): void {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find SteamClient.Apps.${name}`);
    return;
  }
  const wrapped = (appId: number, callback: (details: any) => void) =>
    original.call(api, appId, (details: any) => callback(
      details?.achievements
        ? {
            ...details,
            achievements: translateAchievementGroups(controller, appId, details.achievements),
          }
        : details,
    ));
  try {
    api[name] = wrapped;
    cleanups.push(() => {
      if (api[name] === wrapped) api[name] = original;
    });
  } catch (error) {
    console.warn(`SATLI could not override SteamClient.Apps.${name}`, error);
  }
}

function translateAchievementResponse(
  controller: DisplayOverrideController,
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
      rgAchievements: result.data.rgAchievements.map(
        (value: any) => controller.translateAchievement(appId, value),
      ),
    },
  };
}

function translateAchievementGroups(
  controller: DisplayOverrideController,
  appId: string | number,
  groups: any,
): any {
  const translated = { ...groups };
  for (const key of ['vecAchievedHidden', 'vecHighlight', 'vecUnachieved']) {
    if (Array.isArray(groups[key])) {
      translated[key] = groups[key].map(
        (value: any) => controller.translateAchievement(appId, value),
      );
    }
  }
  return translated;
}

function translateCachedAppDetails(
  controller: DisplayOverrideController,
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
      if (!Array.isArray(entry) || entry[0] !== 'achievementmap') {
        return entry;
      }
      const value = entry[1];
      if (!value || typeof value !== 'object' || typeof value.data !== 'string') {
        return entry;
      }
      const data = translateAchievementMapCache(controller, value.data);
      if (data === value.data) {
        return entry;
      }
      changed = true;
      return [entry[0], { ...value, data }];
    });
    if (!changed) {
      return result;
    }
    return JSON.stringify(translated);
  } catch (error) {
    console.warn('SATLI could not translate Steam achievement activity cache', error);
    return result;
  }
}

function translateAchievementMapCache(
  controller: DisplayOverrideController,
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
      const translated = controller.translateAchievement(appId, achievementEntry[1]);
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
