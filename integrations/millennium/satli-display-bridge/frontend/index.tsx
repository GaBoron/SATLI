import { definePlugin, Field } from 'millennium';
import { useEffect, useState } from 'react';
import { DisplayOverrideController, OverrideMetrics } from '../shared/display_override';
import { installSteamApiOverrides } from './steam_api_override';

let controller: DisplayOverrideController | undefined;
let removeSteamApiOverrides: (() => void) | undefined;
let latestMetrics: OverrideMetrics = { appCount: 0, sourceCount: 0, replacedCount: 0 };
let currentSteamLanguage = 'english';

/** @ffi */
export function getCurrentSteamLanguage(): string {
  return currentSteamLanguage;
}

const SettingsContent = () => {
  const [metrics, setMetrics] = useState(latestMetrics);
  useEffect(() => {
    const timer = window.setInterval(() => setMetrics({ ...latestMetrics }), 1000);
    return () => window.clearInterval(timer);
  }, []);
  return (
    <Field
      label={`已锁定 ${metrics.appCount} 个游戏 · ${metrics.sourceCount} 条无歧义源文本`}
      description={`当前窗口已替换 ${metrics.replacedCount} 处。SATLI 无需后台运行。`}
    />
  );
};

const Icon = () => <span style={{ fontWeight: 700, marginRight: '5px' }}>译</span>;

export default definePlugin(async () => {
  currentSteamLanguage = await SteamClient.Settings.GetCurrentLanguage();
  controller = new DisplayOverrideController(
    document,
    () => backend.getBridgeSnapshot(),
    async () => getCurrentSteamLanguage(),
    (metrics) => {
      latestMetrics = metrics;
    },
  );
  await controller.start();
  removeSteamApiOverrides = installSteamApiOverrides(controller);
  console.log('SATLI achievement display override attached to Steam main UI');
  return {
    title: 'SATLI 成就显示覆盖',
    icon: <Icon />,
    content: <SettingsContent />,
    onDismount: () => {
      removeSteamApiOverrides?.();
      controller?.stop();
    },
  };
});
