import { createAdminPage } from './admin.js';
createAdminPage({ document, window, fetch, confirm, clipboard: navigator.clipboard });
