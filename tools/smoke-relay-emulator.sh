#!/usr/bin/env bash
set -euo pipefail
package="${MAVKA_QA_PACKAGE:-ua.mavka.mobile.relayqa}"
apk="$(find relay-qa-apk -maxdepth 1 -name '*.apk' -type f | sort | head -n1)"
if [[ -z "$apk" || ! -s "$apk" ]]; then
  echo "No release APK for emulator smoke test"
  exit 1
fi
mkdir -p emulator-smoke-artifacts
adb wait-for-device
adb install -r -t "$apk"
adb shell pm list packages | grep -Fx "package:$package"
adb shell pm grant "$package" android.permission.POST_NOTIFICATIONS
# Launch via Android's actual MAIN/LAUNCHER entry, not a guessed .NET Activity.
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1
sleep 12
pid="$(adb shell pidof "$package" | tr -d '\r' || true)"
adb logcat -d -s AndroidRuntime:E > emulator-smoke-artifacts/android-runtime-errors.log || true
adb exec-out screencap -p > emulator-smoke-artifacts/home-screen.png || true
if [[ -z "$pid" ]]; then
  echo "Mavka Mobile did not remain running after launch."
  tail -n 100 emulator-smoke-artifacts/android-runtime-errors.log
  exit 1
fi
if grep -A 2 -E 'FATAL EXCEPTION' emulator-smoke-artifacts/android-runtime-errors.log | grep -F "$package"; then
  echo "Application fatal exception detected."
  exit 1
fi
# Protect existing or production data: emulator has no real accounts or keys.
echo "CLEAN EMULATOR INSTALL AND HOME SCREEN LAUNCH: PASS"
result=0
python3 tools/relay-emulator-ui.py || result=$?
adb logcat -d -s AndroidRuntime:E > emulator-smoke-artifacts/android-runtime-errors.log || true
adb exec-out screencap -p > emulator-smoke-artifacts/final-screen.png || true
adb shell uiautomator dump /sdcard/final-ui.xml >/dev/null 2>&1 || true
adb shell cat /sdcard/final-ui.xml > emulator-smoke-artifacts/final-ui.xml || true
exit "$result"
