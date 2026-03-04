import test from 'node:test';
import assert from 'node:assert/strict';
import { createTerminalProtocolRenderer } from './useTerminalProtocol.js';

function createMockTerm() {
  return {
    resetCalls: 0,
    writes: [],
    reset() {
      this.resetCalls += 1;
    },
    write(text, callback) {
      this.writes.push(String(text || ''));
      callback?.();
    }
  };
}

test('renderSnapshot should restore cursor position and visibility after drawing text', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRenderer(term);

  renderer.onMessage({
    type: 'term.snapshot',
    rows: [
      { y: 0, segs: [['hello', 0]] },
      { y: 1, segs: [['world', 0]] }
    ],
    cursor: { x: 1, y: 0, visible: true }
  });

  assert.equal(term.resetCalls, 1);
  assert.deepEqual(term.writes, ['hello\r\nworld\u001b[1;2H\u001b[?25h']);
});

test('renderSnapshot should apply cursor control even when rows are empty', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRenderer(term);

  renderer.onMessage({
    type: 'term.snapshot',
    rows: [],
    cursor: { x: 3, y: 4, visible: false }
  });

  assert.equal(term.resetCalls, 1);
  assert.deepEqual(term.writes, ['\u001b[5;4H\u001b[?25l']);
});
