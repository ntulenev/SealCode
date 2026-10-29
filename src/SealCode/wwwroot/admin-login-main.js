import { token } from './admin-token.js';

/** @typedef {{ accounts: { id: {
 *   initialize(options: { client_id: string, callback(result: { credential: string }): void }): void,
 *   renderButton(element: HTMLElement, options: { theme: string, size: string, width: number }): void
 * } } }} GoogleApi */

if (new URLSearchParams(window.location.search).get('error'))
  document.getElementById('error').classList.remove('hidden');

const googleSection = document.getElementById('google-login');
const googleError = document.getElementById('google-error');

async function startGoogleSignIn() {
  const configuration = await fetch('/admin/google-config');
  if (!configuration.ok) return;
  const { ClientId } = await configuration.json();
  if (!ClientId) return;

  const script = document.createElement('script');
  script.src = 'https://accounts.google.com/gsi/client';
  script.async = true;
  script.onload = () => {
    const google = /** @type {Window & { google: GoogleApi }} */ (/** @type {unknown} */ (window)).google;
    google.accounts.id.initialize({
      client_id: ClientId,
      callback: async ({ credential }) => {
        googleError.classList.add('hidden');
        try {
          const response = await fetch('/admin/google-login', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'X-CSRF-TOKEN': await token()
            },
            body: JSON.stringify({ Credential: credential })
          });
          if (!response.ok) throw new Error('Google sign-in failed.');
          window.location.assign('/admin');
        } catch {
          googleError.classList.remove('hidden');
        }
      }
    });
    google.accounts.id.renderButton(
      /** @type {HTMLElement} */ (document.getElementById('google-signin')),
      { theme: 'outline', size: 'large', width: 320 }
    );
    googleSection.classList.remove('hidden');
  };
  script.onerror = () => {
    googleError.textContent = 'Google sign-in is unavailable. Try again later.';
    googleError.classList.remove('hidden');
    googleSection.classList.remove('hidden');
  };
  document.head.appendChild(script);
}

startGoogleSignIn().catch(() => {});
