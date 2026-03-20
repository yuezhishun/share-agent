# V4 终端布局调整经验

本文档记录 `DesktopTerminalViewV4.vue` 中终端主区域宽度、滚动条宽度和满高布局的稳定做法，避免后续再次把 xterm 测量区、可视内容区、滚动条槽位混在一起。

## 1. 核心结论

- 终端内容区宽度、滚动条槽位宽度、外层壳体宽度必须分开定义。
- `fitAddon.fit()` 只能看到“终端内容测量区”的宽高，不能把滚动条槽位一起算进去。
- 滚动条要占右侧独立槽位，不能仅靠给 `.xterm-viewport` 加 `padding-right` 伪造，否则容易出现“看得到滚动条，但鼠标点不中”的覆盖问题。
- 终端满高要靠整条 flex 链路分配剩余空间，不能再加固定 `max-height` 上限，否则桌面端永远不可能真正吃满中栏。

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
- `--terminal-padding`：终端外壳内边距，不参与 xterm 列数语义，只参与视觉留白。
- `--terminal-scrollbar-width`：右侧滚动条独立槽位宽度。
- `--terminal-shell-width`：中栏终端整体外壳上限宽度，等于内容区 + 两侧 padding + 滚动条槽位。

代入当前数值后可得：

- `--terminal-shell-width = 1450 + 8 * 2 + 55 = 1521px`

这里要特别注意三种不同口径：

- `1450px`：理想情况下，终端文本内容区能拿到的目标上限。
- `1521px`：终端壳体上限，包含内容区、左右 padding 和滚动条槽位。
- `实际内容测量宽度`：`fitAddon.fit()` 真正看到的 `.terminal-host` 宽度，通常小于 `1450px`，因为还会继续被三栏布局和中栏内边距压缩。

不要把上面三者混为一谈。肉眼看到“终端主区差不多有 1430px”时，往往看到的是中栏壳体或其近似视觉宽度，不是 xterm 真正用来算列数的内容测量宽度。

## 3. 宽度调整原则

### 3.1 不要把滚动条宽度直接加到 xterm 测量区

错误方向：

- `.terminal-host { width: display + scrollbar }`
- `.xterm { width: display + scrollbar }`
- `.xterm-viewport { width: display + scrollbar; padding-right: scrollbar }`

这会带来两个问题：

- `fit()` 会按“内容区 + 滚动条区”一起计算列数，导致文字区域延伸到滚动条下面。
- 中栏在宽屏下容易超出 grid 列宽，被右栏遮挡。

### 3.2 正确做法是把“内容测量区”和“滚动条槽位”拆开

稳定做法：

- 外层 `.terminal-panel-content` 按 `min(100%, var(--terminal-shell-width))` 收敛宽度。
- `.terminal-viewport` 占满外层宽度，但只负责壳体和 padding。
- `terminalRef` 对应的 `.terminal-host` 只给内容测量区宽度：

```css
.terminal-host {
  width: calc(100% - var(--terminal-scrollbar-width));
}
```

- `.xterm-viewport` 绝对定位并向右扩出滚动条槽位：

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
- 滚动条落在独立右槽位里，不再和文字选择层重叠。
- 修改滚动条宽度时，只需要改 `--terminal-scrollbar-width`，布局语义保持一致。

### 3.3 实际宽度扣减链路

当前 V4 代码里，桌面端实际宽度不是直接等于 `--terminal-display-width`，而是会经历下面这条扣减链路：

1. `.main` 是三栏 grid：

```css
.main {
  grid-template-columns: minmax(230px, 320px) minmax(560px, 1fr) minmax(260px, 320px);
}
```

左右栏先占掉一部分屏幕宽度，中栏只拿剩余空间。

2. `.terminal-panel` 再扣掉自身水平 padding：

```css
.terminal-panel {
  padding: 12px 14px;
}
```

也就是中栏内容先减去 `28px`。

3. `.terminal-panel-content` 只允许自己收敛到壳体上限：

```css
.terminal-panel-content {
  width: min(100%, var(--terminal-shell-width));
  max-width: var(--terminal-shell-width);
}
```

所以这里只能得到：

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

5. `.terminal-host` 再扣掉滚动条独立槽位：

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

这才是 `fitAddon.fit()` 最终能拿去计算列数的宽度。

### 3.4 为什么会看到“壳体很宽，但内容只有约 1210px”

如果桌面端视口宽度是 `1920px`，并且左右栏都接近各自上限：

- 左栏约 `320px`
- 右栏约 `320px`
- 中栏约 `1920 - 320 - 320 = 1280px`
- 再减 `.terminal-panel` 水平 padding `28px`
- 得到 `.terminal-panel-content ≈ 1252px`
- 再减 `.terminal-viewport` padding `16px`
- 再减滚动条槽位 `55px`
- 最终 `.terminal-host ≈ 1181px`

所以在 `1920px` 这一档，实际内容区本来就不可能接近 `1450px`。

再看几个典型宽度：

- `1920px` 视口：实际内容区约 `1181px`
- `2000px` 视口：实际内容区约 `1261px`
- `2160px` 视口：实际内容区约 `1421px`
- `2300px` 以上：实际内容区才可能逼近或到达 `1450px` 上限

因此，如果你在页面上“看起来”看到大约 `1430px` 的终端主区域，但真正能显示文本的宽度只有 `1210px` 左右，这通常不是 bug，而是：

- 你看到的是壳体或中栏视觉宽度；
- `fit()` 实际使用的是扣除了 padding 和滚动条槽位后的 `.terminal-host` 宽度；
- 三栏布局本身也会先吃掉大量可用空间。

## 4. 高度调整原则

### 4.1 终端满高依赖完整 flex 链路

要让 V4 像 V3 一样吃满中栏剩余高度，至少要满足：

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

### 4.2 不要再加桌面端固定高度上限

曾经的问题是：

```css
max-height: min(100%, 960px);
```

这会让终端主区在大屏下永远停在 960px 左右，视觉上看起来像“没有充满屏幕”。桌面端需要满高时，应去掉这个上限，交给父层剩余高度分配。

## 5. 后续调整建议

以后如果只想微调视觉比例，优先只改这三个变量：

- `--terminal-display-width`
- `--terminal-padding`
- `--terminal-scrollbar-width`

推荐调整方式：

- 想让终端文字区更窄或更宽：改 `--terminal-display-width`
- 想让滚动条更容易点中：改 `--terminal-scrollbar-width`
- 想让内容离边框更远或更近：改 `--terminal-padding`

但要注意：

- 只改 `--terminal-display-width`，并不能保证当前桌面宽度下实际内容区真的变成对应数值。
- 如果实际瓶颈来自三栏 grid 分配，那么应该先看左右栏宽度和中栏 padding，而不是误以为 `fit()` 自己少算了。

不要轻易改下面这些结构规则，除非明确知道会影响 `fit()`：

- `.terminal-host { width: calc(100% - var(--terminal-scrollbar-width)) }`
- `.xterm-viewport` 的绝对定位和负 `right`
- `.terminal-panel-content` / `.terminal-viewport` 的 `flex: 1 1 auto` 与 `min-height: 0`

## 6. 验收检查清单

每次调整后至少检查：

- 桌面宽屏下终端不会压住右栏。
- 右侧滚动条看得到，也能直接点中和拖拽。
- 文本选择区不会覆盖滚动条槽位。
- `fit()` 后列数稳定，没有明显横向抖动。
- 终端主体高度吃满中栏剩余空间。
- `1380px / 980px / 820px / 640px` 断点没有明显回归。
