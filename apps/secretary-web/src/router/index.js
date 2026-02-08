import { createRouter, createWebHistory } from 'vue-router';
import TaskBoard from '../views/TaskBoard.vue';
import TerminalWorkspace from '../views/TerminalWorkspace.vue';

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: TaskBoard },
    { path: '/terminal', component: TerminalWorkspace }
  ]
});
