# Cluster LAN

适用场景：公网 master 运行在 `https://pty.addai.vip`，局域网设备作为 slave 连接 master；每台 slave 本地也部署一套前端，优先走本地访问。

## 生产 master

1. 构建前端：`bash deploy/cluster-lan/build-master-frontend.sh`
2. 安装 systemd：`sudo bash deploy/cluster-lan/install-master-service.sh`
3. 安装 Nginx：`sudo bash deploy/cluster-lan/install-master-nginx.sh`
4. 验证：`bash deploy/cluster-lan/verify-master.sh`

## 局域网 slave

1. 构建前端：`bash deploy/cluster-lan/build-slave-frontend.sh`
2. 安装 systemd：`sudo bash deploy/cluster-lan/install-slave-service.sh`
3. 安装 Nginx：`sudo bash deploy/cluster-lan/install-slave-nginx.sh`
4. 验证：`bash deploy/cluster-lan/verify-slave.sh`

## 连接关系

- master 站点：前端 `/`，API `/api/`，Hub `/hubs/terminal`
- slave 本地站点：前端 `/`，API `/api/`，Hub `/hubs/terminal`
- slave 通过 `MASTER_URL` 注册到远程 master

## 默认环境名

- Linux master：`ClusterLinuxMaster`
- Linux slave：`ClusterLinuxSlaveLocal`
- Windows master：`ClusterWindowsMaster`
- Windows slave：`ClusterWindowsSlaveLocal`
