"""Exercise the actual browser adapter against native IndexedDB, without a mocked filesystem."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright
SCRIPT = Path(__file__).resolve().parents[2] / 'src/CadSpace.App/Platforms/WebAssembly/WasmScripts/RecoveryStorage.js'
URL = os.environ.get('CADSPACE_URL', 'http://127.0.0.1:8177/CadSpace/')
async def main():
    async with async_playwright() as p:
        options = {'headless': True}
        if os.environ.get('CHROMIUM_EXECUTABLE'): options['executable_path'] = os.environ['CHROMIUM_EXECUTABLE']
        browser = await p.chromium.launch(**options)
        context = await browser.new_context()
        async def page():
            result = await context.new_page()
            await result.goto(URL + 'build-info.json')
            await result.add_script_tag(content=SCRIPT.read_text())
            return result
        first = await page()
        key = 'a' * 32 + '.1.recovery'
        other = 'b' * 32 + '.0.recovery'
        text = 'Unicode Ω / Zażółć / 日本語\n' + 'checkpoint' * 10000
        await first.evaluate('([k,t]) => CadSpaceRecoveryStorage.write(k,t)', [key, text])
        await first.close()  # no deferred filesystem flush or arbitrary sleep
        second = await page()
        assert await second.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', key) == text
        await second.evaluate('([k,t]) => CadSpaceRecoveryStorage.write(k,t)', [other, 'other session'])
        peer = await page()
        assert await peer.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', other) == 'other session'
        assert await peer.evaluate('async () => JSON.parse(await CadSpaceRecoveryStorage.list()).length') == 2
        assert await peer.evaluate('async () => {try {await CadSpaceRecoveryStorage.write("../escape", "x"); return false;} catch {return true;}}')
        assert await peer.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', 'c' * 32 + '.0.recovery') == ''
        assert await peer.evaluate('async k => {try {await CadSpaceRecoveryStorage.write(k, ""); return false;} catch {return true;}}', key)
        assert await peer.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', key) == text
        assert await peer.evaluate('''async k => {
            const original = IDBDatabase.prototype.transaction;
            IDBDatabase.prototype.transaction = function (...args) {
                const tx = original.apply(this, args);
                if (args[1] === 'readwrite') queueMicrotask(() => tx.abort());
                return tx;
            };
            try { await CadSpaceRecoveryStorage.write(k, 'must not commit'); return false; }
            catch { return true; }
            finally { IDBDatabase.prototype.transaction = original; }
        }''', key)
        assert await peer.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', key) == text
        await peer.evaluate('(k) => CadSpaceRecoveryStorage.remove(k)', key)
        await peer.close()
        third = await page()
        assert await third.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', key) == ''
        assert await third.evaluate('(k) => CadSpaceRecoveryStorage.read(k)', other) == 'other session'
        await third.evaluate('(k) => CadSpaceRecoveryStorage.remove(k)', other)
        assert await third.evaluate('() => CadSpaceRecoveryStorage.list()') == '[]'
        await context.close()
        await browser.close()
        print('PASS IndexedDB: cross-page Unicode roundtrip, peer visibility, key/value guards, missing read, real aborted-write rejection/rollback, committed deletion and independent slots')
asyncio.run(main())
