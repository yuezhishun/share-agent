export function detectDefaultCwdPath() {
  if (typeof navigator !== 'undefined') {
    const platform = String(navigator.platform || navigator.userAgent || '').toLowerCase();
    if (platform.includes('win')) {
      return 'D:/workspace/code';
    }
  }

  return '/home/yueyuan';
}

export const DEFAULT_CWD_PATH = detectDefaultCwdPath();
export const FILE_CHUNK_BYTES = 64 * 1024;
export const FILE_CHUNK_MAX_LINES = 800;
export const COMBO_KEY_INTERVAL_MS = 120;
export const QUICK_COMMAND_INTERVAL_MS = 300;
export const SHORTCUT_GROUP_ORDER = ['控制键', '导航键', '常用命令', 'custom', '自定义'];
const IMAGE_FILE_EXTENSIONS = new Set([
  '.apng',
  '.avif',
  '.bmp',
  '.gif',
  '.ico',
  '.jpeg',
  '.jpg',
  '.png',
  '.svg',
  '.webp'
]);

const codexQuickCommand = 'codex --dangerously-bypass-approvals-and-sandbox';

export const BUILT_IN_SHORTCUT_ITEMS = [
  { id: 'esc', label: 'esc', group: '控制键', value: '\u001b' },
  { id: 'tab', label: 'tab', group: '控制键', value: '\t' },
  { id: 'enter', label: 'enter', group: '控制键', value: '\r' },
  { id: 'altEnter', label: 'alt+enter', group: '控制键', value: '\u001b\r' },
  { id: 'backspace', label: 'backspace', group: '控制键', value: '\u007f' },
  { id: 'delete', label: 'delete', group: '控制键', value: '\u001b[3~' },
  { id: 'ctrlC', label: 'ctrl+c', group: '控制键', value: '\u0003' },
  { id: 'altTab', label: 'alt+tab', group: '控制键', value: '\u001b[Z' },
  { id: 'codex', label: 'codex', group: '常用命令', sequence: [codexQuickCommand, '\r'], intervalMs: QUICK_COMMAND_INTERVAL_MS },
  { id: 'home', label: 'home', group: '导航键', value: '\u001b[H' },
  { id: 'end', label: 'end', group: '导航键', value: '\u001b[F' },
  { id: 'arrowLeft', label: '←', group: '导航键', value: '\u001b[D' },
  { id: 'arrowUp', label: '↑', group: '导航键', value: '\u001b[A' },
  { id: 'arrowDown', label: '↓', group: '导航键', value: '\u001b[B' },
  { id: 'arrowRight', label: '→', group: '导航键', value: '\u001b[C' },
  { id: 'pgUp', label: 'pgUp', group: '导航键', value: '\u001b[5~' },
  { id: 'pgDn', label: 'pgDn', group: '导航键', value: '\u001b[6~' },
  { id: 'at', label: '@', group: '常用命令', value: '@' },
  { id: 'bang', label: '!', group: '常用命令', value: '!' },
  { id: 'slash', label: '/', group: '常用命令', value: '/' },
  { id: 'ls', label: 'ls', group: '常用命令', sequence: ['ls', '\r'], intervalMs: QUICK_COMMAND_INTERVAL_MS },
  { id: 'pwd', label: 'pwd', group: '常用命令', sequence: ['pwd', '\r'], intervalMs: QUICK_COMMAND_INTERVAL_MS },
  { id: 'resume', label: '/resume', group: '常用命令', sequence: ['/resume', '\r'], intervalMs: QUICK_COMMAND_INTERVAL_MS },
  { id: 'new', label: '/new', group: '常用命令', sequence: ['/new', '\r'], intervalMs: QUICK_COMMAND_INTERVAL_MS },
  { id: 'status', label: '/status', group: '常用命令', sequence: ['/status', '\r'], intervalMs: QUICK_COMMAND_INTERVAL_MS }
];

export function buildRecipeEditor(seed, normalizeSelectableCwd, formatCommandLine) {
  const source = seed || {};
  const command = String(source.command || '').trim();
  const args = Array.isArray(source.args) ? source.args : [];
  const envOverrides = source.envOverrides && typeof source.envOverrides === 'object' && !Array.isArray(source.envOverrides)
    ? source.envOverrides
    : (source.env && typeof source.env === 'object' && !Array.isArray(source.env) ? source.env : {});
  return {
    name: String(source.name || ''),
    cwd: normalizeSelectableCwd(source.cwd),
    commandLine: command ? formatCommandLine(command, args) : '',
    envInput: JSON.stringify(envOverrides, null, 2),
    group: String(source.group || 'general'),
    supportedOs: Array.isArray(source.supportedOs) ? source.supportedOs.map((item) => String(item || '').trim()).filter(Boolean) : [],
    selectedEnvGroupNames: Array.isArray(source.envGroupNames) && source.envGroupNames.length > 0
      ? [String(source.envGroupNames[0] || '').trim()].filter(Boolean)
      : [],
    selectedEnvEntryIds: Array.isArray(source.envEntryIds) ? source.envEntryIds.map((item) => String(item || '').trim()).filter(Boolean) : []
  };
}

export function parseJsonOrDefault(input, fallback) {
  const text = String(input || '').trim();
  if (!text) {
    return fallback;
  }
  return JSON.parse(text);
}

export function parseRecipeEnv(input) {
  const raw = String(input || '').trim();
  if (!raw) {
    return {};
  }
  const parsed = JSON.parse(raw);
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('环境变量必须是 JSON 对象');
  }
  return parsed;
}

export function compressPath(path) {
  const raw = String(path || '').trim();
  if (!raw) {
    return '/';
  }

  const normalized = raw.replace(/\\/g, '/').replace(/\/+/g, '/');
  const segments = normalized.split('/').filter(Boolean);
  if (segments.length <= 3) {
    return normalized;
  }

  return `.../${segments.slice(-3).join('/')}`;
}

function extractCommandLabel(command) {
  const raw = String(command || '').trim();
  if (!raw) {
    return 'bash';
  }

  const match = raw.match(/^"([^"]+)"|^'([^']+)'|^(\S+)/);
  const token = String(match?.[1] || match?.[2] || match?.[3] || raw).trim();
  if (!token) {
    return 'bash';
  }

  const normalized = token.replace(/\\/g, '/');
  const baseName = normalized.split('/').filter(Boolean).pop() || normalized;
  return baseName.replace(/\.(exe|cmd|bat)$/i, '') || 'bash';
}

export function normalizeShortcutGroup(value) {
  const text = String(value || '').trim();
  return text || 'custom';
}

export function compareShortcutGroup(a, b) {
  const left = normalizeShortcutGroup(a);
  const right = normalizeShortcutGroup(b);
  const leftIndex = SHORTCUT_GROUP_ORDER.indexOf(left);
  const rightIndex = SHORTCUT_GROUP_ORDER.indexOf(right);
  if (leftIndex >= 0 || rightIndex >= 0) {
    if (leftIndex < 0) {
      return 1;
    }
    if (rightIndex < 0) {
      return -1;
    }
    if (leftIndex !== rightIndex) {
      return leftIndex - rightIndex;
    }
  }
  return left.localeCompare(right, 'zh-Hans-CN');
}

export function formatNodeOption(node) {
  if (!node) {
    return '未知节点';
  }
  const name = String(node.node_name || node.node_id || 'node').trim();
  const role = String(node.node_role || '').trim();
  const status = node.node_online === false ? 'offline' : 'online';
  return role ? `${name} · ${role} · ${status}` : `${name} · ${status}`;
}

export function formatInstanceSummary(instance) {
  const instanceCwd = String(instance?.cwd || '').trim() || '~';
  const instanceCommand = extractCommandLabel(instance?.command);
  return `${instanceCommand} ${instanceCwd}`;
}

export function formatInstanceTooltip(instance, instanceAlias) {
  const instanceCwd = String(instance?.cwd || '').trim() || '~';
  const instanceCommand = String(instance?.command || '').trim() || 'bash';
  const summary = `${instanceCommand}\n${instanceCwd}`;
  if (!instanceAlias) {
    return summary;
  }
  return `${instanceAlias}\n${summary}\n${String(instance?.id || '').trim()}`;
}

export function formatRecipeSummary(item) {
  const recipeCwd = String(item?.cwd || '').trim() || '~';
  const recipeCommand = String(item?.command || '').trim() || 'bash';
  const recipeArgs = Array.isArray(item?.args) && item.args.length > 0 ? ` ${item.args.join(' ')}` : '';
  return `${recipeCwd} | ${recipeCommand}${recipeArgs}`;
}

export function formatSize(size) {
  const value = Number(size || 0);
  if (!Number.isFinite(value) || value <= 0) {
    return '0 B';
  }
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }
  if (value < 1024 * 1024 * 1024) {
    return `${(value / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${(value / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

export function formatFileModifiedTime(value) {
  if (!value) {
    return '未知';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(date);
}

export function formatFileEntryTooltip(item) {
  const name = String(item?.name || item?.path || '').trim() || '未命名';
  const size = item?.kind === 'file' ? `\n大小：${formatSize(item?.size)}` : '';
  const modified = formatFileModifiedTime(item?.mtime);
  return `${name}${size}\n最后修改：${modified}`;
}

export function formatFileTabTooltip(tab) {
  const path = String(tab?.path || '').trim() || String(tab?.name || '').trim() || '未命名';
  const size = formatSize(tab?.size);
  return `${path}\n大小：${size}`;
}

export function resolveEditorKind(path) {
  const lower = String(path || '').trim().toLowerCase();
  for (const extension of IMAGE_FILE_EXTENSIONS) {
    if (lower.endsWith(extension)) {
      return 'image';
    }
  }
  return lower.endsWith('.md') || lower.endsWith('.markdown') ? 'markdown-ir' : 'code';
}
