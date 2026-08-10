using System.Text.Json;
using Ghostagram.Core;
using Ghostagram.Execution;

namespace Ghostagram.NodeSets.UML;

/// <summary>
/// A provider-neutral structural UML authoring catalog. These descriptors are
/// intentionally visual datatypes and do not register execution handlers.
/// </summary>
public static class UmlNodeSet
{
    public const string Id = "uml-basic";
    public const string DisplayName = "UML";

    private static readonly DiagramNodeStyle ClassStyle = new("#eff6ff", "#2563eb", "#172554", "left");
    private static readonly DiagramNodeStyle AbstractStyle = new("#f5f3ff", "#7c3aed", "#2e1065", "left");
    private static readonly DiagramNodeStyle InterfaceStyle = new("#ecfeff", "#0891b2", "#164e63", "left");
    private static readonly DiagramNodeStyle EnumerationStyle = new("#f0fdf4", "#16a34a", "#14532d", "left");
    private static readonly DiagramNodeStyle DataTypeStyle = new("#fff7ed", "#ea580c", "#7c2d12", "left");
    private static readonly DiagramNodeStyle ObjectStyle = new("#f8fafc", "#475569", "#0f172a", "left");

    public static NodeSetDescriptor Descriptor { get; } = new(
        Id,
        DisplayName,
        [
            Class(1), AbstractClass(1), Interface(1), Enumeration(1), DataType(1), Object(1),
            Class(2), AbstractClass(2), Interface(2), Enumeration(2), DataType(2), Object(2)
        ],
        "Basic structural UML elements for class and domain-model diagrams.");

    private static NodeTypeDescriptor Class(int version) => new(
        "uml.class", version, "Class", DisplayName, "mdi:code-braces-box", version == 1 ? 260 : 220, version == 1 ? 174 : 124,
        properties:
        [
            Property("stereotype", "Stereotype", "Stereotype"),
            Property("attributes", "Attributes", "Attributes"),
            Property("operations", "Operations", "Operations")
        ],
        ports: RelationshipPorts(version),
        metadata: Metadata("class", "A UML classifier with attribute and operation compartments."),
        style: ClassStyle);

    private static NodeTypeDescriptor AbstractClass(int version) => new(
        "uml.abstract-class", version, "Abstract Class", DisplayName, "mdi:shape-outline", version == 1 ? 260 : 220, version == 1 ? 174 : 124,
        properties:
        [
            Property("stereotype", "Stereotype", "Stereotype", "\u00ABabstract\u00BB"),
            Property("attributes", "Attributes", "Attributes"),
            Property("operations", "Operations", "Operations")
        ],
        ports: RelationshipPorts(version),
        metadata: Metadata("abstractClass", "A non-instantiable UML classifier intended as a generalization target."),
        style: AbstractStyle);

    private static NodeTypeDescriptor Interface(int version) => new(
        "uml.interface", version, "Interface", DisplayName, "mdi:lan-connect", version == 1 ? 260 : 220, version == 1 ? 153 : 106,
        properties:
        [
            Property("stereotype", "Stereotype", "Stereotype", "\u00ABinterface\u00BB"),
            Property("operations", "Operations", "Operations")
        ],
        ports: RelationshipPorts(version),
        metadata: Metadata("interface", "A UML interface that declares operations supplied by realizing classifiers."),
        style: InterfaceStyle);

    private static NodeTypeDescriptor Enumeration(int version) => new(
        "uml.enumeration", version, "Enumeration", DisplayName, "mdi:format-list-bulleted-square", version == 1 ? 250 : 220, version == 1 ? 153 : 106,
        properties:
        [
            Property("stereotype", "Stereotype", "Stereotype", "\u00ABenumeration\u00BB"),
            Property("literals", "Literals", "Literals")
        ],
        ports: RelationshipPorts(version),
        metadata: Metadata("enumeration", "A UML enumeration whose values are listed as literals."),
        style: EnumerationStyle);

    private static NodeTypeDescriptor DataType(int version) => new(
        "uml.data-type", version, "Data Type", DisplayName, "mdi:database-outline", version == 1 ? 260 : 220, version == 1 ? 174 : 124,
        properties:
        [
            Property("stereotype", "Stereotype", "Stereotype", "\u00ABdataType\u00BB"),
            Property("fields", "Fields", "Fields"),
            Property("constraints", "Constraints", "Constraints")
        ],
        ports: RelationshipPorts(version),
        metadata: Metadata("dataType", "A value-oriented UML data type with fields and optional constraints."),
        style: DataTypeStyle);

    private static NodeTypeDescriptor Object(int version) => new(
        "uml.object", version, "Object", DisplayName, "mdi:cube-outline", version == 1 ? 250 : 220, version == 1 ? 153 : 106,
        properties:
        [
            Property("classifier", "Classifier", "Classifier"),
            Property("slots", "Slots", "Slots")
        ],
        ports: RelationshipPorts(version),
        metadata: Metadata("object", "An object-instance snapshot with a classifier and slot values."),
        style: ObjectStyle);

    private static NodePropertyDefinition Property(string id, string name, string label, string? defaultValue = null) =>
        new(id, name, DiagramPropertyTypes.String, DiagramPropertyModes.DisplayAndEdit, label,
            Connectable: false, DefaultValue: defaultValue is null ? null : JsonSerializer.SerializeToElement(defaultValue));

    private static NodePortDefinition[] RelationshipPorts(int version) => version == 1
        ?
        [
            new("relationships-in", "target", "uml.relationship", Label: "Relations", Order: 0),
            new("relationships-out", "source", "uml.relationship", Label: "Relations", Order: 1)
        ]
        :
        [
            new("relationships-top", "both", "uml.relationship", Label: "Relations", Order: 0, Anchor: "top"),
            new("relationships-right", "both", "uml.relationship", Label: "Relations", Order: 1, Anchor: "right"),
            new("relationships-bottom", "both", "uml.relationship", Label: "Relations", Order: 2, Anchor: "bottom"),
            new("relationships-left", "both", "uml.relationship", Label: "Relations", Order: 3, Anchor: "left")
        ];

    private static IReadOnlyDictionary<string, JsonElement> Metadata(string kind, string description) =>
        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["umlKind"] = JsonSerializer.SerializeToElement(kind),
            ["description"] = JsonSerializer.SerializeToElement(description)
        };
}
