# Implementation Stages

## Stage 0

Lock the new product center:

- recipe catalog
- run orchestration
- structured run result
- terminal as a secondary tool

Exit criteria:

- all new work maps to `RecipeDefinition` or `RecipeRun`
- no new feature starts from a raw terminal-first entry

## Stage 1

Build backend domain and persistence first.

Deliverables:

- recipe repository
- run repository
- run lifecycle service
- node routing abstraction

Exit criteria:

- one recipe can be executed once on a chosen node
- run state can be queried without opening a terminal

## Stage 2

Build the new web workflow.

Deliverables:

- recipes list
- run list
- run detail
- recipe editor

Exit criteria:

- main path is `pick recipe -> pick node -> run -> inspect result`

## Stage 3

Attach secondary tools.

Deliverables:

- terminal attach from run detail
- file workspace as recipe/result helper
- legacy entry links

Exit criteria:

- terminal and file tools are reachable, but not the default center

## Stage 4

Legacy demotion.

Deliverables:

- old entry points marked as legacy
- migration notes for operators

Exit criteria:

- new users land in the recipe/run workflow by default

