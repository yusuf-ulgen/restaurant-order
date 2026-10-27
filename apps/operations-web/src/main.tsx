import { configureBrowserPush } from '@restaurant-order/ui';
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './index.css';

configureBrowserPush({
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
  appId: import.meta.env.VITE_FIREBASE_APP_ID,
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
  messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
  vapidKey: import.meta.env.VITE_FIREBASE_VAPID_KEY,
});

const rootElement = document.getElementById('root');
if (rootElement) {
  ReactDOM.createRoot(rootElement).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}
