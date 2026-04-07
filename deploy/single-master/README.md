# Single Master

适用场景：单台 Linux 或 Windows 机器同时提供前端和 gateway。

## Linux

1. 构建前端：`bash deploy/single-master/build-frontend.sh`
2. 安装 systemd 服务：`sudo bash deploy/single-master/install-service.sh`
3. 安装 Nginx 站点：`sudo bash deploy/single-master/install-nginx.sh`
4. 验证：`bash deploy/single-master/verify.sh`

## Windows

1. 构建前端：`powershell -File deploy/single-master/build-frontend.ps1`
2. 前台启动 gateway：`powershell -File deploy/single-master/start-gateway.ps1`

## 默认配置

- 环境名：`SingleLinuxMaster` / `SingleWindowsMaster`
- 前端访问路径：`/`
- API：`/api/`
- Hub：`/hubs/terminal`

生产站点默认：

- `SERVER_NAME=pty.addai.vip`
- `SITE_ROOT=/www/wwwroot/pty-agent-web`
- `GATEWAY_PORT=7300`
