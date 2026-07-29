using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Editor.E2E.Tests;

[SetUpFixture]
public sealed class AppHostFixture
{
    private static Process? _process;
    public static string BaseUrl { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(repoRoot, "src", "AppHost", "AppHost.csproj");
        BaseUrl = $"http://127.0.0.1:{AllocateLoopbackPort()}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --configuration {GetTestBuildConfiguration()} --no-launch-profile --project \"{projectPath}\" -- --urls {BaseUrl}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        startInfo.Environment["Logging__EventLog__LogLevel__Default"] = "None";

        _process = Process.Start(startInfo);

        if (_process is null)
        {
            throw new InvalidOperationException("Unable to start AppHost.");
        }

        _ = _process.StandardOutput.ReadToEndAsync();
        _ = _process.StandardError.ReadToEndAsync();

        using var client = new HttpClient();
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                var response = await client.GetAsync($"{BaseUrl}/healthz");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(750);
        }

        throw new TimeoutException("AppHost did not become healthy in time.");
    }

    private static int AllocateLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GetTestBuildConfiguration()
    {
        var configuration = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Name;
        return string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
    }

    [OneTimeTearDown]
    public void StopServer()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }

        _process?.Dispose();
        _process = null;
    }
}

[NonParallelizable]
public sealed class EditorSmokeTests : PageTest
{
    [Test]
    public async Task CanvasOverlays_Navigate_And_Preserve_Compact_Status()
    {
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "true");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeHiddenAsync();
        await Expect(Page.Locator(".minimap-card")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit Diagram" }).ClickAsync();

        await Page.Locator(".diagram-node").First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Center Selection" }).ClickAsync();
        await Expect(Page.GetByTestId("editor-status-bar")).ToContainTextAsync("Flowchart");
        Assert.That((await Page.GetByTestId("editor-status-bar").BoundingBoxAsync())?.Height, Is.EqualTo(24).Within(1));
        Assert.That(await Page.Locator(".canvas-card").EvaluateAsync<string>("element => getComputedStyle(element).display"), Is.EqualTo("block"));
    }

    [Test]
    public async Task CanvasOverlays_Preserve_Unavailable_Command_Semantics()
    {
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Center Selection" })).ToBeDisabledAsync();
        var session = Page.Locator("[data-command-group='session']");
        await session.Locator("summary").ClickAsync();
        await Expect(session.GetByRole(AriaRole.Button, new() { Name = "Push" })).ToBeDisabledAsync();
        await Expect(session.GetByRole(AriaRole.Button, new() { Name = "Pull" })).ToBeDisabledAsync();
    }

    [Test]
    public async Task CompactCommandBar_Maps_Each_Legacy_Workflow_Once()
    {
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var commandBar = Page.GetByTestId("editor-command-bar");
        var expectedIds = new[]
        {
            "document-title", "rename", "favorite", "save", "snapshot", "snapshot-name", "undo", "redo",
            "sync-toggle", "push", "pull", "toggle-review-mode", "presentation", "export-json", "export-svg",
            "export-png", "import-json", "layout-selector", "run-layout", "add-group", "fit-diagram", "center-selection"
        };

        foreach (var commandId in expectedIds)
        {
            await Expect(Page.Locator($"[data-command-id='{commandId}']")).ToHaveCountAsync(1);
        }

        var inventory = new Dictionary<string, string[]>
        {
            ["history"] = ["Snapshot", "Undo", "Redo"],
            ["session"] = ["Push", "Pull", "Toggle Review Mode", "Presentation"],
            ["file"] = ["Rename", "Favorite", "Export JSON", "SVG", "PNG", "Import JSON"],
            ["arrange"] = ["Run Layout", "Add Group"]
        };

        foreach (var (group, commands) in inventory)
        {
            var details = commandBar.Locator($"[data-command-group='{group}']");
            await details.Locator("summary").ClickAsync();
            foreach (var command in commands)
            {
                await Expect(details.GetByText(command, new() { Exact = true })).ToBeVisibleAsync();
            }

            Assert.That(await commandBar.Locator("details[open]").CountAsync(), Is.LessThanOrEqualTo(1));
        }

        var history = commandBar.Locator("[data-command-group='history']");
        await history.Locator("summary").ClickAsync();
        var snapshot = commandBar.Locator("[data-command-id='snapshot']");
        var commandBarBounds = await commandBar.BoundingBoxAsync();
        var snapshotBounds = await snapshot.BoundingBoxAsync();
        Assert.That(snapshotBounds?.Y, Is.GreaterThan(commandBarBounds!.Y + commandBarBounds.Height));
        await snapshot.ClickAsync();
        await Page.Locator("[data-drawer-trigger='snapshots']").ClickAsync();
        await Expect(Page.Locator(".snapshot-row").First).ToBeVisibleAsync();

        await commandBar.Locator("[data-command-group='file'] summary").ClickAsync();
        await Page.Locator("[data-command-id='favorite']").ClickAsync();
        await Expect(Page.Locator("[data-command-id='favorite']")).ToHaveTextAsync("Unfavorite");
        var session = commandBar.Locator("[data-command-group='session']");
        await session.Locator("summary").ClickAsync();
        await session.Locator("[data-command-id='sync-toggle']").CheckAsync();
        await Expect(session.Locator("[data-command-id='push']")).ToBeEnabledAsync();
        await Expect(session.Locator("[data-command-id='pull']")).ToBeEnabledAsync();
        await session.Locator("[data-command-id='presentation']").ClickAsync();
        await Expect(Page.GetByTestId("editor-shell")).ToHaveClassAsync(new Regex("is-presentation"));
    }

    [Test]
    public async Task CanvasInitializationFailure_Leaves_Ready_Marker_False()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                const replaceChildren = Element.prototype.replaceChildren;
                window.__feature03JsPlumbFailureCount = 0;
                Element.prototype.replaceChildren = function (...args) {
                    if (this.classList?.contains("editor-canvas-host")) {
                        window.__feature03JsPlumbFailureCount++;
                        throw new Error("FEATURE-03 simulated jsPlumb initialization failure");
                    }

                    return replaceChildren.apply(this, args);
                };
            })();
            """);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.Locator(".editor-canvas-host")).ToBeAttachedAsync();
        await Page.WaitForFunctionAsync("window.__feature03JsPlumbFailureCount > 0");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Canvas initialization failed");
        Assert.That(await Page.EvaluateAsync<int>("window.__feature03JsPlumbFailureCount"), Is.GreaterThan(0));
        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "false");
        await Page.WaitForTimeoutAsync(250);
        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "false");
    }

    [Test]
    public async Task ContextualDrawers_Open_Assets_Insert_And_Dismiss()
    {
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var commandBarBounds = await Page.GetByTestId("editor-command-bar").BoundingBoxAsync();
        var brandBounds = await Page.Locator(".editor-brand").BoundingBoxAsync();
        Assert.That(brandBounds!.Y + brandBounds.Height, Is.LessThanOrEqualTo(commandBarBounds!.Y + commandBarBounds.Height + 1));
        var nodes = Page.Locator(".diagram-node");
        await Expect(nodes.First).ToBeVisibleAsync();
        var initialCount = await nodes.CountAsync();

        var assetsTrigger = Page.GetByRole(AriaRole.Button, new() { Name = "Assets" });
        await Expect(assetsTrigger).ToBeVisibleAsync();
        await assetsTrigger.ClickAsync();
        await Expect(Page.Locator(".drawer-host--assets")).ToBeVisibleAsync();
        await Page.Locator(".drawer-host--assets .stencil-card").First.ClickAsync();
        await Expect(nodes).ToHaveCountAsync(initialCount + 1);
        await Page.Locator(".drawer-host--assets").GetByRole(AriaRole.Button, new() { Name = "Library" }).ClickAsync();
        await Expect(Page.Locator(".drawer-host--assets .panel-heading")).ToContainTextAsync("Library");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(".drawer-host--assets")).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-drawer-trigger='assets']")).ToBeFocusedAsync();
    }

    [Test]
    public async Task ContextualDrawers_Switch_Modes_Without_Replacing_Canvas()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var canvas = Page.Locator(".editor-canvas-host");
        var identity = await canvas.EvaluateAsync<string>("element => { element.dataset.drawerIdentity ??= 'stable'; return element.dataset.drawerIdentity; }");

        foreach (var mode in new[] { "Inspector", "Layers", "Snapshots", "Validation" })
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = mode }).ClickAsync();
            await Expect(Page.Locator(".inspector-panel")).ToBeVisibleAsync();
        }

        await Page.Locator("[data-drawer-trigger='review']").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add Comment" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Pin Annotation" })).ToBeVisibleAsync();

        await Page.SetViewportSizeAsync(1024, 768);
        Assert.That(await canvas.EvaluateAsync<string>("element => element.dataset.drawerIdentity"), Is.EqualTo(identity));
        Assert.That((await Page.GetByTestId("editor-viewport").GetAttributeAsync("data-active-drawer")), Is.EqualTo("review"));
    }

    [Test]
    public async Task AdaptiveShell_Uses_Target_Geometry_At_Desktop_Viewport()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var commandBar = await Page.GetByTestId("editor-command-bar").BoundingBoxAsync();
        var activityRail = await Page.GetByTestId("editor-activity-rail").BoundingBoxAsync();
        var statusBar = await Page.GetByTestId("editor-status-bar").BoundingBoxAsync();
        var canvasHost = await Page.Locator(".editor-canvas-host").BoundingBoxAsync();

        Assert.Multiple(() =>
        {
            Assert.That(commandBar?.Height, Is.EqualTo(52).Within(1));
            Assert.That(activityRail?.Width, Is.EqualTo(72).Within(1));
            Assert.That(statusBar?.Height, Is.EqualTo(24).Within(1));
            Assert.That(canvasHost!.Width * canvasHost.Height, Is.GreaterThanOrEqualTo(1440 * 900 * .8));
        });
    }

    [Test]
    public async Task AdaptiveShell_Remains_Stable_Across_Compact_Breakpoint()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var canvas = Page.Locator(".editor-canvas-host");
        var identity = await canvas.EvaluateAsync<string>("element => { element.dataset.feature01Identity ??= 'stable'; return element.dataset.feature01Identity; }");
        await Page.SetViewportSizeAsync(1024, 768);
        await Expect(canvas).ToBeVisibleAsync();
        Assert.That(await canvas.EvaluateAsync<string>("element => element.dataset.feature01Identity"), Is.EqualTo(identity));
        Assert.That((await Page.GetByTestId("editor-viewport").EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns")).Split(' ').Length, Is.EqualTo(2));
    }

    [Test]
    public async Task AdaptiveShell_Keeps_Initial_Rail_And_Command_Bar_Controls_Visible()
    {
        await Page.SetViewportSizeAsync(1024, 768);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var rail = Page.GetByTestId("editor-activity-rail");
        Assert.That((await rail.BoundingBoxAsync())?.Width, Is.EqualTo(72).Within(1));
        foreach (var trigger in await rail.Locator("[data-drawer-trigger]").AllAsync())
        {
            var isClipped = await trigger.EvaluateAsync<bool>(
                "element => element.scrollWidth > element.clientWidth || element.scrollHeight > element.clientHeight");
            Assert.That(isClipped, Is.False);
        }

        var viewportWidth = await Page.EvaluateAsync<int>("window.innerWidth");
        var commandBar = Page.GetByTestId("editor-command-bar");
        foreach (var control in await commandBar.Locator(":scope > .editor-toolbar > button, :scope > .editor-toolbar > details > summary").AllAsync())
        {
            await Expect(control).ToBeVisibleAsync();
            var bounds = await control.BoundingBoxAsync();
            Assert.That(bounds!.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.X + bounds.Width, Is.LessThanOrEqualTo(viewportWidth));
        }
    }

    [Test]
    public async Task CanvasViewport_Synchronization_Does_Not_Rebuild_The_Diagram()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "true");

        var canvasHost = Page.Locator(".editor-canvas-host");
        var initialRenderCount = await canvasHost.GetAttributeAsync("data-render-count");
        var viewport = Page.Locator(".diagram-viewport");
        await viewport.EvaluateAsync("element => { element.scrollLeft = 480; element.scrollTop = 360; }");
        await Page.WaitForTimeoutAsync(300);

        Assert.That(await canvasHost.GetAttributeAsync("data-render-count"), Is.EqualTo(initialRenderCount));
        await Page.Locator("[data-drawer-trigger='assets']").ClickAsync();
        await Expect(Page.Locator(".drawer-host--assets")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Moving_A_Node_Does_Not_Rebuild_The_Canvas_And_Leaves_The_Shell_Responsive()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "true");

        var canvasHost = Page.Locator(".editor-canvas-host");
        var node = Page.Locator(".diagram-node").First;
        var dragHandle = node.Locator(".diagram-node__title");
        await node.ScrollIntoViewIfNeededAsync();
        var initialBounds = await node.BoundingBoxAsync();
        var dragHandleBounds = await dragHandle.BoundingBoxAsync();
        var initialRenderCount = int.Parse((await canvasHost.GetAttributeAsync("data-render-count"))!);

        await Page.Mouse.MoveAsync(
            dragHandleBounds!.X + dragHandleBounds.Width / 2,
            dragHandleBounds.Y + dragHandleBounds.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.WaitForTimeoutAsync(100);
        await Page.Mouse.MoveAsync(
            dragHandleBounds.X + dragHandleBounds.Width / 2 + 96,
            dragHandleBounds.Y + dragHandleBounds.Height / 2 + 48,
            new MouseMoveOptions { Steps = 16 });
        await Page.Mouse.UpAsync();

        await Page.WaitForTimeoutAsync(300);
        var movedBounds = await node.BoundingBoxAsync();
        Assert.That(movedBounds!.X, Is.GreaterThan(initialBounds!.X + 20));
        await Expect(canvasHost).ToHaveAttributeAsync("data-render-count", initialRenderCount.ToString());
        var settledRenderCount = int.Parse((await canvasHost.GetAttributeAsync("data-render-count"))!);
        await Page.WaitForTimeoutAsync(750);

        Assert.That(settledRenderCount, Is.EqualTo(initialRenderCount));
        Assert.That(
            int.Parse((await canvasHost.GetAttributeAsync("data-render-count"))!),
            Is.EqualTo(settledRenderCount));

        await Page.Locator("[data-drawer-trigger='assets']").ClickAsync();
        await Expect(Page.Locator(".drawer-host--assets")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdaptiveShell_Remains_Usable_At_200Percent_Zoom_And_Reduced_Motion()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{AppHostFixture.BaseUrl}/editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.EvaluateAsync("document.documentElement.style.zoom = '2'");

        var canvas = Page.Locator(".editor-canvas-host");
        await Expect(canvas).ToBeVisibleAsync();
        Assert.That((await canvas.BoundingBoxAsync())?.Width, Is.GreaterThan(100));

        await Page.Locator("[data-drawer-trigger='inspector']").ClickAsync();
        var inspector = Page.Locator(".inspector-panel");
        await Expect(inspector).ToBeVisibleAsync();
        Assert.That(await inspector.EvaluateAsync<string>("element => getComputedStyle(element).transitionDuration"), Is.EqualTo("0s"));

        await Page.Keyboard.PressAsync("Escape");
        await Expect(inspector).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-drawer-trigger='inspector']")).ToBeFocusedAsync();
    }

    [Test]
    public async Task Documents_Catalog_Creates_And_Opens_A_Document()
    {
        await Page.GotoAsync(
            $"{AppHostFixture.BaseUrl}/documents",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Documents, snapshots, and reusable building blocks." })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create New Document" }).ClickAsync();

        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "true");
        await Expect(Page.Locator(".diagram-node").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".status-strip")).ToContainTextAsync("Flowchart");
    }

    [Test]
    public async Task Editor_Adds_Node_Undo_Redo_And_Persists()
    {
        await Page.GotoAsync(
            $"{AppHostFixture.BaseUrl}/editor",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "true");

        var nodes = Page.Locator(".diagram-node");
        await Expect(nodes.First).ToBeVisibleAsync();
        var initialCount = await nodes.CountAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Assets" }).ClickAsync();
        await Page.Locator(".drawer-host--assets .stencil-card").First.ClickAsync();
        await Expect(nodes).ToHaveCountAsync(initialCount + 1);

        await Page.Locator(".diagram-runtime-host").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+Z");
        await Expect(nodes).ToHaveCountAsync(initialCount);

        await Page.Keyboard.PressAsync("ControlOrMeta+Shift+Z");
        await Expect(nodes).ToHaveCountAsync(initialCount + 1);

        await Page.WaitForTimeoutAsync(800);
        await Page.ReloadAsync();
        await Expect(Page.Locator(".diagram-node")).ToHaveCountAsync(initialCount + 1);
    }

    [Test]
    public async Task Editor_Can_Save_Snapshot_LibraryItem_And_Review_Comment()
    {
        await Page.GotoAsync(
            $"{AppHostFixture.BaseUrl}/editor",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByTestId("editor-shell")).ToHaveAttributeAsync("data-editor-ready", "true");

        await Page.Locator(".diagram-node").First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Inspector" }).ClickAsync();
        await Expect(Page.Locator(".inspector-panel")).ToContainTextAsync("Node selected");

        await Page.Locator("[data-drawer-trigger='review']").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add Comment" })).ToBeVisibleAsync();

        var reviewSection = Page.Locator(".inspector-section").Filter(new()
        {
            Has = Page.GetByRole(AriaRole.Heading, new() { Name = "Review" })
        });
        await reviewSection.Locator("input").First.FillAsync("Architecture Review");
        await reviewSection.Locator("textarea").First.FillAsync("Check the shared workflow path.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Comment" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Resolve" })).ToBeVisibleAsync();

        await Page.Locator("[data-command-group='history'] summary").ClickAsync();
        await Page.Locator("[data-command-group='history']").GetByRole(AriaRole.Button, new() { Name = "Snapshot" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Snapshots" }).ClickAsync();
        await Expect(Page.Locator(".snapshot-row").First).ToContainTextAsync("Checkpoint");

        await Page.Locator("[data-drawer-trigger='library']").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save to Library" }).ClickAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Documents" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Recent Documents" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".documents-panel").Nth(1)).ToContainTextAsync("Reusable Snippet");
    }
}
