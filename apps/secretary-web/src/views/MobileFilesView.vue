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
        <button type="button" :disabled="filesStore.actionLoading" @click="pickUploadFiles">Upload</button>
      </div>
      <div class="row">
        <button type="button" :disabled="filesStore.actionLoading" @click="toggleFolderCreator">
          {{ showFolderCreator ? 'Cancel' : 'New Folder' }}
        </button>
      </div>
      <form v-if="showFolderCreator" class="row" @submit.prevent="createFolder">
        <input v-model="folderName" placeholder="Folder name" />
        <button type="submit">Create</button>
      </form>
      <label class="checkbox-row">
        <input v-model="filesStore.showHidden" type="checkbox" @change="refresh" /> Show hidden
      </label>
      <div class="subtle">{{ filesStore.currentPath }}</div>
      <div v-if="filesStore.error" class="error">{{ filesStore.error }}</div>
      <div v-if="filesStore.actionError" class="error">{{ filesStore.actionError }}</div>
      <ul class="files-list">
        <li v-for="item in filesStore.items" :key="item.path" class="file-item">
          <button type="button" @click="open(item)">{{ item.kind }} · {{ item.name }}</button>
          <div class="row file-actions">
            <button type="button" @click="beginRename(item)">Rename</button>
            <button type="button" :disabled="item.kind !== 'file' && item.kind !== 'dir'" @click="downloadFile(item)">Download</button>
            <button type="button" @click="removeFile(item)">Delete</button>
          </div>
          <form v-if="renamingPath === item.path" class="row" @submit.prevent="saveRename">
            <input v-model="renameValue" />
            <button type="submit">Save</button>
            <button type="button" @click="cancelRename">Cancel</button>
          </form>
        </li>
      </ul>
    </section>

    <section class="panel" v-if="filesStore.preview || filesStore.previewError">
      <div v-if="filesStore.previewError" class="error">{{ filesStore.previewError }}</div>
      <pre v-else class="preview">{{ filesStore.preview.content }}</pre>
    </section>

    <input ref="uploadFilesInputRef" class="hidden-input" type="file" multiple @change="onUploadFilesChange" />
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { useWebCliFilesStore } from '../stores/webcli-files.js';

const filesStore = useWebCliFilesStore();
const uploadFilesInputRef = ref(null);
const showFolderCreator = ref(false);
const folderName = ref('');
const renamingPath = ref('');
const renameValue = ref('');

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

function pickUploadFiles() {
  uploadFilesInputRef.value?.click();
}

async function onUploadFilesChange(event) {
  try {
    await filesStore.uploadFiles(event?.target?.files, filesStore.currentPath);
  } finally {
    if (event?.target) {
      event.target.value = '';
    }
  }
}

function toggleFolderCreator() {
  showFolderCreator.value = !showFolderCreator.value;
  folderName.value = '';
}

async function createFolder() {
  const name = String(folderName.value || '').trim();
  if (!name) {
    return;
  }
  await filesStore.createDirectory(name, filesStore.currentPath);
  folderName.value = '';
  showFolderCreator.value = false;
}

function beginRename(item) {
  renamingPath.value = item.path;
  renameValue.value = item.name;
}

function cancelRename() {
  renamingPath.value = '';
  renameValue.value = '';
}

async function saveRename() {
  if (!renamingPath.value) {
    return;
  }
  await filesStore.renameEntry(renamingPath.value, renameValue.value);
  cancelRename();
}

async function removeFile(item) {
  await filesStore.removeEntry(item.path, {
    recursive: item.kind === 'dir'
  });
}

async function downloadFile(item) {
  if (item.kind !== 'file' && item.kind !== 'dir') {
    return;
  }
  await filesStore.downloadEntry(item.path);
}

onMounted(async () => {
  await filesStore.loadList(filesStore.currentPath);
});
</script>

<style scoped>
.file-item {
  border: 1px solid var(--line);
  border-radius: 8px;
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.file-actions {
  flex-wrap: wrap;
}
</style>
