using Diagrams.Core.Exports;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Layouts;
using Diagrams.Core.Serialization;
using Diagrams.Core.Templates;
using Diagrams.Core.Validation;
using Diagrams.Interop.JsPlumb;
using Editor.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IDocumentSerializer, DiagramDocumentSerializer>();
builder.Services.AddScoped<IDiagramLayoutEngine, DiagramLayoutEngine>();
builder.Services.AddScoped<IValidationEngine, DiagramValidationEngine>();
builder.Services.AddScoped<DiagramStencilCatalog>();
builder.Services.AddScoped<PortPresetCatalog>();
builder.Services.AddScoped<DiagramTemplateCatalog>();
builder.Services.AddScoped<SvgExportRenderer>();
builder.Services.AddScoped<IDocumentCatalogRepository, BrowserDocumentCatalogRepository>();
builder.Services.AddScoped<IDocumentSyncService, HttpDocumentSyncService>();
builder.Services.AddScoped<ICommandHistory, DocumentCommandHistory>();
builder.Services.AddScoped<IJsPlumbAdapter, JsPlumbAdapter>();
builder.Services.AddScoped<DiagramEditorState>();
builder.Services.AddScoped<EditorShellUiState>();

await builder.Build().RunAsync();
