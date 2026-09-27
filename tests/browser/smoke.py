"""Exercise the published Uno app; require visible shaded geometry, never accept a 2D fallback."""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright
from PIL import Image

async def main():
    output = Path("artifacts/browser-smoke")
    output.mkdir(parents=True, exist_ok=True)
    events = []
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True, args=[
            "--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"
        ])
        page = await browser.new_page(viewport={"width": 1600, "height": 1000}, device_scale_factor=1)
        page.on("console", lambda message: events.append({"type": message.type, "text": message.text}))
        page.on("pageerror", lambda error: events.append({"type": "pageerror", "text": str(error)}))
        page.on("requestfailed", lambda request: events.append({"type": "requestfailed", "url": request.url, "error": request.failure}))
        async def command(*values):
            for value in values:
                await page.keyboard.type(value)
                await page.keyboard.press("Enter")
            await page.wait_for_timeout(400)
        try:
            await page.goto(os.environ.get("CADSPACE_URL", "http://127.0.0.1:8177/CadSpace/"), wait_until="domcontentloaded", timeout=60000)
            await page.wait_for_function("document.title.includes('CadSpace')", timeout=90000)
            await page.locator("canvas").first.wait_for(state="visible", timeout=30000)
            await page.wait_for_timeout(3000)
            await page.screenshot(path=str(output / "01-drafting.png"), full_page=True)
            (output / "body.txt").write_text(await page.locator("body").inner_text(), encoding="utf-8")
            (output / "dom.html").write_text(await page.content(), encoding="utf-8")
            await command("CIRCLE", "100,100", "25")
            assert "*" in await page.title(), "A command must dirty the active document"
            await command("UNDO")
            assert "*" not in await page.title(), "Undo must restore the saved initial document"
            await page.keyboard.press("Control+n")
            await page.wait_for_timeout(500)
            await command("BOX", "0,0", "200,120", "80")
            assert "*" in await page.title(), "Mesh creation must change the drawing"
            await page.wait_for_timeout(2500)
            await page.screenshot(path=str(output / "02-mesh.png"), full_page=True)
            await page.mouse.move(650, 400)
            await page.mouse.down()
            await page.mouse.move(780, 450, steps=12)
            await page.mouse.up()
            await page.mouse.wheel(0, -240)
            await page.wait_for_timeout(600)
            await page.screenshot(path=str(output / "03-orbit.png"), full_page=True)
            messages = "\n".join(event.get("text", "") for event in events)
            assert "CADSPACE_GPU_FRAME: triangles=12" in messages, "The GPU renderer must draw the box's twelve triangles"
            for forbidden in ("readPixels: invalid", "glReadPixels: Invalid", "could not be initialized", "framebuffer readback failed"):
                assert forbidden.lower() not in messages.lower(), f"Rendering failed: {messages}"
            assert "CADSPACE_READBACK: RGBA" in messages, "Portable readback must be installed"
            image = Image.open(output / "02-mesh.png").convert("RGB")
            roi = image.crop((200, 240, 1120, 760))
            filled = sum(1 for r,g,b in roi.getdata() if min(r,g,b) > 75 and max(r,g,b) - min(r,g,b) < 55)
            assert filled > 15000, f"Expected shaded box pixels, got {filled}; a draw-call log is insufficient"
            before = image.crop((200, 240, 1120, 760))
            after = Image.open(output / "03-orbit.png").convert("RGB").crop((200, 240, 1120, 760))
            changed = sum(1 for a,b in zip(before.getdata(), after.getdata()) if sum(abs(x-y) for x,y in zip(a,b)) > 45)
            assert changed > 5000, f"Orbit/zoom must change rendered geometry, only {changed} pixels changed"
            assert "3D renderer initialization failed" not in messages, messages
            errors = [event for event in events if event["type"] == "pageerror"]
            assert not errors, f"Unhandled browser errors: {errors}"
            print(f"PASS browser startup, editing, undo, visible mesh ({filled} pixels) and orbit ({changed} changed pixels)")
        finally:
            await page.screenshot(path=str(output / "last-state.png"), full_page=True)
            (output / "console.json").write_text(json.dumps(events, indent=2), encoding="utf-8")
            await browser.close()

asyncio.run(main())
