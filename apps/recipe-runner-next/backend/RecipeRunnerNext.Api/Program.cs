using RecipeRunnerNext.Api.Models;
using RecipeRunnerNext.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RecipeCatalogService>();
builder.Services.AddSingleton<RecipeRunService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    name = "recipe-runner-next-api",
    version = 1,
    status = "ok"
}));

app.MapGet("/api/v3/nodes/{nodeId}/recipes", async (string nodeId, RecipeCatalogService recipes, CancellationToken ct) =>
{
    var items = await recipes.ListAsync(nodeId, ct);
    return Results.Ok(new { items });
});

app.MapPost("/api/v3/nodes/{nodeId}/recipes", async (string nodeId, CreateRecipeRequest request, RecipeCatalogService recipes, CancellationToken ct) =>
{
    var created = await recipes.CreateAsync(nodeId, request, ct);
    return Results.Created($"/api/v3/nodes/{Uri.EscapeDataString(nodeId)}/recipes/{Uri.EscapeDataString(created.RecipeId)}", created);
});

app.MapPut("/api/v3/nodes/{nodeId}/recipes/{recipeId}", async (string nodeId, string recipeId, UpdateRecipeRequest request, RecipeCatalogService recipes, CancellationToken ct) =>
{
    var updated = await recipes.UpdateAsync(nodeId, recipeId, request, ct);
    return updated is null ? Results.NotFound(new { error = "recipe not found" }) : Results.Ok(updated);
});

app.MapDelete("/api/v3/nodes/{nodeId}/recipes/{recipeId}", async (string nodeId, string recipeId, RecipeCatalogService recipes, CancellationToken ct) =>
{
    var deleted = await recipes.DeleteAsync(nodeId, recipeId, ct);
    return deleted ? Results.NoContent() : Results.NotFound(new { error = "recipe not found" });
});

app.MapPost("/api/v3/runs", async (CreateRunRequest request, RecipeRunService runs, CancellationToken ct) =>
{
    var created = await runs.CreateAsync(request, ct);
    return Results.Created($"/api/v3/runs/{Uri.EscapeDataString(created.RunId)}", created);
});

app.MapGet("/api/v3/runs", (RecipeRunService runs, string? target_node_id, string? status) =>
{
    var items = runs.List(target_node_id, status);
    return Results.Ok(new { items });
});

app.MapGet("/api/v3/runs/{runId}", (string runId, RecipeRunService runs) =>
{
    var item = runs.Get(runId);
    return item is null ? Results.NotFound(new { error = "run not found" }) : Results.Ok(item);
});

app.MapGet("/api/v3/runs/{runId}/output", (string runId, RecipeRunService runs) =>
{
    var item = runs.Get(runId);
    return item is null
        ? Results.NotFound(new { error = "run not found" })
        : Results.Ok(new
        {
            run_id = item.RunId,
            stdout = item.Stdout,
            stderr = item.Stderr
        });
});

app.MapPost("/api/v3/runs/{runId}/cancel", async (string runId, RecipeRunService runs, CancellationToken ct) =>
{
    var item = await runs.CancelAsync(runId, ct);
    return item is null ? Results.NotFound(new { error = "run not found" }) : Results.Ok(item);
});

app.MapGet("/api/v3/runs/{runId}/terminal", (string runId, RecipeRunService runs) =>
{
    var item = runs.Get(runId);
    if (item is null)
    {
        return Results.NotFound(new { error = "run not found" });
    }

    if (item.RuntimeRef is null || !string.Equals(item.RuntimeRef.Kind, "terminal", StringComparison.Ordinal))
    {
        return Results.NotFound(new { error = "run has no attached terminal" });
    }

    return Results.Ok(new
    {
        run_id = item.RunId,
        runtime = item.RuntimeRef
    });
});

app.Run();

