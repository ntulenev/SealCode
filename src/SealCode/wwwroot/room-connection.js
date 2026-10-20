/** Serializes connection startup and configures reconnect policy. */
export function createRoomConnection(signalR, url, online = () => navigator.onLine) {
  const connection = new signalR.HubConnectionBuilder().withUrl(url)
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => online() ? 3000 : 2000 }).build();
  connection.serverTimeoutInMilliseconds = 15000; connection.keepAliveIntervalInMilliseconds = 5000;
  let starting = null;
  async function start() {
    if (connection.state !== signalR.HubConnectionState.Disconnected) return true;
    if (!starting) starting = connection.start().then(() => true, () => false).finally(() => { starting = null; });
    return starting;
  }
  return { connection, start };
}
