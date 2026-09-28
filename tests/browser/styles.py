"""Actual Skia and shader linetypes plus the custom layer/style dialogs."""
import asyncio
import json
import os
from pathlib import Path
from PIL import Image
from playwright.async_api import async_playwright

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600, 'height':1000}, device_scale_factor=1)
        events=[]
        page.on('console', lambda m: events.append({'type':m.type,'text':m.text}))
        page.on('pageerror', lambda e: events.append({'type':'pageerror','text':str(e)}))
        async def command(*values):
            await page.mouse.click(300,956)
            for value in values:
                await page.keyboard.type(value); await page.keyboard.press('Enter')
            await page.wait_for_timeout(500)
        async def capture(name):
            await page.mouse.move(300,956)
            await page.screenshot(path=str(output/name),full_page=True)
            return Image.open(output/name).convert('RGB').crop((200,260,1120,750))
        def ink(image):
            return sum(min(r,g,b)>90 and max(r,g,b)-min(r,g,b)<65 for r,g,b in image.getdata())
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(2500)
            assert await page.evaluate("typeof globalThis.CadSpaceRecoveryStorage?.write === 'function'"), 'Published bootstrap must include the storage adapter'
            await page.keyboard.press('Control+n');await page.wait_for_timeout(300)
            await command('CELTYPE','DASHED','LINE','0,0','240,0','','ZOOM')
            dashed2d=ink(await capture('27-dashed-drafting.png'))
            await command('3DORBIT');dashed3d=ink(await capture('28-dashed-gpu.png'))
            await command('SELECTALL','ERASE','CELTYPE','CONTINUOUS','LINE','0,0','240,0','')
            continuous3d=ink(await capture('29-continuous-gpu.png'))
            assert continuous3d>200 and continuous3d*.35<dashed3d<continuous3d*.9, (dashed3d,continuous3d)
            await command('TOP','ZOOM');continuous2d=ink(await capture('30-continuous-drafting.png'))
            assert continuous2d>500 and continuous2d*.35<dashed2d<continuous2d*.9,(dashed2d,continuous2d)
            await command('3DORBIT')
            model_before = await capture('33-model-before-manager.png')
            await command('LAYER');await capture('31-layer-properties-manager.png')
            # Text-only reflected getters used to vanish from the trimmed application.
            # Assert visible glyphs in each previously blank first-row field, not just an open dialog.
            row = Image.open(output/'31-layer-properties-manager.png').convert('RGB')
            for name, box in {
                'On': (645, 327, 686, 344), 'Lock': (696, 327, 743, 344),
                'Linetype': (850, 327, 999, 344), 'Weight': (1008, 327, 1068, 344),
            }.items():
                assert sum(max(pixel)<110 for pixel in row.crop(box).getdata())>4, f'Missing rendered {name} cells after trimming'
            await page.keyboard.press('Escape');await page.wait_for_timeout(300)
            model_after = await capture('34-model-after-layer-manager.png')
            assert sum(sum(abs(a-b) for a,b in zip(x,y))>40 for x,y in zip(model_before.getdata(),model_after.getdata()))<15, 'Layer manager must not change the model camera or switch to 2D'
            await command('LINETYPE');await capture('32-linetype-manager.png')
            await page.keyboard.press('Escape');await page.wait_for_timeout(300)
            model_after = await capture('35-model-after-linetype-manager.png')
            assert sum(sum(abs(a-b) for a,b in zip(x,y))>40 for x,y in zip(model_before.getdata(),model_after.getdata()))<15, 'Linetype manager must not switch the model view'
            await command('LTSCALE','2');assert '*' in await page.title()
            # Perform actual manager edits and check the application's real saved native checkpoint.
            # No test-only model API: persistence is the same IndexedDB adapter used by recovery.
            async def fill(x, y, text):
                await page.mouse.click(x,y);await page.keyboard.press('Control+a');await page.keyboard.type(text)
            await command('LAYER')
            await fill(500,606,'QA_TEMP');await page.mouse.click(675,606);await page.wait_for_timeout(300)
            await page.mouse.click(490,376);await page.wait_for_timeout(200)
            await fill(490,673,'QA_LAYER');await fill(650,673,'12ABEF');await fill(889,673,'0.50')
            await page.mouse.click(1128,673);await page.wait_for_timeout(350)
            await page.mouse.click(490,376);await page.mouse.click(778,606);await page.wait_for_timeout(200)
            await capture('36-layer-manager-edited.png')
            await page.keyboard.press('Escape');await page.wait_for_timeout(200)
            await command('TOP','LINE','0,20','240,20','')
            # page.evaluate awaits the promise before returning its serialized value.
            # Do not use wait_for_function with an async predicate: the Promise itself
            # can satisfy its truthiness check before the checkpoint is committed.
            project = None
            deadline = asyncio.get_running_loop().time() + 20
            last_checkpoints = []
            while asyncio.get_running_loop().time() < deadline:
                last_checkpoints = await page.evaluate("""async () => {
                    const projects = [];
                    for (const key of JSON.parse(await CadSpaceRecoveryStorage.list())) {
                        const value = await CadSpaceRecoveryStorage.read(key);
                        if (value) projects.push(JSON.parse(value.slice(value.indexOf('\\n') + 1)));
                    }
                    return projects;
                }""")
                assert isinstance(last_checkpoints, list), 'Checkpoint reads must return resolved data'
                for candidate in last_checkpoints:
                    assert isinstance(candidate, dict) and candidate.get('format') == 'CadSpace'
                    drawing = candidate['drawing']
                    layer = next((x for x in drawing['layers'] if x['name'] == 'QA_LAYER'), None)
                    new_line = any(x['type'] == 'LINE' and x['layer'] == 'QA_LAYER' and
                                   x['a'] == [0, 20, 0] and x['b'] == [240, 20, 0]
                                   for x in drawing['entities'])
                    if (layer and layer['color'] == 0xFF12ABEF and layer['weight'] == 0.5 and
                            not any(x['name'] == 'QA_TEMP' for x in drawing['layers']) and
                            drawing['linetypeScale'] == 2 and len(drawing['entities']) == 2 and new_line):
                        project = candidate
                        break
                if project is not None:
                    break
                await page.wait_for_timeout(250)
            (output/'layer-manager-checkpoints-observed.json').write_text(json.dumps(last_checkpoints, indent=2), encoding='utf8')
            assert isinstance(project, dict), 'No committed native checkpoint contains the expected layer properties and line geometry'
            (output/'layer-manager-checkpoint.json').write_text(json.dumps(project,indent=2),encoding='utf8')
            await command('LAYER')
            await fill(500,258,'QA_LAYER');await page.wait_for_timeout(250)
            await capture('37-layer-manager-filtered.png')
            await page.keyboard.press('Escape');await page.wait_for_timeout(200)
            errors=[e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')]
            assert not errors,errors
            print(f'PASS linetypes: 2D ink={dashed2d}/{continuous2d}, GPU ink={dashed3d}/{continuous3d}; compiled layer cells; preserved 3D camera; UI layer creation/rename/color/weight/current/filter; validated native checkpoint; global scale edit')
        finally:
            await page.screenshot(path=str(output/'styles-last-state.png'),full_page=True)
            (output/'styles-console.json').write_text(json.dumps(events,indent=2),encoding='utf8')
            await browser.close()
asyncio.run(main())
