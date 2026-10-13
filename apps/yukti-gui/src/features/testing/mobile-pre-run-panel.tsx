import { Card, Input } from "@/components/ui/primitives";

// MobileModule.Setup (src/Yukti.Infrastructure.InMemory/Modules/MobileModule.cs)
// only opens an Appium session if ctx.Variables.mobile is populated — that
// dictionary comes from TriggerFlowRunCommand.VariableOverrides, not step
// params. Without this panel there was no way to supply it, so every
// Mobile test failed with "not set up" regardless of what the user filled
// into the action form above.
export interface MobileDeviceConfig {
  platformName: string;
  deviceName: string;
  automationName: string;
  appiumUrl: string;
  app: string;
}

export const emptyMobileDeviceConfig: MobileDeviceConfig = {
  platformName: "",
  deviceName: "",
  automationName: "",
  appiumUrl: "",
  app: "",
};

export function buildMobileVariableOverrides(config: MobileDeviceConfig): Record<string, unknown> | undefined {
  const mobile: Record<string, unknown> = {};
  if (config.platformName.trim()) mobile.platformName = config.platformName.trim();
  if (config.deviceName.trim()) mobile.deviceName = config.deviceName.trim();
  if (config.automationName.trim()) mobile.automationName = config.automationName.trim();
  if (config.appiumUrl.trim()) mobile.appiumUrl = config.appiumUrl.trim();
  if (config.app.trim()) mobile.app = config.app.trim();
  return Object.keys(mobile).length > 0 ? { mobile } : undefined;
}

export function MobilePreRunPanel({
  config,
  onChange,
}: {
  config: MobileDeviceConfig;
  onChange: (config: MobileDeviceConfig) => void;
}) {
  return (
    <Card className="flex flex-col gap-3 p-4">
      <h2 className="text-body-sm font-medium text-ink">Device config</h2>
      <p className="text-caption text-ink-dim">
        Required for the module to open an Appium session — without this, every action fails with "not set up".
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">Platform name*</label>
          <Input
            placeholder="Android or iOS"
            value={config.platformName}
            onChange={(e) => onChange({ ...config, platformName: e.target.value })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">Device name*</label>
          <Input value={config.deviceName} onChange={(e) => onChange({ ...config, deviceName: e.target.value })} />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">Automation name*</label>
          <Input
            placeholder="UiAutomator2 or XCUITest"
            value={config.automationName}
            onChange={(e) => onChange({ ...config, automationName: e.target.value })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">Appium URL</label>
          <Input
            placeholder="http://localhost:4723"
            value={config.appiumUrl}
            onChange={(e) => onChange({ ...config, appiumUrl: e.target.value })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">App</label>
          <Input value={config.app} onChange={(e) => onChange({ ...config, app: e.target.value })} />
        </div>
      </div>
    </Card>
  );
}
