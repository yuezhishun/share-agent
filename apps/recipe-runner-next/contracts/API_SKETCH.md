# API Sketch

## Recipes

### `GET /api/v3/nodes/{nodeId}/recipes`

Returns:

- list of `RecipeDefinition`

### `POST /api/v3/nodes/{nodeId}/recipes`

Creates one recipe in the node-local catalog.

### `PUT /api/v3/nodes/{nodeId}/recipes/{recipeId}`

Updates one recipe in the node-local catalog.

### `DELETE /api/v3/nodes/{nodeId}/recipes/{recipeId}`

Deletes one recipe in the node-local catalog.

## Runs

### `POST /api/v3/runs`

Request body:

- `recipe_id`
- `source_node_id`
- `target_node_id`
- `overrides`

Behavior:

- resolve the recipe
- dispatch to the target node
- create and return a `RecipeRun`

### `GET /api/v3/runs`

Returns:

- paged or simple list of recent `RecipeRun`

### `GET /api/v3/runs/{runId}`

Returns:

- one `RecipeRun`

### `POST /api/v3/runs/{runId}/cancel`

Cancels the run if it is still active.

### `GET /api/v3/runs/{runId}/output`

Returns:

- structured output or output chunks for the run

### `GET /api/v3/runs/{runId}/terminal`

Returns:

- terminal attach metadata if the run has an interactive terminal

