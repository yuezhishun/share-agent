<template>
  <div ref="hostRef" class="markdown-editor-host" data-testid="file-editor-markdown" />
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import Vditor from 'vditor';
import 'vditor/dist/index.css';

const props = defineProps({
  modelValue: {
    type: String,
    default: ''
  },
  readOnly: {
    type: Boolean,
    default: false
  }
});

const emit = defineEmits(['update:modelValue', 'save']);

const hostRef = ref(null);

let editor = null;
let syncingFromProps = false;

onMounted(() => {
  editor = new Vditor(hostRef.value, {
    mode: 'ir',
    height: '100%',
    width: '100%',
    cache: {
      enable: false
    },
    counter: {
      enable: false
    },
    toolbarConfig: {
      pin: false
    },
    toolbar: [
      'headings',
      'bold',
      'italic',
      'strike',
      '|',
      'list',
      'ordered-list',
      'check',
      '|',
      'quote',
      'line',
      'code',
      'inline-code',
      '|',
      'link',
      'table',
      '|',
      'undo',
      'redo'
    ],
    preview: {
      markdown: {
        toc: true
      }
    },
    input(value) {
      if (syncingFromProps) {
        return;
      }
      emit('update:modelValue', value);
    },
    after() {
      editor?.setValue(String(props.modelValue ?? ''));
      updateReadOnly(props.readOnly);
      bindSaveShortcut();
    }
  });
});

watch(() => props.modelValue, (value) => {
  if (!editor) {
    return;
  }
  const nextValue = String(value ?? '');
  if (editor.getValue() === nextValue) {
    return;
  }
  syncingFromProps = true;
  editor.setValue(nextValue, true);
  syncingFromProps = false;
});

watch(() => props.readOnly, (value) => {
  updateReadOnly(value);
});

onBeforeUnmount(() => {
  editor?.destroy?.();
  editor = null;
});

function updateReadOnly(readOnly) {
  const element = hostRef.value?.querySelector?.('.vditor-ir textarea');
  if (element) {
    element.readOnly = readOnly === true;
  }
}

function bindSaveShortcut() {
  const element = hostRef.value?.querySelector?.('.vditor-ir textarea');
  if (!element) {
    return;
  }
  element.addEventListener('keydown', (event) => {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      emit('save');
    }
  });
}
</script>

<style scoped>
.markdown-editor-host {
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

.markdown-editor-host :deep(.vditor) {
  height: 100%;
  border: none;
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
}

.markdown-editor-host :deep(.vditor-toolbar) {
  border-bottom: 1px solid #333;
  flex: 0 0 auto;
}

.markdown-editor-host :deep(.vditor-content) {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  overflow: hidden;
}

.markdown-editor-host :deep(.vditor-ir) {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  overflow: auto;
  background: #1e1e1e;
  color: #ddd;
  scrollbar-width: none;
  -ms-overflow-style: none;
}

.markdown-editor-host :deep(.vditor-ir::-webkit-scrollbar) {
  display: none;
}

.markdown-editor-host :deep(.vditor-ir pre.vditor-reset) {
  min-height: 100%;
  overflow-x: hidden;
  white-space: pre-wrap;
  word-break: break-word;
}

.markdown-editor-host :deep(.vditor-ir .vditor-reset) {
  max-width: 100%;
}

.markdown-editor-host :deep(.vditor-ir .vditor-reset pre) {
  max-width: 100%;
  overflow-x: hidden;
}

.markdown-editor-host :deep(.vditor-ir .vditor-reset pre > code) {
  max-width: 100%;
  overflow-x: hidden;
  white-space: pre-wrap;
  word-break: break-word;
  word-wrap: break-word;
}
</style>
