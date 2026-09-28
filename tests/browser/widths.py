"""Exercise native wide polylines through real command input and rendered 2D/3D pixels."""
import asyncio
import json
import os
from pathlib import Path
from PIL import Image, ImageDraw
from ui_helpers import bounds
from playwright.async_api import async_playwright

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600,'height':1000}, device_scale_factor=1)
        events=[]
        page.on('console', lambda m: events.append({'type':m.type,'text':m.text}))
        page.on('pageerror', lambda e: events.append({'type':'pageerror','text':str(e)}))
        async def command(*values):
            await page.mouse.click(300,956)
            for value in values:
                await page.keyboard.type(value); await page.keyboard.press('Enter')
            await page.wait_for_timeout(450)
        async def shot(name):
            positions = await bounds(page, events)
            await page.mouse.move(300,956)
            await page.screenshot(path=str(output/name),full_page=True)
            image = Image.open(output/name).convert('RGB')
            # Mask actual navigation controls so their icons cannot satisfy the geometry assertion.
            mask = ImageDraw.Draw(image)
            for key, (x,y,w,h) in positions.items():
                if key.startswith('navigation.') or key.startswith('viewport.') and key != 'viewport.surface':
                    mask.rectangle((x-3,y-3,x+w+3,y+h+3), fill=(0,0,0))
            x,y,w,h = positions['viewport.surface']
            image = image.crop((int(x+4),int(y+42),int(x+w-4),int(y+h-34)))
            return sum(min(px)>75 and max(px)-min(px)<65 for px in image.getdata())
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000)
            await page.wait_for_timeout(2500)
            await page.keyboard.press('Control+n');await page.wait_for_timeout(350)
            await command('PLINEWID','8','PLINE','0,0','200,0','200,80','','ZOOM')
            wide2d=await shot('38-wide-polyline-2d.png')
            assert wide2d>12000, f'Native width must fill the draft viewport, got {wide2d}'
            await command('SELECTALL','PEDIT','WIDTH','0','QSELECT','POINT,*,Replace,All')
            thin2d=await shot('39-polyline-centerline.png')
            assert thin2d<wide2d*.3, (thin2d,wide2d)
            await command('UNDO','3DORBIT')
            wide3d=await shot('40-wide-polyline-gpu.png')
            assert wide3d>2000, f'Wide polylines must have actual GPU faces, got {wide3d}'
            await command('TOP','SELECTALL')
            await shot('41-polyline-properties.png')
            await command('PEDIT','REVERSE','PEDIT','CLOSE')
            await shot('42-polyline-closed.png')
            assert '*' in await page.title()
            project=None
            for _ in range(80):
                projects=await page.evaluate("""async () => {
                    const result=[];
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())){
                        const value=await CadSpaceRecoveryStorage.read(key);
                        if(value)result.push(JSON.parse(value.slice(value.indexOf('\\n')+1)));
                    }
                    return result;
                }""")
                for candidate in projects:
                    entities=candidate['drawing']['entities']
                    if len(entities)==1 and entities[0]['type']=='LWPOLYLINE' and entities[0].get('constantWidth')==8 and entities[0]['closed'] and entities[0]['vertices'][0]['point']==[200,80,0]:
                        project=candidate; break
                if project is not None:break
                await page.wait_for_timeout(250)
            assert project is not None, 'The actual checkpoint must retain width, reversed points and closure'
            (output/'width-checkpoint.json').write_text(json.dumps(project,indent=2))
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events
            print(f'PASS native polyline widths: 2D={wide2d}, centerline={thin2d}, GPU={wide3d}; PEDIT width/undo/reverse/close; Properties capture; native checkpoint')
        finally:
            await page.screenshot(path=str(output/'widths-last-state.png'),full_page=True)
            (output/'widths-console.json').write_text(json.dumps(events,indent=2))
            await browser.close()
asyncio.run(main())
