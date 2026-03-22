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
