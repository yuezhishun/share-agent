<template>
  <div class="recipes-tab-panel">
    <form v-if="showTerminalEnvEditor" class="recipe-form-card terminal-env-editor" @submit.prevent="emit('submit-terminal-env-editor')">
      <div class="card-header">
        <div>
          <div class="card-eyebrow">Terminal Env</div>
          <div class="card-title">{{ editingTerminalEnvId ? '编辑环境变量' : '添加环境变量' }}</div>
        </div>
        <button type="button" class="icon-btn-inline icon-only-btn" title="关闭" @click="emit('close-terminal-env-editor')">
          <i class="fa-solid fa-xmark" />
        </button>
      </div>

      <label class="form-field">
        <span>变量名</span>
        <input
          :value="terminalEnvEditor.key"
          type="text"
          placeholder="如 OPENAI_API_KEY"
          @input="emit('update:terminalEnvEditor', { ...terminalEnvEditor, key: $event.target.value })"
        />
      </label>

      <label class="form-field">
        <span>分组</span>
        <input
          :value="terminalEnvEditor.group"
          list="terminal-env-group-options"
          type="text"
          placeholder="可选择已有分组，也可直接输入新分组"
          @input="emit('update:terminalEnvEditor', { ...terminalEnvEditor, group: $event.target.value })"
        />
        <datalist id="terminal-env-group-options">
          <option v-for="group in terminalEnvGroups" :key="`terminal-env-group-${group}`" :value="group" />
        </datalist>
      </label>

      <label class="form-field">
        <span>值</span>
        <textarea
          :value="terminalEnvEditor.value"
          rows="3"
          placeholder="单行是字符串，换行自动为数组"
          @input="emit('update:terminalEnvEditor', { ...terminalEnvEditor, value: $event.target.value })"
        />
      </label>

      <div class="field-hint">运行时按节点平台自动拼接；Windows 用 `;`，Linux 用 `:`</div>

      <div class="action-row">
        <button type="submit" class="primary">{{ editingTerminalEnvId ? '更新' : '保存' }}</button>
        <button type="button" class="secondary" @click="emit('reset-terminal-env-editor')">清空</button>
      </div>
    </form>

    <section v-if="showTerminalEnvLibrary" class="recipe-card env-library-card">
      <div class="card-header">
        <div>
          <div class="card-eyebrow">Terminal Env</div>
          <div class="card-title">环境变量库</div>
        </div>
        <div class="header-actions">
          <button type="button" class="icon-btn-inline icon-only-btn" title="关闭" @click="emit('close-terminal-env-library')">
            <i class="fa-solid fa-xmark" />
          </button>
        </div>
      </div>

      <div class="stack-actions">
        <select :value="terminalEnvGroupFilter" @change="emit('update:terminalEnvGroupFilter', $event.target.value)">
          <option value="">全部分组</option>
          <option v-for="group in terminalEnvGroups" :key="group" :value="group">{{ group }}</option>
        </select>
        <input
          :value="terminalEnvSearch"
          type="text"
          placeholder="搜索 key"
          @input="emit('update:terminalEnvSearch', $event.target.value)"
        />
      </div>

      <ul class="terminal-env-list">
        <li v-for="item in filteredTerminalEnvItems" :key="item.id" class="terminal-env-item">
          <div class="terminal-env-item-content">
            <div class="terminal-env-item-head">
              <div class="terminal-env-item-key">{{ item.key }}</div>
              <div class="terminal-env-item-meta">{{ item.group }}</div>
            </div>
            <div class="terminal-env-item-preview">
              {{ item.valueType === 'array' ? item.value.join(' · ') : item.value }}
            </div>
          </div>
          <div class="item-actions terminal-env-item-actions">
            <button type="button" class="recipe-action-btn" title="编辑变量" @click="emit('edit-terminal-env', item)">
              <i class="fa-regular fa-pen-to-square" />
            </button>
            <button type="button" class="recipe-action-btn danger" title="删除变量" @click="emit('remove-terminal-env', item.id)">
              <i class="fa-regular fa-trash-can" />
            </button>
          </div>
        </li>
      </ul>
    </section>

    <section v-if="showTerminalSizeEditor" class="recipe-card terminal-size-card">
      <div class="card-header">
        <div>
          <div class="card-eyebrow">Terminal Size</div>
          <div class="card-title">伪终端尺寸</div>
        </div>
        <button type="button" class="icon-btn-inline icon-only-btn" title="关闭" @click="emit('cancel-terminal-size-editor')">
          <i class="fa-solid fa-xmark" />
        </button>
      </div>
      <div class="field-grid">
        <label class="form-field">
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
        <label class="form-field">
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
      </div>
      <div class="action-row">
        <button type="button" class="primary" @click="emit('apply-terminal-size')">确定</button>
        <button type="button" class="secondary" @click="emit('cancel-terminal-size-editor')">取消</button>
      </div>
    </section>

    <form v-if="showRecipeEditor" class="recipe-form-card recipe-editor" @submit.prevent="emit('submit-recipe-editor')">
      <div class="card-header">
        <div>
          <div class="card-eyebrow">Recipe Editor</div>
          <div class="card-title">{{ editingRecipeId ? '编辑终端配方' : '新建终端配方' }}</div>
        </div>
        <button type="button" class="icon-btn-inline icon-only-btn" title="关闭" @click="emit('cancel-recipe-edit')">
          <i class="fa-solid fa-xmark" />
        </button>
      </div>

      <label class="form-field recipe-name-field">
        <span>显示名</span>
        <input
          :ref="setRecipeNameInputRef"
          :value="recipeEditor.name"
          type="text"
          placeholder="显示名（可选）"
          @input="emit('update:recipeEditor', { ...recipeEditor, name: $event.target.value })"
        />
      </label>

      <label class="form-field recipe-cwd-field">
        <span>工作目录</span>
        <select
          :value="recipeEditor.cwd"
          :disabled="recipeFoldersLoading || recipeFolderOptions.length === 0"
          @change="emit('update:recipeEditor', { ...recipeEditor, cwd: $event.target.value })"
        >
          <option v-for="item in recipeFolderOptions" :key="item.path" :value="item.path">
            {{ item.label }}
          </option>
        </select>
      </label>

      <label class="form-field">
        <span>命令行</span>
        <textarea
          :value="recipeEditor.commandLine"
          rows="3"
          placeholder='命令行，如 bash -lc "npm run dev" 或 ["bash","-lc","npm run dev"]'
          required
          @input="emit('update:recipeEditor', { ...recipeEditor, commandLine: $event.target.value })"
        />
      </label>

      <div v-if="recipeFoldersError" class="panel-error">{{ recipeFoldersError }}</div>

      <section class="recipe-block">
        <div class="recipe-block-title">兼容平台</div>
        <div class="platform-row compact-chip-row">
          <label class="option-tile compact-option-tile">
            <span class="compact-option-body">
              <input
                :checked="Array.isArray(recipeEditor.supportedOs) && recipeEditor.supportedOs.includes('windows')"
                type="checkbox"
                @change="emit('toggle-recipe-supported-os', 'windows')"
              />
              <span class="compact-option-label">Windows</span>
            </span>
          </label>
          <label class="option-tile compact-option-tile">
            <span class="compact-option-body">
              <input
                :checked="Array.isArray(recipeEditor.supportedOs) && recipeEditor.supportedOs.includes('linux')"
                type="checkbox"
                @change="emit('toggle-recipe-supported-os', 'linux')"
              />
              <span class="compact-option-label">Linux</span>
            </span>
          </label>
        </div>
      </section>

      <label class="form-field recipe-env-group-field">
        <span>环境变量分组</span>
        <select
          :value="Array.isArray(recipeEditor.selectedEnvGroupNames) && recipeEditor.selectedEnvGroupNames.length > 0 ? recipeEditor.selectedEnvGroupNames[0] : ''"
          @change="emit('set-recipe-env-group', $event.target.value)"
        >
          <option value="">不选择</option>
          <option v-for="group in terminalEnvGroups" :key="`recipe-env-group-${group}`" :value="group">
            {{ group }}
          </option>
        </select>
      </label>

      <section class="recipe-block">
        <div class="recipe-block-title">环境变量项</div>
        <div class="entry-list">
          <label v-for="item in terminalEnvItems" :key="`recipe-env-${item.id}`" class="entry-row compact-entry-row">
            <input
              :checked="Array.isArray(recipeEditor.selectedEnvEntryIds) && recipeEditor.selectedEnvEntryIds.includes(item.id)"
              type="checkbox"
              @change="emit('toggle-recipe-env-entry', item.id)"
            />
            <span class="entry-key compact-entry-key">{{ item.key }}</span>
            <small v-if="isEnvEntryIncludedByGroup(item)" class="entry-tag">已由分组包含</small>
          </label>
        </div>
      </section>

      <label class="form-field">
        <span>覆盖环境变量</span>
        <textarea
          :value="recipeEditor.envInput"
          rows="2"
          placeholder='配方覆盖环境变量(JSON对象)，如 {"TERM":"xterm-256color"}'
          @input="emit('update:recipeEditor', { ...recipeEditor, envInput: $event.target.value })"
        />
      </label>

      <section class="recipe-block">
        <div class="recipe-block-title">最终环境变量预览</div>
        <pre class="recipe-env-preview">{{ JSON.stringify(recipeEnvPreview, null, 2) }}</pre>
      </section>

      <div class="action-row">
        <button type="submit" class="primary" :disabled="recipeFoldersLoading || recipeFolderOptions.length === 0">{{ editingRecipeId ? '更新配方' : '保存配方' }}</button>
        <button type="button" class="secondary" @click="emit('cancel-recipe-edit')">取消</button>
      </div>
    </form>

    <ul v-if="!showTerminalEnvLibrary && !showTerminalEnvEditor" id="recipeList" class="recipe-list">
      <li v-for="item in recipeItems" :key="item.id" class="recipe-item" :class="{ default: isDefaultCreateRecipe(item.id) }">
        <div class="recipe-item-main">
          <div class="recipe-icon"><i class="fa-regular fa-file-lines" /></div>
          <div class="recipe-title-wrap">
            <div class="recipe-name">{{ item.name || item.command }}</div>
            <div class="recipe-command" :title="formatRecipeSummary(item)">{{ formatRecipeSummary(item) }}</div>
          </div>
          <div class="item-actions recipe-item-actions">
            <button
              type="button"
              class="recipe-action-btn"
              :class="{ active: isDefaultCreateRecipe(item.id) }"
              :title="isDefaultCreateRecipe(item.id) ? '取消 + 号默认配方' : '设为 + 号默认配方'"
              @click="emit('toggle-default-create-recipe', item)"
            >
              <i :class="isDefaultCreateRecipe(item.id) ? 'fa-solid fa-star' : 'fa-regular fa-star'" />
            </button>
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
</template>

<script setup>
defineProps({
  showTerminalSizeEditor: { type: Boolean, default: false },
  terminalSizeDraftCols: { type: String, default: '' },
  terminalSizeDraftRows: { type: String, default: '' },
  showTerminalEnvLibrary: { type: Boolean, default: false },
  showTerminalEnvEditor: { type: Boolean, default: false },
  terminalEnvSearch: { type: String, default: '' },
  terminalEnvGroupFilter: { type: String, default: '' },
  terminalEnvGroups: { type: Array, default: () => [] },
  terminalEnvItems: { type: Array, default: () => [] },
  filteredTerminalEnvItems: { type: Array, default: () => [] },
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
  setRecipeNameInputRef: { type: Function, required: true }
});

const emit = defineEmits([
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
  'update:recipeEditor',
  'submit-recipe-editor',
  'toggle-recipe-supported-os',
  'set-recipe-env-group',
  'toggle-recipe-env-entry',
  'toggle-default-create-recipe',
  'run-recipe',
  'edit-recipe',
  'remove-recipe'
]);
</script>

<style scoped>
.recipes-tab-panel {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background-color: #1f2832;
}

.recipe-card,
.recipe-form-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  padding: 10px;
  border: 1px solid #324456;
  border-radius: 14px;
  background: linear-gradient(180deg, rgba(14, 22, 31, 0.96), rgba(18, 29, 39, 0.96));
  box-sizing: border-box;
}

.card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.header-actions {
  display: flex;
  gap: 4px;
}

.card-eyebrow {
  font-size: 0.68rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #82a6c8;
}

.card-title,
.recipe-block-title {
  font-size: 0.84rem;
  font-weight: 600;
  color: #e2edf8;
}

.stack-actions,
.action-row,
.item-actions,
.chip-row {
  display: flex;
  gap: 8px;
  min-width: 0;
}

.stack-actions {
  flex-direction: column;
}

.chip-row {
  gap: 6px;
}

.field-grid,
.option-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  min-width: 0;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.form-field span,
.field-hint,
.entry-row small {
  font-size: 0.72rem;
  color: #90afcb;
}

.toggle-row,
.entry-row,
.option-tile {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  font-size: 0.78rem;
  color: #d9e7f6;
}

.toggle-row input,
.entry-row input,
.option-tile input {
  flex: 0 0 auto;
  margin: 0;
}

input,
select,
textarea,
button {
  box-sizing: border-box;
}

input[type='text'],
input[type='number'],
select,
textarea {
  width: 100%;
  min-width: 0;
  max-width: 100%;
  border: 1px solid #3d4b5b;
  border-radius: 10px;
  background: #131b24;
  color: #e6eef7;
  padding: 7px 10px;
  font-size: 0.82rem;
}

textarea {
  resize: vertical;
}

button {
  width: auto;
  max-width: 100%;
  min-width: 0;
  min-height: 32px;
  padding: 7px 12px;
  border: 1px solid #3d4b5b;
  border-radius: 10px;
  background: #19232d;
  color: #d9e7f6;
  font-size: 0.8rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  cursor: pointer;
  transition: background-color 0.15s ease, border-color 0.15s ease, color 0.15s ease, opacity 0.15s ease;
}

button:hover {
  background-color: #223243;
  border-color: #4c6480;
}

button.primary {
  background: #0e639c;
  border-color: #0e639c;
  color: #fff;
}

button.primary:hover {
  background: #1177bb;
  border-color: #1177bb;
}

button.secondary {
  background: #15212b;
}

.icon-btn-inline {
  margin-left: auto;
}

.icon-only-btn,
.recipe-action-btn {
  width: 32px;
  min-width: 32px;
  height: 32px;
  padding: 0;
}

.terminal-env-list,
.entry-list,
.recipe-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.terminal-env-list,
.recipe-list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.terminal-env-item,
.recipe-item,
.recipe-block {
  min-width: 0;
  border: 1px solid #314556;
  border-radius: 12px;
  background: rgba(21, 31, 40, 0.9);
}

.recipe-block,
.terminal-env-item {
  padding: 8px 10px;
}

.recipe-block {
  gap: 7px;
}

.recipe-item {
  position: relative;
  display: flex;
  align-items: center;
  padding: 8px 10px;
  min-height: 42px;
  transition: background-color 0.12s ease, padding-right 0.12s ease;
}

.recipe-action-btn.active {
  color: #f0c96a;
  border-color: #6f5a2a;
  background-color: rgba(58, 49, 30, 0.92);
}

.recipe-action-btn.danger:hover {
  color: #ffd2cd;
  border-color: #8f4a43;
  background-color: rgba(90, 36, 30, 0.88);
}

.platform-grid {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.option-tile {
  flex: 1 1 0;
  min-width: 0;
  padding: 8px 10px;
  border: 1px solid #304252;
  border-radius: 12px;
  background: rgba(18, 28, 38, 0.82);
  align-items: center;
  white-space: nowrap;
}

.compact-chip-row {
  gap: 6px;
}

.compact-option-tile {
  flex: 1 1 0;
  min-height: 30px;
  padding: 0;
  border-radius: 999px;
  background: rgba(16, 25, 34, 0.9);
  border-color: #3a4c5f;
  font-size: 0.74rem;
  line-height: 1;
  justify-content: stretch;
}

.compact-option-body {
  width: 100%;
  min-width: 0;
  min-height: 30px;
  display: grid;
  grid-template-columns: 14px minmax(0, 1fr);
  align-items: center;
  column-gap: 6px;
  padding: 0 10px;
}

.compact-option-body input {
  width: 14px;
  height: 14px;
  margin: 0;
}

.compact-option-tile:focus-within {
  background: rgba(14, 99, 156, 0.18);
  border-color: #0e639c;
}

.compact-option-label {
  display: block;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.compact-option-body input:checked + .compact-option-label {
  color: #dff3ff;
}

.entry-list {
  max-height: 136px;
  overflow-y: auto;
  overflow-x: hidden;
  gap: 6px;
}

.entry-row {
  min-height: 28px;
}

.compact-entry-row {
  min-height: 32px;
  padding: 4px 8px;
  border: 1px solid #2f4152;
  border-radius: 8px;
  background: rgba(16, 24, 33, 0.72);
  font-size: 0.74rem;
  line-height: 1.2;
  display: grid;
  grid-template-columns: 14px minmax(0, 1fr) auto;
  align-items: center;
  column-gap: 8px;
}

.entry-key {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.compact-entry-key {
  display: block;
  color: #d9e7f6;
  font-size: 0.74rem;
  line-height: 1.2;
}

.entry-row small {
  flex: 0 0 auto;
  white-space: nowrap;
}

.entry-tag {
  display: inline-flex;
  align-items: center;
  min-height: 18px;
  padding: 0 6px;
  border: 1px solid rgba(78, 119, 156, 0.55);
  border-radius: 999px;
  background: rgba(21, 44, 63, 0.72);
  color: #8fb8de;
  font-size: 0.66rem;
  line-height: 1;
}

.recipe-env-preview {
  margin: 0;
  padding: 8px 10px;
  border-radius: 10px;
  background: #111923;
  overflow: auto;
  max-height: 170px;
}

.panel-error {
  color: #ef6a62;
  font-size: 12px;
}

.platform-row {
  display: flex;
  gap: 8px;
  flex-wrap: nowrap;
  align-items: center;
}

.terminal-env-item {
  position: relative;
  display: flex;
  align-items: center;
  padding-right: 82px;
  transition: background-color 0.12s ease;
}

.terminal-env-item-content {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.terminal-env-item-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.terminal-env-item-key {
  font-weight: 600;
  color: #f2f7fb;
  font-size: 0.8rem;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.terminal-env-item-preview {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.72rem;
  color: #9cdcfe;
  white-space: pre-wrap;
  word-break: break-word;
  overflow-wrap: anywhere;
  line-height: 1.4;
  width: 100%;
}

.terminal-env-item-meta {
  font-size: 0.68rem;
  color: #90afcb;
  white-space: nowrap;
}

.terminal-env-item-actions,
.recipe-item-actions {
  display: flex;
  gap: 4px;
  position: absolute;
  right: 10px;
  top: 50%;
  transform: translateY(-50%);
  visibility: hidden;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.12s ease;
}

.terminal-env-item:hover .terminal-env-item-actions,
.terminal-env-item:focus-within .terminal-env-item-actions,
.recipe-item:hover .recipe-item-actions,
.recipe-item:focus-within .recipe-item-actions {
  visibility: visible;
  opacity: 1;
  pointer-events: auto;
}

.recipe-item:hover,
.recipe-item:focus-within {
  padding-right: 146px;
}

.recipe-item-main {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.recipe-icon {
  color: #c586c0;
  width: 20px;
  flex: 0 0 auto;
  display: inline-flex;
  justify-content: center;
  font-size: 1rem;
}

.recipe-title-wrap {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.recipe-name {
  font-weight: 500;
  color: #f2f7fb;
  font-size: 0.8rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.recipe-command {
  font-size: 0.71rem;
  line-height: 1.3;
  color: #9cdcfe;
  opacity: 0.9;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

.recipe-action-btn {
  background-color: rgba(20, 28, 36, 0.96);
}

@media (max-width: 480px) {
  .field-grid,
  .option-grid,
  .platform-grid {
    grid-template-columns: 1fr;
  }

  .action-row {
    flex-direction: column;
  }

  .action-row > button,
  .stack-actions > button,
  .stack-actions > select,
  .stack-actions > input {
    width: 100%;
  }

  .compact-option-tile {
    flex: 1 1 0;
  }

  .compact-entry-row {
    grid-template-columns: 14px minmax(0, 1fr);
    align-items: start;
  }

  .entry-tag {
    grid-column: 2;
    justify-self: start;
    margin-top: 2px;
  }

  .terminal-env-item,
  .recipe-item {
    padding-right: 10px;
  }

  .item-actions {
    justify-content: flex-start;
  }

  .terminal-env-item-actions,
  .recipe-item-actions {
    position: static;
    transform: none;
    visibility: visible;
    opacity: 1;
    pointer-events: auto;
  }

  .terminal-env-item,
  .recipe-item-main {
    align-items: flex-start;
  }
}

@media (max-width: 320px) {
  .recipes-tab-panel {
    padding: 8px;
  }
}
</style>
