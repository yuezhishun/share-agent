import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ConfigProvider } from '@arco-design/web-react';
import '@arco-design/web-react/es/_util/react-19-adapter';
import '@arco-design/web-react/dist/css/arco.css';
import App from './web/App';
import './styles.css';

const root = createRoot(document.getElementById('root')!);

root.render(
  <React.StrictMode>
    <ConfigProvider theme={{ primaryColor: '#0f766e' }}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ConfigProvider>
  </React.StrictMode>
);
