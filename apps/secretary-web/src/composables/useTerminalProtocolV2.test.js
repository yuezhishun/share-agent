import test from 'node:test';
import assert from 'node:assert/strict';
import { createTerminalProtocolRendererV2 } from './useTerminalProtocolV2.js';

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

test('v2 snapshot should discard stale raw that arrives after the baseline', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRendererV2(term);

  renderer.onMessage({
    type: 'term.v2.snapshot',
    seq: 10,
    rows: [{ y: 0, segs: [['prompt$ done', 0]] }],
    cursor: { x: 12, y: 0, visible: true }
  });

  renderer.onMessage({
    type: 'term.v2.raw',
    seq: 10,
    replay: false,
    data: 'done\r\n'
  });

  assert.equal(term.resetCalls, 1);
  assert.deepEqual(term.writes, ['prompt$ done\u001b[1;13H\u001b[?25h']);
});

test('v2 raw newer than snapshot should continue rendering', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRendererV2(term);

  renderer.onMessage({
    type: 'term.v2.snapshot',
    seq: 10,
    rows: [{ y: 0, segs: [['prompt$ ', 0]] }],
    cursor: { x: 7, y: 0, visible: true }
  });

  renderer.onMessage({
    type: 'term.v2.raw',
    seq: 11,
    replay: false,
    data: 'next\r\n'
  });

  assert.equal(term.resetCalls, 1);
  assert.equal(term.writes[1], 'next\r\n');
});
