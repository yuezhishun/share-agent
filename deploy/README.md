# Deploy Scripts

`deploy/` 现在按场景组织，不再把所有发布脚本平铺在根目录。

## 场景选择

| 场景 | 目录 | 说明 |
| --- | --- | --- |
| 单机 master | `deploy/single-master` | 单台机器提供前端、Nginx 和 gateway |
| 生产 master + 局域网 slave | `deploy/cluster-lan` | 公网 master 在 `https://pty.addai.vip`，局域网 slave 各自本地部署前端和 gateway |
| 本机 cluster 示例 | `deploy/cluster-examples` | 用应用内 `appsettings.Cluster*.json` 快速启动 master/slave，不负责生产部署 |
| Docker 本地联调 | `deploy/docker` | `docker compose` + Nginx 容器 |

## 执行顺序

### 单机 master

Linux:

1. `bash deploy/single-master/build-frontend.sh`
2. `sudo bash deploy/single-master/install-service.sh`
3. `sudo bash deploy/single-master/install-nginx.sh`
4. `bash deploy/single-master/verify.sh`

Windows:

1. `powershell -File deploy/single-master/build-frontend.ps1`
2. `powershell -File deploy/single-master/start-gateway.ps1`

### 生产 master + 局域网 slave

生产 master:

1. `bash deploy/cluster-lan/build-master-frontend.sh`
2. `sudo bash deploy/cluster-lan/install-master-service.sh`
3. `sudo bash deploy/cluster-lan/install-master-nginx.sh`
4. `bash deploy/cluster-lan/verify-master.sh`

局域网 slave:

1. `bash deploy/cluster-lan/build-slave-frontend.sh`
2. `sudo bash deploy/cluster-lan/install-slave-service.sh`
3. `sudo bash deploy/cluster-lan/install-slave-nginx.sh`
4. `bash deploy/cluster-lan/verify-slave.sh`
5. `bash deploy/cluster-lan/verify-cluster.sh`

## 变量约定

前端构建统一使用以下变量：

- `VITE_APP_BASE_PATH`
- `VITE_WEBPTY_BASE`
- `VITE_WEBPTY_HUB_PATH`
- `VITE_WEBPTY_HUB_URL` 仅用于跨域兜底

不再使用 `VITE_WEBPTY_HUB_PATH_V2`。
