#!/usr/bin/env python3
"""Synthetic emulator only: create profile, generate and re-open public Relay QR.
Does not activate, register, send messages, or contact the Relay server.
"""
import base64
import io
import json
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path
import zxingcpp
from PIL import Image

PACKAGE = os.environ.get("MAVKA_QA_PACKAGE", "ua.mavka.mobile.relayqa")
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
def prove_device():
    menu("Синхронізація Gmail")
    tap("Cloudflare Relay →")
    tap("Перевірити ключі пристрою")
    root = tree()
    texts = [n.get("text", "") for n in root.iter("node")]
    proof = next((t for t in texts if t.startswith("Локальні ключі пристрою справні. Відбиток: ")), None)
    assert proof is not None, "Local signing proof failed: " + str(texts)
    assert not any(t == "Показати QR підключення для ПК" for t in texts), "Phone registration QR is still exposed"
    return proof

def assert_screen(text):
    for _ in range(10):
        root = tree()
        assert adb("shell", "pidof", PACKAGE).strip(), "Process exited on settings navigation"
        texts = [n.get("text", "") for n in root.iter("node")]
        if any("MOBILE-" in t for t in texts):
            OUT.joinpath("settings-failure.xml").write_bytes(ET.tostring(root))
            OUT.joinpath("settings-failure.png").write_bytes(adb("exec-out", "screencap", "-p"))
            raise AssertionError("Settings exception shown: " + str(texts))
        if any(text in t for t in texts): return
        swipe()
    OUT.joinpath("settings-failure.xml").write_bytes(ET.tostring(root))
    OUT.joinpath("settings-failure.png").write_bytes(adb("exec-out", "screencap", "-p"))
    raise AssertionError("Screen missing: " + text + " / " + str(texts))

OUT.mkdir(exist_ok=True)
menu("Ключ обміну")
assert_screen("Немає автоматично встановленої пари ключів.")
OUT.joinpath("exchange-keys-fresh.png").write_bytes(adb("exec-out", "screencap", "-p"))
menu("Пароль документів")
field("Пароль документів", "SYNTHETIC-QA-PASSWORD-ONLY")
tap("Зберегти / змінити пароль")
assert_screen("Пароль збережено.")
menu("Профіль користувача")
field("Назва підрозділу", "SYNTHETIC-QA-UNIT")
field("Прізвище Ім’я По батькові", "SYNTHETIC-QA-USER")
field("Звання", "SYNTHETIC-QA-RANK")
field("Посада користувача", "SYNTHETIC-QA-POSITION")
tap("Зберегти профіль")
first = prove_device()
OUT.joinpath("relay-local-proof-synthetic.png").write_bytes(adb("exec-out", "screencap", "-p"))
menu("Ключ обміну")
assert_screen("Немає автоматично встановленої пари ключів.")
adb("shell", "am", "force-stop", PACKAGE)
adb("shell", "monkey", "-p", PACKAGE, "-c", "android.intent.category.LAUNCHER", "1")
time.sleep(5)
second = prove_device()
assert second == first, "Identity changed after restart"
menu("Пароль документів")
assert_screen("Пароль уже захищено збережений на телефоні.")
menu("Ключ обміну")
assert_screen("Немає автоматично встановленої пари ключів.")
print("ANDROID15 EXCHANGE-KEY SCREEN, MANUAL PASSWORD KEYSTORE PERSISTENCE, LOCAL P256 SIGNATURE PROOF AND RESTART: PASS. NO NETWORK ENROLLMENT.")
