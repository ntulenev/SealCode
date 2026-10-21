/** Stable categories shared by transport and local failures. */
export const HubErrorCode = Object.freeze({
  InvalidInput: 'InvalidInput',
  LimitExceeded: 'LimitExceeded',
  RateLimited: 'RateLimited',
  RoomNotFound: 'RoomNotFound',
  NotParticipant: 'NotParticipant'
});

/** Classifies errors, including SignalR wrappers and legacy size refusals. */
export function classifyHubError(error) {
  const text = String(error?.message ?? error ?? '');
  // Keep the former size/rate matching rules used by the retry queues.
  if (/LimitExceeded|too large/.test(text)) return HubErrorCode.LimitExceeded;
  if (/RateLimited/.test(text)) return HubErrorCode.RateLimited;
  if (/InvalidInput/.test(text)) return HubErrorCode.InvalidInput;
  if (/\bRoom not found\b/.test(text)) return HubErrorCode.RoomNotFound;
  if (/Join the room before changing it/.test(text)) return HubErrorCode.NotParticipant;
  return null;
}

/** Creates a local error with the same format as a server refusal. */
export function createHubError(code, message) {
  if (code === HubErrorCode.RoomNotFound) return new Error('Room not found');
  if (code === HubErrorCode.NotParticipant)
    return new Error('Join the room before changing it');
  return new Error(`${code}: ${message}`);
}
