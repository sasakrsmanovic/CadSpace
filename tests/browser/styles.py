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
            await command('LAYER');await capture('31-layer-properties-manager.png')
            await page.keyboard.press('Escape');await page.wait_for_timeout(300)
            await command('LINETYPE');await capture('32-linetype-manager.png')
            await page.keyboard.press('Escape');await page.wait_for_timeout(300)
            await command('LTSCALE','2');assert '*' in await page.title()
            errors=[e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')]
            assert not errors,errors
            print(f'PASS linetypes: 2D ink={dashed2d}/{continuous2d}, GPU ink={dashed3d}/{continuous3d}; Layer and Linetype dialogs; global scale edit')
        finally:
            await page.screenshot(path=str(output/'styles-last-state.png'),full_page=True)
            (output/'styles-console.json').write_text(json.dumps(events,indent=2),encoding='utf8')
            await browser.close()
asyncio.run(main())
