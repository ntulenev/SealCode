/** Advances browser timers while allowing server responses and their callbacks to settle. */
export async function advance(page, milliseconds) {
  for (let remaining = milliseconds; remaining > 0; remaining -= 200) {
    await page.clock.runFor(Math.min(200, remaining));
    await page.evaluate(async () => {
      if (typeof probe !== 'undefined' && probe.state === 'Connected') {
        await probe.invoke('GetRoomRevisions', location.pathname.split('/').at(-1));
      } else {
        await fetch('/health', { cache: 'no-store' });
      }
    });
    await page.waitForTimeout(50);
  }
}
