<template>
  <div class="code-editor" data-testid="file-editor-code">
    <div ref="gutterRef" class="code-editor-gutter" aria-hidden="true">
      <div
        v-for="lineNumber in lineNumbers"
        :key="lineNumber"
        class="code-editor-line-number"
      >
        {{ lineNumber }}
      </div>
    </div>
    <textarea
      ref="textareaRef"
      :value="modelValue"
      :readonly="readOnly"
      class="code-editor-textarea"
      spellcheck="false"
      @input="onInput"
      @keydown="onKeydown"
      @scroll="syncScroll"
    />
  </div>
</template>

<script setup>
import { computed, ref } from 'vue';

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

const gutterRef = ref(null);
const textareaRef = ref(null);

const lineNumbers = computed(() => {
  const total = Math.max(1, String(props.modelValue ?? '').split('\n').length);
  return Array.from({ length: total }, (_, index) => index + 1);
});

function onInput(event) {
  if (props.readOnly) {
    return;
  }
  emit('update:modelValue', event?.target?.value ?? '');
  syncScroll();
}

function onKeydown(event) {
  if (props.readOnly) {
    return;
  }
  if ((event.metaKey || event.ctrlKey) && String(event.key || '').toLowerCase() === 's') {
    event.preventDefault();
    emit('save');
  }
}

function syncScroll() {
  if (!gutterRef.value || !textareaRef.value) {
    return;
  }
  gutterRef.value.scrollTop = textareaRef.value.scrollTop;
}
</script>

<style scoped>
.code-editor {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  background: #111826;
}

.code-editor-gutter {
  min-width: 56px;
  max-width: 72px;
  overflow: hidden;
  padding: 12px 10px 12px 12px;
  border-right: 1px solid #2f4257;
  background: #172231;
  color: #6f87a0;
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.84rem;
  line-height: 1.45;
  text-align: right;
  user-select: none;
}

.code-editor-line-number {
  white-space: pre;
}

.code-editor-textarea {
  width: 100%;
  height: 100%;
  min-height: 0;
  border: none;
  outline: none;
  resize: none;
  overflow: auto;
  background: transparent;
  color: #edf2f7;
  padding: 12px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.84rem;
  line-height: 1.45;
  tab-size: 2;
}

.code-editor-textarea[readonly] {
  cursor: default;
}
</style>
