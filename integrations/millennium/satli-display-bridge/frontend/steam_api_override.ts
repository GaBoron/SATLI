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
  wrapRegistration(apps, 'RegisterForAppDetails', cleanups, controller);

  return () => {
    for (const cleanup of cleanups.reverse()) {
      cleanup();
    }
  };
}

function wrapPromiseResult(
  api: MutableApi,
  name: string,
  cleanups: Array<() => void>,
  transform: (args: any[], result: any) => any,
): void {
  const original = api[name];
  if (typeof original !== 'function') {
    console.warn(`SATLI could not find SteamClient.Apps.${name}`);
    return;
  }
  const wrapped = async (...args: any[]) => transform(args, await original.apply(api, args));
  try {
    api[name] = wrapped;
    cleanups.push(() => {
      if (api[name] === wrapped) api[name] = original;
    });
  } catch (error) {
    console.warn(`SATLI could not override SteamClient.Apps.${name}`, error);
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
