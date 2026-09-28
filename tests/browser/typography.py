"""Catch UI-font fallback using actual rendered title pixels, not a claimed FontFamily string."""
import asyncio
import json
import os
from pathlib import Path
from PIL import Image
from playwright.async_api import async_playwright


def title_ink_bounds(path):
    # At 1600x1000 the startup title alone occupies this part of the application bar.
    # The fixed title is 'Design studio.dxf  —  CadSpace', rendered at 11 layout units.
    image = Image.open(path).convert('RGB').crop((480, 4, 1160, 30))
    mask = Image.new('L', image.size)
    mask.putdata([255 if min(pixel) > 120 else 0 for pixel in image.getdata()])
    return mask.getbbox()


def assert_proportional_title(path):
    bounds = title_ink_bounds(path)
    assert bounds is not None, 'Startup title must have visible text pixels'
    width = bounds[2] - bounds[0]
    # The verified sans-serif title occupies about 160 px. The observed unwanted
    # monospace fallback occupies 197 px. Keep tolerance for raster antialiasing.
    assert 140 <= width <= 185, f'Expected proportional UI title, not monospace fallback: width={width}'
    return width


async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    records = []
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        try:
            # Independent contexts cover cold startup, including delayed font delivery.
            for delayed in (False, True):
                context = await browser.new_context(viewport={'width': 1600, 'height': 1000}, device_scale_factor=1)
                page = await context.new_page(); events = []; font_responses = []
                page.on('console', lambda e: events.append({'type': e.type, 'text': e.text}))
                page.on('pageerror', lambda e: events.append({'type': 'pageerror', 'text': str(e)}))
                page.on('response', lambda r: font_responses.append({'url': r.url, 'status': r.status}) if '/Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf' in r.url and '.manifest' not in r.url else None)
                if delayed:
                    async def delay_regular_font(route):
                        await asyncio.sleep(.8)
                        await route.continue_()
                    await page.route('**/Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf', delay_regular_font)
                name = '65-ui-typography-delayed.png' if delayed else '64-ui-typography.png'
                try:
                    await page.goto(os.environ.get('CADSPACE_URL', 'http://127.0.0.1:8177/CadSpace/'), wait_until='domcontentloaded')
                    await page.wait_for_function("document.title.includes('Design studio.dxf')", timeout=90000)
                    await page.wait_for_timeout(2800)
                    await page.screenshot(path=str(output / name), full_page=True)
                    width = assert_proportional_title(output / name)
                    assert any(r['status'] == 200 for r in font_responses), 'The explicit packaged regular UI font must be delivered'
                    assert not [e for e in events if e['type'] == 'pageerror'], events
                    records.append({'delayed': delayed, 'titleInkWidth': width, 'fontResponses': font_responses, 'events': events})
                finally:
                    await page.screenshot(path=str(output / ('typography-delayed-last.png' if delayed else 'typography-last.png')), full_page=True)
                    (output / 'typography-diagnostics.json').write_text(json.dumps({'completed': records, 'currentEvents': events, 'fontResponses': font_responses}, indent=2))
                    await context.close()
            print('PASS explicit packaged UI font and proportional title pixels, including delayed cold font delivery:', [r['titleInkWidth'] for r in records])
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
