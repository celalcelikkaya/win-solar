import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './index.css';

console.info('%c☀ Win Solar %c— developed by Celal Çelikkaya ☾', 'font-weight:700;color:#ff9a3c', 'color:#7aa2ff');

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
