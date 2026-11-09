using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T4: implements the shared UiVocabulary.Locator against
/// Appium, the Mobile counterpart to WEB-T3's PlaywrightLocatorResolver. No live Appium server is
/// available in this environment (the same constraint MOB-T1/T2/T3's own tests document), so
/// AppiumLocatorResolver.ResolveAsync's full fallback/Within/Nth orchestration -- which needs a
/// live FindElement/FindElements call -- isn't exercisable end-to-end here either. What IS
/// provable without one: AppiumLocatorResolver.Build, the pure strategy-to-Selenium-By mapping,
/// needs no live driver at all (a By is a client-side value object; the actual element lookup
/// only happens when something later calls FindElement/FindElements against it).
/// </summary>
public sealed class MobileLocatorTests
{
    [Fact]
    public void AccessibilityId_builds_the_real_Appium_By()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.AccessibilityId, "login-button", null), MobilePlatform.Android);
        Assert.Contains("login-button", by.ToString());
    }

    [Fact]
    public void ResourceId_builds_the_same_ById_compatibility_mapping_MOB_T1s_original_ResolveElement_already_relied_on()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.ResourceId, "com.app:id/submit", null), MobilePlatform.Android);
        Assert.Contains("com.app:id/submit", by.ToString());
    }

    [Fact]
    public void IosPredicate_builds_the_real_Appium_By()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.IosPredicate, "label == 'Login'", null), MobilePlatform.Ios);
        Assert.Contains("label == 'Login'", by.ToString());
    }

    [Fact]
    public void IosClassChain_builds_the_real_Appium_By()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.IosClassChain, "**/XCUIElementTypeButton[`label == \"Login\"`]", null), MobilePlatform.Ios);
        Assert.Contains("XCUIElementTypeButton", by.ToString());
    }

    [Fact]
    public void ClassName_builds_a_plain_Selenium_By()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.ClassName, "android.widget.Button", null), MobilePlatform.Android);
        Assert.Contains("android.widget.Button", by.ToString());
    }

    [Fact]
    public void XPath_builds_a_plain_Selenium_By()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.XPath, "//android.widget.Button[@text='Login']", null), MobilePlatform.Android);
        Assert.Contains("//android.widget.Button[@text='Login']", by.ToString());
    }

    [Fact]
    public void Text_on_Android_builds_an_AndroidUIAutomator_text_query()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.Text, "Login", null), MobilePlatform.Android);
        Assert.Contains("UiSelector", by.ToString());
        Assert.Contains("Login", by.ToString());
    }

    [Fact]
    public void Text_on_iOS_builds_an_IosNSPredicate_label_query_not_the_Android_form()
    {
        var by = AppiumLocatorResolver.Build(new LocatorSpec(LocatorStrategy.Text, "Login", null), MobilePlatform.Ios);
        Assert.Contains("Login", by.ToString());
        Assert.DoesNotContain("UiSelector", by.ToString());
    }

    [Theory]
    [InlineData(LocatorStrategy.TestId)]
    [InlineData(LocatorStrategy.Role)]
    [InlineData(LocatorStrategy.Label)]
    [InlineData(LocatorStrategy.Placeholder)]
    [InlineData(LocatorStrategy.Css)]
    public void A_Web_only_strategy_is_rejected_naming_itself_never_silently_misinterpreted(LocatorStrategy webOnlyStrategy)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            AppiumLocatorResolver.Build(new LocatorSpec(webOnlyStrategy, "irrelevant", null), MobilePlatform.Android));

        Assert.Contains(webOnlyStrategy.ToString(), ex.Message);
        Assert.Contains("not valid for the Mobile module", ex.Message);
    }

    /// <summary>MOB-T4's own checklist: "One Object Repository entry can hold both web and
    /// mobile strategies for the same logical element." Proven at the orchestration level,
    /// independent of MobileModule ever actually running (Setup() always fails without a live
    /// Appium server in this environment) -- FlowEngine.ResolveLocatorRef (WEB-T2) is already
    /// engine-neutral: it resolves a RepositoryRef into a real Locator for ANY module, with zero
    /// knowledge of which strategy that Locator's Primary carries. Uses EchoModule
    /// (VariableStoreTests.cs, same project) purely as a param-echoing probe, not MobileModule
    /// itself, since only the resolution mechanism is under test here.</summary>
    [Fact]
    public async Task An_Object_Repository_entry_holding_a_Mobile_strategy_locator_resolves_to_a_real_Locator_for_any_module()
    {
        var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(new EchoModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());

        var repositories = new InMemoryObjectRepositoryRepository(uowFactory);
        var repositoryElements = new InMemoryObjectRepositoryElementQuery(repositories);

        var engine = new FlowEngine(moduleDispatcher, registry, new VariableStore(), new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>()),
            runRepo, uowFactory, loggerFactory.CreateLogger<FlowEngine>(), repositoryElements: repositoryElements);

        var tenantId = TenantId.New();
        var repository = ObjectRepository.Create("Login screen", tenantId);
        var mobileLocator = new Locator(
            new LocatorSpec(LocatorStrategy.AccessibilityId, "login-button", null),
            Array.Empty<LocatorSpec>(), "Login button", null, null);
        repository.SetElement("LoginPage", "LoginButton", mobileLocator);
        await repositories.Save(repository, CancellationToken.None);
        await using (var uow = uowFactory.Create()) await uow.Commit(CancellationToken.None); // flush the staged save (same InMemoryUnitOfWork shape DeviceProfileResolutionTests' own SeedProfile works around)

        var flow = Flow.CreateDraft("Mobile object repository flow", null, tenantId, UserId.New());
        flow.AddStep("Tap login", EchoModule.Kind, "echo", new Dictionary<string, object?>
        {
            ["selector"] = new Dictionary<string, object?> { ["repositoryPage"] = "LoginPage", ["repositoryElement"] = "LoginButton" },
        });
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);

        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        var result = Assert.Single(completed.Results);
        Assert.Equal(StepStatus.Passed, result.Status);
        var echoed = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result.Data);
        var resolvedLocator = Assert.IsType<Locator>(echoed["selector"]);
        Assert.Equal(LocatorStrategy.AccessibilityId, resolvedLocator.Primary.Strategy);
        Assert.Equal("login-button", resolvedLocator.Primary.Value);
    }
}
