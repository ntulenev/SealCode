import { token } from './admin-token.js';

/** Creates the snippet editor with explicit browser and Markdown dependencies. */
/** @param {{document: Document, window: Window, fetch: typeof fetch, confirm: (message: string) => boolean, renderMarkdown: (markdown: string, language: string, target: HTMLElement, isCurrent: () => boolean) => Promise<string | null>}} options */
export function createSnippetEditor({ document, window, fetch, confirm, renderMarkdown }) {
  /** @typedef {import('./snippets-main.js').Category} Category */
  const category = /** @type {HTMLSelectElement} */ (document.getElementById('editCategory'));
  const question = /** @type {HTMLSelectElement} */ (document.getElementById('editQuestion'));
  const topic = /** @type {HTMLInputElement} */ (document.getElementById('editTopic'));
  const description = /** @type {HTMLTextAreaElement} */ (document.getElementById('editDescription'));
  const form = /** @type {HTMLFormElement} */ (document.getElementById('snippetEditForm'));
  const categoryForm = /** @type {HTMLFormElement} */ (document.getElementById('categoryEditForm'));
  const categoryKey = /** @type {HTMLInputElement} */ (document.getElementById('categoryKey'));
  const categoryTitle = /** @type {HTMLInputElement} */ (document.getElementById('categoryTitle'));
  const categoryOrder = /** @type {HTMLInputElement} */ (document.getElementById('categorySortOrder'));
  const questionCategory = /** @type {HTMLSelectElement} */ (document.getElementById('editQuestionCategory'));
  const questionNumber = /** @type {HTMLInputElement} */ (document.getElementById('editQuestionNumber'));
  const newQuestion = /** @type {HTMLButtonElement} */ (document.getElementById('newQuestion'));
  const deleteQuestion = /** @type {HTMLButtonElement} */ (document.getElementById('deleteQuestion'));
  const deleteCategory = /** @type {HTMLButtonElement} */ (document.getElementById('deleteCategory'));
  const steps = document.getElementById('editSteps');
  const preview = document.getElementById('editPreview');
  const status = document.getElementById('editStatus');
  const publish = /** @type {HTMLButtonElement} */ (document.getElementById('publishSnippets'));
  const review = /** @type {HTMLButtonElement} */ (document.getElementById('previewSnippets'));
  /** @type {Category[]} */
  let categories = [];
  let version = '';
  let dirty = false;
  let busy = false;
  let revision = 0;
  let isNew = false;
  let categoryDirty = false;
  /** @type {string | null} */
  let originalCategory = null;

  function changed() {
    dirty = true;
    ++revision;
    publish.disabled = true;
    preview.hidden = true;
    status.textContent = 'Unpublished changes. Preview your revision before publishing.';
  }
  function canDiscard() { return !(dirty || categoryDirty) || confirm('Discard unpublished changes?'); }
  function selectedCategory() { return categories.find(item => item.Language === category.value); }
  function selectedQuestion() { return selectedCategory()?.Questions.find(item => item.Number === Number(question.value)); }

  function addStep(title = '', markdown = '') {
    const section = document.createElement('section');
    section.className = 'snippet-edit-step';
    const label = document.createElement('label');
    label.textContent = 'Example title';
    const input = document.createElement('input');
    input.value = title;
    input.required = true;
    input.maxLength = 1000;
    label.append(input);
    const sourceLabel = document.createElement('label');
    sourceLabel.textContent = 'Markdown source';
    const source = document.createElement('textarea');
    source.value = markdown;
    source.required = true;
    source.maxLength = 500000;
    source.spellcheck = false;
    source.rows = 14;
    sourceLabel.append(source);
    const actions = document.createElement('div');
    actions.className = 'snippet-edit-actions';
    for (const [text, action] of /** @type {[string, () => void][]} */ ([
      ['Move up', () => { if (section.previousElementSibling) steps.insertBefore(section, section.previousElementSibling); }],
      ['Move down', () => { if (section.nextElementSibling) steps.insertBefore(section.nextElementSibling, section); }],
      ['Remove example', () => { if (steps.children.length > 1) section.remove(); }]
    ])) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'ghost';
      button.textContent = text;
      button.onclick = () => { action(); changed(); };
      actions.append(button);
    }
    section.append(label, sourceLabel, actions);
    steps.append(section);
  }
  function showQuestion() {
    const item = selectedQuestion();
    isNew = false;
    deleteQuestion.disabled = !item;
    form.hidden = !item;
    steps.replaceChildren();
    preview.hidden = true;
    ++revision;
    dirty = false;
    publish.disabled = true;
    if (!item) return;
    questionCategory.value = category.value;
    questionNumber.value = String(item.Number);
    topic.value = item.Topic;
    description.value = item.Description;
    for (const step of item.Steps) addStep(step.Title, step.Markdown);
    status.textContent = 'Edit the source, then preview before publishing.';
  }
  /** @param {string} text @param {string} value */
  function option(text, value) {
    const item = document.createElement('option');
    item.textContent = text;
    item.value = value;
    return item;
  }
  function showCategory() {
    const item = selectedCategory();
    originalCategory = item?.Language ?? null;
    categoryKey.value = item?.Language ?? '';
    categoryTitle.value = item?.Title ?? '';
    categoryOrder.value = String(item?.SortOrder ?? 0);
    categoryDirty = false;
    newQuestion.disabled = !item;
    deleteCategory.disabled = !item;
    question.replaceChildren();
    for (const item of selectedCategory()?.Questions ?? []) question.add(option(`${item.Number}. ${item.Topic}`, String(item.Number)));
    question.disabled = !question.options.length;
    showQuestion();
  }
  async function load(preferredLanguage = category.value, preferredNumber = question.value) {
    const current = revision;
    const response = await fetch('/admin/snippets/manage/catalog', { cache: 'no-store' });
    if (!response.ok || response.redirected) throw new Error('Library unavailable. A super-admin session and PostgreSQL snippets are required.');
    const snapshot = await response.json();
    if (current !== revision) return;
    categories = snapshot.Categories;
    version = snapshot.Version;
    const previousCategory = preferredLanguage;
    const previousQuestion = preferredNumber;
    category.replaceChildren();
    questionCategory.replaceChildren();
    for (const item of categories) {
      category.add(option(item.Title, item.Language));
      questionCategory.add(option(item.Title, item.Language));
    }
    category.disabled = !categories.length;
    if (categories.some(item => item.Language === previousCategory)) category.value = previousCategory;
    showCategory();
    if ([...question.options].some(item => item.value === previousQuestion)) {
      question.value = previousQuestion;
      showQuestion();
    }
    rememberSelection();
    if (!categories.length) status.textContent = 'The database library is empty.';
  }
  category.onchange = () => {
    if (busy || !canDiscard()) { category.value = category.dataset.previous || ''; return; }
    category.dataset.previous = category.value;
    showCategory();
    question.dataset.previous = question.value;
  };
  question.onchange = () => {
    if (busy || !canDiscard()) { question.value = question.dataset.previous || ''; return; }
    showQuestion();
    question.dataset.previous = question.value;
  };
  form.addEventListener('input', changed);
  document.getElementById('addExample').onclick = () => {
    if (steps.children.length >= 100) return;
    addStep(); changed();
  };
  function draft() {
    /** @type {{ExpectedVersion: string, Topic: string, Description: string, Steps: {Title: string, Markdown: string}[], TargetLanguage?: string, TargetNumber?: number}} */
    const payload = {
      ExpectedVersion: version, Topic: topic.value, Description: description.value,
      Steps: [...steps.querySelectorAll('section')].map(section => ({
        Title: section.querySelector('input').value, Markdown: section.querySelector('textarea').value
      }))
    };
    if (!isNew && questionCategory.value !== category.value) payload['TargetLanguage'] = questionCategory.value;
    if (!isNew && Number(questionNumber.value) !== Number(question.value)) payload['TargetNumber'] = Number(questionNumber.value);
    return payload;
  }
  review.onclick = async () => {
    if (!form.reportValidity()) return;
    const current = ++revision;
    publish.disabled = true;
    preview.replaceChildren();
    preview.hidden = false;
    const heading = document.createElement('h2');
    heading.textContent = topic.value;
    const summary = document.createElement('p');
    summary.textContent = description.value;
    preview.append(heading, summary);
    try {
      for (const [index, step] of draft().Steps.entries()) {
        const heading = document.createElement('h3');
        heading.textContent = `${index + 1}. ${step.Title}`;
        preview.append(heading);
        await renderMarkdown(step.Markdown, category.value, preview, () => current === revision);
        if (current !== revision) return;
      }
      publish.disabled = false;
      status.textContent = 'Preview ready. Publish to update the library and application cache immediately.';
    } catch { status.textContent = 'Preview failed. Try again.'; }
  };
  async function mutate(/** @type {string} */ url, /** @type {object} */ payload,
    /** @type {string} */ message, preferredLanguage = category.value, preferredNumber = question.value) {
    busy = true;
    const controls = /** @type {(HTMLInputElement | HTMLSelectElement | HTMLButtonElement)[]} */ ([
      ...form.querySelectorAll('input, textarea, button, select'),
      ...categoryForm.querySelectorAll('input, button'), category, question, newQuestion, deleteQuestion
    ]);
    const previous = controls.map(control => control.disabled);
    for (const control of controls) control.disabled = true;
    try {
      const response = await fetch(url, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await token(fetch) },
        body: JSON.stringify(payload)
      });
      if (response.status === 409) throw new Error('Another editor changed the library, the source was deleted or the destination is occupied. Your draft remains here. Copy it before reloading.');
      if (!response.ok || response.redirected) throw new Error('Publication could not be confirmed. Your draft remains here; reload the library to check the saved version.');
      dirty = false;
      categoryDirty = false;
      await load(preferredLanguage, preferredNumber);
      status.textContent = message;
    } catch (error) { status.textContent = error.message; }
    finally {
      busy = false;
      controls.forEach((control, index) => { control.disabled = previous[index]; });
      category.disabled = !categories.length;
      question.disabled = !question.options.length;
      newQuestion.disabled = !selectedCategory();
      deleteQuestion.disabled = isNew || !selectedQuestion();
      deleteCategory.disabled = originalCategory === null;
      publish.disabled = true;
    }
  }
  form.onsubmit = async (/** @type {SubmitEvent} */ event) => {
    event.preventDefault();
    if (busy || publish.disabled || !form.reportValidity()) return;
    if (categoryDirty && !confirm('Discard unpublished category metadata and publish this question?')) return;
    const destination = questionCategory.value;
    const number = questionNumber.value;
    const path = isNew ? `/questions/${encodeURIComponent(destination)}/${number}`
      : `/${encodeURIComponent(category.value)}/${question.value}`;
    await mutate(`/admin/snippets/manage${path}`, draft(),
      'Published. The application cache is updated.', destination, number);
  };
  categoryForm.addEventListener('input', () => {
    categoryDirty = true;
    status.textContent = 'Unpublished category metadata. Save category to apply these changes.';
  });
  categoryForm.onsubmit = async (/** @type {SubmitEvent} */ event) => {
    event.preventDefault();
    if (busy || !categoryForm.reportValidity()) return;
    if (dirty && !confirm('Discard unpublished question changes and save this category?')) return;
    const destination = categoryKey.value;
    await mutate('/admin/snippets/manage/categories', {
      ExpectedVersion: version, OriginalLanguage: originalCategory, Language: destination,
      Title: categoryTitle.value, SortOrder: Number(categoryOrder.value)
    }, 'Category saved. The application cache is updated.', destination);
  };
  document.getElementById('newCategory').onclick = () => {
    if (busy || !canDiscard()) return;
    showCategory();
    originalCategory = null;
    categoryKey.value = '';
    categoryTitle.value = '';
    categoryOrder.value = '0';
    categoryDirty = true;
    deleteCategory.disabled = true;
    status.textContent = 'Enter a new category key, title and display order, then save category.';
    categoryKey.focus();
  };
  deleteCategory.onclick = async () => {
    if (busy || originalCategory === null) return;
    if (!confirm(`Delete category "${selectedCategory().Title}" (${originalCategory}) and ALL ${selectedCategory().Questions.length} questions and their examples? Unpublished changes will be discarded.`)) return;
    await mutate(`/admin/snippets/manage/categories/${encodeURIComponent(originalCategory)}/delete`,
      { ExpectedVersion: version }, 'Category and its questions deleted. The application cache is updated.');
  };
  newQuestion.onclick = () => {
    if (busy || !selectedCategory() || !canDiscard()) return;
    showCategory();
    isNew = true;
    form.hidden = false;
    questionCategory.value = category.value;
    questionNumber.value = String(Math.max(0, ...selectedCategory().Questions.map(item => item.Number)) + 1);
    topic.value = '';
    description.value = '';
    steps.replaceChildren();
    addStep();
    deleteQuestion.disabled = true;
    changed();
    topic.focus();
  };
  deleteQuestion.onclick = async () => {
    if (busy || isNew || !selectedQuestion()) return;
    if (!confirm(`Delete question ${question.value}: ${selectedQuestion().Topic}, and all its examples? Unpublished changes will be discarded.`)) return;
    await mutate(`/admin/snippets/manage/questions/${encodeURIComponent(category.value)}/${question.value}/delete`,
      { ExpectedVersion: version }, 'Question deleted. The application cache is updated.');
  };
  document.getElementById('reloadSnippets').onclick = () => {
    if (!busy && canDiscard()) return load().catch(error => { status.textContent = error.message; });
  };
  function rememberSelection() {
    category.dataset.previous = category.value;
    question.dataset.previous = question.value;
  }
  window.addEventListener('beforeunload', event => { if (dirty || categoryDirty) { event.preventDefault(); event.returnValue = ''; } });
  return { ready: load().catch(error => { status.textContent = error.message; }) };
}
