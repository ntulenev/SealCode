export async function token(fetchToken = fetch) {
  const response = await fetchToken('/admin/token');
  if (!response.ok || response.redirected) {
    throw new Error(
      response.status === 401 || response.redirected
        ? 'Session expired. Sign in again.'
        : 'Request failed. Try again.'
    );
  }
  return (await response.json()).Token;
}
/** Secures login and logout forms and reports request failures. */
/** @param {Document} document
 * @param {typeof fetch} [fetchToken] */
export function registerAdminForms(document, fetchToken = fetch) {
  for (const form of /** @type {NodeListOf<HTMLFormElement>} */ (
    document.querySelectorAll('form[action^="/admin/"]')
  )) {
    form.addEventListener('submit', async (event) => {
      if (form.dataset.secured) return;
      event.preventDefault();
      try {
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = '__RequestVerificationToken';
        input.value = await token(fetchToken);
        form.appendChild(input);
        form.dataset.secured = 'true';
        form.requestSubmit();
      } catch (error) {
        const result =
          document.getElementById('createResult') || document.getElementById('error');
        if (result) {
          result.textContent =
            error?.message === 'Session expired. Sign in again.'
              ? error.message
              : 'Request failed. Try again.';
          result.classList.remove('hidden');
        }
      }
    });
  }
}
