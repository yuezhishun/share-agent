# Decision Log

## Fixed Decisions

### Product center

- The main object is `RecipeRun`.
- The main user journey is `recipe -> execution -> result`.

### Topology

- Product model is peer nodes.
- Existing master/slave transport may remain as a compatibility implementation.

### Storage

- Recipes are stored per node.
- There is no global master-owned recipe catalog in the first version.

### Execution

- Default execution mode is one-shot targeted dispatch.
- Default runner is a managed job, not an interactive terminal.

### UI

- Terminal is secondary.
- File editing stays, but as a helper workspace.
- Shortcuts are folded into the recipe system instead of staying a separate primary model.

## Change Rule

Any future change that breaks one of the fixed decisions must update:

- `REFACTOR_BLUEPRINT.md`
- this file

before implementation starts.

