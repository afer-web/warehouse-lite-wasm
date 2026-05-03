/**
 * MiniWMS SQLite WASM bridge (sql.js).
 * Persistence: OPFS (/miniWms/database.sqlite), then IndexedDB snapshot, otherwise an empty DB.
 * Prerequisite: load `sqlite/sql-wasm.js` first so the global `initSqlJs` is available.
 */
(function () {
  const LS_PREFS_NAMESPACE = 'MiniWms:Prefs';
  const IDB_DB_NAME = 'MiniWmsDbStorage';
  const IDB_STORE = 'sqlite';
  const IDB_KEY = 'db_v1';

  /** @type {import('sql.js').Database | null} */
  let db = null;

  /** @type {Promise<unknown> | null} */
  let initPromise = null;

  /** @type {Worker | null} */
  let opfsSaverWorker = null;

  /** @typedef {typeof import('sql.js')} SqlJsNamespace */

  function ensureWorker() {
    if (typeof Worker === 'undefined') return null;
    if (opfsSaverWorker) return opfsSaverWorker;
    try {
      opfsSaverWorker = new Worker('js/opfsWriteWorker.js');
    } catch {
      try {
        opfsSaverWorker = new Worker('./js/opfsWriteWorker.js');
      } catch {
        opfsSaverWorker = null;
      }
    }
    return opfsSaverWorker;
  }

  async function readBlobFromOpfs() {
    if (!navigator.storage?.getDirectory) return null;
    try {
      const root = await navigator.storage.getDirectory();
      const mini = await root.getDirectoryHandle('miniWms', { create: false });
      const fh = await mini.getFileHandle('database.sqlite', { create: false });
      const file = await fh.getFile();
      const buf = new Uint8Array(await file.arrayBuffer());
      return buf.length ? buf : null;
    } catch {
      return null;
    }
  }

  /** @returns {Promise<Uint8Array | null>} */
  async function readIndexedDb() {
    if (!indexedDB) return null;
    const dbi = await new Promise((resolve, reject) => {
      const open = indexedDB.open(IDB_DB_NAME, 1);
      open.onupgradeneeded = () => {
        const inner = open.result;
        if (!inner.objectStoreNames.contains(IDB_STORE)) inner.createObjectStore(IDB_STORE);
      };
      open.onsuccess = () => resolve(open.result);
      open.onerror = () => reject(open.error);
    });
    const store = dbi.transaction(IDB_STORE, 'readonly').objectStore(IDB_STORE);
    /** @type {Uint8Array | undefined} */
    const val = await new Promise((resolve, reject) => {
      const r = store.get(IDB_KEY);
      r.onsuccess = () => resolve(/** @type {any} */ (r.result));
      r.onerror = () => reject(r.error);
    });
    dbi.close();
    return val instanceof Uint8Array && val.byteLength ? val : null;
  }

  /** @param {Uint8Array} blob */
  async function writeIndexedDb(blob) {
    const dbi = await new Promise((resolve, reject) => {
      const open = indexedDB.open(IDB_DB_NAME, 1);
      open.onupgradeneeded = () => {
        const inner = open.result;
        if (!inner.objectStoreNames.contains(IDB_STORE)) inner.createObjectStore(IDB_STORE);
      };
      open.onsuccess = () => resolve(open.result);
      open.onerror = () => reject(open.error);
    });
    await new Promise((resolve, reject) => {
      const tx = dbi.transaction(IDB_STORE, 'readwrite');
      tx.objectStore(IDB_STORE).put(blob, IDB_KEY);
      tx.oncomplete = () => resolve(undefined);
      tx.onerror = () => reject(tx.error);
    });
    dbi.close();
  }

  /**
   * @param {Uint8Array} blob
   * @returns {Promise<void>}
   */
  async function writeOpfsWorker(blob) {
    const sliced = Uint8Array.from(blob);
    const w = ensureWorker();
    if (!w) {
      await writeOpfsMain(blob);
      return;
    }
    await new Promise((resolve, reject) => {
      const cb = ({ data }) => {
        if (data?.type === 'ok') resolve();
        else reject(new Error(data?.message ?? 'OPFS worker write failure'));
        w.removeEventListener('message', cb);
      };
      w.addEventListener('message', cb);
      try {
        w.postMessage({ type: 'write', blob: sliced }, [sliced.buffer]);
      } catch (err) {
        w.removeEventListener('message', cb);
        reject(err);
      }
    });
  }

  /** @param {Uint8Array} blob */
  async function writeOpfsMain(blob) {
    if (!navigator.storage?.getDirectory) return;
    const root = await navigator.storage.getDirectory();
    const mini = await root.getDirectoryHandle('miniWms', { create: true });
    const fh = await mini.getFileHandle('database.sqlite', { create: true });
    // createWritable is broadly available without synchronous OPFS locking
    const writable = await fh.createWritable({ keepExistingData: false });
    await writable.write(blob);
    await writable.close();
  }

  /**
   * @param {Uint8Array} blob
   * @returns {Promise<void>}
   */
  async function writeOpfsPreferWorker(blob) {
    if (!navigator.storage?.getDirectory) return;
    try {
      await writeOpfsWorker(blob);
    } catch {
      await writeOpfsMain(blob);
    }
  }

  /** Minimal schema + trigger keeping Stocks in sync with Movements ledger. */
  function migrateInternal() {
    const d = db;
    if (!d) throw new Error('migrate without db');

    // NOTE: TRIGGER keeps Stocks aligned; validations (e.g., non‑negative qty) enforced in MovementService SQL.
    d.exec(`
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Items (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Code TEXT NOT NULL UNIQUE,
  Description TEXT NOT NULL,
  Unit TEXT NOT NULL,
  CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Locations (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Code TEXT NOT NULL UNIQUE,
  Area TEXT NOT NULL,
  Description TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Stocks (
  ItemId INTEGER NOT NULL,
  LocationId INTEGER NOT NULL,
  Quantity REAL NOT NULL DEFAULT 0,
  PRIMARY KEY (ItemId, LocationId),
  FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE RESTRICT ON UPDATE CASCADE,
  FOREIGN KEY (LocationId) REFERENCES Locations(Id) ON DELETE RESTRICT ON UPDATE CASCADE
);

CREATE TABLE IF NOT EXISTS Movements (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ItemId INTEGER NOT NULL,
  LocationId INTEGER NOT NULL,
  Quantity REAL NOT NULL,
  Type INTEGER NOT NULL CHECK (Type IN (0, 1)),
  Timestamp TEXT NOT NULL,
  FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE CASCADE ON UPDATE CASCADE,
  FOREIGN KEY (LocationId) REFERENCES Locations(Id) ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Movements_Timestamp ON Movements(Timestamp DESC);

CREATE TRIGGER IF NOT EXISTS TRG_Movements_AfterInsert
AFTER INSERT ON Movements
BEGIN
  INSERT INTO Stocks (ItemId, LocationId, Quantity)
  SELECT NEW.ItemId, NEW.LocationId, 0
  WHERE NOT EXISTS (
    SELECT 1 FROM Stocks s WHERE s.ItemId = NEW.ItemId AND s.LocationId = NEW.LocationId
  );

  UPDATE Stocks
    SET Quantity = Quantity + CASE WHEN NEW.Type = 0 THEN NEW.Quantity ELSE -NEW.Quantity END
    WHERE ItemId = NEW.ItemId AND LocationId = NEW.LocationId;
END;
`);

    return true;
  }

  /** @type {SqlJsNamespace | undefined} */

  async function hydrateDatabase(SQLModule) {
    let bytes = await readBlobFromOpfs();
    if (!bytes || !bytes.byteLength) bytes = await readIndexedDb();
    return new SQLModule.Database(bytes ?? undefined);
  }

  /** @returns {Promise<any>} sql.js module exposing Database */
  async function resolveSqlWasm() {
    /** @type {any} */
    const g = typeof globalThis !== 'undefined' ? globalThis : window;
    /** @type {any} */
    const initFn = g.initSqlJs;
    if (typeof initFn !== 'function') {
      throw new Error('initSqlJs is missing — load sqlite/sql-wasm.js before miniWmsDb.js');
    }

    /** sql.js richiede percorso del file .wasm rinominato in sqlite/sqlite-wasm.wasm via locateFile */
    return await initFn({
      locateFile: () => './sqlite/sqlite-wasm.wasm',
    });
  }

  async function bootstrap() {
    const SQL = await resolveSqlWasm();
    db = await hydrateDatabase(SQL);
    migrateInternal();
    schedulePersistSoon();
    return true;
  }

  function ensureDb() {
    if (!db) throw new Error('Database not initialized. Call miniWmsDb.ensureReady first.');
    return db;
  }

  let persistTimer = null;

  /** @typedef {Record<string, string | number>} Row */
  /** @typedef {{ changes?: number }} RunResult */

  function schedulePersistSoon() {
    if (!db) return;
    clearTimeout(persistTimer);
    persistTimer = setTimeout(() => {
      persistTimer = null;
      void persistImmediateInternal();
    }, 120);
  }

  async function persistImmediateInternal() {
    const d = db;
    if (!d) return false;
    const buf = /** @type {Uint8Array} */ (d.export()); // synchronous export
    const copy = Uint8Array.from(buf);
    try {
      await writeIndexedDb(copy);
    } catch {
      // ignore IndexedDB outage in rare hardened profiles
    }
    try {
      await writeOpfsPreferWorker(copy);
    } catch {
      // IndexedDB persistence still holds latest snapshot
    }
    return true;
  }

  /**
   * Public query helper — positional binding only (safe with prepared statements).
   * @param {string} sqlText
   * @param {(string|number|null)=} bindsCsv JSON array string produced from C#
   */
  function queryJson(sqlText, bindsCsv) {
    return JSON.stringify(query(sqlText, bindsCsv));
  }

  function query(sqlText, bindsCsv) {
    /** @type {(string | number | null)[] | undefined} */
    let binds;
    try {
      binds = bindsCsv ? JSON.parse(bindsCsv) : undefined;
    } catch {
      binds = [];
    }

    const d = ensureDb();
    const stmt = d.prepare(sqlText);
    /** @type {Row[]} */
    const rows = [];
    try {
      if (binds?.length) stmt.bind(binds);
      while (stmt.step()) rows.push(stmt.getAsObject());
      return rows;
    } finally {
      stmt.free();
    }
  }

  /**
   * @param {string} sqlText
   * @param {(string | number | null)=} bindsCsv
   * @returns {RunResult}
   */
  function run(sqlText, bindsCsv) {
    /** @type {(string | number | null)[] | undefined} */
    let binds;
    try {
      binds = bindsCsv ? JSON.parse(bindsCsv) : undefined;
    } catch {
      binds = [];
    }
    const d = ensureDb();
    const stmt = d.prepare(sqlText);
    try {
      if (binds?.length) stmt.bind(binds);
      stmt.step();
    } finally {
      stmt.free();
    }
    schedulePersistSoon();
    return { changes: typeof d.getRowsModified === 'function' ? d.getRowsModified() : 0 };
  }

  /** @returns {boolean} */
  function begin() {
    ensureDb().run('BEGIN IMMEDIATE TRANSACTION;');
    return true;
  }

  /** @returns {boolean} */
  function commit() {
    ensureDb().run('COMMIT;');
    schedulePersistSoon();
    return true;
  }

  /** @returns {boolean} */
  function rollback() {
    ensureDb().run('ROLLBACK;');
    schedulePersistSoon();
    return true;
  }

  function ensureReady() {
    if (!initPromise) {
      initPromise = bootstrap().catch(err => {
        initPromise = null;
        console.error('[MiniWms] DB init failed', err);
        throw err;
      });
    }
    return initPromise;
  }

  window.miniWmsDb = {
    ensureReady,
    query,
    queryJson,
    run,
    begin,
    commit,
    rollback,
    persist: persistImmediateInternal,
  };

  window.MiniWmsPreferencesNs = LS_PREFS_NAMESPACE;
})();
