<script setup>
import { computed, onMounted, reactive } from 'vue';
import { useTaskStore } from '../stores/task.js';

const store = useTaskStore();

const form = reactive({
  title: 'Build backend task pipeline',
  intent: 'Please implement and report milestones for backend tasks.',
  cliType: 'codex',
  isComplex: true,
  priority: 3
});

async function submitTask() {
  await store.createTask({ ...form });
  await store.loadTasks();
  await store.loadTimeline(store.selectedTaskId);
  await store.loadSessions(store.selectedTaskId);
  await store.loadReport();
}

async function chooseTask(taskId) {
  store.selectedTaskId = taskId;
  await store.loadTimeline(taskId);
  await store.loadSessions(taskId);
}

const selectedTask = computed(() => store.tasks.find((x) => x.taskId === store.selectedTaskId) || null);

onMounted(async () => {
  await store.loadTasks();
  if (store.selectedTaskId) {
    await store.loadTimeline(store.selectedTaskId);
    await store.loadSessions(store.selectedTaskId);
  }
  await store.loadReport();
});
</script>

<template>
  <div class="grid">
    <section class="panel">
      <h3>创建任务</h3>
      <div class="list">
        <input v-model="form.title" placeholder="任务标题" />
        <textarea v-model="form.intent" rows="6" placeholder="任务意图" />
        <div class="row">
          <input v-model="form.cliType" placeholder="cliType" />
          <input v-model.number="form.priority" type="number" min="1" max="5" placeholder="priority" />
          <select v-model="form.isComplex">
            <option :value="true">复杂任务</option>
            <option :value="false">简单任务</option>
          </select>
        </div>
        <button @click="submitTask">提交任务</button>
      </div>

      <h3>任务列表</h3>
      <div class="list">
        <button
          v-for="task in store.tasks"
          :key="task.taskId"
          class="card"
          @click="chooseTask(task.taskId)"
        >
          <div>{{ task.title }}</div>
          <small>{{ task.status }} · P{{ task.priority }}</small>
          <small class="mono">{{ task.taskId }}</small>
        </button>
      </div>
    </section>

    <section class="panel">
      <h3>任务时间线</h3>
      <pre v-if="store.error" class="mono">{{ store.error }}</pre>
      <div class="list">
        <div v-for="evt in store.timeline" :key="evt.eventId" class="card">
          <div>
            <span class="badge">{{ evt.eventType }}</span>
            <small>{{ evt.severity }}</small>
          </div>
          <pre class="mono">{{ evt.payload }}</pre>
        </div>
      </div>

      <h3>任务会话</h3>
      <div class="row">
        <RouterLink
          v-if="store.selectedTaskId"
          class="card"
          :to="{ path: '/terminal', query: { taskId: store.selectedTaskId } }"
        >
          打开该任务的终端工作台视图
        </RouterLink>
      </div>
      <div class="list">
        <RouterLink
          v-for="session in store.sessions"
          :key="session.sessionId"
          class="card"
          :to="{ path: '/terminal', query: { sessionId: session.sessionId } }"
        >
          <div>{{ session.mode }} · {{ session.status }}</div>
          <small class="mono">{{ session.sessionId }}</small>
        </RouterLink>
        <p v-if="store.sessions.length === 0" class="mono">当前任务暂无会话</p>
      </div>

      <h3>任务详情</h3>
      <div class="list">
        <RouterLink
          v-if="selectedTask?.plannerSessionId"
          class="card"
          :to="{ path: '/terminal', query: { sessionId: selectedTask.plannerSessionId } }"
        >
          打开 Planner 会话
          <small class="mono">{{ selectedTask.plannerSessionId }}</small>
        </RouterLink>
        <RouterLink
          v-if="selectedTask?.executorSessionId"
          class="card"
          :to="{ path: '/terminal', query: { sessionId: selectedTask.executorSessionId } }"
        >
          打开 Executor 会话
          <small class="mono">{{ selectedTask.executorSessionId }}</small>
        </RouterLink>
      </div>
      <pre class="mono">{{ JSON.stringify(selectedTask, null, 2) }}</pre>

      <h3>阶段报告</h3>
      <pre class="mono">{{ JSON.stringify(store.report, null, 2) }}</pre>
    </section>
  </div>
</template>
