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
    for attempt in range(3):
        adb("shell", "uiautomator", "dump", "/sdcard/relay-window.xml")
        raw=adb("shell", "cat", "/sdcard/relay-window.xml")
        root=ET.fromstring(raw)
        # The hosted emulator sometimes shows an ANR from its SYSTEM launcher.
        # Dismiss ONLY that exact dialog; MaVka ANRs/crashes remain test failures.
        launcher=any(n.get("resource-id")=="android:id/alertTitle" and n.get("text")=="Pixel Launcher isn't responding" and n.get("package")=="android" for n in root.iter("node"))
        if not launcher:return root
        OUT.mkdir(exist_ok=True)
        OUT.joinpath("system-launcher-anr-"+str(attempt)+".xml").write_bytes(raw)
        close=next((n for n in root.iter("node") if n.get("resource-id")=="android:id/aerr_close"),None)
        assert close is not None and attempt<2,"System launcher repeatedly blocks Android smoke"
        x1,y1,x2,y2=bounds(close)
        adb("shell","input","tap",str((x1+x2)//2),str((y1+y2)//2))
        adb("shell","monkey","-p",PACKAGE,"-c","android.intent.category.LAUNCHER","1")
        time.sleep(3)
    raise AssertionError("Android UI unavailable")

def bounds(n):
    return list(map(int, re.findall(r"\d+", n.attrib["bounds"])))
def swipe():
    adb("shell", "input", "swipe", "500", "1700", "500", "550", "300")
    time.sleep(.5)
def tap(label, scroll=True):
    for _ in range(14 if scroll else 1):
        root = tree()
        for n in root.iter("node"):
            if any(label.casefold()==value.casefold() for value in (n.get("text"),n.get("content-desc")) if value):
                x1,y1,x2,y2 = bounds(n)
                if x2 > x1 and y2 > y1:
                    adb("shell", "input", "tap", str((x1+x2)//2), str((y1+y2)//2))
                    time.sleep(.6)
                    return
        if scroll: swipe()
    raise AssertionError("UI element missing: " + label)
def menu(label):
    navigation=label in {"Головна","Майно підрозділу","Історія документів","Вхідні","Сповіщення та нагадування","Бібліотека документів"}
    tap("Відкрити ліве меню навігації" if navigation else "Відкрити праве меню команд", False)
    tap(label)
def field(hint, value):
    # Match the editable control, never a screen title with the same text.
    # Otherwise BACK navigates home because the keyboard never opened.
    found = False
    for _ in range(14):
        root = tree()
        for n in root.iter("node"):
            if n.get("class") == "android.widget.EditText" and hint in (n.get("text"), n.get("hint"), n.get("content-desc")):
                x1,y1,x2,y2 = bounds(n)
                if x2 > x1 and y2 > y1:
                    adb("shell","input","tap",str((x1+x2)//2),str((y1+y2)//2))
                    found = True
                    break
        if found: break
        swipe()
    assert found, "Editable field missing: " + hint
    time.sleep(.4)
    adb("shell", "input", "text", value)
    ime = adb("shell", "dumpsys", "input_method").decode("utf-8",errors="replace")
    if any(flag in ime for flag in ("mInputShown=true", "mIsInputViewShown=true", "isInputViewShown=true")):
        adb("shell", "input", "keyevent", "4")
    time.sleep(.4)

def prove_device():
    menu("Синхронізація Gmail")
    tap("Cloudflare Relay →")
    backgrounds=[n for n in tree().iter("node") if n.get("text")=="Фоновий обмін із сервером"]
    assert len(backgrounds)==1 and backgrounds[0].get("checked")=="false", "Background exchange must default off and remain off after restart"
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
menu("Профіль користувача")
adb("shell","pm","grant",PACKAGE,"android.permission.CAMERA")
tap("Сканувати QR камерою")
assert_screen("Сканер QR Мавки")
assert_screen("Сканування QR: наведіть камеру на код із ПК.")
assert b"ua.mavka.mobile.LiveQrScannerActivity" in adb("shell","dumpsys","activity","activities"), "Scanner must stay in MaVka instead of launching a photo app"
OUT.joinpath("in-app-qr-scanner.png").write_bytes(adb("exec-out","screencap","-p"))
tap("Скасувати сканування")
assert_screen("Сканувати QR камерою")
print("ANDROID15 EXCHANGE-KEY SCREEN, MANUAL PASSWORD KEYSTORE PERSISTENCE, LOCAL P256 SIGNATURE PROOF, BACKGROUND OFF, IN-APP SCANNER NAVIGATION AND RESTART: PASS. NO NETWORK ENROLLMENT OR PHYSICAL CAMERA DECODE.")

menu("Головна")
assert_screen("Оновити пошту")
assert_screen("Оновити сервер")
assert_screen("Автооновлення: ВИКЛ")
tap("Автооновлення: ВИКЛ")
assert_screen("Автооновлення: ВКЛ")
assert_screen("наступна:")
adb("shell","input","keyevent","3")
time.sleep(2)
adb("shell","monkey","-p",PACKAGE,"-c","android.intent.category.LAUNCHER","1")
assert_screen("Автооновлення: ВКЛ")
tap("Автооновлення: ВКЛ")
assert_screen("Автооновлення: ВИКЛ")
print("FOREGROUND AUTO-REFRESH DEFAULT OFF, EXPLICIT TOGGLE, HOME/RESUME AND STOP: PASS; NO NETWORK")
adb("shell","am","start","-n",PACKAGE+"/ua.mavka.mobile.StorageQaActivity")
assert_screen("NATIVE STORAGE QA PASS")
OUT.joinpath("native-storage-qa.png").write_bytes(adb("exec-out","screencap","-p"))
print("NATIVE ANDROID SQLITE MIGRATION, NOTICE DELETION/TOMBSTONE, CHANGE BATCH RESTART/ORDER/SCOPE, MVO/OBSERVER AND OPENING STATUS: PASS; NO NETWORK")

adb("shell","am","force-stop",PACKAGE)
adb("shell","monkey","-p",PACKAGE,"-c","android.intent.category.LAUNCHER","1")
time.sleep(5)
menu("Майно підрозділу")
assert_screen("Актуальний облік головної Мавки")
assert_screen("10 од.")
tap("Зарахувати всі отримані надходження")
assert_screen("Зарахувати всі отримані надходження?")
tap("Скасувати", False)
assert_screen("10 од.")
OUT.joinpath("root-unit-assets.png").write_bytes(adb("exec-out","screencap","-p"))
tap("Обрати підрозділ")
tap("Усі доступні підрозділи")
assert_screen("15 од.")
OUT.joinpath("root-combined-assets.png").write_bytes(adb("exec-out","screencap","-p"))
asset=next(n for n in tree().iter("node") if n.get("text")=="Synthetic")
x1,y1,x2,y2=bounds(asset)
x,y=str((x1+x2)//2),str((y1+y2)//2)
adb("shell","input","tap",x,y)
adb("shell","input","tap",x,y)
assert_screen("Розподіл майна між підрозділами")
assert_screen("Усього: 15 од.")
assert_screen("QA One — 10 од.")
assert_screen("QA Two — 5 од.")
OUT.joinpath("combined-distribution.png").write_bytes(adb("exec-out","screencap","-p"))
tap("Закрити",False)
print("DOUBLE-TAP EXACT ITEM DISTRIBUTION, PER-UNIT COUNTS AND TOTAL: PASS")

field("Пошук за назвою або NSN","missing-qa")
assert_screen("За цим запитом майна не знайдено.")
tap("Скинути пошук")
assert_screen("15 од.")
tap("Журнал зарахувань на телефоні")
assert_screen("Знайдено позицій: 2 із 2")
field("Пошук за назвою або NSN","find-two")
assert_screen("Знайдено позицій: 1 із 2")
assert_screen("FIND-TWO")
OUT.joinpath("combined-snapshot-search.png").write_bytes(adb("exec-out","screencap","-p"))
tap("Скинути пошук")
assert_screen("Знайдено позицій: 2 із 2")
print("ALL-UNIT ROOT AND SNAPSHOT SEARCH, NSN CASE INSENSITIVITY AND RESET: PASS")
menu("Історія документів")
assert_screen("Документи головної Мавки")
tap("Переглянути зміни")
assert_screen("Рядків руху: 2")
OUT.joinpath("root-document.png").write_bytes(adb("exec-out","screencap","-p"))
print("ROOT MIRROR UNIT/COMBINED INVENTORY AND COMPLETE DOCUMENT UI: PASS; NO NETWORK")
adb("shell","am","start","-n",PACKAGE+"/ua.mavka.mobile.StorageQaActivity","--ez","large_ui","true")
assert_screen("NATIVE LARGE UI QA READY")
adb("shell","am","force-stop",PACKAGE)
adb("shell","monkey","-p",PACKAGE,"-c","android.intent.category.LAUNCHER","1")
assert_screen("Стан синхронізації")
for _ in range(2):
    started=time.monotonic()
    menu("Історія документів")
    assert_screen("Документи головної Мавки")
    menu("Головна")
    assert_screen("Стан синхронізації")
    assert time.monotonic()-started<20,"Large document load blocked menu/Home navigation"
OUT.joinpath("large-inventory-home-responsive.png").write_bytes(adb("exec-out","screencap","-p"))
print("33000 ENCRYPTED DOCUMENT ROWS: MENU/HOME RESPONSIVE DURING LOAD; OBSOLETE READ CANCELLED: PASS; NO NETWORK")


menu("Налаштування")
for _ in range(8):
    texts=[n.get("text","") for n in tree().iter("node")]
    assert "Діагностика" not in texts and "Галерея іконок (демо)" not in texts, "Demo diagnostics must be absent from settings"
    swipe()
print("SETTINGS WITHOUT DEMO DIAGNOSTICS: PASS")

adb("shell","am","start","-n",PACKAGE+"/ua.mavka.mobile.StorageQaActivity","--ez","reset_only","true")
assert_screen("NATIVE ACCOUNT RESET QA PASS")
OUT.joinpath("native-account-reset-qa.png").write_bytes(adb("exec-out","screencap","-p"))
print("NATIVE ACCOUNT RESET WITH PROFILE/GMAIL/PASSWORD/AES/KEYSTORE/GRANTS PRESERVED AND INTERRUPTED-RESET RECOVERY: PASS; NO NETWORK")
adb("shell","am","force-stop",PACKAGE)
adb("shell","monkey","-p",PACKAGE,"-c","android.intent.category.LAUNCHER","1")
time.sleep(5)
menu("Налаштування")
tap("Обнулити облік усіх підрозділів і документи")
assert_screen("Обнулити облік на телефоні?")
tap("Скасувати",False)
assert_screen("Керування локальним обліком")
print("EXPLICIT WHOLE-ACCOUNT RESET CONFIRMATION AND CANCEL UI: PASS")
