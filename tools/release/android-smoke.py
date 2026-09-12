#!/usr/bin/env python3
"""No-account system tests, including APK replacement without uninstall or data clear."""
import hashlib
import json
import os
import pathlib
import re
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


def version_code(report: str) -> int:
    values = set(re.findall(r"\bversionCode=(\d+)\b", report))
    if len(values) != 1 or int(next(iter(values))) < 1:
        raise RuntimeError("No unique installed versionCode")
    return int(next(iter(values)))


def signing_identity(report: str) -> str:
    required = (r"^Verifies\s*$", r"^Number of signers:\s*1\s*$",
                r"^Verified using v2 scheme \(APK Signature Scheme v2\):\s*true\s*$")
    if not all(re.search(pattern, report, re.M) for pattern in required):
        raise RuntimeError("Unverified or unsupported test APK signature")
    digests = set(s.lower() for s in re.findall(
        r"^(?:Signer (?:#\d+|\(minSdkVersion=[^\r\n]+\))|V3\.0 Signer:) certificate SHA-256 digest:\s*([A-Fa-f0-9]{64})\s*$",
        report, re.M))
    if len(digests) != 1:
        raise RuntimeError("No unique verified signer certificate")
    return digests.pop()


def run_phase(activity: str, phase: str, expected_version: int) -> dict:
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
    if not result or result.get("passed") is not True or str(result.get("versionCode")) != str(expected_version):
        raise RuntimeError("Runtime check failed or stale package executed: " + phase)
    suffix = str(len(list(OUT.glob("*.json"))))
    (OUT / (phase + "-" + suffix + ".json")).write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    apks = [ROOT / "artifacts/device-apks" / f"version-{v}.apk" for v in (1001, 1002)]
    if not all(apk.is_file() for apk in apks):
        raise RuntimeError("Build both version-1001.apk and version-1002.apk first")
    hashes = [hashlib.sha256(apk.read_bytes()).hexdigest() for apk in apks]
    if hashes[0] == hashes[1]:
        raise RuntimeError("Reinstalling identical bytes is not an upgrade test")
    sdk = pathlib.Path(os.environ["ANDROID_HOME"])
    signers = sorted((sdk / "build-tools").glob("*/apksigner"))
    if not signers:
        raise RuntimeError("Android SDK apksigner is required")
    certificates = []
    for version, apk in zip((1001, 1002), apks):
        report = subprocess.run([str(signers[-1]), "verify", "--verbose", "--print-certs", str(apk)],
                                capture_output=True, text=True, check=True, timeout=45).stdout
        certificates.append(signing_identity(report))
        (OUT / f"signature-{version}.txt").write_text(report, encoding="utf-8")
    if certificates[0] != certificates[1]:
        raise RuntimeError("Update test APKs changed signing identity")
    # The isolated package MUST be absent initially. Never silently clear a pre-existing app.
    if "package:" in adb("shell", "pm", "path", PACKAGE, check=False):
        raise RuntimeError("Use a fresh emulator; refusing to uninstall an existing test app")
    if "Success" not in adb("install", str(apks[0])):
        raise RuntimeError("Initial test APK installation failed")
    if version_code(adb("shell", "dumpsys", "package", PACKAGE)) != 1001:
        raise RuntimeError("Wrong baseline version installed")
    activity = adb("shell", "cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    if not activity.startswith(PACKAGE + "/"):
        raise RuntimeError("No test launcher activity")
    # Preserve the existing process-restart coverage before testing a different version.
    run_phase(activity, "write", 1001)
    run_phase(activity, "restore", 1001)
    run_phase(activity, "write", 1001)
    adb("shell", "am", "force-stop", PACKAGE)
    if "Success" not in adb("install", "-r", str(apks[1])):
        raise RuntimeError("In-place APK upgrade failed")
    if version_code(adb("shell", "dumpsys", "package", PACKAGE)) != 1002:
        raise RuntimeError("Upgraded version was not installed")
    # The new binary must decrypt the OLD SecureStorage and read the OLD export path/bytes.
    run_phase(activity, "upgrade-restore", 1002)
    for _ in range(2):
        run_phase(activity, "picker-cancel", 1002)
    adb("shell", "am", "force-stop", PACKAGE)
    adb("shell", "am", "start", "-W", "-n", activity)
    time.sleep(2)
    if not adb("shell", "pidof", PACKAGE):
        raise RuntimeError("Native UI process is not running")
    adb("shell", "uiautomator", "dump", "/sdcard/exporter-ui.xml")
    adb("pull", "/sdcard/exporter-ui.xml", str(OUT / "native-ui.xml"))
    if "引继" not in (OUT / "native-ui.xml").read_text(encoding="utf-8"):
        raise RuntimeError("Native login UI is not visible")
    image = subprocess.run(["adb", "exec-out", "screencap", "-p"], capture_output=True, timeout=30, check=True).stdout
    (OUT / "native-ui.png").write_bytes(image)
    build_commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True,
                                  text=True, check=True, timeout=15).stdout.strip()
    evidence = {
        "sourceCommit": os.environ.get("SOURCE_COMMIT", build_commit), "buildCommit": build_commit,
        "workflowRun": os.environ.get("GITHUB_RUN_ID"), "testPackage": PACKAGE,
        "upgrade": {"fromVersionCode": 1001, "toVersionCode": 1002, "apkSha256": hashes,
                    "certificateSha256": certificates[0], "installMode": "adb install -r",
                    "uninstalledBetweenVersions": False, "clearedDataBetweenVersions": False,
                    "savedCredentialsReadableAfterUpgrade": True, "previousExportReadableAfterUpgrade": True},
        "androidApi": adb("shell", "getprop", "ro.build.version.sdk"),
        "androidRelease": adb("shell", "getprop", "ro.build.version.release"),
        "abi": adb("shell", "getprop", "ro.product.cpu.abi"),
        "emulator": True, "physicalDevice": False, "realGameLogin": False, "passed": True,
    }
    (OUT / "BUILD-INFO.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print("PASS: distinct APK versions upgrade in place; previous credentials/exports survive; restart, picker and native UI pass.")


if __name__ == "__main__":
    main()
