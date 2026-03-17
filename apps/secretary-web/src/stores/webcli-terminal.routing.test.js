import test from 'node:test';
import assert from 'node:assert/strict';
import { createPinia, setActivePinia } from 'pinia';
import {
  containsTerminalQueryProbe,
  looksLikeTerminalAutoResponse,
  useWebCliTerminalStore
} from './webcli-terminal.js';

function setupStore() {
  setActivePinia(createPinia());
  const store = useWebCliTerminalStore();
  const invokes = [];
  store.connection = {
    invoke(method, payload) {
      invokes.push({ method, payload });
      return Promise.resolve();
    }
  };
  store.wsConnected = true;
  store.instances = [{ id: 'codex-1' }, { id: 'bash-1' }];
  return { store, invokes };
}

test('detect terminal query probe and auto response payloads', () => {
  assert.equal(containsTerminalQueryProbe('\u001b[6n'), true);
  assert.equal(containsTerminalQueryProbe('\u001b[c'), true);
  assert.equal(containsTerminalQueryProbe('\u001b[0c'), true);
  assert.equal(containsTerminalQueryProbe('\u001b[>0c'), true);
  assert.equal(containsTerminalQueryProbe('\u001b]10;?\u0007'), true);
  assert.equal(containsTerminalQueryProbe('ls -la\r'), false);

  assert.equal(looksLikeTerminalAutoResponse('\u001b[1;1R'), true);
  assert.equal(looksLikeTerminalAutoResponse('\u001b[?1;2c'), true);
  assert.equal(looksLikeTerminalAutoResponse('\u001b]10;rgb:d4d4/d4d4/d4d4\u0007'), true);
  assert.equal(looksLikeTerminalAutoResponse('\u001b[A'), false);
  assert.equal(looksLikeTerminalAutoResponse('pwd\r'), false);
});

test('sendInput should route terminal auto response back to probe instance', async () => {
  const { store, invokes } = setupStore();

  store.selectedInstanceId = 'codex-1';
  store.emitMessage({
    type: 'term.raw',
    instance_id: 'codex-1',
    data: '\u001b[6n\u001b[c\u001b]10;?\u0007'
  });

  store.selectedInstanceId = 'bash-1';
  await store.sendInput('\u001b[1;1R');

  assert.equal(invokes.length, 1);
  assert.equal(invokes[0].method, 'SendInput');
  assert.equal(invokes[0].payload.instanceId, 'codex-1');
  assert.equal(invokes[0].payload.data, '\u001b[1;1R');
});

test('sendInput should route terminal auto response for parameterized DA probes', async () => {
  const { store, invokes } = setupStore();

  store.selectedInstanceId = 'codex-1';
  store.emitMessage({
    type: 'term.raw',
    instance_id: 'codex-1',
    data: '\u001b[0c\u001b[>0c'
  });

  store.selectedInstanceId = 'bash-1';
  await store.sendInput('\u001b[?1;2c');

  assert.equal(invokes.length, 1);
  assert.equal(invokes[0].method, 'SendInput');
  assert.equal(invokes[0].payload.instanceId, 'codex-1');
  assert.equal(invokes[0].payload.data, '\u001b[?1;2c');
});

test('sendInput should keep regular user input on selected instance', async () => {
  const { store, invokes } = setupStore();

  store.selectedInstanceId = 'codex-1';
  store.emitMessage({
    type: 'term.raw',
    instance_id: 'codex-1',
    data: '\u001b[6n'
  });

  store.selectedInstanceId = 'bash-1';
  await store.sendInput('ls\r');

  assert.equal(invokes.length, 1);
  assert.equal(invokes[0].payload.instanceId, 'bash-1');
  assert.equal(invokes[0].payload.data, 'ls\r');
});

test('processIncomingMessage should not suppress first snapshot right after resize ack', () => {
  const { store } = setupStore();
  store.selectedInstanceId = 'bash-1';
  store.resizeAckByInstance['bash-1'] = Date.now();
  store.resetStreamState('bash-1', 0);

  const emitted = store.processIncomingMessage({
    type: 'term.snapshot',
    instance_id: 'bash-1',
    seq: 1,
    base_seq: 1,
    cursor: { x: 0, y: 0, visible: true },
    rows: [{ y: 0, segs: [['prompt$', 0]] }]
  });

  assert.equal(emitted.length, 1);
  assert.equal(emitted[0].type, 'term.snapshot');
});

test('connect should prioritize selected instance even if background sync fails', async () => {
  const { store } = setupStore();
  const invocations = [];
  let syncAttempted = 0;

  store.ensureConnection = async () => {};
  store.joinInstance = async (instanceId) => {
    invocations.push({ type: 'join', instanceId });
    if (!store.joinedInstanceIds.includes(instanceId)) {
      store.joinedInstanceIds.push(instanceId);
    }
    return true;
  };
  store.syncJoinedInstances = async () => {
    syncAttempted += 1;
    throw new Error('background sync failed');
  };
  store.requestRawSync = async (instanceId, reason, options) => {
    invocations.push({ type: 'sync', instanceId, reason, options });
  };
  store.waitForSnapshot = async () => null;
  store.wsConnected = true;

  await store.connect('bash-1');

  assert.equal(store.selectedInstanceId, 'bash-1');
  assert.equal(store.status, 'Connected');
  assert.equal(invocations.some((x) => x.type === 'join' && x.instanceId === 'bash-1'), true);
  assert.equal(invocations.some((x) => x.type === 'sync' && x.instanceId === 'bash-1'), true);
  assert.equal(syncAttempted > 0, true);
});
