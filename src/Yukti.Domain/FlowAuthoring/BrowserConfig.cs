namespace Yukti.Domain.FlowAuthoring;

/// <summary>Web UI Testing Module Spec, WEB-T8: deliberately NOT in Yukti.Domain.UiVocabulary --
/// "Chromium/Firefox/WebKit" is a Web-module-specific concept (a Mobile module has no browser
/// engine at all), so it would violate WEB-T1's own "no browser-specific concept in this layer"
/// rule. Lives alongside Flow itself instead, same place DefaultTimeoutMs/ContinueOnFailure do.</summary>
public enum BrowserEngine { Chromium, Firefox, WebKit }

public enum ColorScheme { Light, Dark, NoPreference }

/// <summary>
/// Web UI Testing Module Spec, WEB-T8: a Flow's own browser/context configuration -- which
/// engine, viewport, device emulation preset, user agent, locale, timezone, colour scheme. One
/// bundled value object rather than seven scalar properties directly on Flow, matching the
/// spec's own grouping ("Browser engine per flow... Viewport size, device emulation presets,
/// user agent, locale, timezone, colour scheme").
/// </summary>
/// <param name="StorageStateName">"A flow can save its logged-in state and later flows can
/// start from it, skipping login" -- the name a prior flow's `saveStorageState` step saved
/// under (a tenant-scoped file, Yukti.Infrastructure.InMemory.Modules.BrowserStorageStateStorage,
/// not a database row -- see that class's own doc comment for why). Null means "start with no
/// storage state," every flow's behavior before this task.</param>
public sealed record BrowserConfig(
    BrowserEngine Engine = BrowserEngine.Chromium,
    int? ViewportWidth = null,
    int? ViewportHeight = null,
    string? DeviceEmulationPreset = null,
    string? UserAgent = null,
    string? Locale = null,
    string? TimeZoneId = null,
    ColorScheme? ColorScheme = null,
    string? StorageStateName = null);
