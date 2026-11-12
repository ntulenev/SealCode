/** Compact, revisioned viewer count shared by editor and guest pages.
 * @param {Document} document
 * @returns {(count: number, nextRevision: number) => void}
 */
export function createObserverPresence(document) {
  const bar = document.querySelector?.('.participants-bar');
  let badge = document.getElementById('observerCount');
  if (!badge && bar) {
    badge = document.createElement('span');
    badge.id = 'observerCount';
    badge.className = 'observer-count hidden';
    badge.setAttribute?.('role', 'status');
    bar.append(badge);
  }
  let revision = -1;
  return (count, nextRevision) => {
    if (!badge || !Number.isSafeInteger(count) || !Number.isSafeInteger(nextRevision) || count < 0 || nextRevision < revision) return;
    revision = nextRevision;
    badge.textContent = `👁 ${count}`;
    badge.title = `Observers: ${count}. Read-only guest connections.`;
    badge.setAttribute?.('aria-label', badge.title);
    badge.classList.toggle('hidden', count === 0);
  };
}
