# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

pty-agent 是一个交互式伪终端 MVP，包含两个核心组件：

1. **Terminal Gateway (.NET)**：基于 .NET + Porta.Pty + SignalR Hub 的后端服务
2. **Web Console (Vue 3)**：基于 Vue 3 + xterm.js 的前端终端界面

## 项目结构

```
apps/
├── terminal-gateway-dotnet/
│   ├── TerminalGateway.Api/          # .NET Web API 项目
│   │   ├── Endpoints/                # API 端点和 SignalR Hubs
│   │   ├── Services/                 # 业务服务（SessionManager、InstanceManager 等）
│   │   ├── ProcessRunner/            # 进程执行库（内置）
│   │   ├── Pty/                      # PTY 引擎封装
│   │   └── native/                   # libporta_pty.so 原生库
│   └── TerminalGateway.Api.Tests/    # xUnit 测试项目
├── secretary-web/                    # Vue 3 前端项目
│   ├── src/
│   │   ├── views/                    # 页面视图
│   │   ├── components/               # 组件
│   │   ├── stores/                   # Pinia 状态管理
│   │   └── composables/              # 组合式函数
│   └── playwright.config.js          # E2E 测试配置
deploy/                               # 部署脚本和配置
docs/                                 # 文档
```

## 常用命令

### .NET Gateway

```bash
# 运行开发服务器（默认监听 0.0.0.0:7300）
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj

# 运行测试
dotnet test apps/terminal-gateway-dotnet/TerminalGateway.sln -v minimal

# 解决方案管理
dotnet sln apps/terminal-gateway-dotnet/TerminalGateway.sln list
```

### 前端 (secretary-web)

```bash
cd apps/secretary-web

# 开发服务器
npm install
npm run dev                 # 默认端口 3000

# 构建
npm run build               # 输出到 dist/

# E2E 测试（需要先构建）
npm run test:e2e:install    # 安装 Playwright Chromium
npm run test:e2e            # 运行 E2E 测试
npm run test:e2e:headed     # 带浏览器界面运行
npm run test:e2e:integration    # 集成测试配置
npm run test:e2e:cluster    # 集群测试配置
```

### 部署脚本

**Linux/macOS (Bash)**

```bash
# 本地部署（systemd + nginx）
sudo ./deploy/release-local.sh          # 完整部署
sudo ./deploy/release-frontend-local.sh # 仅前端

# 集群部署
sudo ./deploy/release-cluster-local.sh
sudo ./deploy/install-cluster-nginx-local.sh

# 验证
./deploy/smoke.sh                       # 冒烟测试（默认 http://127.0.0.1:8080）
./deploy/verify-local.sh

# 清理构建产物
./clean.sh
./clean.sh --dry-run
```

**Windows (PowerShell)**

```powershell
# 集群部署（使用本项目专属 Nginx）
# 详见 deploy/WINDOWS_CLUSTER_GUIDE.md

# 1. 构建并部署前端
.\deploy\release-cluster-frontend-local.ps1

# 2. 启动两个 .NET Gateway（Master + Slave）
.\deploy\start-cluster-gateways.ps1

# 3. 启动 Nginx（在新窗口中）
cd nginx
.\nginx.exe -c conf/nginx.cluster.conf

# 4. 验证部署
.\deploy\verify-cluster-local.ps1
```

**Windows 端口分配**

| 服务 | 端口 | 访问地址 |
|------|------|----------|
| Master Gateway | 7310 | http://127.0.0.1:7310 |
| Slave Gateway | 7320 | http://127.0.0.1:7320 |
| Master Web | 7311 | http://localhost:7311 |
| Slave Web | 7321 | http://localhost:7321 |

Nginx 位置：`D:\workspace\code\ai-agent\share-agent\nginx\`

## 架构要点

### 通信协议

- **HTTP API**：`/api/*` - RESTful API 端点
- **SignalR Hub**：`/hubs/terminal` - 终端实时通信（WebSocket）
- 前端通过 `/web-pty/api/*` 和 `/web-pty/hubs/terminal` 访问网关

### 核心服务

- **InstanceManager**：管理终端实例生命周期
- **SessionManager**：管理用户会话和实例映射
- **TerminalConnectionRegistryV2**：SignalR 连接注册
- **TerminalEventRelayV2**：终端事件中继
- **ProcessRunner**：内置进程执行库（支持管道、并发控制）

### 集群模式

支持 Master-Slave 集群架构：
- Master 网关：协调多个 Slave 节点
- Slave 网关：执行实际终端实例
- ClusterHub (`/hubs/cluster`)：节点间通信

## 环境变量

### Gateway 常用配置

```bash
PORT=7300                           # 监听端口
HOST=0.0.0.0                        # 监听地址
TERMINAL_GATEWAY_TOKEN=             # 网关认证令牌
TERMINAL_WS_TOKEN=                  # WebSocket 令牌
TERMINAL_PROFILE_STORE_FILE=        # Profile 存储路径
TERMINAL_SETTINGS_STORE_FILE=       # 设置存储路径
TERMINAL_MAX_OUTPUT_BUFFER_BYTES=   # 输出缓冲区大小限制
TERMINAL_PROCESS_MANAGER_MAX_CONCURRENCY=  # 进程管理器最大并发
TERMINAL_PATH_PREFIXES=             # PTY 子进程 PATH 前缀
TERMINAL_FS_ALLOWED_ROOTS=          # 文件系统允许访问的根目录
```

## 开发工作流

1. **启动后端**：`dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj`
2. **启动前端**：`cd apps/secretary-web && npm run dev`
3. **前端开发服务器代理**：vite.config.js 已配置代理，前端请求自动转发到后端
4. **运行测试**：后端用 `dotnet test`，前端 E2E 需要先 `npm run build` 再 `npm run test:e2e`

## 技术栈版本

- .NET 10.0
- Vue 3.5 + Vite 7.x
- SignalR (@microsoft/signalr 10.0)
- xterm.js 5.3 + addons
- Porta.Pty 1.0.7
- xUnit + Playwright
