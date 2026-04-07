import test from 'node:test';
import assert from 'node:assert/strict';
import { createPinia, setActivePinia } from 'pinia';
import { useWebCliTerminalStore } from './webcli-terminal.js';

function setupStore() {
  setActivePinia(createPinia());
  return useWebCliTerminalStore();
}

test('resolvePreferredNodeId should keep current node, then prefer online current node, then online master, then first node', () => {
  const store = setupStore();
  store.nodes = [
    { node_id: 'master-offline', node_role: 'master', node_online: false, is_current: false },
    { node_id: 'slave-online', node_role: 'slave', node_online: true, is_current: true },
    { node_id: 'master-online', node_role: 'master', node_online: true, is_current: false }
  ];

  assert.equal(store.resolvePreferredNodeId('slave-online'), 'slave-online');
  assert.equal(store.resolvePreferredNodeId(''), 'slave-online');

  store.nodes = [
    { node_id: 'master-offline', node_role: 'master', node_online: false, is_current: true },
    { node_id: 'slave-online', node_role: 'slave', node_online: true, is_current: false },
    { node_id: 'master-online', node_role: 'master', node_online: true, is_current: false }
  ];

  assert.equal(store.resolvePreferredNodeId(''), 'master-online');

  store.nodes = [
    { node_id: 'master-offline', node_role: 'master', node_online: false, is_current: false },
    { node_id: 'slave-online', node_role: 'slave', node_online: true, is_current: false },
    { node_id: 'master-online', node_role: 'master', node_online: true }
  ];

  assert.equal(store.resolvePreferredNodeId(''), 'master-online');

  store.nodes = [
    { node_id: 'master-offline', node_role: 'master', node_online: false, is_current: false },
    { node_id: 'slave-offline', node_role: 'slave', node_online: false, is_current: false }
  ];

  assert.equal(store.resolvePreferredNodeId(''), 'master-offline');
  assert.equal(store.getDefaultNodeId('missing-node'), 'master-offline');
});

test('sendInput should forward terminal capability responses to the backend', async () => {
  const store = setupStore();
  const sent = [];
  store.connection = {
    invoke(method, payload) {
      sent.push({ method, payload });
      return Promise.resolve();
    }
  };
  store.wsConnected = true;
  store.selectedInstanceId = 'instance-1';

  const autoResponse = '\u001b[1;1R\u001b[?1;2c';
  await store.sendInput(autoResponse, { source: 'terminal' });
  assert.equal(sent.length, 1);
  assert.equal(sent[0].payload.data, autoResponse);
  assert.equal(sent[0].payload.source, 'terminal');

  await store.sendInput('\u001b[A', { source: 'terminal' });
  assert.equal(sent.length, 2);
  assert.equal(sent[1].payload.data, '\u001b[A');
  assert.equal(sent[1].payload.source, 'terminal');

  await store.sendInput('\u001b[200~body\u001b[201~');
  assert.equal(sent.length, 3);
  assert.equal(sent[2].payload.data, '\u001b[200~body\u001b[201~');
  assert.equal(sent[2].payload.source, 'programmatic');
});

test('sendInput should use fire-and-forget transport when requested', async () => {
  const store = setupStore();
  const sent = [];
  store.connection = {
    send(method, payload) {
      sent.push({ method, payload });
      return Promise.resolve();
    }
  };
  store.wsConnected = true;
  store.selectedInstanceId = 'instance-1';

  await store.sendInput('ls\r', { source: 'shortcut', fireAndForget: true });
  assert.equal(sent.length, 1);
  assert.equal(sent[0].method, 'SendInput');
  assert.equal(sent[0].payload.instanceId, 'instance-1');
  assert.equal(sent[0].payload.data, 'ls\r');
  assert.equal(sent[0].payload.source, 'shortcut');
});

test('connect should only join the selected instance and request screen sync', async () => {
  const store = setupStore();
  const calls = [];
  store.ensureConnection = async () => {
    store.connection = {
      invoke(method, payload) {
        calls.push({ method, payload });
        return Promise.resolve();
      }
    };
    store.wsConnected = true;
  };

  await store.connect('inst-1');

  assert.equal(store.selectedInstanceId, 'inst-1');
  assert.deepEqual(calls, [
    { method: 'JoinInstance', payload: { instanceId: 'inst-1' } },
    { method: 'RequestSync', payload: { instanceId: 'inst-1', type: 'screen', reqId: calls[1].payload.reqId } }
  ]);
  assert.match(String(calls[1].payload.reqId || ''), /^screen-sync-/);
  assert.deepEqual(store.joinedInstanceIds, ['inst-1']);
});

test('createInstance should insert returned summary immediately without refreshing global instances', async () => {
  const store = setupStore();
  const calls = [];
  const originalFetch = globalThis.fetch;

  globalThis.fetch = async (url, options = {}) => {
    calls.push(String(url));
    if (String(url).endsWith('/api/nodes/slave-a/instances')) {
      return {
        ok: true,
        async json() {
          return {
            instance_id: 'inst-1',
            node_id: 'slave-a',
            summary: {
              id: 'inst-1',
              command: 'bash',
              cwd: '/workspace',
              cols: 80,
              rows: 24,
              created_at: '2026-04-05T00:00:00.0000000+00:00',
              status: 'running',
              clients: 0,
              node_id: 'slave-a',
              node_name: 'Slave A',
              node_role: 'slave',
              node_online: true
            }
          };
        }
      };
    }

    throw new Error(`unexpected fetch: ${url}`);
  };

  try {
    const created = await store.createInstance({ command: 'bash', args: ['-i'] }, 'slave-a');
    assert.equal(created.instance_id, 'inst-1');
    assert.equal(store.selectedInstanceId, 'inst-1');
    assert.equal(store.instances.length, 1);
    assert.equal(store.instances[0].id, 'inst-1');

    assert.deepEqual(calls, ['/api/nodes/slave-a/instances']);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test('term.instance.missing should clear local state for the missing instance', () => {
  const store = setupStore();
  store.instances = [
    { id: 'inst-missing' },
    { id: 'inst-keep' }
  ];
  store.joinedInstanceIds = ['inst-missing', 'inst-keep'];
  store.selectedInstanceId = 'inst-missing';
  store.streamStates = {
    'inst-missing': {
      syncTimeout: null,
      screenSyncTimeout: null
    },
    'inst-keep': {
      syncTimeout: null,
      screenSyncTimeout: null
    }
  };
  store.resizeAckByInstance = {
    'inst-missing': Date.now(),
    'inst-keep': Date.now()
  };

  const processed = store.processIncomingMessage({
    type: 'term.instance.missing',
    instance_id: 'inst-missing',
    action: 'screen',
    reason: 'not_found'
  });

  assert.equal(processed.length, 1);
  assert.deepEqual(store.instances.map((item) => item.id), ['inst-keep']);
  assert.deepEqual(store.joinedInstanceIds, ['inst-keep']);
  assert.equal(store.selectedInstanceId, 'inst-keep');
  assert.equal(store.streamStates['inst-missing'], undefined);
  assert.equal(store.resizeAckByInstance['inst-missing'], undefined);
  assert.equal(store.status, 'Instance not found: inst-missing');
});
