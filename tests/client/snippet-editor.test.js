import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createSnippetEditor } from '../../src/SealCode/wwwroot/admin-snippets.js';

function element(tag = 'div') {
  return {
    tag, children: [], handlers: {}, dataset: {}, textContent: '', _value: '', disabled: false, hidden: false,
    get value() { return this.tag === 'select' ? (this.children.find(item => item.value === this._value) || this.children[0])?.value || '' : this._value; },
    set value(value) { this._value = value; },
    get options() { return this.children; },
    get previousElementSibling() { return this.parent?.children[this.parent.children.indexOf(this) - 1]; },
    get nextElementSibling() { return this.parent?.children[this.parent.children.indexOf(this) + 1]; },
    append(...items) { for (const item of items) { item.parent = this; this.children.push(item); } },
    add(item) { this.append(item); },
    replaceChildren(...items) { this.children = []; this.append(...items); },
    insertBefore(item, target) { item.remove(); const index = this.children.indexOf(target); this.children.splice(index, 0, item); item.parent = this; },
    remove() { if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1); },
    addEventListener(name, handler) { this.handlers[name] = handler; },
    querySelectorAll(selector) {
      const tags = selector.split(',').map(tag => tag.trim());
      return this.children.flatMap(item => [...(tags.includes(item.tag) ? [item] : []), ...item.querySelectorAll(selector)]);
    },
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; },
    focus() {},
    reportValidity() { return this.valid !== false; }
  };
}
const library = () => ({ Version: 'version-1', Categories: [
  { Language: 'general', Title: 'General', Questions: [
    { Number: 1, Topic: 'First topic', Description: 'Coverage', Steps: [{ Title: 'First', Markdown: 'Original' }] },
    { Number: 2, Topic: 'Second topic', Description: 'Coverage', Steps: [{ Title: 'Second', Markdown: 'Second source' }] }
  ] },
  { Language: 'sql', Title: 'SQL', Questions: [{ Number: 3, Topic: 'SQL topic', Description: 'SQL coverage', Steps: [{ Title: 'Query', Markdown: 'SELECT 1;' }] }] }
] });
async function fixture({ snapshot = library(), get, post, csrf, render, confirm = () => true } = {}) {
  const ids = ['editCategory', 'editQuestion', 'editTopic', 'editDescription', 'snippetEditForm', 'editSteps', 'editPreview', 'editStatus', 'publishSnippets', 'previewSnippets', 'addExample', 'reloadSnippets', 'categoryEditForm', 'categoryKey', 'categoryTitle', 'categorySortOrder', 'editQuestionCategory', 'editQuestionNumber', 'newQuestion', 'deleteQuestion', 'deleteCategory', 'newCategory', 'saveCategory'];
  const elements = Object.fromEntries(ids.map(id => [id, element(id === 'editCategory' || id === 'editQuestion' || id === 'editQuestionCategory' ? 'select' : id === 'editTopic' ? 'input' : id === 'editDescription' ? 'textarea' : id === 'snippetEditForm' ? 'form' : /Snippets|Example/.test(id) ? 'button' : 'div')]));
  elements.snippetEditForm.append(elements.editTopic, elements.editDescription, elements.editSteps,
    elements.publishSnippets, elements.previewSnippets, elements.addExample, elements.editQuestionCategory, elements.editQuestionNumber);
  elements.categoryEditForm.append(elements.categoryKey, elements.categoryTitle, elements.categorySortOrder,
    elements.newCategory, elements.saveCategory, elements.deleteCategory);
  const calls = [];
  const handlers = {};
  const fetch = async (url, options) => {
    calls.push({ url, options });
    if (url === '/admin/token') return csrf ? csrf() : { ok: true, json: async () => ({ Token: 'csrf-token' }) };
    if (options?.method === 'POST') return post ? post(url, options) : { ok: true };
    return get ? get() : { ok: true, json: async () => snapshot };
  };
  const page = createSnippetEditor({
    document: { getElementById: id => elements[id], createElement: element },
    window: { addEventListener: (name, handler) => { handlers[name] = handler; } }, fetch, confirm,
    renderMarkdown: render || (async (source, language, target) => { const text = element('p'); text.textContent = source; target.append(text); return source; })
  });
  await page.ready;
  const input = () => elements.snippetEditForm.handlers.input();
  const review = () => elements.previewSnippets.onclick();
  const submit = () => elements.snippetEditForm.onsubmit({ preventDefault() {} });
  return { elements, calls, handlers, input, review, submit };
}

test('editor loads ordered sources and publishes a reviewed complete revision with CSRF', async () => {
  const { elements: e, calls, review, submit } = await fixture();
  assert.equal(e.snippetEditForm.hidden, false);
  assert.equal(e.editTopic.value, 'First topic');
  assert.equal(e.editSteps.querySelector('textarea').value, 'Original');
  await submit();
  assert.equal(calls.filter(call => call.options?.method === 'POST').length, 0);
  await review();
  assert.equal(e.publishSnippets.disabled, false);
  assert.equal(e.editPreview.hidden, false);
  await submit();
  const request = calls.find(call => call.options?.method === 'POST');
  assert.equal(request.url, '/admin/snippets/manage/general/1');
  assert.equal(request.options.headers['X-CSRF-TOKEN'], 'csrf-token');
  assert.deepEqual(JSON.parse(request.options.body), { ExpectedVersion: 'version-1', Topic: 'First topic', Description: 'Coverage', Steps: [{ Title: 'First', Markdown: 'Original' }] });
  assert.equal(e.editStatus.textContent, 'Published. The application cache is updated.');
  assert.equal(e.publishSnippets.disabled, true);
});

test('example ordering, removal and maximum count determine the publication payload', async () => {
  const { elements: e, review, submit, calls } = await fixture();
  e.addExample.onclick();
  const next = e.editSteps.children[1];
  next.querySelector('input').value = 'Next';
  next.querySelector('textarea').value = 'Next source';
  await next.querySelectorAll('button')[0].onclick();
  assert.equal(e.editSteps.children[0], next);
  await next.querySelectorAll('button')[1].onclick();
  assert.equal(e.editSteps.children[1], next);
  await next.querySelectorAll('button')[2].onclick();
  assert.equal(e.editSteps.children.length, 1);
  await e.editSteps.children[0].querySelectorAll('button')[0].onclick();
  await e.editSteps.children[0].querySelectorAll('button')[1].onclick();
  await e.editSteps.children[0].querySelectorAll('button')[2].onclick();
  assert.equal(e.editSteps.children.length, 1);
  for (let i = 0; i < 101; i++) e.addExample.onclick();
  assert.equal(e.editSteps.children.length, 100);
  for (const section of e.editSteps.children) {
    section.querySelector('input').value ||= 'Additional example';
    section.querySelector('textarea').value ||= 'Additional source';
  }
  await review(); await submit();
  assert.equal(JSON.parse(calls.find(call => call.options?.method === 'POST').options.body).Steps.length, 100);
});

for (const failure of ['conflict', 'server', 'network', 'token', 'redirect']) {
  test(`publication ${failure} preserves the draft and restores controls`, async () => {
    const { elements: e, input, review, submit } = await fixture({
      post: async () => { if (failure === 'network') throw new Error('Network unavailable'); return { ok: failure === 'redirect', redirected: failure === 'redirect', status: failure === 'conflict' ? 409 : 500 }; },
      csrf: failure === 'token' ? async () => ({ ok: false, status: 401 }) : undefined
    });
    e.editTopic.value = 'Local draft'; input(); await review(); await submit();
    assert.equal(e.editTopic.value, 'Local draft');
    assert.equal(e.editCategory.disabled, false);
    assert.equal(e.editTopic.disabled, false);
    assert.equal(e.publishSnippets.disabled, true);
    assert.match(e.editStatus.textContent, failure === 'conflict' ? /Another editor/ : failure === 'token' ? /Session expired/ : failure === 'network' ? /Network unavailable/ : /could not be confirmed/);
  });
}

test('pending publication blocks duplicate submissions, navigation and reload', async () => {
  let finish;
  const { elements: e, review, submit, calls } = await fixture({ post: () => new Promise(resolve => { finish = resolve; }) });
  await review();
  const pending = submit();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(e.editTopic.disabled, true);
  await submit();
  e.editCategory.onchange(); e.editQuestion.onchange();
  e.reloadSnippets.onclick();
  assert.equal(calls.filter(call => call.options?.method === 'POST').length, 1);
  finish({ ok: true }); await pending;
  assert.equal(e.editTopic.disabled, false);
});

test('preview validation, failed rendering and stale async previews cannot enable publication', async () => {
  let finish;
  let fail = false;
  const { elements: e, review, input } = await fixture({ render: async () => {
    if (fail) throw new Error('Highlight failed');
    return new Promise(resolve => { finish = resolve; });
  } });
  e.snippetEditForm.valid = false;
  await review();
  assert.equal(e.publishSnippets.disabled, true);
  e.snippetEditForm.valid = true;
  const pending = review();
  input(); finish('Old source'); await pending;
  assert.equal(e.publishSnippets.disabled, true);
  assert.equal(e.editPreview.hidden, true);
  fail = true; await review();
  assert.equal(e.editStatus.textContent, 'Preview failed. Try again.');
});

test('dirty navigation requires confirmation and protects the draft on reload and unload', async () => {
  let discard = false;
  const { elements: e, input, handlers } = await fixture({ confirm: () => discard });
  let prevented = false;
  handlers.beforeunload({ preventDefault() { prevented = true; } });
  assert.equal(prevented, false);
  e.editTopic.value = 'Draft'; input();
  e.editCategory.value = 'sql'; e.editCategory.onchange();
  assert.equal(e.editCategory.value, 'general');
  e.editQuestion.value = '2'; e.editQuestion.onchange();
  assert.equal(e.editQuestion.value, '1');
  await e.reloadSnippets.onclick();
  assert.equal(e.editTopic.value, 'Draft');
  handlers.beforeunload({ preventDefault() { prevented = true; } });
  assert.equal(prevented, true);
  discard = true;
  e.editQuestion.value = '2'; e.editQuestion.onchange();
  assert.equal(e.editTopic.value, 'Second topic');
  e.editCategory.value = 'sql'; e.editCategory.onchange();
  assert.equal(e.editTopic.value, 'SQL topic');
  await e.reloadSnippets.onclick();
  assert.equal(e.editQuestion.value, '3');
});

test('a delayed reload cannot overwrite edits made while the request was pending', async () => {
  let reads = 0;
  let finish;
  const { elements: e, input } = await fixture({ get: async () => {
    if (++reads === 1) return { ok: true, json: async () => library() };
    return new Promise(resolve => { finish = resolve; });
  } });
  const reload = e.reloadSnippets.onclick();
  e.editTopic.value = 'New draft'; input();
  finish({ ok: true, json: async () => library() }); await reload;
  assert.equal(e.editTopic.value, 'New draft');
  assert.match(e.editStatus.textContent, /Unpublished changes/);
});

for (const state of ['empty', 'noQuestions', 'failure', 'redirect', 'network']) {
  test(`catalog handles ${state}`, async () => {
    const snapshot = library();
    if (state === 'empty') snapshot.Categories = [];
    if (state === 'noQuestions') snapshot.Categories[0].Questions = [];
    const { elements: e } = await fixture({ get: async () => {
      if (state === 'network') throw new Error('Network unavailable');
      return { ok: state !== 'failure', redirected: state === 'redirect', json: async () => snapshot };
    } });
    if (state === 'empty') { assert.equal(e.editCategory.disabled, true); assert.match(e.editStatus.textContent, /empty/); }
    else if (state === 'noQuestions') { assert.equal(e.snippetEditForm.hidden, true); assert.equal(e.editQuestion.disabled, true); }
    else assert.match(e.editStatus.textContent, /unavailable|Network/);
  });
}

test('reload failures report the error while retaining existing fields', async () => {
  let reads = 0;
  const { elements: e } = await fixture({ get: async () => {
    if (++reads > 1) throw new Error('Reload unavailable');
    return { ok: true, json: async () => library() };
  } });
  await e.reloadSnippets.onclick();
  assert.equal(e.editTopic.value, 'First topic');
  assert.equal(e.editStatus.textContent, 'Reload unavailable');
});

const submitEvent = () => ({ preventDefault() {} });

test('category metadata can be created, renamed and reordered without editing the question draft', async () => {
  const snapshot = library();
  const { elements: e, calls } = await fixture({ snapshot, post: async (url, options) => {
    const data = JSON.parse(options.body);
    const metadata = { Language: data.Language, Title: data.Title, SortOrder: data.SortOrder };
    if (data.OriginalLanguage === null) snapshot.Categories.push({ ...metadata, Questions: [] });
    else Object.assign(snapshot.Categories.find(item => item.Language === data.OriginalLanguage), metadata);
    return { ok: true };
  } });
  e.newCategory.onclick();
  e.categoryKey.value = 'javascript'; e.categoryTitle.value = 'JavaScript'; e.categorySortOrder.value = '-5';
  e.categoryEditForm.handlers.input();
  await e.categoryEditForm.onsubmit(submitEvent());
  const data = JSON.parse(calls.find(call => call.url.endsWith('/categories')).options.body);
  assert.deepEqual(data, { ExpectedVersion: 'version-1', OriginalLanguage: null, Language: 'javascript', Title: 'JavaScript', SortOrder: -5 });
  assert.equal(e.editCategory.value, 'javascript');
  assert.equal(e.snippetEditForm.hidden, true);
  assert.equal(e.newQuestion.disabled, false);
  assert.equal(e.categoryTitle.value, 'JavaScript');
  e.categoryKey.value = 'cpp'; e.categoryTitle.value = 'C++'; e.categorySortOrder.value = '15';
  e.categoryEditForm.handlers.input();
  await e.categoryEditForm.onsubmit(submitEvent());
  const renamed = JSON.parse(calls.filter(call => call.url.endsWith('/categories')).at(-1).options.body);
  assert.equal(renamed.OriginalLanguage, 'javascript');
  assert.equal(renamed.Language, 'cpp');
  assert.equal(renamed.SortOrder, 15);
  assert.equal(e.editCategory.value, 'cpp');
});

test('new questions and moves publish to the chosen category and number', async () => {
  const { elements: e, calls, review, submit } = await fixture();
  e.newQuestion.onclick();
  assert.equal(e.editQuestionNumber.value, '3');
  assert.equal(e.deleteQuestion.disabled, true);
  e.editTopic.value = 'New question'; e.editDescription.value = 'Coverage';
  e.editSteps.querySelector('input').value = 'Example'; e.editSteps.querySelector('textarea').value = 'Source';
  await review(); await submit();
  assert.equal(calls.find(call => call.options?.method === 'POST').url, '/admin/snippets/manage/questions/general/3');
  e.editQuestionCategory.value = 'sql'; e.editQuestionNumber.value = '7';
  await review(); await submit();
  const move = calls.filter(call => call.options?.method === 'POST').at(-1);
  assert.equal(move.url, '/admin/snippets/manage/general/1');
  assert.equal(JSON.parse(move.options.body).TargetLanguage, 'sql');
  assert.equal(JSON.parse(move.options.body).TargetNumber, 7);
});

test('deleting a category or question requires confirmation and includes the observed revision', async () => {
  let confirm = false;
  const { elements: e, calls } = await fixture({ confirm: () => confirm });
  await e.deleteCategory.onclick(); await e.deleteQuestion.onclick();
  assert.equal(calls.filter(call => call.options?.method === 'POST').length, 0);
  confirm = true;
  await e.deleteQuestion.onclick();
  assert.equal(calls.find(call => call.options?.method === 'POST').url, '/admin/snippets/manage/questions/general/1/delete');
  await e.deleteCategory.onclick();
  const request = calls.filter(call => call.options?.method === 'POST').at(-1);
  assert.equal(request.url, '/admin/snippets/manage/categories/general/delete');
  assert.deepEqual(JSON.parse(request.options.body), { ExpectedVersion: 'version-1' });
});

test('unsaved category metadata prevents accidental question publication and navigation', async () => {
  const { elements: e, calls, review, submit } = await fixture({ confirm: () => false });
  e.categoryTitle.value = 'Unsaved title'; e.categoryEditForm.handlers.input();
  await review(); await submit();
  e.newCategory.onclick(); e.newQuestion.onclick();
  assert.equal(e.categoryTitle.value, 'Unsaved title');
  assert.equal(calls.filter(call => call.options?.method === 'POST').length, 0);
});
