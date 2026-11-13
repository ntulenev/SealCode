import { SyncErrors } from './sync-errors.js';

/** Owns synchronization error rendering and local text downloads.
 * @param {{syncError: HTMLElement, downloadLocalText: HTMLElement | null,
 * document: Document, roomId: string, getModel: () => {getValue: () => string} | null,
 * downloadBoard?: (() => void) | null, BlobType?: typeof Blob,
 * urlApi?: Pick<typeof URL, 'createObjectURL' | 'revokeObjectURL'>,
 * setTimer?: (callback: () => void, delay: number) => unknown}} options
 */
export function createSyncErrorView({ syncError, downloadLocalText, document, roomId,
  getModel, downloadBoard, BlobType = Blob, urlApi = URL, setTimer = setTimeout }) {
  const syncErrors = new SyncErrors();
  /** @param {{kind: string, message: string} | null} error */
  function renderSyncError(error) {
    syncError.dataset.kind = error?.kind ?? '';
    syncError.textContent = error?.message ?? '';
    syncError.classList.toggle('hidden', error === null);
    downloadLocalText?.classList.toggle(
      'hidden', !['update-size', 'text-size', 'checkpoint-size'].includes(error?.kind ?? '')
    );
  }
  downloadLocalText?.addEventListener('click', () => {
    if (downloadBoard) return downloadBoard();
    const model = getModel();
    if (!model) return;
    const blob = new BlobType([model.getValue()], { type: 'text/plain;charset=utf-8' });
    const url = urlApi.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `sealcode-${roomId}.txt`;
    document.body.append(link);
    link.click();
    link.remove();
    setTimer(() => urlApi.revokeObjectURL(url), 1000);
  });

  /** @param {string} kind
   * @param {string} message */
  function showSyncError(kind, message) {
    renderSyncError(syncErrors.show(kind, message));
  }

  /** @param {string} kind */
  function clearSyncError(kind) {
    renderSyncError(syncErrors.clear(kind));
  }

  return { showSyncError, clearSyncError, renderSyncError };
}
