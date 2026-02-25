<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { WebglAddon } from '@xterm/addon-webgl';
import { useTerminalStore } from '../stores/terminal.js';
import 'xterm/css/xterm.css';

const props = defineProps({
  sessionId: { type: String, required: true },
  displaySettings: { type: Object, required: true },
  quickCommands: { type: Array, default: () => [] }
});

const emit = defineEmits(['open-quick-command-editor']);

const store = useTerminalStore();
const mountRef = ref(null);
const resizeOverlayRef = ref(null);
const pasteBridgeRef = ref(null);
const pasteHint = ref('');
const session = computed(() => store.sessions.find((x) => x.sessionId === props.sessionId) || null);
const normalizedQuickCommands = computed(() => normalizeQuickCommands(props.quickCommands || []));
const outputTruncatedHint = computed(() => {
  if (session.value?.outputTruncated !== true) {
    return '';
  }
  const max = Number(session.value?.maxOutputBufferBytes || 0);
  if (!Number.isFinite(max) || max <= 0) {
    return '历史输出已截断，仅保留最近一段内容';
  }
  const mb = (max / (1024 * 1024)).toFixed(max % (1024 * 1024) === 0 ? 0 : 1);
  return `历史输出已截断，仅保留最近 ${mb} MB`;
});
const historyLoadingHint = computed(() => {
  return store.isSessionHistoryLoading(props.sessionId) ? '正在加载更早历史...' : '';
});

let term;
let fitAddon;
let rendererAddon;
let rendererLossDisposable = null;
let unsub;
let overlayTimer = null;
let pasteHintTimer = null;
let lastTerminalInteractionAt = 0;
let altVoiceCaptureArmed = false;
let replayRendered = false;
let containerResizeObserver = null;
let resizeRaf = null;
let scrollLoadCooldownTimer = null;
let scrollLoadInFlight = false;

onMounted(() => {
  const settings = normalizeDisplaySettings(props.displaySettings);
  term = new Terminal(buildTerminalOptions(settings));
  installAltScreenGuard(settings.keepScrollbackOnAltScreen);

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.loadAddon(new WebLinksAddon());

  term.open(mountRef.value);
  syncReadonlyInputState();
  installRenderer(settings.rendererType);

  term.attachCustomKeyEventHandler((event) => {
    const key = String(event.key || '').toLowerCase();
    const isPasteHotkey = (event.ctrlKey || event.metaKey) && key === 'v';
    const isShiftInsert = event.shiftKey && key === 'insert';
    if (event.type === 'keydown' && isPasteHotkey) {
      return true;
    }
    if (event.type === 'keydown' && isShiftInsert) {
      event.preventDefault();
      pasteFromClipboard();
      return false;
    }
    return true;
  });

  fitAddon.fit();
  term.focus();
  markTerminalInteraction();

  mountRef.value?.addEventListener('click', focusTerminal);
  mountRef.value?.addEventListener('keydown', markTerminalInteraction, true);
  mountRef.value?.addEventListener('mousedown', markTerminalInteraction, true);
  mountRef.value?.addEventListener('paste', onPaste);
  mountRef.value?.addEventListener('contextmenu', onContextMenu);
  window.addEventListener('keydown', onWindowKeyDown, true);
  window.addEventListener('keyup', onWindowKeyUp, true);
  window.addEventListener('paste', onWindowPaste, true);

  unsub = store.subscribe(props.sessionId, (msg) => {
    if (msg.type === 'ready') {
      replayRendered = false;
    }
    if (msg.type === 'output') {
      if (msg.replay === true && replayRendered !== true) {
        term.reset();
        replayRendered = true;
      }
      term.write(msg.data);
    }
    if (msg.type === 'exit') {
      term.writeln(`\r\n[exit] code=${msg.exitCode}`);
    }
    if (msg.type === 'error') {
      if (msg.code === 'READ_ONLY') {
        store.updateSessionLocal(props.sessionId, { writable: false, lastError: msg.message || 'read-only' });
      }
      term.writeln(`\r\n[error] ${msg.message}`);
    }
  });

  const cachedOutput = store.getSessionOutputBuffer(props.sessionId);
  if (cachedOutput) {
    term.write(cachedOutput);
  }

  store.ensureConnection(props.sessionId);

  term.onData((data) => {
    markTerminalInteraction();
    if (session.value?.writable === false) {
      return;
    }
    if (data === '\u0016') {
      pasteFromClipboard();
      return;
    }
    store.sendInput(props.sessionId, data);
  });

  term.onScroll(async () => {
    if (scrollLoadInFlight) {
      return;
    }
    const viewportY = Number(term?.buffer?.active?.viewportY || 0);
    if (viewportY > 0) {
      return;
    }
    scrollLoadInFlight = true;
    try {
      await store.loadOlderHistory(props.sessionId);
    } catch {
      // keep terminal responsive even if history loading fails
    } finally {
      if (scrollLoadCooldownTimer) {
        clearTimeout(scrollLoadCooldownTimer);
      }
      scrollLoadCooldownTimer = setTimeout(() => {
        scrollLoadInFlight = false;
      }, 300);
    }
  });

  if (settings.copyOnSelect) {
    term.onSelectionChange(() => {
      copySelection();
    });
  }

  window.addEventListener('resize', scheduleResize);
  if (typeof window !== 'undefined' && typeof window.ResizeObserver === 'function') {
    const resizeTarget = mountRef.value?.parentElement || mountRef.value;
    if (resizeTarget) {
      containerResizeObserver = new window.ResizeObserver(() => {
        scheduleResize();
      });
      containerResizeObserver.observe(resizeTarget);
    }
  }
  scheduleResize();
});

watch(
  () => session.value?.writable,
  () => {
    syncReadonlyInputState();
  }
);

onBeforeUnmount(() => {
  window.removeEventListener('resize', scheduleResize);
  window.removeEventListener('paste', onWindowPaste, true);
  mountRef.value?.removeEventListener('click', focusTerminal);
  mountRef.value?.removeEventListener('keydown', markTerminalInteraction, true);
  mountRef.value?.removeEventListener('mousedown', markTerminalInteraction, true);
  mountRef.value?.removeEventListener('paste', onPaste);
  mountRef.value?.removeEventListener('contextmenu', onContextMenu);
  window.removeEventListener('keydown', onWindowKeyDown, true);
  window.removeEventListener('keyup', onWindowKeyUp, true);
  if (overlayTimer) {
    clearTimeout(overlayTimer);
  }
  if (pasteHintTimer) {
    clearTimeout(pasteHintTimer);
  }
  if (scrollLoadCooldownTimer) {
    clearTimeout(scrollLoadCooldownTimer);
    scrollLoadCooldownTimer = null;
  }
  if (unsub) {
    unsub();
  }
  if (containerResizeObserver) {
    containerResizeObserver.disconnect();
    containerResizeObserver = null;
  }
  if (resizeRaf) {
    cancelAnimationFrame(resizeRaf);
    resizeRaf = null;
  }
  rendererLossDisposable?.dispose?.();
  rendererLossDisposable = null;
  rendererAddon?.dispose?.();
  term?.dispose();
});

function buildTerminalOptions(settings) {
  const theme = resolveTheme(settings.themeId);
  return {
    allowProposedApi: true,
    convertEol: true,
    scrollback: settings.scrollback,
    cursorBlink: settings.cursorBlink,
    cursorStyle: settings.cursorStyle,
    fontSize: settings.fontSize,
    fontFamily: settings.fontFamily,
    lineHeight: settings.lineHeight,
    letterSpacing: settings.letterSpacing,
    theme
  };
}

function syncReadonlyInputState() {
  if (!term) {
    return;
  }
  const readonly = session.value?.writable === false;
  term.options.disableStdin = readonly;
  if (readonly) {
    clearPasteHint();
  }
}

function installAltScreenGuard(enabled) {
  if (!enabled || !term?.parser?.registerCsiHandler) {
    return;
  }

  const isAltScreenParam = (value) => value === 47 || value === 1047 || value === 1048 || value === 1049;
  const shouldBlock = (params = []) => Array.isArray(params) && params.some((value) => isAltScreenParam(Number(value)));

  // Ignore ANSI alternate-screen mode switches so scrollback history is preserved.
  term.parser.registerCsiHandler({ prefix: '?', final: 'h' }, (params) => shouldBlock(params));
  term.parser.registerCsiHandler({ prefix: '?', final: 'l' }, (params) => shouldBlock(params));
}

function installRenderer(rendererType) {
  rendererLossDisposable?.dispose?.();
  rendererLossDisposable = null;
  rendererAddon?.dispose?.();
  rendererAddon = null;

  const order = rendererType === 'webgl'
    ? ['webgl']
    : rendererType === 'canvas'
      ? ['webgl']
      : rendererType === 'dom'
        ? []
        : ['webgl'];

  for (const type of order) {
    try {
      if (type === 'webgl') {
        rendererAddon = new WebglAddon();
      }
      if (rendererAddon) {
        term.loadAddon(rendererAddon);
        if (typeof rendererAddon.onContextLoss === 'function') {
          rendererLossDisposable = rendererAddon.onContextLoss(() => {
            setPasteHint('WebGL 渲染异常，已切换到 DOM 渲染');
            installRenderer('dom');
            scheduleResize();
          });
        }
      }
      return;
    } catch {
      rendererAddon = null;
    }
  }
}

function scheduleResize() {
  if (resizeRaf) {
    return;
  }
  resizeRaf = requestAnimationFrame(() => {
    resizeRaf = null;
    onResize();
  });
}

function onResize() {
  fitAddon?.fit();
  if (term) {
    store.sendResize(props.sessionId, term.cols, term.rows);
    const settings = normalizeDisplaySettings(props.displaySettings);
    if (settings.showResizeOverlay) {
      showResizeOverlay(`${term.cols}x${term.rows}`);
    }
  }
}

function showResizeOverlay(text) {
  if (!resizeOverlayRef.value) {
    return;
  }
  resizeOverlayRef.value.textContent = text;
  resizeOverlayRef.value.classList.add('visible');
  if (overlayTimer) {
    clearTimeout(overlayTimer);
  }
  overlayTimer = setTimeout(() => {
    resizeOverlayRef.value?.classList.remove('visible');
  }, 280);
}

function focusTerminal() {
  markTerminalInteraction();
  term?.focus();
}

function markTerminalInteraction() {
  lastTerminalInteractionAt = Date.now();
}

function onWindowKeyDown(event) {
  if (!isPlainAlt(event)) {
    return;
  }
  if (!shouldGuardAltFocus()) {
    altVoiceCaptureArmed = false;
    return;
  }
  altVoiceCaptureArmed = true;
  markTerminalInteraction();
  event.preventDefault();
}

function onWindowKeyUp(event) {
  if (!isPlainAlt(event)) {
    return;
  }
  if (!altVoiceCaptureArmed) {
    return;
  }
  altVoiceCaptureArmed = false;
  markTerminalInteraction();
  event.preventDefault();
  setTimeout(() => {
    if (!session.value?.sessionId) {
      return;
    }
    const active = typeof document !== 'undefined' ? document.activeElement : null;
    if (active && isEditableElement(active) && !(mountRef.value && mountRef.value.contains(active))) {
      return;
    }
    focusTerminal();
  }, 0);
}

function shouldGuardAltFocus() {
  if (isTerminalFocused()) {
    return true;
  }
  return shouldHandleRecentTerminalPaste();
}

function isPlainAlt(event) {
  if (!event || event.key !== 'Alt') {
    return false;
  }
  return !event.ctrlKey && !event.metaKey && !event.shiftKey;
}

function onPaste(event) {
  if (!shouldHandlePaste(event)) {
    return;
  }
  const text = event?.clipboardData?.getData('text') || '';
  if (!text) {
    return;
  }
  event.preventDefault();
  sendPastedText(text);
  term?.focus();
}

function onContextMenu(event) {
  const settings = normalizeDisplaySettings(props.displaySettings);
  if (settings.rightClickPaste !== true) {
    return;
  }
  event.preventDefault();
  markTerminalInteraction();
  pasteFromClipboard();
}

function onWindowPaste(event) {
  if (!shouldHandlePaste(event)) {
    return;
  }
  onPaste(event);
}

async function pasteFromClipboard() {
  if (session.value?.writable === false) {
    term?.focus();
    return;
  }

  if (!canReadClipboard()) {
    requestPasteViaBridge();
    return;
  }

  try {
    const text = await navigator.clipboard.readText();
    if (!sendPastedText(text)) {
      requestPasteViaBridge();
    }
  } catch {
    requestPasteViaBridge();
  } finally {
    term?.focus();
  }
}

function onPasteBridge(event) {
  const text = event?.clipboardData?.getData('text') || '';
  if (!text) {
    return;
  }
  event.preventDefault();
  sendPastedText(text);
  clearPasteHint();
  if (pasteBridgeRef.value) {
    pasteBridgeRef.value.value = '';
    pasteBridgeRef.value.blur();
  }
  term?.focus();
}

function sendPastedText(text) {
  if (session.value?.writable === false) {
    return false;
  }
  const normalized = String(text || '');
  if (!normalized) {
    return false;
  }
  store.sendInput(props.sessionId, normalized);
  return true;
}

function requestPasteViaBridge() {
  setPasteHint('剪贴板读取受限，请按 Ctrl/Cmd+V 完成粘贴');
  if (!pasteBridgeRef.value) {
    return;
  }
  pasteBridgeRef.value.focus();
  pasteBridgeRef.value.select();
}

function setPasteHint(message) {
  pasteHint.value = String(message || '');
  if (pasteHintTimer) {
    clearTimeout(pasteHintTimer);
  }
  pasteHintTimer = setTimeout(() => {
    pasteHint.value = '';
    pasteHintTimer = null;
  }, 3200);
}

function clearPasteHint() {
  pasteHint.value = '';
  if (pasteHintTimer) {
    clearTimeout(pasteHintTimer);
    pasteHintTimer = null;
  }
}

function canReadClipboard() {
  return Boolean(typeof navigator !== 'undefined' && navigator.clipboard?.readText);
}

function shouldHandlePaste(event = null) {
  const target = event?.target;
  if (target && isEditableElement(target) && !(mountRef.value && mountRef.value.contains(target))) {
    return false;
  }
  if (target && mountRef.value && mountRef.value.contains(target)) {
    return true;
  }
  if (isTerminalFocused(event)) {
    return true;
  }
  return shouldHandleRecentTerminalPaste();
}

function isTerminalFocused(event = null) {
  const target = event?.target;
  if (target && isEditableElement(target) && !(mountRef.value && mountRef.value.contains(target))) {
    return false;
  }

  const active = typeof document !== 'undefined' ? document.activeElement : null;
  return Boolean(active && mountRef.value && mountRef.value.contains(active));
}

function shouldHandleRecentTerminalPaste() {
  if (!session.value?.sessionId) {
    return false;
  }
  if (Date.now() - lastTerminalInteractionAt > resolveSpeechPasteGraceMs()) {
    return false;
  }
  const active = typeof document !== 'undefined' ? document.activeElement : null;
  if (active && isEditableElement(active) && !(mountRef.value && mountRef.value.contains(active))) {
    return false;
  }
  return true;
}

function resolveSpeechPasteGraceMs() {
  const settings = normalizeDisplaySettings(props.displaySettings);
  const seconds = Number(settings.speechPasteGraceSeconds);
  const clampedSeconds = Number.isFinite(seconds) ? Math.max(0, Math.min(120, seconds)) : 15;
  return clampedSeconds * 1000;
}

function runQuickCommand(item) {
  if (!props.sessionId || session.value?.writable === false) {
    return;
  }
  const content = String(item?.content || '').trim();
  if (!content) {
    return;
  }

  let payload = content;
  const sendMode = String(item?.sendMode || 'auto');
  if (sendMode === 'enter') {
    payload = `${content}\r`;
  } else if (sendMode === 'auto' && !/\s/.test(content)) {
    payload = `${content}\r`;
  }

  markTerminalInteraction();
  store.sendInput(props.sessionId, payload);
  focusTerminal();
}

async function copySelection() {
  const text = term?.getSelection() || '';
  if (!text || typeof navigator === 'undefined' || !navigator.clipboard) {
    return;
  }
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // ignore clipboard errors
  }
}

function isEditableElement(node) {
  if (!node || typeof node !== 'object') {
    return false;
  }
  const tag = String(node.tagName || '').toLowerCase();
  if (tag === 'input' || tag === 'textarea' || tag === 'select') {
    return true;
  }
  return node.isContentEditable === true;
}

function normalizeQuickCommands(input = []) {
  if (!Array.isArray(input)) {
    return [];
  }
  const items = [];
  for (const raw of input) {
    if (typeof raw === 'string') {
      const content = raw.trim();
      if (!content) {
        continue;
      }
      items.push({ id: content, label: content, content, sendMode: 'auto', enabled: true, order: items.length });
      continue;
    }

    if (!raw || typeof raw !== 'object') {
      continue;
    }
    const content = String(raw.content || '').trim();
    if (!content) {
      continue;
    }
    const sendMode = ['auto', 'enter', 'raw'].includes(String(raw.sendMode)) ? String(raw.sendMode) : 'auto';
    items.push({
      id: String(raw.id || `${content}-${items.length}`),
      label: String(raw.label || content),
      content,
      sendMode,
      enabled: raw.enabled !== false,
      order: Number.isFinite(Number(raw.order)) ? Number(raw.order) : items.length
    });
  }
  return items
    .filter((x) => x.enabled !== false)
    .sort((a, b) => a.order - b.order);
}

function normalizeDisplaySettings(input = {}) {
  const renderer = String(input.rendererType || 'auto');
  return {
    fontFamily: String(input.fontFamily || 'JetBrains Mono, monospace'),
    fontSize: Number(input.fontSize || 14),
    lineHeight: Number(input.lineHeight || 1.2),
    letterSpacing: Number(input.letterSpacing || 0),
    cursorBlink: input.cursorBlink !== false,
    cursorStyle: String(input.cursorStyle || 'block'),
    scrollback: Number(input.scrollback || 50000),
    rendererType: renderer === 'canvas' ? 'auto' : renderer,
    rightClickPaste: input.rightClickPaste !== false,
    copyOnSelect: input.copyOnSelect === true,
    keepScrollbackOnAltScreen: input.keepScrollbackOnAltScreen === true,
    showResizeOverlay: input.showResizeOverlay !== false,
    speechPasteGraceSeconds: Number.isFinite(Number(input.speechPasteGraceSeconds))
      ? Number(input.speechPasteGraceSeconds)
      : 15,
    themeId: String(input.themeId || 'dark-classic')
  };
}

function resolveTheme(themeId) {
  if (themeId === 'light') {
    return {
      background: '#ffffff',
      foreground: '#1f2328',
      cursor: '#0969da',
      cursorAccent: '#ffffff',
      selectionBackground: '#b6d5ff'
    };
  }

  if (themeId === 'dark-modern') {
    return {
      background: '#0f1722',
      foreground: '#d6deeb',
      cursor: '#53b2ff',
      cursorAccent: '#0f1722',
      selectionBackground: '#294a74'
    };
  }

  return {
    background: '#091019',
    foreground: '#dbe6f2',
    cursor: '#3fd0b5',
    cursorAccent: '#091019',
    selectionBackground: '#2f4d64'
  };
}
</script>

<template>
  <div class="terminal-tab-shell">
    <small v-if="pasteHint" class="mono warn-text terminal-paste-hint">{{ pasteHint }}</small>
    <small v-if="outputTruncatedHint" class="mono warn-text terminal-paste-hint">{{ outputTruncatedHint }}</small>
    <small v-if="historyLoadingHint" class="mono terminal-paste-hint">{{ historyLoadingHint }}</small>
    <div class="terminal-surface">
      <div ref="resizeOverlayRef" class="terminal-resize-overlay" />
      <div ref="mountRef" class="terminal-wrap" />
      <textarea
        ref="pasteBridgeRef"
        class="terminal-paste-bridge"
        rows="1"
        aria-label="终端粘贴中转输入"
        @paste="onPasteBridge"
      />
    </div>
    <section class="terminal-quickbar">
      <div class="terminal-quickbar-head">
        <strong>快捷指令</strong>
        <button class="terminal-quickbar-edit-link" @click="emit('open-quick-command-editor')">编辑快捷指令</button>
      </div>
      <div class="terminal-quickbar-list">
        <a
          v-for="item in normalizedQuickCommands"
          :key="item.id"
          class="terminal-quickbar-run"
          :class="{ disabled: session?.writable === false }"
          href="#"
          @click.prevent="runQuickCommand(item)"
        >
          {{ item.label }}
        </a>
        <div v-if="normalizedQuickCommands.length === 0" class="terminal-empty-list">暂无快捷指令</div>
      </div>
    </section>
  </div>
</template>
