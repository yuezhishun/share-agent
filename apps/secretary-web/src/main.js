import { createApp } from 'vue';
import { createPinia } from 'pinia';
import router from './router/index.js';
import App from './App.vue';
import '@fortawesome/fontawesome-free/css/all.min.css';
import './css2.css';
import './styles.css';

const app = createApp(App);
app.use(createPinia());
app.use(router);
app.mount('#app');
