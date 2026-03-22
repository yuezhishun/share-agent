# pty-agent

`pty-agent` 是一个多应用仓库，当前包含两部分：

1. `terminal-gateway-dotnet`：基于 .NET、Porta.Pty 和 SignalR 的终端网关，提供终端、进程、文件与集群节点能力。
2. `secretary-web`：基于 Vue 3、Vite 和 xterm.js 的桌面控制台。

已移除的 `recipe-runner-next` 不再属于当前发布内容。

## 目录结构

- `apps/terminal-gateway-dotnet/TerminalGateway.Api`：网关主服务
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`：xUnit 测试
- `apps/secretary-web`：前端控制台与 Playwright E2E
- `deploy`：本地部署、集群部署、验证和 Nginx 示例配置
- `docs`：运行说明、部署说明和接口文档

## 本地开发

### 启动后端网关

```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

默认以 `master` 角色启动，监听 `0.0.0.0:8080`。

### 运行 .NET 测试

```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests/TerminalGateway.Api.Tests.csproj -v minimal
```

如需跑完整解决方案：

```bash
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.sln -v minimal
```

### 启动前端

```bash
cd apps/secretary-web
npm install
npm run dev
```

默认前端通过 `/web-pty/api/*`、`/web-pty/hubs/terminal` 和 `/web-pty/hubs/cluster` 访问网关。

## 构建与验证

### 前端构建

```bash
cd apps/secretary-web
npm run build
```

### Playwright E2E

首次运行前建议先安装浏览器：

```bash
cd apps/secretary-web
npm run test:e2e:install
```

常用命令：

```bash
cd apps/secretary-web
npm run test:e2e
npm run test:e2e:integration
npm run test:e2e:cluster
```

### Docker Compose 与部署验证

```bash
cd deploy
docker compose up --build
./smoke.sh
```

## 部署脚本

- `deploy/release-local.sh`：单机 master 发布
- `deploy/release-frontend-local.sh`：单机前端单独发布
- `deploy/release-cluster-local.sh`：本地 master/slave 集群发布
- `deploy/release-cluster-frontend-local.sh`：集群前端单独发布
- `deploy/verify-local.sh`：单机部署验证
- `deploy/verify-cluster-local.sh`：集群部署验证
- `deploy/nginx.conf`：单机 Nginx 示例
- `deploy/nginx-cluster-master.conf.example`：集群 master Nginx 示例
- `deploy/nginx-cluster-slave.conf.example`：集群 slave Nginx 示例

## 清理构建产物

```bash
./clean.sh
./clean.sh --dry-run
```

默认会清理 `bin`、`obj`、`node_modules`、`dist`、`coverage`、`TestResults`、`test-results`、`playwright-report`、`.vite`、`.turbo` 等构建目录。

## 文档

- `docs/terminal-gateway-dotnet.md`：网关运行方式、REST API 与 SignalR Hub
- `docs/terminal-gateway-deploy.md`：单机与集群部署说明
- `docs/terminal-gateway-processes.md`：进程能力说明
- `docs/nginx-config-paths.md`：Nginx 配置路径参考
- `docs/Porta.Pty使用说明.md`：Porta.Pty 参考文档
