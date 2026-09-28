"""Locate real rendered Uno controls through read-only UISTATS, then use normal pointer/keyboard input."""
import asyncio
import json
import re

async def command(page, *values):
    size = page.viewport_size
    await page.mouse.click(min(300, size['width'] * .4), size['height'] - 44)
    await page.wait_for_timeout(100)
    for value in values:
        await page.keyboard.type(value)
        await page.keyboard.press('Enter')
    await page.wait_for_timeout(350)

async def bounds(page, events):
    begin = len(events)
    await page.keyboard.press('Control+Shift+F12')
    for _ in range(40):
        for e in reversed(events[begin:]):
            found = re.search(r'CADSPACE_UI_BOUNDS:(\{.*\})', e.get('text', ''))
            if found:
                result = json.loads(found.group(1))
                assert 'command.input' in result, 'Diagnostics must describe the actual command control'
                return result
        await page.wait_for_timeout(100)
    raise AssertionError('No rendered control diagnostics: ' + str(events[-10:]))

async def click(page, events, control):
    data = await bounds(page, events)
    assert control in data, f'Missing visible control {control}; available={list(data)}'
    x, y, w, h = data[control]
    await page.mouse.click(x + w / 2, y + h / 2)
    await page.wait_for_timeout(300)
    return data
