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
  const samples = [
    { cols: 120, rows: 34 },
    { cols: 120, rows: 34 }
  ];
  const term = {
    get cols() {
      return samples[0]?.cols ?? 0;
    },
    get rows() {
      const current = samples.shift();
      return current?.rows ?? 0;
    }
  };
  const host = {
    getBoundingClientRect() {
      return { width: 960, height: 640 };
    }
  };

  const geometry = await measureStableTerminalGeometry({
    activeCenterTab: 'terminal',
    hostElement: host,
    term,
    fitAddon: { fit() {} },
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
