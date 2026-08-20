using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Text;

namespace Ghostagram.Server.Export;

/// <summary>A trusted server-side renderer registration available to deterministic SVG export.</summary>
public sealed record NodeSvgRendererRegistration(string Key, int Version);

/// <summary>Serializable node property information exposed to a trusted SVG body renderer.</summary>
public sealed record NodeSvgProperty(string Id, string Label, string Value);

/// <summary>
/// Geometry and durable display state for a custom node body. The exporter retains ownership of
/// the outer node rectangle, title, icon, ports, and all interaction chrome.
/// </summary>
public sealed record NodeSvgRenderContext(
    string NodeId,
    string Label,
    double BodyX,
    double BodyY,
    double BodyWidth,
    double BodyHeight,
    IReadOnlyList<NodeSvgProperty> Properties);

/// <summary>
/// Trusted server extension point for a node body. Implementations can emit only through
/// <see cref="NodeSvgWriter"/>; persisted diagrams never contain executable callbacks or markup.
/// </summary>
public interface INodeSvgBodyRenderer
{
    string Key { get; }
    int Version { get; }
    void Render(NodeSvgRenderContext context, NodeSvgWriter writer);
}

public interface INodeSvgRendererRegistry
{
    IReadOnlyList<NodeSvgRendererRegistration> Registrations { get; }
    bool TryGet(string key, int version, out INodeSvgBodyRenderer renderer);
}

/// <summary>Immutable exact-version registry for trusted server SVG body renderers.</summary>
public sealed class NodeSvgRendererRegistry : INodeSvgRendererRegistry
{
    private readonly FrozenDictionary<(string Key, int Version), INodeSvgBodyRenderer> renderers;

    public NodeSvgRendererRegistry(IEnumerable<INodeSvgBodyRenderer>? renderers = null)
    {
        var items = (renderers ?? []).ToArray();
        foreach (var renderer in items)
        {
            if (string.IsNullOrWhiteSpace(renderer.Key))
                throw new ArgumentException("A node SVG renderer key is required.", nameof(renderers));
            if (renderer.Version < 1)
                throw new ArgumentOutOfRangeException(nameof(renderers), "A node SVG renderer version must be positive.");
        }

        var duplicate = items.GroupBy(renderer => (renderer.Key, renderer.Version))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate node SVG renderer registration '{duplicate.Key.Key}@{duplicate.Key.Version}'.");

        this.renderers = items.ToFrozenDictionary(renderer => (renderer.Key, renderer.Version));
        Registrations = Array.AsReadOnly(items
            .Select(renderer => new NodeSvgRendererRegistration(renderer.Key, renderer.Version))
            .OrderBy(renderer => renderer.Key, StringComparer.Ordinal)
            .ThenBy(renderer => renderer.Version)
            .ToArray());
    }

    public IReadOnlyList<NodeSvgRendererRegistration> Registrations { get; }

    public bool TryGet(string key, int version, out INodeSvgBodyRenderer renderer) =>
        renderers.TryGetValue((key, version), out renderer!);
}

/// <summary>
/// Escaping-only SVG body writer. It deliberately has no raw-markup API, so custom values cannot
/// escape into executable HTML/SVG even when they originate in persisted diagram properties.
/// </summary>
public sealed class NodeSvgWriter
{
    private readonly StringBuilder content = new();

    public void Text(double x, double y, string value, int size = 12, string color = "#334155", string anchor = "start")
    {
        RequireFinite(x, y);
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size), "SVG text size must be positive.");
        content.Append("<text x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(y))
            .Append("\" font-family=\"system-ui,sans-serif\" font-size=\"").Append(size)
            .Append("\" fill=\"").Append(Escape(color)).Append("\" text-anchor=\"").Append(Escape(anchor)).Append("\">")
            .Append(Escape(value)).Append("</text>");
    }

    public void Rectangle(double x, double y, double width, double height, string fill = "none", string stroke = "none", double cornerRadius = 0)
    {
        RequireFinite(x, y, width, height, cornerRadius);
        if (width < 0 || height < 0 || cornerRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(width), "SVG rectangle dimensions must be non-negative.");
        content.Append("<rect x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(y))
            .Append("\" width=\"").Append(Number(width)).Append("\" height=\"").Append(Number(height))
            .Append("\" rx=\"").Append(Number(cornerRadius)).Append("\" fill=\"").Append(Escape(fill))
            .Append("\" stroke=\"").Append(Escape(stroke)).Append("\"/>");
    }

    public void Circle(double centerX, double centerY, double radius, string fill = "none", string stroke = "none")
    {
        RequireFinite(centerX, centerY, radius);
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), "SVG circle radius must be non-negative.");
        content.Append("<circle cx=\"").Append(Number(centerX)).Append("\" cy=\"").Append(Number(centerY))
            .Append("\" r=\"").Append(Number(radius)).Append("\" fill=\"").Append(Escape(fill))
            .Append("\" stroke=\"").Append(Escape(stroke)).Append("\"/>");
    }

    public void Path(string data, string fill = "none", string stroke = "#334155", double strokeWidth = 1)
    {
        if (string.IsNullOrWhiteSpace(data)) throw new ArgumentException("SVG path data is required.", nameof(data));
        RequireFinite(strokeWidth);
        if (strokeWidth < 0) throw new ArgumentOutOfRangeException(nameof(strokeWidth), "SVG path stroke width must be non-negative.");
        content.Append("<path d=\"").Append(Escape(data)).Append("\" fill=\"").Append(Escape(fill))
            .Append("\" stroke=\"").Append(Escape(stroke)).Append("\" stroke-width=\"").Append(Number(strokeWidth)).Append("\"/>");
    }

    internal string ToSvg() => content.ToString();

    private static void RequireFinite(params double[] values)
    {
        if (values.Any(value => !double.IsFinite(value)))
            throw new ArgumentOutOfRangeException(nameof(values), "SVG coordinates must be finite.");
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string Number(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
