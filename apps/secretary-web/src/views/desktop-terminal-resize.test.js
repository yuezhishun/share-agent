import test from 'node:test';
import assert from 'node:assert/strict';
import {
  isTerminalGeometryChanged,
  isTerminalViewportRenderable,
  measureStableTerminalGeometry,
  normalizeTerminalGeometry
} from './desktop-terminal-resize.js';

test('isTerminalViewportRenderable should only allow visible terminal viewport', () => {
  const visibleHost = {
    getBoundingClientRect() {
      return { width: 960, height: 640 };
    }
  };
  const hiddenHost = {
    getBoundingClientRect() {
      return { width: 0, height: 0 };
    }
  };

  assert.equal(isTerminalViewportRenderable('terminal', visibleHost), true);
  assert.equal(isTerminalViewportRenderable('file:/tmp/demo.txt', visibleHost), false);
  assert.equal(isTerminalViewportRenderable('terminal', hiddenHost), false);
  assert.equal(isTerminalViewportRenderable('terminal', null), false);
});

test('measureStableTerminalGeometry should wait for two stable non-zero samples', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  const host = {
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    term,
    wait: async () => {}
  });

  assert.deepEqual(geometry, { cols: 120, rows: 34 });
});

test('measureStableTerminalGeometry should prefer fitAddon dimensions over DOM measurement', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  const fitAddon = {
    proposeDimensions() {
      return { cols: 132, rows: 41 };
    }
  };
  const host = {
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    fitAddon,
    term,
    wait: async () => {}
  });

  assert.deepEqual(geometry, { cols: 132, rows: 41 });
});

test('measureStableTerminalGeometry should prefer xterm screen width when scrollbar reserve is present', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  const host = {
    querySelector(selector) {
      if (selector !== '.xterm-screen') {
        return null;
      }
      return {
        getBoundingClientRect() {
          return { width: 940, height: 612 };
        }
      };
    },
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    term,
    wait: async () => {}
  });

  assert.deepEqual(geometry, { cols: 117, rows: 34 });
});

test('measureStableTerminalGeometry should subtract viewport reserve width when screen element is not ready', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  const host = {
    querySelector(selector) {
      if (selector === '.xterm-screen') {
        return null;
      }
      if (selector === '.xterm-viewport') {
        return {
          getBoundingClientRect() {
            return { width: 20, height: 612 };
          }
        };
      }
      return null;
    },
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    term,
    wait: async () => {}
  });

  assert.deepEqual(geometry, { cols: 117, rows: 34 });
});

test('measureStableTerminalGeometry should keep waiting until a stable screen measurement is available', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  let screenReads = 0;
  const host = {
    querySelector(selector) {
      if (selector === '.xterm-screen') {
        screenReads += 1;
        if (screenReads < 2) {
          return {
            getBoundingClientRect() {
              return { width: 0, height: 0 };
            }
          };
        }
        return {
          getBoundingClientRect() {
            return { width: 940, height: 612 };
          }
        };
      }
      if (selector === '.xterm-viewport') {
        const reserveWidth = screenReads < 2 ? 960 : 20;
        return {
          getBoundingClientRect() {
            return { width: reserveWidth, height: 612 };
          }
        };
      }
      return null;
    },
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const waits = [];
  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    term,
    attempts: 4,
    wait: async (ms) => {
      waits.push(ms);
    }
  });

  assert.deepEqual(geometry, { cols: 117, rows: 34 });
  assert.ok(screenReads >= 3);
  assert.ok(waits.length >= 2);
});

test('measureStableTerminalGeometry should wait for stable fitAddon dimensions before falling back', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  let calls = 0;
  const fitAddon = {
    proposeDimensions() {
      calls += 1;
      if (calls < 3) {
        return undefined;
      }
      return { cols: 128, rows: 39 };
    }
  };
  const host = {
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    fitAddon,
    term,
    attempts: 4,
    wait: async () => {}
  });

  assert.deepEqual(geometry, { cols: 128, rows: 39 });
  assert.equal(calls, 4);
});

test('measureStableTerminalGeometry should fall back to DOM measurement when fitAddon stays unavailable', async () => {
  const term = {
    _core: {
      _renderService: {
        dimensions: {
          css: {
            cell: {
              width: 8,
              height: 18
            }
          }
        }
      }
    }
  };
  const fitAddon = {
    proposeDimensions() {
      return undefined;
    }
  };
  const host = {
    getBoundingClientRect() {
      return { width: 960, height: 612 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    fitAddon,
    term,
    attempts: 3,
    wait: async () => {}
  });

  assert.deepEqual(geometry, { cols: 120, rows: 34 });
});

test('normalizeTerminalGeometry should coerce invalid values to zero-based geometry', () => {
  assert.deepEqual(normalizeTerminalGeometry('120', '34'), { cols: 120, rows: 34 });
  assert.deepEqual(normalizeTerminalGeometry(null, undefined), { cols: 0, rows: 0 });
  assert.deepEqual(normalizeTerminalGeometry(-4, 0), { cols: 0, rows: 0 });
});

test('isTerminalGeometryChanged should only allow non-zero geometry transitions', () => {
  assert.equal(isTerminalGeometryChanged(null, { cols: 120, rows: 34 }), true);
  assert.equal(isTerminalGeometryChanged({ cols: 120, rows: 34 }, { cols: 120, rows: 34 }), false);
  assert.equal(isTerminalGeometryChanged({ cols: 120, rows: 34 }, { cols: 121, rows: 34 }), true);
  assert.equal(isTerminalGeometryChanged({ cols: 120, rows: 34 }, { cols: 0, rows: 34 }), false);
});
