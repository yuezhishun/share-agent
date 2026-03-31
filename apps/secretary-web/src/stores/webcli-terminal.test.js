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
