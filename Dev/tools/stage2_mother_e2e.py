# @module: Mother.Tools.stage2_mother_e2e
# @purpose: Two-Build Stage 2 4-Drive Sandbox E2E suite (Pillar 3 pointer/keyboard, WebView2 UI, Release Setup lifecycle, hostDiffCount==0).
# @depends-on: argparse, ctypes, http.server, json, os, pathlib, shutil, socket, struct, subprocess, sys, threading, time, urllib.error, urllib.request, uuid, zlib
# @exports: run_stage2_e2e, main

from __future__ import annotations

import argparse
import ctypes
import http.server
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
import zlib
from pathlib import Path
from typing import Any

DEV_ROOT = Path(__file__).resolve().parents[1]
OUTER_ROOT = DEV_ROOT.parent
FOUNDATION_TOOLS = DEV_ROOT.parents[1] / "Foundation" / "Dev" / "tools"
if str(FOUNDATION_TOOLS) not in sys.path:
    sys.path.insert(0, str(FOUNDATION_TOOLS))

from sandbox_ctl import (  # noqa: E402
    diff_host_snapshots,
    init_sandbox,
    resolve_sandbox_config,
    take_host_snapshot,
    utc_now_iso,
)

SAFE_SYSTEM_ENV_KEYS = (
    "SYSTEMROOT", "WINDIR", "COMSPEC", "PATH", "PATHEXT",
    "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
    "DOTNET_ROOT", "DOTNET_CLI_TELEMETRY_OPTOUT",
    "SystemDrive", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
    "CommonProgramFiles", "CommonProgramFiles(x86)", "ALLUSERSPROFILE", "PUBLIC",
)


def _get_thread_desktop() -> str:
    if os.name != "nt":
        return "non-windows"
    k32, u32 = ctypes.windll.kernel32, ctypes.windll.user32
    buf = ctypes.create_unicode_buffer(256)
    u32.GetUserObjectInformationW(u32.GetThreadDesktop(k32.GetCurrentThreadId()), 2, buf, ctypes.sizeof(buf), ctypes.byref(ctypes.c_ulong(0)))
    return buf.value or "unknown"


def _is_udp_5004_bound() -> bool:
    try:
        return ":5004" in subprocess.check_output(["netstat", "-ano", "-p", "udp"], text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return False


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


def _build_sandbox_env(cfg: dict[str, Any]) -> dict[str, str]:
    env = {k: os.environ[k] for k in SAFE_SYSTEM_ENV_KEYS if os.environ.get(k)}
    prof = Path(cfg["sandboxProfileDir"])
    local_app = prof / "AppData" / "Local"
    temp_dir = local_app / "Temp"
    bundle_dir = temp_dir / "dotnet_bundle"
    for d in (prof, local_app, prof / "AppData" / "Roaming", temp_dir, bundle_dir):
        d.mkdir(parents=True, exist_ok=True)
    env.update({
        "FOUNDATION_SANDBOX_ROOT": str(cfg["sandboxRoot"]),
        "FOUNDATION_SANDBOX_PROFILE_DIR": str(prof),
        "USERPROFILE": str(prof),
        "HOME": str(prof),
        "LOCALAPPDATA": str(local_app),
        "APPDATA": str(prof / "AppData" / "Roaming"),
        "TEMP": str(temp_dir),
        "TMP": str(temp_dir),
        "DOTNET_BUNDLE_EXTRACT_BASE_DIR": str(bundle_dir),
        "FOUNDATION_SUPPRESS_MODALS": "1",
    })
    for role in ("SANDBOX_C_DRIVE", "SANDBOX_D_DRIVE", "SANDBOX_REMOVABLE_DRIVE", "SANDBOX_DISC_DRIVE"):
        if cfg["drives"].get(role):
            env[role] = cfg["drives"][role]
    env.pop("APP_ISOLATED_TEST", None)
    return env


def _run_bounded(cmd: list[str], env: dict[str, str], log_dir: Path, tag: str, timeout_s: int = 30) -> tuple[int, str, str]:
    out_p, err_p = log_dir / f"{tag}.stdout.log", log_dir / f"{tag}.stderr.log"
    with out_p.open("w", encoding="utf-8") as fo, err_p.open("w", encoding="utf-8") as fe:
        proc = subprocess.Popen(cmd, env=env, stdout=fo, stderr=fe, text=True)
        try:
            rc = proc.wait(timeout=timeout_s)
        except subprocess.TimeoutExpired:
            proc.terminate()
            proc.wait(timeout=5)
            raise TimeoutError(f"Command '{tag}' timed out after {timeout_s}s")
    return rc, out_p.read_text(encoding="utf-8", errors="replace"), err_p.read_text(encoding="utf-8", errors="replace")


def _http_json(url: str, method: str = "GET", body: Any = None, token: str | None = None, timeout_s: float = 5.0) -> tuple[int, dict[str, Any]]:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout_s) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            return resp.status, json.loads(raw) if raw.strip() else {}
    except urllib.error.HTTPError as err:
        raw = err.read().decode("utf-8", errors="replace")
        return err.code, json.loads(raw) if raw.strip().startswith("{") else {"raw": raw}


def _wait_http_ok(url: str, token: str | None = None, timeout_s: float = 20.0) -> dict[str, Any]:
    deadline = time.monotonic() + timeout_s
    last_err = ""
    while time.monotonic() < deadline:
        try:
            code, payload = _http_json(url, token=token, timeout_s=2.0)
            if 200 <= code < 300:
                return payload
            last_err = f"HTTP {code}: {payload}"
        except (OSError, urllib.error.URLError, json.JSONDecodeError) as ex:
            last_err = str(ex)
        time.sleep(0.25)
    raise RuntimeError(f"Timed out waiting for {url}: {last_err}")


def _find_node(node: dict[str, Any] | None, ctype: str, text_sub: str = "", visible_only: bool = False, exact: bool = False) -> dict[str, Any] | None:
    if not node:
        return None
    vis_ok = not visible_only or bool(node.get("isVisible"))
    txt = node.get("text", "")
    txt_ok = (txt.lower() == text_sub.lower()) if exact else (text_sub.lower() in txt.lower())
    if vis_ok and node.get("controlType", "").lower() == ctype.lower() and txt_ok:
        return node
    for ch in node.get("children", []):
        if (m := _find_node(ch, ctype, text_sub, visible_only, exact)) is not None:
            return m
    return None


def _png_is_nonblank(png_path: Path) -> tuple[bool, int, int, int]:
    raw = png_path.read_bytes()
    if not raw.startswith(b"\x89PNG\r\n\x1a\n") or len(raw) < 2048:
        return False, 0, 0, len(raw)
    w, h = struct.unpack(">II", raw[16:24])
    idat = bytearray()
    pos = 8
    while pos + 8 <= len(raw):
        clen, ctype = struct.unpack(">I4s", raw[pos:pos + 8])
        if ctype == b"IDAT":
            idat.extend(raw[pos + 8:pos + 8 + clen])
        pos += 12 + clen
    decomp = zlib.decompress(bytes(idat))
    unique = len(set(decomp[i] for i in range(1, min(len(decomp), 240000), 17)))
    return (w >= 400 and h >= 300 and unique >= 12), w, h, len(raw)


def _capture_snapshot_when_ready(ap: int, tok: str, s_png: Path, d_png: Path, min_wait_s: float = 2.0, timeout_s: float = 20.0) -> dict[str, Any]:
    time.sleep(min_wait_s)
    deadline = time.monotonic() + timeout_s
    last_meta, last_api = (False, 0, 0, 0), (0, None)
    while time.monotonic() < deadline:
        code, res = _http_json(f"http://127.0.0.1:{ap}/v1/snapshot", "POST", {"filePath": str(s_png), "snapshotFormat": "png"}, tok, 10.0)
        last_api = (code, res)
        if code == 200 and s_png.is_file():
            meta = _png_is_nonblank(s_png)
            last_meta = meta
            if meta[0] and meta[3] > 6000:
                shutil.copy2(s_png, d_png)
                return {"filePath": str(d_png), "sandboxPath": str(s_png), "width": meta[1], "height": meta[2], "sizeBytes": meta[3], "api": res}
        time.sleep(0.45)
    raise RuntimeError(f"Snapshot '{d_png.name}' did not stabilize non-blank: {last_meta} last_api={last_api}")


def _terminate_tree(proc: subprocess.Popen[str]) -> None:
    if proc.poll() is not None:
        return
    subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=8, check=False)


def _start_echo_server(port: int) -> http.server.ThreadingHTTPServer:
    html = b"""<!DOCTYPE html><html><body style="margin:0;background:#0b0e14;color:#e2e8f0;font-family:Segoe UI,sans-serif;padding:32px">
    <h1 style="color:#38bdf8;margin:0 0 12px">Echo App v0.3.0 (Stage 2 Mock)</h1>
    <p>Generic pluggable Suite App rendered dynamically via Mother Core without hardcoded tabs.</p></body></html>"""

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            is_h = self.path.startswith("/api/v1/health")
            self.send_response(200)
            self.send_header("Content-Type", "application/json" if is_h else "text/html; charset=utf-8")
            self.end_headers()
            self.wfile.write(b'{"status":"ok","appId":"echo-app"}' if is_h else html)

        def log_message(self, format: str, *args: Any) -> None:
            return

    srv = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv


def _maybe_start_streamer(ap: int, tok: str) -> subprocess.Popen[str] | None:
    s_ps1 = Path.home() / ".gemini/config/skills/exebox-stream/scripts/start_exebox_vlc_stream.ps1"
    if not s_ps1.is_file() or not _is_udp_5004_bound():
        return None
    return subprocess.Popen(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(s_ps1),
                             "-AutomationPort", str(ap), "-AutomationToken", tok, "-UdpPort", "5004", "-Fps", "20", "-DurationSeconds", "20"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, text=True)


def run_stage2_e2e(auto_setup: Path, shots_dir: Path) -> dict[str, Any]:
    cfg = resolve_sandbox_config(argparse.Namespace(env_file=None, c_drive=None, d_drive=None, removable_drive=None, disc_drive=None, sandbox_root=None, profile_dir=None))
    init_sandbox(cfg, skip_subst=False, force_hive=False)
    take_host_snapshot(cfg, name="mother-v030-baseline")

    run_root = Path(cfg["sandboxRoot"]) / "runs" / f"mother_v030_{uuid.uuid4().hex[:8]}"
    logs_dir, s_shots = run_root / "logs", run_root / "shots"
    setup_data, mother_data, mother_programs = run_root / "setup_data", run_root / "mother_data", run_root / "mother_programs"
    base_inst = Path(cfg["sandboxProfileDir"]) / "AppData" / "Local" / "Programs" / "StreamItAll" / "Mother" / "Mother"
    wiz_typed_inst = Path(f"{base_inst}Wiz")
    rel_inst = run_root / "release_install_verify"
    for d in (shots_dir, s_shots, logs_dir, setup_data, mother_data, mother_programs):
        d.mkdir(parents=True, exist_ok=True)
    for d in (base_inst, wiz_typed_inst, rel_inst):
        if d.exists():
            shutil.rmtree(d, ignore_errors=True)

    env = _build_sandbox_env(cfg)
    rel_launcher, rel_setup = OUTER_ROOT / "Builds" / "v0.3.0" / "Mother.exe", OUTER_ROOT / "Releases" / "v0.3.0" / "Mother-Setup-v0.3.0-win-x64.exe"
    assert rel_launcher.is_file() and rel_setup.is_file() and auto_setup.is_file()
    assert not (OUTER_ROOT / "Builds" / "v0.3.0" / "AGENTS.md").exists() and not (OUTER_ROOT / "Builds" / "v0.3.0" / "LICENSE").exists()
    assert not any((OUTER_ROOT / "Builds" / "v0.3.0").rglob("Foundation.Automation.*"))

    rc_app_rel, _, _ = _run_bounded([str(rel_launcher), "--automation-port", "51991", "--automation-token", "rel"], env, logs_dir, "rel_app_fail")
    rc_setup_rel, _, _ = _run_bounded([str(rel_setup), "--automation-port", "51992", "--automation-token", "rel"], env, logs_dir, "rel_setup_fail")
    assert rc_app_rel == 2 and rc_setup_rel == 2

    # Release signed installer lifecycle: silent install -> verify -> uninstall --purge-data
    rc_ri, _, err_ri = _run_bounded([str(rel_setup), "--silent", "--no-shortcuts", "--no-launch", "--install-dir", str(rel_inst),
                                     "--data-dir", str(setup_data), "--log-dir", str(logs_dir)], env, logs_dir, "rel_install", 240)
    rc_rv, _, _ = _run_bounded([str(rel_setup), "--verify", "--silent", "--install-dir", str(rel_inst), "--data-dir", str(setup_data), "--log-dir", str(logs_dir)], env, logs_dir, "rel_verify", 90)
    rc_ru, _, _ = _run_bounded([str(rel_setup), "--uninstall", "--silent", "--purge-data", "--install-dir", str(rel_inst), "--data-dir", str(setup_data), "--log-dir", str(logs_dir)], env, logs_dir, "rel_uninstall", 180)
    assert rc_ri == 0 and rc_rv == 0 and rc_ru == 0 and not (rel_inst / "Mother.exe").exists(), f"Release setup lifecycle failed: {err_ri}"

    screenshots: list[dict[str, Any]] = []
    # 1. Interactive Setup Wizard driven via /v1/tree, /v1/pointer & /v1/keyboard (Pillar 3 -> real install)
    p1, t1 = _free_port(), uuid.uuid4().hex
    wo, we = (logs_dir / "wiz.stdout.log").open("w", encoding="utf-8"), (logs_dir / "wiz.stderr.log").open("w", encoding="utf-8")
    wp = subprocess.Popen([str(auto_setup), "--install-dir", str(base_inst), "--data-dir", str(setup_data), "--log-dir", str(logs_dir),
                           "--automation-port", str(p1), "--automation-token", t1], env=env, stdout=wo, stderr=we, text=True)
    st = None
    try:
        st = _maybe_start_streamer(p1, t1)
        _wait_http_ok(f"http://127.0.0.1:{p1}/v1/health", token=t1)
        tree = _http_json(f"http://127.0.0.1:{p1}/v1/tree", token=t1)[1]["data"]
        tb_node = _find_node(tree, "TextBox", str(base_inst), visible_only=True, exact=True)
        chk_launch = _find_node(tree, "CheckBox", "Launch")
        chk_desk = _find_node(tree, "CheckBox", "Desktop")
        btn_det = _find_node(tree, "Button", "Details")
        btn_inst = _find_node(tree, "Button", "Install Now")
        assert tb_node and chk_launch and chk_desk and btn_inst
        # Off-client (2000,2000) prevents WinForms WmLButtonUp + Driver PerformClick/Checked double-toggling checkboxes/toggles
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": chk_launch["id"], "x": 2000, "y": 2000}, t1)[0] == 200
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": chk_desk["id"], "x": 2000, "y": 2000}, t1)[0] == 200
        if btn_det:
            _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": btn_det["id"], "x": 2000, "y": 2000}, t1)
        assert _http_json(f"http://127.0.0.1:{p1}/v1/keyboard", "POST", {"action": "type", "targetId": tb_node["id"], "text": "Wiz"}, t1)[0] == 200
        tree_after = _http_json(f"http://127.0.0.1:{p1}/v1/tree", token=t1)[1]["data"]
        assert _find_node(tree_after, "TextBox", str(wiz_typed_inst), visible_only=True, exact=True) is not None
        screenshots.append({"stage": "Setup Wizard (v2 Manifest + Pointer/Keyboard)", **_capture_snapshot_when_ready(p1, t1, s_shots / "stage2_01_setup_wizard.png", shots_dir / "stage2_01_setup_wizard.png", 0.8)})
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": btn_inst["id"]}, t1)[0] == 200
        dl, btn_fin = time.monotonic() + 300.0, None
        while time.monotonic() < dl:
            cur_tree = _http_json(f"http://127.0.0.1:{p1}/v1/tree", token=t1)[1].get("data")
            btn_fin = _find_node(cur_tree, "Button", "Finish")
            if btn_fin and btn_fin.get("isEnabled"):
                break
            time.sleep(1.5)
        assert btn_fin and btn_fin.get("isEnabled"), "Setup Wizard did not reach enabled Finish button within 300s"
        assert _http_json(f"http://127.0.0.1:{p1}/v1/pointer", "POST", {"action": "click", "targetId": btn_fin["id"]}, t1)[0] == 200
        assert wp.wait(timeout=15) == 0, f"Setup Wizard exited with code {wp.returncode}"
        assert (wiz_typed_inst / "Mother.exe").is_file() and (wiz_typed_inst / "app" / "Mother.App.exe").is_file()
        hive_txt = Path(cfg["hivePath"]).read_text(encoding="utf-8")
        assert "LastInstallBaseDir" in hive_txt and "MotherWiz" in hive_txt and "StreamItAll\\\\Mother" in hive_txt
    finally:
        _terminate_tree(wp)
        if st:
            _terminate_tree(st)
        wo.close(); we.close()

    installed_exe = wiz_typed_inst / "Mother.exe"

    def launch_and_capture(tab_arg: list[str], out_name: str, label: str, hp: int, extra_check: Any = None) -> None:
        ap, tok = _free_port(), uuid.uuid4().hex
        so, se = (logs_dir / f"{out_name}.stdout.log").open("w", encoding="utf-8"), (logs_dir / f"{out_name}.stderr.log").open("w", encoding="utf-8")
        proc = subprocess.Popen([str(installed_exe), "--port", str(hp), "--data-dir", str(mother_data), "--log-dir", str(logs_dir),
                                 "--programs-dir", str(mother_programs), "--automation-port", str(ap), "--automation-token", tok, *tab_arg],
                                env=env, stdout=so, stderr=se, text=True)
        stm = None
        try:
            stm = _maybe_start_streamer(ap, tok)
            _wait_http_ok(f"http://127.0.0.1:{hp}/api/v1/suite/health", timeout_s=25.0)
            _wait_http_ok(f"http://127.0.0.1:{ap}/v1/health", token=tok, timeout_s=25.0)
            if extra_check:
                extra_check(hp, ap, tok)
            screenshots.append({"stage": label, **_capture_snapshot_when_ready(ap, tok, s_shots / out_name, shots_dir / out_name, 2.5, 25.0)})
        finally:
            _terminate_tree(proc)
            if stm:
                _terminate_tree(stm)
            so.close(); se.close()

    def check_empty(p: int, ap: int, tok: str) -> None:
        assert _http_json(f"http://127.0.0.1:{p}/api/v1/suite/apps")[1].get("apps") == []
        assert _http_json(f"http://127.0.0.1:{p}/api/v1/suite/doctor/run", "POST", {})[1].get("overall") == "pass"
        t_app = _http_json(f"http://127.0.0.1:{ap}/v1/tree", token=tok)[1]["data"]
        assert t_app and _http_json(f"http://127.0.0.1:{ap}/v1/pointer", "POST", {"action": "click", "targetId": t_app["id"]}, tok)[0] == 200

    launch_and_capture(["--window"], "stage2_02_empty_state.png", "Mother 0-App Empty State", _free_port(), check_empty)

    echo_port = _free_port()
    echo_srv = _start_echo_server(echo_port)
    try:
        (mother_data / "manifests").mkdir(parents=True, exist_ok=True)
        (mother_data / "manifests" / "echo-app.suite.manifest.json").write_text(json.dumps({
            "schemaVersion": 2, "appId": "echo-app", "name": "Echo App", "version": "1.0.0",
            "internalPort": echo_port, "residencyMode": "warm", "brandAccent": "#38bdf8"
        }, indent=2), encoding="utf-8")

        def check_echo_and_put_settings(p: int, ap: int, tok: str) -> None:
            assert any(a.get("appId") == "echo-app" for a in _http_json(f"http://127.0.0.1:{p}/api/v1/suite/apps/reload", "POST", {})[1].get("apps", []))
            assert _http_json(f"http://127.0.0.1:{p}/api/v1/suite/settings", "PUT", {
                "schemaVersion": 2, "idleTimeoutMinutes": 35, "shouldAutoAlignSisterApps": True,
                "shouldAllowTailscaleBind": False, "shouldInhibitSleepDuringActiveLock": True,
                "shouldShowDesktopToasts": True, "shouldSendDiscordWebhooks": True, "stage2Tag": "persisted"
            })[0] == 200

        launch_and_capture(["--suite-tab", "echo-app"], "stage2_03_echo_app_tab.png", "Generic Mock App (echo-app) Tab", _free_port(), check_echo_and_put_settings)
    finally:
        echo_srv.shutdown()

    cfg_json = json.loads((mother_data / "config" / "settings.json").read_text(encoding="utf-8"))
    assert cfg_json["schemaVersion"] == 2 and cfg_json["idleTimeoutMinutes"] == 35 and cfg_json["shouldAutoAlignSisterApps"] is True

    def check_settings_reloaded(p: int, ap: int, tok: str) -> None:
        st_res = _http_json(f"http://127.0.0.1:{p}/api/v1/suite/settings")[1].get("settings", {})
        assert st_res.get("schemaVersion") == 2 and st_res.get("idleTimeoutMinutes") == 35 and st_res.get("shouldSendDiscordWebhooks") is True

    launch_and_capture(["--suite-tab", "route:settings"], "stage2_04_settings_v2.png", "Persisted Settings v2", _free_port(), check_settings_reloaded)

    rc_diag, diag_out, _ = _run_bounded([str(installed_exe), "--diagnose", "--data-dir", str(mother_data), "--log-dir", str(logs_dir),
                                         "--programs-dir", str(mother_programs)], env, logs_dir, "diagnose")
    assert rc_diag == 0 and '"overall": "pass"' in diag_out.lower()
    diff = diff_host_snapshots(cfg, name="mother-v030-baseline")
    is_drive_tmp = lambda p: ".tmp.driveupload" in str(p).lower()
    drive_noise = {k: [x for x in diff.get(k, []) if is_drive_tmp(x)] for k in ("addedFiles", "removedFiles", "modifiedFiles")}
    mother_files = [x for k in ("addedFiles", "removedFiles", "modifiedFiles") for x in diff.get(k, []) if not is_drive_tmp(x)]
    mother_regs = [x for k in ("addedRegistryEntries", "removedRegistryEntries", "modifiedRegistryEntries") for x in diff.get(k, [])]
    mother_diff_count = len(mother_files) + len(mother_regs)
    assert mother_diff_count == 0, f"Mother host mutation detected! {diff}"

    receipt = {
        "schemaVersion": 1, "appId": "mother", "version": "0.3.0", "executedAtUtc": utc_now_iso(),
        "threadDesktop": _get_thread_desktop(), "vlcUdp5004Bound": _is_udp_5004_bound(),
        "releaseAppAutomationExitCode": rc_app_rel, "releaseSetupAutomationExitCode": rc_setup_rel,
        "releaseInstallerLifecycleVerified": True, "pillar3PointerKeyboardVerified": True,
        "diagnoseExitCode": rc_diag, "hostDiffCount": mother_diff_count,
        "rawHostDiffCount": diff["hostDiffCount"], "driveSyncNoiseFiltered": drive_noise,
        "sandboxRoot": str(cfg["sandboxRoot"]), "sandboxInstallDir": str(wiz_typed_inst), "screenshots": screenshots,
    }
    r_path = DEV_ROOT / "docs" / "reports" / "0.3.0.2026-11-19.-.stage2-sandbox-e2e-receipt.json"
    r_path.parent.mkdir(parents=True, exist_ok=True)
    r_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Mother v0.3.0 Stage 2 4-Drive Sandbox E2E")
    ap.add_argument("--auto-setup", type=Path, default=Path(os.environ.get("TEMP", ".")) / "Mother-Setup-v0.3.0-win-x64-automation.exe")
    ap.add_argument("--shots-dir", type=Path, default=DEV_ROOT / "logs" / "stage2_shots")
    args = ap.parse_args(argv)
    print(json.dumps(run_stage2_e2e(args.auto_setup, args.shots_dir), indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
