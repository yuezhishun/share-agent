import { createRouter, createWebHistory } from 'vue-router';
import DesktopTerminalView from '../views/DesktopTerminalView.vue';
import MobileTerminalView from '../views/MobileTerminalView.vue';
import MobileFilesView from '../views/MobileFilesView.vue';

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: DesktopTerminalView },
    { path: '/mobile', component: MobileTerminalView },
    { path: '/mobile/files', component: MobileFilesView }
  ]
});
