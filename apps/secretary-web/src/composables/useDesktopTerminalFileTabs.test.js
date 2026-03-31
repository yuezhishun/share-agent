import test from 'node:test';
import assert from 'node:assert/strict';
import { ref } from 'vue';
import { useDesktopTerminalFileTabs } from './useDesktopTerminalFileTabs.js';

function createTabsApi({ activeTab = 'terminal', setStatus = () => {} } = {}) {
  const activeCenterTab = ref(activeTab);
  const switchCalls = [];
  const api = useDesktopTerminalFileTabs({
    buildNodeFileApiPath: () => '/api/mock',
    parseErrorMessage: async () => 'error',
    filesStore: {
      async loadList() {},
      async saveFile() {}
    },
    getActiveNodeId: () => 'node-a',
    switchCenterTab: (tabId) => {
      switchCalls.push(tabId);
      activeCenterTab.value = tabId;
    },
    activeCenterTab,
    setStatus
  });

  return {
    ...api,
    activeCenterTab,
    switchCalls
  };
}

test('closeAllFileTabs should remove every file tab and return to terminal', () => {
  const tabsApi = createTabsApi();
  tabsApi.fileTabs.value = [
    { id: 'file:node-a:/tmp/a.txt', dirty: false },
    { id: 'file:node-a:/tmp/b.txt', dirty: false }
  ];
  tabsApi.activeCenterTab.value = 'file:node-a:/tmp/b.txt';

  tabsApi.closeAllFileTabs();

  assert.deepEqual(tabsApi.fileTabs.value, []);
  assert.equal(tabsApi.activeCenterTab.value, 'terminal');
  assert.deepEqual(tabsApi.switchCalls, ['terminal']);
});

test('closeAllFileTabs should keep files open when dirty confirmation is rejected', () => {
  const tabsApi = createTabsApi({ activeTab: 'file:node-a:/tmp/a.txt' });
  tabsApi.fileTabs.value = [
    { id: 'file:node-a:/tmp/a.txt', dirty: true },
    { id: 'file:node-a:/tmp/b.txt', dirty: false }
  ];

  const originalWindow = globalThis.window;
  const confirmMessages = [];
  globalThis.window = {
    confirm(message) {
      confirmMessages.push(String(message));
      return false;
    }
  };

  try {
    tabsApi.closeAllFileTabs();
  } finally {
    globalThis.window = originalWindow;
  }

  assert.equal(tabsApi.fileTabs.value.length, 2);
  assert.equal(tabsApi.activeCenterTab.value, 'file:node-a:/tmp/a.txt');
  assert.deepEqual(confirmMessages, ['当前有 1 个文件未保存，确认全部关闭并丢弃修改？']);
});
