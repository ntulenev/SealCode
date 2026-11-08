import * as monaco from 'monaco-editor';
import { bindEditorTheme } from './editor-theme.js';

if (window.self !== window.top) {
  const brand = document.querySelector('.site-header .brand');
  const label = document.createElement('span');
  label.className = 'brand';
  label.append(...brand.childNodes);
  brand.replaceWith(label);
  document.querySelector('.site-header a')?.remove();
}

/** @typedef {{ Number: number, Topic: string, Description: string, Markdown: string }} Question */
/** @typedef {{ Language: string, Title: string, Questions: Question[] }} Category */
const tabs = document.getElementById('snippetTabs');
const list = document.getElementById('snippetQuestions');
const heading = document.getElementById('snippetTitle');
const description = document.getElementById('snippetDescription');
const search = /** @type {HTMLInputElement} */ (document.getElementById('snippetSearch'));
const searchSummary = document.getElementById('snippetSearchSummary');
const content = document.getElementById('snippetContent');
const status = document.getElementById('snippetStatus');
const copy = /** @type {HTMLButtonElement} */ (document.getElementById('copySnippet'));
let selectedText = '';
let revision = 0;
/** @type {Category | null} */
let activeCategory = null;
/** @type {number | null} */
let selectedNumber = null;
bindEditorTheme(monaco, document);

/** Render only plain text and fenced code. Raw HTML and Markdown links cannot execute. */
async function renderQuestion(/** @type {Question} */ question, /** @type {string} */ language) {
  const current = ++revision;
  heading.textContent = `${String(question.Number).padStart(2, '0')} / ${question.Topic}`;
  description.textContent = question.Description;
  content.replaceChildren();
  status.textContent = '';
  const blocks = question.Markdown.split(/^```([^\r\n]*)\r?\n([\s\S]*?)^```\s*$/gm);
  const code = [];
  for (let i = 0; i < blocks.length; i += 3) {
    if (blocks[i].trim()) {
      const text = document.createElement('p');
      text.className = 'snippet-prose';
      text.textContent = blocks[i].trim();
      content.append(text);
    }
    if (blocks[i + 2] !== undefined) {
      const pre = document.createElement('pre');
      pre.className = 'snippet-code';
      const source = blocks[i + 2].replace(/\r?\n$/, '');
      pre.textContent = source;
      content.append(pre);
      code.push(source);
      const html = await monaco.editor.colorize(source, blocks[i + 1].trim() || language, {});
      if (current !== revision) return;
      pre.innerHTML = html;
    }
  }
  selectedText = code.length ? code.join('\n\n') : question.Markdown.trim();
  copy.disabled = false;
}

/** @param {Category} category */
function showCategory(category) {
  activeCategory = category;
  selectedNumber = null;
  for (const tab of tabs.querySelectorAll('button'))
    tab.setAttribute('aria-pressed', String(tab.dataset.language === category.Language));
  renderQuestionList();
}

function renderQuestionList() {
  if (!activeCategory) return;
  const category = activeCategory;
  const terms = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const questions = category.Questions.filter(question => {
    const text = `${String(question.Number).padStart(2, '0')} ${question.Topic} ${question.Description}`.toLowerCase();
    return terms.every(term => text.includes(term));
  });
  list.replaceChildren();
  searchSummary.textContent = `${questions.length} of ${category.Questions.length} questions in ${category.Title}`;
  for (const question of questions) {
    const button = document.createElement('button');
    button.className = 'snippet-question';
    const number = document.createElement('span');
    number.className = 'snippet-number';
    number.textContent = String(question.Number).padStart(2, '0');
    const topic = document.createElement('span');
    topic.className = 'snippet-question-topic';
    topic.textContent = question.Topic;
    const coverage = document.createElement('span');
    coverage.className = 'snippet-question-description';
    coverage.textContent = question.Description;
    const text = document.createElement('span');
    text.className = 'snippet-question-text';
    text.append(topic, coverage);
    button.append(number, text);
    button.dataset.number = String(question.Number);
    button.onclick = () => {
      selectedNumber = question.Number;
      for (const item of list.querySelectorAll('button')) item.setAttribute('aria-pressed', String(item === button));
      copy.disabled = true;
      renderQuestion(question, category.Language).catch(() => { status.textContent = 'Unable to display this question.'; });
    };
    list.append(button);
  }
  const selected = /** @type {HTMLButtonElement | null} */ (
    selectedNumber === null ? null : list.querySelector(`button[data-number="${selectedNumber}"]`)
  );
  if (questions.length) {
    (selected || list.querySelector('button')).click();
  } else {
    ++revision;
    selectedNumber = null;
    selectedText = '';
    heading.textContent = 'No matching questions';
    description.textContent = 'Try another number, topic or description, or clear the search.';
    content.replaceChildren();
    status.textContent = '';
    copy.disabled = true;
  }
}

search.addEventListener('input', renderQuestionList);

copy.onclick = async () => {
  try {
    await navigator.clipboard.writeText(selectedText);
    status.textContent = 'Copied to clipboard.';
  } catch {
    status.textContent = 'Clipboard unavailable. Select and copy the text manually.';
  }
};

fetch('/admin/snippets/catalog', { cache: 'no-store' }).then(async response => {
  if (!response.ok) throw new Error('Access unavailable');
  const categories = /** @type {Category[]} */ (await response.json());
  for (const category of categories) {
    const button = document.createElement('button');
    button.textContent = category.Title;
    button.dataset.language = category.Language;
    button.onclick = () => showCategory(category);
    tabs.append(button);
  }
  const language = new URLSearchParams(window.location.search).get('language')?.trim().toLowerCase();
  const initial = categories.find(category => category.Language === language)
    ?? categories.find(category => category.Language === 'general')
    ?? categories[0];
  if (initial) showCategory(initial);
  else status.textContent = 'No questions available for the configured languages.';
}).catch(() => { status.textContent = 'Question library unavailable. Sign in with an authorized admin account.'; });
