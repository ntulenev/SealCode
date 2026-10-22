/** Advances browser timers while allowing server responses and their callbacks to settle. */
export async function advance(page, milliseconds) {
  const pages = Array.isArray(page) ? page : [page];
  for (let remaining = milliseconds; remaining > 0; remaining -= 200) {
    await Promise.all(
      pages.map((item) => item.clock.runFor(Math.min(200, remaining)))
    );
    await Promise.all(
      pages.map((item) =>
        item.evaluate(async () => {
          if (typeof probe !== 'undefined' && probe.state === 'Connected') {
            await probe.invoke('GetRoomRevisions', location.pathname.split('/').at(-1));
          } else {
            await fetch('/health', { cache: 'no-store' });
          }
        })
      )
    );
    await pages[0].waitForTimeout(50);
  }
}
