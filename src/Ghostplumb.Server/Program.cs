using Ghostplumb.Contracts;
using Ghostplumb.Server;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<FileDocumentStore>();
builder.Services.AddSingleton<IDocumentStore>(provider => provider.GetRequiredService<FileDocumentStore>());
builder.Services.AddSingleton<IDocumentCommandQueue, DocumentCommandQueue>();
builder.Services.AddSingleton<IDocumentEventPublisher, SignalRDocumentEventPublisher>();
builder.Services.AddSingleton<DiagramCommandService>();

var app = builder.Build();
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
app.MapHub<DiagramHub>("/hubs/diagrams");

app.Run();

public partial class Program;
