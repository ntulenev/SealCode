(() => {
  const form = document.getElementById('joinRoomForm');
  const input = /** @type {HTMLInputElement} */ (document.getElementById('roomLink'));
  const error = document.getElementById('roomLinkError');
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    try {
      const url = new URL(input.value.trim(), window.location.origin);
      if (url.origin !== window.location.origin || !/^\/room\/[A-Za-z0-9_-]{22}\/?$/.test(url.pathname))
        throw new Error('Invalid room link');
      window.location.assign(url.href);
    } catch {
      error.textContent = 'Enter a room link from this SealCode site.';
      error.classList.remove('hidden');
      input.setAttribute('aria-invalid', 'true');
      input.focus();
    }
  });
  input.addEventListener('input', () => {
    error.classList.add('hidden');
    input.removeAttribute('aria-invalid');
  });
})();
