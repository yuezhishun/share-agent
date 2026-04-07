# pty-agent

`pty-agent` 是一个多应用仓库，当前包含两部分：

1. `terminal-gateway-dotnet`：基于 .NET、Porta.Pty 和 SignalR 的终端网关，提供终端、进程、文件与集群节点能力。
2. `secretary-web`：基于 Vue 3、Vite 和 xterm.js 的桌面控制台。

已移除的 `recipe-runner-next` 不再属于当前发布内容。

## 目录结构

- `apps/terminal-gateway-dotnet/TerminalGateway.Api`：网关主服务
- `apps/terminal-gateway-dotnet/TerminalGateway.Api.Tests`：xUnit 测试
- `apps/secretary-web`：前端控制台与 Playwright E2E
- `deploy`：按场景组织的部署入口、验证脚本和 Docker 联调配置
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

默认前端通过 `/api/*`、`/hubs/terminal` 和 `/hubs/cluster` 访问网关。

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
cd deploy/docker
docker compose up --build
./smoke.sh
```

## 部署脚本

- `deploy/single-master`：单机 master 的前端构建、systemd、Nginx 和验证入口
- `deploy/cluster-lan`：公网 master + 局域网 slave 的部署入口
- `deploy/cluster-examples`：仅用于本机快速启动 cluster 示例
- `deploy/docker`：Docker Compose 联调环境

建议先看：

- `deploy/README.md`
- `docs/terminal-gateway-deploy.md`

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
