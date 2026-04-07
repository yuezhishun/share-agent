# Nginx 配置路径记录

本文档记录当前项目关键 Nginx 配置位置，避免部署与排障时路径混淆。

## 1. Nginx 主配置（线上实际入口）
- `/www/server/nginx/conf/nginx.conf`

说明：
- 这是 Nginx 启动时加载的主配置。
- 当前环境下该文件会 `include /www/server/panel/vhost/nginx/*.conf`，实际站点路由通常在 vhost 文件中。

## 2. 项目站点配置（线上实际生效）
- `/www/server/panel/vhost/nginx/pty-agent-web.conf`
- `/www/server/panel/vhost/nginx/pyt.addai.vip.conf`

说明：
- 上述文件包含本项目 Web、API、SignalR Hub 的反向代理规则。
- 当前重点路径：`/api/`、`/hubs/terminal`（dotnet gateway）。
- 历史 WebSocket 路由（如 `/ws/terminal`、`/ws/term`）仅在旧方案中出现，若仍有残留请按当前代码核对是否仍需要。

## 3. 仓库部署参考配置
- `deploy/single-master/install-nginx.sh`
- `deploy/cluster-lan/install-master-nginx.sh`
- `deploy/cluster-lan/install-slave-nginx.sh`
- `deploy/docker/nginx.conf`

说明：
- 生产 master 默认走同域同前缀：
  - 前端：`/`
  - API：`/api/`
  - Hub：`/hubs/terminal`
- 局域网 slave 本地站点也走相同前缀，只是反代到 slave 本机端口。
- Docker 联调使用 `deploy/docker/nginx.conf`，不要和宿主机宝塔 vhost 混用。
