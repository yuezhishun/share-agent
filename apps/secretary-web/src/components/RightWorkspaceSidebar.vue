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
          :class="{ active: showTerminalEnvLibrary }"
          title="环境变量库"
          data-testid="terminal-env-library-toggle"
          @click="showTerminalEnvLibrary ? emit('close-terminal-env-library') : emit('open-terminal-env-library')"
        ><i class="fa-solid fa-boxes-stacked" /></span>
        <span
          class="add-icon"
          :class="{ active: showTerminalEnvEditor }"
          :title="showTerminalEnvEditor ? '收起环境变量表单' : '添加环境变量'"
          data-testid="terminal-env-editor-toggle"
          @click="showTerminalEnvEditor ? emit('close-terminal-env-editor') : emit('open-terminal-env-editor')"
        ><i :class="showTerminalEnvEditor ? 'fa-solid fa-minus' : 'fa-solid fa-plus'" /></span>
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
          :ref="setFolderNameInputRef"
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
      <div class="voice-mode-card" data-testid="voice-mode-settings">
        <div class="voice-mode-header">
          <div class="voice-mode-title">语音输入模式</div>
          <button
            type="button"
            class="voice-mode-toggle-btn"
            :class="{ active: voiceModeEnabled }"
            :aria-pressed="voiceModeEnabled"
            data-testid="voice-mode-toggle"
            :title="voiceModeEnabled ? '切换到普通终端输入' : '切换到语音输入模式'"
            @click="emit('toggle-voice-mode')"
          >
            <i :class="voiceModeEnabled ? 'fa-regular fa-keyboard' : 'fa-solid fa-microphone-lines'" aria-hidden="true" />
            <span>{{ voiceModeEnabled ? '切换到终端输入' : '切换到语音输入' }}</span>
          </button>
        </div>
        <div class="voice-mode-meta">网页快捷键：{{ voiceModeShortcutLabel }}</div>
        <div class="voice-mode-meta">当前输入：{{ voiceModeEnabled ? '语音输入模式' : '普通终端输入' }}</div>
      </div>
      <form
        v-if="showShortcutEditor"
        class="shortcut-editor"
        data-testid="shortcut-editor"
        @submit.prevent="emit('add-shortcut-command')"
      >
        <div class="shortcut-editor-field">
          <span class="shortcut-editor-label">按钮名</span>
          <input
            :ref="setShortcutLabelInputRef"
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
      <div class="shortcut-note">按分组发送控制键或命令，常用新增项收在右上角 +</div>
      <div class="shortcut-groups">
        <div v-for="group in shortcutGroups" :key="group.group" class="shortcut-group">
          <div class="shortcut-group-title">{{ group.group }}</div>
          <div class="shortcut-grid">
            <div
              v-for="item in group.items"
              :key="item.id"
              class="shortcut-btn-shell"
            >
              <button
                type="button"
                class="shortcut-btn"
                @click="emit('send-shortcut', item)"
              >
                {{ item.label }}
              </button>
              <button
                v-if="item.isCustom"
                type="button"
                class="shortcut-remove-btn"
                :title="`删除快捷指令 ${item.label}`"
                @click="emit('remove-shortcut', item)"
              >
                <i class="fa-regular fa-trash-can" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <RightSidebarRecipesTab
      v-else
      :show-terminal-size-editor="showTerminalSizeEditor"
      :terminal-size-draft-cols="terminalSizeDraftCols"
      :terminal-size-draft-rows="terminalSizeDraftRows"
      :show-terminal-env-library="showTerminalEnvLibrary"
      :terminal-env-search="terminalEnvSearch"
      :terminal-env-group-filter="terminalEnvGroupFilter"
      :terminal-env-groups="terminalEnvGroups"
      :terminal-env-items="terminalEnvItems"
      :filtered-terminal-env-items="filteredTerminalEnvItems"
      :show-terminal-env-editor="showTerminalEnvEditor"
      :terminal-env-editor="terminalEnvEditor"
      :editing-terminal-env-id="editingTerminalEnvId"
      :show-recipe-editor="showRecipeEditor"
      :recipe-editor="recipeEditor"
      :recipe-folders-loading="recipeFoldersLoading"
      :recipe-folder-options="recipeFolderOptions"
      :recipe-folders-error="recipeFoldersError"
      :editing-recipe-id="editingRecipeId"
      :recipe-items="recipeItems"
      :recipe-env-preview="recipeEnvPreview"
      :is-env-entry-included-by-group="isEnvEntryIncludedByGroup"
      :is-default-create-recipe="isDefaultCreateRecipe"
      :format-recipe-summary="formatRecipeSummary"
      :set-recipe-name-input-ref="setRecipeNameInputRef"
      @close-terminal-env-library="emit('close-terminal-env-library')"
      @open-terminal-env-editor="emit('open-terminal-env-editor')"
      @close-terminal-env-editor="emit('close-terminal-env-editor')"
      @update:terminal-env-search="emit('update:terminalEnvSearch', $event)"
      @update:terminal-env-group-filter="emit('update:terminalEnvGroupFilter', $event)"
      @update:terminal-env-editor="emit('update:terminalEnvEditor', $event)"
      @reset-terminal-env-editor="emit('reset-terminal-env-editor')"
      @submit-terminal-env-editor="emit('submit-terminal-env-editor')"
      @edit-terminal-env="emit('edit-terminal-env', $event)"
      @remove-terminal-env="emit('remove-terminal-env', $event)"
      @update:terminal-size-draft-cols="emit('update:terminalSizeDraftCols', $event)"
      @update:terminal-size-draft-rows="emit('update:terminalSizeDraftRows', $event)"
      @apply-terminal-size="emit('apply-terminal-size')"
      @cancel-terminal-size-editor="emit('cancel-terminal-size-editor')"
      @cancel-recipe-edit="emit('cancel-recipe-edit')"
      @update:recipe-editor="emit('update:recipeEditor', $event)"
      @submit-recipe-editor="emit('submit-recipe-editor')"
      @toggle-recipe-supported-os="emit('toggle-recipe-supported-os', $event)"
      @set-recipe-env-group="emit('set-recipe-env-group', $event)"
      @toggle-recipe-env-entry="emit('toggle-recipe-env-entry', $event)"
      @toggle-default-create-recipe="emit('toggle-default-create-recipe', $event)"
      @run-recipe="emit('run-recipe', $event)"
      @edit-recipe="emit('edit-recipe', $event)"
      @remove-recipe="emit('remove-recipe', $event)"
    />

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
import RightSidebarRecipesTab from './RightSidebarRecipesTab.vue';

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
  voiceModeEnabled: { type: Boolean, default: false },
  voiceModeShortcutLabel: { type: String, default: '' },
  showTerminalSizeEditor: { type: Boolean, default: false },
  terminalSizeDraftCols: { type: String, default: '' },
  terminalSizeDraftRows: { type: String, default: '' },
  showTerminalEnvLibrary: { type: Boolean, default: false },
  terminalEnvSearch: { type: String, default: '' },
  terminalEnvGroupFilter: { type: String, default: '' },
  terminalEnvGroups: { type: Array, default: () => [] },
  terminalEnvItems: { type: Array, default: () => [] },
  filteredTerminalEnvItems: { type: Array, default: () => [] },
  showTerminalEnvEditor: { type: Boolean, default: false },
  terminalEnvEditor: { type: Object, default: () => ({}) },
  editingTerminalEnvId: { type: String, default: '' },
  showRecipeEditor: { type: Boolean, default: false },
  recipeEditor: { type: Object, required: true },
  recipeFoldersLoading: { type: Boolean, default: false },
  recipeFolderOptions: { type: Array, default: () => [] },
  recipeFoldersError: { type: String, default: '' },
  editingRecipeId: { type: String, default: '' },
  recipeItems: { type: Array, default: () => [] },
  recipeEnvPreview: { type: Object, default: () => ({}) },
  isEnvEntryIncludedByGroup: { type: Function, required: true },
  isDefaultCreateRecipe: { type: Function, required: true },
  formatRecipeSummary: { type: Function, required: true },
  setShortcutLabelInputRef: { type: Function, required: true },
  setFolderNameInputRef: { type: Function, required: true },
  setRecipeNameInputRef: { type: Function, required: true }
});

const emit = defineEmits([
  'toggle-show-hidden',
  'refresh-files-list',
  'toggle-folder-creator',
  'pick-upload-files',
  'toggle-shortcut-editor',
  'toggle-voice-mode',
  'toggle-terminal-size-editor',
  'open-terminal-env-library',
  'close-terminal-env-library',
  'open-terminal-env-editor',
  'close-terminal-env-editor',
  'update:terminalEnvSearch',
  'update:terminalEnvGroupFilter',
  'update:terminalEnvEditor',
  'reset-terminal-env-editor',
  'submit-terminal-env-editor',
  'edit-terminal-env',
  'remove-terminal-env',
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
  'remove-shortcut',
  'update:recipeEditor',
  'submit-recipe-editor',
  'toggle-recipe-supported-os',
  'set-recipe-env-group',
  'toggle-recipe-env-entry',
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
  position: relative;
}

.terminal-env-library {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid #35506a;
  border-radius: 14px;
  background: linear-gradient(180deg, rgba(12, 22, 31, 0.98), rgba(16, 27, 37, 0.98));
}

.terminal-env-library-topbar,
.terminal-env-library-toolbar,
.terminal-env-editor-row,
.terminal-env-item-actions,
.recipe-chip-list,
.recipe-editor-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.terminal-env-library-title,
.recipe-editor-block-title {
  font-size: 0.78rem;
  font-weight: 600;
  color: #d8ecff;
}

.icon-btn-inline {
  margin-left: auto;
}

.terminal-env-library-layout {
  display: grid;
  grid-template-columns: 88px minmax(0, 1fr);
  gap: 10px;
}

.terminal-env-group-list,
.terminal-env-main,
.terminal-env-editor,
.recipe-editor-block,
.recipe-env-entry-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.terminal-env-group-chip,
.recipe-chip {
  justify-content: flex-start;
  background: #1b2430;
  border-color: #3d4b5b;
  color: #c7dff7;
}

.terminal-env-group-chip.active,
.recipe-chip.active {
  background: #0e639c;
  border-color: #0e639c;
  color: #fff;
}

.terminal-env-list {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.terminal-env-item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  padding: 10px;
  border: 1px solid #33485d;
  border-radius: 10px;
  background: rgba(23, 33, 44, 0.86);
}

.terminal-env-item-main {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.terminal-env-item-key {
  font-weight: 600;
  color: #f2f7fb;
}

.terminal-env-item-meta,
.terminal-env-array-hint,
.recipe-check-row small {
  font-size: 0.72rem;
  color: #90afcb;
}

.terminal-env-item-preview,
.recipe-env-preview {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.72rem;
  color: #9cdcfe;
  white-space: pre-wrap;
  word-break: break-word;
}

.terminal-env-array-editor,
.terminal-env-array-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.terminal-env-array-row {
  flex-direction: row;
}

.terminal-env-enabled-toggle,
.recipe-check-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.8rem;
  color: #d9e7f6;
}

.recipe-editor-block {
  padding: 10px;
  border: 1px solid #31485d;
  border-radius: 10px;
  background: rgba(20, 29, 38, 0.86);
}

.recipe-env-entry-list {
  max-height: 160px;
  overflow-y: auto;
}

.recipe-env-preview {
  margin: 0;
  padding: 10px;
  border-radius: 8px;
  background: #111923;
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

.voice-mode-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px solid #2f4251;
  border-radius: 12px;
  background: linear-gradient(180deg, rgba(18, 31, 42, 0.96), rgba(15, 24, 34, 0.96));
}

.voice-mode-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
}

.voice-mode-title {
  font-size: 0.8rem;
  font-weight: 600;
  color: #d8ecff;
}

.voice-mode-toggle-btn {
  background: rgba(18, 62, 88, 0.82);
  border: 1px solid rgba(84, 156, 202, 0.65);
  color: #dff4ff;
}

.voice-mode-toggle-btn:hover {
  background: rgba(23, 78, 109, 0.92);
}

.voice-mode-toggle-btn.active {
  background: rgba(22, 102, 67, 0.9);
  border-color: rgba(94, 204, 149, 0.72);
  color: #effff6;
}

.voice-mode-meta {
  font-size: 0.73rem;
  color: #95b5d8;
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

.shortcut-btn-shell {
  position: relative;
}

.shortcut-btn {
  width: 100%;
  justify-content: center;
  font-size: 0.76rem;
  background-color: #2d3a4a;
  border-color: #3f4e60;
  color: #d9e7f6;
}

.shortcut-btn:hover {
  background-color: #39506a;
}

.shortcut-remove-btn {
  position: absolute;
  top: 4px;
  right: 4px;
  width: 22px;
  height: 22px;
  padding: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  background-color: rgba(17, 19, 21, 0.8);
  border-color: rgba(244, 135, 113, 0.35);
  color: #f2a194;
}

.shortcut-remove-btn:hover {
  background-color: rgba(94, 33, 27, 0.9);
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
  .shortcut-editor {
    grid-template-columns: 1fr;
  }

  .terminal-env-library-layout {
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
