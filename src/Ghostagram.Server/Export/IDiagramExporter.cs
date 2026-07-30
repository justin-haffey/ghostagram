using Ghostagram.Contracts;

namespace Ghostagram.Server.Export;

public sealed record DiagramExportArtifact(string MediaType, string Content);

public interface IDiagramExporter
{
    string Format { get; }
    DiagramExportArtifact Export(DiagramSnapshot snapshot);
}
