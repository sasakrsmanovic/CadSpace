"""Exercise UI-only options, classic menus, document rebinding and cancellable palette manipulation."""
import asyncio
import json
import os
import re
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width': 1600, 'height': 1000}, device_scale_factor=1)
        events = []
        page.on('console', lambda e: events.append({'type': e.type, 'text': e.text}))
        page.on('pageerror', lambda e: events.append({'type': 'pageerror', 'text': str(e)}))
        async def prefs():
            value = await page.evaluate('CadSpaceWorkspaceSettings.read()')
            return json.loads(value) if value else {}
        def placement(value, name='properties'):
            return next(p for p in value['palettes'] if p['id'] == name)
        async def state():
            await bounds(page, events)
            return next(e['text'] for e in reversed(events) if 'CADSPACE_UI_STATE:' in e.get('text', ''))
        async def shot(name):
            await page.screenshot(path=str(output / name), full_page=True)
        async def fill(id, value):
            await click(page, events, id); await page.keyboard.press('Control+a'); await page.keyboard.type(value)
        try:
            await page.goto(os.environ.get('CADSPACE_URL', 'http://127.0.0.1:8177/CadSpace/'), wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')", timeout=90000); await page.wait_for_timeout(2800)
            await click(page, events, 'quick.NEW')
            await command(page, 'MENUBAR', '1')
            await click(page, events, 'menubar.Draw'); await click(page, events, 'menu.Draw.CIRCLE')
            await command(page, '50,50', '20', 'ZOOM')
            assert '*' in await page.title(), 'Classic menu must start the real drawing command'
            await shot('60-classic-menu-workspace.png')
            # A real resize commits preferences; cancellation restores the previous height.
            data = await bounds(page, events); x,y,w,h = data['command.resize']
            await page.mouse.move(x+300,y+h/2); await page.mouse.down(); await page.mouse.move(x+300,y-62,steps=8); await page.mouse.up(); await page.wait_for_timeout(350)
            saved = await prefs(); assert saved['commandHeight'] > 120, saved
            data = await bounds(page, events); x,y,w,h = data['command.resize']
            await page.mouse.move(x+300,y+h/2); await page.mouse.down(); await page.mouse.move(x+300,y-30,steps=5); await page.keyboard.press('Escape'); await page.mouse.up(); await page.wait_for_timeout(250)
            assert (await prefs())['commandHeight'] == saved['commandHeight'], 'Escape must cancel console resizing'
            # Options are staged. Cancelling must not apply the checkbox edit.
            await click(page, events, 'quick.OPTIONS'); await click(page, events, 'options.menu')
            await click(page, events, 'options.dialog.CloseButton')
            assert (await prefs())['menuBarVisible'], 'Cancel must leave the live menu preference unchanged'
            # Apply display settings while in 3D without changing the current view.
            await command(page, '3DORBIT'); before = await state()
            await click(page, events, 'quick.OPTIONS')
            await click(page, events, 'options.cube'); await click(page, events, 'options.navigation')
            await fill('options.commandHeight', '120')
            await shot('61-workspace-options-display.png')
            await click(page, events, 'options.dialog.PrimaryButton')
            data = await bounds(page, events)
            assert 'viewport.cube' not in data and 'navigation.pan' not in data
            assert 'model=True;' in await state() and (await prefs())['commandHeight'] == 120
            # Status customization hides controls, not their modes.
            await click(page, events, 'status.customize'); await click(page, events, 'status.show.GRID'); await page.keyboard.press('Escape')
            assert 'status.GRID' not in await bounds(page, events) and 'grid=True' in await state()
            # Rebinding must refresh snap menu closures even when both documents have the same modes.
            await click(page, events, 'quick.NEW'); data = await bounds(page, events)
            old_index = max(int(k.rsplit('.',1)[1]) for k in data if k.startswith('documents.activate.'))
            old_state = int(re.search(r'snapModes=(\d+)', await state()).group(1))
            await click(page, events, 'status.snapOptions'); await page.keyboard.press('Escape')
            await click(page, events, 'quick.NEW')
            await click(page, events, 'status.snapOptions'); await click(page, events, 'snap.Nearest'); await page.keyboard.press('Escape')
            assert int(re.search(r'snapModes=(\d+)', await state()).group(1)) != old_state, 'Snap toggle must edit the new session'
            await click(page, events, 'documents.activate.' + str(old_index))
            assert int(re.search(r'snapModes=(\d+)', await state()).group(1)) == old_state, 'Snap toggle must not edit the previous session'
            # Floating movement and resize previews are transactional UI interactions.
            await click(page, events, 'dock.properties.float'); floating = placement(await prefs())
            data = await bounds(page, events); x,y,w,h = data['dock.properties.drag']
            await page.mouse.move(x+12,y+h/2); await page.mouse.down(); await page.mouse.move(x+160,y+85,steps=8); await page.keyboard.press('Escape'); await page.mouse.up(); await page.wait_for_timeout(250)
            assert placement(await prefs()) == floating, 'Escape must cancel palette dragging'
            data = await bounds(page, events); x,y,w,h = data['dock.properties.resize']
            await page.mouse.move(x+w/2,y+h/2); await page.mouse.down(); await page.mouse.move(x+w/2+65,y+35,steps=8); await page.keyboard.press('Escape'); await page.mouse.up(); await page.wait_for_timeout(250)
            assert placement(await prefs()) == floating, 'Escape must cancel palette resizing'
            data = await bounds(page, events); x,y,w,h = data['dock.properties.drag']
            await page.mouse.move(x+12,y+h/2); await page.keyboard.down('Control'); await page.mouse.down(); await page.mouse.move(1590,400,steps=12); await page.mouse.up(); await page.keyboard.up('Control'); await page.wait_for_timeout(250)
            assert placement(await prefs())['dock'] == 'Floating', 'Ctrl must prevent edge docking'
            await click(page, events, 'dock.properties.right'); await shot('62-workspace-customized.png')
            saved = await prefs()
            await page.reload(wait_until='domcontentloaded'); await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000); await page.wait_for_timeout(2800)
            assert await prefs() == saved, 'Display and palette preferences must survive a real reload'
            data = await bounds(page, events)
            assert 'menubar.File' in data and 'viewport.cube' not in data and 'status.GRID' not in data
            await shot('63-workspace-preferences-restored.png')
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')], events[-30:]
            (output/'polish-preferences.json').write_text(json.dumps(saved,indent=2))
            print('PASS classic menu invokes geometry, staged Options cancel/apply, view preservation, status visibility, snap-menu document rebinding, palette/console Escape cancellation, Ctrl float and reload persistence')
        finally:
            await shot('polish-last-state.png'); (output/'polish-console.json').write_text(json.dumps(events,indent=2)); await browser.close()
asyncio.run(main())
