using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Ghostagram.Contracts;

namespace Ghostagram.Server.Export;

/// <summary>Deterministic, dependency-free SVG projection for server and MCP exports.</summary>
public sealed class SvgDiagramExporter : IDiagramExporter
{
    private const double NodeHeaderHeight = 30;
    private const double PropertyHeight = 20;
    private const double CompactPropertyHeight = 18;
    private const double PropertyGap = 1;
    private const double SectionHeaderHeight = 22;
    private static readonly string[] MarkerTypes = ["arrow", "plain-arrow", "triangle-open", "diamond", "diamond-open", "erd-one", "erd-zero-one", "erd-one-many", "erd-zero-many"];
    public string Format => "svg";

    public DiagramExportArtifact Export(DiagramSnapshot snapshot)
    {
        var root = snapshot.Model;
        var nodes = Items(root, "nodes").Select(Node.Parse).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var groups = Items(root, "groups").Select(Box.Parse).ToArray();
        var ports = Items(root, "ports").Select(Port.Parse).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var edgeTypes = Items(root, "edgeTypes")
            .Where(item => !string.IsNullOrWhiteSpace(S(item, "id")))
            .ToDictionary(item => S(item, "id"), item => item, StringComparer.Ordinal);
        var edges = Items(root, "edges").Select(item => Edge.Parse(item, edgeTypes)).ToArray();
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
            .Append(N(width)).Append(' ').Append(N(height)).Append("\"><defs>");
        foreach (var markerType in MarkerTypes) svg.Append(MarkerSvg(markerType));
        svg.Append("</defs>")
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
                .Append("\" fill=\"none\" stroke=\"").Append(Escape(edge.Stroke)).Append("\" stroke-width=\"2\"");
            if (!string.IsNullOrWhiteSpace(edge.StartMarker)) svg.Append(" marker-start=\"url(#gp-").Append(edge.StartMarker).Append(")\"");
            if (!string.IsNullOrWhiteSpace(edge.EndMarker)) svg.Append(" marker-end=\"url(#gp-").Append(edge.EndMarker).Append(")\"");
            svg.Append("/>");
            if (!string.IsNullOrWhiteSpace(edge.Label))
                svg.Append(Text((source.X + target.X) / 2, (source.Y + target.Y) / 2 - 6, edge.Label, 12, "#334155", "middle"));
            svg.Append("</g>");
        }

        foreach (var node in nodes.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var layout = node.Layout;
            svg.Append("<g data-node-id=\"").Append(Escape(node.Id)).Append("\"><rect x=\"").Append(N(node.Box.X))
                .Append("\" y=\"").Append(N(node.Box.Y)).Append("\" width=\"").Append(N(node.Box.Width)).Append("\" height=\"")
                .Append(N(node.Box.Height)).Append("\" rx=\"8\" fill=\"#ffffff\" stroke=\"#334155\" stroke-width=\"1.5\"/>")
                .Append(Text(node.Box.X + 12, node.Box.Y + 20, node.Box.Label, 15, "#0f172a"));
            if (!string.IsNullOrWhiteSpace(node.Icon))
                svg.Append(Text(node.Box.X + node.Box.Width - 10, node.Box.Y + 20, IconGlyph(node.Icon), 13, "#475569", "end"));
            if (layout.Progressive)
                svg.Append("<path d=\"M ").Append(N(node.Box.X)).Append(' ').Append(N(node.Box.Y + NodeHeaderHeight))
                    .Append(" L ").Append(N(node.Box.X + node.Box.Width)).Append(' ').Append(N(node.Box.Y + NodeHeaderHeight))
                    .Append("\" stroke=\"#334155\" stroke-opacity=\"0.28\"/>");
            foreach (var section in layout.Sections)
            {
                var heading = $"{(section.Collapsed ? "▸ " : "▾ ")}{section.Section.Title}";
                svg.Append("<text data-section-id=\"").Append(Escape(section.Section.Id)).Append("\" x=\"")
                    .Append(N(node.Box.X + 8 + Math.Min(section.Depth, 4) * 8)).Append("\" y=\"")
                    .Append(N(node.Box.Y + section.Y + 15))
                    .Append("\" font-family=\"system-ui,sans-serif\" font-size=\"11\" font-weight=\"600\" fill=\"#334155\">")
                    .Append(Escape(heading)).Append("</text>");
            }
            foreach (var row in layout.Rows)
            {
                var y = node.Box.Y + row.Y + row.Height / 2 + 4;
                svg.Append(Text(node.Box.X + 8, y, row.Property.Label, 11, "#64748b"))
                    .Append(Text(node.Box.X + node.Box.Width - 8, y, row.Property.Value, 11, "#334155", "end"));
            }
            svg.Append("</g>");
        }

        foreach (var port in ports.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!TryPoint(port.Id, ports, nodes, out var point)) continue;
            if (!string.IsNullOrWhiteSpace(port.PropertyId))
            {
                svg.Append("<g data-port-id=\"").Append(Escape(port.Id)).Append("\" cx=\"").Append(N(point.X))
                    .Append("\" cy=\"").Append(N(point.Y)).Append("\" data-port-kind=\"property\">")
                    .Append("<rect x=\"").Append(N(point.X - 5)).Append("\" y=\"").Append(N(point.Y - 5))
                    .Append("\" width=\"10\" height=\"10\" rx=\"2\" fill=\"#f8fafc\" stroke=\"#0f766e\" stroke-width=\"2\"/>")
                    .Append("<rect x=\"").Append(N(point.X - 1.5)).Append("\" y=\"").Append(N(point.Y - 1.5))
                    .Append("\" width=\"3\" height=\"3\" rx=\"1\" fill=\"#0f766e\"/></g>");
            }
            else
            {
                svg.Append("<circle data-port-id=\"").Append(Escape(port.Id)).Append("\" cx=\"")
                    .Append(N(point.X)).Append("\" cy=\"").Append(N(point.Y)).Append("\" data-port-kind=\"node\"")
                    .Append(" r=\"5\" fill=\"#f8fafc\" stroke=\"#0f766e\" stroke-width=\"2\"/>");
            }
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
            if (node.Layout.PropertyAnchors.TryGetValue(port.PropertyId, out var anchor))
            {
                var y = box.Y + anchor.OffsetY;
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
                var y = box.Y + NodeHeaderHeight + PropertyHeight / 2 + slot * (PropertyHeight + PropertyGap);
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

    private static string MarkerSvg(string type)
    {
        var openPath = "fill=\"white\" stroke=\"context-stroke\" stroke-width=\"1.35\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";
        var linePath = "fill=\"none\" stroke=\"context-stroke\" stroke-width=\"1.35\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";
        var (viewBox, refX, refY, width, height, shape) = type switch
        {
            "plain-arrow" => ("0 0 10 10", "9", "5", "7", "7", $"<path d=\"M 0 0 L 10 5 L 0 10\" {linePath}/>") ,
            "triangle-open" => ("0 0 10 10", "9", "5", "7", "7", $"<path d=\"M 0 0 L 10 5 L 0 10 z\" {openPath}/>") ,
            "diamond" => ("0 0 10 10", "9", "5", "7", "7", "<path d=\"M 0 5 L 5 0 L 10 5 L 5 10 z\" fill=\"context-stroke\"/>") ,
            "diamond-open" => ("0 0 10 10", "9", "5", "7", "7", $"<path d=\"M 0 5 L 5 0 L 10 5 L 5 10 z\" {openPath}/>") ,
            "erd-one" => Cardinality($"<path d=\"M 15 1 L 15 11 M 20 1 L 20 11\" {linePath}/>") ,
            "erd-zero-one" => Cardinality($"<circle cx=\"12\" cy=\"6\" r=\"3.25\" fill=\"white\" stroke=\"context-stroke\" stroke-width=\"1.35\"/><path d=\"M 20 1 L 20 11\" {linePath}/>") ,
            "erd-one-many" => Cardinality($"<path d=\"M 9 1 L 9 11 M 14 6 L 22 1 M 14 6 L 22 6 M 14 6 L 22 11\" {linePath}/>") ,
            "erd-zero-many" => Cardinality($"<circle cx=\"9\" cy=\"6\" r=\"3.25\" fill=\"white\" stroke=\"context-stroke\" stroke-width=\"1.35\"/><path d=\"M 14 6 L 22 1 M 14 6 L 22 6 M 14 6 L 22 11\" {linePath}/>") ,
            _ => ("0 0 10 10", "9", "5", "7", "7", "<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"context-stroke\"/>")
        };
        return $"<marker id=\"gp-{type}\" viewBox=\"{viewBox}\" refX=\"{refX}\" refY=\"{refY}\" markerWidth=\"{width}\" markerHeight=\"{height}\" orient=\"auto-start-reverse\">{shape}</marker>";
    }

    private static (string ViewBox, string RefX, string RefY, string Width, string Height, string Shape) Cardinality(string shape) =>
        ("0 0 24 12", "22", "6", "14", "8", shape);

    private static string MarkerAt(IEnumerable<JsonElement> overlays, double location)
    {
        foreach (var overlay in overlays)
        {
            var type = overlay.ValueKind == JsonValueKind.String ? overlay.GetString() ?? string.Empty : S(overlay, "type");
            if (!MarkerTypes.Contains(type, StringComparer.Ordinal)) continue;
            var candidateLocation = overlay.ValueKind == JsonValueKind.Object ? D(overlay, "location", 1) : 1;
            if (candidateLocation == location) return type;
        }
        return string.Empty;
    }

    private readonly record struct Point(double X, double Y);
    private sealed record Box(string Id, string Label, double X, double Y, double Width, double Height)
    {
        public static Box Parse(JsonElement item) => new(S(item, "id"), S(item, "label"), D(item, "x", 0), D(item, "y", 0), D(item, "width", 160), D(item, "height", 80));
    }
    private sealed class Node
    {
        private Node(string id, Box box, string icon, List<Property> properties, List<Section> sections, Presentation? presentation)
        {
            Id = id;
            Box = box;
            Icon = icon;
            Properties = properties;
            Sections = sections;
            Presentation = presentation;
            Layout = Project(this);
        }

        public string Id { get; }
        public Box Box { get; }
        public string Icon { get; }
        public List<Property> Properties { get; }
        public List<Section> Sections { get; }
        public Presentation? Presentation { get; }
        public NodeLayout Layout { get; }

        public static Node Parse(JsonElement item)
        {
            var box = Box.Parse(item);
            var properties = Items(item, "properties").Select(Property.Parse).ToList();
            var sections = Items(item, "sections")
                .Select((section, index) => Section.Parse(section, index))
                .OrderBy(section => section.Order)
                .ThenBy(section => section.Id, StringComparer.Ordinal)
                .ToList();
            var presentation = item.TryGetProperty("presentation", out var rawPresentation) && rawPresentation.ValueKind == JsonValueKind.Object
                ? Presentation.Parse(rawPresentation)
                : null;
            return new(box.Id, box, S(item, "icon"), properties, sections, presentation);
        }
    }
    private sealed record Property(string Id, string Label, string Value, bool Hidden, string SectionId, string Type, string EditorKind)
    {
        public static Property Parse(JsonElement item)
        {
            var id = S(item, "id");
            var label = S(item, "label", S(item, "name", id));
            var mode = S(item, "mode", "display");
            var value = item.TryGetProperty("value", out var rawValue) ? DisplayValue(rawValue) : "—";
            var type = S(item, "type", "string");
            var editorKind = item.TryGetProperty("editor", out var editor) && editor.ValueKind == JsonValueKind.Object
                ? S(editor, "kind", "auto")
                : "auto";
            return new(id, label, value, mode.Equals("hidden", StringComparison.OrdinalIgnoreCase), S(item, "sectionId"), type, editorKind);
        }

        public double RowHeight(bool compact)
        {
            if (compact) return CompactPropertyHeight;
            var kind = EffectiveEditorKind();
            return kind is "multiline" or "json" ? 48 : kind == "range" ? 28 : PropertyHeight;
        }

        private string EffectiveEditorKind()
        {
            if (!string.IsNullOrWhiteSpace(EditorKind) && !EditorKind.Equals("auto", StringComparison.Ordinal)) return EditorKind;
            return Type switch
            {
                "boolean" => "toggle",
                "integer" or "decimal" or "number" => "number",
                "date" => "date",
                "dateTime" or "datetime" => "dateTime",
                "enum" => "select",
                "json" => "json",
                _ => "text"
            };
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
    private sealed record Section(string Id, string Title, string ParentSectionId, int Order, bool Collapsible)
    {
        public static Section Parse(JsonElement item, int index) => new(
            S(item, "id"),
            S(item, "title", S(item, "id")),
            S(item, "parentSectionId"),
            I(item, "order", index),
            !item.TryGetProperty("collapsible", out var collapsible) || collapsible.ValueKind != JsonValueKind.False);
    }

    private sealed record Presentation(string DisplayMode, HashSet<string> CollapsedSectionIds)
    {
        public static Presentation Parse(JsonElement item) => new(
            S(item, "displayMode", "expanded"),
            Items(item, "collapsedSectionIds")
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.Ordinal));
    }

    private sealed record PropertyRow(Property Property, double Y, double Height, int Depth, string SectionId);
    private sealed record SectionProjection(Section Section, double Y, int Depth, bool Collapsed);
    private sealed record AnchorProjection(double OffsetY, bool Proxied, string ProxyId);
    private sealed record NodeLayout(
        bool Progressive,
        string DisplayMode,
        bool Compact,
        List<PropertyRow> Rows,
        List<SectionProjection> Sections,
        Dictionary<string, AnchorProjection> PropertyAnchors);

    private static NodeLayout Project(Node node)
    {
        var displayMode = node.Presentation?.DisplayMode ?? "expanded";
        var compact = displayMode.Equals("compact", StringComparison.Ordinal);
        var progressive = node.Sections.Count > 0 || node.Presentation is not null;
        var rows = new List<PropertyRow>();
        var sectionProjections = new List<SectionProjection>();
        var anchors = new Dictionary<string, AnchorProjection>(StringComparer.Ordinal);
        if (displayMode.Equals("collapsed", StringComparison.Ordinal))
        {
            foreach (var property in node.Properties) anchors[property.Id] = new(NodeHeaderHeight / 2, true, "node");
            return new(progressive, displayMode, compact, rows, sectionProjections, anchors);
        }

        var cursor = NodeHeaderHeight;
        var visible = node.Properties.Where(property => !property.Hidden).ToArray();
        var bySection = visible.Where(property => !string.IsNullOrWhiteSpace(property.SectionId))
            .GroupBy(property => property.SectionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var unsectioned = visible.Where(property => string.IsNullOrWhiteSpace(property.SectionId));
        var children = node.Sections.Where(section => !string.IsNullOrWhiteSpace(section.ParentSectionId))
            .GroupBy(section => section.ParentSectionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var collapsed = node.Presentation?.CollapsedSectionIds ?? [];

        void AddRow(Property property, int depth, string sectionId)
        {
            var height = property.RowHeight(compact);
            rows.Add(new(property, cursor, height, depth, sectionId));
            anchors[property.Id] = new(cursor + height / 2, false, string.Empty);
            cursor += height + PropertyGap;
        }

        IEnumerable<string> DescendantPropertyIds(string sectionId)
        {
            foreach (var property in bySection.GetValueOrDefault(sectionId) ?? []) yield return property.Id;
            foreach (var child in children.GetValueOrDefault(sectionId) ?? [])
                foreach (var propertyId in DescendantPropertyIds(child.Id)) yield return propertyId;
        }

        void AddSection(Section section, int depth)
        {
            var start = cursor;
            var isCollapsed = collapsed.Contains(section.Id);
            cursor += SectionHeaderHeight + PropertyGap;
            if (isCollapsed)
            {
                foreach (var propertyId in DescendantPropertyIds(section.Id))
                    anchors[propertyId] = new(start + SectionHeaderHeight / 2, true, section.Id);
            }
            else
            {
                foreach (var property in bySection.GetValueOrDefault(section.Id) ?? []) AddRow(property, depth + 1, section.Id);
                foreach (var child in children.GetValueOrDefault(section.Id) ?? []) AddSection(child, depth + 1);
            }
            sectionProjections.Add(new(section, start, depth, isCollapsed));
        }

        foreach (var property in unsectioned) AddRow(property, 0, string.Empty);
        foreach (var section in node.Sections.Where(section => string.IsNullOrWhiteSpace(section.ParentSectionId))) AddSection(section, 0);
        foreach (var property in node.Properties)
            if (!anchors.ContainsKey(property.Id)) anchors[property.Id] = new(NodeHeaderHeight / 2, true, "node");
        return new(progressive, displayMode, compact, rows, sectionProjections, anchors);
    }

    private sealed record Port(string Id, string NodeId, string Anchor, string Direction, string PropertyId, int Order)
    {
        public static Port Parse(JsonElement item) => new(
            S(item, "id"), S(item, "nodeId"), S(item, "anchor"), S(item, "direction", "both"), S(item, "propertyId"), I(item, "order", 0));
    }
    private sealed record Edge(string Id, string SourcePortId, string TargetPortId, string Label, string Connector, string Stroke, string StartMarker, string EndMarker)
    {
        public static Edge Parse(JsonElement item, IReadOnlyDictionary<string, JsonElement> edgeTypes)
        {
            edgeTypes.TryGetValue(S(item, "type"), out var edgeType);
            var overlays = item.TryGetProperty("overlays", out var directOverlays) && directOverlays.ValueKind == JsonValueKind.Array
                ? directOverlays.EnumerateArray()
                : edgeType.ValueKind == JsonValueKind.Object
                    ? Items(edgeType, "overlays")
                    : [];
            var connector = item.TryGetProperty("connector", out var directConnector) && directConnector.ValueKind == JsonValueKind.String
                ? directConnector.GetString() ?? "flowchart"
                : edgeType.ValueKind == JsonValueKind.Object
                    ? S(edgeType, "connector", "flowchart")
                    : "flowchart";
            var stroke = StyleStroke(item, edgeType);
            var resolvedOverlays = overlays.ToArray();
            return new(
                S(item, "id"), S(item, "sourcePortId"), S(item, "targetPortId"), S(item, "label"), connector, stroke,
                MarkerAt(resolvedOverlays, 0), MarkerAt(resolvedOverlays, 1));
        }

        private static string StyleStroke(JsonElement item, JsonElement edgeType)
        {
            if (item.TryGetProperty("style", out var directStyle) && directStyle.ValueKind == JsonValueKind.Object)
            {
                var directStroke = S(directStyle, "stroke");
                if (!string.IsNullOrWhiteSpace(directStroke)) return directStroke;
            }

            if (edgeType.ValueKind == JsonValueKind.Object && edgeType.TryGetProperty("style", out var typeStyle) && typeStyle.ValueKind == JsonValueKind.Object)
            {
                var typeStroke = S(typeStyle, "stroke");
                if (!string.IsNullOrWhiteSpace(typeStroke)) return typeStroke;
            }

            return "#0f766e";
        }
    }
}
