import { createRouter, createWebHistory } from 'vue-router';

const DesktopTerminalView = () => import('../views/DesktopTerminalView.vue');
const DesktopTerminalViewV2 = () => import('../views/DesktopTerminalViewV2.vue');
const DesktopTerminalViewV3 = () => import('../views/DesktopTerminalViewV3.vue');
const DesktopTerminalViewV4 = () => import('../views/DesktopTerminalViewV4.vue');
const ProcessesView = () => import('../views/ProcessesView.vue');
const MobileTerminalView = () => import('../views/MobileTerminalView.vue');
const MobileFilesView = () => import('../views/MobileFilesView.vue');

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: DesktopTerminalViewV3 },
    { path: '/terminal', component: DesktopTerminalView },
    { path: '/terminal-v2', component: DesktopTerminalViewV2 },
    { path: '/terminal-v3', component: DesktopTerminalViewV3 },
    { path: '/terminal-v4', component: DesktopTerminalViewV4 },
    { path: '/proc', component: ProcessesView },
    { path: '/mobile', component: MobileTerminalView },
    { path: '/mobile/files', component: MobileFilesView }
  ]
});
