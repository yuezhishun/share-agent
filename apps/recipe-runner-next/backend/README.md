# Backend Workspace

This folder will host the new backend implementation for:

- recipe catalog
- run orchestrator
- node routing
- runtime adapters

Suggested build order:

1. repository interfaces
2. domain services
3. API handlers
4. node transport adapters
5. legacy bridges

Do not import legacy terminal-first workflows into the core domain layer.

