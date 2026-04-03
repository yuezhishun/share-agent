# V4 终端布局调整经验

本文档记录 `DesktopTerminalView.vue` 中终端主区域宽度、滚动条宽度和满高布局的稳定做法。当前目标版本是宽滚动条方案，并直接使用 `"xterm": "^5.3.0"`。

除非特别说明，下文第 2-6 节描述的都是桌面端默认布局，也就是未命中 `@media (max-width: 980px)` / `@media (max-width: 820px)` 覆盖规则时的结构语义。

后续如果继续调整 V4，请以这套“宽滚动条 + 独立右侧槽位”的语义为准，不要再切回当前主线曾经使用过的窄滚动条保留宽度方案。

## 1. 核心结论

- 终端内容区宽度、滚动条槽位宽度、外层壳体宽度必须分开定义。
- `fitAddon.fit()` 只能看到“终端内容测量区”的宽高，不能把滚动条槽位一起算进去。
- 宽滚动条要占右侧独立槽位，不能只靠 `.xterm-screen` 或 `scrollbar-gutter` 预留窄保留区。
- 终端满高要靠完整 flex 链路分配剩余空间，不能加固定桌面端高度上限。

## 2. 当前稳定参数

当前 V4 使用以下常量：

```css
--terminal-display-width: 1450px;
--terminal-padding: 8px;
--terminal-scrollbar-width: 55px;
--terminal-shell-width: calc(
  var(--terminal-display-width) +
  (var(--terminal-padding) * 2) +
  var(--terminal-scrollbar-width)
);
```

语义约定：

- `--terminal-display-width`：终端文本内容区的目标上限宽度。
- `--terminal-padding`：终端壳体内边距，也参与壳体总宽度计算。
- `--terminal-scrollbar-width`：右侧宽滚动条独立槽位宽度。
- `--terminal-shell-width`：终端整体壳体上限宽度，等于内容区 + 左右 padding + 右侧滚动条槽位。

代入当前数值后可得：

- `--terminal-shell-width = 1450 + 8 * 2 + 55 = 1521px`

这里要区分三种宽度口径：

- `1450px`：理想终端文本内容区上限。
- `1521px`：终端壳体上限。
- `实际内容测量宽度`：`fitAddon.fit()` 真正用来计算列数的 `.terminal-host` 宽度。

## 3. 宽度调整原则

### 3.1 不要把滚动条宽度直接算进 xterm 内容测量区

不要改成下面这类结构：

- `.terminal-host { width: 100% }`
- `.xterm-screen { width: calc(100% - reserve) }`
- `.xterm-viewport { right: 0; width: reserve }`

这类结构适合窄滚动条保留区方案，不适合当前要保留的宽滚动条方案。

### 3.2 正确做法是把“内容测量区”和“滚动条槽位”拆开

稳定做法：

- `.terminal-panel-content` 按 `min(100%, var(--terminal-shell-width))` 收敛宽度。
- `.terminal-viewport` 占满壳体宽度，并保留四边 padding。
- `.terminal-host` 只给内容测量区宽度：

```css
.terminal-host {
  width: calc(100% - var(--terminal-scrollbar-width));
}
```

- `.xterm-viewport` 绝对定位并向右外扩出滚动条槽位：

```css
.terminal-viewport :deep(.xterm-viewport) {
  position: absolute;
  top: 0;
  right: calc(-1 * var(--terminal-scrollbar-width));
  bottom: 0;
  left: 0;
}
```

这样做的结果：

- `fitAddon.fit()` 看到的宽度就是纯内容区宽度。
- 宽滚动条落在独立右侧槽位里，点击区更容易命中。
- 文本层不会压到滚动条下面。

### 3.3 实际宽度扣减链路

当前桌面端默认布局下，实际内容宽度大致经过下面这条链路：

1. `.main` 三栏 grid 先分掉左右栏宽度：

```css
.main {
  grid-template-columns: minmax(230px, 320px) minmax(560px, 1fr) minmax(260px, 320px);
}
```

2. `.terminal-panel` 再扣掉自身水平 padding：

```css
.terminal-panel {
  padding: 12px 14px;
}
```

也就是先减 `28px`。

3. `.terminal-panel-content` 收敛到壳体上限：

```css
.terminal-panel-content {
  width: min(100%, var(--terminal-shell-width));
  max-width: var(--terminal-shell-width);
}
```

也就是：

```text
terminal-panel-content 宽度 = min(中栏可用宽度 - 28px, 1521px)
```

4. `.terminal-viewport` 再扣掉左右 padding：

```css
.terminal-viewport {
  padding: 8px;
}
```

也就是再减 `16px`。

5. `.terminal-host` 再扣掉滚动条槽位：

```css
.terminal-host {
  width: calc(100% - var(--terminal-scrollbar-width));
}
```

也就是再减 `55px`。

最终公式：

```text
实际内容测量宽度
= min(中栏可用宽度 - 28px, 1521px) - 16px - 55px
= min(中栏可用宽度 - 28px, 1521px) - 71px
```

这才是桌面端默认布局里 `fitAddon.fit()` 最终能拿去计算列数的宽度。

### 3.4 响应式断点例外

上面的宽度公式不适用于所有断点。当前代码在窄屏下有明确例外：

- `@media (max-width: 980px)` 时，`.terminal-panel` 的 padding 会从 `12px 14px` 变成 `10px`。
- 同一个断点下，`.terminal-panel-content`、`.terminal-viewport`、`.terminal-host`、`.xterm`、`.xterm-viewport` 都会统一覆盖成 `width: 100%; max-width: 100%`。
- 这意味着 `.terminal-host { width: calc(100% - var(--terminal-scrollbar-width)) }` 只应被视为桌面端默认布局规则，不是所有断点下都必须保持不变。
- `@media (max-width: 820px)` 时，`.main` 会从三栏 grid 切成纵向 flex，整体宽度来源也会随之变化。

因此，后续如果核对“宽滚动条 + 独立右侧槽位”方案是否仍然成立，必须先区分是在桌面端默认布局，还是在响应式覆盖后的移动/窄屏布局。

## 4. 测量逻辑原则

宽滚动条版本的测量语义应该是：

- 优先使用 `fitAddon.proposeDimensions()`。
- 如果 `fitAddon` 暂时拿不到稳定值，再回退到 DOM 测量。
- DOM 回退测量直接使用 `.terminal-host` 的可见宽度。
- 不再依赖 `.xterm-screen` 扣减滚动条保留区，也不再从 `.xterm-viewport` 读取 reserve width。

原因很简单：

- 这套布局里 `.terminal-host` 自身已经扣除了滚动条槽位。
- 再从 DOM 二次减去 viewport 宽度，会把滚动条宽度重复扣掉。

## 5. 高度调整原则

### 5.1 终端满高依赖完整 flex 链路

要让 V4 像旧版一样吃满中栏剩余高度，至少要满足：

```css
.terminal-panel {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.terminal-panel-content {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
}

.terminal-viewport {
  flex: 1 1 auto;
  min-height: 0;
  height: 100%;
  max-height: none;
}

.terminal-host,
.terminal-viewport :deep(.xterm) {
  height: 100%;
}
```

### 5.2 不要再加桌面端固定高度上限

曾经的问题是：

```css
max-height: min(100%, 960px);
```

这会让终端在大屏下永远停在一个固定高度附近，看起来像“没有充满中栏”。

## 6. 后续调整建议

以后如果只是微调宽滚动条版本，优先只改这三个变量：

- `--terminal-display-width`
- `--terminal-padding`
- `--terminal-scrollbar-width`

推荐方式：

- 想让文本区更宽或更窄：改 `--terminal-display-width`
- 想让滚动条更容易点中：改 `--terminal-scrollbar-width`
- 想让内容离外壳边界更远或更近：改 `--terminal-padding`

除非明确知道影响，否则不要轻易改下面这些桌面端默认布局规则：

- `.terminal-host { width: calc(100% - var(--terminal-scrollbar-width)) }`
- `.xterm-viewport` 的绝对定位和负 `right`
- `.terminal-panel-content` / `.terminal-viewport` 的 `flex: 1 1 auto` 与 `min-height: 0`
- DOM 回退测量直接读取 `hostElement.getBoundingClientRect()`

响应式断点下当前允许的例外是：

- `<=980px` 时，宽度相关节点统一回退到 `width: 100%`
- `<=820px` 时，整体主布局切成纵向堆叠

## 7. 验收检查清单

每次调整后至少检查：

- 桌面宽屏下终端不会压住右栏。
- 右侧宽滚动条看得到，也能直接点中和拖拽。
- 文本选择区不会覆盖滚动条槽位。
- `fit()` 后列数稳定，没有明显横向抖动。
- `fitAddon` 失败时，DOM 回退测量仍能得到正确列数。
- 终端主体高度吃满中栏剩余空间。
- `1380px / 980px / 820px / 640px` 断点没有明显回归。
