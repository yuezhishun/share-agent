export function isTerminalViewportRenderable(activeCenterTab, hostElement) {
  if (activeCenterTab !== 'terminal' || !hostElement) {
    return false;
  }

  if (typeof hostElement.getBoundingClientRect !== 'function') {
    return false;
  }

  const rect = hostElement.getBoundingClientRect();
  return Number(rect.width) > 0 && Number(rect.height) > 0;
}

export function normalizeTerminalGeometry(cols, rows) {
  return {
    cols: Math.max(0, Number(cols) || 0),
    rows: Math.max(0, Number(rows) || 0)
  };
}

export function isTerminalGeometryChanged(previous, next) {
  const prev = previous ? normalizeTerminalGeometry(previous.cols, previous.rows) : null;
  const current = normalizeTerminalGeometry(next?.cols, next?.rows);
  if (current.cols <= 0 || current.rows <= 0) {
    return false;
  }
  if (!prev) {
    return true;
  }
  return prev.cols !== current.cols || prev.rows !== current.rows;
}

export async function measureStableTerminalGeometry(options = {}) {
  const {
    activeCenterTab = 'terminal',
    hostElement = null,
    fitAddon = null,
    term = null,
    isDocumentHidden = () => false,
    wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
    attempts = 8,
    intervalMs = 60
  } = options;

  let previous = null;
  let stableCount = 0;

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    if (isDocumentHidden() || !isTerminalViewportRenderable(activeCenterTab, hostElement)) {
      await wait(intervalMs);
      continue;
    }

    try {
      fitAddon?.fit?.();
    } catch {
    }

    const cols = Math.max(0, Number(term?.cols) || 0);
    const rows = Math.max(0, Number(term?.rows) || 0);
    if (cols > 0 && rows > 0) {
      const current = { cols, rows };
      if (previous && previous.cols === current.cols && previous.rows === current.rows) {
        stableCount += 1;
      } else {
        previous = current;
        stableCount = 1;
      }

      if (stableCount >= 2) {
        return current;
      }
    }

    await wait(intervalMs);
  }

  return previous && previous.cols > 0 && previous.rows > 0 ? previous : null;
}
