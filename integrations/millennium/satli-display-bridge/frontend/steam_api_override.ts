import { DisplayOverrideController } from '../shared/display_override';
import {
  translateCachedAppDetails,
  translateLoadedAppDetailsCache,
} from './achievement_cache';
import {
  translateAchievementCollection,
  translateAchievementGroups,
  translateAchievementNotification,
  translateAchievementResponse,
} from './achievement_payload';

type AnyFunction = (...args: any[]) => any;
type MutableApi = Record<string, AnyFunction>;

export function installSteamApiOverrides(
  controller: DisplayOverrideController,
): () => void {
  const cleanups: Array<() => void> = [];
  const activePaths: string[] = [];
  const apps = SteamClient.Apps as unknown as MutableApi;

  if (wrapPromiseResult(apps, 'GetMyAchievementsForApp', cleanups, (args, result) =>
    translateAchievementResponse(controller, args[0], result))) {
    activePaths.push('my achievements');
  }
  if (wrapPromiseResult(apps, 'GetFriendAchievementsForApp', cleanups, (args, result) =>
    translateAchievementResponse(controller, args[0], result))) {
    activePaths.push('friend achievements');
  }
  if (wrapPromiseResult(apps, 'GetAchievementsInTimeRange', cleanups, (args, result) =>
    translateAchievementCollection(controller, args[0], result))) {
    activePaths.push('session history');
  }
  if (wrapPromiseResult(apps, 'GetCachedAppDetails', cleanups, (args, result) =>
    translateCachedAppDetails(controller, args[0], result))) {
    activePaths.push('native app-details cache');
  }
  if (wrapRegistration(apps, 'RegisterForAppDetails', cleanups, controller)) {
    activePaths.push('live app details');
  }

  let attachedAppDetailsCache: MutableApi | undefined;
  const attachAppDetailsCache = () => {
    const appDetailsCache = (window as any).appDetailsCache as MutableApi | undefined;
    if (!appDetailsCache || appDetailsCache === attachedAppDetailsCache) {
      return;
    }
    attachedAppDetailsCache = appDetailsCache;
    if (wrapPromiseResult(appDetailsCache, 'GetCachedDataForApp', cleanups, (args, result) =>
      translateLoadedAppDetailsCache(controller, args[0], args[1], result),
      'Steam library cache')) {
      console.debug('SATLI attached to Steam library achievements and activity caches');
    }
  };
  attachAppDetailsCache();
  const cacheTimer = window.setInterval(attachAppDetailsCache, 1000);
  cleanups.push(() => window.clearInterval(cacheTimer));

  console.log(`SATLI Steam API coverage active: ${activePaths.join(', ') || 'DOM fallback only'}`);

  let attachedGameSessions: MutableApi | undefined;
  let gameSessionsTimer: number | undefined;
  let gameSessionsAttempts = 0;
  const attachGameSessions = (): boolean => {
    const gameSessions = SteamClient.GameSessions as unknown as MutableApi | undefined;
    if (!gameSessions || gameSessions === attachedGameSessions
      || typeof gameSessions.RegisterForAchievementNotification !== 'function') {
      return false;
    }
    if (!wrapAchievementNotifications(
      gameSessions,
      'RegisterForAchievementNotification',
      cleanups,
      controller,
    )) {
      return false;
    }
    attachedGameSessions = gameSessions;
    console.debug('SATLI attached to Steam in-game achievement notifications');
    return true;
  };

  if (!attachGameSessions()) {
    console.debug('SATLI waiting for the Steam in-game achievement notification API');
    gameSessionsTimer = window.setInterval(() => {
      gameSessionsAttempts += 1;
      if (attachGameSessions()) {
        window.clearInterval(gameSessionsTimer);
        gameSessionsTimer = undefined;
      } else if (gameSessionsAttempts >= 150) {
        window.clearInterval(gameSessionsTimer);
        gameSessionsTimer = undefined;
        console.warn('SATLI could not attach to Steam in-game achievement notifications before the startup deadline');
      }
    }, 100);
    cleanups.push(() => {
      if (gameSessionsTimer !== undefined) window.clearInterval(gameSessionsTimer);
    });
  }

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
): boolean {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find SteamClient.GameSessions.${name}`);
    return false;
  }
  const wrapped = (callback: (notification: any) => void) =>
    original.call(api, (notification: any) => {
      callback(translateAchievementNotification(controller, notification));
    });
  try {
    api[name] = wrapped;
    cleanups.push(() => {
      if (api[name] === wrapped) api[name] = original;
    });
    return true;
  } catch (error) {
    console.warn(`SATLI could not override SteamClient.GameSessions.${name}`, error);
    return false;
  }
}

function wrapPromiseResult(
  api: MutableApi,
  name: string,
  cleanups: Array<() => void>,
  transform: (args: any[], result: any) => any,
  apiLabel = 'SteamClient.Apps',
): boolean {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find ${apiLabel}.${name}`);
    return false;
  }
  const wrapped = async (...args: any[]) => transform(args, await original.apply(api, args));
  try {
    api[name] = wrapped;
    cleanups.push(() => {
      if (api[name] === wrapped) api[name] = original;
    });
    return true;
  } catch (error) {
    console.warn(`SATLI could not override ${apiLabel}.${name}`, error);
    return false;
  }
}

function wrapRegistration(
  api: MutableApi,
  name: string,
  cleanups: Array<() => void>,
  controller: DisplayOverrideController,
): boolean {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find SteamClient.Apps.${name}`);
    return false;
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
    return true;
  } catch (error) {
    console.warn(`SATLI could not override SteamClient.Apps.${name}`, error);
    return false;
  }
}
