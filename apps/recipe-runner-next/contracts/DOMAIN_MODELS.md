# Domain Models

## RecipeDefinition

Purpose:

- node-local executable definition

Fields:

- `recipe_id`
- `node_id`
- `name`
- `group`
- `cwd`
- `command`
- `args`
- `env`
- `default_runner`
- `created_at`
- `updated_at`

Rules:

- `recipe_id` is unique within the node catalog
- `default_runner` is `managed_job` or `interactive_terminal`
- `command` is required

## RecipeRun

Purpose:

- one execution record produced from a recipe

Fields:

- `run_id`
- `recipe_id`
- `target_node_id`
- `source_node_id`
- `trigger_source`
- `status`
- `started_at`
- `finished_at`
- `exit_code`
- `runner_type`
- `runtime_ref`
- `stdout_summary`
- `stderr_summary`
- `artifacts`
- `error`

Rules:

- one run points to one target node
- `status` should support at least `queued`, `running`, `succeeded`, `failed`, `cancelled`
- `runtime_ref` is optional and only exists when the run binds to a concrete runtime

## RuntimeRef

Purpose:

- reference to the execution carrier

Fields:

- `kind`
- `id`
- `node_id`

Rules:

- `kind` is `process` or `terminal`
- process is the default carrier
- terminal is allowed when the recipe or debug flow explicitly requires it

