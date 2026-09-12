#!/usr/bin/env python3
"""No-account Android runtime verification for the isolated Debug test package."""
import json
import pathlib
import subprocess
import time

PACKAGE = "io.github.iviesever.mementomori.exporter.devicetest"
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts" / "device-validation"


def adb(*args: str, check: bool = True) -> str:
    p = subprocess.run(["adb", *args], capture_output=True, text=True, timeout=45)
    if check and p.returncode:
        raise RuntimeError("adb failed: " + " ".join(args[:3]))
    return p.stdout.strip()


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    apks = list((ROOT / "MementoMori.Exporter.Android/bin/Debug").rglob("*-Signed.apk"))
    if len(apks) != 1:
        raise RuntimeError("Expected exactly one test APK")
    if "Success" not in adb("install", "-r", str(apks[0])):
        raise RuntimeError("Test APK installation failed")
    activity = adb("shell", "cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    if not activity.startswith(PACKAGE + "/"):
        raise RuntimeError("No test launcher activity")
    for phase in ("write", "restore", "picker-cancel", "picker-cancel"):
        adb("shell", "am", "force-stop", PACKAGE)
        adb("shell", "run-as", PACKAGE, "rm", "-f", "files/device-smoke.json", check=False)
        adb("shell", "am", "start", "-W", "-n", activity, "--es", "exporter_test_phase", phase)
        deadline = time.monotonic() + 90
        result = None
        canceled = False
        while time.monotonic() < deadline:
            raw = adb("shell", "run-as", PACKAGE, "cat", "files/device-smoke.json", check=False)
            try:
                result = json.loads(raw)
            except (json.JSONDecodeError, ValueError):
                result = None
            if result and result.get("state") == "waiting-for-picker-cancel" and not canceled:
                top = adb("shell", "dumpsys", "activity", "activities")
                resumed = [line.lower() for line in top.splitlines() if "resumedactivity" in line.lower()]
                if any("documentsui" in line for line in resumed):
                    time.sleep(1)
                    adb("shell", "input", "keyevent", "4")
                    canceled = True
            if result and "passed" in result:
                break
            time.sleep(1)
        if not result or result.get("passed") is not True:
            raise RuntimeError("Runtime check failed: " + phase)
        suffix = str(len(list(OUT.glob("*.json"))))
        (OUT / (phase + "-" + suffix + ".json")).write_text(json.dumps(result, indent=2), encoding="utf-8")
    adb("shell", "am", "force-stop", PACKAGE)
    adb("shell", "am", "start", "-W", "-n", activity)
    time.sleep(2)
    if not adb("shell", "pidof", PACKAGE):
        raise RuntimeError("Native UI process is not running")
    adb("shell", "uiautomator", "dump", "/sdcard/exporter-ui.xml")
    adb("pull", "/sdcard/exporter-ui.xml", str(OUT / "native-ui.xml"))
    xml = (OUT / "native-ui.xml").read_text(encoding="utf-8")
    if "引继" not in xml:
        raise RuntimeError("Native login UI is not visible")
    image = subprocess.run(["adb", "exec-out", "screencap", "-p"], capture_output=True, timeout=30, check=True).stdout
    (OUT / "native-ui.png").write_bytes(image)
    print("Android emulator: storage restart, export copy/clear, repeated picker cancellation and native UI checks passed.")


if __name__ == "__main__":
    main()
