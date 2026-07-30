using Ghostagram.Contracts;
using Ghostagram.Server;
using Ghostagram.Server.Export;
using Ghostagram.Server.Layout;
using Ghostagram.Server.Sessions;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<FileDocumentStore>();
builder.Services.AddSingleton<IDocumentStore>(provider => provider.GetRequiredService<FileDocumentStore>());
builder.Services.AddSingleton<IDocumentCommandQueue, DocumentCommandQueue>();
builder.Services.AddSingleton<IDocumentEventPublisher, SignalRDocumentEventPublisher>();
builder.Services.AddSingleton<DiagramCommandService>();
builder.Services.AddSingleton<IDiagramLayoutStrategy, GhostLayeredLayoutStrategy>();
builder.Services.AddSingleton<IDiagramLayoutStrategyResolver, DiagramLayoutStrategyResolver>();
builder.Services.AddSingleton<DiagramLayoutService>();
builder.Services.AddSingleton<IDiagramExporter, SvgDiagramExporter>();
builder.Services.AddSingleton<DiagramSessionService>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
var ghostagramAssets = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Ghostagram"));
if (Directory.Exists(ghostagramAssets))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(ghostagramAssets),
        RequestPath = "/ghostagram"
    });
}
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'";
    await next();
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
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
app.MapPost("/api/documents/{documentId}/layout", async (string documentId, DiagramLayoutRequest request, DiagramLayoutService layouts, CancellationToken cancellationToken) =>
{
    if (!string.Equals(documentId, request.DocumentId, StringComparison.Ordinal))
        return Results.BadRequest(new { code = "DOCUMENT_ID_MISMATCH", message = "Route and layout request document IDs must match." });

    var result = await layouts.ExecuteAsync(request, cancellationToken);
    return result.Accepted ? Results.Ok(result) : result.Code == "REVISION_CONFLICT" ? Results.Conflict(result) : Results.BadRequest(result);
});
app.MapMcp("/mcp");
app.MapHub<DiagramHub>("/hubs/diagrams");

app.Run();

public partial class Program;
