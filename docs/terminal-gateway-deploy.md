# terminal-gateway 部署说明

## 1. 部署入口

当前仓库的部署脚本按场景组织：

- `deploy/single-master`
  - 单机 master，前端由 Nginx 提供，后端本机反代
- `deploy/cluster-lan`
  - 公网 master + 局域网 slave
  - master 公网入口固定为 `https://pty.addai.vip`
  - slave 本机也部署一套前端，优先本地访问
- `deploy/cluster-examples`
  - 本机快速启动示例，不负责生产部署
- `deploy/docker`
  - Docker Compose 联调

## 2. 路由约定

### 2.1 生产 master

- 前端：`/`
- API：`/api/`
- Terminal Hub：`/hubs/terminal`
- Cluster Hub：`/hubs/cluster`

### 2.2 局域网 slave

- 前端：`/`
- API：`/api/`
- Terminal Hub：`/hubs/terminal`
- slave 通过 `MASTER_URL` 接入远程 master

### 2.3 前端构建变量

统一使用：

- `VITE_APP_BASE_PATH`
- `VITE_WEBPTY_BASE`
- `VITE_WEBPTY_HUB_PATH`
- `VITE_WEBPTY_HUB_URL`

不再使用 `VITE_WEBPTY_HUB_PATH_V2`。

## 3. 单机 master

### 3.1 Linux

执行顺序：

```bash
bash deploy/single-master/build-frontend.sh
sudo bash deploy/single-master/install-service.sh
sudo bash deploy/single-master/install-nginx.sh
bash deploy/single-master/verify.sh
```

说明：

- `build-frontend.sh` 把前端发布到 `/www/wwwroot/pty-agent-web`
- `install-service.sh` 写入单机 master 的 systemd service
- `install-nginx.sh` 写入站点 vhost，默认代理到 `127.0.0.1:7300`
- `verify.sh` 校验前端、健康接口和终端实例创建/销毁

### 3.2 Windows

执行顺序：

```powershell
powershell -File deploy/single-master/build-frontend.ps1
powershell -File deploy/single-master/start-gateway.ps1
```

说明：

- 默认环境名：`SingleWindowsMaster`
- Windows 脚本只负责前端构建和前台启动 gateway
- 若本机使用 Nginx，请让根路径指向构建输出目录，并把 `/api/`、`/hubs/` 反代到 gateway

## 4. 生产 master + 局域网 slave

### 4.1 生产 master

执行顺序：

```bash
bash deploy/cluster-lan/build-master-frontend.sh
sudo bash deploy/cluster-lan/install-master-service.sh
sudo bash deploy/cluster-lan/install-master-nginx.sh
bash deploy/cluster-lan/verify-master.sh
```

默认值：

- master service 监听 `127.0.0.1:7310`
- Nginx 根目录 `/www/wwwroot/pty-agent-web`
- 站点域名 `pty.addai.vip`

### 4.2 局域网 slave

执行顺序：

```bash
bash deploy/cluster-lan/build-slave-frontend.sh
sudo MASTER_URL=https://pty.addai.vip bash deploy/cluster-lan/install-slave-service.sh
sudo bash deploy/cluster-lan/install-slave-nginx.sh
bash deploy/cluster-lan/verify-slave.sh
```

默认值：

- slave service 监听 `127.0.0.1:7320`
- slave 前端发布目录 `/www/wwwroot/pty-agent-slave-web`
- slave 本地站点默认 `server_name=slave.local`

### 4.3 集群联通验证

```bash
bash deploy/cluster-lan/verify-cluster.sh
```

脚本检查：

- 公网 master 健康
- slave 本机健康
- master 的 `/api/nodes` 中能看到 slave

## 5. 本机 cluster 示例

这组脚本只负责本机快速运行，不负责生产安装：

```bash
bash deploy/cluster-examples/start-master.sh
bash deploy/cluster-examples/start-master-slave.sh
```

Windows：

```powershell
powershell -File deploy/cluster-examples/start-master.ps1
powershell -File deploy/cluster-examples/start-master-slave.ps1
```

单一来源是 `TerminalGateway.Api` 下的 `appsettings.Cluster*.json`。

## 6. Docker 联调

```bash
cd deploy/docker
docker compose up --build
./smoke.sh
```

## 7. 排障

### 7.1 检查服务状态

```bash
systemctl --no-pager --lines=50 status terminal-gateway-single-master.service
systemctl --no-pager --lines=50 status terminal-gateway-master.service
systemctl --no-pager --lines=50 status terminal-gateway-slave.service
```

### 7.2 检查监听端口

```bash
ss -ltnp | rg ':7300|:7310|:7320'
```

### 7.3 典型问题

- `502 Bad Gateway`
  - 先检查 Nginx `proxy_pass` 端口是否和当前服务一致
- slave 不显示在 master 节点列表中
  - 先检查 `MASTER_URL`
  - 再检查 `CLUSTER_TOKEN`
- 前端能打开但无法连终端
  - 先检查 `/hubs/terminal` 是否被正确反代
