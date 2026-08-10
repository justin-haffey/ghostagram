using System.Text.Json;
using System.Text.Json.Nodes;
using Ghostagram.Contracts;
using Ghostagram.Core;

namespace Ghostagram.Server;

/// <summary>
/// The DOM-free authoritative subset of the Ghostagram operation protocol.
/// Rendering-only viewport fit/center requests are intentionally not persisted.
/// </summary>
public static class GhostagramDocumentReducer
{
    private static readonly HashSet<string> Collections = ["nodes", "ports", "edges", "groups", "edgeTypes"];

    public static JsonElement Apply(JsonElement model, IReadOnlyList<GhostagramOperation> operations)
    {
        var root = JsonNode.Parse(model.GetRawText())?.AsObject() ?? throw new DiagramCommandException("INVALID_MODEL", "Document model must be an object.");
        foreach (var operation in operations) ApplyOne(root, operation);
        Validate(root);
        return JsonSerializer.SerializeToElement(root);
    }

    private static void ApplyOne(JsonObject root, GhostagramOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.Type)) throw new DiagramCommandException("INVALID_OPERATION", "Operation type is required.");
        var value = ObjectValue(operation);
        switch (operation.Type)
        {
            case "node.upsert": Upsert(root, "nodes", value); break;
            case "port.upsert": Upsert(root, "ports", value); break;
            case "edge.upsert": Upsert(root, "edges", value); break;
            case "group.upsert": Upsert(root, "groups", value); break;
            case "edgeType.upsert": Upsert(root, "edgeTypes", value); break;
            case "node.remove": Remove(root, "nodes", Id(operation, value)); break;
            case "port.remove": Remove(root, "ports", Id(operation, value)); break;
            case "edge.remove": Remove(root, "edges", Id(operation, value)); break;
            case "group.remove": RemoveGroup(root, Id(operation, value)); break;
            case "edgeType.remove": Remove(root, "edgeTypes", Id(operation, value)); break;
            case "group.assignNode": Assign(root, "nodes", String(value, "nodeId"), "groupId", value["groupId"]?.DeepClone()); break;
            case "group.assignGroup": Assign(root, "groups", String(value, "groupId"), "parentGroupId", value["parentGroupId"]?.DeepClone()); break;
            case "selection.replace": root["selection"] = value["ids"]?.DeepClone() ?? new JsonArray(); break;
            case "viewport.set": root["viewport"] = Merge(root["viewport"] as JsonObject ?? new JsonObject(), value); break;
            default: throw new DiagramCommandException("CAPABILITY_UNSUPPORTED", $"Operation '{operation.Type}' is not supported by the server reducer.");
        }
    }

    private static JsonObject ObjectValue(GhostagramOperation operation)
    {
        if (operation.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || operation.Value.ValueKind != JsonValueKind.Object)
            throw new DiagramCommandException("INVALID_OPERATION", $"Operation '{operation.Type}' requires an object value.");
        return JsonNode.Parse(operation.Value.GetRawText())!.AsObject();
    }

    private static void Upsert(JsonObject root, string name, JsonObject value)
    {
        var id = String(value, "id");
        var items = Collection(root, name);
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is JsonObject existing && IdOf(existing) == id) { items[index] = Merge(existing, value); return; }
        }
        items.Add(value.DeepClone());
    }

    private static void Remove(JsonObject root, string name, string id)
    {
        var items = Collection(root, name);
        for (var index = items.Count - 1; index >= 0; index--) if (items[index] is JsonObject item && IdOf(item) == id) items.RemoveAt(index);
    }

    private static void RemoveGroup(JsonObject root, string id)
    {
        foreach (var node in Collection(root, "nodes").OfType<JsonObject>())
            if (OptionalString(node, "groupId") == id) node["groupId"] = null;
        foreach (var group in Collection(root, "groups").OfType<JsonObject>())
            if (OptionalString(group, "parentGroupId") == id) group["parentGroupId"] = null;
        Remove(root, "groups", id);
    }

    private static void Assign(JsonObject root, string name, string id, string property, JsonNode? value)
    {
        var item = Collection(root, name).OfType<JsonObject>().FirstOrDefault(candidate => IdOf(candidate) == id)
            ?? throw new DiagramCommandException("MISSING_REFERENCE", $"'{id}' does not exist.");
        item[property] = value;
    }

    private static void Validate(JsonObject root)
    {
        foreach (var name in Collections) Collection(root, name);
        var nodes = Collection(root, "nodes").OfType<JsonObject>().ToArray();
        var nodeIds = Ids(Collection(root, "nodes"), "node");
        var propertyIdsByNode = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var propertyIds = new HashSet<string>(StringComparer.Ordinal);
            if (node["properties"] is not null and not JsonArray)
                throw new DiagramCommandException("INVALID_MODEL", $"Node '{IdOf(node)}' properties must be an array.");
            foreach (var property in (node["properties"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var propertyId = IdOf(property, "node property");
                if (!propertyIds.Add(propertyId))
                    throw new DiagramCommandException("DUPLICATE_ID", $"Node '{IdOf(node)}' contains duplicate property id '{propertyId}'.");
            }
            ValidateProgressiveNode(node);
            propertyIdsByNode[IdOf(node)] = propertyIds;
        }
        var portIds = Ids(Collection(root, "ports"), "port");
        var groupIds = Ids(Collection(root, "groups"), "group");
        var edgeTypeIds = Ids(Collection(root, "edgeTypes"), "edge type");
        foreach (var port in Collection(root, "ports").OfType<JsonObject>())
        {
            var nodeId = String(port, "nodeId");
            if (!nodeIds.Contains(nodeId)) throw new DiagramCommandException("MISSING_REFERENCE", $"Port '{IdOf(port)}' references a missing node.");
            if (OptionalString(port, "propertyId") is { } propertyId && !propertyIdsByNode[nodeId].Contains(propertyId))
                throw new DiagramCommandException("MISSING_REFERENCE", $"Port '{IdOf(port)}' references missing property '{propertyId}' on node '{nodeId}'.");
        }
        foreach (var node in nodes) if (OptionalString(node, "groupId") is { } groupId && !groupIds.Contains(groupId)) throw new DiagramCommandException("MISSING_REFERENCE", $"Node '{IdOf(node)}' references a missing group.");
        foreach (var group in Collection(root, "groups").OfType<JsonObject>()) if (OptionalString(group, "parentGroupId") is { } parent && !groupIds.Contains(parent)) throw new DiagramCommandException("MISSING_REFERENCE", $"Group '{IdOf(group)}' references a missing parent group.");
        ValidateGroupCycles(Collection(root, "groups").OfType<JsonObject>());
        foreach (var edge in Collection(root, "edges").OfType<JsonObject>())
        {
            if (!portIds.Contains(String(edge, "sourcePortId")) || !portIds.Contains(String(edge, "targetPortId"))) throw new DiagramCommandException("MISSING_REFERENCE", $"Edge '{IdOf(edge)}' references a missing port.");
            if (OptionalString(edge, "type") is { } type && !edgeTypeIds.Contains(type)) throw new DiagramCommandException("MISSING_REFERENCE", $"Edge '{IdOf(edge)}' references a missing edge type.");
        }
    }

    private static JsonArray Collection(JsonObject root, string name)
    {
        if (root[name] is JsonArray items) return items;
        items = new JsonArray();
        root[name] = items;
        return items;
    }
    private static HashSet<string> Ids(JsonArray items, string kind)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.OfType<JsonObject>())
        {
            var id = IdOf(item, kind);
            if (!ids.Add(id)) throw new DiagramCommandException("DUPLICATE_ID", $"Duplicate {kind} id '{id}'.");
        }
        return ids;
    }

    private static void ValidateGroupCycles(IEnumerable<JsonObject> groups)
    {
        var parentById = groups.ToDictionary(group => IdOf(group), group => OptionalString(group, "parentGroupId"), StringComparer.Ordinal);
        foreach (var start in parentById.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (parentById.TryGetValue(current, out var parent) && parent is not null)
            {
                if (!visited.Add(current))
                    throw new DiagramCommandException("INVALID_GROUP_HIERARCHY", $"Group hierarchy contains a cycle involving '{current}'.");
                current = parent;
            }
        }
    }

    private static void ValidateProgressiveNode(JsonObject node)
    {
        var nodeId = IdOf(node);
        if (node["sections"] is not null and not JsonArray)
            throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' sections must be an array.");

        var sections = (node["sections"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var sectionsById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            var sectionId = IdOf(section, "node section");
            if (!sectionsById.TryAdd(sectionId, section))
                throw new DiagramCommandException("DUPLICATE_ID", $"Node '{nodeId}' contains duplicate section id '{sectionId}'.");
            String(section, "title", "node section");
            if (section["order"] is JsonValue orderValue && (!orderValue.TryGetValue<int>(out var order) || order < 0))
                throw new DiagramCommandException("INVALID_MODEL", $"Node section '{sectionId}' has an invalid order.");
            if (section["collapsible"] is JsonValue collapsibleValue && !collapsibleValue.TryGetValue<bool>(out _))
                throw new DiagramCommandException("INVALID_MODEL", $"Node section '{sectionId}' has an invalid collapsible value.");
        }

        foreach (var section in sections)
        {
            var sectionId = IdOf(section, "node section");
            if (OptionalString(section, "parentSectionId") is { } immediateParentId && !sectionsById.ContainsKey(immediateParentId))
                throw new DiagramCommandException("MISSING_REFERENCE", $"Node section '{sectionId}' references missing parent section '{immediateParentId}'.");

            var visited = new HashSet<string>(StringComparer.Ordinal) { sectionId };
            var current = section;
            var depth = 1;
            while (OptionalString(current, "parentSectionId") is { } parentId)
            {
                if (!visited.Add(parentId))
                    throw new DiagramCommandException("INVALID_SECTION_HIERARCHY", $"Node '{nodeId}' section hierarchy contains a cycle involving '{parentId}'.");
                if (++depth > 8)
                    throw new DiagramCommandException("INVALID_SECTION_HIERARCHY", $"Node '{nodeId}' section '{sectionId}' exceeds the maximum nesting depth of 8.");
                current = sectionsById[parentId];
            }
        }

        foreach (var property in (node["properties"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var propertyId = IdOf(property, "node property");
            if (OptionalString(property, "sectionId") is { } sectionId && !sectionsById.ContainsKey(sectionId))
                throw new DiagramCommandException("MISSING_REFERENCE", $"Node property '{propertyId}' references missing section '{sectionId}'.");
            ValidatePropertyEditor(property, propertyId);
        }

        if (node["presentation"] is null) return;
        if (node["presentation"] is not JsonObject presentation)
            throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' presentation must be an object.");
        var displayMode = OptionalString(presentation, "displayMode") ?? DiagramNodeDisplayModes.Expanded;
        if (displayMode is not (DiagramNodeDisplayModes.Expanded or DiagramNodeDisplayModes.Compact or DiagramNodeDisplayModes.Collapsed))
            throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' has unsupported display mode '{displayMode}'.");
        if (presentation["expandedHeight"] is JsonValue heightValue &&
            (!heightValue.TryGetValue<double>(out var expandedHeight) || !double.IsFinite(expandedHeight) || expandedHeight <= 0))
            throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' expanded height must be finite and positive.");
        if (presentation["collapsedSectionIds"] is not null and not JsonArray)
            throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' collapsed section ids must be an array.");
        var collapsedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in presentation["collapsedSectionIds"] as JsonArray ?? [])
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var collapsedId) || string.IsNullOrWhiteSpace(collapsedId))
                throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' collapsed section ids must contain non-empty strings.");
            if (!collapsedIds.Add(collapsedId))
                throw new DiagramCommandException("DUPLICATE_ID", $"Node '{nodeId}' contains duplicate collapsed section id '{collapsedId}'.");
            if (!sectionsById.TryGetValue(collapsedId, out var section))
                throw new DiagramCommandException("MISSING_REFERENCE", $"Node '{nodeId}' presentation references missing collapsed section '{collapsedId}'.");
            if (section["collapsible"] is JsonValue collapsible && collapsible.TryGetValue<bool>(out var canCollapse) && !canCollapse)
                throw new DiagramCommandException("INVALID_MODEL", $"Node '{nodeId}' presentation collapses non-collapsible section '{collapsedId}'.");
        }
    }

    private static void ValidatePropertyEditor(JsonObject property, string propertyId)
    {
        if (property["editor"] is null) return;
        if (property["editor"] is not JsonObject editor)
            throw new DiagramCommandException("INVALID_MODEL", $"Node property '{propertyId}' editor must be an object.");
        var kind = OptionalString(editor, "kind") ?? DiagramPropertyEditorKinds.Auto;
        var type = OptionalString(property, "type") ?? DiagramPropertyTypes.String;
        var compatible = kind switch
        {
            DiagramPropertyEditorKinds.Auto => true,
            DiagramPropertyEditorKinds.Text or DiagramPropertyEditorKinds.Multiline or DiagramPropertyEditorKinds.Color => type == DiagramPropertyTypes.String,
            DiagramPropertyEditorKinds.Toggle => type == DiagramPropertyTypes.Boolean,
            DiagramPropertyEditorKinds.Number or DiagramPropertyEditorKinds.Range => type is DiagramPropertyTypes.Integer or DiagramPropertyTypes.Decimal or "number",
            DiagramPropertyEditorKinds.Date => type == DiagramPropertyTypes.Date,
            DiagramPropertyEditorKinds.DateTime => type is DiagramPropertyTypes.DateTime or "datetime",
            DiagramPropertyEditorKinds.Select => type == DiagramPropertyTypes.Enum,
            DiagramPropertyEditorKinds.Json => type == DiagramPropertyTypes.Json,
            _ => false
        };
        if (!compatible)
            throw new DiagramCommandException("INVALID_MODEL", $"Editor '{kind}' is incompatible with node property '{propertyId}' of type '{type}'.");

        var minimum = OptionalFiniteNumber(editor, "minimum", propertyId);
        var maximum = OptionalFiniteNumber(editor, "maximum", propertyId);
        var step = OptionalFiniteNumber(editor, "step", propertyId);
        if (minimum is { } min && maximum is { } max && min > max)
            throw new DiagramCommandException("INVALID_MODEL", $"Node property '{propertyId}' editor minimum exceeds its maximum.");
        if (step is <= 0)
            throw new DiagramCommandException("INVALID_MODEL", $"Node property '{propertyId}' editor step must be positive.");
        if (kind is not (DiagramPropertyEditorKinds.Number or DiagramPropertyEditorKinds.Range) &&
            (minimum is not null || maximum is not null || step is not null))
            throw new DiagramCommandException("INVALID_MODEL", $"Editor '{kind}' for node property '{propertyId}' cannot define numeric bounds.");
    }

    private static double? OptionalFiniteNumber(JsonObject item, string key, string propertyId)
    {
        if (item[key] is null) return null;
        if (item[key] is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number))
            throw new DiagramCommandException("INVALID_MODEL", $"Node property '{propertyId}' editor {key} must be a finite number.");
        return number;
    }
    private static string Id(GhostagramOperation operation, JsonObject value) => operation.Id ?? IdOf(value);
    private static string IdOf(JsonObject item, string kind = "item") => String(item, "id", kind);
    private static string String(JsonObject item, string key, string kind = "item") => OptionalString(item, key) ?? throw new DiagramCommandException("INVALID_MODEL", $"{kind} requires a string '{key}'.");
    private static string? OptionalString(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
    private static JsonObject Merge(JsonObject existing, JsonObject update) { var merged = existing.DeepClone().AsObject(); foreach (var pair in update) merged[pair.Key] = pair.Value?.DeepClone(); return merged; }
}

public sealed class DiagramCommandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
