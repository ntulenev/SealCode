export async function token() {
  const response = await fetch('/admin/token');
  if (!response.ok) throw new Error('Could not load security token');
  return (await response.json()).Token;
}
for (const form of document.querySelectorAll('form[action^="/admin/"]')) {
  form.addEventListener('submit', async event => {
    if (form.dataset.secured) return;
    event.preventDefault();
    const input = document.createElement('input');
    input.type = 'hidden'; input.name = '__RequestVerificationToken'; input.value = await token();
    form.appendChild(input); form.dataset.secured = 'true'; form.requestSubmit();
  });
}
