using System.Diagnostics;
using System.Net.Http;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Editor.E2E.Tests;

[SetUpFixture]
public sealed class AppHostFixture
{
    private static Process? _process;
    public static string BaseUrl { get; } = "http://127.0.0.1:5087";

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(repoRoot, "src", "AppHost", "AppHost.csproj");

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" --urls {BaseUrl}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

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
    public async Task Documents_Catalog_Creates_And_Opens_A_Document()
    {
        await Page.GotoAsync(
            $"{AppHostFixture.BaseUrl}/documents",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Documents, snapshots, and reusable building blocks." })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create New Document" }).ClickAsync();

        await Expect(Page.GetByText("Technical Studio v1.5")).ToBeVisibleAsync();
        await Expect(Page.Locator(".diagram-node").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".status-strip")).ToContainTextAsync("Flowchart");
    }

    [Test]
    public async Task Editor_Adds_Node_Undo_Redo_And_Persists()
    {
        await Page.GotoAsync(
            $"{AppHostFixture.BaseUrl}/editor",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Expect(Page.GetByText("Technical Studio v1.5")).ToBeVisibleAsync();

        var nodes = Page.Locator(".diagram-node");
        await Expect(nodes.First).ToBeVisibleAsync();
        var initialCount = await nodes.CountAsync();

        await Page.Locator(".stencil-card").First.ClickAsync();
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

        await Expect(Page.GetByText("Technical Studio v1.5")).ToBeVisibleAsync();

        await Page.Locator(".diagram-node").First.ClickAsync();
        await Expect(Page.Locator(".inspector-panel")).ToContainTextAsync("Node selected");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Review" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add Comment" })).ToBeVisibleAsync();

        var reviewSection = Page.Locator(".inspector-section").Filter(new()
        {
            Has = Page.GetByRole(AriaRole.Heading, new() { Name = "Review" })
        });
        await reviewSection.Locator("input").First.FillAsync("Architecture Review");
        await reviewSection.Locator("textarea").First.FillAsync("Check the shared workflow path.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Comment" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Resolve" })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Snapshot" }).ClickAsync();
        await Expect(Page.Locator(".snapshot-row").First).ToContainTextAsync("Checkpoint");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Library" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save to Library" }).ClickAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Documents" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Recent Documents" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".documents-panel").Nth(1)).ToContainTextAsync("Reusable Snippet");
    }
}
