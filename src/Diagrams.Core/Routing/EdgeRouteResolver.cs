using System.Collections.Immutable;
using Diagrams.Core.Models;

namespace Diagrams.Core.Routing;

public static class EdgeRouteResolver
{
    public static bool SupportsManualWaypoints(DiagramEdge edge)
        => SupportsManualWaypoints(edge.ConnectorKind);

    public static bool SupportsManualWaypoints(ConnectorKind connectorKind)
        => connectorKind is ConnectorKind.Flowchart or ConnectorKind.Straight;

    public static bool HasVisibleManualRoute(DiagramEdge edge)
        => SupportsManualWaypoints(edge) && edge.Waypoints.Length > 0;

    public static ImmutableArray<DiagramPoint> ResolveVisibleRoutePoints(DiagramDocument document, DiagramEdge edge)
        => HasVisibleManualRoute(edge)
            ? BuildPoints(document, edge, includeDefaultConnectorPath: false)
            : [];

    public static ImmutableArray<DiagramPoint> ResolveEditablePolyline(DiagramDocument document, DiagramEdge edge)
        => SupportsManualWaypoints(edge)
            ? BuildPoints(document, edge, includeDefaultConnectorPath: true)
            : [];

    public static ImmutableArray<DiagramPoint> OffsetWaypoints(ImmutableArray<DiagramPoint> waypoints, double deltaX, double deltaY)
        => waypoints.Select(point => new DiagramPoint(point.X + deltaX, point.Y + deltaY)).ToImmutableArray();

    public static DiagramPoint? ResolvePolylineMidpoint(ImmutableArray<DiagramPoint> points)
    {
        if (points.Length == 0)
        {
            return null;
        }

        if (points.Length == 1)
        {
            return points[0];
        }

        var totalLength = 0d;
        for (var index = 0; index < points.Length - 1; index++)
        {
            totalLength += SegmentLength(points[index], points[index + 1]);
        }

        if (totalLength <= 0)
        {
            return points[0];
        }

        var targetLength = totalLength / 2d;
        var traversed = 0d;
        for (var index = 0; index < points.Length - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            var length = SegmentLength(start, end);
            if (length <= 0)
            {
                continue;
            }

            if (traversed + length >= targetLength)
            {
                var distanceIntoSegment = targetLength - traversed;
                var ratio = distanceIntoSegment / length;
                return new DiagramPoint(
                    start.X + ((end.X - start.X) * ratio),
                    start.Y + ((end.Y - start.Y) * ratio));
            }

            traversed += length;
        }

        return points[^1];
    }

    private static ImmutableArray<DiagramPoint> BuildPoints(DiagramDocument document, DiagramEdge edge, bool includeDefaultConnectorPath)
    {
        if (!TryResolveEndpoints(document, edge, out var source, out var target))
        {
            return [];
        }

        if (HasVisibleManualRoute(edge))
        {
            return Normalize([source, .. edge.Waypoints, target]);
        }

        if (!includeDefaultConnectorPath)
        {
            return [];
        }

        return edge.ConnectorKind switch
        {
            ConnectorKind.Flowchart => Normalize(
            [
                source,
                new DiagramPoint((source.X + target.X) / 2d, source.Y),
                new DiagramPoint((source.X + target.X) / 2d, target.Y),
                target
            ]),
            ConnectorKind.Straight => Normalize([source, target]),
            _ => []
        };
    }

    private static bool TryResolveEndpoints(DiagramDocument document, DiagramEdge edge, out DiagramPoint source, out DiagramPoint target)
    {
        var sourcePort = document.FindPort(edge.SourcePortId);
        var targetPort = document.FindPort(edge.TargetPortId);
        if (sourcePort is null || targetPort is null)
        {
            source = new DiagramPoint(0, 0);
            target = new DiagramPoint(0, 0);
            return false;
        }

        var sourceBounds = document.GetPortBounds(sourcePort);
        var targetBounds = document.GetPortBounds(targetPort);
        source = new DiagramPoint(sourceBounds.CenterX, sourceBounds.CenterY);
        target = new DiagramPoint(targetBounds.CenterX, targetBounds.CenterY);
        return true;
    }

    private static ImmutableArray<DiagramPoint> Normalize(ImmutableArray<DiagramPoint> points)
    {
        var builder = ImmutableArray.CreateBuilder<DiagramPoint>(points.Length);
        DiagramPoint? previous = null;
        foreach (var point in points)
        {
            if (previous is not null && previous == point)
            {
                continue;
            }

            builder.Add(point);
            previous = point;
        }

        return builder.ToImmutable();
    }

    private static double SegmentLength(DiagramPoint start, DiagramPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
