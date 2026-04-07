# Cluster Startup Scripts

The single source of truth for cluster configuration is now the `TerminalGateway.Api` project:

- `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.ClusterWindowsMaster.json`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.ClusterWindowsSlaveLocal.json`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.ClusterWindowsSlaveCloud.json`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.ClusterLinuxMaster.json`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.ClusterLinuxSlaveLocal.json`
- `apps/terminal-gateway-dotnet/TerminalGateway.Api/appsettings.ClusterLinuxSlaveCloud.json`

This folder only keeps startup scripts.

Scripts:

- `start-master.ps1`
- `start-master.sh`
- `start-slave-cloud.ps1`
- `start-slave-cloud.sh`
- `start-master-slave.ps1`
- `start-master-slave.sh`

Usage notes:

- Scripts select a fixed ASP.NET Core environment and start the API project.
- Do not maintain cluster JSON in this folder anymore.
- Keep path, role, and allowed-root changes in the API project's `appsettings.Cluster*.json` files.
- Runtime overrides are limited to values such as `CLUSTER_TOKEN` and `MASTER_URL`.
