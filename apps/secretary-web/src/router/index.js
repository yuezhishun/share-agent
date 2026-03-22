import { createRouter, createWebHistory } from 'vue-router';

const DesktopTerminalViewV4 = () => import('../views/DesktopTerminalViewV4.vue');
const ProcessesView = () => import('../views/ProcessesView.vue');

export default createRouter({
  history: createWebHistory(String(import.meta.env.VITE_APP_BASE_PATH || '/').trim() || '/'),
  routes: [
    { path: '/', component: DesktopTerminalViewV4 },
    { path: '/proc', component: ProcessesView },
  ]
});
