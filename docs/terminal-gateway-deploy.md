# terminal-gateway 部署说明

## 1. 适用范围

本文档只描述当前仓库内 `terminal-gateway-dotnet` 的部署与运行方式，重点覆盖：

- 集群模式：master + slave
- 单实例模式：仅 master
- 单实例模式：仅 slave
- 本地直接运行
- 从 cluster 切回单 master 时的 Nginx 切换

相关脚本：

- `deploy/release-local.sh`
- `deploy/release-frontend-local.sh`
- `deploy/release-cluster-local.sh`
- `deploy/release-cluster-frontend-local.sh`
- `deploy/verify-local.sh`
- `deploy/verify-cluster-local.sh`

## 2. 模式说明

- 默认角色是 `master`
- 当 `GATEWAY_ROLE=slave` 且 `MASTER_URL` 非空时，网关会以 slave 身份连接 master
- cluster 模式通常使用两个 systemd 服务：
  - `terminal-gateway-master.service`
  - `terminal-gateway-slave.service`
- 单 master 模式通常使用一个 systemd 服务：
  - `terminal-gateway-dotnet.service`

## 3. 常用环境变量

- `HOST`
- `PORT`
- `GATEWAY_ROLE`
- `NODE_ID`
- `NODE_NAME`
- `NODE_LABEL`
- `MASTER_URL`
- `CLUSTER_TOKEN`
- `FILES_BASE_PATH`
- `TERMINAL_SETTINGS_STORE_FILE`
- `TERMINAL_PROFILE_STORE_FILE`

## 4. 本地直接运行

### 4.1 本地启动单 master 模式

```bash
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

默认值：

- `HOST=0.0.0.0`
- `PORT=8080`
- `GATEWAY_ROLE=master`

如需显式指定：

```bash
GATEWAY_ROLE=master \
PORT=7300 \
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

### 4.2 本地启动单 slave 模式

前提：已有可访问的 master。

```bash
GATEWAY_ROLE=slave \
HOST=127.0.0.1 \
PORT=7320 \
NODE_ID=slave-local \
NODE_NAME="Slave Local" \
MASTER_URL=http://127.0.0.1:7310 \
CLUSTER_TOKEN=dev-cluster-token \
FILES_BASE_PATH=/home/yueyuan/gitlab \
TERMINAL_SETTINGS_STORE_FILE=/tmp/pty-agent-terminal-settings-slave.json \
TERMINAL_PROFILE_STORE_FILE=/tmp/pty-agent-terminal-profiles-slave.json \
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

### 4.3 本地启动 master/slave 模式

先启动 master：

```bash
GATEWAY_ROLE=master \
HOST=127.0.0.1 \
PORT=7310 \
NODE_ID=master-local \
NODE_NAME="Master Local" \
CLUSTER_TOKEN=dev-cluster-token \
FILES_BASE_PATH=/home/yueyuan \
TERMINAL_SETTINGS_STORE_FILE=/tmp/pty-agent-terminal-settings-master.json \
TERMINAL_PROFILE_STORE_FILE=/tmp/pty-agent-terminal-profiles-master.json \
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

再启动 slave：

```bash
GATEWAY_ROLE=slave \
HOST=127.0.0.1 \
PORT=7320 \
NODE_ID=slave-local \
NODE_NAME="Slave Local" \
MASTER_URL=http://127.0.0.1:7310 \
CLUSTER_TOKEN=dev-cluster-token \
FILES_BASE_PATH=/home/yueyuan/gitlab \
TERMINAL_SETTINGS_STORE_FILE=/tmp/pty-agent-terminal-settings-slave.json \
TERMINAL_PROFILE_STORE_FILE=/tmp/pty-agent-terminal-profiles-slave.json \
dotnet run --project apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
```

约束：

- `CLUSTER_TOKEN` 必须一致
- `MASTER_URL` 必须可达
- master/slave 应使用不同的 `PORT`、`NODE_ID` 和本地存储文件

## 5. systemd 部署

### 5.1 启动集群模式

```bash
sudo bash deploy/release-cluster-local.sh
```

脚本行为：

- 写入 `terminal-gateway-master.service`
- 写入 `terminal-gateway-slave.service`
- 构建 master/slave 两套前端
- 发布到：
  - `/www/wwwroot/pty-agent-web-master`
  - `/www/wwwroot/pty-agent-web-slave`
- 启用并重启两个服务

默认端口：

- master：`7310`
- slave：`7320`

如需覆盖默认值：

```bash
sudo MASTER_PORT=7310 \
SLAVE_PORT=7320 \
MASTER_NODE_ID=master-local \
SLAVE_NODE_ID=slave-local \
bash deploy/release-cluster-local.sh
```

### 5.2 更新集群模式

后端和前端一起更新：

```bash
sudo bash deploy/release-cluster-local.sh
```

只更新前端：

```bash
sudo bash deploy/release-cluster-frontend-local.sh
```

验证：

```bash
bash deploy/verify-cluster-local.sh
```

### 5.3 停止集群模式

停止并取消开机自启：

```bash
sudo systemctl disable --now terminal-gateway-master.service
sudo systemctl disable --now terminal-gateway-slave.service
```

只停止：

```bash
sudo systemctl stop terminal-gateway-master.service
sudo systemctl stop terminal-gateway-slave.service
```

### 5.4 启动 master 模式

```bash
sudo bash deploy/release-local.sh
```

脚本行为：

- 写入 `terminal-gateway-dotnet.service`
- 构建单套前端
- 发布到 `/www/wwwroot/pty-agent-web`
- 启用并重启该服务

默认监听：

- `HOST=127.0.0.1`
- `PORT=7300`

### 5.5 更新 master 模式

后端和前端一起更新：

```bash
sudo bash deploy/release-local.sh
```

只更新前端：

```bash
sudo bash deploy/release-frontend-local.sh
```

验证：

```bash
bash deploy/verify-local.sh
```

### 5.6 停止 master 模式

停止并取消开机自启：

```bash
sudo systemctl disable --now terminal-gateway-dotnet.service
```

只停止：

```bash
sudo systemctl stop terminal-gateway-dotnet.service
```

### 5.7 启动单 slave 模式

前提：已有可访问的 master。当前仓库没有单独的 `release-slave-local.sh`，因此单 slave 需手动写 systemd unit。

示例：

```ini
[Unit]
Description=PTY Agent Terminal Gateway (slave)
After=network.target

[Service]
Type=simple
WorkingDirectory=/home/yueyuan/pty-agent
Environment=HOST=127.0.0.1
Environment=PORT=7320
Environment=GATEWAY_ROLE=slave
Environment=NODE_ID=slave-local
Environment=NODE_NAME=Slave Local
Environment=MASTER_URL=http://127.0.0.1:7310
Environment=CLUSTER_TOKEN=dev-cluster-token
Environment=FILES_BASE_PATH=/home/yueyuan/gitlab
Environment=TERMINAL_SETTINGS_STORE_FILE=/tmp/pty-agent-terminal-settings-slave.json
Environment=TERMINAL_PROFILE_STORE_FILE=/tmp/pty-agent-terminal-profiles-slave.json
ExecStart=/usr/bin/dotnet run --project /home/yueyuan/pty-agent/apps/terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj
Restart=always
RestartSec=2
StandardOutput=append:/www/wwwlogs/terminal-gateway-slave.out.log
StandardError=append:/www/wwwlogs/terminal-gateway-slave.err.log

[Install]
WantedBy=multi-user.target
```

保存为 `/etc/systemd/system/terminal-gateway-slave.service` 后执行：

```bash
sudo systemctl daemon-reload
sudo systemctl enable terminal-gateway-slave.service
sudo systemctl restart terminal-gateway-slave.service
```

### 5.8 停止单 slave 模式

停止并取消开机自启：

```bash
sudo systemctl disable --now terminal-gateway-slave.service
```

只停止：

```bash
sudo systemctl stop terminal-gateway-slave.service
```

## 6. 从 cluster 切回仅 master

### 6.1 systemd 切换

```bash
sudo systemctl disable --now terminal-gateway-master.service
sudo systemctl disable --now terminal-gateway-slave.service
sudo bash deploy/release-local.sh
```

### 6.2 Nginx 也要同步切换

如果线上 Nginx 仍保留 cluster 反代，`/web-pty/api/*` 会继续转发到旧的 `7310/7320`，从而返回 `502`。切换到单 master 时需要一并修改站点 vhost：

- 根目录从 `pty-agent-web-master` 切回 `pty-agent-web`
- `/web-pty/api/` 和 `/api/` 的 `proxy_pass` 从 `127.0.0.1:7310` 改回 `127.0.0.1:7300`
- `/web-pty/hubs/` 和 `/hubs/` 的 `proxy_pass` 从 `127.0.0.1:7310` 改回 `127.0.0.1:7300`
- 如果不再提供 slave 页面，可移除 `/slave/*` 相关 location

修改后执行：

```bash
sudo nginx -t
sudo systemctl reload nginx
```

线上 Nginx 关键路径可参考：

- `docs/nginx-config-paths.md`

## 7. 排障

### 7.1 检查服务状态

```bash
systemctl --no-pager --lines=50 status terminal-gateway-dotnet.service
systemctl --no-pager --lines=50 status terminal-gateway-master.service
systemctl --no-pager --lines=50 status terminal-gateway-slave.service
```

### 7.2 检查监听端口

```bash
ss -ltnp | rg ':7300|:7310|:7320|:8080'
```

### 7.3 检查本机健康

```bash
curl -sS http://127.0.0.1:7300/api/health
curl -sS http://127.0.0.1:7310/api/health
curl -sS http://127.0.0.1:7320/api/health
```

### 7.4 典型问题

- `502 Bad Gateway`
  - 优先检查 Nginx 反代端口是否和当前模式一致
  - 再检查对应 systemd 服务是否真的在监听该端口

- slave 连不上 master
  - 检查 `MASTER_URL`
  - 检查 `CLUSTER_TOKEN`
  - 检查防火墙和网络可达性

- 切回单 master 后页面能开但 API 502
  - 基本都是 Nginx 仍指向 `7310`

## 8. 验证

单 master 常用验证：

```bash
curl -k https://your-domain/web-pty/api/health
curl -k https://your-domain/web-pty/api/nodes
curl -k https://your-domain/web-pty/api/instances
```

cluster 常用验证：

```bash
bash deploy/verify-cluster-local.sh
```
