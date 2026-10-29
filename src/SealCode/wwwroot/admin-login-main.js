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
        let errorMessage = 'Google sign-in failed. Try again.';
        try {
          const response = await fetch('/admin/google-login', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'X-CSRF-TOKEN': await token()
            },
            body: JSON.stringify({ Credential: credential })
          });
          if (response.ok) {
            window.location.assign('/admin');
            return;
          }
          if (response.status === 401)
            errorMessage = 'Google sign-in failed. This account may not belong to an organization allowed to access this site.';
        } catch { /* Network or token request failed. */ }
        googleError.textContent = errorMessage;
        googleError.classList.remove('hidden');
      }
    });
    googleSection.classList.remove('hidden');
    const buttonContainer = /** @type {HTMLElement} */ (document.getElementById('google-signin'));
    google.accounts.id.renderButton(buttonContainer, {
      theme: 'outline',
      size: 'large',
      width: Math.min(400, Math.floor(buttonContainer.getBoundingClientRect().width))
    });
  };
  script.onerror = () => {
    googleError.textContent = 'Google sign-in is unavailable. Try again later.';
    googleError.classList.remove('hidden');
    googleSection.classList.remove('hidden');
  };
  document.head.appendChild(script);
}

startGoogleSignIn().catch(() => {});
