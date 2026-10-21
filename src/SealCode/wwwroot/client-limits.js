import { DEFAULT_MAX_YJS_MESSAGE_BYTES } from './yjs-message-size.js';

/** Reads validated server limits in either JSON naming convention. */
/** @param {Record<string, any>} [features]
 * @returns {import('./client-types.js').ClientLimits} */
export function readClientLimits(features = {}) {
  const limits = features.Limits ?? features.limits ?? {};
  /** @param {Record<string, any>} source
   * @param {string} name
   * @param {number} fallback */
  const read = (source, name, fallback) => {
    const value = source[name] ?? source[name[0].toLowerCase() + name.slice(1)];
    return Number.isSafeInteger(value) && value > 0 ? value : fallback;
  };
  return {
    maxEncodedUpdateBytes: read(limits, 'MaxEncodedUpdateBytes', 4 * 1024 * 1024),
    maxEncodedCheckpointBytes: read(limits, 'MaxEncodedCheckpointBytes', 4 * 1024 * 1024),
    maxSnapshotCharacters: read(limits, 'MaxSnapshotCharacters', 1024 * 1024),
    checkpointEveryRevisions: read(limits, 'CheckpointEveryRevisions', 64),
    checkpointEveryBase64Bytes: read(limits, 'CheckpointEveryBase64Bytes', 256 * 1024),
    maxYjsMessageBytes: read(
      features,
      'MaxYjsMessageBytes',
      DEFAULT_MAX_YJS_MESSAGE_BYTES
    )
  };
}
