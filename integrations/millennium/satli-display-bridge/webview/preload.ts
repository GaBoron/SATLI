import { DisplayOverrideController } from '../shared/display_override';

declare global {
  interface Window {
    __satliDisplayOverride?: DisplayOverrideController;
  }
}

export default async function main() {
  window.__satliDisplayOverride?.stop();
  const controller = new DisplayOverrideController(
    document,
    () => backend.getBridgeSnapshot(),
    () => frontend.getCurrentSteamLanguage(),
    (metrics) => {
      console.log(
        'SATLI display bridge scan',
        window.location.hostname,
        `apps=${metrics.appCount}`,
        `sources=${metrics.sourceCount}`,
        `replaced=${metrics.replacedCount}`,
      );
    },
  );
  window.__satliDisplayOverride = controller;
  await controller.start();
  console.log('SATLI achievement display override attached to', window.location.href);
}
