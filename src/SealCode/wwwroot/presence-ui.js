/** Shortens a participant label while preserving the full name in its title. */
export function truncateName(name) {
  if (!name) return '';
  if (name.length <= 10) return name;
  return `${name.slice(0, 9)}...`;
}

/** Renders room participants, remote carets and transient presence states. */
export class PresenceView {
  constructor({ editor, monaco, participantsList, document }) {
    Object.assign(this, { editor, monaco, participantsList, document });
    this.displayName = '';
    this.currentUsers = [];
    this.cursorPositions = {};
    this.caretColors = {};
    this.caretPalette = [
      '#fbbf24',
      '#22d3ee',
      '#34d399',
      '#f472b6',
      '#a78bfa',
      '#fb7185'
    ];
    this.hideRemoteCarets = false;
    this.hideRemoteTimer = null;
    this.activeEditors = new Map();
    this.selectingUsers = new Map();
    this.copyingUsers = new Map();
    this.caretClassMap = new Map();
    this.caretClassCounter = 0;
  }

  get model() {
    return this.editor?.getModel();
  }

  renderUsers(users) {
    if (!Array.isArray(users)) {
      this.participantsList.innerHTML = '';
      return;
    }
    this.currentUsers = users;
    this.participantsList.innerHTML = '';
    for (const user of users) {
      const li = this.document.createElement('li');
      const pos = this.cursorPositions[user];
      const display = truncateName(user);
      li.title = user;
      if (this.activeEditors.get(user)) {
        li.classList.add('active');
      }
      if (this.selectingUsers.get(user)) {
        li.classList.add('copied');
      }
      if (this.copyingUsers.get(user)) {
        li.classList.add('copying');
      }
      if (typeof pos === 'number' && this.model) {
        const lc = this.getLineColFromOffset(pos);
        li.textContent = `${display} (${lc.line}:${lc.col})`;
      } else {
        li.textContent = display;
      }
      this.participantsList.appendChild(li);
    }
  }

  getLineColFromOffset(offset) {
    if (!this.model) return { line: 1, col: 1 };
    const pos = this.model.getPositionAt(Math.max(0, offset));
    return { line: pos.lineNumber, col: pos.column };
  }

  assignColor(name) {
    if (!this.caretColors[name]) {
      const idx = Object.keys(this.caretColors).length % this.caretPalette.length;
      this.caretColors[name] = this.caretPalette[idx];
    }
    return this.caretColors[name];
  }

  ensureCaretStyles() {
    let styleEl = this.document.getElementById('remoteCaretStyles');
    if (!styleEl) {
      styleEl = this.document.createElement('style');
      styleEl.id = 'remoteCaretStyles';
      this.document.head.appendChild(styleEl);
    }
    return styleEl;
  }

  renderRemoteCarets() {
    if (!this.editor || !this.model) return;
    if (this.hideRemoteCarets) {
      this.editor.remoteCaretDecorations = this.editor.deltaDecorations(
        this.editor.remoteCaretDecorations || [],
        []
      );
      return;
    }
    const decorations = [];
    const styleEl = this.ensureCaretStyles();
    let css = '';

    for (const [name, offset] of Object.entries(this.cursorPositions)) {
      if (name === this.displayName) continue;
      if (typeof offset !== 'number') continue;
      const pos = this.model.getPositionAt(Math.max(0, offset));
      const line = pos.lineNumber;
      const col = Math.min(pos.column, this.model.getLineMaxColumn(line));
      const color = this.assignColor(name);
      if (!this.caretClassMap.has(name)) {
        this.caretClassCounter += 1;
        this.caretClassMap.set(name, `remote-caret-${this.caretClassCounter}`);
      }
      const className = this.caretClassMap.get(name);
      css += `.${className} { border-left: 2px solid ${color}; margin-left: -1px; }\n`;
      decorations.push({
        range: new this.monaco.Range(line, col, line, col),
        options: {
          beforeContentClassName: className,
          stickiness:
            this.monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
        }
      });
    }

    styleEl.textContent = css;
    this.editor.remoteCaretDecorations = this.editor.deltaDecorations(
      this.editor.remoteCaretDecorations || [],
      decorations
    );
  }

  hideRemoteCaretsTemporarily() {
    this.hideRemoteCarets = true;
    this.renderRemoteCarets();
    if (this.hideRemoteTimer) clearTimeout(this.hideRemoteTimer);
    this.hideRemoteTimer = setTimeout(() => {
      this.hideRemoteCarets = false;
      this.renderRemoteCarets();
    }, 800);
  }

  markActiveEditor(name) {
    if (!name) return;
    this.activeEditors.set(name, Date.now());
    this.renderUsers(this.currentUsers);
    setTimeout(() => {
      const last = this.activeEditors.get(name);
      if (last && Date.now() - last > 1200) {
        this.activeEditors.delete(name);
        this.renderUsers(this.currentUsers);
      }
    }, 1300);
  }

  markSelecting(name, isSelecting) {
    if (!name) return;
    if (isSelecting) {
      this.selectingUsers.set(name, Date.now());
    } else {
      this.selectingUsers.delete(name);
    }
    this.renderUsers(this.currentUsers);
  }

  markCopying(name) {
    if (!name) return;
    this.copyingUsers.set(name, Date.now());
    this.renderUsers(this.currentUsers);
    setTimeout(() => {
      const last = this.copyingUsers.get(name);
      if (last && Date.now() - last > 1200) {
        this.copyingUsers.delete(name);
        this.renderUsers(this.currentUsers);
      }
    }, 1300);
  }
}
