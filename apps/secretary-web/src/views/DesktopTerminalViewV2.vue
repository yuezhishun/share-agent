<template>
  <div class="v2-page">
    <aside class="v2-sidebar">
      <div class="v2-card">
        <div class="v2-card-header">
          <div>
            <h1>Terminal V2</h1>
            <p>Oracle-backed screen recovery</p>
          </div>
          <RouterLink class="v2-link" to="/">旧版</RouterLink>
        </div>
        <div class="v2-actions">
          <button type="button" data-testid="refresh-v2" @click="refresh">Refresh</button>
          <button type="button" data-testid="create-v2" @click="createTerminal">Create</button>
          <button type="button" @click="terminalStore.resync" :disabled="!terminalStore.selectedInstanceId">Resync</button>
        </div>
        <div class="v2-status" data-testid="status-v2">{{ terminalStore.status }}</div>
      </div>

      <div class="v2-card">
        <div class="v2-card-title">Instances</div>
        <div class="v2-instance-list">
          <button
            v-for="item in terminalStore.instances"
            :key="item.id"
            type="button"
            class="v2-instance"
            :class="{ active: item.id === terminalStore.selectedInstanceId }"
            @click="connect(item.id)"
          >
            <span>{{ item.id }}</span>
            <small>{{ item.cwd || '~' }}</small>
          </button>
        </div>
        <button type="button" class="v2-danger" @click="terminateSelected" :disabled="!terminalStore.selectedInstanceId">
          Terminate
        </button>
      </div>
    </aside>

    <main class="v2-main">
      <section class="v2-toolbar">
        <span>{{ terminalStore.selectedInstance?.command || 'No instance selected' }}</span>
        <span>{{ terminalStore.selectedInstance?.cwd || '' }}</span>
      </section>
      <section class="v2-terminal-shell">
        <div ref="terminalRef" class="v2-terminal" />
      </section>
    </main>
  </div>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import 'xterm/css/xterm.css';
import { useWebCliTerminalStoreV2 } from '../stores/webcli-terminal-v2.js';
import { createTerminalProtocolRendererV2 } from '../composables/useTerminalProtocolV2.js';
import { isTerminalViewportRenderable } from './desktop-terminal-resize.js';

const terminalStore = useWebCliTerminalStoreV2();
const terminalRef = ref(null);

let term = null;
let fitAddon = null;
let unsubscribe = null;
let renderer = null;

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function focusTerminal() {
  term?.focus();
}

async function fitTerminal() {
  await nextTick();
  if (!isTerminalViewportRenderable('terminal', terminalRef.value)) {
    return;
  }
  fitAddon?.fit();
}

function handleResize({ cols, rows }) {
  terminalStore.sendResize(cols, rows);
}

async function refresh() {
  await Promise.all([
    terminalStore.fetchInstances(),
    terminalStore.fetchNodes()
  ]);
  if (!terminalStore.selectedInstanceId && terminalStore.instances.length > 0) {
    await connect(terminalStore.instances[0].id);
  }
}

async function connect(id) {
  await terminalStore.connect(id);
  await fitTerminal();
  focusTerminal();
}

async function createTerminal() {
  const nodeId = terminalStore.getDefaultNodeId();
  const created = await terminalStore.createInstance({
    command: 'bash',
    cwd: '/home/yueyuan',
    cols: Math.max(80, Number(term?.cols || 80)),
    rows: Math.max(24, Number(term?.rows || 24))
  }, nodeId);
  await connect(String(created.instance_id || ''));
}

async function terminateSelected() {
  await terminalStore.terminateSelected();
  if (terminalStore.selectedInstanceId) {
    await connect(terminalStore.selectedInstanceId);
  } else {
    term?.reset();
  }
}

function onVisibilityChange() {
  fitTerminal();
  if (typeof document !== 'undefined' && document.hidden === false) {
    terminalStore.resync().catch(() => {});
  }
}

onMounted(async () => {
  term = new Terminal({
    convertEol: true,
    cursorBlink: true,
    fontFamily: 'JetBrains Mono, monospace',
    fontSize: 14,
    scrollback: 5000,
    theme: {
      background: '#0b1020',
      foreground: '#d9e1ff'
    }
  });
  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalRef.value);
  renderer = createTerminalProtocolRendererV2(term);
  unsubscribe = terminalStore.subscribe((message) => {
    renderer.onMessage(message);
  });
  term.onData((data) => terminalStore.sendInput(data));
  term.onResize(handleResize);

  window.addEventListener('resize', fitTerminal);
  document.addEventListener('visibilitychange', onVisibilityChange);

  await fitTerminal();
  await refresh();
  await wait(30);
  focusTerminal();
});

onBeforeUnmount(() => {
  unsubscribe?.();
  terminalStore.disconnect();
  window.removeEventListener('resize', fitTerminal);
  document.removeEventListener('visibilitychange', onVisibilityChange);
  term?.dispose();
});
</script>

<style scoped>
.v2-page {
  min-height: 100vh;
  min-height: 100dvh;
  display: grid;
  grid-template-columns: 320px minmax(0, 1fr);
  background: radial-gradient(circle at top left, #1d2a52, #07101d 58%);
  color: #e7ecff;
}

.v2-sidebar {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  border-right: 1px solid rgba(171, 190, 255, 0.16);
  background: rgba(7, 16, 29, 0.72);
  backdrop-filter: blur(12px);
}

.v2-card {
  border: 1px solid rgba(171, 190, 255, 0.16);
  border-radius: 16px;
  padding: 16px;
  background: rgba(14, 24, 44, 0.84);
}

.v2-card-header,
.v2-actions,
.v2-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.v2-card-header h1,
.v2-card-title {
  font-size: 18px;
  margin: 0;
}

.v2-card-header p {
  margin: 4px 0 0;
  color: #93a3d8;
  font-size: 13px;
}

.v2-link,
.v2-instance,
button {
  appearance: none;
  border: 1px solid rgba(171, 190, 255, 0.18);
  background: rgba(20, 33, 61, 0.92);
  color: inherit;
  border-radius: 12px;
  padding: 10px 12px;
  text-decoration: none;
}

.v2-actions {
  margin: 16px 0 12px;
}

.v2-instance-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin: 12px 0;
}

.v2-instance {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  text-align: left;
}

.v2-instance.active {
  border-color: #7cb4ff;
  background: rgba(35, 58, 107, 0.98);
}

.v2-instance small,
.v2-status {
  color: #9eb0e8;
}

.v2-danger {
  width: 100%;
  background: rgba(92, 28, 35, 0.95);
}

.v2-main {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  min-width: 0;
}

.v2-toolbar {
  padding: 18px 22px;
  border-bottom: 1px solid rgba(171, 190, 255, 0.12);
  background: rgba(6, 13, 25, 0.42);
}

.v2-terminal-shell {
  padding: 18px;
  min-height: 0;
}

.v2-terminal {
  width: 100%;
  height: calc(100vh - 94px);
  height: calc(100dvh - 94px);
  border-radius: 18px;
  overflow: hidden;
  border: 1px solid rgba(171, 190, 255, 0.16);
  background: #08101f;
}

.v2-terminal :deep(.xterm),
.v2-terminal :deep(.xterm-screen),
.v2-terminal :deep(.xterm-viewport) {
  height: 100%;
}

@media (max-width: 960px) {
  .v2-page {
    grid-template-columns: 1fr;
  }

  .v2-terminal {
    height: 60vh;
    height: 60dvh;
  }
}
</style>
