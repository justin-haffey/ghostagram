using AppHost.Services;
using AppHost.Components;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Serialization;
using Editor.Client.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IDocumentSerializer, DiagramDocumentSerializer>();
builder.Services.AddSingleton<ServerDocumentSyncStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapHealthChecks("/healthz");

app.MapGet("/api/sync/documents/{documentId}", async (string documentId, ServerDocumentSyncStore store, CancellationToken cancellationToken) =>
{
    var result = await store.PullAsync(documentId, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/sync/documents/{documentId}", async (string documentId, SyncPushRequest request, ServerDocumentSyncStore store, CancellationToken cancellationToken) =>
{
    var result = await store.PushAsync(documentId, request, cancellationToken);
    return Results.Ok(result);
});

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Editor.Client._Imports).Assembly);

app.Run();

public partial class Program;
