/** Populates a language selector using text nodes. */
/** @param {Document} document
 * @param {HTMLSelectElement} selectEl
 * @param {unknown} languages
 * @param {string} [selected] */
export function setLanguageOptions(document, selectEl, languages, selected = undefined) {
  const cleaned = Array.isArray(languages)
    ? languages
        .map((language) => (typeof language === 'string' ? language.trim() : ''))
        .filter((language) => language)
    : [];
  const unique = [...new Set(cleaned)];

  selectEl.innerHTML = '';
  if (unique.length === 0) {
    const option = document.createElement('option');
    option.value = '';
    option.textContent = 'No languages configured';
    option.disabled = true;
    option.selected = true;
    selectEl.appendChild(option);
    selectEl.disabled = true;
    return;
  }

  for (const language of unique) {
    const option = document.createElement('option');
    option.value = language;
    option.textContent = language;
    selectEl.appendChild(option);
  }

  selectEl.disabled = false;
  if (selected && unique.includes(selected)) {
    selectEl.value = selected;
  } else {
    selectEl.selectedIndex = 0;
  }
}
