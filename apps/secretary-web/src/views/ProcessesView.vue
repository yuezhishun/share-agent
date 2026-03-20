<template>
  <div class="proc-shell">
    <aside class="proc-sidebar">
      <div class="panel-header">
        <div>
          <div class="panel-eyebrow">Processes</div>
          <h1>进程</h1>
        </div>
        <button
          type="button"
          class="icon-btn"
          title="刷新列表"
          :disabled="processStore.loadingList"
          @click="refreshCurrent"
        >
          <i class="fa-solid fa-rotate" />
        </button>
      </div>

      <div class="panel-status">
        <span>{{ processStore.status || '就绪' }}</span>
        <span v-if="processStore.error" class="panel-error">{{ processStore.error }}</span>
      </div>

      <section class="node-panel">
        <div class="node-panel-top">
          <span class="panel-eyebrow">Target Node</span>
          <span class="status-pill" :class="processStore.isSelectedNodeOnline ? 'completed' : 'failed'">
            {{ processStore.isSelectedNodeOnline ? 'online' : 'offline' }}
          </span>
        </div>
        <select
          class="node-select"
          :value="processStore.selectedNodeId"
          :disabled="processStore.loadingNodes"
          @change="changeNode($event.target.value)"
        >
          <option v-for="node in processStore.nodes" :key="node.node_id" :value="node.node_id">
            {{ formatNodeLabel(node) }}
          </option>
        </select>
        <div v-if="processStore.selectedNode" class="node-meta">
          <span>{{ processStore.selectedNode.node_role || 'node' }}</span>
          <span>{{ processStore.selectedNode.instance_count ?? 0 }} instances</span>
        </div>
      </section>

      <div v-if="processStore.sortedItems.length === 0" class="panel-empty">暂无已管理进程</div>
      <div v-else class="proc-list">
        <div
          v-for="item in processStore.sortedItems"
          :key="item.processId"
          class="proc-item"
          :class="{ active: item.processId === processStore.selectedProcessId }"
          @click="selectProcess(item.processId)"
        >
          <div class="proc-item-main">
            <span class="proc-item-command">{{ formatCommand(item.command) }}</span>
          </div>
          <div class="proc-item-side">
            <span class="status-pill" :class="statusClass(item.status)">{{ item.status || 'unknown' }}</span>
            <div class="proc-item-hover-meta" :title="`${item.processId} · ${formatDateTime(item.startTime)}`">
              <span class="proc-item-meta">{{ item.processId }}</span>
              <span class="proc-item-meta">{{ formatDateTime(item.startTime) }}</span>
            </div>
          </div>
          <button
            type="button"
            class="icon-btn danger-icon proc-delete-btn"
            title="删除进程"
            :disabled="isRemovingItem(item.processId)"
            @click.stop="removeProcess(item.processId)"
          >
            <i class="fa-regular fa-trash-can" />
          </button>
        </div>
      </div>
    </aside>

    <main class="proc-main">
      <section class="proc-topbar">
        <div class="proc-topbar-copy">
          <span class="panel-eyebrow">Launcher</span>
          <strong>{{ selectedTitle }}</strong>
          <span class="proc-topbar-subtitle">{{ selectedSubtitle }}</span>
        </div>
        <div class="proc-topbar-actions">
          <button
            type="button"
            class="icon-btn"
            title="等待完成"
            :disabled="!processStore.hasSelectedProcess || processStore.waiting || !processStore.isSelectedNodeOnline"
            @click="waitForProcess"
          >
            <i class="fa-regular fa-hourglass-half" />
          </button>
          <button
            type="button"
            class="icon-btn danger-icon"
            title="停止进程"
            :disabled="!processStore.hasSelectedProcess || processStore.stopping || !processStore.isSelectedNodeOnline"
            @click="stopProcess"
          >
            <i class="fa-solid fa-stop" />
          </button>
        </div>
      </section>

      <section class="command-bar">
        <textarea
          ref="commandInputRef"
          v-model="processStore.form.commandLine"
          rows="2"
          class="command-input"
          placeholder='输入完整命令，如 bash -lc "npm run dev"'
          @keydown.enter.exact.prevent="startProcess"
        />
        <div class="command-actions">
          <button type="button" class="icon-btn icon-only primary" title="运行" :disabled="processStore.submitting || !processStore.isSelectedNodeOnline" @click="startProcess">
            <i class="fa-solid fa-play" />
          </button>
          <button type="button" class="icon-btn icon-only" title="重置" :disabled="processStore.submitting" @click="processStore.resetForm()">
            <i class="fa-solid fa-rotate-left" />
          </button>
        </div>
      </section>

      <section class="output-panel">
        <div class="output-toolbar">
          <div class="filter-group">
            <button
              v-for="item in filters"
              :key="item.value"
              type="button"
              class="filter-btn"
              :class="{ active: outputFilter === item.value }"
              @click="outputFilter = item.value"
            >
              {{ item.label }}
            </button>
          </div>
          <button
            type="button"
            class="icon-btn toggle-chip"
            :class="{ active: autoScroll }"
            :aria-pressed="autoScroll"
            :title="autoScroll ? '关闭自动滚动' : '开启自动滚动'"
            @click="autoScroll = !autoScroll"
          >
            <i class="fa-solid fa-arrows-down-to-line" />
          </button>
        </div>

        <div ref="outputScrollerRef" class="output-scroller">
          <div v-if="!processStore.hasSelectedProcess" class="panel-empty">启动或选择一个进程后，这里会显示 stdout / stderr / system 输出。</div>
          <div v-else-if="filteredOutputItems.length === 0" class="panel-empty">当前过滤条件下没有输出内容</div>
          <article
            v-for="(item, index) in filteredOutputItems"
            :key="`${item.timestamp}-${item.outputType}-${index}`"
            class="output-entry"
            :class="typeClass(item.outputType)"
          >
            <div class="output-entry-meta">
              <span>{{ formatDateTime(item.timestamp) }}</span>
              <span>{{ item.outputType }}</span>
            </div>
            <pre class="output-entry-content">{{ item.content }}</pre>
          </article>
        </div>
      </section>
    </main>

    <aside class="proc-rightbar">
      <section class="side-card">
        <div class="side-title-row">
          <div class="side-title">快捷指令</div>
          <button
            type="button"
            class="icon-btn"
            title="新增快捷指令"
            :aria-pressed="showSettingsForm"
            @click="toggleSettingsForm"
          >
            <i :class="showSettingsForm ? 'fa-solid fa-minus' : 'fa-solid fa-plus'" />
          </button>
        </div>
        <div v-if="showSettingsForm" class="settings-form-panel">
          <div class="settings-form-header">
            <div class="side-title">启动参数</div>
            <span class="side-note">保存后用于后续运行和快捷指令启动</span>
          </div>
          <div class="settings-form">
            <label class="field field-full">
              <span>工作目录</span>
              <select ref="settingsFirstInputRef" v-model="settingsDraft.cwd" :disabled="cwdOptionsLoading || cwdOptions.length === 0">
                <option v-for="item in cwdOptions" :key="item.path" :value="item.path">
                  {{ item.label }}
                </option>
              </select>
            </label>
            <div v-if="cwdOptionsError" class="panel-error field-full">{{ cwdOptionsError }}</div>

            <label class="field">
              <span>超时秒数</span>
              <input v-model="settingsDraft.timeoutSeconds" type="text" inputmode="numeric" placeholder="300" />
            </label>

            <label class="field field-full">
              <span>环境变量 JSON</span>
              <textarea
                v-model="settingsDraft.envInput"
                rows="2"
                placeholder='{"FOO":"bar"}'
              />
            </label>

            <label class="field field-full">
              <span>启动时 stdin</span>
              <textarea
                v-model="settingsDraft.stdin"
                rows="2"
                placeholder="一次性写入 stdin，不是交互式输入。"
              />
            </label>
          </div>
          <div class="settings-actions">
            <button
              type="button"
              class="primary"
              :disabled="processStore.submitting"
              @click="saveSettingsForm"
            >
              保存
            </button>
            <button
              type="button"
              :disabled="processStore.submitting"
              @click="cancelSettingsForm"
            >
              取消
            </button>
          </div>
        </div>
        <div class="shortcut-groups">
          <div v-for="group in shortcutGroups" :key="group.title" class="shortcut-group">
            <div class="shortcut-group-title">{{ group.title }}</div>
            <div class="shortcut-grid">
              <button
                v-for="item in group.items"
                :key="item.label"
                type="button"
                class="shortcut-btn"
                @click="applyShortcut(item.command)"
              >
                <span>{{ item.label }}</span>
                <small>{{ item.command }}</small>
              </button>
            </div>
          </div>
        </div>
      </section>

      <section v-if="processStore.selectedProcess" class="side-card">
        <div class="side-title">当前进程</div>
        <div class="summary-grid">
          <div class="summary-item">
            <span class="summary-label">节点</span>
            <span class="summary-value">{{ processStore.selectedNode?.node_name || processStore.selectedNodeId || '-' }}</span>
          </div>
          <div class="summary-item">
            <span class="summary-label">状态</span>
            <span class="summary-value">{{ processStore.selectedProcess.status || 'unknown' }}</span>
          </div>
          <div class="summary-item">
            <span class="summary-label">开始</span>
            <span class="summary-value">{{ formatDateTime(processStore.selectedProcess.startTime) }}</span>
          </div>
          <div class="summary-item">
            <span class="summary-label">耗时</span>
            <span class="summary-value">{{ formatDuration(processStore.selectedProcess.durationMs) }}</span>
          </div>
          <div class="summary-item">
            <span class="summary-label">退出码</span>
            <span class="summary-value">{{ processStore.selectedProcess.result?.exitCode ?? '-' }}</span>
          </div>
        </div>
      </section>
    </aside>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { useWebCliProcessesStore } from '../stores/webcli-processes.js';

const processStore = useWebCliProcessesStore();
const outputFilter = ref('all');
const autoScroll = ref(true);
const outputScrollerRef = ref(null);
const commandInputRef = ref(null);
const settingsFirstInputRef = ref(null);
const showSettingsForm = ref(false);
const defaultCwdPath = '/home/yueyuan';
const cwdOptions = ref([]);
const cwdOptionsLoading = ref(false);
const cwdOptionsError = ref('');
const settingsDraft = reactive(buildSettingsDraft());

const filters = [
  { label: '全部', value: 'all' },
  { label: 'stdout', value: 'standardoutput' },
  { label: 'stderr', value: 'standarderror' },
  { label: 'system', value: 'systemmessage' }
];

const shortcutGroups = [
  {
    title: 'Shell',
    items: [
      { label: 'Bash', command: 'bash' },
      { label: 'Shell', command: 'sh' },
      { label: 'Login Shell', command: 'bash -l' },
      { label: 'Env', command: 'env' }
    ]
  },
  {
    title: 'Node',
    items: [
      { label: 'npm dev', command: 'bash -lc "npm run dev"' },
      { label: 'npm build', command: 'bash -lc "npm run build"' },
      { label: 'npm test', command: 'bash -lc "npm test"' },
      { label: 'pnpm dev', command: 'bash -lc "pnpm dev"' }
    ]
  },
  {
    title: '.NET',
    items: [
      { label: 'dotnet run', command: 'dotnet run' },
      { label: 'dotnet test', command: 'dotnet test -v minimal' },
      { label: 'build', command: 'dotnet build' },
      { label: 'restore', command: 'dotnet restore' }
    ]
  }
];

const filteredOutputItems = computed(() => {
  if (outputFilter.value === 'all') {
    return processStore.outputItems;
  }
  return processStore.outputItems.filter((item) => String(item.outputType || '').toLowerCase() === outputFilter.value);
});

const selectedTitle = computed(() => {
  if (!processStore.selectedProcess) {
    return '输入命令后直接启动进程';
  }
  return formatCommand(processStore.selectedProcess.command);
});

const selectedSubtitle = computed(() => {
  if (!processStore.selectedProcess) {
    return `${processStore.selectedNode?.node_name || processStore.selectedNodeId || '未选择节点'} · Enter 直接运行，下面区域持续显示输出。`;
  }
  return `${processStore.selectedNode?.node_name || processStore.selectedNodeId || 'node'} · ${processStore.selectedProcess.processId} · ${processStore.selectedProcess.status || 'unknown'}`;
});

function formatDateTime(value) {
  if (!value) {
    return '-';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(date);
}

function formatDuration(value) {
  const total = Number(value || 0);
  if (!Number.isFinite(total) || total <= 0) {
    return '-';
  }
  if (total < 1000) {
    return `${Math.round(total)} ms`;
  }
  if (total < 60000) {
    return `${(total / 1000).toFixed(1)} s`;
  }
  return `${(total / 60000).toFixed(1)} min`;
}

function formatCommand(value) {
  return String(value || '').trim() || '-';
}

function formatNodeLabel(node) {
  if (!node) {
    return '未知节点';
  }
  const name = String(node.node_name || node.node_id || 'node').trim();
  const role = String(node.node_role || '').trim();
  const status = node.node_online === false ? 'offline' : 'online';
  return role ? `${name} · ${role} · ${status}` : `${name} · ${status}`;
}

function statusClass(status) {
  const value = String(status || '').toLowerCase();
  if (value === 'running') {
    return 'running';
  }
  if (value === 'completed') {
    return 'completed';
  }
  if (value === 'failed' || value === 'timedout') {
    return 'failed';
  }
  return 'pending';
}

function isRemovingItem(processId) {
  return processStore.removingProcessId === String(processId || '').trim();
}

function typeClass(outputType) {
  const value = String(outputType || '').toLowerCase();
  if (value === 'standarderror') {
    return 'stderr';
  }
  if (value === 'systemmessage') {
    return 'system';
  }
  return 'stdout';
}

function applyShortcut(command) {
  processStore.form.commandLine = command;
  focusCommandInput();
}

function resolveHttpBase() {
  return String(import.meta.env?.VITE_WEBPTY_BASE || '/web-pty').trim();
}

function buildApiPath(pathname, params) {
  const base = resolveHttpBase();
  const url = new URL(`${base}${pathname}`, typeof window !== 'undefined' ? window.location.origin : 'http://127.0.0.1');
  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value === undefined || value === null || value === '') {
        continue;
      }
      url.searchParams.set(key, String(value));
    }
  }
  return `${url.pathname}${url.search}`;
}

async function parseErrorMessage(response, fallback) {
  const text = await response.text();
  if (!text) {
    return fallback;
  }
  try {
    const payload = JSON.parse(text);
    return String(payload?.error || text);
  } catch {
    return text;
  }
}

function normalizeSelectableCwd(value) {
  const text = String(value || '').trim();
  if (!text) {
    return defaultCwdPath;
  }
  return cwdOptions.value.some((item) => item.path === text) ? text : defaultCwdPath;
}

async function loadCwdOptions(nodeId = processStore.selectedNodeId) {
  const targetNodeId = String(nodeId || '').trim();
  if (!targetNodeId) {
    cwdOptions.value = [{
      path: defaultCwdPath,
      label: defaultCwdPath.split('/').filter(Boolean).pop() || defaultCwdPath
    }];
    settingsDraft.cwd = defaultCwdPath;
    processStore.form.cwd = defaultCwdPath;
    cwdOptionsError.value = '';
    cwdOptionsLoading.value = false;
    return;
  }
  cwdOptionsLoading.value = true;
  cwdOptionsError.value = '';
  try {
    const response = await fetch(buildApiPath(`/api/nodes/${encodeURIComponent(targetNodeId)}/files/list`, {
      path: defaultCwdPath
    }));
    if (!response.ok) {
      throw new Error(await parseErrorMessage(response, `load cwd options failed: ${response.status}`));
    }
    const payload = await response.json();
    const items = Array.isArray(payload?.items) ? payload.items : [];
    cwdOptions.value = items
      .filter((item) => item?.kind === 'dir')
      .map((item) => ({
        path: String(item.path || '').trim(),
        label: String(item.name || item.path || '').trim() || String(item.path || '').trim()
      }))
      .filter((item) => item.path);
    if (!cwdOptions.value.some((item) => item.path === defaultCwdPath)) {
      cwdOptions.value.unshift({
        path: defaultCwdPath,
        label: defaultCwdPath.split('/').filter(Boolean).pop() || defaultCwdPath
      });
    }
    settingsDraft.cwd = normalizeSelectableCwd(settingsDraft.cwd || processStore.form.cwd);
    processStore.form.cwd = normalizeSelectableCwd(processStore.form.cwd);
  } catch (error) {
    cwdOptionsError.value = String(error?.message || error || 'load cwd options failed');
    cwdOptions.value = [{
      path: defaultCwdPath,
      label: defaultCwdPath.split('/').filter(Boolean).pop() || defaultCwdPath
    }];
    settingsDraft.cwd = defaultCwdPath;
    processStore.form.cwd = defaultCwdPath;
  } finally {
    cwdOptionsLoading.value = false;
  }
}

function buildSettingsDraft() {
  return {
    cwd: normalizeSelectableCwd(processStore.form.cwd),
    timeoutSeconds: formatTimeoutSeconds(processStore.form.timeoutMs),
    envInput: processStore.form.envInput,
    stdin: processStore.form.stdin
  };
}

function formatTimeoutSeconds(timeoutMs) {
  const value = Number(String(timeoutMs || '').trim());
  if (!Number.isFinite(value) || value <= 0) {
    return '';
  }
  return String(Math.round(value / 1000));
}

function syncSettingsDraft() {
  Object.assign(settingsDraft, buildSettingsDraft());
}

function saveSettingsForm() {
  const rawTimeoutSeconds = String(settingsDraft.timeoutSeconds || '').trim();
  let timeoutMs = '';
  if (rawTimeoutSeconds) {
    const seconds = Number(rawTimeoutSeconds);
    if (!Number.isFinite(seconds) || seconds <= 0) {
      processStore.setError('超时时间必须是正整数秒');
      return;
    }
    timeoutMs = String(Math.round(seconds * 1000));
  }

  processStore.form.cwd = normalizeSelectableCwd(settingsDraft.cwd);
  processStore.form.timeoutMs = timeoutMs;
  processStore.form.allowNonZeroExitCode = true;
  processStore.form.envInput = String(settingsDraft.envInput || '');
  processStore.form.stdin = String(settingsDraft.stdin || '');
  processStore.setError('');
  processStore.setStatus('已保存启动参数');
  showSettingsForm.value = false;
}

function cancelSettingsForm() {
  syncSettingsDraft();
  showSettingsForm.value = false;
  processStore.setError('');
}

function focusCommandInput() {
  commandInputRef.value?.focus();
}

function toggleSettingsForm() {
  showSettingsForm.value = !showSettingsForm.value;
  if (showSettingsForm.value) {
    syncSettingsDraft();
    nextTick(() => {
      settingsFirstInputRef.value?.focus();
    });
  }
}

async function scrollOutputToBottom() {
  await nextTick();
  if (!autoScroll.value) {
    return;
  }
  const el = outputScrollerRef.value;
  if (!el) {
    return;
  }
  el.scrollTop = el.scrollHeight;
}

async function startProcess() {
  try {
    await processStore.createProcess();
    await scrollOutputToBottom();
  } catch {
  }
}

async function selectProcess(processId) {
  try {
    await processStore.selectProcess(processId);
    await scrollOutputToBottom();
  } catch {
  }
}

async function refreshCurrent() {
  try {
    await processStore.loadNodes();
    await processStore.refreshSelected();
  } catch {
  }
}

async function changeNode(nodeId) {
  try {
    await processStore.setSelectedNode(nodeId);
    await loadCwdOptions(processStore.selectedNodeId);
    await scrollOutputToBottom();
  } catch {
  }
}

async function waitForProcess() {
  try {
    await processStore.waitForSelected();
  } catch {
  }
}

async function stopProcess() {
  try {
    await processStore.stopSelected();
  } catch {
  }
}

async function removeProcess(processId) {
  const id = String(processId || '').trim();
  if (!id) {
    return;
  }
  if (!window.confirm(`确认删除进程 ${id}？`)) {
    return;
  }
  try {
    await processStore.removeProcess(id);
  } catch {
  }
}

watch(
  () => filteredOutputItems.value.length,
  () => {
    scrollOutputToBottom().catch(() => {});
  }
);

onMounted(async () => {
  syncSettingsDraft();
  await processStore.initialize();
  await loadCwdOptions(processStore.selectedNodeId);
});

onBeforeUnmount(() => {
  processStore.dispose();
});
</script>

<style scoped>
.proc-shell {
  min-height: 100vh;
  min-height: 100dvh;
  display: grid;
  grid-template-columns: minmax(240px, 300px) minmax(0, 1fr) minmax(280px, 340px);
  background: #111315;
  color: #d7d7d7;
}

.proc-sidebar,
.proc-rightbar {
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 12px;
  background: #1e1e1e;
}

.proc-sidebar {
  border-right: 1px solid #343434;
}

.proc-rightbar {
  border-left: 1px solid #343434;
  overflow-y: auto;
}

.proc-main {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding: 12px 14px;
  gap: 12px;
  background: #1e1e1e;
}

.proc-topbar,
.command-bar,
.output-panel,
.side-card {
  border: 1px solid #343434;
  border-radius: 8px;
  background: #252526;
}

.proc-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 12px;
}

.proc-topbar-copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.proc-topbar-copy strong,
.proc-topbar-subtitle,
.proc-item-command,
.summary-value,
.output-entry-content,
.command-input,
.field input,
.field select,
.field textarea,
.shortcut-btn small {
  font-family: 'JetBrains Mono', monospace;
}

.proc-topbar-copy strong {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.proc-topbar-subtitle,
.panel-status,
.summary-label,
.output-entry-meta,
.proc-item-meta,
.field span,
.side-note {
  color: #9fa6ad;
}

.proc-topbar-actions,
.command-actions,
.output-toolbar,
.filter-group,
.side-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.panel-eyebrow {
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: #7fa7d7;
}

.panel-header h1,
.side-title {
  margin: 0;
  font-size: 0.96rem;
}

.node-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px solid #343434;
  border-radius: 8px;
  background: #252526;
}

.node-panel-top,
.node-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.node-meta {
  color: #9fa6ad;
  font-size: 0.75rem;
}

.node-select {
  width: 100%;
  border: 1px solid #3f3f46;
  border-radius: 8px;
  background: #111315;
  color: #edf2f7;
  padding: 10px 11px;
  font-size: 0.82rem;
}

.panel-error {
  color: #f48771;
}

.panel-empty {
  color: #8f969d;
  padding: 16px 12px;
  border: 1px dashed #3b3b3b;
  border-radius: 8px;
  font-size: 0.84rem;
}

button {
  border: 1px solid #414141;
  background: #2d2d2d;
  color: #d7d7d7;
  border-radius: 8px;
  padding: 7px 12px;
  cursor: pointer;
}

button:hover {
  background: #353535;
}

button:disabled {
  opacity: 0.55;
  cursor: default;
}

.icon-btn {
  width: 34px;
  height: 34px;
  padding: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
}

.icon-only {
  width: 40px;
  height: 40px;
}

.danger-icon:hover:not(:disabled) {
  color: #f2a194;
  border-color: rgba(244, 135, 113, 0.5);
}

.primary {
  background: #0e639c;
  border-color: #0e639c;
  color: #fff;
}

.primary:hover {
  background: #1177bb;
}

.proc-list {
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.proc-item {
  width: 100%;
  padding: 10px 12px;
  display: flex;
  align-items: center;
  gap: 8px;
  background: #252526;
  border: 1px solid #414141;
  border-radius: 8px;
  cursor: pointer;
}

.proc-item.active {
  background: #134a74;
  border-color: #0e639c;
}

.proc-item:hover {
  background: #2c2d2f;
}

.proc-item-main {
  min-width: 0;
  flex: 1;
  display: flex;
  align-items: center;
}

.proc-item-side,
.proc-item-hover-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex: 0 0 auto;
}

.proc-item-side {
  min-width: fit-content;
}

.proc-item-command {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.proc-item-meta {
  font-size: 0.74rem;
  color: #9fa6ad;
  white-space: nowrap;
}

.proc-item-hover-meta {
  display: none;
}

.proc-delete-btn {
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.16s ease;
}

.proc-item:hover .proc-delete-btn,
.proc-item:focus-within .proc-delete-btn {
  opacity: 1;
  pointer-events: auto;
}

.status-pill {
  flex: 0 0 auto;
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  border: 1px solid transparent;
}

.status-pill.running {
  background: rgba(87, 171, 90, 0.15);
  color: #8fd18a;
  border-color: rgba(87, 171, 90, 0.3);
}

.status-pill.completed {
  background: rgba(64, 140, 255, 0.15);
  color: #8fbcff;
  border-color: rgba(64, 140, 255, 0.3);
}

.status-pill.failed {
  background: rgba(244, 135, 113, 0.15);
  color: #f2a194;
  border-color: rgba(244, 135, 113, 0.3);
}

.status-pill.pending {
  background: rgba(191, 191, 191, 0.12);
  color: #c9c9c9;
  border-color: rgba(191, 191, 191, 0.24);
}

.command-bar {
  display: flex;
  align-items: stretch;
  gap: 10px;
  padding: 12px;
}

.command-input {
  flex: 1;
  min-height: 72px;
  width: 100%;
  border: 1px solid #3f3f46;
  border-radius: 8px;
  resize: none;
  background: #111315;
  color: #edf2f7;
  padding: 12px;
  outline: none;
  font-size: 0.88rem;
  line-height: 1.5;
}

.command-input:focus,
.field input:focus,
.field select:focus,
.field textarea:focus {
  border-color: #0e639c;
  box-shadow: 0 0 0 2px rgba(14, 99, 156, 0.18);
}

.command-actions {
  flex-direction: column;
  justify-content: flex-end;
}

.output-panel {
  min-height: 0;
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.output-toolbar {
  justify-content: space-between;
  padding: 10px 12px;
  border-bottom: 1px solid #343434;
}

.filter-btn.active,
.toggle-chip.active {
  background: #134a74;
  border-color: #0e639c;
  color: #fff;
}

.toggle-chip.active i {
  color: #fff;
}

.output-scroller {
  min-height: 0;
  flex: 1;
  overflow-y: auto;
  padding: 12px;
  background: #0d0d0d;
}

.output-entry {
  padding: 10px 12px;
  border-radius: 8px;
  border: 1px solid #242424;
  background: #151515;
}

.output-entry + .output-entry {
  margin-top: 10px;
}

.output-entry.stderr {
  border-color: rgba(244, 135, 113, 0.28);
}

.output-entry.system {
  border-color: rgba(143, 188, 255, 0.28);
}

.output-entry-content {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  color: #e3e3e3;
  font-size: 0.83rem;
  line-height: 1.55;
}

.side-card {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.side-title-row {
  justify-content: space-between;
}

.shortcut-groups,
.shortcut-group,
.settings-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.shortcut-group-title {
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #95b5d8;
}

.shortcut-grid {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.shortcut-btn {
  width: 100%;
  min-height: 44px;
  text-align: left;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
  white-space: normal;
}

.shortcut-btn span,
.summary-value {
  color: #e6e6e6;
}

.shortcut-btn small {
  display: block;
  color: #9fa6ad;
  font-size: 0.72rem;
  line-height: 1.35;
  white-space: normal;
  overflow-wrap: anywhere;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.settings-form-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 12px;
  border: 1px solid #343434;
  border-radius: 8px;
  background: #1f2123;
}

.settings-form-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.settings-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.field input,
.field select,
.field textarea {
  width: 100%;
  border: 1px solid #3f3f46;
  border-radius: 8px;
  background: #111315;
  color: #edf2f7;
  padding: 10px 11px;
  font-size: 0.82rem;
  line-height: 1.45;
}

.field textarea {
  resize: vertical;
  min-height: 3.8rem;
}

.field-full {
  grid-column: 1 / -1;
}

.settings-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.summary-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px;
  border-radius: 8px;
  background: #1b1b1c;
  border: 1px solid #323232;
}

@media (max-width: 1180px) {
  .proc-shell {
    grid-template-columns: minmax(220px, 280px) minmax(0, 1fr);
  }

  .proc-rightbar {
    grid-column: 1 / -1;
    border-left: none;
    border-top: 1px solid #343434;
  }
}

@media (max-width: 860px) {
  .proc-shell {
    display: flex;
    flex-direction: column;
    min-height: 100vh;
    min-height: 100dvh;
  }

  .proc-sidebar,
  .proc-rightbar,
  .proc-main {
    border: none;
  }

  .proc-sidebar,
  .proc-rightbar {
    padding-bottom: 0;
  }

  .command-bar,
  .proc-topbar,
  .output-toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .command-actions,
  .proc-topbar-actions {
    flex-direction: row;
  }

  .settings-form {
    grid-template-columns: 1fr;
  }

  .summary-grid {
    grid-template-columns: 1fr;
  }
}
</style>
