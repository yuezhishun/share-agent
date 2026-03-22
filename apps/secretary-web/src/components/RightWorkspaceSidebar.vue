<template>
  <div class="sidebar-section file-browser">
    <div class="section-header">
      <span>
        <i :class="rightTabIconClass" />
        {{ rightTabTitle }}
      </span>
      <div v-if="activeRightTab === 'files'" class="section-header-actions">
        <span
          id="refreshFolderIcon"
          class="add-icon"
          title="刷新文件夹"
          data-testid="refresh-folder-trigger"
          @click="emit('refresh-files-list')"
        >
          <i class="fa-solid fa-rotate" />
        </span>
        <span
          id="toggleHiddenFilesIcon"
          class="add-icon"
          :class="{ active: filesStore.showHidden }"
          :title="filesStore.showHidden ? '隐藏隐藏文件' : '显示隐藏文件'"
          :aria-pressed="filesStore.showHidden"
          data-testid="toggle-hidden-files"
          @click="emit('toggle-show-hidden')"
        >
          <i :class="filesStore.showHidden ? 'fa-regular fa-eye-slash' : 'fa-regular fa-eye'" />
        </span>
        <span
          id="createFolderIcon"
          class="add-icon"
          :class="{ active: showFolderCreator }"
          :title="showFolderCreator ? '收起新建文件夹' : '新建文件夹'"
          data-testid="create-folder-toggle"
          @click="emit('toggle-folder-creator')"
        >
          <i class="fa-regular fa-folder-open" />
          <i class="fa-solid fa-plus overlay-plus-icon" />
        </span>
        <span
          id="uploadFileIcon"
          class="add-icon"
          title="上传文件"
          data-testid="upload-file-trigger"
          @click="emit('pick-upload-files')"
        >
          <i class="fa-solid fa-upload" />
        </span>
      </div>
      <div v-else-if="activeRightTab === 'recipes'" class="section-header-actions">
        <span
          class="add-icon"
          :class="{ active: showTerminalSizeEditor }"
          title="设置伪终端尺寸"
          data-testid="terminal-size-toggle"
          @click="emit('toggle-terminal-size-editor')"
        ><i class="fa-solid fa-ruler-combined" /></span>
        <span
          id="addRecipeIcon"
          class="add-icon"
          :title="showRecipeEditor ? '收起配方编辑器' : '新建配方'"
          @click="showRecipeEditor ? emit('cancel-recipe-edit') : emit('add-new-recipe')"
        >
          <i :class="showRecipeEditor ? 'fa-solid fa-minus' : 'fa-solid fa-plus'" />
        </span>
      </div>
      <span
        v-else-if="activeRightTab === 'shortcuts'"
        id="addShortcutIcon"
        class="add-icon"
        :title="showShortcutEditor ? '收起快捷指令' : '新建快捷指令'"
        @click="emit('toggle-shortcut-editor')"
      >
        <i :class="showShortcutEditor ? 'fa-solid fa-minus' : 'fa-solid fa-plus'" />
      </span>
    </div>

    <div v-if="activeRightTab === 'files'" class="file-browser-panel">
      <div class="file-header">
        <span id="currentPath" class="path-breadcrumb" :title="filesStore.currentPath">{{ currentPathDisplay }}</span>
      </div>
      <form
        v-if="showFolderCreator"
        class="folder-creator"
        data-testid="folder-creator"
        @submit.prevent="emit('create-folder')"
      >
        <input
          :value="folderName"
          class="folder-creator-input"
          data-testid="folder-name-input"
          type="text"
          maxlength="120"
          placeholder="输入文件夹名称"
          @input="emit('update:folderName', $event.target.value)"
        />
        <div class="folder-creator-actions">
          <button type="submit" class="primary" :disabled="filesStore.actionLoading">创建</button>
          <button type="button" :disabled="filesStore.actionLoading" @click="emit('toggle-folder-creator')">取消</button>
        </div>
      </form>
      <ul id="fileList" class="file-list">
        <li v-if="filesStore.parentPath" class="file-item" @click="emit('go-parent-dir')">
          <i class="fa-regular fa-folder-open folder-icon" />
          <div class="file-info">
            <span class="file-name">..</span>
          </div>
        </li>
        <li
          v-for="item in filesStore.items"
          :key="item.path"
          class="file-item"
          :title="formatFileEntryTooltip(item)"
          @click="emit('open-file-entry', item)"
        >
          <i :class="item.kind === 'dir' ? 'fa-regular fa-folder-open folder-icon' : 'fa-regular fa-file file-icon'" />
          <div class="file-info">
            <span class="file-name">{{ item.name }}</span>
          </div>
          <span v-if="item.kind === 'file' || item.kind === 'dir'" class="file-actions">
            <button
              type="button"
              class="file-action-btn"
              :title="item.kind === 'dir' ? '下载压缩包' : '下载文件'"
              @click.stop="emit('download-file-entry', item)"
            >
              <i class="fa-solid fa-download" />
            </button>
          </span>
        </li>
      </ul>
      <div v-if="filesStore.error" class="panel-error">{{ filesStore.error }}</div>
      <div v-if="filesStore.actionError" class="panel-error">{{ filesStore.actionError }}</div>
    </div>

    <div v-else-if="activeRightTab === 'shortcuts'" class="shortcut-panel">
      <form
        v-if="showShortcutEditor"
        class="shortcut-editor"
        data-testid="shortcut-editor"
        @submit.prevent="emit('add-shortcut-command')"
      >
        <div class="shortcut-editor-field">
          <span class="shortcut-editor-label">按钮名</span>
          <input
            :ref="shortcutLabelInputRef"
            :value="shortcutEditor.label"
            type="text"
            placeholder="如 tail日志"
            @input="emit('update:shortcutEditor', { ...shortcutEditor, label: $event.target.value })"
          />
        </div>
        <div class="shortcut-editor-field">
          <span class="shortcut-editor-label">分组</span>
          <input
            :value="shortcutEditor.group"
            type="text"
            placeholder="如 custom"
            @input="emit('update:shortcutEditor', { ...shortcutEditor, group: $event.target.value })"
          />
        </div>
        <div class="shortcut-editor-field shortcut-editor-field-wide">
          <span class="shortcut-editor-label">指令内容</span>
          <input
            :value="shortcutEditor.command"
            type="text"
            placeholder="如 tail -f app.log"
            @input="emit('update:shortcutEditor', { ...shortcutEditor, command: $event.target.value })"
          />
        </div>
        <div class="shortcut-editor-actions">
          <button
            type="button"
            class="shortcut-enter-toggle"
            :class="{ active: shortcutEditor.pressEnter }"
            :title="shortcutEditor.pressEnter ? '发送后自动回车：已开启' : '发送后自动回车：已关闭'"
            :aria-pressed="shortcutEditor.pressEnter"
            @click="emit('update:shortcutEditor', { ...shortcutEditor, pressEnter: !shortcutEditor.pressEnter })"
          >
            <i class="fa-solid fa-reply shortcut-enter-icon" aria-hidden="true" />
            <span>回车</span>
          </button>
          <button type="submit" class="primary">添加指令</button>
          <button type="button" @click="emit('collapse-shortcut-editor')">取消</button>
        </div>
      </form>
      <div class="shortcut-note">按分组发送控制键或命令，常用新增项收在右上角 `+`。</div>
      <div class="shortcut-groups">
        <div v-for="group in shortcutGroups" :key="group.group" class="shortcut-group">
          <div class="shortcut-group-title">{{ group.group }}</div>
          <div class="shortcut-grid">
            <button
              v-for="item in group.items"
              :key="item.id"
              type="button"
              class="shortcut-btn"
              @click="emit('send-shortcut', item)"
            >
              {{ item.label }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <div v-else class="recipes-panel">
      <div v-if="showTerminalSizeEditor" class="terminal-size-editor">
        <label class="terminal-size-field">
          <span>宽 cols</span>
          <input
            :value="terminalSizeDraftCols"
            data-testid="terminal-size-cols"
            type="number"
            min="1"
            step="1"
            inputmode="numeric"
            @input="emit('update:terminalSizeDraftCols', $event.target.value)"
          />
        </label>
        <label class="terminal-size-field">
          <span>高 rows</span>
          <input
            :value="terminalSizeDraftRows"
            data-testid="terminal-size-rows"
            type="number"
            min="1"
            step="1"
            inputmode="numeric"
            @input="emit('update:terminalSizeDraftRows', $event.target.value)"
          />
        </label>
        <div class="terminal-size-actions">
          <button type="button" class="primary" @click="emit('apply-terminal-size')">确定</button>
          <button type="button" @click="emit('cancel-terminal-size-editor')">取消</button>
        </div>
      </div>
      <form v-if="showRecipeEditor" class="recipe-editor" @submit.prevent="emit('submit-recipe-editor')">
        <input
          :value="recipeEditor.name"
          type="text"
          placeholder="显示名（可选）"
          @input="emit('update:recipeEditor', { ...recipeEditor, name: $event.target.value })"
        />
        <select
          :value="recipeEditor.cwd"
          :disabled="recipeFoldersLoading || recipeFolderOptions.length === 0"
          @change="emit('update:recipeEditor', { ...recipeEditor, cwd: $event.target.value })"
        >
          <option v-for="item in recipeFolderOptions" :key="item.path" :value="item.path">
            {{ item.label }}
          </option>
        </select>
        <textarea
          :value="recipeEditor.commandLine"
          rows="3"
          placeholder='命令行，如 bash -lc "npm run dev" 或 ["bash","-lc","npm run dev"]'
          required
          @input="emit('update:recipeEditor', { ...recipeEditor, commandLine: $event.target.value })"
        />
        <div v-if="recipeFoldersError" class="panel-error">{{ recipeFoldersError }}</div>
        <textarea
          :value="recipeEditor.envInput"
          rows="3"
          placeholder='环境变量(JSON对象)，如 {"TERM":"xterm-256color"}'
          @input="emit('update:recipeEditor', { ...recipeEditor, envInput: $event.target.value })"
        />
        <div class="recipe-editor-actions">
          <button type="submit" class="primary" :disabled="recipeFoldersLoading || recipeFolderOptions.length === 0">{{ editingRecipeId ? '更新配方' : '保存配方' }}</button>
          <button type="button" @click="emit('cancel-recipe-edit')">取消</button>
        </div>
      </form>

      <ul id="recipeList" class="recipe-list">
        <li v-for="item in recipeItems" :key="item.id" class="recipe-item" :class="{ default: isDefaultCreateRecipe(item.id) }">
          <span class="recipe-icon"><i class="fa-regular fa-file-lines" /></span>
          <div class="recipe-info">
            <div class="recipe-name">{{ item.name || item.command }}</div>
            <div class="recipe-command" :title="formatRecipeSummary(item)">{{ formatRecipeSummary(item) }}</div>
          </div>
          <div class="recipe-meta">
            <button
              type="button"
              class="recipe-star-btn"
              :class="{ active: isDefaultCreateRecipe(item.id) }"
              :title="isDefaultCreateRecipe(item.id) ? '取消 + 号默认配方' : '设为 + 号默认配方'"
              @click="emit('toggle-default-create-recipe', item)"
            >
              <i :class="isDefaultCreateRecipe(item.id) ? 'fa-solid fa-star' : 'fa-regular fa-star'" />
            </button>
            <div class="recipe-actions">
              <button type="button" class="recipe-action-btn" title="执行配方" @click="emit('run-recipe', item)">
                <i class="fa-regular fa-play" />
              </button>
              <button type="button" class="recipe-action-btn" title="编辑配方" @click="emit('edit-recipe', item)">
                <i class="fa-regular fa-pen-to-square" />
              </button>
              <button type="button" class="recipe-action-btn danger" title="删除配方" @click="emit('remove-recipe', item.id)">
                <i class="fa-regular fa-trash-can" />
              </button>
            </div>
          </div>
        </li>
      </ul>
    </div>

    <div class="right-tab-footer">
      <button
        type="button"
        class="right-tab-btn"
        :class="{ active: activeRightTab === 'files' }"
        @click="emit('switch-right-tab', 'files')"
      >
        文件浏览器
      </button>
      <button
        type="button"
        class="right-tab-btn"
        :class="{ active: activeRightTab === 'shortcuts' }"
        @click="emit('switch-right-tab', 'shortcuts')"
      >
        快捷指令
      </button>
      <button
        type="button"
        class="right-tab-btn"
        :class="{ active: activeRightTab === 'recipes' }"
        @click="emit('switch-right-tab', 'recipes')"
      >
        终端配方
      </button>
    </div>
  </div>
</template>

<script setup>
defineProps({
  filesStore: { type: Object, required: true },
  activeRightTab: { type: String, default: 'files' },
  rightTabIconClass: { type: String, default: '' },
  rightTabTitle: { type: String, default: '' },
  showFolderCreator: { type: Boolean, default: false },
  currentPathDisplay: { type: String, default: '' },
  folderName: { type: String, default: '' },
  formatFileEntryTooltip: { type: Function, required: true },
  formatSize: { type: Function, required: true },
  showShortcutEditor: { type: Boolean, default: false },
  shortcutEditor: { type: Object, required: true },
  shortcutGroups: { type: Array, default: () => [] },
  showTerminalSizeEditor: { type: Boolean, default: false },
  terminalSizeDraftCols: { type: String, default: '' },
  terminalSizeDraftRows: { type: String, default: '' },
  showRecipeEditor: { type: Boolean, default: false },
  recipeEditor: { type: Object, required: true },
  recipeFoldersLoading: { type: Boolean, default: false },
  recipeFolderOptions: { type: Array, default: () => [] },
  recipeFoldersError: { type: String, default: '' },
  editingRecipeId: { type: String, default: '' },
  recipeItems: { type: Array, default: () => [] },
  isDefaultCreateRecipe: { type: Function, required: true },
  formatRecipeSummary: { type: Function, required: true },
  shortcutLabelInputRef: { type: [Function, Object], required: true }
});

const emit = defineEmits([
  'toggle-show-hidden',
  'refresh-files-list',
  'toggle-folder-creator',
  'pick-upload-files',
  'toggle-shortcut-editor',
  'toggle-terminal-size-editor',
  'update:terminalSizeDraftCols',
  'update:terminalSizeDraftRows',
  'apply-terminal-size',
  'cancel-terminal-size-editor',
  'cancel-recipe-edit',
  'add-new-recipe',
  'create-folder',
  'update:folderName',
  'go-parent-dir',
  'open-file-entry',
  'download-file-entry',
  'update:shortcutEditor',
  'add-shortcut-command',
  'collapse-shortcut-editor',
  'send-shortcut',
  'update:recipeEditor',
  'submit-recipe-editor',
  'toggle-default-create-recipe',
  'run-recipe',
  'edit-recipe',
  'remove-recipe',
  'switch-right-tab'
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

.file-browser {
  flex: 1;
  min-height: 0;
  background-color: #1e1e1e;
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

.overlay-plus-icon {
  position: absolute;
  right: 1px;
  bottom: 1px;
  font-size: 0.52rem;
}

.file-browser-panel {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.file-header {
  padding: 8px 12px;
  background-color: #2d2d2d;
  border-bottom: 1px solid #3c3c3c;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.path-breadcrumb {
  background-color: #3c3c3c;
  padding: 4px 10px;
  border-radius: 16px;
  font-size: 0.8rem;
  font-family: 'JetBrains Mono', monospace;
  color: #9cdcfe;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.folder-creator {
  padding: 10px 12px;
  border-bottom: 1px solid #3c3c3c;
  display: flex;
  align-items: center;
  gap: 10px;
  background: #232a33;
}

.folder-creator-input {
  flex: 1;
  min-width: 0;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 0.82rem;
}

.folder-creator-actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
}

.file-list,
.recipe-list {
  list-style: none;
  padding: 4px 0;
  overflow-y: auto;
}

.file-list {
  flex: 1;
  min-height: 0;
}

.file-item {
  position: relative;
  display: flex;
  align-items: center;
  padding: 6px 16px;
  margin: 2px 8px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.85rem;
  gap: 6px;
}

.file-item:hover {
  background-color: #2a2d2e;
}

.file-item i {
  width: 16px;
  margin-top: 1px;
}

.file-info {
  flex: 1;
  min-width: 0;
  padding-right: 30px;
  display: flex;
  align-items: center;
}

.file-actions {
  position: absolute;
  right: 12px;
  top: 50%;
  transform: translateY(-50%);
  opacity: 0;
  transition: opacity 0.12s;
  z-index: 2;
  display: inline-flex;
  align-items: center;
}

.file-item:hover .file-actions {
  opacity: 1;
}

.file-name {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.file-action-btn {
  border: 1px solid #3f4e60;
  background-color: #2d3a4a;
  color: #d9e7f6;
  border-radius: 6px;
  padding: 2px 8px;
}

.file-action-btn i {
  width: auto;
}

.file-action-btn:hover {
  background-color: #39506a;
}

.folder-icon {
  color: #c586c0;
}

.file-icon {
  color: #9cdcfe;
}

.panel-error {
  color: #ef6a62;
  font-size: 12px;
  padding: 8px 12px;
}

.shortcut-panel {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background-color: #1f2430;
}

.recipes-panel {
  flex: 1;
  overflow-y: auto;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background-color: #1f2832;
}

.terminal-size-editor {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  padding: 12px;
  border: 1px solid #364556;
  border-radius: 12px;
  background: linear-gradient(180deg, rgba(20, 28, 37, 0.96), rgba(16, 22, 29, 0.96));
}

.terminal-size-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.terminal-size-field span {
  font-size: 0.72rem;
  color: #8faecc;
  letter-spacing: 0.02em;
}

.terminal-size-field input {
  width: 100%;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 6px 8px;
  font-size: 0.82rem;
}

.terminal-size-actions {
  grid-column: 1 / -1;
  display: flex;
  gap: 8px;
  padding-top: 2px;
}

.shortcut-note {
  font-size: 0.74rem;
  color: #9eb8d4;
}

.shortcut-editor {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  padding: 12px;
  border: 1px solid #303a47;
  border-radius: 12px;
  background: linear-gradient(180deg, rgba(25, 33, 43, 0.96), rgba(19, 25, 33, 0.96));
}

.shortcut-editor-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.shortcut-editor-field-wide {
  grid-column: 1 / -1;
}

.shortcut-editor-label {
  font-size: 0.72rem;
  color: #8faecc;
  letter-spacing: 0.02em;
}

.shortcut-editor input[type='text'],
.recipe-editor input,
.recipe-editor select,
.recipe-editor textarea {
  width: 100%;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 6px 8px;
  font-size: 0.82rem;
}

.shortcut-editor-actions {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 10px;
  padding-top: 2px;
  flex-wrap: wrap;
}

.shortcut-enter-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: auto;
  min-width: 88px;
  height: 36px;
  border: 1px solid #3d4b5b;
  border-radius: 10px;
  background: #141b24;
  color: #9eb8d4;
  cursor: pointer;
  transition: background-color 0.15s ease, border-color 0.15s ease, color 0.15s ease;
  gap: 8px;
  padding: 0 12px;
}

.shortcut-enter-toggle:hover {
  background-color: #1a2430;
  border-color: #53708f;
}

.shortcut-enter-toggle.active {
  background-color: rgba(14, 99, 156, 0.22);
  border-color: #0e639c;
  color: #9cdcfe;
}

.shortcut-enter-icon {
  font-size: 0.95rem;
}

.shortcut-groups {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.shortcut-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.shortcut-group-title {
  font-size: 0.72rem;
  color: #95b5d8;
  text-transform: uppercase;
  letter-spacing: 0.4px;
}

.shortcut-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
}

.shortcut-btn {
  justify-content: center;
  font-size: 0.76rem;
  background-color: #2d3a4a;
  border-color: #3f4e60;
  color: #d9e7f6;
}

.shortcut-btn:hover {
  background-color: #39506a;
}

.recipe-editor {
  display: flex;
  flex-direction: column;
  gap: 8px;
  border-bottom: 1px solid #3c3c3c;
}

.recipe-editor-actions {
  display: flex;
  gap: 8px;
}

.recipe-item {
  position: relative;
  display: flex;
  align-items: center;
  padding: 8px 44px 8px 12px;
  margin: 2px 0;
  border-radius: 4px;
  font-size: 0.82rem;
  gap: 8px;
}

.recipe-item:hover {
  background-color: #2a2d2e;
}

.recipe-item.default {
  border: 1px solid #3f6e98;
  background-color: #24384d;
}

.recipe-icon {
  color: #c586c0;
  width: 20px;
}

.recipe-info {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.recipe-name {
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.recipe-command {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.7rem;
  color: #9cdcfe;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.recipe-actions {
  position: absolute;
  right: 34px;
  top: 50%;
  transform: translateY(-50%);
  display: inline-flex;
  gap: 6px;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.12s ease;
}

.recipe-item:hover .recipe-actions,
.recipe-item:focus-within .recipe-actions {
  opacity: 1;
  pointer-events: auto;
}

.recipe-meta {
  position: absolute;
  right: 8px;
  top: 50%;
  transform: translateY(-50%);
  z-index: 1;
  display: inline-flex;
  align-items: center;
}

.recipe-star-btn,
.recipe-action-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border: 1px solid transparent;
  background: transparent;
  color: #7f93a8;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.12s ease, color 0.12s ease, border-color 0.12s ease;
}

.recipe-star-btn:hover,
.recipe-action-btn:hover {
  background-color: rgba(61, 75, 91, 0.68);
  color: #d9e7f6;
  border-color: #46607b;
}

.recipe-star-btn.active {
  color: #f0c96a;
}

.recipe-star-btn {
  opacity: 0;
  pointer-events: none;
}

.recipe-item:hover .recipe-star-btn,
.recipe-item:focus-within .recipe-star-btn {
  opacity: 1;
  pointer-events: auto;
}

.recipe-action-btn {
  background-color: rgba(21, 28, 36, 0.92);
  border-color: #3d4b5b;
  color: #d9e7f6;
}

.recipe-action-btn.danger:hover {
  color: #ffd2cd;
  border-color: #8f4a43;
  background-color: rgba(90, 36, 30, 0.88);
}

.recipe-actions i {
  color: #b4b4b4;
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  font-size: 0.8rem;
}

.recipe-actions i:hover {
  background-color: #4a4a4a;
  color: #fff;
}

.right-tab-footer {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding: 8px 10px 10px;
  border-top: 1px solid #3c3c3c;
  background-color: #252526;
}

.right-tab-btn {
  justify-content: center;
  font-size: 0.78rem;
  padding: 6px 8px;
}

.right-tab-btn.active {
  background-color: #0e639c;
  color: #fff;
  border-color: #0e639c;
}

button {
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

button:hover {
  background-color: #4a4a4a;
}

button.primary {
  background-color: #0e639c;
}

button.primary:hover {
  background-color: #1177bb;
}

@media (max-width: 820px) {
  .shortcut-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .shortcut-editor {
    grid-template-columns: 1fr;
  }

  .shortcut-editor-actions,
  .recipe-editor-actions,
  .right-tab-footer {
    flex-wrap: wrap;
  }

  .file-item,
  .recipe-item {
    margin-left: 6px;
    margin-right: 6px;
  }

  .folder-creator {
    align-items: stretch;
    flex-direction: column;
  }

  .folder-creator-actions {
    justify-content: flex-end;
  }
}

@media (max-width: 640px) {
  .file-item,
  .recipe-item {
    padding-left: 10px;
    padding-right: 10px;
  }

  .path-breadcrumb {
    width: 100%;
    flex: 1 1 100%;
  }
}
</style>
