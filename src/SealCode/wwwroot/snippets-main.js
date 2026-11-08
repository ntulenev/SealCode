import { createMarkdownRenderer } from './snippet-markdown.js';
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

/** @typedef {{ Number: number, Title: string, Markdown: string }} Step */
/** @typedef {{ Number: number, Topic: string, Description: string, Markdown: string, Steps?: Step[] }} Question */
/** @typedef {{ Language: string, Title: string, SortOrder?: number, Questions: Question[] }} Category */
const tabs = document.getElementById('snippetTabs');
const list = document.getElementById('snippetQuestions');
const heading = document.getElementById('snippetTitle');
const description = document.getElementById('snippetDescription');
const search = /** @type {HTMLInputElement} */ (document.getElementById('snippetSearch'));
const searchSummary = document.getElementById('snippetSearchSummary');
const content = document.getElementById('snippetContent');
const status = document.getElementById('snippetStatus');
const syncTime = /** @type {HTMLTimeElement} */ (document.getElementById('snippetSyncTime'));
const copy = /** @type {HTMLButtonElement} */ (document.getElementById('copySnippet'));
let selectedText = '';
let revision = 0;
/** @type {Category | null} */
let activeCategory = null;
/** @type {number | null} */
let selectedNumber = null;
bindEditorTheme(monaco, document);
const renderMarkdown = createMarkdownRenderer({ document,
  colorize: (source, language) => monaco.editor.colorize(source, language, {}) });


async function copyText(/** @type {string} */ text) {
  status.textContent = '';
  try {
    await navigator.clipboard.writeText(text);
    status.textContent = 'Copied to clipboard.';
  } catch {
    status.textContent = 'Clipboard unavailable. Select and copy the text manually.';
  }
}

async function renderQuestion(/** @type {Question} */ question, /** @type {string} */ language) {
  const current = ++revision;
  heading.textContent = `${String(question.Number).padStart(2, '0')} / ${question.Topic}`;
  description.textContent = question.Description;
  content.replaceChildren();
  status.textContent = '';
  const steps = question.Steps ?? [{ Number: 1, Title: 'Original example', Markdown: question.Markdown }];
  copy.textContent = steps.length > 1 ? 'Copy all snippets' : 'Copy snippet';
  const grid = document.createElement('div');
  if (steps.length > 1) {
    grid.className = 'snippet-step-grid';
    content.append(grid);
  }
  const texts = [];
  for (const step of steps) {
    const article = document.createElement('article');
    article.className = 'snippet-step';
    article.dataset.step = String(step.Number);
    let stepCopy = /** @type {HTMLButtonElement | null} */ (null);
    if (steps.length > 1) {
      const header = document.createElement('header');
      header.className = 'snippet-step-heading';
      const labels = document.createElement('div');
      const position = document.createElement('p');
      position.className = 'eyebrow';
      position.textContent = `Example ${step.Number} of ${steps.length}`;
      const title = document.createElement('h3');
      title.id = `snippet-example-${current}-${step.Number}`;
      title.textContent = step.Title;
      article.setAttribute('aria-labelledby', title.id);
      labels.append(position, title);
      stepCopy = document.createElement('button');
      stepCopy.className = 'ghost';
      stepCopy.textContent = 'Copy example';
      stepCopy.setAttribute('aria-label', `Copy example ${step.Number}: ${step.Title}`);
      stepCopy.disabled = true;
      header.append(labels, stepCopy);
      article.append(header);
      grid.append(article);
    } else {
      content.append(article);
    }
    const text = await renderMarkdown(step.Markdown, language, article, () => current === revision);
    if (text === null || current !== revision) return;
    texts.push(text);
    if (stepCopy) {
      stepCopy.onclick = () => copyText(text);
      stepCopy.disabled = false;
    }
  }
  selectedText = texts.join('\n\n');
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
    heading.textContent = category.Questions.length ? 'No matching questions' : 'No questions in this category';
    description.textContent = category.Questions.length
      ? 'Try another number, topic or description, or clear the search.'
      : 'Questions will appear here after publication.';
    content.replaceChildren();
    status.textContent = '';
    copy.disabled = true;
  }
}

search.addEventListener('input', renderQuestionList);

copy.onclick = () => copyText(selectedText);

fetch('/admin/snippets/catalog', { cache: 'no-store' }).then(async response => {
  if (!response.ok) throw new Error('Access unavailable');
  const categories = /** @type {Category[]} */ (await response.json());
  const syncedAt = response.headers.get('X-Snippets-Synced-At');
  const date = syncedAt ? new Date(syncedAt) : null;
  if (date && Number.isFinite(date.getTime())) {
    syncTime.dateTime = date.toISOString();
    syncTime.textContent = new Intl.DateTimeFormat(undefined, {
      year: 'numeric', month: 'short', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit', timeZoneName: 'short'
    }).format(date);
  } else {
    syncTime.textContent = 'Unavailable';
  }
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
}).catch(() => {
  syncTime.textContent = 'Unavailable';
  status.textContent = 'Question library unavailable. Sign in with an authorized admin account.';
});
