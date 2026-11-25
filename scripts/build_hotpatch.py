#!/usr/bin/env python3
"""
Build & SHA-256 sign a zero-resign OTA Hot-Patch archive (Garden-HotPatch-<version>.zip)
for Garden Launcher iOS (v1.0.3+, Mono Interpreter 0% JIT).

Whitelisted managed/data targets inside the archive:
  - Mods/SDVMobileControls/{SDVMobileControls.dll, manifest.json, ...}
  - Mods/SDViOSTouchControls/SDViOSTouchControls.dll   (--include-touch <dll>)
  - StardewValley/Lidgren.Network.dll                  (--include-lidgren <dll>)
  - StardewValley/StardewModdingAPI.dll + smapi-internal/* (--include-smapi <dir>)
  - .garden/extra_mod_rules.json                       (--extra-rules <json>)
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import sys
import zipfile


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def add_zip_file(zf: zipfile.ZipFile, arcname: str, data: bytes) -> None:
    info = zipfile.ZipInfo(arcname)
    info.date_time = (2026, 11, 25, 0, 0, 0)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.create_system = 3
    info.external_attr = (0o100644) << 16
    zf.writestr(info, data, compresslevel=9)


def main() -> int:
    ap = argparse.ArgumentParser(description="Package an OTA Hot-Patch ZIP for Garden Launcher iOS.")
    ap.add_argument("--version", required=True, help="Target hot-patch version string, e.g. 1.0.3-p1 or 1.0.4")
    ap.add_argument("--build", type=int, required=True, help="Monotonic integer build > base IPA build (e.g. 10301)")
    ap.add_argument("--min-ipa-build", type=int, default=10300, help="Minimum base IPA build required (default: 10300)")
    ap.add_argument("--mc-dir", type=Path, default=None, help="Path to SDVMobileControls Release output dir")
    ap.add_argument("--include-touch", type=Path, default=None, help="Optional patched v7-touch.dll to include")
    ap.add_argument("--include-lidgren", type=Path, default=None, help="Optional Lidgren.Network.dll to include")
    ap.add_argument("--include-smapi", type=Path, default=None, help="Optional BundledSMAPI root dir to include")
    ap.add_argument("--extra-rules", type=Path, default=None, help="Optional .garden/extra_mod_rules.json to include")
    ap.add_argument("--output", type=Path, default=None, help="Output ZIP path (default: Garden-HotPatch-<ver>.zip)")
    ap.add_argument("--update-version-json", type=Path, default=None, help="Optional version.json path to update in-place")
    ap.add_argument("--repo", default="hanprm9-dot/Garden-Launcher-iOS", help="GitHub owner/repo for download URLs")
    ap.add_argument("--release-tag", default=None, help="Release tag hosting the hotpatch asset (default: v<version>)")
    args = ap.parse_args()

    script_dir = Path(__file__).resolve().parent
    mc_base = script_dir / "SDVMobileControls"
    if not mc_base.is_dir() and (script_dir.parent / "ModGarden" / "SDVMobileControls").is_dir():
        mc_base = script_dir.parent / "ModGarden" / "SDVMobileControls"
    mc_dir = args.mc_dir or (mc_base / "bin" / "Release" / "net6.0")
    mc_manifest = mc_base / "manifest.json"

    out_zip = args.output or Path(f"Garden-HotPatch-{args.version}.zip")
    out_zip.parent.mkdir(parents=True, exist_ok=True)

    staged: dict[str, bytes] = {}

    mc_dll = mc_dir / "SDVMobileControls.dll"
    if not mc_dll.is_file():
        raise FileNotFoundError(f"Missing SDVMobileControls.dll at {mc_dll}")
    dll_bytes = mc_dll.read_bytes()
    assert dll_bytes[:2] == b"MZ", "SDVMobileControls.dll is not a valid PE assembly"
    staged["Mods/SDVMobileControls/SDVMobileControls.dll"] = dll_bytes

    if mc_manifest.is_file():
        mf_obj = json.loads(mc_manifest.read_text(encoding="utf-8-sig"))
        mf_obj["Version"] = args.version
        staged["Mods/SDVMobileControls/manifest.json"] = (json.dumps(mf_obj, indent=2, ensure_ascii=False) + "\n").encode("utf-8")

    if args.include_touch:
        tb = args.include_touch.read_bytes()
        assert tb[:2] == b"MZ", "TouchControls DLL is not a valid PE assembly"
        staged["Mods/SDViOSTouchControls/SDViOSTouchControls.dll"] = tb

    if args.include_lidgren:
        lb = args.include_lidgren.read_bytes()
        assert lb[:2] == b"MZ", "Lidgren.Network.dll is not a valid PE assembly"
        staged["StardewValley/Lidgren.Network.dll"] = lb

    if args.include_smapi:
        smapi_dll = args.include_smapi / "StardewModdingAPI.dll"
        if not smapi_dll.is_file():
            raise FileNotFoundError(f"Missing StardewModdingAPI.dll in {args.include_smapi}")
        staged["StardewValley/StardewModdingAPI.dll"] = smapi_dll.read_bytes()
        smapi_int = args.include_smapi / "smapi-internal"
        if smapi_int.is_dir():
            for f in sorted(smapi_int.rglob("*")):
                if f.is_file() and f.name != "config.user.json":
                    rel = f.relative_to(smapi_int).as_posix()
                    staged[f"StardewValley/smapi-internal/{rel}"] = f.read_bytes()

    if args.extra_rules:
        rb = args.extra_rules.read_bytes()
        json.loads(rb.decode("utf-8-sig"))
        staged[".garden/extra_mod_rules.json"] = rb

    with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for arc in sorted(staged):
            add_zip_file(zf, arc, staged[arc])

    with zipfile.ZipFile(out_zip, "r") as chk:
        assert chk.testzip() is None, "Generated ZIP failed CRC integrity check!"

    raw_zip = out_zip.read_bytes()
    hp_sha = sha256_bytes(raw_zip)
    hp_size = len(raw_zip)
    tag = args.release_tag or f"v{args.version}"
    hp_url = f"https://github.com/{args.repo}/releases/download/{tag}/{out_zip.name}"

    print(f"OK: {out_zip} ({len(staged)} files, {hp_size} bytes)")
    print(f"  hotpatch_sha256 : {hp_sha}")
    print(f"  hotpatch_size   : {hp_size}")
    print(f"  hotpatch_url    : {hp_url}")

    if args.update_version_json:
        vdoc = json.loads(args.update_version_json.read_text(encoding="utf-8"))
        vdoc["version"] = args.version
        vdoc["build"] = args.build
        vdoc["min_ipa_build"] = args.min_ipa_build
        vdoc["date"] = dt.date.today().isoformat()
        vdoc["hotpatch_url"] = hp_url
        vdoc["hotpatch_sha256"] = hp_sha
        vdoc["hotpatch_size"] = hp_size
        args.update_version_json.write_text(json.dumps(vdoc, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"  Updated {args.update_version_json}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
