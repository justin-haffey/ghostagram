using System.Text.Json.Serialization;
using Diagrams.Core.Attributes;

namespace Diagrams.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$layout")]
[JsonDerivedType(typeof(FreeformLayoutSpec), "freeform")]
[JsonDerivedType(typeof(HierarchyTopDownLayoutSpec), "hierarchy-top-down")]
[JsonDerivedType(typeof(HierarchyLeftRightLayoutSpec), "hierarchy-left-right")]
[JsonDerivedType(typeof(TreeLayoutSpec), "tree")]
[JsonDerivedType(typeof(GridLayoutSpec), "grid")]
[JsonDerivedType(typeof(RadialLayoutSpec), "radial")]
[JsonDerivedType(typeof(MindMapLayoutSpec), "mind-map")]
public abstract record class LayoutSpec(LayoutKind Kind, double Padding = 48);

[LayoutCapability(LayoutKind.Freeform, "Freeform")]
public sealed record class FreeformLayoutSpec(double Padding = 48) : LayoutSpec(LayoutKind.Freeform, Padding);

[LayoutCapability(LayoutKind.HierarchyTopDown, "Hierarchy Top Down")]
public sealed record class HierarchyTopDownLayoutSpec(
    double HorizontalSpacing = 240,
    double VerticalSpacing = 180,
    double Padding = 80) : LayoutSpec(LayoutKind.HierarchyTopDown, Padding);

[LayoutCapability(LayoutKind.HierarchyLeftRight, "Hierarchy Left Right")]
public sealed record class HierarchyLeftRightLayoutSpec(
    double HorizontalSpacing = 260,
    double VerticalSpacing = 160,
    double Padding = 80) : LayoutSpec(LayoutKind.HierarchyLeftRight, Padding);

[LayoutCapability(LayoutKind.Tree, "Tree")]
public sealed record class TreeLayoutSpec(
    double HorizontalSpacing = 220,
    double VerticalSpacing = 180,
    double Padding = 80) : LayoutSpec(LayoutKind.Tree, Padding);

[LayoutCapability(LayoutKind.Grid, "Grid")]
public sealed record class GridLayoutSpec(
    int Columns = 3,
    double CellWidth = 240,
    double CellHeight = 180,
    double Padding = 72) : LayoutSpec(LayoutKind.Grid, Padding);

[LayoutCapability(LayoutKind.Radial, "Radial", supportsGroups: false)]
public sealed record class RadialLayoutSpec(
    double RadiusStep = 220,
    double Padding = 120) : LayoutSpec(LayoutKind.Radial, Padding);

[LayoutCapability(LayoutKind.MindMap, "Mind Map", supportsGroups: false)]
public sealed record class MindMapLayoutSpec(
    double PrimaryRadius = 280,
    double SecondarySpacing = 180,
    double Padding = 120) : LayoutSpec(LayoutKind.MindMap, Padding);

public static class LayoutSpecDefaults
{
    public static LayoutSpec For(LayoutKind kind) => kind switch
    {
        LayoutKind.Freeform => new FreeformLayoutSpec(),
        LayoutKind.HierarchyTopDown => new HierarchyTopDownLayoutSpec(),
        LayoutKind.HierarchyLeftRight => new HierarchyLeftRightLayoutSpec(),
        LayoutKind.Tree => new TreeLayoutSpec(),
        LayoutKind.Grid => new GridLayoutSpec(),
        LayoutKind.Radial => new RadialLayoutSpec(),
        LayoutKind.MindMap => new MindMapLayoutSpec(),
        _ => new FreeformLayoutSpec()
    };
}

