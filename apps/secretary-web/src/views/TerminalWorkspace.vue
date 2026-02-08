<script setup>
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import TerminalTab from '../components/TerminalTab.vue';
import { useTerminalStore } from '../stores/terminal.js';
import { useTerminalProfileStore } from '../stores/terminal-profiles.js';

const DISPLAY_SETTINGS_V3_KEY = 'terminal.displaySettings.v3';
const DISPLAY_SETTINGS_V2_KEY = 'terminal.displaySettings.v2';
const SELECTED_PROJECT_PATH_KEY = 'terminal.selectedProjectPath.v1';
const MANUAL_PROJECT_PATH_KEY = 'terminal.manualProjectPath.v1';
const TERMINAL_API_BASE = import.meta.env.VITE_TERMINAL_API_BASE || '/terminal-api';

const route = useRoute();
const router = useRouter();
const terminalStore = useTerminalStore();
const profileStore = useTerminalProfileStore();

const loading = ref(false);
const createBusy = ref(false);
const terminateBusy = ref(false);
const error = ref('');

const showSettings = ref(false);
const settingsSection = ref('general-appearance');
const displaySettings = ref(readDisplaySettings());
const selectedSessionId = ref('');
const projectOptions = ref([]);
const selectedProjectPath = ref(readSelectedProjectPath());
const manualProjectPath = ref(readManualProjectPath());
const showDirPicker = ref(false);
const dirPickerPath = ref('/');
const dirPickerItems = ref([]);
const dirPickerBusy = ref(false);
const dirPickerError = ref('');
const fsAllowedRoots = ref([]);
const fsAllowedRootsText = ref('');
const fsRootsBusy = ref(false);
const cliDraft = ref(null);
const activeCliKey = computed(() => cliKeyFromSection(settingsSection.value));
const cliConfigEntries = computed(() => {
  const cliDefaults = displaySettings.value?.cliDefaults && typeof displaySettings.value.cliDefaults === 'object'
    ? displaySettings.value.cliDefaults
    : {};
  return Object.entries(cliDefaults).map(([key, value]) => ({
    key,
    name: cliDisplayName(key, value)
  }));
});
const activeCliConfig = computed(() => {
  const key = activeCliKey.value;
  if (cliDraft.value && cliDraft.value.key === key) {
    return cliDraft.value;
  }
  const current = displaySettings.value?.cliDefaults?.[key];
  if (current?.defaultProfile) {
    return current;
  }
  return {
    defaultProfile: normalizeDefaultProfile({}, defaultCommandLineForCliKey(key))
  };
});
const CLI_ICON_OPTIONS = ['terminal', 'bot', 'code', 'server', 'tool', 'book', 'spark', 'rocket'];

const sessions = computed(() => {
  const list = terminalStore.sessions
    .filter((x) => !isExitedSession(x))
    .slice();
  return list;
});

const activeSession = computed(() => terminalStore.activeSession);
const activeProfile = computed(() => {
  const profileId = activeSession.value?.profileId || '';
  if (!profileId) {
    return null;
  }
  return profileStore.profiles.find((x) => x.profileId === profileId) || null;
});

const mergedQuickCommands = computed(() => {
  const globalList = normalizeQuickCommands(displaySettings.value.general?.quickCommands || []);
  const profileList = normalizeQuickCommands(activeProfile.value?.quickCommands || []);
  const map = new Map();
  for (const item of globalList) {
    map.set(item.id || item.content, item);
  }
  for (const item of profileList) {
    map.set(item.id || item.content, item);
  }
  return Array.from(map.values()).sort((a, b) => a.order - b.order);
});

watch(
  () => route.query.sessionId,
  (sessionId) => {
    if (!sessionId || typeof sessionId !== 'string') {
      return;
    }
    selectedSessionId.value = sessionId;
    terminalStore.openSession(sessionId, String(sessionId).slice(0, 8));
  },
  { immediate: true }
);

watch(
  () => terminalStore.activeSessionId,
  (sessionId) => {
    if (!sessionId) {
      return;
    }
    selectedSessionId.value = sessionId;
    syncRouteSession(sessionId);
  }
);

watch(selectedSessionId, (sessionId) => {
  if (!sessionId) {
    return;
  }
  terminalStore.openSession(sessionId, String(sessionId).slice(0, 8));
  syncRouteSession(sessionId);
});

onMounted(async () => {
  loading.value = true;
  error.value = '';
  try {
    await loadProjectOptions();
    await Promise.all([profileStore.loadProfiles(), terminalStore.loadSessions({ includeExited: false })]);
    await loadGlobalQuickCommandsFromBackend();
    await loadFsAllowedRootsFromBackend();
    const settings = normalizeDisplaySettings(displaySettings.value);
    const querySessionId = typeof route.query.sessionId === 'string' ? route.query.sessionId : '';
    let targetSessionId = '';

    if (querySessionId) {
      targetSessionId = querySessionId;
    } else if (sessions.value.length > 0) {
      targetSessionId = sessions.value[0].sessionId;
    } else if (settings.autoCreateOnEmpty) {
      const created = await createSessionInternal();
      targetSessionId = created?.sessionId || '';
    }

    if (targetSessionId) {
      selectedSessionId.value = targetSessionId;
      terminalStore.openSession(targetSessionId, String(targetSessionId).slice(0, 8));
      if (settings.replayOnEntry) {
        terminalStore.reconnectNow(targetSessionId, { replay: true });
      }
    }
  } catch (err) {
    error.value = String(err?.message || err);
  } finally {
    loading.value = false;
  }
});

function syncRouteSession(sessionId) {
  if (route.query.sessionId === sessionId) {
    return;
  }
  router.replace({ path: '/terminal', query: { ...route.query, sessionId } });
}

function openSettings(section = 'general-appearance') {
  settingsSection.value = section;
  showSettings.value = true;
}

function closeSettings() {
  showSettings.value = false;
}

async function saveGeneralSettings() {
  error.value = '';
  try {
    displaySettings.value = normalizeDisplaySettings(displaySettings.value);
    await persistGlobalQuickCommandsToBackend(displaySettings.value.general?.quickCommands || []);
    persistDisplaySettings(displaySettings.value);
  } catch (err) {
    error.value = String(err?.message || err);
  }
}

async function loadGlobalQuickCommandsFromBackend() {
  const localCommands = normalizeQuickCommands(displaySettings.value.general?.quickCommands || []);
  try {
    const res = await fetch(`${TERMINAL_API_BASE}/settings/global-quick-commands`);
    if (!res.ok) {
      throw new Error(await readError(res, `load global quick commands failed: ${res.status}`));
    }
    const body = await res.json();
    const remoteCommands = normalizeQuickCommands(body?.quickCommands || []);

    if (remoteCommands.length > 0) {
      displaySettings.value.general.quickCommands = remoteCommands;
      persistDisplaySettings(displaySettings.value);
      return;
    }

    if (localCommands.length > 0) {
      await persistGlobalQuickCommandsToBackend(localCommands);
    }
  } catch {
    // Keep local quick commands if backend unavailable.
  }
}

async function persistGlobalQuickCommandsToBackend(quickCommands) {
  const normalized = normalizeQuickCommands(quickCommands || []);
  const res = await fetch(`${TERMINAL_API_BASE}/settings/global-quick-commands`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ quickCommands: normalized })
  });
  if (!res.ok) {
    throw new Error(await readError(res, `save global quick commands failed: ${res.status}`));
  }
}

async function refreshSessions() {
  loading.value = true;
  error.value = '';
  try {
    await terminalStore.loadSessions({ includeExited: false });
  } catch (err) {
    error.value = String(err?.message || err);
  } finally {
    loading.value = false;
  }
}

async function createSessionInternal() {
  const settings = normalizeDisplaySettings(displaySettings.value);
  const launch = resolveSessionLaunchConfig();
  const projectPath = String(selectedProjectPath.value || '').trim();
  const sessionCwd = projectPath || launch.cwd || '';
  const now = new Date();
  const hh = String(now.getHours()).padStart(2, '0');
  const mm = String(now.getMinutes()).padStart(2, '0');
  const created = await terminalStore.createSession({
    cliType: launch.cliType,
    shell: launch.shell,
    cwd: sessionCwd || undefined,
    workspaceRoot: projectPath || undefined,
    args: launch.args,
    env: launch.env,
    title: `终端 ${hh}:${mm}`,
    cols: settings.defaultCols,
    rows: settings.defaultRows
  });
  await terminalStore.loadSessions({ includeExited: false });
  return created;
}

async function createSession() {
  createBusy.value = true;
  error.value = '';
  try {
    const created = await createSessionInternal();
    selectedSessionId.value = created.sessionId;
    terminalStore.openSession(created.sessionId, created.title || String(created.sessionId).slice(0, 8));
  } catch (err) {
    error.value = String(err?.message || err);
  } finally {
    createBusy.value = false;
  }
}

function reconnect() {
  if (!terminalStore.activeSessionId) {
    return;
  }
  terminalStore.reconnectNow(terminalStore.activeSessionId);
}

function disconnect() {
  if (!terminalStore.activeSessionId) {
    return;
  }
  terminalStore.disconnect(terminalStore.activeSessionId, true);
}

async function terminateSessionById(sessionId) {
  if (!sessionId || terminateBusy.value) {
    return;
  }

  terminateBusy.value = true;
  error.value = '';
  try {
    await terminalStore.terminateSession(sessionId);
    terminalStore.disconnect(sessionId, true);
    await terminalStore.loadSessions({ includeExited: false });

    if (terminalStore.activeSessionId === sessionId) {
      terminalStore.activeSessionId = '';
    }

    if (!terminalStore.activeSessionId && sessions.value.length > 0) {
      const first = sessions.value[0];
      selectedSessionId.value = first.sessionId;
      terminalStore.openSession(first.sessionId, first.title || String(first.sessionId).slice(0, 8));
    }
  } catch (err) {
    error.value = String(err?.message || err);
  } finally {
    terminateBusy.value = false;
  }
}

function isExitedSession(session) {
  return session?.status === 'exited' || session?.connectionStatus === 'exited';
}

function sessionCliName(session) {
  const shell = String(session?.shell || '').trim();
  if (shell) {
    const base = shell.split('/').filter(Boolean).pop() || shell;
    return base.toLowerCase();
  }
  const cliType = String(session?.cliType || '').trim().toLowerCase();
  if (cliType) {
    return cliType;
  }
  return 'cli';
}

function sessionWorkingDirectory(session) {
  return String(session?.cwd || session?.workspaceRoot || '').trim();
}

function formatSessionWorkingDirectory(session) {
  const cwd = sessionWorkingDirectory(session);
  if (!cwd) {
    return '未设置目录';
  }
  if (cwd.length <= 50) {
    return cwd;
  }
  return `...${cwd.slice(-50)}`;
}

function formatLastActive(timestamp) {
  const text = String(timestamp || '');
  if (!text) {
    return '--';
  }
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return '--';
  }
  return date.toLocaleTimeString('zh-CN', { hour12: false });
}

function sessionStatus(session) {
  return String(session?.connectionStatus || session?.status || 'idle');
}

function statusDotClass(session) {
  const status = sessionStatus(session);
  if (status === 'connected') return 'connected';
  if (status === 'reconnecting') return 'reconnecting';
  if (status === 'error') return 'error';
  if (status === 'exited') return 'exited';
  return 'idle';
}

function isReadonlySession(session) {
  return session?.writable === false;
}

function openSessionFromList(sessionId) {
  if (!sessionId) {
    return;
  }
  selectedSessionId.value = sessionId;
}

function setDefaultCliKey(cliKey) {
  const normalized = String(cliKey || '').trim();
  if (!normalized) {
    return;
  }
  const settings = normalizeDisplaySettings(displaySettings.value);
  if (!settings.cliDefaults?.[normalized]) {
    return;
  }
  displaySettings.value.defaultCliKey = normalized;
}

function sectionButtonClass(section) {
  return settingsSection.value === section ? 'active' : '';
}

function cliKeyFromSection(section) {
  const raw = String(section || '');
  if (raw.startsWith('cli:')) {
    const key = raw.slice(4).trim();
    if (key) {
      return key;
    }
  }
  if (raw.startsWith('cli-')) {
    const key = raw.slice(4).trim();
    if (key) {
      return key;
    }
  }

  const settings = normalizeDisplaySettings(displaySettings.value);
  if (settings.defaultCliKey && settings.cliDefaults?.[settings.defaultCliKey]) {
    return settings.defaultCliKey;
  }
  const first = Object.keys(settings.cliDefaults || {})[0];
  if (first) {
    return first;
  }
  return 'bash';
}

function cliDisplayName(cliKey, cliConfig) {
  const name = String(cliConfig?.defaultProfile?.name || '').trim();
  if (name) {
    return name;
  }
  if (cliKey === 'codex') return 'Codex CLI';
  if (cliKey === 'claude') return 'Claude Code CLI';
  if (cliKey === 'bash') return 'Bash';
  if (cliKey === 'custom-agent') return '自定义Agent';
  return `配置 ${cliKey}`;
}

function cliSectionKey(cliKey) {
  return `cli:${cliKey}`;
}

function addCliConfig() {
  const settings = normalizeDisplaySettings(displaySettings.value);
  const now = Date.now().toString(36);
  let cliKey = `custom-${now}`;
  let index = 1;
  while (settings.cliDefaults?.[cliKey]) {
    cliKey = `custom-${now}-${index}`;
    index += 1;
  }

  const currentCount = Object.keys(settings.cliDefaults || {}).length;
  cliDraft.value = {
    key: cliKey,
    defaultProfile: normalizeDefaultProfile(
      { name: `新配置 ${currentCount + 1}`, commandLine: '/bin/bash' },
      '/bin/bash'
    )
  };
  settingsSection.value = cliSectionKey(cliKey);
}

function cliTitleFromKey(cliKey) {
  if (cliDraft.value?.key === cliKey) {
    return cliDisplayName(cliKey, cliDraft.value);
  }
  const settings = normalizeDisplaySettings(displaySettings.value);
  return cliDisplayName(cliKey, settings.cliDefaults?.[cliKey]);
}

function isCliProfileSection(section) {
  const key = cliKeyFromSection(section);
  if (cliDraft.value?.key === key) {
    return true;
  }
  return Boolean(displaySettings.value?.cliDefaults?.[key]);
}

function defaultCommandLineForCliKey(cliKey) {
  if (cliKey === 'codex') return 'codex';
  if (cliKey === 'claude') return 'claude';
  return '/bin/bash';
}

function ensureCliConfigMap(merged, legacyCliDefaults) {
  const normalizedMap = {};
  const fixed = [
    { key: 'bash', legacyKey: 'custom' },
    { key: 'codex', legacyKey: 'codex' },
    { key: 'claude', legacyKey: 'claude' }
  ];

  for (const item of fixed) {
    const current = merged.cliDefaults[item.key] || {};
    const legacy = legacyCliDefaults[item.legacyKey] || {};
    const mergedDefaultProfile = {
      ...(current.defaultProfile || {}),
      startupArgsText: String(
        current.defaultProfile?.startupArgsText
          || current.startupArgsText
          || legacy.defaultArgsText
          || ''
      ),
      envText: String(
        current.defaultProfile?.envText
          || current.envText
          || legacy.envText
          || ''
      ),
      commandLine: String(
        current.defaultProfile?.commandLine
          || current.commandLine
          || defaultCommandLineForCliKey(item.key)
      )
    };

    if (item.key === 'codex' && mergedDefaultProfile.commandLine === '/bin/bash') {
      mergedDefaultProfile.commandLine = 'codex';
    }
    if (item.key === 'claude' && mergedDefaultProfile.commandLine === '/bin/bash') {
      mergedDefaultProfile.commandLine = 'claude';
    }

    normalizedMap[item.key] = {
      defaultProfile: normalizeDefaultProfile(mergedDefaultProfile, defaultCommandLineForCliKey(item.key))
    };
  }

  for (const [key, value] of Object.entries(merged.cliDefaults || {})) {
    if (normalizedMap[key]) {
      continue;
    }
    normalizedMap[key] = {
      defaultProfile: normalizeDefaultProfile(value?.defaultProfile || {}, '/bin/bash')
    };
  }

  merged.cliDefaults = normalizedMap;
  const defaultKey = String(merged.defaultCliKey || '').trim();
  if (!defaultKey || !merged.cliDefaults[defaultKey]) {
    merged.defaultCliKey = Object.keys(merged.cliDefaults)[0] || 'bash';
  }
}

async function saveCliConfig() {
  const key = activeCliKey.value;
  const draft = cliDraft.value;
  if (draft && draft.key === key) {
    if (!displaySettings.value.cliDefaults || typeof displaySettings.value.cliDefaults !== 'object') {
      displaySettings.value.cliDefaults = {};
    }
    displaySettings.value.cliDefaults[key] = {
      defaultProfile: normalizeDefaultProfile(draft.defaultProfile || {}, defaultCommandLineForCliKey(key))
    };
    if (!displaySettings.value.defaultCliKey) {
      displaySettings.value.defaultCliKey = key;
    }
    cliDraft.value = null;
  } else {
    if (!displaySettings.value.cliDefaults?.[key]) {
      return;
    }
    displaySettings.value.cliDefaults[key] = {
      defaultProfile: normalizeDefaultProfile(
        displaySettings.value.cliDefaults[key].defaultProfile || {},
        defaultCommandLineForCliKey(key)
      )
    };
  }

  await saveGeneralSettings();
}

async function deleteCliConfig() {
  const key = activeCliKey.value;
  if (!key) {
    return;
  }

  if (cliDraft.value?.key === key) {
    cliDraft.value = null;
    const first = Object.keys(displaySettings.value?.cliDefaults || {})[0];
    settingsSection.value = first ? cliSectionKey(first) : 'general-behavior';
    return;
  }

  const cliDefaults = displaySettings.value?.cliDefaults || {};
  const keys = Object.keys(cliDefaults);
  if (keys.length <= 1) {
    error.value = '至少保留一个指令配置';
    return;
  }

  delete cliDefaults[key];
  if (displaySettings.value.defaultCliKey === key) {
    displaySettings.value.defaultCliKey = Object.keys(cliDefaults)[0] || '';
  }

  const nextKey = Object.keys(cliDefaults)[0] || '';
  settingsSection.value = nextKey ? cliSectionKey(nextKey) : 'general-behavior';
  await saveGeneralSettings();
}

function normalizeDefaultProfile(input = {}, fallbackCommandLine = '/bin/bash') {
  const fallbackArgs = fallbackCommandLine === 'codex'
    ? '--dangerously-bypass-approvals-and-sandbox'
    : fallbackCommandLine === 'claude'
      ? '--dangerously-skip-permissions'
      : '';
  return {
    name: String(input.name || ''),
    commandLine: String(input.commandLine || fallbackCommandLine),
    cwd: String(input.cwd || ''),
    icon: String(input.icon || ''),
    startupArgsText: String(input.startupArgsText || fallbackArgs),
    envText: String(input.envText || '')
  };
}

function normalizeDisplaySettings(input = {}) {
  const inputObj = input && typeof input === 'object' ? input : {};
  const legacyCliDefaults = inputObj.cliDefaults && typeof inputObj.cliDefaults === 'object'
    ? inputObj.cliDefaults
    : {};
  const defaults = {
    fontFamily: 'JetBrains Mono, monospace',
    fontSize: 14,
    lineHeight: 1.2,
    letterSpacing: 0,
    cursorBlink: true,
    cursorStyle: 'block',
    scrollback: 50000,
    rendererType: 'auto',
    rightClickPaste: true,
    copyOnSelect: false,
    keepScrollbackOnAltScreen: false,
    showResizeOverlay: true,
    speechPasteGraceSeconds: 15,
    themeId: 'dark-classic',
    showRefreshAction: false,
    showReconnectAction: false,
    showDisconnectAction: false,
    defaultCols: 160,
    defaultRows: 40,
    autoCreateOnEmpty: true,
    replayOnEntry: true,
    defaultCliKey: 'codex',
    general: {
      defaultProfile: normalizeDefaultProfile({}),
      quickCommands: [],
      mcp: {
        enabled: false,
        serverListText: ''
      },
      skill: {
        enabled: false,
        skillListText: ''
      },
      nodejs: {
        nodePath: '',
        npmClient: 'npm',
        registry: ''
      },
      git: {
        userName: '',
        userEmail: '',
        defaultBranch: 'main'
      }
    },
    cliDefaults: {
      bash: {
        defaultProfile: normalizeDefaultProfile({}, defaultCommandLineForCliKey('bash'))
      },
      codex: {
        defaultProfile: normalizeDefaultProfile({}, defaultCommandLineForCliKey('codex'))
      },
      claude: {
        defaultProfile: normalizeDefaultProfile({}, defaultCommandLineForCliKey('claude'))
      }
    }
  };

  const merged = {
    ...defaults,
    ...inputObj,
    general: {
      ...defaults.general,
      ...(inputObj.general || {})
    },
    cliDefaults: {
      ...defaults.cliDefaults,
      ...(inputObj.cliDefaults || {})
    }
  };

  merged.fontSize = clampInt(merged.fontSize, 10, 40, 14);
  merged.lineHeight = clampNum(merged.lineHeight, 1, 2, 1.2);
  merged.letterSpacing = clampNum(merged.letterSpacing, -2, 8, 0);
  merged.scrollback = clampInt(merged.scrollback, 1000, 200000, 50000);
  merged.speechPasteGraceSeconds = clampInt(merged.speechPasteGraceSeconds, 0, 120, 15);
  merged.defaultCols = clampInt(merged.defaultCols, 40, 400, 160);
  merged.defaultRows = clampInt(merged.defaultRows, 10, 200, 40);
  merged.general.defaultProfile = normalizeDefaultProfile(merged.general.defaultProfile || {});
  merged.general.defaultProfile.commandLine = '/bin/bash';
  merged.general.defaultProfile.startupArgsText = '';
  merged.general.defaultProfile.name = '';
  merged.general.defaultProfile.icon = '';
  ensureCliConfigMap(merged, legacyCliDefaults);

  const quickCommandSource = Array.isArray(merged.general.quickCommands)
    ? merged.general.quickCommands
    : Array.isArray(inputObj.globalQuickCommands)
      ? inputObj.globalQuickCommands
      : [];
  merged.general.quickCommands = normalizeQuickCommands(quickCommandSource);

  merged.general.mcp = {
    enabled: Boolean(merged.general.mcp?.enabled),
    serverListText: String(merged.general.mcp?.serverListText || '')
  };
  merged.general.skill = {
    enabled: Boolean(merged.general.skill?.enabled),
    skillListText: String(merged.general.skill?.skillListText || '')
  };
  merged.general.nodejs = {
    nodePath: String(merged.general.nodejs?.nodePath || ''),
    npmClient: String(merged.general.nodejs?.npmClient || 'npm'),
    registry: String(merged.general.nodejs?.registry || '')
  };
  merged.general.git = {
    userName: String(merged.general.git?.userName || ''),
    userEmail: String(merged.general.git?.userEmail || ''),
    defaultBranch: String(merged.general.git?.defaultBranch || 'main')
  };

  // Keep legacy field mirrored for compatibility in dependent logic.
  merged.globalQuickCommands = [...merged.general.quickCommands];

  return merged;
}

function resolveSessionLaunchConfig() {
  const settings = normalizeDisplaySettings(displaySettings.value);
  const currentSessionCwd = String(activeSession.value?.cwd || '').trim();
  const cliKey = settings.cliDefaults?.[settings.defaultCliKey]
    ? settings.defaultCliKey
    : Object.keys(settings.cliDefaults || {})[0] || 'bash';
  const generalDefaults = normalizeDefaultProfile(settings.general?.defaultProfile || {}, '/bin/bash');
  const cliDefaults = normalizeDefaultProfile(
    settings.cliDefaults?.[cliKey]?.defaultProfile || {},
    defaultCommandLineForCliKey(cliKey)
  );

  const shell = String(cliDefaults.commandLine || defaultCommandLineForCliKey(cliKey)).trim()
    || defaultCommandLineForCliKey(cliKey);
  const cwd = String(cliDefaults.cwd || currentSessionCwd || generalDefaults.cwd || '').trim();
  const argsText = String(cliDefaults.startupArgsText || '');
  const envText = mergeEnvTexts(generalDefaults.envText, cliDefaults.envText);
  const cliType = cliKey === 'codex' || cliKey === 'claude' ? cliKey : 'custom';

  return {
    cliType,
    shell,
    cwd,
    args: parseArgLines(argsText),
    env: parseEnvPairs(envText)
  };
}

function readDisplaySettings() {
  const defaults = normalizeDisplaySettings({});
  if (typeof window === 'undefined') {
    return defaults;
  }

  try {
    const rawV3 = window.localStorage.getItem(DISPLAY_SETTINGS_V3_KEY);
    if (rawV3) {
      return normalizeDisplaySettings(JSON.parse(rawV3));
    }

    const rawV2 = window.localStorage.getItem(DISPLAY_SETTINGS_V2_KEY);
    if (!rawV2) {
      return defaults;
    }
    const migrated = normalizeDisplaySettings(JSON.parse(rawV2));
    window.localStorage.setItem(DISPLAY_SETTINGS_V3_KEY, JSON.stringify(migrated));
    return migrated;
  } catch {
    return defaults;
  }
}

function persistDisplaySettings(settings) {
  if (typeof window === 'undefined') {
    return;
  }
  window.localStorage.setItem(DISPLAY_SETTINGS_V3_KEY, JSON.stringify(settings));
}

function clampInt(value, min, max, fallback) {
  const num = Number(value);
  if (!Number.isFinite(num)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, Math.trunc(num)));
}

function clampNum(value, min, max, fallback) {
  const num = Number(value);
  if (!Number.isFinite(num)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, num));
}

function normalizeQuickCommands(list = []) {
  if (!Array.isArray(list)) {
    return [];
  }
  const out = [];
  for (const raw of list) {
    if (typeof raw === 'string') {
      const content = raw.trim();
      if (!content) {
        continue;
      }
      out.push({ id: `${content}-${out.length}`, label: content, content, sendMode: 'auto', enabled: true, order: out.length });
      continue;
    }
    if (!raw || typeof raw !== 'object') {
      continue;
    }
    const content = String(raw.content || '').trim();
    if (!content) {
      continue;
    }
    const sendMode = ['auto', 'enter', 'raw'].includes(String(raw.sendMode || 'auto')) ? String(raw.sendMode) : 'auto';
    out.push({
      id: String(raw.id || `${content}-${out.length}`),
      label: String(raw.label || content),
      content,
      sendMode,
      enabled: raw.enabled !== false,
      order: Number.isFinite(Number(raw.order)) ? Number(raw.order) : out.length
    });
  }
  return out.sort((a, b) => a.order - b.order).map((item, index) => ({ ...item, order: index }));
}

function parseArgLines(text) {
  return String(text || '')
    .split('\n')
    .map((x) => x.trim())
    .filter((x) => x.length > 0);
}

function parseEnvPairs(text) {
  const env = {};
  for (const line of parseArgLines(text)) {
    const idx = line.indexOf('=');
    if (idx <= 0) {
      continue;
    }
    const key = line.slice(0, idx).trim();
    const value = line.slice(idx + 1).trim();
    if (!key) {
      continue;
    }
    env[key] = value;
  }
  return env;
}

function mergeEnvTexts(baseText, overrideText) {
  const base = parseEnvPairs(baseText || '');
  const override = parseEnvPairs(overrideText || '');
  return Object.entries({ ...base, ...override })
    .map(([k, v]) => `${k}=${v}`)
    .join('\n');
}

function pathBaseName(path) {
  const normalized = String(path || '').trim().replace(/\/+$/, '');
  if (!normalized) {
    return '';
  }
  const parts = normalized.split('/').filter(Boolean);
  return parts.length > 0 ? parts[parts.length - 1] : normalized;
}

function normalizeProjectPath(path) {
  const value = String(path || '').trim();
  if (!value) {
    return '';
  }
  if (!value.startsWith('/')) {
    return '';
  }
  return value.replace(/\/+$/, '') || '/';
}

function readSelectedProjectPath() {
  if (typeof window === 'undefined') {
    return '';
  }
  return normalizeProjectPath(window.localStorage.getItem(SELECTED_PROJECT_PATH_KEY) || '');
}

function readManualProjectPath() {
  if (typeof window === 'undefined') {
    return '';
  }
  return normalizeProjectPath(window.localStorage.getItem(MANUAL_PROJECT_PATH_KEY) || '');
}

function persistSelectedProjectPath(path) {
  if (typeof window === 'undefined') {
    return;
  }
  const normalized = normalizeProjectPath(path);
  if (!normalized) {
    window.localStorage.removeItem(SELECTED_PROJECT_PATH_KEY);
    return;
  }
  window.localStorage.setItem(SELECTED_PROJECT_PATH_KEY, normalized);
}

function persistManualProjectPath(path) {
  if (typeof window === 'undefined') {
    return;
  }
  const normalized = normalizeProjectPath(path);
  if (!normalized) {
    window.localStorage.removeItem(MANUAL_PROJECT_PATH_KEY);
    return;
  }
  window.localStorage.setItem(MANUAL_PROJECT_PATH_KEY, normalized);
}

function mergeProjectOptions(discoveredItems = [], manualPath = '') {
  const map = new Map();
  for (const item of Array.isArray(discoveredItems) ? discoveredItems : []) {
    const path = normalizeProjectPath(item?.path || '');
    if (!path) {
      continue;
    }
    map.set(path, {
      path,
      source: String(item?.source || 'codex'),
      label: String(item?.label || pathBaseName(path) || path)
    });
  }

  const manual = normalizeProjectPath(manualPath);
  if (manual) {
    const existing = map.get(manual);
    map.set(manual, {
      path: manual,
      source: existing?.source || 'manual',
      label: existing?.label || pathBaseName(manual) || manual
    });
  }

  return Array.from(map.values()).sort((a, b) => String(a.path).localeCompare(String(b.path)));
}

async function loadProjectOptions() {
  let discovered = [];
  try {
    const res = await fetch(`${TERMINAL_API_BASE}/projects/discover`);
    if (res.ok) {
      const body = await res.json();
      discovered = Array.isArray(body?.items) ? body.items : [];
    }
  } catch {
    discovered = [];
  }

  const options = mergeProjectOptions(discovered, manualProjectPath.value);
  projectOptions.value = options;
  const selected = normalizeProjectPath(selectedProjectPath.value);
  if (!selected) {
    selectedProjectPath.value = '';
    return;
  }
  if (!options.some((x) => x.path === selected)) {
    selectedProjectPath.value = '';
    persistSelectedProjectPath('');
  }
}

function onProjectSelect(path) {
  const normalized = normalizeProjectPath(path);
  selectedProjectPath.value = normalized;
  persistSelectedProjectPath(normalized);
}

async function clearManualProjectPath() {
  manualProjectPath.value = '';
  selectedProjectPath.value = '';
  persistManualProjectPath('');
  persistSelectedProjectPath('');
  await loadProjectOptions();
}

function openDirPickerModal() {
  const firstAllowedRoot = normalizeProjectPath(fsAllowedRoots.value?.[0] || '');
  const firstDiscovered = normalizeProjectPath(projectOptions.value?.[0]?.path || '');
  const preferred = normalizeProjectPath(selectedProjectPath.value || manualProjectPath.value || firstAllowedRoot || firstDiscovered || '/home');
  dirPickerPath.value = preferred || '/home';
  showDirPicker.value = true;
  loadDirPicker(dirPickerPath.value);
}

function closeDirPickerModal() {
  showDirPicker.value = false;
}

async function loadDirPicker(path) {
  const normalized = normalizeProjectPath(path);
  if (!normalized) {
    return;
  }
  dirPickerBusy.value = true;
  dirPickerError.value = '';
  try {
    const res = await fetch(`${TERMINAL_API_BASE}/fs/dirs?path=${encodeURIComponent(normalized)}`);
    if (!res.ok) {
      throw new Error(await readError(res, `load dirs failed: ${res.status}`));
    }
    const body = await res.json();
    dirPickerPath.value = normalizeProjectPath(body?.path || normalized) || normalized;
    dirPickerItems.value = Array.isArray(body?.items) ? body.items : [];
  } catch (err) {
    dirPickerError.value = String(err?.message || err);
    dirPickerItems.value = [];

    // Fallback to the first allowed root if current path is not accessible.
    const firstAllowedRoot = normalizeProjectPath(fsAllowedRoots.value?.[0] || '');
    if (firstAllowedRoot && firstAllowedRoot !== normalized) {
      try {
        const retry = await fetch(`${TERMINAL_API_BASE}/fs/dirs?path=${encodeURIComponent(firstAllowedRoot)}`);
        if (retry.ok) {
          const body = await retry.json();
          dirPickerPath.value = normalizeProjectPath(body?.path || firstAllowedRoot) || firstAllowedRoot;
          dirPickerItems.value = Array.isArray(body?.items) ? body.items : [];
          dirPickerError.value = '';
        }
      } catch {
        // keep original error
      }
    }
  } finally {
    dirPickerBusy.value = false;
  }
}

function openDirPickerChild(path) {
  const normalized = normalizeProjectPath(path);
  if (!normalized) {
    return;
  }
  loadDirPicker(normalized);
}

function openDirPickerParent() {
  const current = normalizeProjectPath(dirPickerPath.value);
  if (!current || current === '/') {
    return;
  }
  const parts = current.split('/').filter(Boolean);
  parts.pop();
  const parent = parts.length > 0 ? `/${parts.join('/')}` : '/';
  loadDirPicker(parent);
}

async function applyDirPickerSelection() {
  const selected = normalizeProjectPath(dirPickerPath.value);
  if (!selected) {
    return;
  }
  manualProjectPath.value = selected;
  selectedProjectPath.value = selected;
  persistManualProjectPath(selected);
  persistSelectedProjectPath(selected);
  await loadProjectOptions();
  showDirPicker.value = false;
}

function parseFsAllowedRootsText(text) {
  return String(text || '')
    .split('\n')
    .map((x) => normalizeProjectPath(x))
    .filter((x) => x.length > 0);
}

async function loadFsAllowedRootsFromBackend() {
  try {
    const res = await fetch(`${TERMINAL_API_BASE}/settings/fs-allowed-roots`);
    if (!res.ok) {
      throw new Error(await readError(res, `load fs allowed roots failed: ${res.status}`));
    }
    const body = await res.json();
    const roots = Array.isArray(body?.fsAllowedRoots)
      ? body.fsAllowedRoots.map((x) => normalizeProjectPath(x)).filter((x) => x.length > 0)
      : [];
    fsAllowedRoots.value = roots;
    fsAllowedRootsText.value = roots.join('\n');
  } catch {
    // keep local empty value if backend unavailable
  }
}

async function saveFsAllowedRootsToBackend() {
  fsRootsBusy.value = true;
  error.value = '';
  try {
    const roots = parseFsAllowedRootsText(fsAllowedRootsText.value);
    const res = await fetch(`${TERMINAL_API_BASE}/settings/fs-allowed-roots`, {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ fsAllowedRoots: roots })
    });
    if (!res.ok) {
      throw new Error(await readError(res, `save fs allowed roots failed: ${res.status}`));
    }
    const body = await res.json();
    const saved = Array.isArray(body?.fsAllowedRoots)
      ? body.fsAllowedRoots.map((x) => normalizeProjectPath(x)).filter((x) => x.length > 0)
      : roots;
    fsAllowedRoots.value = saved;
    fsAllowedRootsText.value = saved.join('\n');
  } catch (err) {
    error.value = String(err?.message || err);
  } finally {
    fsRootsBusy.value = false;
  }
}

function addGlobalQuickCommand() {
  const next = normalizeQuickCommands(displaySettings.value.general?.quickCommands || []);
  next.push({ id: `global-${Date.now()}`, label: '', content: '', sendMode: 'auto', enabled: true, order: next.length });
  displaySettings.value.general.quickCommands = next;
}

function removeGlobalQuickCommand(index) {
  const next = normalizeQuickCommands(displaySettings.value.general?.quickCommands || []);
  if (index < 0 || index >= next.length) {
    return;
  }
  next.splice(index, 1);
  displaySettings.value.general.quickCommands = next.map((item, idx) => ({ ...item, order: idx }));
}

function openQuickCommandEditor() {
  openSettings('general-quick-commands');
}

async function readError(res, fallback) {
  try {
    const text = await res.text();
    return text || fallback;
  } catch {
    return fallback;
  }
}
</script>

<template>
  <div class="terminal-min-page">
    <div class="terminal-vscode-shell">
      <section class="terminal-min-body panel terminal-vscode-main">
        <small v-if="error" class="mono error-text">{{ error }}</small>
        <TerminalTab
          v-if="terminalStore.activeSessionId"
          :key="terminalStore.activeSessionId"
          :session-id="terminalStore.activeSessionId"
          :display-settings="displaySettings"
          :quick-commands="mergedQuickCommands"
          @open-quick-command-editor="openQuickCommandEditor"
        />
        <div v-else class="terminal-empty">
          <h4>没有活动终端</h4>
          <p>点击右侧“新建”开始。</p>
        </div>
      </section>

      <aside class="panel terminal-vscode-sidebar">
        <div class="terminal-project-panel">
          <label>项目</label>
          <div class="terminal-project-row">
            <select :value="selectedProjectPath" @change="onProjectSelect($event.target.value)">
              <option value="">未锁定项目</option>
              <option v-for="item in projectOptions" :key="item.path" :value="item.path">
                {{ item.label }} · {{ item.source }}
              </option>
            </select>
            <div class="terminal-project-actions">
              <button @click="openDirPickerModal">选择</button>
              <button v-if="manualProjectPath" @click="clearManualProjectPath">清除</button>
            </div>
          </div>
          <small class="mono subtle">当前项目：{{ selectedProjectPath || '未锁定' }}</small>
        </div>

        <div class="terminal-vscode-actions">
          <button :disabled="createBusy" @click="createSession">+ 新建</button>
          <button @click="openSettings()">⚙ 设置</button>
          <button v-if="displaySettings.showRefreshAction" @click="refreshSessions">刷新</button>
          <button v-if="displaySettings.showReconnectAction" :disabled="!terminalStore.activeSessionId" @click="reconnect">重连</button>
          <button v-if="displaySettings.showDisconnectAction" :disabled="!terminalStore.activeSessionId" @click="disconnect">断开</button>
        </div>

        <div class="terminal-vscode-active">
          <span class="badge">总数 {{ sessions.length }}</span>
        </div>

        <div class="terminal-vscode-list">
          <div
            v-for="session in sessions"
            :key="session.sessionId"
            :class="['terminal-session-row', { active: session.sessionId === terminalStore.activeSessionId }]"
            @click="openSessionFromList(session.sessionId)"
          >
            <div class="terminal-session-cwd" :title="sessionWorkingDirectory(session) || '未设置目录'">
              {{ formatSessionWorkingDirectory(session) }}
            </div>
            <div class="terminal-session-open">
              <span class="terminal-session-name">
                <span class="terminal-session-title">{{ sessionCliName(session) }}</span>
                <span v-if="isReadonlySession(session)" class="terminal-session-readonly" title="只读模式">
                  <i class="terminal-session-readonly-icon" aria-hidden="true" />
                  只读
                </span>
              </span>
              <span class="terminal-session-last" :title="`最后活跃：${formatLastActive(session.lastActivityAt)}`">
                {{ formatLastActive(session.lastActivityAt) }}
              </span>
              <span class="terminal-session-status" :title="sessionStatus(session)">
                <i :class="['terminal-session-dot', `is-${statusDotClass(session)}`]" />
              </span>
              <button
                class="terminal-session-end"
                :disabled="terminateBusy"
                title="结束终端"
                aria-label="结束终端"
                @click.stop="terminateSessionById(session.sessionId)"
              >
                🗑
              </button>
            </div>
          </div>
          <div v-if="sessions.length === 0" class="terminal-empty-list">暂无会话</div>
        </div>
      </aside>
    </div>

    <div v-if="showSettings" class="settings-mask" @click.self="closeSettings">
      <section class="panel settings-drawer settings-two-col">
        <div class="settings-head">
          <h3>终端设置中心</h3>
          <button @click="closeSettings">关闭</button>
        </div>

        <div class="settings-split">
          <aside class="settings-nav">
            <div class="settings-nav-title">通用设置</div>
            <button :class="sectionButtonClass('general-appearance')" @click="settingsSection = 'general-appearance'">外观与交互</button>
            <button :class="sectionButtonClass('general-behavior')" @click="settingsSection = 'general-behavior'">终端行为</button>
            <button :class="sectionButtonClass('general-default-profile')" @click="settingsSection = 'general-default-profile'">默认配置</button>
            <button :class="sectionButtonClass('general-quick-commands')" @click="settingsSection = 'general-quick-commands'">快捷指令</button>
            <button :class="sectionButtonClass('general-file-browser')" @click="settingsSection = 'general-file-browser'">目录浏览</button>
            <button :class="sectionButtonClass('general-mcp')" @click="settingsSection = 'general-mcp'">MCP配置</button>
            <button :class="sectionButtonClass('general-skill')" @click="settingsSection = 'general-skill'">SKILL配置</button>
            <button :class="sectionButtonClass('general-nodejs')" @click="settingsSection = 'general-nodejs'">Node.js配置</button>
            <button :class="sectionButtonClass('general-git')" @click="settingsSection = 'general-git'">Git配置</button>

            <div class="settings-nav-title">指令配置</div>
            <button
              v-for="item in cliConfigEntries"
              :key="item.key"
              :class="sectionButtonClass(cliSectionKey(item.key))"
              @click="settingsSection = cliSectionKey(item.key)"
            >
              {{ item.name }}
            </button>
            <button class="settings-nav-add" @click="addCliConfig">+ 添加新的配置</button>
          </aside>

          <section class="settings-editor">
            <div v-if="settingsSection === 'general-appearance'" class="list settings-body">
              <h4>外观与交互</h4>
              <div class="row">
                <label>
                  字体
                  <input v-model="displaySettings.fontFamily" placeholder="JetBrains Mono, monospace" />
                </label>
              </div>
              <div class="row">
                <label>
                  字号
                  <input v-model.number="displaySettings.fontSize" type="number" min="10" max="40" />
                </label>
                <label>
                  行高
                  <input v-model.number="displaySettings.lineHeight" type="number" min="1" max="2" step="0.05" />
                </label>
                <label>
                  字距
                  <input v-model.number="displaySettings.letterSpacing" type="number" min="-2" max="8" step="0.2" />
                </label>
              </div>
              <div class="row">
                <label>
                  光标样式
                  <select v-model="displaySettings.cursorStyle">
                    <option value="block">block</option>
                    <option value="bar">bar</option>
                    <option value="underline">underline</option>
                  </select>
                </label>
                <label>
                  渲染器
                  <select v-model="displaySettings.rendererType">
                    <option value="auto">auto</option>
                    <option value="webgl">webgl</option>
                    <option value="dom">dom</option>
                  </select>
                </label>
                <label>
                  主题
                  <select v-model="displaySettings.themeId">
                    <option value="dark-classic">dark-classic</option>
                    <option value="dark-modern">dark-modern</option>
                    <option value="light">light</option>
                  </select>
                </label>
              </div>
              <div class="row">
                <label>
                  回滚行数
                  <input v-model.number="displaySettings.scrollback" type="number" min="1000" max="200000" />
                </label>
                <label>
                  语音粘贴兜底秒数
                  <input v-model.number="displaySettings.speechPasteGraceSeconds" type="number" min="0" max="120" />
                </label>
              </div>
              <div class="terminal-settings-check-grid">
                <label class="inline-check"><input v-model="displaySettings.cursorBlink" type="checkbox" /> 光标闪烁</label>
                <label class="inline-check"><input v-model="displaySettings.rightClickPaste" type="checkbox" /> 右键粘贴</label>
                <label class="inline-check"><input v-model="displaySettings.copyOnSelect" type="checkbox" /> 选中即复制</label>
                <label class="inline-check"><input v-model="displaySettings.keepScrollbackOnAltScreen" type="checkbox" /> 保留滚动历史（忽略备用屏）</label>
                <label class="inline-check"><input v-model="displaySettings.showResizeOverlay" type="checkbox" /> 显示尺寸浮层</label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存通用设置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-behavior'" class="list settings-body">
              <h4>终端行为</h4>
              <div class="row">
                <label>
                  默认 CLI 配置
                  <select :value="displaySettings.defaultCliKey" @change="setDefaultCliKey($event.target.value)">
                    <option v-for="item in cliConfigEntries" :key="item.key" :value="item.key">
                      {{ item.name }}
                    </option>
                  </select>
                </label>
              </div>
              <div class="row">
                <label>
                  默认列数 cols
                  <input v-model.number="displaySettings.defaultCols" type="number" min="40" max="400" />
                </label>
                <label>
                  默认行数 rows
                  <input v-model.number="displaySettings.defaultRows" type="number" min="10" max="200" />
                </label>
              </div>
              <div class="terminal-settings-check-grid">
                <label class="inline-check"><input v-model="displaySettings.autoCreateOnEmpty" type="checkbox" /> 无会话时自动创建终端</label>
                <label class="inline-check"><input v-model="displaySettings.replayOnEntry" type="checkbox" /> 进入终端页时 replay 刷新</label>
                <label class="inline-check"><input v-model="displaySettings.showRefreshAction" type="checkbox" /> 显示刷新按钮</label>
                <label class="inline-check"><input v-model="displaySettings.showReconnectAction" type="checkbox" /> 显示重连按钮</label>
                <label class="inline-check"><input v-model="displaySettings.showDisconnectAction" type="checkbox" /> 显示断开按钮</label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存行为设置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-default-profile'" class="list settings-body">
              <h4>Node-PTY 默认配置</h4>
              <p class="subtle">通用配置仅作为全局兜底：启动目录与环境变量。CLI 启动命令与参数请在各 CLI 配置中设置。</p>
              <div class="row">
                <label>
                  启动目录
                  <input v-model="displaySettings.general.defaultProfile.cwd" placeholder="留空则使用后端/会话目录" />
                </label>
              </div>
              <div class="row">
                <label>
                  环境变量（KEY=VALUE 每行一个）
                  <textarea v-model="displaySettings.general.defaultProfile.envText" rows="3" />
                </label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存默认配置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-quick-commands'" class="list settings-body">
              <h4>快捷指令</h4>
              <div class="row">
                <button @click="addGlobalQuickCommand">新增指令</button>
                <button @click="saveGeneralSettings">保存快捷指令</button>
              </div>
              <div class="quickcmd-editor-list">
                <div v-for="(item, idx) in displaySettings.general.quickCommands" :key="item.id || idx" class="quickcmd-editor-row">
                  <input v-model="item.label" placeholder="显示名称" />
                  <input v-model="item.content" placeholder="指令内容" />
                  <select v-model="item.sendMode">
                    <option value="auto">auto</option>
                    <option value="enter">enter</option>
                    <option value="raw">raw</option>
                  </select>
                  <label class="inline-check"><input v-model="item.enabled" type="checkbox" /> 启用</label>
                  <button @click="removeGlobalQuickCommand(idx)">删除</button>
                </div>
                <div v-if="displaySettings.general.quickCommands.length === 0" class="terminal-empty-list">暂无全局快捷指令</div>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-file-browser'" class="list settings-body">
              <h4>目录浏览</h4>
              <p class="subtle">系统选择目录时仅允许访问以下根路径（每行一个绝对路径）。</p>
              <div class="row">
                <label>
                  允许访问的根路径
                  <textarea v-model="fsAllowedRootsText" rows="6" placeholder="/home&#10;/workspace&#10;/www" />
                </label>
              </div>
              <div class="row">
                <button :disabled="fsRootsBusy" @click="saveFsAllowedRootsToBackend">保存目录浏览配置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-mcp'" class="list settings-body">
              <h4>MCP配置</h4>
              <div class="row">
                <label class="inline-check"><input v-model="displaySettings.general.mcp.enabled" type="checkbox" /> 启用 MCP 默认配置</label>
              </div>
              <div class="row">
                <label>
                  MCP Server 列表（每行一个）
                  <textarea v-model="displaySettings.general.mcp.serverListText" rows="6" placeholder="例如: default-mcp" />
                </label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存 MCP 配置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-skill'" class="list settings-body">
              <h4>SKILL配置</h4>
              <div class="row">
                <label class="inline-check"><input v-model="displaySettings.general.skill.enabled" type="checkbox" /> 启用 SKILL 默认配置</label>
              </div>
              <div class="row">
                <label>
                  SKILL 列表（每行一个）
                  <textarea v-model="displaySettings.general.skill.skillListText" rows="6" placeholder="例如: skill-installer" />
                </label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存 SKILL 配置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-nodejs'" class="list settings-body">
              <h4>Node.js配置</h4>
              <div class="row">
                <label>
                  node 路径
                  <input v-model="displaySettings.general.nodejs.nodePath" placeholder="例如 /usr/bin/node" />
                </label>
                <label>
                  npm 客户端
                  <input v-model="displaySettings.general.nodejs.npmClient" placeholder="npm / pnpm / yarn" />
                </label>
              </div>
              <div class="row">
                <label>
                  registry
                  <input v-model="displaySettings.general.nodejs.registry" placeholder="例如 https://registry.npmjs.org" />
                </label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存 Node.js 配置</button>
              </div>
            </div>

            <div v-else-if="settingsSection === 'general-git'" class="list settings-body">
              <h4>Git配置</h4>
              <div class="row">
                <label>
                  user.name
                  <input v-model="displaySettings.general.git.userName" />
                </label>
                <label>
                  user.email
                  <input v-model="displaySettings.general.git.userEmail" />
                </label>
              </div>
              <div class="row">
                <label>
                  defaultBranch
                  <input v-model="displaySettings.general.git.defaultBranch" placeholder="main" />
                </label>
              </div>
              <div class="row">
                <button @click="saveGeneralSettings">保存 Git 配置</button>
              </div>
            </div>

            <div v-else-if="isCliProfileSection(settingsSection)" class="list settings-body">
              <h4>{{ cliTitleFromKey(activeCliKey) }}</h4>
              <div class="command-config-grid">
                <label>
                  名称
                  <input v-model="activeCliConfig.defaultProfile.name" placeholder="用于左侧列表展示" />
                </label>
                <label>
                  图标
                  <select v-model="activeCliConfig.defaultProfile.icon">
                    <option v-for="icon in CLI_ICON_OPTIONS" :key="icon" :value="icon">{{ icon }}</option>
                  </select>
                </label>
                <label>
                  启动命令
                  <input v-model="activeCliConfig.defaultProfile.commandLine" :placeholder="activeCliKey === 'codex' ? 'codex' : activeCliKey === 'claude' ? 'claude' : '/bin/bash'" />
                </label>
                <label>
                  启动目录
                  <input v-model="activeCliConfig.defaultProfile.cwd" placeholder="留空则优先当前会话目录，再用通用配置" />
                </label>
                <label class="command-config-span-2">
                  启动参数（args，每行一个）
                  <textarea v-model="activeCliConfig.defaultProfile.startupArgsText" rows="3" />
                </label>
                <label class="command-config-span-2">
                  环境变量（KEY=VALUE 每行一个）
                  <textarea v-model="activeCliConfig.defaultProfile.envText" rows="3" />
                </label>
              </div>
              <div class="row command-config-actions">
                <button @click="saveCliConfig">保存指令配置</button>
                <button @click="deleteCliConfig">删除当前配置</button>
              </div>
            </div>

          </section>
        </div>
      </section>
    </div>

    <div v-if="showDirPicker" class="settings-mask" @click.self="closeDirPickerModal">
      <section class="panel terminal-min-settings">
        <div class="settings-head">
          <h3>选择项目目录</h3>
          <button @click="closeDirPickerModal">关闭</button>
        </div>
        <div class="row">
          <label>
            当前目录
            <input v-model="dirPickerPath" placeholder="/home/yueyuan/pty-agent" @keydown.enter.prevent="loadDirPicker(dirPickerPath)" />
          </label>
        </div>
        <div class="row terminal-project-actions">
          <button :disabled="dirPickerBusy" @click="openDirPickerParent">上级目录</button>
          <button :disabled="dirPickerBusy" @click="loadDirPicker(dirPickerPath)">刷新</button>
          <button :disabled="dirPickerBusy || !dirPickerPath" @click="applyDirPickerSelection">选择当前目录</button>
        </div>
        <small v-if="dirPickerError" class="mono error-text">{{ dirPickerError }}</small>
        <div class="terminal-dir-list">
          <button
            v-for="item in dirPickerItems"
            :key="item.path"
            class="terminal-dir-item"
            :disabled="dirPickerBusy"
            @click="openDirPickerChild(item.path)"
          >
            <span>{{ item.name }}</span>
            <small class="mono subtle">{{ item.path }}</small>
          </button>
          <div v-if="!dirPickerBusy && dirPickerItems.length === 0" class="terminal-empty-list">当前目录无可用子目录</div>
        </div>
      </section>
    </div>

  </div>
</template>
