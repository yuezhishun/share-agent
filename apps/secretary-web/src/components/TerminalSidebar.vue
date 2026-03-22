<template>
  <div>
    <div class="sidebar-section node-target">
      <div class="section-header">
        <span><i class="fa-solid fa-network-wired" /> 目标节点</span>
        <div class="section-header-actions">
          <span class="node-state" :class="{ offline: isSelectedNodeOnline === false }">
            {{ isSelectedNodeOnline ? 'online' : 'offline' }}
          </span>
          <span id="refreshNodeIcon" class="add-icon" title="刷新节点状态" @click="emit('refresh-nodes')"><i class="fa-solid fa-rotate" /></span>
        </div>
      </div>
      <div class="node-target-body">
        <select class="node-select" :value="createNodeId" @change="emit('target-node-change', $event.target.value)">
          <option v-for="node in nodes" :key="node.node_id" :value="node.node_id">
            {{ formatNodeOption(node) }}
          </option>
        </select>
        <div class="node-target-meta">
          <span>{{ selectedNode?.node_name || '未选择节点' }}</span>
          <span>{{ visibleTerminalInstances.length }} instances</span>
        </div>
      </div>
    </div>

    <div class="sidebar-section sessions">
      <div class="section-header">
        <span><i class="fa-regular fa-window-maximize" /> 终端会话</span>
        <div class="section-header-actions">
          <span
            id="addTerminalIcon"
            class="add-icon"
            :title="createTerminalTitle"
            data-testid="create-button"
            @click="emit('create-instance')"
          ><i class="fa-solid fa-plus" /></span>
          <span id="refreshTerminalIcon" class="add-icon" title="刷新列表" @click="emit('refresh-terminals')"><i class="fa-solid fa-rotate" /></span>
        </div>
      </div>
      <ul id="instance-list" class="terminal-list" data-testid="instance-list">
        <li
          v-for="item in visibleTerminalInstances"
          :key="item.id"
          class="terminal-item"
          :class="{ active: item.id === selectedInstanceId }"
          @click="emit('connect', item.id)"
        >
          <i class="fa-regular fa-terminal" />
          <input
            v-if="renamingInstanceId === item.id"
            :ref="setRenameInstanceInputRef"
            :value="renameInstanceValue"
            class="terminal-rename-input"
            :data-testid="`rename-instance-input-${item.id}`"
            maxlength="60"
            placeholder="输入会话名称"
            @input="emit('update:renameInstanceValue', $event.target.value)"
            @click.stop
            @blur="emit('save-rename-instance', item.id)"
            @keydown.enter.prevent.stop="emit('save-rename-instance', item.id)"
            @keydown.esc.prevent.stop="emit('cancel-rename-instance')"
          />
          <span
            v-else
            class="terminal-name"
            :title="formatInstanceTooltip(item)"
          >{{ formatInstanceDisplayName(item) }}</span>
          <span v-if="item.node_online === false" class="instance-state offline">offline</span>
          <span class="terminal-actions">
            <span
              class="sync-btn"
              title="同步终端内容"
              @click.stop="emit('sync-terminal-item', item.id)"
            ><i class="fa-solid fa-rotate" /></span>
            <span
              class="rename-btn"
              title="查看纯文本"
              @click.stop="emit('view-plain-text-item', item.id)"
            ><i class="fa-regular fa-file-lines" /></span>
            <span
              class="rename-btn"
              :title="getInstanceAlias(item.id) ? '修改会话名' : '设置会话名'"
              :data-testid="`rename-instance-${item.id}`"
              @click.stop="emit('begin-rename-instance', item)"
            ><i class="fa-regular fa-pen-to-square" /></span>
            <span class="close-btn" title="关闭" @click.stop="emit('close-terminal', item.id)"><i class="fa-regular fa-circle-xmark" /></span>
          </span>
        </li>
      </ul>
    </div>
  </div>
</template>

<script setup>
defineProps({
  nodes: { type: Array, default: () => [] },
  createNodeId: { type: String, default: '' },
  selectedNode: { type: Object, default: null },
  isSelectedNodeOnline: { type: Boolean, default: true },
  visibleTerminalInstances: { type: Array, default: () => [] },
  selectedInstanceId: { type: String, default: '' },
  renamingInstanceId: { type: String, default: '' },
  renameInstanceValue: { type: String, default: '' },
  createTerminalTitle: { type: String, default: '' },
  formatNodeOption: { type: Function, required: true },
  formatInstanceTooltip: { type: Function, required: true },
  formatInstanceDisplayName: { type: Function, required: true },
  getInstanceAlias: { type: Function, required: true },
  setRenameInstanceInputRef: { type: Function, required: true }
});

const emit = defineEmits([
  'target-node-change',
  'refresh-nodes',
  'create-instance',
  'refresh-terminals',
  'connect',
  'update:renameInstanceValue',
  'save-rename-instance',
  'cancel-rename-instance',
  'sync-terminal-item',
  'view-plain-text-item',
  'begin-rename-instance',
  'close-terminal'
]);
</script>

<style scoped>
.sidebar-section {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-bottom: 1px solid #3c3c3c;
}

.sidebar-section:last-child {
  border-bottom: none;
}

.node-target {
  flex: 0 0 auto;
}

.sessions {
  flex: 2;
  min-height: 0;
}

.section-header {
  padding: 10px 16px 6px;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #9e9e9e;
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #2a2a2a;
}

.section-header span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.section-header-actions {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.node-state {
  font-size: 0.7rem;
  color: #89d185;
}

.node-state.offline {
  color: #ef6a62;
}

.add-icon {
  position: relative;
  cursor: pointer;
  color: #b4b4b4;
  padding: 4px;
  border-radius: 4px;
  transition: background 0.1s;
}

.add-icon:hover {
  background-color: #3a3a3a;
  color: #fff;
}

.add-icon.active {
  background-color: rgba(14, 99, 156, 0.22);
  color: #9cdcfe;
}

.node-target-body {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
}

.node-select {
  width: 100%;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 8px 10px;
  font-size: 0.8rem;
}

.node-target-meta {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  color: #9eb8d4;
  font-size: 0.74rem;
}

.terminal-size-editor {
  padding: 10px;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  border-bottom: 1px solid #3c3c3c;
  background: linear-gradient(180deg, #1a2430, #141b24);
}

.terminal-size-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  color: #9eb8d4;
  font-size: 0.74rem;
}

.terminal-size-field input {
  width: 100%;
  background: #0f151d;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 8px 10px;
  font-size: 0.82rem;
}

.terminal-size-actions {
  grid-column: 1 / -1;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.terminal-size-actions button {
  width: auto;
  background-color: #3c3c3c;
  border: 1px solid transparent;
  color: #e0e0e0;
  font-size: 0.86rem;
  padding: 6px 12px;
  border-radius: 4px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  transition: background 0.15s;
}

.terminal-size-actions button:hover {
  background-color: #4a4a4a;
}

.terminal-size-actions button.primary {
  background-color: #0e639c;
}

.terminal-size-actions button.primary:hover {
  background-color: #1177bb;
}

.terminal-list {
  list-style: none;
  padding: 4px 0;
  overflow-y: auto;
}

.terminal-item {
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr) auto;
  align-items: center;
  padding: 7px 12px;
  padding-right: 12px;
  margin: 2px 8px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.84rem;
  gap: 8px;
  min-height: 36px;
  box-sizing: border-box;
  position: relative;
  transition: background-color 0.12s ease, padding-right 0.12s ease;
}

.terminal-item:hover {
  background-color: #2a2d2e;
}

.terminal-item:hover,
.terminal-item:focus-within {
  padding-right: 88px;
}

.terminal-item.active {
  background-color: #37373d;
}

.terminal-item i {
  color: #6c9cbe;
  width: 18px;
}

.terminal-name {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  font-family: 'JetBrains Mono', monospace;
}

.terminal-rename-input {
  width: 100%;
  min-width: 0;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 4px 6px;
  font-size: 0.8rem;
}

.instance-state {
  font-size: 0.72rem;
  color: #97c6f2;
}

.instance-state.offline {
  color: #ef6a62;
}

.terminal-actions {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  justify-content: flex-end;
  width: 76px;
  position: absolute;
  right: 12px;
  top: 50%;
  transform: translateY(-50%);
  visibility: hidden;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.12s ease;
}

.sync-btn,
.rename-btn,
.close-btn {
  color: #9e9e9e;
  border-radius: 4px;
  padding: 2px 4px;
  font-size: 0.8rem;
}

.terminal-item:hover .terminal-actions,
.terminal-item:focus-within .terminal-actions {
  visibility: visible;
  opacity: 1;
  pointer-events: auto;
}

.sync-btn:hover,
.rename-btn:hover,
.close-btn:hover {
  background-color: #4a4a4a;
  color: #fff;
}

@media (max-width: 820px) {
  .terminal-size-editor {
    grid-template-columns: 1fr;
  }
}
</style>
