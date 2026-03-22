import { createRouter, createWebHistory } from 'vue-router';

const DesktopTerminalView = () => import('../views/DesktopTerminalView.vue');
const ProcessesView = () => import('../views/ProcessesView.vue');

export default createRouter({
  history: createWebHistory(String(import.meta.env.VITE_APP_BASE_PATH || '/').trim() || '/'),
  routes: [
    { path: '/', component: DesktopTerminalView },
    { path: '/proc', component: ProcessesView },
  ]
});
