#!/usr/bin/env python3
"""Synthetic emulator only: create profile, generate and re-open public Relay QR.
Does not activate, register, send messages, or contact the Relay server.
"""
import base64
import io
import json
import re
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path
import zxingcpp
from PIL import Image

PACKAGE = "ua.mavka.mobile.relayqa"
OUT = Path("emulator-smoke-artifacts")
def adb(*args):
    return subprocess.check_output(["adb", *args])
def tree():
    adb("shell", "uiautomator", "dump", "/sdcard/relay-window.xml")
    return ET.fromstring(adb("shell", "cat", "/sdcard/relay-window.xml"))
def bounds(n):
    return list(map(int, re.findall(r"\d+", n.attrib["bounds"])))
def swipe():
    adb("shell", "input", "swipe", "500", "1700", "500", "550", "300")
    time.sleep(.5)
def tap(label, scroll=True):
    for _ in range(14 if scroll else 1):
        root = tree()
        for n in root.iter("node"):
            if label in (n.get("text"), n.get("content-desc")):
                x1,y1,x2,y2 = bounds(n)
                if x2 > x1 and y2 > y1:
                    adb("shell", "input", "tap", str((x1+x2)//2), str((y1+y2)//2))
                    time.sleep(.6)
                    return
        if scroll: swipe()
    raise AssertionError("UI element missing: " + label)
def menu(label):
    tap("Відкрити праве меню команд", False)
    tap(label)
def field(hint, value):
    tap(hint)
    adb("shell", "input", "text", value)
    adb("shell", "input", "keyevent", "4")
    time.sleep(.4)
def open_qr():
    menu("Синхронізація Gmail")
    tap("Cloudflare Relay →")
    tap("Показати QR підключення для ПК")
    for _ in range(16):
        screenshot = adb("exec-out", "screencap", "-p")
        found = zxingcpp.read_barcodes(Image.open(io.BytesIO(screenshot)))
        for qr in found:
            try: data = json.loads(qr.text)
            except (ValueError, TypeError): continue
            if data.get("format") == "MAVKA_RELAY_DEVICE_REQUEST_V1":
                return data, screenshot
        swipe()
    root = tree()
    OUT.joinpath("relay-ui-failure.xml").write_bytes(ET.tostring(root))
    raise AssertionError("Relay QR was not decodable on screen")

OUT.mkdir(exist_ok=True)
menu("Профіль користувача")
field("Назва підрозділу", "SYNTHETIC-QA-UNIT")
field("Прізвище Ім’я По батькові", "SYNTHETIC-QA-USER")
field("Звання", "SYNTHETIC-QA-RANK")
field("Посада користувача", "SYNTHETIC-QA-POSITION")
tap("Зберегти профіль")
first, screenshot = open_qr()
assert set(first) == {"format", "server", "mainDeviceId", "deviceId", "clientId", "publicSigningKey", "publicEncryptionKey"}
assert first["server"] == "https://mavka-relay.arudichik.workers.dev"
assert first["mainDeviceId"] == "5110f288-ed5f-4d7f-afb5-2c2501f50076"
assert re.fullmatch(r"[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}", first["deviceId"])
assert first["deviceId"] != first["clientId"]
for name in ("publicSigningKey", "publicEncryptionKey"):
    key = first[name]
    assert set(key) == {"kty", "crv", "x", "y"}
    assert key["kty"] == "EC" and key["crv"] == "P-256"
    assert all(len(base64.urlsafe_b64decode(key[c] + "=")) == 32 for c in ("x", "y"))
assert first["publicSigningKey"] != first["publicEncryptionKey"]
OUT.joinpath("relay-public-qr-synthetic.png").write_bytes(screenshot)
OUT.joinpath("relay-public-request-synthetic.json").write_text(json.dumps(first, ensure_ascii=False, indent=2))
adb("shell", "am", "force-stop", PACKAGE)
adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")
time.sleep(5)
second, _ = open_qr()
assert second == first, "Identity/profile changed after restarting the app"
print("ANDROID KEYSTORE P-256 QR, PUBLIC-ONLY PAYLOAD, AND RESTART STABILITY: PASS. NO NETWORK ENROLLMENT.")
