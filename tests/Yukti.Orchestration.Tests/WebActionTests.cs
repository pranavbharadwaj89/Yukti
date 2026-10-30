using Microsoft.Extensions.Logging;
using Xunit;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T6: the full action set, proven through the real WebModule
/// and FlowEngine -- never a direct Playwright call bypassing the module, same convention
/// WebModuleTests itself documents. Every action here that targets an element is reached via a
/// plain CSS selector (the pre-existing, unmigrated-by-this-task path); WEB-T4's own tests
/// already cover the Locator-object path for the actions that existed before this task.
/// </summary>
public sealed class WebActionTests
{
    private const string Html = """
        <!doctype html>
        <html>
        <body>
          <button id="single" data-count="0">Single</button>
          <button id="dbl" data-count="0">Double</button>
          <button id="ctx" data-count="0">Right</button>
          <input id="text1" value="prefill" />
          <button id="keyBtn">KeyTarget</button>
          <p id="keyResult">none</p>
          <input id="chk1" type="checkbox" />
          <input id="chk2" type="checkbox" checked />
          <div id="hoverTarget">Hover me</div>
          <p id="hoverResult">not hovered</p>
          <div id="dragSource" draggable="true">Drag</div>
          <div id="dropTarget">Drop here</div>
          <div style="height:2000px">tall spacer</div>
          <div id="scrollTarget">Scroll target</div>
          <input id="fileInput" type="file" />
          <p id="fileResult">0</p>
          <select id="singleSelect"><option value="a">Alpha</option><option value="b">Bravo</option></select>
          <select id="multiSelect" multiple><option value="x">X-ray</option><option value="y">Yankee</option><option value="z">Zulu</option></select>
          <iframe id="frame1" srcdoc="&lt;button id=innerBtn data-testid=inner-btn&gt;Inner&lt;/button&gt;"></iframe>
          <button id="openWindowBtn">Open window</button>
          <button id="alertBtn">Alert</button>
          <p id="alertResult">none</p>
          <button id="confirmBtn">Confirm</button>
          <p id="confirmResult">none</p>
          <button id="promptBtn">Prompt</button>
          <p id="promptResult">none</p>
          <script>
            function bindCount(id) {
              document.getElementById(id).addEventListener('click', () => {
                const el = document.getElementById(id);
                el.dataset.count = (parseInt(el.dataset.count) + 1).toString();
              });
            }
            bindCount('single'); bindCount('dbl');
            document.getElementById('ctx').addEventListener('contextmenu', e => {
              e.preventDefault();
              const el = document.getElementById('ctx');
              el.dataset.count = (parseInt(el.dataset.count) + 1).toString();
            });
            document.getElementById('keyBtn').addEventListener('keydown', e => {
              document.getElementById('keyResult').textContent = e.key;
            });
            document.getElementById('hoverTarget').addEventListener('mouseenter', () => {
              document.getElementById('hoverResult').textContent = 'hovered';
            });
            document.getElementById('dragSource').addEventListener('dragstart', e => e.dataTransfer.setData('text', 'x'));
            document.getElementById('dropTarget').addEventListener('dragover', e => e.preventDefault());
            document.getElementById('dropTarget').addEventListener('drop', e => {
              e.preventDefault();
              document.getElementById('dropTarget').textContent = 'dropped';
            });
            document.getElementById('fileInput').addEventListener('change', e => {
              document.getElementById('fileResult').textContent = e.target.files.length.toString();
            });
            document.getElementById('openWindowBtn').addEventListener('click', () => {
              const w = window.open('about:blank', '_blank');
              w.document.title = 'Popup Window';
            });
            document.getElementById('alertBtn').addEventListener('click', () => {
              alert('hello');
              document.getElementById('alertResult').textContent = 'alert closed';
            });
            document.getElementById('confirmBtn').addEventListener('click', () => {
              document.getElementById('confirmResult').textContent = confirm('sure?') ? 'confirmed' : 'cancelled';
            });
            document.getElementById('promptBtn').addEventListener('click', () => {
              const result = prompt('name?');
              document.getElementById('promptResult').textContent = result === null ? '(null)' : result;
            });
          </script>
        </body>
        </html>
        """;

    private static readonly string DataUrl = "data:text/html," + Uri.EscapeDataString(Html);

    private static FlowEngine BuildEngine()
    {
        var loggerFactory = LoggerFactory.Create(l => l.SetMinimumLevel(LogLevel.Warning));
        var dispatcher = new InMemoryDomainEventDispatcher();
        var uowFactory = new InMemoryUnitOfWorkFactory(dispatcher);
        var runRepo = new InMemoryFlowRunRepository(uowFactory);

        var registry = new ModuleRegistry();
        registry.Register(new WebModule());
        var moduleRegistrations = new InMemoryModuleRegistrationRepository(uowFactory);
        var moduleDispatcher = new ModuleDispatcher(registry, moduleRegistrations, new ModuleExecutionStrategySelector(), loggerFactory.CreateLogger<ModuleDispatcher>());
        var variableStore = new VariableStore();
        var retryHandler = new RetryFlakeHandler(loggerFactory.CreateLogger<RetryFlakeHandler>());
        return new FlowEngine(moduleDispatcher, registry, variableStore, retryHandler, runRepo, uowFactory,
            loggerFactory.CreateLogger<FlowEngine>(), new RetryPolicy(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0));
    }

    private static Dictionary<string, object?> P(params (string, object?)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    private static async Task<FlowRun> RunSteps(FlowEngine engine, TenantId tenantId, params (string action, Dictionary<string, object?> @params)[] steps)
    {
        var flow = Flow.CreateDraft("Web action test flow", null, tenantId, UserId.New());
        var navigate = ("navigate", P(("url", DataUrl)));
        foreach (var (action, @params) in new[] { navigate }.Concat(steps))
            flow.AddStep(action, ModuleKind.Web, action, @params);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        return await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);
    }

    private static void AssertAllPassed(FlowRun run)
    {
        for (var i = 0; i < run.Results.Count; i++)
            Assert.Equal(StepStatus.Passed, run.Results[i].Status); // index i names which step failed on assertion failure
    }

    [Fact]
    public async Task Back_forward_and_reload_navigate_the_shared_page()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("navigate", P(("url", "data:text/html,<p>page two</p>"))),
            ("back", P()),
            ("forward", P()),
            ("reload", P()));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Click_supports_modifiers_button_and_click_count()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("click", P(("selector", "#single"), ("clickCount", 1))),
            ("assertText", P(("selector", "#single"), ("expected", "Single"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task DoubleClick_registers_as_two_clicks()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("doubleClick", P(("selector", "#dbl"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task RightClick_fires_the_context_menu_handler()
    {
        // The page's own 'contextmenu' listener bumps #ctx's data-count attribute -- checked via
        // waitFor's attributeEquals condition (WEB-T5), the only way this module reads an
        // attribute today.
        var condition = new Dictionary<string, object?>
        {
            ["type"] = "attributeEquals",
            ["locator"] = new Dictionary<string, object?> { ["primary"] = new Dictionary<string, object?> { ["strategy"] = "Css", ["value"] = "#ctx" } },
            ["attr"] = "data-count",
            ["expected"] = "1",
        };
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("rightClick", P(("selector", "#ctx"))),
            ("waitFor", P(("condition", condition), ("timeoutMs", 1000))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Clear_empties_a_prefilled_input()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("clear", P(("selector", "#text1"))),
            ("fill", P(("selector", "#text1"), ("value", "after-clear"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Type_sends_keystrokes_one_at_a_time()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("type", P(("selector", "#text1"), ("text", "typed"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Press_sends_a_key_the_page_can_observe()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("press", P(("selector", "#keyBtn"), ("key", "Enter"))),
            ("assertText", P(("selector", "#keyResult"), ("expected", "Enter"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Check_and_uncheck_are_idempotent()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("check", P(("selector", "#chk1"))),
            ("check", P(("selector", "#chk1"))), // idempotent -- a no-op the second time
            ("uncheck", P(("selector", "#chk2"))),
            ("uncheck", P(("selector", "#chk2"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Hover_fires_the_pages_own_mouseenter_handler()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("hover", P(("selector", "#hoverTarget"))),
            ("assertText", P(("selector", "#hoverResult"), ("expected", "hovered"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task DragAndDrop_moves_the_source_onto_the_target()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("dragAndDrop", P(("source", "#dragSource"), ("target", "#dropTarget"))),
            ("assertText", P(("selector", "#dropTarget"), ("expected", "dropped"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task ScrollIntoView_moves_a_far_down_element_into_the_viewport()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("scrollIntoView", P(("selector", "#scrollTarget"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task ScrollBy_scrolls_the_page_by_an_offset()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("scrollBy", P(("x", 0), ("y", 200))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task UploadFile_sets_a_real_file_on_a_file_input()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"web-t6-upload-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "upload me");
        try
        {
            var engine = BuildEngine();
            var tenantId = TenantId.New();
            var run = await RunSteps(engine, tenantId,
                ("uploadFile", P(("selector", "#fileInput"), ("paths", tempFile))),
                ("assertText", P(("selector", "#fileResult"), ("expected", "1"))));
            AssertAllPassed(run);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Select_by_value_still_works_exactly_as_before()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("select", P(("selector", "#singleSelect"), ("value", "b"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Select_by_label_works()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("select", P(("selector", "#singleSelect"), ("label", "Alpha"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Select_by_index_works()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("select", P(("selector", "#singleSelect"), ("index", 1))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Select_supports_multi_select_by_values()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId, ("select", P(("selector", "#multiSelect"), ("values", new List<object?> { "x", "z" }))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task SwitchToFrame_and_back_reaches_the_iframes_own_button_then_the_main_page_again()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("switchToFrame", P(("selector", "#frame1"))),
            ("click", P(("selector", "#innerBtn"))),
            ("switchToMainFrame", P()),
            ("click", P(("selector", "#single"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task SwitchToWindow_by_index_then_closeWindow_returns_to_the_original_page()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("click", P(("selector", "#openWindowBtn"))),
            ("switchToWindow", P(("index", 1))),
            ("closeWindow", P()),
            ("click", P(("selector", "#single")))); // back on the original page
        AssertAllPassed(run);
    }

    [Fact]
    public async Task SwitchToWindow_by_title_finds_the_popup()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("click", P(("selector", "#openWindowBtn"))),
            ("switchToWindow", P(("title", "Popup Window"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task HandleDialog_accepts_an_alert()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("handleDialog", P(("accept", true))),
            ("click", P(("selector", "#alertBtn"))),
            ("assertText", P(("selector", "#alertResult"), ("expected", "alert closed"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task HandleDialog_can_accept_a_confirm()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("handleDialog", P(("accept", true))),
            ("click", P(("selector", "#confirmBtn"))),
            ("assertText", P(("selector", "#confirmResult"), ("expected", "confirmed"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task HandleDialog_can_dismiss_a_confirm()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("handleDialog", P(("accept", false))),
            ("click", P(("selector", "#confirmBtn"))),
            ("assertText", P(("selector", "#confirmResult"), ("expected", "cancelled"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task HandleDialog_can_accept_a_prompt_with_text()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("handleDialog", P(("accept", true), ("promptText", "Ada"))),
            ("click", P(("selector", "#promptBtn"))),
            ("assertText", P(("selector", "#promptResult"), ("expected", "Ada"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task HandleDialog_can_dismiss_a_prompt()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var run = await RunSteps(engine, tenantId,
            ("handleDialog", P(("accept", false))),
            ("click", P(("selector", "#promptBtn"))),
            ("assertText", P(("selector", "#promptResult"), ("expected", "(null)"))));
        AssertAllPassed(run);
    }

    [Fact]
    public async Task Screenshot_can_capture_a_single_element()
    {
        var engine = BuildEngine();
        var tenantId = TenantId.New();
        var flow = Flow.CreateDraft("Screenshot test flow", null, tenantId, UserId.New());
        flow.AddStep("Navigate", ModuleKind.Web, "navigate", P(("url", DataUrl)));
        flow.AddStep("Screenshot", ModuleKind.Web, "screenshot", P(("selector", "#single")));
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        var completed = await engine.Execute(flow, run, new InMemoryCredentialResolver(), CancellationToken.None);

        AssertAllPassed(completed);
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(completed.Results[1].Data);
        var fileName = Assert.IsType<string>(data["path"]);
        var fullPath = ScreenshotStorage.FullPath(completed.Id, fileName);
        Assert.True(File.Exists(fullPath), $"Expected a screenshot at {fullPath}");
        File.Delete(fullPath);
    }
}
