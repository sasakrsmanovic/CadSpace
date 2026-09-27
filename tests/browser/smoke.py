"""Run against the published Uno application; retain logs/screenshots on failure."""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright

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
        try:
            await page.goto(os.environ.get("CADSPACE_URL", "http://127.0.0.1:8177/CadSpace/"), wait_until="domcontentloaded", timeout=60000)
            await page.wait_for_function("document.title.includes('CadSpace')", timeout=90000)
            await page.locator("canvas").first.wait_for(state="visible", timeout=30000)
            await page.wait_for_timeout(3000)
            await page.screenshot(path=str(output / "01-drafting.png"), full_page=True)
            (output / "body.txt").write_text(await page.locator("body").inner_text(), encoding="utf-8")
            (output / "dom.html").write_text(await page.content(), encoding="utf-8")
            # Exercise the shared command entry via focus established by the app.
            await page.keyboard.type("CIRCLE")
            await page.keyboard.press("Enter")
            await page.keyboard.type("100,100")
            await page.keyboard.press("Enter")
            await page.keyboard.type("25")
            await page.keyboard.press("Enter")
            await page.wait_for_timeout(600)
            assert "*" in await page.title(), "A command must dirty the active document"
            await page.keyboard.type("UNDO")
            await page.keyboard.press("Enter")
            await page.wait_for_timeout(300)
            assert "*" not in await page.title(), "Undo must restore the saved initial document"
            await page.keyboard.type("3DORBIT")
            await page.keyboard.press("Enter")
            await page.wait_for_timeout(2500)
            await page.screenshot(path=str(output / "02-3d-view.png"), full_page=True)
            await page.mouse.move(650, 400)
            await page.mouse.down()
            await page.mouse.move(780, 450, steps=12)
            await page.mouse.up()
            await page.mouse.wheel(0, -240)
            await page.wait_for_timeout(400)
            await page.screenshot(path=str(output / "03-orbit.png"), full_page=True)
            errors = [event for event in events if event["type"] == "pageerror"]
            assert not errors, f"Unhandled browser errors: {errors}"
            print("PASS browser startup, command editing, undo and orbit input; screenshots retained")
        finally:
            await page.screenshot(path=str(output / "last-state.png"), full_page=True)
            (output / "console.json").write_text(json.dumps(events, indent=2), encoding="utf-8")
            await browser.close()

asyncio.run(main())
