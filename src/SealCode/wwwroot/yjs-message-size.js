export const DEFAULT_MAX_YJS_MESSAGE_BYTES = 16 * 1024 * 1024;
const encoder = new TextEncoder();

/** Includes JSON escaping, UTF-8 encoding, and space for the invocation envelope. */
/** @param {string} target
 * @param {unknown[]} args
 * @param {number} maxBytes */
export function fitsYjsMessage(target, args, maxBytes) {
  const message =
    JSON.stringify({
      type: 1,
      invocationId: '9007199254740991',
      target,
      arguments: args
    }) + '\u001e';
  // Reserve space for protocol metadata as well as the longest JS integer ID.
  return encoder.encode(message).byteLength + 128 <= maxBytes;
}
