import { defineStore } from 'pinia';

const API_BASE = import.meta.env.VITE_API_BASE || '';

async function safeJson(url, init) {
  const res = await fetch(url, init);
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return await res.json();
}

export const useTaskStore = defineStore('task', {
  state: () => ({
    tasks: [],
    selectedTaskId: '',
    timeline: [],
    report: null,
    sessions: [],
    error: ''
  }),
  actions: {
    async createTask(payload) {
      try {
        const body = await safeJson(`${API_BASE}/api/tasks`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(payload)
        });
        const task = body.task;
        this.tasks.unshift(task);
        this.selectedTaskId = task.taskId;
        this.error = '';
        return task;
      } catch (err) {
        this.error = `createTask failed: ${String(err?.message || err)}`;
        throw err;
      }
    },

    async loadTasks(limit = 50) {
      try {
        this.tasks = await safeJson(`${API_BASE}/api/tasks?limit=${encodeURIComponent(limit)}`);
        if (!this.selectedTaskId && this.tasks.length > 0) {
          this.selectedTaskId = this.tasks[0].taskId;
        }
        this.error = '';
      } catch (err) {
        this.error = `loadTasks failed: ${String(err?.message || err)}`;
      }
    },

    async loadTimeline(taskId) {
      if (!taskId) return;
      try {
        this.timeline = await safeJson(`${API_BASE}/api/tasks/${taskId}/timeline`);
        this.error = '';
      } catch (err) {
        this.error = `loadTimeline failed: ${String(err?.message || err)}`;
      }
    },

    async loadSessions(taskId) {
      if (!taskId) {
        this.sessions = [];
        return;
      }
      try {
        this.sessions = await safeJson(`${API_BASE}/api/tasks/${taskId}/sessions`);
        this.error = '';
      } catch (err) {
        this.error = `loadSessions failed: ${String(err?.message || err)}`;
      }
    },

    async loadReport() {
      try {
        this.report = await safeJson(`${API_BASE}/api/reports/progress`);
        this.error = '';
      } catch (err) {
        this.error = `loadReport failed: ${String(err?.message || err)}`;
      }
    }
  }
});
