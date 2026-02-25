# Nginx 配置路径记录

本文档记录当前项目的关键 Nginx 配置位置，避免部署与排障时路径混淆。

## 1. Nginx 主配置（线上实际入口）

- `/www/server/nginx/conf/nginx.conf`

说明：
- 这是 Nginx 启动时加载的主配置。
- 当前环境下该文件会 `include /www/server/panel/vhost/nginx/*.conf`，实际站点路由通常在 vhost 文件中。

## 2. 项目站点配置（线上实际生效）

- `/www/server/panel/vhost/nginx/pty-agent-web.conf`
- `/www/server/panel/vhost/nginx/pyt.addai.vip.conf`

说明：
- 上述文件包含本项目 Web、API、WebSocket 的反向代理规则（例如 `/api/`、`/hubs/`、`/ws/terminal`、`/terminal-api/`）。
- 若线上访问异常，优先检查这里的 `server_name`、`listen`、`location`、`proxy_pass`。

## 3. 仓库部署参考配置

- `deploy/nginx.conf`

说明：
- 该文件用于仓库内 Docker Compose 部署场景的 Nginx 配置模板。
- 不一定与线上宝塔/宿主机 Nginx 完全一致，排障时请先确认当前实际加载的是哪套配置。
