import test from 'node:test';
import assert from 'node:assert/strict';
import { createPinia, setActivePinia } from 'pinia';
import { useWebCliProcessesStore } from './webcli-processes.js';

function setupStore() {
  setActivePinia(createPinia());
  return useWebCliProcessesStore();
}

function createJsonResponse(payload, ok = true, status = 200) {
  return {
    ok,
    status,
    async json() {
      return payload;
    },
    async text() {
      return JSON.stringify(payload);
    }
  };
}

test('buildRunRequest should split shell-style command line into file and args', () => {
  const store = setupStore();
  store.form.commandLine = 'bash -lc "npm run dev"';
  store.form.cwd = '/tmp/demo';
  store.form.envInput = '{"TERM":"xterm-256color"}';
  store.form.timeoutMs = '';
  store.form.allowNonZeroExitCode = false;

  assert.deepEqual(store.buildRunRequest(), {
    file: 'bash',
    args: ['-lc', 'npm run dev'],
    cwd: '/tmp/demo',
    env: { TERM: 'xterm-256color' },
    stdin: '',
    timeoutMs: null,
    allowNonZeroExitCode: false,
    metadata: {
      source: 'processes-view',
      target_node_id: ''
    }
  });
});

test('store defaults should use 300 second timeout and allow non-zero exit codes', () => {
  const store = setupStore();

  assert.equal(store.form.timeoutMs, '300000');
  assert.equal(store.form.allowNonZeroExitCode, true);
});

test('buildRunRequest should keep JSON array command line compatibility', () => {
  const store = setupStore();
  store.form.commandLine = '["python3","-c","print(1)"]';

  const payload = store.buildRunRequest();
  assert.equal(payload.file, 'python3');
  assert.deepEqual(payload.args, ['-c', 'print(1)']);
});

test('buildRunRequest should reject malformed command line', () => {
  const store = setupStore();
  store.form.commandLine = '"unterminated';

  assert.throws(() => store.buildRunRequest(), /未闭合/);
});

test('resolvePreferredNodeId should prefer current node, online master, then first online', () => {
  const store = setupStore();
  store.nodes = [
    { node_id: 'slave-a', node_role: 'slave', node_online: true },
    { node_id: 'master-a', node_role: 'master', node_online: true }
  ];

  assert.equal(store.resolvePreferredNodeId('slave-a'), 'slave-a');
  assert.equal(store.resolvePreferredNodeId(''), 'master-a');

  store.nodes = [
    { node_id: 'slave-offline', node_role: 'slave', node_online: false },
    { node_id: 'slave-online', node_role: 'slave', node_online: true }
  ];

  assert.equal(store.resolvePreferredNodeId(''), 'slave-online');
});

test('removeProcess should delete a non-selected item without changing the selection', async () => {
  const store = setupStore();
  store.nodes = [{ node_id: 'node-a', node_online: true }];
  store.selectedNodeId = 'node-a';
  store.items = [
    { processId: 'proc-1', status: 'completed' },
    { processId: 'proc-2', status: 'running' }
  ];
  store.selectedProcessId = 'proc-1';
  store.selectedProcess = { processId: 'proc-1', status: 'completed' };
  store.outputItems = [{ processId: 'proc-1', content: 'alpha' }];

  const requests = [];
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (url, options = {}) => {
    requests.push({ url: String(url), method: options.method || 'GET' });
    return createJsonResponse({ ok: true, processId: 'proc-2' });
  };

  try {
    await store.removeProcess('proc-2');
  } finally {
    globalThis.fetch = originalFetch;
  }

  assert.equal(requests.length, 1);
  assert.equal(requests[0].method, 'DELETE');
  assert.match(requests[0].url, /\/api\/nodes\/node-a\/processes\/proc-2$/);
  assert.deepEqual(store.items.map((item) => item.processId), ['proc-1']);
  assert.equal(store.selectedProcessId, 'proc-1');
  assert.equal(store.selectedProcess?.processId, 'proc-1');
  assert.equal(store.outputItems.length, 1);
});

test('removeProcess should clear the deleted selected item and surface fetch errors', async () => {
  const store = setupStore();
  store.nodes = [{ node_id: 'node-a', node_online: true }];
  store.selectedNodeId = 'node-a';
  store.items = [
    { processId: 'proc-1', status: 'completed' },
    { processId: 'proc-2', status: 'completed' }
  ];
  store.selectedProcessId = 'proc-1';
  store.selectedProcess = { processId: 'proc-1', status: 'completed' };
  store.outputItems = [{ processId: 'proc-1', content: 'alpha' }];

  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (url, options = {}) => {
    if ((options.method || 'GET') === 'DELETE') {
      return createJsonResponse({ ok: true, processId: 'proc-1' });
    }
    if (String(url).endsWith('/proc-2')) {
      return createJsonResponse({ processId: 'proc-2', status: 'completed', command: 'echo 2' });
    }
    if (String(url).endsWith('/proc-2/output')) {
      return createJsonResponse({ items: [{ processId: 'proc-2', content: 'beta', outputType: 'standardoutput' }] });
    }
    return createJsonResponse({ error: 'boom' }, false, 500);
  };

  try {
    await store.removeProcess('proc-1');
    assert.equal(store.selectedProcessId, 'proc-2');
    assert.equal(store.selectedProcess?.processId, 'proc-2');
    assert.equal(store.outputItems[0]?.content, 'beta');

    globalThis.fetch = async () => createJsonResponse({ error: 'boom' }, false, 500);
    await assert.rejects(() => store.removeProcess('proc-2'));
    assert.equal(store.error, 'boom');
  } finally {
    globalThis.fetch = originalFetch;
  }
});
