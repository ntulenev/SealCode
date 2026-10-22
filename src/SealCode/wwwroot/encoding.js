/** Converts between Base64 and byte arrays. */
/** @param {Uint8Array} data */
export function base64FromUint8(data) {
  if (!data || data.length === 0) return '';
  let binary = '';
  const chunkSize = 0x8000;
  for (let i = 0; i < data.length; i += chunkSize) {
    const chunk = data.subarray(i, i + chunkSize);
    binary += String.fromCharCode(...chunk);
  }
  return btoa(binary);
}

/** Converts between Base64 and byte arrays. */
/** @param {string} base64 */
export function uint8FromBase64(base64) {
  if (!base64) return new Uint8Array();
  const binary = atob(base64);
  const len = binary.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}
