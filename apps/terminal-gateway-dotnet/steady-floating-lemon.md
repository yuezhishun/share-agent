# AionUi CLI 管理迁移至 .NET 架构设计方案

## Context

当前AionUi项目使用TypeScript/Node.js实现与各类AI CLI工具（Claude、Codex、Goose、Qwen等）的交互，通过ACP（Agent Communication Protocol）协议进行通信。用户希望：

1. 使用.NET重写CLI调用逻辑
2. 集中管理CLI调用过程
3. 利用现有前端界面进行配置和监控（最大限度减少工作量）

### 现有架构分析

#### 1. 前端架构（保持不变）

- **技术栈**: React + TypeScript + Electron
- **UI组件**: @arco-design/web-react
- **状态管理**: SWR + 本地状态
- **进程通信**: IPC Bridge (`src/common/adapter/ipcBridge.ts`)

#### 2. 现有后端（TypeScript）


| 组件            | 文件路径                                 | 职责                          |
| --------------- | ---------------------------------------- | ----------------------------- |
| AcpConnection   | `src/process/agent/acp/AcpConnection.ts` | 管理CLI进程生命周期和协议通信 |
| AcpAgentManager | `src/process/task/AcpAgentManager.ts`    | 高层会话管理、消息路由        |
| acpConnectors   | `src/process/agent/acp/acpConnectors.ts` | 各Backend的启动逻辑           |
| safeExec        | `src/process/utils/safeExec.ts`          | 安全的命令执行工具            |

#### 3. ACP协议核心方法

- `initialize` - 协议初始化
- `session/new` - 创建会话
- `session/prompt` - 发送消息
- `session/update` - 接收流式更新
- `session/request_permission` - 权限请求
- `fs/read_text_file`, `fs/write_text_file` - 文件操作

#### 4. 现有C#示例

项目已包含基础C# ACP客户端示例 (`csharp-acp-client/Program.cs`)，展示了：

- Process启动和stdio重定向
- JSON-RPC请求/响应处理
- 流式更新解析

---

## 推荐方案: 保持前端 + .NET后端服务（支持混合部署）

### 架构概览（支持多模式部署）

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│                              部署模式总览                                            │
├─────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                     │
│  ┌──────────────────────────┐    ┌──────────────────────────┐                     │
│  │      模式1: 本地模式      │    │      模式2: 云端模式      │                     │
│  │    (Electron + 本地.NET)  │    │   (Web/Electron + 云端.NET)│                     │
│  │                          │    │                          │                     │
│  │  ┌──────────────────┐   │    │   ┌──────────────────┐    │                     │
│  │  │  Electron App    │   │    │   │  Browser/Electron│    │                     │
│  │  │  (React Frontend)│   │    │   │  (React Frontend)│    │                     │
│  │  └────────┬─────────┘   │    │   └────────┬─────────┘    │                     │
│  │           │ localhost   │    │            │ HTTPS/WSS    │                     │
│  │           ▼             │    │            ▼              │                     │
│  │  ┌──────────────────┐   │    │   ┌──────────────────┐    │                     │
│  │  │ .NET Service     │   │    │   │  Cloud Service   │    │                     │
│  │  │  (localhost:5000)│   │    │   │  (Azure/AWS/阿里云)│   │                     │
│  │  └────────┬─────────┘   │    │   └────────┬─────────┘    │                     │
│  │           │ spawn       │    │            │ SSH/Remote   │                     │
│  │           ▼             │    │            ▼              │                     │
│  │  ┌──────────────────┐   │    │   ┌──────────────────┐    │                     │
│  │  │ 本地CLI进程       │   │    │   │ 云端CLI进程       │    │                     │
│  │  │ (Claude/Codex...)│   │    │   │ (Claude/Codex...)│    │                     │
│  │  └──────────────────┘   │    │   └──────────────────┘    │                     │
│  └──────────────────────────┘    └──────────────────────────┘                     │
│                                                                                     │
│  ┌──────────────────────────────────────────────────────────────────────────┐    │
│  │                         模式3: 混合模式                                   │    │
│  │                    (本地前端 + 远程服务 + 本地/远程CLI)                      │    │
│  │                                                                            │    │
│  │   Electron App ──► 云端.NET服务 ──► 本地CLI (通过本地代理)                  │    │
│  │                      或                                                     │    │
│  │   Electron App ──► 云端.NET服务 ──► 远程CLI (SSH)                          │    │
│  └──────────────────────────────────────────────────────────────────────────┘    │
│                                                                                     │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

### 核心设计思路

**保持前端不变，只替换后端实现：**

1. **前端改动最小化**

   - 继续使用现有的React组件和状态管理
   - 通过HTTP API调用.NET服务替代直接spawn进程
   - 流式响应通过SignalR或Server-Sent Events接收
2. **TypeScript后端 → .NET后端**

   - 将 `AcpConnection.ts` 逻辑移植到 `AcpProtocolHandler.cs`
   - 将 `AcpAgentManager.ts` 移植到 `AcpSessionManager.cs`
   - 通过REST API和SignalR暴露功能
3. **Electron主进程作为代理**

   - 启动.NET服务进程
   - 转发前端请求到.NET服务
   - 管理.NET服务生命周期

### 多模式部署详解

#### 模式1: 本地模式（Local Mode）

适用于个人开发者，所有组件运行在本地机器。

```
┌──────────────────────────────────────────┐
│  用户电脑                                │
│                                          │
│  ┌──────────────────┐                    │
│  │  Electron App    │                    │
│  │  (React UI)      │                    │
│  └────────┬─────────┘                    │
│           │ HTTP/WebSocket               │
│           ▼                              │
│  ┌──────────────────┐                    │
│  │  .NET Service    │◄── 管理           │
│  │  (localhost)     │    CLI进程        │
│  └────────┬─────────┘                    │
│           │                              │
│  ┌────────┴─────────┐                    │
│  │ 本地CLI进程       │                    │
│  │ (Claude/Codex...)│                    │
│  └──────────────────┘                    │
└──────────────────────────────────────────┘
```

**特点**:

- 完全离线工作
- 数据不离开本地机器
- 适合对隐私敏感的场景
- 启动速度快

**部署**:

- Electron打包时包含.NET运行时
- 一键安装，开箱即用

---

#### 模式2: 云端模式（Cloud Mode）

适用于团队协作，.NET服务和CLI进程运行在云端服务器。

```
┌─────────────────────────────────────────────────────────────┐
│                    用户浏览器/Electron                       │
│                                                             │
│  ┌──────────────────┐                                       │
│  │  React UI        │                                       │
│  └────────┬─────────┘                                       │
│           │ HTTPS/WSS (互联网)                               │
└───────────┼─────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────┐
│                    云服务器 (Azure/AWS/阿里云)                │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  .NET Service (Docker/Kubernetes)                    │   │
│  │  - REST API                                          │   │
│  │  - SignalR Hub                                       │   │
│  │  - 管理多租户会话                                     │   │
│  └────────┬─────────────────────────────────────────────┘   │
│           │                                                  │
│  ┌────────┴──────────────────────────────────────────┐      │
│  │  云端CLI进程池                                     │      │
│  │  (每个用户/团队隔离运行)                            │      │
│  │                                                   │      │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐             │      │
│  │  │Claude   │ │Codex    │ │Goose    │  ...        │      │
│  │  │用户A    │ │用户B    │ │用户C    │             │      │
│  │  └─────────┘ └─────────┘ └─────────┘             │      │
│  └──────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────┘
```

**特点**:

- 随时随地访问
- 支持多用户协作
- 统一的配置管理
- 适合企业部署

**部署**:

- Docker容器化部署
- Kubernetes编排
- 负载均衡支持
- SSL证书自动配置

**云端服务配置示例**:

```yaml
# docker-compose.yml (云端部署)
version: '3.8'
services:
  aionui-service:
    image: aionui/climanager:latest
    ports:
      - "80:80"
      - "443:443"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:80;https://+:443
      - ConnectionStrings__Redis=redis:6379  # SignalR Backplane
    volumes:
      - ./data:/app/data
      - /var/run/docker.sock:/var/run/docker.sock  # 管理CLI容器
    depends_on:
      - redis

  redis:
    image: redis:alpine
    # SignalR Backplane for scaling
```

---

#### 模式3: 混合模式（Hybrid Mode）

适用于需要远程管理但CLI需在本地运行的场景。

```
┌─────────────────────────────────────────────────────────────┐
│  用户电脑 (本地)                                             │
│                                                             │
│  ┌──────────────────┐        ┌──────────────────┐          │
│  │  Electron App    │◄──────►│  Local Agent     │          │
│  │  (React UI)      │        │  (可选代理)       │          │
│  └────────┬─────────┘        └────────┬─────────┘          │
│           │                           │                     │
│           │    ┌──────────────────────┘                     │
│           │    │                                            │
│           │    ▼ (WebSocket)                               │
└───────────┼─────────────────────────────────────────────────┘
            │
            ▼ (HTTPS)
┌─────────────────────────────────────────────────────────────┐
│  云服务器 (远程管理)                                          │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  .NET Service (云端)                                 │   │
│  │  - 用户认证/授权                                       │   │
│  │  - 配置同步                                            │   │
│  │  - 任务调度                                            │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
            │
            │ (可选: SSH/Remote)
            ▼
┌─────────────────────────────────────────────────────────────┐
│  本地CLI进程 (由本地Agent启动)                                │
│  ┌──────────────────┐                                       │
│  │  Claude/Codex... │                                       │
│  └──────────────────┘                                       │
└─────────────────────────────────────────────────────────────┘
```

**特点**:

- 配置存储在云端
- CLI进程运行在本地
- 支持远程启动本地CLI
- 适合企业安全策略

**实现方式**:

- 本地Agent通过反向WebSocket连接云端
- 云端下发命令，本地执行
- 执行结果通过Agent返回云端

---

### 纯Web模式支持

前端可以**完全脱离Electron**，在浏览器中运行：

```
┌─────────────────────────────────────────────────────────────┐
│  用户浏览器 (Chrome/Firefox/Edge)                           │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  React UI (SPA)                                      │   │
│  │  - 单页应用                                           │   │
│  │  - SignalR客户端                                      │   │
│  │  - 文件上传下载                                        │   │
│  └─────────────────┬────────────────────────────────────┘   │
└────────────────────┼────────────────────────────────────────┘
                     │ HTTPS/WSS
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  云服务器 / 本地服务器                                        │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  .NET Service                                        │   │
│  │  ┌────────────────┐  ┌────────────────┐             │   │
│  │  │  Static Files  │  │  API + SignalR │             │   │
│  │  │  (前端静态文件) │  │    Hub         │             │   │
│  │  └────────────────┘  └────────────────┘             │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**前端适配**:

```typescript
// src/services/api/config.ts
const API_CONFIG = {
  // 自动检测运行环境
  baseUrl: window.location.hostname === 'localhost'
    ? 'http://localhost:5000'  // 本地模式
    : 'https://api.aionui.com', // 云端模式

  signalRUrl: window.location.hostname === 'localhost'
    ? 'http://localhost:5000/acp-hub'
    : 'https://api.aionui.com/acp-hub'
};

// 使用方式
const response = await fetch(`${API_CONFIG.baseUrl}/api/sessions`);
```

**部署配置**:

```csharp
// Program.cs - 支持CORS和静态文件
var builder = WebApplication.CreateBuilder(args);

// 允许前端跨域访问
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 提供前端静态文件
app.UseStaticFiles();

// CORS
app.UseCors("AllowAll");

// API路由
app.MapControllers();
app.MapHub<AcpHub>("/acp-hub");

// 前端路由回退
app.MapFallbackToFile("index.html");

app.Run();
```

### 配置管理与环境切换

前端支持运行时切换服务地址：

```typescript
// src/services/api/ApiClient.ts
class ApiClient {
  private baseUrl: string;
  private hubConnection: HubConnection | null = null;

  constructor() {
    // 从配置或localStorage读取
    this.baseUrl = this.getServiceUrl();
  }

  private getServiceUrl(): string {
    // 优先级: 用户配置 > 环境变量 > 默认值
    const userConfig = localStorage.getItem('aionui_service_url');
    if (userConfig) return userConfig;

    // Electron模式下检测.NET服务端口
    if (window.electronAPI?.isElectron) {
      return 'http://localhost:5000';
    }

    // Web模式默认
    return process.env.REACT_APP_API_URL || 'https://api.aionui.com';
  }

  // 切换服务地址
  setServiceUrl(url: string) {
    this.baseUrl = url;
    localStorage.setItem('aionui_service_url', url);
    this.reconnect();
  }

  // 检测本地服务是否可用
  async checkLocalService(): Promise<boolean> {
    try {
      const response = await fetch('http://localhost:5000/api/health', {
        method: 'GET',
        signal: AbortSignal.timeout(3000)
      });
      return response.ok;
    } catch {
      return false;
    }
  }

  private reconnect() {
    // 断开并重新连接
    if (this.hubConnection) {
      this.hubConnection.stop();
    }
    this.connectSignalR();
  }
}

export const apiClient = new ApiClient();
```

**设置界面配置项**:

```typescript
// 设置页面添加服务地址配置
const ServiceSettings = () => {
  const [serviceUrl, setServiceUrl] = useState(apiClient.getServiceUrl());
  const [isLocalMode, setIsLocalMode] = useState(false);

  useEffect(() => {
    // 检测本地服务
    apiClient.checkLocalService().then(setIsLocalMode);
  }, []);

  return (
    <div>
      <h3>服务模式</h3>
      <RadioGroup value={isLocalMode ? 'local' : 'remote'}>
        <Radio value="local">
          本地模式 {isLocalMode && '✓ 已检测到本地服务'}
        </Radio>
        <Radio value="remote">云端模式</Radio>
      </RadioGroup>

      {!isLocalMode && (
        <Input
          label="服务地址"
          value={serviceUrl}
          onChange={setServiceUrl}
          placeholder="https://api.aionui.com"
        />
      )}
    </div>
  );
};
```

### 云端部署详细配置

#### Kubernetes部署

```yaml
# k8s-deployment.yml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: aionui-service
spec:
  replicas: 3  # 多实例部署
  selector:
    matchLabels:
      app: aionui-service
  template:
    metadata:
      labels:
        app: aionui-service
    spec:
      containers:
      - name: aionui-service
        image: aionui/climanager:latest
        ports:
        - containerPort: 80
        - containerPort: 443
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__Redis
          value: "redis:6379"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
---
apiVersion: v1
kind: Service
metadata:
  name: aionui-service
spec:
  selector:
    app: aionui-service
  ports:
  - port: 80
    targetPort: 80
  type: LoadBalancer
```

#### Azure Container Instances快速部署

```bash
# 一键部署到Azure
az container create \
  --resource-group myResourceGroup \
  --name aionui-service \
  --image aionui/climanager:latest \
  --dns-name-label aionui-service \
  --ports 80 443 \
  --environment-variables \
    ASPNETCORE_ENVIRONMENT=Production
```

#### AWS ECS部署

```json
// task-definition.json
{
  "family": "aionui-service",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "256",
  "memory": "512",
  "containerDefinitions": [
    {
      "name": "aionui-service",
      "image": "aionui/climanager:latest",
      "portMappings": [
        {
          "containerPort": 80,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        }
      ]
    }
  ]
}
```

### 安全考虑

#### 云端部署安全措施

```csharp
// Program.cs - 安全配置
var builder = WebApplication.CreateBuilder(args);

// JWT认证
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://auth.aionui.com";
        options.Audience = "aionui-api";
    });

// API限流
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100;
    });
});

var app = builder.Build();

// 中间件顺序很重要
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<AcpHub>("/acp-hub")
   .RequireAuthorization();  // SignalR需要认证
```

#### 本地模式安全

```csharp
// 本地模式只允许localhost访问
if (builder.Environment.IsDevelopment() ||
    builder.Configuration.GetValue<bool>("LocalMode"))
{
    // 本地模式安全：只绑定localhost
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5000);
    });
}
```

### Electron与云端服务集成

```typescript
// Electron主进程支持远程服务
// src/main/remote-service.ts

class RemoteServiceManager {
  private currentMode: 'local' | 'remote' = 'local';
  private remoteUrl: string = '';

  // 启动本地.NET服务或连接到远程服务
  async initialize(): Promise<void> {
    const config = await this.loadServiceConfig();

    if (config.mode === 'local') {
      await this.startLocalService();
    } else {
      await this.connectToRemote(config.url, config.token);
    }
  }

  // 切换到远程服务
  async switchToRemote(url: string, token: string): Promise<void> {
    // 停止本地服务
    await this.stopLocalService();

    // 测试远程连接
    const connected = await this.testRemoteConnection(url, token);
    if (!connected) {
      throw new Error('无法连接到远程服务');
    }

    // 保存配置
    await this.saveServiceConfig({ mode: 'remote', url, token });

    // 通知渲染进程
    mainWindow.webContents.send('service-switched', { mode: 'remote', url });
  }

  // 切换回本地服务
  async switchToLocal(): Promise<void> {
    await this.startLocalService();
    await this.saveServiceConfig({ mode: 'local' });
    mainWindow.webContents.send('service-switched', { mode: 'local' });
  }
}
```

### .NET后端服务架构

```
AionUi.CliManager.Service/
├── Program.cs                          # 服务入口
├── appsettings.json                    # 配置
├── Controllers/                        # REST API控制器
│   ├── BackendsController.cs           # Backend管理API
│   ├── SessionsController.cs           # 会话管理API
│   └── LogsController.cs               # 日志查询API
├── Hubs/                               # SignalR实时通信
│   └── AcpHub.cs                       # ACP会话Hub
├── Services/                           # 业务逻辑服务
│   ├── ICliProcessManager.cs           # CLI进程管理接口
│   ├── CliProcessManager.cs            # CLI进程管理实现
│   ├── IAcpSessionManager.cs           # 会话管理接口
│   ├── AcpSessionManager.cs            # 会话管理实现
│   └── IBackendConfigurationService.cs # 配置服务
├── Acp/                                # ACP协议实现
│   ├── AcpProtocolHandler.cs           # 协议处理器
│   ├── Models/
│   │   ├── JsonRpcModels.cs            # JSON-RPC消息
│   │   ├── AcpSessionModels.cs         # 会话模型
│   │   └── AcpUpdateModels.cs          # 更新事件模型
│   └── Handlers/
│       ├── FileOperationHandler.cs     # 文件操作处理
│       └── PermissionHandler.cs        # 权限请求处理
├── Configuration/                      # 配置管理
│   ├── BackendConfiguration.cs         # Backend配置模型
│   └── ConfigurationStore.cs           # 配置存储
└── Logging/                            # 日志系统
    ├── ICliLogger.cs
    └── CliLogger.cs
```

### API设计

#### REST API端点

```csharp
// BackendsController.cs
[ApiController]
[Route("api/[controller]")]
public class BackendsController : ControllerBase
{
    // GET /api/backends - 获取所有Backend
    [HttpGet]
    public async Task<ActionResult<List<BackendConfiguration>>> GetAll()

    // GET /api/backends/{id} - 获取指定Backend
    [HttpGet("{id}")]
    public async Task<ActionResult<BackendConfiguration>> Get(string id)

    // POST /api/backends - 创建Backend
    [HttpPost]
    public async Task<ActionResult<BackendConfiguration>> Create([FromBody] BackendConfiguration config)

    // PUT /api/backends/{id} - 更新Backend
    [HttpPut("{id}")]
    public async Task<ActionResult> Update(string id, [FromBody] BackendConfiguration config)

    // DELETE /api/backends/{id} - 删除Backend
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)

    // POST /api/backends/{id}/test - 测试Backend连接
    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestResult>> TestConnection(string id)
}

// SessionsController.cs
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    // POST /api/sessions - 创建新会话
    [HttpPost]
    public async Task<ActionResult<AcpSession>> Create([FromBody] CreateSessionRequest request)

    // GET /api/sessions - 获取所有活跃会话
    [HttpGet]
    public async Task<ActionResult<List<AcpSession>>> GetActive()

    // GET /api/sessions/{id} - 获取会话详情
    [HttpGet("{id}")]
    public async Task<ActionResult<AcpSession>> Get(string id)

    // DELETE /api/sessions/{id} - 关闭会话
    [HttpDelete("{id}")]
    public async Task<ActionResult> Close(string id)

    // POST /api/sessions/{id}/prompt - 发送消息（非流式）
    [HttpPost("{id}/prompt")]
    public async Task<ActionResult<PromptResponse>> SendPrompt(string id, [FromBody] SendPromptRequest request)

    // POST /api/sessions/{id}/cancel - 取消当前请求
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult> Cancel(string id)

    // PUT /api/sessions/{id}/model - 切换模型
    [HttpPut("{id}/model")]
    public async Task<ActionResult> SetModel(string id, [FromBody] SetModelRequest request)

    // PUT /api/sessions/{id}/mode - 设置模式
    [HttpPut("{id}/mode")]
    public async Task<ActionResult> SetMode(string id, [FromBody] SetModeRequest request)
}

// LogsController.cs
[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    // GET /api/logs?sessionId=&level=&startTime= - 查询日志
    [HttpGet]
    public async Task<ActionResult<List<LogEntry>>> Query([FromQuery] LogQueryRequest request)

    // GET /api/logs/stream - 日志流（SSE）
    [HttpGet("stream")]
    public IAsyncEnumerable<LogEntry> Stream([FromQuery] string? sessionId = null)
}
```

#### SignalR Hub

```csharp
// AcpHub.cs - 实时通信
public class AcpHub : Hub
{
    // 客户端调用：创建会话
    public async Task<AcpSession> CreateSession(string backendId, string? workingDirectory = null)

    // 客户端调用：发送消息（流式响应通过事件推送）
    public async Task SendPrompt(string sessionId, string prompt)

    // 客户端调用：确认权限
    public async Task ConfirmPermission(string sessionId, string optionId)

    // 服务器推送：会话更新
    // await Clients.Group(sessionId).SendAsync("SessionUpdate", update)

    // 服务器推送：权限请求
    // await Clients.Group(sessionId).SendAsync("PermissionRequested", request)

    // 服务器推送：流式消息块
    // await Clients.Group(sessionId).SendAsync("MessageChunk", chunk)

    // 服务器推送：工具调用更新
    // await Clients.Group(sessionId).SendAsync("ToolCallUpdate", update)
}
```

### 前端适配层

前端需要创建一个适配层，将现有的直接进程调用改为HTTP API调用：

```typescript
// src/process/agent/acp/AcpConnection.ts → 适配为调用.NET API

// 原有：直接spawn进程
// this.child = spawn(cliPath, args, {...})

// 新：通过HTTP API
class AcpConnectionAdapter {
  private hubConnection: HubConnection;
  private sessionId: string | null = null;

  async connect(backend: AcpBackend, cliPath?: string): Promise<void> {
    // 1. 通过API创建会话
    const response = await fetch('http://localhost:5000/api/sessions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ backendId: backend, cliPath })
    });
    const session = await response.json();
    this.sessionId = session.sessionId;

    // 2. 建立SignalR连接
    this.hubConnection = new HubConnectionBuilder()
      .withUrl('http://localhost:5000/acp-hub')
      .withAutomaticReconnect()
      .build();

    // 3. 订阅实时事件
    this.hubConnection.on('SessionUpdate', (update) => {
      this.onSessionUpdate(update);
    });

    this.hubConnection.on('PermissionRequested', (request) => {
      this.onPermissionRequest(request);
    });

    await this.hubConnection.start();
    await this.hubConnection.invoke('JoinSession', this.sessionId);
  }

  async sendPrompt(prompt: string): Promise<void> {
    if (!this.sessionId) throw new Error('No active session');
    await this.hubConnection.invoke('SendPrompt', this.sessionId, prompt);
  }

  async confirmPermission(optionId: string): Promise<void> {
    if (!this.sessionId) throw new Error('No active session');
    await this.hubConnection.invoke('ConfirmPermission', this.sessionId, optionId);
  }

  async disconnect(): Promise<void> {
    if (this.sessionId) {
      await fetch(`http://localhost:5000/api/sessions/${this.sessionId}`, {
        method: 'DELETE'
      });
    }
    await this.hubConnection.stop();
  }
}
```

### 核心服务实现

```csharp
// ICliProcessManager.cs
public interface ICliProcessManager
{
    Task<CliProcess> StartAsync(BackendConfiguration config, CancellationToken ct = default);
    Task StopAsync(string processId, CancellationToken ct = default);
    Task<CliProcess?> GetProcessAsync(string processId);
    Task<IReadOnlyList<CliProcess>> GetRunningProcessesAsync();
    event EventHandler<ProcessStateChangedEventArgs>? ProcessStateChanged;
    event EventHandler<LogOutputEventArgs>? LogOutput;
}

// IAcpSessionManager.cs
public interface IAcpSessionManager
{
    Task<AcpSession> CreateSessionAsync(string backendId, string? workingDirectory = null, CancellationToken ct = default);
    Task<AcpSession?> GetSessionAsync(string sessionId);
    Task<IReadOnlyList<AcpSession>> GetActiveSessionsAsync();
    Task SendPromptAsync(string sessionId, string prompt, CancellationToken ct = default);
    Task CancelPromptAsync(string sessionId, CancellationToken ct = default);
    Task SetModelAsync(string sessionId, string modelId, CancellationToken ct = default);
    Task SetModeAsync(string sessionId, string modeId, CancellationToken ct = default);
    Task CloseSessionAsync(string sessionId, CancellationToken ct = default);

    event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
    event EventHandler<AcpSessionUpdateEventArgs>? SessionUpdateReceived;
    event EventHandler<AcpPermissionRequestEventArgs>? PermissionRequested;
}
```

### 项目结构

```
AionUi/
├── src/                              # 现有前端代码（保持不变）
│   ├── process/agent/acp/            # 需要添加适配层
│   │   ├── AcpConnection.ts          # 改为调用.NET API
│   │   └── AcpConnectionAdapter.ts   # 新增：API适配器
│   └── ...                           # 其他现有代码
│
├── server-dotnet/                    # 新增：.NET后端服务
│   ├── AionUi.CliManager.Service/
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Controllers/
│   │   │   ├── BackendsController.cs
│   │   │   ├── SessionsController.cs
│   │   │   └── LogsController.cs
│   │   ├── Hubs/
│   │   │   └── AcpHub.cs
│   │   ├── Services/
│   │   │   ├── ICliProcessManager.cs
│   │   │   ├── CliProcessManager.cs
│   │   │   ├── IAcpSessionManager.cs
│   │   │   ├── AcpSessionManager.cs
│   │   │   ├── IBackendConfigurationService.cs
│   │   │   └── BackendConfigurationService.cs
│   │   ├── Acp/
│   │   │   ├── AcpProtocolHandler.cs
│   │   │   ├── Models/
│   │   │   │   ├── JsonRpcModels.cs
│   │   │   │   ├── AcpSessionModels.cs
│   │   │   │   └── AcpUpdateModels.cs
│   │   │   └── Handlers/
│   │   │       ├── FileOperationHandler.cs
│   │   │       └── PermissionHandler.cs
│   │   ├── Configuration/
│   │   │   ├── BackendConfiguration.cs
│   │   │   └── ConfigurationStore.cs
│   │   └── Logging/
│   │       ├── ICliLogger.cs
│   │       └── CliLogger.cs
│   │
│   ├── AionUi.CliManager.Core/       # 核心库（可选分离）
│   │   └── ...                       # 可被Service引用的核心逻辑
│   │
│   └── AionUi.CliManager.Service.Tests/
│       └── ...                       # 单元测试
│
└── AionUi.CliManager.sln
```

### Electron集成

```typescript
// Electron主进程启动.NET服务
// src/index.ts (Electron main process)

import { spawn } from 'child_process';
import path from 'path';

let dotnetProcess: ChildProcess | null = null;

function startDotNetService(): Promise<void> {
  return new Promise((resolve, reject) => {
    const dotnetPath = path.join(__dirname, '../server-dotnet/publish/AionUi.CliManager.Service.exe');

    dotnetProcess = spawn(dotnetPath, [], {
      env: {
        ...process.env,
        ASPNETCORE_URLS: 'http://localhost:5000',
        ASPNETCORE_ENVIRONMENT: 'production'
      }
    });

    dotnetProcess.stdout?.on('data', (data) => {
      console.log('[.NET Service]', data.toString());
      if (data.toString().includes('Now listening on')) {
        resolve();
      }
    });

    dotnetProcess.stderr?.on('data', (data) => {
      console.error('[.NET Service Error]', data.toString());
    });

    dotnetProcess.on('error', reject);

    // 超时处理
    setTimeout(() => reject(new Error('Timeout starting .NET service')), 30000);
  });
}

function stopDotNetService(): void {
  if (dotnetProcess && !dotnetProcess.killed) {
    dotnetProcess.kill();
  }
}

// 应用启动时启动.NET服务
app.whenReady().then(async () => {
  await startDotNetService();
  createWindow();
});

// 应用退出时停止.NET服务
app.on('before-quit', () => {
  stopDotNetService();
});
```

### 关键技术选型


| 组件     | 技术                       | 说明               |
| -------- | -------------------------- | ------------------ |
| Web框架  | ASP.NET Core 8/9           | 高性能Web服务      |
| 实时通信 | SignalR                    | 流式消息推送       |
| 序列化   | System.Text.Json           | 高性能JSON处理     |
| 配置存储 | 本地JSON文件               | 与现有配置格式兼容 |
| 日志     | Serilog                    | 结构化日志         |
| 进程管理 | System.Diagnostics.Process | 标准.NET进程API    |
| 依赖注入 | 内置DI容器                 | 标准做法           |

### 实施步骤

#### Phase 1: .NET后端开发（2-3周）

1. 创建.NET服务项目结构
2. 实现 `CliProcessManager` - 进程管理
3. 实现 `AcpProtocolHandler` - ACP协议通信（从C#示例扩展）
4. 实现 `AcpSessionManager` - 会话管理
5. 实现REST API Controllers
6. 实现SignalR Hub
7. 单元测试

#### Phase 2: 前端适配层（1周）

1. 创建 `AcpConnectionAdapter.ts`
2. 修改 `AcpConnection.ts` 使用适配器
3. 添加SignalR客户端依赖
4. 测试API调用和事件接收

#### Phase 3: Electron集成（3-5天）

1. 主进程启动.NET服务
2. 服务生命周期管理
3. 打包配置（包含.NET运行时）
4. 端到端测试

#### Phase 4: 云端部署支持（1周）

1. Docker容器化
2. Kubernetes配置
3. 云平台部署脚本（Azure/AWS/阿里云）
4. CI/CD流水线配置
5. 多模式切换功能

### 工作量评估


| 模块         | 预估工作量 | 说明                         |
| ------------ | ---------- | ---------------------------- |
| .NET后端服务 | 2-3周      | 核心逻辑移植，从C#示例扩展   |
| 前端适配层   | 1周        | 主要是HTTP/SignalR客户端封装 |
| Electron集成 | 3-5天      | 服务启动/停止管理            |
| 云端部署     | 1周        | Docker/K8s/云平台配置        |
| 测试验证     | 3-5天      | 集成测试                     |
| **总计**     | **5-6周**  | 比全栈重写减少约50%工作量    |

### 验证方案

#### 1. 单元测试

- .NET服务各组件单元测试
- ACP协议序列化/反序列化测试

#### 2. 集成测试

- API端点测试
- SignalR实时通信测试
- 前端→.NET服务→CLI端到端测试

#### 3. 多模式部署验证

**本地模式验证**:

```bash
# 1. 构建并启动本地服务
cd server-dotnet
dotnet run

# 2. 启动Electron应用
npm run start

# 3. 验证
# - 前端自动检测到 localhost:5000
# - 可以配置Backend并创建会话
# - 流式响应正常接收
```

**云端模式验证**:

```bash
# 1. 部署到云端（以Docker为例）
docker build -t aionui/climanager .
docker run -p 80:80 aionui/climanager

# 2. 浏览器访问
open http://localhost

# 3. 或使用Electron连接云端
# 在设置中切换为云端模式，输入服务地址
```

**混合模式验证**:

```bash
# 1. 启动云端服务
kubectl apply -f k8s-deployment.yml

# 2. Electron配置远程地址
# 设置 → 服务模式 → 云端模式
# 输入: https://aionui.yourdomain.com

# 3. 验证连接
# 检查前端是否能正常调用远程API
# 验证SignalR实时通信
```

#### 4. 功能验证清单


| 功能        | 本地模式 | 云端模式    | 混合模式 |
| ----------- | -------- | ----------- | -------- |
| Backend配置 | ✓       | ✓          | ✓       |
| 创建会话    | ✓       | ✓          | ✓       |
| 发送消息    | ✓       | ✓          | ✓       |
| 流式响应    | ✓       | ✓          | ✓       |
| 权限请求    | ✓       | ✓          | ✓       |
| 文件操作    | ✓       | ✓ (云端FS) | ✓       |
| 多用户支持  | -        | ✓          | -        |
| 会话持久化  | ✓       | ✓          | ✓       |

---

## 关键文件清单


| 源文件                                   | 作用            | .NET对应实现                         |
| ---------------------------------------- | --------------- | ------------------------------------ |
| `src/process/agent/acp/AcpConnection.ts` | ACP协议通信     | `AcpProtocolHandler.cs`              |
| `src/process/agent/acp/acpConnectors.ts` | Backend启动逻辑 | `CliProcessManager.cs`               |
| `src/process/task/AcpAgentManager.ts`    | 高层会话管理    | `AcpSessionManager.cs` + `AcpHub.cs` |
| `src/common/types/acpTypes.ts`           | 类型定义        | `Models/*.cs`                        |
| `examples/csharp-acp-client/Program.cs`  | C#示例参考      | 作为`AcpProtocolHandler` 基础        |

---

## 架构优势总结

### 1. 部署灵活性

这种架构设计提供了**三种部署模式**，用户可以根据需求自由选择：


| 场景       | 推荐模式 | 优势                                 |
| ---------- | -------- | ------------------------------------ |
| 个人开发者 | 本地模式 | 数据隐私、离线可用、响应快           |
| 团队协作   | 云端模式 | 统一配置、多用户支持、随时随地访问   |
| 企业环境   | 混合模式 | 本地CLI执行 + 云端管理、符合安全策略 |

### 2. 技术栈优势

**.NET后端的优势**:

- 高性能：ASP.NET Core的性能优于Node.js
- 类型安全：C#的强类型减少运行时错误
- 并发处理：async/await和线程池管理优于JavaScript
- 部署友好：单文件发布、Docker支持

**保持前端的优势**:

- 复用现有React组件和状态管理
- 无需学习新的UI框架
- 用户体验保持一致
- 开发效率最大化

### 3. 扩展性

**水平扩展**（云端模式）:

```
用户请求 → 负载均衡器 → .NET服务实例1
                                ↓
                         .NET服务实例2
                                ↓
                         .NET服务实例3
```

- Kubernetes自动扩缩容
- SignalR Backplane支持多实例

**垂直扩展**:

- 支持更多Backend类型（只需添加配置）
- 插件化架构支持自定义Handler

### 4. 成本控制


| 部署方式   | 成本           | 适用场景           |
| ---------- | -------------- | ------------------ |
| 本地模式   | 免费           | 个人用户、小型团队 |
| 云端自建   | 低（云服务器） | 中型团队           |
| Serverless | 按需付费       | 波动负载、原型验证 |

### 5. 迁移路径清晰

```
阶段1: 本地模式（立即可用）
   ↓
阶段2: 添加云端支持（可选）
   ↓
阶段3: 混合模式（企业需求）
```

每个阶段都是增量式改进，不会破坏现有功能。

---

## FAQ

**Q1: Electron客户端能否同时连接本地和云端服务？**
A: 可以。通过设置界面可以切换服务模式，甚至可以在不同会话中使用不同模式。

**Q2: 云端部署时，CLI进程在哪里运行？**
A: 有三种选择：

1. 云端服务器运行CLI（适合轻量级任务）
2. 本地Agent代理（混合模式，CLI在本地）
3. SSH到远程服务器执行（企业环境）

**Q3: 前端需要改动多少代码？**
A: 核心改动约10%：

- 添加API客户端封装（约500行）
- 修改AcpConnection使用API（约200行）
- 添加服务配置界面（约300行）
- 其余UI组件完全复用

**Q4: 是否支持移动端访问？**
A: 云端模式下，可以通过浏览器访问。如需原生App，可以复用.NET后端API。

**Q5: 数据安全性如何保障？**
A:

- 本地模式：数据完全在本地
- 云端模式：支持JWT认证、HTTPS加密、API限流
- 文件操作：本地模式下直接访问本地文件系统，云端模式通过SFTP/SSH

---

## 下一步行动建议

1. **快速启动**（1周）

   - 基于C#示例扩展实现基础AcpProtocolHandler
   - 创建最小可用的.NET服务（仅支持Goose或Claude）
   - 前端添加API适配层
   - 本地模式端到端验证
2. **功能完善**（3-4周）

   - 支持所有Backend类型
   - 完整的会话管理
   - 云端部署配置
3. **生产就绪**（1-2周）

   - 安全加固
   - 性能优化
   - 监控和日志
   - 文档完善

**推荐从本地模式开始**，快速验证架构可行性，然后再扩展到云端部署。
