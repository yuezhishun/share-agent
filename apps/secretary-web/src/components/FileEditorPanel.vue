<template>
  <div class="editor-viewport">
    <div class="editor-toolbar">
      <div class="editor-meta">
        <span class="editor-path">{{ activeFileTab.path }}</span>
        <span v-if="isMarkdown" class="editor-kind-badge">Markdown IR</span>
      </div>
      <div class="editor-actions">
        <span v-if="activeFileTab.readOnly" class="read-only-badge">只读</span>
        <span v-if="activeFileTab.dirty" class="dirty-indicator">未保存</span>
        <button type="button" @click="emit('reload')" :disabled="activeFileTab.loading">重载</button>
        <button v-if="!activeFileTab.readOnly" type="button" class="primary" @click="emit('save')" :disabled="activeFileTab.loading">保存</button>
      </div>
    </div>

    <div v-if="activeFileTab.error" class="panel-error">{{ activeFileTab.error }}</div>
    <div v-else-if="activeFileTab.loading" class="editor-loading">加载中...</div>
    <template v-else>
      <div v-if="showProgressiveActions" class="editor-warning editor-warning-actions">
        <span>{{ progressiveMessage }}</span>
        <div class="warning-actions">
          <button v-if="activeFileTab.hasMoreAfter" type="button" @click="emit('loadMore')">加载更多</button>
          <button v-if="activeFileTab.largeFile" type="button" @click="emit('tailPreview')">查看尾部</button>
          <button v-if="activeFileTab.cursorStart > 0 || activeFileTab.mode === 'tail'" type="button" @click="emit('loadFromStart')">回到开头</button>
        </div>
      </div>
      <div v-else-if="activeFileTab.truncated" class="editor-warning">文件内容已截断展示（{{ activeFileTab.truncateReason || 'max_lines' }}）</div>
      <MarkdownIrEditor
        v-if="isMarkdown"
        :model-value="activeFileTab.content"
        :read-only="activeFileTab.readOnly"
        @update:model-value="emit('update:modelValue', $event)"
        @save="emit('save')"
      />
      <PlainTextCodeEditor
        v-else
        :model-value="activeFileTab.content"
        :read-only="activeFileTab.readOnly"
        @update:model-value="emit('update:modelValue', $event)"
        @save="emit('save')"
      />
    </template>
  </div>
</template>

<script setup>
import { computed, defineAsyncComponent } from 'vue';
import PlainTextCodeEditor from './PlainTextCodeEditor.vue';

const MarkdownIrEditor = defineAsyncComponent(() => import('./MarkdownIrEditor.vue'));

const props = defineProps({
  activeFileTab: {
    type: Object,
    required: true
  }
});

const emit = defineEmits(['reload', 'save', 'update:modelValue', 'loadMore', 'tailPreview', 'loadFromStart']);

const isMarkdown = computed(() => props.activeFileTab?.editorKind === 'markdown-ir');
const showProgressiveActions = computed(() => props.activeFileTab?.largeFile === true || props.activeFileTab?.readOnly === true);
const progressiveMessage = computed(() => {
  const tab = props.activeFileTab || {};
  const loadedLines = Number(tab.loadedLines || tab.lines_shown || 0);
  if (tab.mode === 'tail') {
    return `大文件尾部只读预览，当前展示 ${loadedLines} 行。`;
  }
  return `大文件只读预览，当前已加载 ${loadedLines} 行，可继续按需加载。`;
});
</script>

<style scoped>
.editor-viewport {
  background-color: #111826;
  border-radius: 6px;
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid #2f4257;
}

.editor-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  border-bottom: 1px solid #2f4257;
  background-color: #1a2735;
}

.editor-meta {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.editor-path {
  color: #9cdcfe;
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.78rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.editor-kind-badge {
  flex: 0 0 auto;
  border: 1px solid #40617e;
  border-radius: 999px;
  padding: 2px 8px;
  color: #cfe8ff;
  font-size: 0.72rem;
  background: #203447;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.editor-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.editor-actions button {
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

.editor-actions button:hover {
  background-color: #4a4a4a;
}

.editor-actions button.primary {
  background-color: #0e639c;
}

.editor-actions button.primary:hover {
  background-color: #1177bb;
}

.dirty-indicator {
  color: #f0a64a;
  font-size: 0.75rem;
}

.read-only-badge {
  color: #9cdcfe;
  font-size: 0.75rem;
}

.editor-loading {
  padding: 16px;
  color: #b8c7d9;
  font-size: 0.86rem;
}

.editor-warning {
  padding: 8px 10px;
  color: #f0a64a;
  font-size: 0.78rem;
  border-bottom: 1px solid #2f4257;
  background-color: #1f2c3a;
}

.editor-warning-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
}

.warning-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.warning-actions button {
  width: auto;
  background-color: #2d3a4a;
  border: 1px solid #3f4e60;
  color: #d9e7f6;
  font-size: 0.78rem;
  padding: 4px 10px;
  border-radius: 999px;
  cursor: pointer;
}

.warning-actions button:hover {
  background-color: #39506a;
}

.panel-error {
  color: #ef6a62;
  font-size: 12px;
  padding: 8px 12px;
}
</style>
