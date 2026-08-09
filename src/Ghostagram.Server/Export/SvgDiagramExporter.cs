using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Ghostagram.Contracts;

namespace Ghostagram.Server.Export;

/// <summary>Deterministic, dependency-free SVG projection for server and MCP exports.</summary>
public sealed class SvgDiagramExporter : IDiagramExporter
{
    public string Format => "svg";

    public DiagramExportArtifact Export(DiagramSnapshot snapshot)
    {
        var root = snapshot.Model;
        var nodes = Items(root, "nodes").Select(Node.Parse).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var groups = Items(root, "groups").Select(Box.Parse).ToArray();
        var ports = Items(root, "ports").Select(Port.Parse).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var edges = Items(root, "edges").Select(Edge.Parse).ToArray();
        var boxes = groups.Concat(nodes.Values.Select(item => item.Box)).ToArray();
        var minX = boxes.Length == 0 ? 0 : boxes.Min(item => item.X);
        var minY = boxes.Length == 0 ? 0 : boxes.Min(item => item.Y);
        var maxX = boxes.Length == 0 ? 640 : boxes.Max(item => item.X + item.Width);
        var maxY = boxes.Length == 0 ? 480 : boxes.Max(item => item.Y + item.Height);
        const double padding = 40;
        var width = Math.Max(1, maxX - minX + padding * 2);
        var height = Math.Max(1, maxY - minY + padding * 2);

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"")
            .Append(Escape($"Ghostagram diagram {snapshot.DocumentId}"))
            .Append("\" viewBox=\"").Append(N(minX - padding)).Append(' ').Append(N(minY - padding)).Append(' ')
            .Append(N(width)).Append(' ').Append(N(height)).Append("\">")
            .Append("<defs><marker id=\"gp-arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#0f766e\"/></marker></defs>")
            .Append("<rect x=\"").Append(N(minX - padding)).Append("\" y=\"").Append(N(minY - padding))
            .Append("\" width=\"").Append(N(width)).Append("\" height=\"").Append(N(height)).Append("\" fill=\"#f8fafc\"/>");

        foreach (var group in groups.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            svg.Append("<g data-group-id=\"").Append(Escape(group.Id)).Append("\"><rect x=\"").Append(N(group.X))
                .Append("\" y=\"").Append(N(group.Y)).Append("\" width=\"").Append(N(group.Width)).Append("\" height=\"")
                .Append(N(group.Height)).Append("\" rx=\"8\" fill=\"#e2e8f0\" fill-opacity=\"0.55\" stroke=\"#64748b\" stroke-dasharray=\"6 4\"/>")
                .Append(Text(group.X + 12, group.Y + 21, group.Label, 14, "#334155")).Append("</g>");
        }

        foreach (var edge in edges.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!TryPoint(edge.SourcePortId, ports, nodes, out var source) ||
                !TryPoint(edge.TargetPortId, ports, nodes, out var target)) continue;
            svg.Append("<g data-edge-id=\"").Append(Escape(edge.Id)).Append("\"><path d=\"").Append(EdgePath(edge.Connector, source, target))
                .Append("\" fill=\"none\" stroke=\"#0f766e\" stroke-width=\"2\" marker-end=\"url(#gp-arrow)\"/>");
            if (!string.IsNullOrWhiteSpace(edge.Label))
                svg.Append(Text((source.X + target.X) / 2, (source.Y + target.Y) / 2 - 6, edge.Label, 12, "#334155", "middle"));
            svg.Append("</g>");
        }

        foreach (var node in nodes.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            svg.Append("<g data-node-id=\"").Append(Escape(node.Id)).Append("\"><rect x=\"").Append(N(node.Box.X))
                .Append("\" y=\"").Append(N(node.Box.Y)).Append("\" width=\"").Append(N(node.Box.Width)).Append("\" height=\"")
                .Append(N(node.Box.Height)).Append("\" rx=\"8\" fill=\"#ffffff\" stroke=\"#334155\" stroke-width=\"1.5\"/>")
                .Append(Text(node.Box.X + 12, node.Box.Y + (node.Properties.Count == 0 ? node.Box.Height / 2 + 5 : 21), node.Box.Label, 15, "#0f172a"));
            if (!string.IsNullOrWhiteSpace(node.Icon))
                svg.Append(Text(node.Box.X + node.Box.Width - 10, node.Box.Y + 20, IconGlyph(node.Icon), 13, "#475569", "end"));
            var visibleIndex = 0;
            for (var index = 0; index < node.Properties.Count; index++)
            {
                var property = node.Properties[index];
                if (property.Hidden) continue;
                var y = node.Box.Y + 49 + visibleIndex++ * 21;
                svg.Append(Text(node.Box.X + 12, y, property.Label, 11, "#64748b"))
                    .Append(Text(node.Box.X + node.Box.Width - 12, y, property.Value, 11, "#334155", "end"));
            }
            svg.Append("</g>");
        }

        foreach (var port in ports.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!TryPoint(port.Id, ports, nodes, out var point)) continue;
            svg.Append("<circle data-port-id=\"").Append(Escape(port.Id)).Append("\" cx=\"").Append(N(point.X)).Append("\" cy=\"")
                .Append(N(point.Y)).Append("\" r=\"5\" fill=\"#f8fafc\" stroke=\"#0f766e\" stroke-width=\"2\"/>");
        }

        svg.Append("</svg>");
        return new("image/svg+xml", svg.ToString());
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
        => root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
            : [];

    private static bool TryPoint(
        string portId,
        IReadOnlyDictionary<string, Port> ports,
        IReadOnlyDictionary<string, Node> nodes,
        out Point point)
    {
        point = default;
        if (!ports.TryGetValue(portId, out var port) || !nodes.TryGetValue(port.NodeId, out var node)) return false;
        var box = node.Box;
        var explicitAnchor = !string.IsNullOrWhiteSpace(port.Anchor);
        point = port.Anchor.ToLowerInvariant() switch
        {
            "left" => new(box.X, box.Y + box.Height / 2),
            "right" => new(box.X + box.Width, box.Y + box.Height / 2),
            "top" => new(box.X + box.Width / 2, box.Y),
            "bottom" => new(box.X + box.Width / 2, box.Y + box.Height),
            "center" => new(box.X + box.Width / 2, box.Y + box.Height / 2),
            _ => new(box.X + box.Width, box.Y + box.Height / 2)
        };
        if (!string.IsNullOrWhiteSpace(port.PropertyId))
        {
            var row = node.Properties.Where(item => !item.Hidden).ToList()
                .FindIndex(item => string.Equals(item.Id, port.PropertyId, StringComparison.Ordinal));
            if (row >= 0)
            {
                var y = box.Y + 40 + row * 21;
                point = port.Direction.Equals("target", StringComparison.OrdinalIgnoreCase)
                    ? new(box.X, y)
                    : new(box.X + box.Width, y);
            }
        }
        else if (!explicitAnchor && node.Properties.Count > 0)
        {
            var ordered = ports.Values.Where(item => string.Equals(item.NodeId, node.Id, StringComparison.Ordinal))
                .OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
            var slot = ordered.FindIndex(item => string.Equals(item.Id, port.Id, StringComparison.Ordinal));
            if (slot >= 0)
            {
                var y = box.Y + 40 + slot * 21;
                point = port.Direction.Equals("target", StringComparison.OrdinalIgnoreCase)
                    ? new(box.X, y)
                    : new(box.X + box.Width, y);
            }
        }
        return true;
    }

    private static string EdgePath(string connector, Point source, Point target)
    {
        if (connector.Equals("bezier", StringComparison.OrdinalIgnoreCase) ||
            connector.Equals("state-machine", StringComparison.OrdinalIgnoreCase))
        {
            var control = Math.Max(48, Math.Max(Math.Abs(target.X - source.X) * .45, Math.Abs(target.Y - source.Y) * .28));
            return $"M {N(source.X)} {N(source.Y)} C {N(source.X + control)} {N(source.Y)} {N(target.X - control)} {N(target.Y)} {N(target.X)} {N(target.Y)}";
        }

        if (connector.Equals("flowchart", StringComparison.OrdinalIgnoreCase) ||
            connector.Equals("square", StringComparison.OrdinalIgnoreCase))
        {
            var middleX = (source.X + target.X) / 2;
            return $"M {N(source.X)} {N(source.Y)} L {N(middleX)} {N(source.Y)} L {N(middleX)} {N(target.Y)} L {N(target.X)} {N(target.Y)}";
        }

        return $"M {N(source.X)} {N(source.Y)} L {N(target.X)} {N(target.Y)}";
    }

    private static string IconGlyph(string icon)
        => icon.Contains("agent", StringComparison.OrdinalIgnoreCase) ? "AI" : "◇";

    private static string Text(double x, double y, string label, int size, string color, string anchor = "start")
        => $"<text x=\"{N(x)}\" y=\"{N(y)}\" font-family=\"system-ui,sans-serif\" font-size=\"{size}\" fill=\"{color}\" text-anchor=\"{anchor}\">{Escape(label)}</text>";

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
    private static string S(JsonElement item, string name, string fallback = "")
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static double D(JsonElement item, string name, double fallback)
        => item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
    private static int I(JsonElement item, string name, int fallback)
        => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private readonly record struct Point(double X, double Y);
    private sealed record Box(string Id, string Label, double X, double Y, double Width, double Height)
    {
        public static Box Parse(JsonElement item) => new(S(item, "id"), S(item, "label"), D(item, "x", 0), D(item, "y", 0), D(item, "width", 160), D(item, "height", 80));
    }
    private sealed record Node(string Id, Box Box, string Icon, List<Property> Properties)
    {
        public static Node Parse(JsonElement item)
        {
            var box = Box.Parse(item);
            var properties = Items(item, "properties").Select(Property.Parse).ToList();
            return new(box.Id, box, S(item, "icon"), properties);
        }
    }
    private sealed record Property(string Id, string Label, string Value, bool Hidden)
    {
        public static Property Parse(JsonElement item)
        {
            var id = S(item, "id");
            var label = S(item, "label", S(item, "name", id));
            var mode = S(item, "mode", "display");
            var value = item.TryGetProperty("value", out var rawValue) ? DisplayValue(rawValue) : "—";
            return new(id, label, value, mode.Equals("hidden", StringComparison.OrdinalIgnoreCase));
        }

        private static string DisplayValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.GetRawText()
        };
    }
    private sealed record Port(string Id, string NodeId, string Anchor, string Direction, string PropertyId, int Order)
    {
        public static Port Parse(JsonElement item) => new(
            S(item, "id"), S(item, "nodeId"), S(item, "anchor"), S(item, "direction", "both"), S(item, "propertyId"), I(item, "order", 0));
    }
    private sealed record Edge(string Id, string SourcePortId, string TargetPortId, string Label, string Connector)
    {
        public static Edge Parse(JsonElement item) => new(
            S(item, "id"), S(item, "sourcePortId"), S(item, "targetPortId"), S(item, "label"), S(item, "connector", "flowchart"));
    }
}
