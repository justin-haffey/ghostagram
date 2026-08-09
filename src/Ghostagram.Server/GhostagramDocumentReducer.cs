using System.Text.Json;
using System.Text.Json.Nodes;
using Ghostagram.Contracts;

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
