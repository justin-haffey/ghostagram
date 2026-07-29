using System.Globalization;
using System.Security;
using System.Text;
using Diagrams.Core.Models;
using Diagrams.Core.Routing;

namespace Diagrams.Core.Exports;

public sealed class SvgExportRenderer
{
    public string Render(DiagramDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{Format(document.Canvas.Width)}" height="{Format(document.Canvas.Height)}" viewBox="0 0 {Format(document.Canvas.Width)} {Format(document.Canvas.Height)}">""");
        builder.AppendLine("<defs>");
        builder.AppendLine("""<marker id="arrow" markerWidth="12" markerHeight="12" refX="10" refY="6" orient="auto"><path d="M0,0 L12,6 L0,12 z" fill="#1f827b" /></marker>""");
        builder.AppendLine("""<marker id="triangle" markerWidth="12" markerHeight="12" refX="10" refY="6" orient="auto"><path d="M0,6 L12,0 L12,12 z" fill="#1f827b" /></marker>""");
        builder.AppendLine("""<marker id="diamond" markerWidth="14" markerHeight="14" refX="12" refY="7" orient="auto"><path d="M0,7 L7,0 L14,7 L7,14 z" fill="#1f827b" /></marker>""");
        builder.AppendLine("""<marker id="hollow-diamond" markerWidth="14" markerHeight="14" refX="12" refY="7" orient="auto"><path d="M0,7 L7,0 L14,7 L7,14 z" fill="#faf9f5" stroke="#1f827b" /></marker>""");
        builder.AppendLine("</defs>");

        foreach (var group in document.Groups)
        {
            builder.AppendLine($"""<rect x="{Format(group.Bounds.X)}" y="{Format(group.Bounds.Y)}" width="{Format(group.Bounds.Width)}" height="{Format(group.Bounds.Height)}" rx="20" fill="rgba(44,168,160,0.05)" stroke="#1f827b" stroke-dasharray="10 10" stroke-width="2" />""");
            builder.AppendLine($"""<text x="{Format(group.Bounds.X + 20)}" y="{Format(group.Bounds.Y + 28)}" font-family="Manrope, sans-serif" font-size="18" fill="#1f2530">{Escape(group.Label)}</text>""");
        }

        foreach (var edge in document.Edges)
        {
            var sourcePort = document.FindPort(edge.SourcePortId);
            var targetPort = document.FindPort(edge.TargetPortId);
            if (sourcePort is null || targetPort is null)
            {
                continue;
            }

            var source = document.GetPortBounds(sourcePort);
            var target = document.GetPortBounds(targetPort);
            var style = document.Resolve(edge.StyleToken);
            var routedPoints = EdgeRouteResolver.ResolveVisibleRoutePoints(document, edge);
            var path = routedPoints.Length > 1
                ? BuildPolylinePath(routedPoints)
                : edge.ConnectorKind switch
                {
                    ConnectorKind.Bezier => $"""M {Format(source.CenterX)} {Format(source.CenterY)} C {Format(source.CenterX + 120)} {Format(source.CenterY)}, {Format(target.CenterX - 120)} {Format(target.CenterY)}, {Format(target.CenterX)} {Format(target.CenterY)}""",
                    ConnectorKind.StateMachine => $"""M {Format(source.CenterX)} {Format(source.CenterY)} C {Format(source.CenterX + 120)} {Format(source.CenterY - 80)}, {Format(target.CenterX - 120)} {Format(target.CenterY + 80)}, {Format(target.CenterX)} {Format(target.CenterY)}""",
                    ConnectorKind.Flowchart => $"""M {Format(source.CenterX)} {Format(source.CenterY)} L {Format((source.CenterX + target.CenterX) / 2)} {Format(source.CenterY)} L {Format((source.CenterX + target.CenterX) / 2)} {Format(target.CenterY)} L {Format(target.CenterX)} {Format(target.CenterY)}""",
                    _ => $"""M {Format(source.CenterX)} {Format(source.CenterY)} L {Format(target.CenterX)} {Format(target.CenterY)}"""
                };

            builder.Append($"""<path d="{path}" fill="none" stroke="{style.Edge}" stroke-width="3" """);
            var targetMarker = ResolveMarker(edge.Markers.Target);
            if (targetMarker is not null)
            {
                builder.Append($"""marker-end="url(#{targetMarker})" """);
            }

            var sourceMarker = ResolveMarker(edge.Markers.Source);
            if (sourceMarker is not null)
            {
                builder.Append($"""marker-start="url(#{sourceMarker})" """);
            }

            builder.AppendLine("/>");

            var routedMidpoint = EdgeRouteResolver.ResolvePolylineMidpoint(routedPoints);
            var labelX = routedMidpoint?.X ?? ((source.CenterX + target.CenterX) / 2);
            var labelY = (routedMidpoint?.Y ?? ((source.CenterY + target.CenterY) / 2)) - 10;
            builder.AppendLine($"""<text x="{Format(labelX)}" y="{Format(labelY)}" text-anchor="middle" font-family="IBM Plex Mono, monospace" font-size="14" fill="#1f2530">{Escape(edge.Label)}</text>""");
        }

        foreach (var node in document.Nodes)
        {
            var style = document.Resolve(node.StyleToken);
            builder.AppendLine($"""<rect x="{Format(node.Bounds.X)}" y="{Format(node.Bounds.Y)}" width="{Format(node.Bounds.Width)}" height="{Format(node.Bounds.Height)}" rx="18" fill="{style.Fill}" stroke="{style.Border}" stroke-width="2.5" />""");
            builder.AppendLine($"""<text x="{Format(node.Bounds.X + 18)}" y="{Format(node.Bounds.Y + 30)}" font-family="Manrope, sans-serif" font-size="18" font-weight="700" fill="{style.Text}">{Escape(node.Label)}</text>""");
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static string BuildPolylinePath(IReadOnlyList<DiagramPoint> points)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            builder.Append(index == 0 ? "M " : " L ");
            builder.Append(Format(point.X));
            builder.Append(' ');
            builder.Append(Format(point.Y));
        }

        return builder.ToString();
    }

    private static string? ResolveMarker(MarkerKind marker) => marker switch
    {
        MarkerKind.Arrow => "arrow",
        MarkerKind.Triangle => "triangle",
        MarkerKind.Diamond => "diamond",
        MarkerKind.HollowDiamond => "hollow-diamond",
        _ => null
    };
}
