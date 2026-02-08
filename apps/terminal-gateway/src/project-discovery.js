import { existsSync, readFileSync } from 'node:fs';
import { basename, isAbsolute } from 'node:path';

const PROJECT_HEADER_RE = /^\s*\[projects\."([^"]+)"\]\s*$/;

export function discoverProjects(options = {}) {
  const codexConfigPath = String(options.codexConfigPath || '').trim();
  const claudeConfigPath = String(options.claudeConfigPath || '').trim();
  const items = [];

  if (codexConfigPath) {
    items.push(...readCodexProjects(codexConfigPath));
  }

  return {
    items: dedupeProjects(items),
    meta: {
      codexConfigPath: codexConfigPath || null,
      claudeConfigPath: claudeConfigPath || null
    }
  };
}

export function readCodexProjects(configPath) {
  if (!configPath || !existsSync(configPath)) {
    return [];
  }

  let content = '';
  try {
    content = readFileSync(configPath, 'utf8');
  } catch {
    return [];
  }

  const items = [];
  for (const line of content.split('\n')) {
    const match = line.match(PROJECT_HEADER_RE);
    if (!match) {
      continue;
    }
    const path = String(match[1] || '').trim();
    if (!path || !isAbsolute(path)) {
      continue;
    }
    items.push({
      path,
      label: basename(path) || path,
      source: 'codex'
    });
  }
  return items;
}

function dedupeProjects(items = []) {
  const deduped = new Map();
  for (const item of items) {
    if (!item?.path) {
      continue;
    }
    if (!deduped.has(item.path)) {
      deduped.set(item.path, item);
    }
  }
  return Array.from(deduped.values()).sort((a, b) => String(a.path).localeCompare(String(b.path)));
}
