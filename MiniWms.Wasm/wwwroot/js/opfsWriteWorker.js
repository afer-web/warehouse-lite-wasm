/**
 * Dedicated worker for OPFS sync writes — kept minimal to avoid blocking UI thread exports.
 */
self.addEventListener('message', async (ev) => {
  const msg = /** @type {any} */ (ev.data);
  if (!msg || msg.type !== 'write') {
    /** @type {any} */
    (self).postMessage({ type: 'err', message: 'invalid payload' });
    return;
  }

  /** @type {Uint8Array} */
  let blob = msg.blob instanceof Uint8Array ? msg.blob : new Uint8Array();

  try {
    if (!self.navigator?.storage?.getDirectory) throw new Error('OPFS unsupported');
    const root = await self.navigator.storage.getDirectory();
    const mini = await root.getDirectoryHandle('miniWms', { create: true });
    const fh = await mini.getFileHandle('database.sqlite', { create: true });
    const writable = await fh.createWritable({ keepExistingData: false });
    await writable.write(blob);
    await writable.close();

    blob = /** @type {any} */ (null);
    /** @type {any} */ (self).postMessage({ type: 'ok' });
  } catch (err) {
    /** @type {any} */ (self).postMessage({
      type: 'err',
      message: /** @type {Error} */ (err).message || String(err),
    });
  }
});
