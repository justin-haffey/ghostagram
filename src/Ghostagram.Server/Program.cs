using Ghostagram.Contracts;
using Ghostagram.Server.Components;
using Ghostagram.Server;
using Ghostagram.Server.Export;
using Ghostagram.Server.Layout;
using Ghostagram.Server.Sessions;
using Ghostagram.Server.Persistence;
using Ghostagram.Execution;
using Ghostagram.NodeSets.Maf;
using Ghostagram.NodeSets.UML;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    var keyDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "ghostagram-server", "data-protection"));
    keyDirectory.Create();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keyDirectory)
        .SetApplicationName("Ghostagram.Server.Development");
}
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddMudServices();
builder.Services.AddSignalR();
builder.Services.AddScoped<LaboratoryCircuitState>();
builder.Services.AddScoped<CircuitHandler>(provider => provider.GetRequiredService<LaboratoryCircuitState>());
builder.Services.AddSingleton<FileDocumentStore>();
builder.Services.AddSingleton<IDocumentStore>(provider => provider.GetRequiredService<FileDocumentStore>());
builder.Services.AddSingleton<IDocumentCatalog>(provider => provider.GetRequiredService<FileDocumentStore>());
builder.Services.AddSingleton<FilePaletteCatalogRepository>();
builder.Services.AddSingleton<IPaletteCatalogRepository>(provider => provider.GetRequiredService<FilePaletteCatalogRepository>());
builder.Services.AddSingleton<ILaboratoryWorkspaceStore, FileLaboratoryWorkspaceStore>();
builder.Services.AddSingleton<IDocumentCommandQueue, DocumentCommandQueue>();
builder.Services.AddSingleton<DocumentChangeNotifier>();
builder.Services.AddSingleton<IDocumentChangeNotifier>(provider => provider.GetRequiredService<DocumentChangeNotifier>());
builder.Services.AddSingleton<IDocumentEventPublisher, SignalRDocumentEventPublisher>();
builder.Services.AddSingleton<DiagramCommandService>();
builder.Services.AddSingleton<IDiagramLayoutStrategy, GhostLayeredLayoutStrategy>();
builder.Services.AddSingleton<IDiagramLayoutStrategyResolver, DiagramLayoutStrategyResolver>();
builder.Services.AddSingleton<DiagramLayoutService>();
builder.Services.AddSingleton<IDiagramExporter, SvgDiagramExporter>();
builder.Services.AddSingleton<DiagramSessionService>();
builder.Services.AddSingleton<Ghostagram.Server.Mcp.GhostagramCapabilityCatalog>();
builder.Services.AddSingleton<INodeTypeRegistry>(_ => new NodeTypeRegistry([MafOrchestrationNodeSet.Descriptor, UmlNodeSet.Descriptor]));
builder.Services.AddSingleton<INodeFactory, DeterministicNodeFactory>();
builder.Services.AddSingleton<IGraphCompiler, GraphCompiler>();
builder.Services.AddSingleton<IGraphExecutionEngine, GraphExecutionEngine>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
var ghostagramAssets = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Ghostagram"));
if (Directory.Exists(ghostagramAssets))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(ghostagramAssets),
        RequestPath = "/ghostagram",
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
    });
}
app.UseStaticFiles();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    // MudBlazor emits its theme variables and Ghostagram emits instance-scoped
    // animation keyframes as inline style elements. Scripts remain self-only.
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; connect-src 'self' https://api.iconify.design; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' https://code.iconify.design";
    await next();
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/documents", async (IDocumentCatalog catalog, CancellationToken cancellationToken)
    => Results.Ok(await catalog.ListAsync(cancellationToken)));
app.MapGet("/api/documents/{documentId}", async (string documentId, DiagramCommandService commands, CancellationToken cancellationToken)
    => (await commands.GetSnapshotAsync(documentId, cancellationToken)) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());
app.MapGet("/api/documents/{documentId}/changes", async (string documentId, long afterRevision, DiagramCommandService commands, CancellationToken cancellationToken)
    => Results.Ok(await commands.GetChangesAsync(documentId, afterRevision, cancellationToken)));
app.MapPost("/api/documents/{documentId}/commands", async (string documentId, DiagramCommand command, DiagramCommandService commands, CancellationToken cancellationToken) =>
{
    if (!string.Equals(documentId, command.DocumentId, StringComparison.Ordinal))
        return Results.BadRequest(new { code = "DOCUMENT_ID_MISMATCH", message = "Route and command document IDs must match." });

    var result = await commands.SubmitAsync(command, cancellationToken);
    return result.Accepted ? Results.Ok(result) : result.Code == "REVISION_CONFLICT" ? Results.Conflict(result) : Results.BadRequest(result);
});
app.MapGet("/api/layouts", (DiagramLayoutService layouts) => Results.Ok(layouts.Capabilities()));
app.MapGet("/api/palettes", async (IPaletteCatalogRepository catalogs, CancellationToken cancellationToken)
    => Results.Ok(await catalogs.ListAsync(cancellationToken)));
app.MapGet("/api/palettes/{catalogId}", async (string catalogId, IPaletteCatalogRepository catalogs, CancellationToken cancellationToken)
    => (await catalogs.GetAsync(catalogId, cancellationToken)) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());
app.MapPost("/api/documents/{documentId}/layout", async (string documentId, DiagramLayoutRequest request, DiagramLayoutService layouts, CancellationToken cancellationToken) =>
{
    if (!string.Equals(documentId, request.DocumentId, StringComparison.Ordinal))
        return Results.BadRequest(new { code = "DOCUMENT_ID_MISMATCH", message = "Route and layout request document IDs must match." });

    var result = await layouts.ExecuteAsync(request, cancellationToken);
    return result.Accepted ? Results.Ok(result) : result.Code == "REVISION_CONFLICT" ? Results.Conflict(result) : Results.BadRequest(result);
});
app.MapMcp("/mcp");
app.MapHub<DiagramHub>("/hubs/diagrams");
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
