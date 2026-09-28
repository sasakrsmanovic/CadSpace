"""Verify integrated application/ribbon/palette/navigation/layout UI against the published app."""
import asyncio
import json
import os
import re
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import command, bounds, click

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600, 'height':1000}, device_scale_factor=1)
        events = []
        page.on('console', lambda e: events.append({'type':e.type, 'text':e.text}))
        page.on('pageerror', lambda e: events.append({'type':'pageerror', 'text':str(e)}))
        async def shot(name): await page.screenshot(path=str(output/name), full_page=True)
        async def preferences(): return json.loads(await page.evaluate('CadSpaceWorkspaceSettings.read()'))
        def placement(prefs, id): return next(p for p in prefs['palettes'] if p['id'] == id)
        try:
            await page.goto(os.environ.get('CADSPACE_URL', 'http://127.0.0.1:8177/CadSpace/'), wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')", timeout=90000); await page.wait_for_timeout(2800)
            initial = await bounds(page, events)
            for key in ['shell.application','quick.NEW','shell.search','ribbon.layer','ribbon.color','ribbon.linetype','ribbon.lineweight','viewport.cube','viewport.views','viewport.styles','navigation.pan','dock.properties.float','layout.Model','layout.new','status.workspace']:
                assert key in initial, key
            await shot('50-cad-workspace.png')
            await click(page, events, 'shell.application'); await shot('51-application-menu.png'); await page.keyboard.press('Escape')
            await click(page, events, 'quick.NEW'); assert 'Drawing' in await page.title()
            await click(page, events, 'command.RECTANG'); await command(page, '0,0', '200,100', 'ZOOM', 'SELECTALL')
            assert 'tab.Polyline' in await bounds(page, events)
            await click(page, events, 'tab.Polyline'); await click(page, events, 'command.PEDIT'); await command(page, 'Width', '6')
            await shot('52-contextual-polyline-ribbon.png')
            await click(page, events, 'dock.properties.float'); assert placement(await preferences(),'properties')['dock'] == 'Floating'
            await shot('53-floating-properties.png')
            positions = await bounds(page, events); x,y,w,h = positions['dock.properties.drag']
            await page.mouse.move(x+15,y+h/2); await page.mouse.down(); await page.mouse.move(1590,400,steps=15); await page.mouse.up(); await page.wait_for_timeout(350)
            assert placement(await preferences(),'properties')['dock'] == 'Right', 'Actual header drag must dock at right edge'
            await click(page, events, 'dock.properties.pin'); assert placement(await preferences(),'properties')['autoHide']
            await click(page, events, 'dock.properties.tab'); await shot('54-auto-hide-palette.png')
            await page.keyboard.press('Escape'); await page.keyboard.press('Control+1'); await page.keyboard.press('Control+1')
            await command(page,'TOOLPALETTES'); assert placement(await preferences(),'tools')['visible']
            await click(page,events,'tools.category.3D'); await click(page,events,'tools.search')
            await page.wait_for_timeout(250); await page.keyboard.type('BOX',delay=30); await page.wait_for_timeout(300)
            positions=await bounds(page,events); x,y,w,h=positions['tools.list']
            await page.mouse.click(x+80,y+22); await command(page,'0,0','80,60','30')
            await bounds(page,events); assert any('model=True' in e.get('text','') for e in events[-20:])
            await shot('55-tool-palette-modeling.png')
            await click(page,events,'viewport.views'); await click(page,events,'view.Front')
            await bounds(page,events)
            state=next(e['text'] for e in reversed(events) if 'CADSPACE_UI_STATE:' in e.get('text',''))
            assert 'yaw=-90;' in state and 'pitch=0;' in state,state
            positions=await bounds(page,events); x,y,w,h=positions['viewport.cube']
            await page.mouse.click(x+43,y+43); await bounds(page,events)
            state=next(e['text'] for e in reversed(events) if 'CADSPACE_UI_STATE:' in e.get('text',''))
            assert abs(float(re.search(r'pitch=([^;]+)',state).group(1)))>30,state
            await shot('56-viewcube-corner.png')
            await command(page,'TOP','TOOLPALETTESCLOSE')
            await click(page,events,'layout.new'); assert 'layout.Layout2' in await bounds(page,events)
            await click(page,events,'tab.Layout'); await click(page,events,'command.LAYOUT_DELETE')
            assert 'layout.Layout2' not in await bounds(page,events)
            await command(page,'UNDO'); assert 'layout.Layout2' in await bounds(page,events)
            await command(page,'PROPERTIES','RIBBONCLOSE'); saved=await preferences(); assert saved['ribbonMinimized']
            await page.reload(wait_until='domcontentloaded'); await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000); await page.wait_for_timeout(2800)
            assert await preferences()==saved
            restored=await bounds(page,events); assert 'command.LINE' not in restored and 'ribbon.minimize' in restored
            await command(page,'RIBBON','CLEANSCREENON'); assert 'ribbon.minimize' not in await bounds(page,events)
            await command(page,'CLEANSCREENOFF'); assert 'ribbon.minimize' in await bounds(page,events)
            await shot('57-restored-workspace.png')
            await page.set_viewport_size({'width':800,'height':680}); await page.wait_for_timeout(500)
            small=await bounds(page,events); assert 'dock.properties.tab' in small and 'command.input' in small
            await shot('58-compact-workspace.png')
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-20:]
            (output/'workspace-preferences.json').write_text(json.dumps(saved,indent=2))
            print('PASS application/ribbon invocation, contextual tab, docking/auto-hide, tool-palette modeling, view menus/cube corners, layout lifecycle, persisted preferences, clean screen and compact layout')
        finally:
            await shot('workspace-last-state.png'); (output/'workspace-console.json').write_text(json.dumps(events,indent=2)); await browser.close()
asyncio.run(main())
