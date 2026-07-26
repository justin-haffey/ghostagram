using System.Net.Http.Json;
using Diagrams.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AppHost.Tests;

public sealed class AppHostIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AppHostIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Home_Page_Renders_Branding()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Professional diagramming built for technical teams.", html);
        Assert.Contains("Open Workspace", html);
    }

    [Fact]
    public async Task Healthz_Returns_Healthy()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Editor_Route_Returns_App_Shell()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/editor");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("_framework/blazor.web.js", html);
    }

    [Fact]
    public async Task Diagram_Editor_Runtime_Is_Published_As_A_Static_Web_Asset()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/_content/Diagrams.Interop.JsPlumb/js/diagram-editor.js");
        var source = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("export function initialize", source);
        Assert.Contains("export function renderDocument", source);
    }

    [Fact]
    public async Task Sync_Pull_For_Missing_Document_Returns_Unsuccessful_Result()
    {
        using var client = _factory.CreateClient();
        var documentId = $"missing-{Guid.NewGuid():N}";
        var response = await client.GetAsync($"/api/sync/documents/{documentId}");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SyncResult>();

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.False(result.HasConflict);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task Sync_Push_Then_Pull_RoundTrips_Document()
    {
        using var client = _factory.CreateClient();
        var documentId = $"sync-{Guid.NewGuid():N}";
        var document = new DiagramDocument
        {
            DocumentId = documentId,
            Metadata = DiagramMetadata.Create("Synced Diagram"),
            TemplateKind = TemplateKind.Flowchart
        };

        var pushResponse = await client.PostAsJsonAsync(
            $"/api/sync/documents/{documentId}",
            new
            {
                knownServerRevision = 0L,
                document
            });

        pushResponse.EnsureSuccessStatusCode();
        var pushed = await pushResponse.Content.ReadFromJsonAsync<SyncResult>();
        Assert.NotNull(pushed);
        Assert.True(pushed.IsSuccess);
        Assert.NotNull(pushed.Document);
        Assert.True(pushed.ServerRevision > 0);

        var pullResponse = await client.GetAsync($"/api/sync/documents/{documentId}");
        pullResponse.EnsureSuccessStatusCode();
        var pulled = await pullResponse.Content.ReadFromJsonAsync<SyncResult>();

        Assert.NotNull(pulled);
        Assert.True(pulled.IsSuccess);
        Assert.NotNull(pulled.Document);
        Assert.Equal(documentId, pulled.Document.DocumentId);
        Assert.Equal("Synced Diagram", pulled.Document.Metadata.Title);
        Assert.Equal(pushed.ServerRevision, pulled.ServerRevision);
    }

    [Fact]
    public async Task Sync_Push_Returns_Conflict_For_Stale_Revisions()
    {
        using var client = _factory.CreateClient();
        var documentId = $"conflict-{Guid.NewGuid():N}";
        var document = new DiagramDocument
        {
            DocumentId = documentId,
            Metadata = DiagramMetadata.Create("Conflict Diagram"),
            TemplateKind = TemplateKind.Flowchart
        };

        var firstPush = await client.PostAsJsonAsync(
            $"/api/sync/documents/{documentId}",
            new
            {
                knownServerRevision = 0L,
                document
            });

        firstPush.EnsureSuccessStatusCode();

        var secondPush = await client.PostAsJsonAsync(
            $"/api/sync/documents/{documentId}",
            new
            {
                knownServerRevision = 0L,
                document
            });

        secondPush.EnsureSuccessStatusCode();
        var conflict = await secondPush.Content.ReadFromJsonAsync<SyncResult>();

        Assert.NotNull(conflict);
        Assert.False(conflict.IsSuccess);
        Assert.True(conflict.HasConflict);
        Assert.NotNull(conflict.Document);
        Assert.True(conflict.ServerRevision > 0);
    }
}
