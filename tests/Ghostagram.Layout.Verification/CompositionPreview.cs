using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ghostagram.Contracts;
using Ghostagram.Server.Export;

namespace Ghostagram.Layout.Verification;

/// <summary>Renders actual public projection result JSON through the existing SVG exporter.</summary>
public static class CompositionPreview
{
    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Count; index += 2)
            {
                if (index + 1 >= args.Count || args[index] is not ("--composition-preview" or "--output") ||
                    !options.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("Preview requires unique --composition-preview <result.json> --output <directory> pairs.");
            }
            if (!options.TryGetValue("--composition-preview", out var input) || !options.TryGetValue("--output", out var destination))
                throw new ArgumentException("An actual projection result and output directory are required.");
            var sourcePath = Path.GetFullPath(input);
            var output = Path.GetFullPath(destination);
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length is <= 0 or > 4 * 1024 * 1024)
                throw new InvalidDataException("Projection preview input must contain at most 4 MiB of public result JSON.");
            var bytes = File.ReadAllBytes(sourcePath);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            var result = json.RootElement;
            var status = result.GetProperty("status").GetString();
            if (status is not ("Fresh" or "Stale" or "Unsupported" or "Skewed" or "Failed"))
                throw new InvalidDataException("Unknown or missing closed projection status.");
            var resultRevision = result.GetProperty("presentationRevision").GetInt64();
            if (resultRevision < 0) throw new InvalidDataException("A presentation revision is required.");
            var resultDigest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            JsonElement? diagram = null;
            var diagramLabel = "No accepted diagram";
            long? renderedRevision = null;
            string? renderedContentDigest = null;
            if (status == "Fresh")
            {
                diagram = result.GetProperty("document");
                diagramLabel = "Fresh definition projection";
                renderedRevision = resultRevision;
                renderedContentDigest = ContentDigest(result);
            }
            else
            {
                if (result.TryGetProperty("document", out var unexpected) && unexpected.ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Only Fresh may carry a newly accepted diagram.");
                if (status == "Stale" && result.TryGetProperty("lastKnown", out var previous) && previous.ValueKind == JsonValueKind.Object)
                {
                    if (previous.GetProperty("status").GetString() != "Fresh")
                        throw new InvalidDataException("A stale last-known diagram must be an exact prior Fresh result.");
                    diagram = previous.GetProperty("document");
                    renderedRevision = previous.GetProperty("presentationRevision").GetInt64();
                    if (renderedRevision < 0) throw new InvalidDataException("The last-known presentation revision is invalid.");
                    renderedContentDigest = ContentDigest(previous);
                    diagramLabel = "Stale: last-known definition projection; full reprojection required";
                }
            }
            string? svg = null;
            var width = "0"; var height = "0";
            if (diagram is { } model)
            {
                var documentId = model.GetProperty("documentId").GetString();
                if (string.IsNullOrWhiteSpace(documentId)) throw new InvalidDataException("The projected document identity is missing.");
                svg = new SvgDiagramExporter().Export(new DiagramSnapshot(documentId, renderedRevision!.Value, model.Clone())).Content;
                var viewBox = XDocument.Parse(svg).Root?.Attribute("viewBox")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (viewBox is not { Length: 4 } || !double.TryParse(viewBox[2], CultureInfo.InvariantCulture, out var w) ||
                    !double.TryParse(viewBox[3], CultureInfo.InvariantCulture, out var h) || !double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0)
                    throw new InvalidDataException("The existing renderer did not return finite SVG bounds.");
                width = w.ToString("G17", CultureInfo.InvariantCulture); height = h.ToString("G17", CultureInfo.InvariantCulture);
            }
            var files = new[] { "index.html", "preview-provenance.json" }.Concat(svg is null ? [] : new[] { "diagram.svg" }).ToArray();
            if (files.Any(file => File.Exists(Path.Combine(output, file))))
                throw new IOException("Preview files already exist; choose a fresh output directory to retain evidence history.");
            var panel = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Composition definition projection</title>")
                .Append("<style>body{font:16px system-ui,sans-serif;margin:24px;color:#172033}h1{font-size:24px}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f3f5f8;padding:12px}object{display:block;border:1px solid #cbd2dd}dt{font-weight:600}dd{margin:4px 0 16px}</style>")
                .Append("<h1>").Append(E(diagramLabel)).Append("</h1><dl><dt>Result status</dt><dd>").Append(E(status))
                .Append("</dd><dt>Public result SHA-256</dt><dd>").Append(E(resultDigest)).Append("</dd><dt>Result presentation revision</dt><dd>")
                .Append(resultRevision.ToString(CultureInfo.InvariantCulture)).Append("</dd>");
            if (renderedRevision is { } actualRevision)
                panel.Append("<dt>Rendered presentation revision</dt><dd>").Append(actualRevision.ToString(CultureInfo.InvariantCulture))
                    .Append("</dd><dt>Rendered source content digest</dt><dd>").Append(E(renderedContentDigest)).Append("</dd>");
            panel.Append("</dl>");
            foreach (var field in new[] { "sourceAnchor", "sourceDiagnostics", "diagnostics" })
                if (result.TryGetProperty(field, out var value))
                    panel.Append("<h2>").Append(E(field)).Append("</h2><pre>")
                        .Append(E(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }))).Append("</pre>");
            if (svg is not null)
                panel.Append("<h2>").Append(E(diagramLabel)).Append("</h2><object aria-label=\"").Append(E(diagramLabel))
                    .Append("\" type=\"image/svg+xml\" data=\"diagram.svg\" width=\"").Append(width).Append("\" height=\"").Append(height).Append("\"></object>");
            panel.Append("</html>");
            Directory.CreateDirectory(output);
            if (svg is not null) File.WriteAllText(Path.Combine(output, "diagram.svg"), svg, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "index.html"), panel.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "preview-provenance.json"), JsonSerializer.Serialize(new
            {
                inputFile = sourcePath, resultSha256 = resultDigest, status, resultPresentationRevision = resultRevision,
                renderedPresentationRevision = renderedRevision, renderedSourceContentDigest = renderedContentDigest,
                diagramDisposition = diagramLabel,
                svgSha256 = svg is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(svg))),
                renderer = "Ghostagram.Server.Export.SvgDiagramExporter", visibleInspection = "Pending"
            }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            Console.WriteLine($"Composition preview created: {Path.Combine(output, "index.html")}; visible inspection pending.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException or InvalidOperationException or FormatException)
        {
            Console.Error.WriteLine($"Composition preview failed: {exception.Message}");
            return 2;
        }
    }

    private static string? ContentDigest(JsonElement result) =>
        result.TryGetProperty("sourceAnchor", out var anchor) && anchor.ValueKind == JsonValueKind.Object &&
        anchor.TryGetProperty("contentDigest", out var digest) ? digest.GetString() : null;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
