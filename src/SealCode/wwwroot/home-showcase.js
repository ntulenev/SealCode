(() => {
  const dialog = /** @type {HTMLDialogElement} */ (document.querySelector('.screenshot-dialog'));
  const preview = dialog.querySelector('img');
  for (const link of document.querySelectorAll('a[data-screenshot]')) {
    link.addEventListener('click', (event) => {
      const click = /** @type {MouseEvent} */ (event);
      if (click.ctrlKey || click.metaKey || click.shiftKey || click.altKey) return;
      event.preventDefault();
      const image = link.querySelector('img');
      preview.src = image.src;
      preview.alt = image.alt;
      dialog.showModal();
    });
  }
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) dialog.close();
  });
})();
