<template>
  <div class="mobile-page">
    <section class="panel mobile-header">
      <h1>Mobile Files</h1>
      <div class="row">
        <RouterLink class="link-btn" to="/mobile">Terminal</RouterLink>
        <RouterLink class="link-btn" to="/">Desktop</RouterLink>
      </div>
    </section>

    <section class="panel">
      <div class="row">
        <button type="button" :disabled="!filesStore.parentPath || filesStore.loading" @click="goParent">Parent</button>
        <button type="button" :disabled="filesStore.loading" @click="refresh">Refresh</button>
      </div>
      <label class="checkbox-row">
        <input v-model="filesStore.showHidden" type="checkbox" @change="refresh" /> Show hidden
      </label>
      <div class="subtle">{{ filesStore.currentPath }}</div>
      <div v-if="filesStore.error" class="error">{{ filesStore.error }}</div>
      <ul class="files-list">
        <li v-for="item in filesStore.items" :key="item.path">
          <button type="button" @click="open(item)">{{ item.kind }} · {{ item.name }}</button>
        </li>
      </ul>
    </section>

    <section class="panel" v-if="filesStore.preview || filesStore.previewError">
      <div v-if="filesStore.previewError" class="error">{{ filesStore.previewError }}</div>
      <pre v-else class="preview">{{ filesStore.preview.content }}</pre>
    </section>
  </div>
</template>

<script setup>
import { onMounted } from 'vue';
import { useWebCliFilesStore } from '../stores/webcli-files.js';

const filesStore = useWebCliFilesStore();

function goParent() {
  if (!filesStore.parentPath) {
    return;
  }
  filesStore.loadList(filesStore.parentPath);
}

function refresh() {
  filesStore.loadList(filesStore.currentPath);
}

function open(item) {
  filesStore.openEntry(item);
}

onMounted(async () => {
  await filesStore.loadList(filesStore.currentPath);
});
</script>
