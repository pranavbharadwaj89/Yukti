namespace Yukti.Domain.UiVocabulary;

/// <summary>
/// Web UI Testing Module Spec, WEB-T1: every way a <see cref="Locator"/> can name an element,
/// web and mobile strategies together in one enum so it never changes shape again when the
/// Mobile module (Appium) arrives — adding a case here later would be a breaking change to
/// every already-published <see cref="Locator"/>.
/// </summary>
public enum LocatorStrategy
{
    // Web (Playwright)
    TestId,
    Role,
    Label,
    Text,
    Placeholder,
    Css,
    XPath,

    // Mobile (Appium) — declared now so the enum never changes in the Mobile phase
    AccessibilityId,
    ResourceId,
    IosPredicate,
    IosClassChain,
    ClassName,
}
