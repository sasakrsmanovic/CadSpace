// Application-scoped IndexedDB adapter. No drawings leave the browser.
// Promise resolution means transaction completion, never just request.onsuccess.
(() => {
    "use strict";
    const storeName = "checkpoints";
    const databaseName = "CadSpaceRecovery:v1:" + new URL(".", document.baseURI).pathname;
    const maximumCharacters = 32 * 1024 * 1024 + 4096;
    const timeoutMilliseconds = 30000;
    let pendingDatabase = null;

    function validateKey(key) {
        if (typeof key !== "string" || !/^[0-9a-f]{32}\.[01]\.recovery$/.test(key))
            throw new TypeError("Invalid recovery slot key.");
    }
    function openDatabase() {
        if (pendingDatabase) return pendingDatabase;
        const pending = new Promise((resolve, reject) => {
            if (!globalThis.indexedDB) { reject(new Error("IndexedDB is unavailable.")); return; }
            let settled = false;
            const request = indexedDB.open(databaseName, 1);
            const timer = setTimeout(() => fail(new Error("Recovery database open timed out.")), timeoutMilliseconds);
            function fail(error) { if (!settled) { settled = true; clearTimeout(timer); reject(error); } }
            request.onupgradeneeded = () => {
                if (!request.result.objectStoreNames.contains(storeName)) request.result.createObjectStore(storeName);
            };
            request.onerror = () => fail(request.error || new Error("Cannot open recovery database."));
            request.onblocked = () => fail(new Error("Another tab is blocking the recovery database upgrade."));
            request.onsuccess = () => {
                const db = request.result;
                if (settled) { db.close(); return; }
                settled = true; clearTimeout(timer);
                db.onversionchange = () => { db.close(); pendingDatabase = null; };
                db.onclose = () => { pendingDatabase = null; };
                resolve(db);
            };
        });
        pendingDatabase = pending;
        pending.catch(() => { if (pendingDatabase === pending) pendingDatabase = null; });
        return pending;
    }
    async function transact(mode, operation) {
        const db = await openDatabase();
        return new Promise((resolve, reject) => {
            let transaction;
            // Older engines may not accept transaction options. Do not suppress other failures.
            try { transaction = db.transaction(storeName, mode, { durability: mode === "readwrite" ? "strict" : "default" }); }
            catch (error) {
                if (!(error instanceof TypeError)) { reject(error); return; }
                transaction = db.transaction(storeName, mode);
            }
            let value;
            let error = null;
            const timer = setTimeout(() => {
                error = new Error("Recovery database transaction timed out.");
                try { transaction.abort(); } catch { reject(error); }
            }, timeoutMilliseconds);
            transaction.oncomplete = () => { clearTimeout(timer); resolve(value); };
            transaction.onabort = () => { clearTimeout(timer); reject(error || transaction.error || new Error("Recovery transaction aborted.")); };
            // The normal error event bubbles into an abort; retain its actual reason.
            transaction.onerror = () => { error = transaction.error || error; };
            try {
                const request = operation(transaction.objectStore(storeName));
                request.onsuccess = () => { value = request.result; };
                request.onerror = () => { error = request.error; };
            } catch (caught) { error = caught; transaction.abort(); }
        });
    }
    globalThis.CadSpaceRecoveryStorage = Object.freeze({
        async list() {
            const keys = await transact("readonly", store => store.getAllKeys(null, 257));
            if (keys.length > 256) throw new Error("Too many recovery slots; archive older checkpoints.");
            return JSON.stringify(keys.filter(key => typeof key === "string"));
        },
        async read(key) {
            validateKey(key);
            const value = await transact("readonly", store => store.get(key));
            if (value === undefined) return "";
            if (typeof value !== "string" || value.length > maximumCharacters)
                throw new Error("Invalid or oversized recovery checkpoint.");
            return value;
        },
        async write(key, text) {
            validateKey(key);
            if (typeof text !== "string" || text.length === 0 || text.length > maximumCharacters)
                throw new RangeError("Invalid or oversized recovery checkpoint.");
            await transact("readwrite", store => store.put(text, key));
        },
        async remove(key) {
            validateKey(key);
            await transact("readwrite", store => store.delete(key));
        }
    });
})();
