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

test('snapshot should discard stale raw that arrives after the baseline', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRenderer(term);

  renderer.onMessage({
    type: 'term.snapshot',
    seq: 10,
    rows: [{ y: 0, segs: [['prompt$ done', 0]] }],
    cursor: { x: 12, y: 0, visible: true }
  });

  renderer.onMessage({
    type: 'term.raw',
    seq: 10,
    replay: false,
    data: 'done\r\n'
  });

  assert.equal(term.resetCalls, 1);
  assert.deepEqual(term.writes, ['prompt$ done\u001b[1;13H\u001b[?25h']);
});

test('raw newer than snapshot should continue rendering', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRenderer(term);

  renderer.onMessage({
    type: 'term.snapshot',
    seq: 10,
    rows: [{ y: 0, segs: [['prompt$ ', 0]] }],
    cursor: { x: 7, y: 0, visible: true }
  });

  renderer.onMessage({
    type: 'term.raw',
    seq: 11,
    replay: false,
    data: 'next\r\n'
  });

  assert.equal(term.resetCalls, 1);
  assert.equal(term.writes[1], 'next\r\n');
});

test('history chunk should rebuild scrollback ahead of the latest snapshot', () => {
  const term = createMockTerm();
  const renderer = createTerminalProtocolRenderer(term);

  renderer.onMessage({
    type: 'term.snapshot',
    seq: 20,
    rows: [
      { y: 0, segs: [['prompt$ one', 0]] },
      { y: 1, segs: [['prompt$ two', 0]] }
    ],
    cursor: { x: 11, y: 1, visible: true }
  });

  renderer.onMessage({
    type: 'term.history.chunk',
    lines: [
      { segs: [['older line 1', 0]] },
      { segs: [['older line 2', 0]] }
    ]
  });

  assert.equal(term.resetCalls, 2);
  assert.equal(
    term.writes[1],
    'older line 1\r\nolder line 2\r\nprompt$ one\r\nprompt$ two\u001b[2;12H\u001b[?25h'
  );
});
