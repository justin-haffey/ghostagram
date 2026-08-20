using Ghostagram.Bridge;

namespace Ghostagram.Server.GraphWorkspaces;

public static class GraphWorkspaceEndpoints
{
    public static RouteGroupBuilder MapGraphWorkspaceApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/api/graph-workspaces");

        group.MapPost("/{workspaceId}", (string workspaceId, IGraphWorkspaceService workspaces) =>
        {
            try
            {
                var snapshot = workspaces.Create(workspaceId);
                return Results.Created($"/api/graph-workspaces/{Uri.EscapeDataString(workspaceId)}", snapshot);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { code = "INVALID_WORKSPACE_ID", message = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { code = "WORKSPACE_CAPACITY_REACHED", message = exception.Message });
            }
        });

        group.MapGet("/{workspaceId}", (string workspaceId, IGraphWorkspaceService workspaces) =>
        {
            try
            {
                return workspaces.TryGetSnapshot(workspaceId, out var snapshot)
                    ? Results.Ok(snapshot)
                    : Results.NotFound(new { code = "WORKSPACE_NOT_FOUND", message = $"Graph workspace '{workspaceId}' was not found." });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { code = "INVALID_WORKSPACE_ID", message = exception.Message });
            }
        });

        group.MapPost("/{workspaceId}/load", async (string workspaceId, IGraphWorkspaceService workspaces, CancellationToken cancellationToken) =>
        {
            try
            {
                var snapshot = await workspaces.LoadAsync(workspaceId, cancellationToken);
                return snapshot is null
                    ? Results.NotFound(new { code = "WORKSPACE_NOT_FOUND", message = $"Persisted graph workspace '{workspaceId}' was not found." })
                    : Results.Ok(snapshot);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { code = "INVALID_WORKSPACE_ID", message = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { code = "WORKSPACE_LOAD_REJECTED", message = exception.Message });
            }
        });

        group.MapPut("/{workspaceId}", async (string workspaceId, IGraphWorkspaceService workspaces, CancellationToken cancellationToken) =>
        {
            try
            {
                var snapshot = await workspaces.SaveAsync(workspaceId, cancellationToken);
                return snapshot is null
                    ? Results.NotFound(new { code = "WORKSPACE_NOT_FOUND", message = $"Graph workspace '{workspaceId}' was not found." })
                    : Results.Ok(snapshot);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { code = "INVALID_WORKSPACE_ID", message = exception.Message });
            }
        });

        group.MapPost("/{workspaceId}/commands", (string workspaceId, GraphDiagramCommand command, IGraphWorkspaceService workspaces) =>
        {
            try
            {
                if (!workspaces.TryApply(workspaceId, command, out var result))
                    return Results.NotFound(new { code = "WORKSPACE_NOT_FOUND", message = $"Graph workspace '{workspaceId}' was not found." });
                return result!.Accepted
                    ? Results.Ok(result)
                    : result.Code is "GRAPH_VERSION_CONFLICT" or "DIAGRAM_REVISION_CONFLICT"
                        ? Results.Conflict(result)
                        : Results.BadRequest(result);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { code = "INVALID_WORKSPACE_ID", message = exception.Message });
            }
        });

        group.MapDelete("/{workspaceId}", async (string workspaceId, IGraphWorkspaceService workspaces, CancellationToken cancellationToken) =>
        {
            try
            {
                return await workspaces.DeleteAsync(workspaceId, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound(new { code = "WORKSPACE_NOT_FOUND", message = $"Graph workspace '{workspaceId}' was not found." });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { code = "INVALID_WORKSPACE_ID", message = exception.Message });
            }
        });

        return group;
    }
}
