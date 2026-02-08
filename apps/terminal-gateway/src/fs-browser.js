import { readdirSync, realpathSync, statSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';

export function listDirectories(inputPath, options = {}) {
  const requestedPath = String(inputPath || '').trim();
  if (!requestedPath || !isAbsolute(requestedPath)) {
    throw new Error('path must be an absolute path');
  }

  const allowedRoots = normalizeAllowedRoots(options.allowedRoots || []);
  if (allowedRoots.length === 0) {
    throw new Error('fs browser allowedRoots is empty');
  }

  const resolved = resolve(requestedPath);
  const targetRealPath = realpathSyncSafe(resolved);
  if (!targetRealPath) {
    throw new Error('path does not exist');
  }

  if (!isUnderAllowedRoots(targetRealPath, allowedRoots)) {
    throw new Error('path is outside allowed roots');
  }

  const rows = [];
  for (const name of readdirSync(targetRealPath)) {
    const childPath = join(targetRealPath, name);
    let stats;
    try {
      stats = statSync(childPath);
    } catch {
      continue;
    }
    if (!stats.isDirectory()) {
      continue;
    }

    const childRealPath = realpathSyncSafe(childPath);
    if (!childRealPath) {
      continue;
    }
    if (!isUnderAllowedRoots(childRealPath, allowedRoots)) {
      continue;
    }

    rows.push({
      name,
      path: childRealPath,
      hasChildren: hasDirectoryChild(childRealPath, allowedRoots)
    });
  }

  rows.sort((a, b) => String(a.name).localeCompare(String(b.name)));
  return {
    path: targetRealPath,
    items: rows
  };
}

function hasDirectoryChild(dirPath, allowedRoots) {
  try {
    for (const name of readdirSync(dirPath)) {
      const childPath = join(dirPath, name);
      let stats;
      try {
        stats = statSync(childPath);
      } catch {
        continue;
      }
      if (!stats.isDirectory()) {
        continue;
      }
      const childRealPath = realpathSyncSafe(childPath);
      if (!childRealPath) {
        continue;
      }
      if (isUnderAllowedRoots(childRealPath, allowedRoots)) {
        return true;
      }
    }
  } catch {
    return false;
  }
  return false;
}

function normalizeAllowedRoots(items = []) {
  const roots = [];
  for (const raw of items) {
    const x = String(raw || '').trim();
    if (!x || !isAbsolute(x)) {
      continue;
    }
    const resolved = resolve(x);
    const real = realpathSyncSafe(resolved);
    if (!real) {
      continue;
    }
    roots.push(real);
  }

  return Array.from(new Set(roots));
}

function isUnderAllowedRoots(targetRealPath, allowedRoots) {
  for (const root of allowedRoots) {
    if (targetRealPath === root) {
      return true;
    }
    if (targetRealPath.startsWith(`${root}/`)) {
      return true;
    }
  }
  return false;
}

function realpathSyncSafe(inputPath) {
  try {
    return realpathSync(inputPath);
  } catch {
    return '';
  }
}
