"""Exercise real published-app rendering and CAD UI behavior; retain screenshots on failure."""
import asyncio,json,os,re
from pathlib import Path
from playwright.async_api import async_playwright
from PIL import Image, ImageDraw
from ui_helpers import click as click_ui, bounds as ui_bounds

ROI=(200,240,1120,760)
def crop(path):return Image.open(path).convert('RGB').crop(ROI)
def filled(image):return sum(min(r,g,b)>75 and max(r,g,b)-min(r,g,b)<55 for r,g,b in image.getdata())
def blue_bounds(path, controls):
    x,y,w,h = controls['viewport.surface']
    left,top,right,bottom = int(x),int(y),int(x+w),int(y+h)
    image=Image.open(path).convert('RGB').crop((left,top,right,bottom))
    mask=Image.new('L',image.size);mask.putdata([255 if b-r>50 and b-g>20 and b>120 else 0 for r,g,b in image.getdata()])
    # Exclude navigation chrome, never the viewport.surface container itself.
    # This keeps the entire selected drawing and its grip pixels in the assertion.
    draw=ImageDraw.Draw(mask)
    for name,(cx,cy,cw,ch) in controls.items():
        if name.startswith('navigation.') or name.startswith('viewport.') and name != 'viewport.surface':
            draw.rectangle((cx-left-1,cy-top-1,cx+cw-left+1,cy+ch-top+1),fill=0)
    box=mask.getbbox();assert box is not None,'Expected editable blue grips'
    return (box[0]+left,box[1]+top,box[2]+left,box[3]+top)

def difference(a,b):return sum(sum(abs(x-y) for x,y in zip(p,q))>45 for p,q in zip(a.getdata(),b.getdata()))

async def main():
    output=Path('artifacts/browser-smoke');output.mkdir(parents=True,exist_ok=True);events=[]
    async with async_playwright() as p:
        browser=await p.chromium.launch(headless=True,args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
        page=await browser.new_page(viewport={'width':1600,'height':1000},device_scale_factor=1)
        page.on('console',lambda m:events.append({'type':m.type,'text':m.text}))
        page.on('pageerror',lambda e:events.append({'type':'pageerror','text':str(e)}))
        page.on('requestfailed',lambda r:events.append({'type':'requestfailed','url':r.url,'error':r.failure}))
        async def command(*values):
            await page.mouse.click(300,956)
            for value in values:await page.keyboard.type(value);await page.keyboard.press('Enter')
            await page.wait_for_timeout(500)
        async def shot(name):
            await page.screenshot(path=str(output/name),full_page=True);return crop(output/name)
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded',timeout=60000)
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000)
            await page.locator('canvas').first.wait_for(state='visible',timeout=30000);await page.wait_for_timeout(3000)
            await shot('01-workspace.png')
            await page.mouse.click(300,956);await page.keyboard.type('CIR');await page.keyboard.press('Tab');await page.keyboard.press('Enter')
            await command('100,100','25');assert '*' in await page.title(),'Completion and circle command must edit the document'
            await command('UNDO');assert '*' not in await page.title()
            await page.keyboard.press('Control+n');await page.wait_for_timeout(500)
            await command('BOX','0,0','200,120','80');await page.wait_for_timeout(2000)
            mesh=await shot('02-shaded.png');assert filled(mesh)>15000,'Expected visible shaded mesh, not a fallback'
            logs=lambda:'\n'.join(e.get('text','') for e in events)
            async def render_stats():
                await page.wait_for_timeout(250);await command('RENDERSTATS')
                values=re.findall(r'CADSPACE_RENDER_STATS: scene=(\d+); overlay=(\d+); model=(\d+)',logs())
                assert values,'Render statistics were not reported'
                return tuple(map(int,values[-1]))
            model_before=await render_stats()
            for i in range(20):await page.mouse.move(320+i*14,340+(i%3)*5);await page.wait_for_timeout(20)
            model_after=await render_stats()
            assert model_after[2]==model_before[2],f'Idle pointer must not rerecord 3D: {model_before} -> {model_after}'
            before=re.findall(r'CADSPACE_GPU_UPLOADS: geometry=(\d+)',logs())[-1]
            await page.mouse.click(650,520);await page.wait_for_timeout(600);selected=await shot('03-selected.png')
            blue=sum(b-r>55 and b-g>20 and b>100 for r,g,b in selected.getdata());assert blue>10000,'Mesh must highlight after face picking'
            assert re.findall(r'CADSPACE_GPU_UPLOADS: geometry=(\d+)',logs())[-1]==before,'Selection must not upload geometry again'
            await page.mouse.click(230,280)
            await command('VSCURRENT','Wireframe');wire=await shot('04-wireframe.png');assert filled(wire)<filled(mesh)*.1
            await command('VSCURRENT','HiddenLine');hidden=await shot('05-hidden-line.png');assert difference(wire,hidden)>100
            await command('VSCURRENT','Shaded');shaded=await shot('06-shaded-only.png');assert filled(shaded)>15000
            await command('VSCURRENT','ShadedEdges','PERSPECTIVE','0');ortho=await shot('07-orthographic.png');assert difference(mesh,ortho)>2000
            await command('CLIP3D','0,0,40,0,0,1');clipped=await shot('08-clipped.png');assert filled(clipped)<filled(ortho)*.8
            await command('CLIP3D','OFF','PERSPECTIVE','1')
            await command('TEXT','80,60,160','GPU LABEL');text=await shot('09-text.png')
            assert 'CADSPACE_GPU_TEXT: labels=1' in logs();assert difference(mesh,text)>50
            await command('UNDO');before_image=await shot('10-restored.png')
            await page.mouse.move(650,400);await page.mouse.down();await page.mouse.move(780,450,steps=12);await page.mouse.up();await page.mouse.wheel(0,-240);await page.wait_for_timeout(600)
            assert difference(before_image,await shot('11-orbit.png'))>5000
            await page.keyboard.press('F2');await page.wait_for_timeout(200);await shot('12-command-history.png');await page.keyboard.press('F2')
            await page.keyboard.press('Control+1');await page.wait_for_timeout(200);await shot('13-palette-hidden.png');await page.keyboard.press('Control+1')
            await command('TOP','LINE');await page.mouse.move(450,380);await page.wait_for_timeout(200);await shot('14-dynamic-input.png');await page.keyboard.press('Escape')
            # A simple known document makes exact grip coordinates and undo pixels observable.
            await page.keyboard.press('Control+n');await page.wait_for_timeout(300)
            await command('LINE','0,0','200,0','','CIRCLE','50,50','15','ZOOM','QSELECT','LINE,*,Replace,All')
            original=await shot('15-editable-grips.png');box=blue_bounds(output/'15-editable-grips.png',await ui_bounds(page,events))
            end=(box[2]-4,(box[1]+box[3])/2);assert box[2]-box[0]>800 and box[3]-box[1]>=6,box
            static_before=await render_stats()
            for i in range(20):await page.mouse.move(420+i*12,350+(i%4)*4);await page.wait_for_timeout(20)
            static_after=await render_stats()
            assert static_after[0]==static_before[0] and static_after[1]>static_before[1],f'Cursor must update overlay only: {static_before} -> {static_after}'
            await page.mouse.move(*end);await page.mouse.down();await page.mouse.move(end[0]-60,end[1]-90,steps=10)
            await shot('16-grip-preview.png');await page.mouse.up();await page.mouse.move(300,956);await page.wait_for_timeout(300)
            moved=await shot('17-grip-committed.png');assert difference(original,moved)>500,'Grip drag must change actual drawing geometry'
            await command('UNDO');assert difference(original,await shot('18-grip-undo.png'))<15,'One undo must restore grip edit exactly'
            await page.mouse.move(*end);await page.mouse.down();await page.mouse.move(end[0]-50,end[1]-70,steps=10)
            await page.keyboard.press('Escape');await page.mouse.up();await page.mouse.move(300,956);await page.wait_for_timeout(300)
            assert difference(original,await shot('19-grip-cancelled.png'))<15,'Escape during a grip drag must not commit'
            await command('STRETCH','190,-5','210,5','0,0','-10,30')
            assert difference(original,await shot('20-stretch.png'))>500,'Crossing STRETCH must move the enclosed endpoint'
            await command('UNDO');assert difference(original,await shot('21-stretch-undo.png'))<15
            # The ribbon dialog is a real selection workflow; defaults select both visible objects.
            await click_ui(page,events,"command.QSELECT");await page.wait_for_timeout(400);await shot('22-quick-select.png')
            await page.keyboard.press('Enter');await page.wait_for_timeout(400)
            await command('ERASE');empty=await shot('23-quick-select-erased.png')
            assert difference(original,empty)>1000,'Quick Select dialog must select both primitives for erase'
            await command('UNDO');await shot('24-quick-select-undo.png')
            (output/'invalidation.json').write_text(json.dumps({'idle3dBefore':model_before,'idle3dAfter':model_after,'cursor2dBefore':static_before,'cursor2dAfter':static_after},indent=2))
            for marker in ('CADSPACE_READBACK: RGBA','CADSPACE_GPU_FRAME: triangles=12','CADSPACE_PICK: count=1'):assert marker in logs(),marker
            for forbidden in ('readPixels: invalid','glReadPixels: Invalid','could not be initialized','framebuffer readback failed','3D renderer error','3D renderer initialization failed'):assert forbidden.lower() not in logs().lower(),logs()
            assert not [e for e in events if e['type']=='pageerror'],logs()
            # Isolated origin storage: wait for the real periodic checkpoint, reload, then restore.
            recovery_context=await browser.new_context(viewport={'width':1600,'height':1000},device_scale_factor=1)
            recovery_page=await recovery_context.new_page();recovery_events=[]
            recovery_page.on('console',lambda m:recovery_events.append(m.text))
            recovery_page.on('pageerror',lambda e:events.append({'type':'pageerror','text':str(e)}))
            async def wait_recovery(marker):
                for _ in range(300):
                    if any(marker in x for x in recovery_events):return
                    await recovery_page.wait_for_timeout(100)
                raise AssertionError('Missing recovery event '+marker+'; '+str(recovery_events))
            try:
                await recovery_page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
                await recovery_page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await recovery_page.wait_for_timeout(3000)
                await wait_recovery('CADSPACE_RECOVERY: ready backend=IndexedDB')
                await recovery_page.keyboard.press('Control+n');await recovery_page.wait_for_timeout(400);await recovery_page.mouse.click(300,956)
                for value in ('LINE','0,0','200,0','','ZOOM'):
                    await recovery_page.keyboard.type(value);await recovery_page.keyboard.press('Enter')
                await wait_recovery('CADSPACE_RECOVERY: saved generation=1 objects=1')
                # The completion event must guarantee durability; reload without a flush delay.
                await recovery_page.reload(wait_until='domcontentloaded');await recovery_page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await recovery_page.wait_for_timeout(3000)
                await recovery_page.keyboard.press("Control+Shift+r");await recovery_page.wait_for_timeout(400)
                await recovery_page.screenshot(path=str(output/'25-recovery-dialog.png'),full_page=True)
                await recovery_page.keyboard.press('Enter');await wait_recovery('CADSPACE_RECOVERY: restored objects=1')
                assert '*' in await recovery_page.title(),'Recovered drawing must remain unsaved'
                await recovery_page.screenshot(path=str(output/'26-recovered-drawing.png'),full_page=True)
                assert not [e for e in events if e['type']=='pageerror'],logs()
            finally:
                await recovery_page.screenshot(path=str(output/'recovery-last-state.png'),full_page=True)
                (output/'recovery-console.json').write_text(json.dumps(recovery_events,indent=2))
                await recovery_context.close()
            print('PASS grips, STRETCH, Quick Select, recovery after reload, static/overlay invalidation; rendered workspace, completion, editing, mesh pixels, indexed picking, selection-only uploads, visual styles, clipping, GPU text, projection, orbit, history, palette and dynamic prompts')
        finally:
            await page.screenshot(path=str(output/'last-state.png'),full_page=True)
            (output/'console.json').write_text(json.dumps(events,indent=2),encoding='utf-8')
            await browser.close()
asyncio.run(main())
