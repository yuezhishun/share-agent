import test from 'node:test';
import assert from 'node:assert/strict';
import { isTerminalViewportRenderable } from './desktop-terminal-resize.js';

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
