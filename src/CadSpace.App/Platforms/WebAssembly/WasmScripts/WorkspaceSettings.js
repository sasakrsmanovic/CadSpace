// UI preferences only. Deliberately separate from drawing recovery and IndexedDB records.
(() => {
    const key = 'cadspace.workspace.v1';
    globalThis.CadSpaceWorkspaceSettings = Object.freeze({
        read() { return localStorage.getItem(key) || ''; },
        write(value) {
            if (typeof value !== 'string' || value.length > 32768) throw new Error('Invalid workspace preferences size.');
            localStorage.setItem(key, value);
        }
    });
})();
