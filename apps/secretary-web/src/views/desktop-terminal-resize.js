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

function readFitAddonGeometry(fitAddon) {
  if (!fitAddon || typeof fitAddon.proposeDimensions !== 'function') {
    return null;
  }

  try {
    const dims = fitAddon.proposeDimensions();
    const cols = Math.max(0, Number(dims?.cols) || 0);
    const rows = Math.max(0, Number(dims?.rows) || 0);
    if (cols <= 0 || rows <= 0) {
      return null;
    }
    return { cols, rows };
  } catch {
    return null;
  }
}

function readTerminalScrollbarReserveWidth(hostElement) {
  if (!hostElement || typeof hostElement.querySelector !== 'function') {
    return 0;
  }

  const viewportElement = hostElement.querySelector('.xterm-viewport');
  if (!viewportElement || typeof viewportElement.getBoundingClientRect !== 'function') {
    return 0;
  }

  const rect = viewportElement.getBoundingClientRect();
  return Math.max(0, Number(rect.width) || 0);
}

function readTerminalCellSize(term) {
  const dims = term?._core?._renderService?.dimensions?.css?.cell;
  const width = Number(dims?.width) || 0;
  const height = Number(dims?.height) || 0;
  if (width <= 0 || height <= 0) {
    return null;
  }
  return { width, height };
}

function readTerminalContentRect(hostElement) {
  if (hostElement && typeof hostElement.querySelector === 'function') {
    const screenElement = hostElement.querySelector('.xterm-screen');
    if (screenElement && typeof screenElement.getBoundingClientRect === 'function') {
      const rect = screenElement.getBoundingClientRect();
      const width = Math.max(0, Number(rect.width) || 0);
      const height = Math.max(0, Number(rect.height) || 0);
      if (width > 0 && height > 0) {
        return { width, height };
      }
    }
  }

  if (!hostElement || typeof hostElement.getBoundingClientRect !== 'function') {
    return null;
  }

  const rect = hostElement.getBoundingClientRect();
  const scrollbarReserveWidth = readTerminalScrollbarReserveWidth(hostElement);
  return {
    width: Math.max(0, (Number(rect.width) || 0) - scrollbarReserveWidth),
    height: Math.max(0, Number(rect.height) || 0)
  };
}

function measureTerminalGeometry(hostElement, term) {
  const rect = readTerminalContentRect(hostElement);
  const hostWidth = Math.max(0, Number(rect?.width) || 0);
  const hostHeight = Math.max(0, Number(rect?.height) || 0);
  const cell = readTerminalCellSize(term);
  if (!cell || hostWidth <= 0 || hostHeight <= 0) {
    return null;
  }

  const cols = Math.max(2, Math.floor(hostWidth / cell.width));
  const rows = Math.max(1, Math.floor(hostHeight / cell.height));
  return { cols, rows };
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

  const preferFitAddon = Boolean(fitAddon && typeof fitAddon.proposeDimensions === 'function');
  let previous = null;
  let stableCount = 0;
  let fallbackDomMeasurement = null;

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    if (isDocumentHidden() || !isTerminalViewportRenderable(activeCenterTab, hostElement)) {
      await wait(intervalMs);
      continue;
    }

    const fitGeometry = readFitAddonGeometry(fitAddon);
    if (fitGeometry?.cols > 0 && fitGeometry?.rows > 0) {
      if (previous && previous.cols === fitGeometry.cols && previous.rows === fitGeometry.rows) {
        stableCount += 1;
      } else {
        previous = fitGeometry;
        stableCount = 1;
      }

      if (stableCount >= 2) {
        return fitGeometry;
      }

      await wait(intervalMs);
      continue;
    }

    const current = measureTerminalGeometry(hostElement, term);
    if (current?.cols > 0 && current?.rows > 0) {
      if (preferFitAddon) {
        fallbackDomMeasurement = current;
      } else {
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
    }

    await wait(intervalMs);
  }

  if (previous && previous.cols > 0 && previous.rows > 0) {
    return previous;
  }

  return fallbackDomMeasurement && fallbackDomMeasurement.cols > 0 && fallbackDomMeasurement.rows > 0
    ? fallbackDomMeasurement
    : null;
}
