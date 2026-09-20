#!/usr/bin/env python3
"""docker-rep2-win の Alpine minirootfs マニフェスト (secret gist) を更新する。

Usage:
    python3 scripts/update_alpine_manifest.py <version> [--arch x86_64|aarch64] [--untested] [--dry-run] [-y]

<version> は x.y.z 形式。dl-cdn の .sha512 サイドカーからハッシュを取得し、
gh gist edit で versions.json を指定バージョンの単一エントリに置き換える。
アプリ側 (InstallService.VerifyFileHashAsync) は SHA512 固定で検証するためハッシュは .sha512 を使う。
"""
import argparse
import json
import re
import subprocess
import sys
import tempfile
import urllib.request

GIST_ID = "341c40b1a0861bb72b24736a2c7ca49e"
GIST_FILE = "versions.json"
CDN_BASE = "https://dl-cdn.alpinelinux.org/alpine"


def fetch_manifest_hash(branch: str, version: str, arch: str) -> str:
    url = f"{CDN_BASE}/{branch}/releases/{arch}/alpine-minirootfs-{version}-{arch}.tar.gz.sha512"
    with urllib.request.urlopen(url, timeout=30) as resp:
        token = resp.read().decode("ascii").split(" ", 1)[0].strip()
    if not re.fullmatch(r"[0-9a-f]{128}", token):
        sys.exit(f"sha512 サイドカーの内容が不正です: {url}\n  {token!r}")
    return token


def main() -> None:
    parser = argparse.ArgumentParser(description="マニフェスト gist の Alpine バージョンを更新する")
    parser.add_argument("version", help="Alpine のバージョン (例: 3.24.2)")
    parser.add_argument("--arch", choices=["x86_64", "aarch64"], default="x86_64")
    parser.add_argument("--untested", action="store_true", help="is_tested=false で登録する")
    parser.add_argument("--dry-run", action="store_true", help="gist を更新せず結果表示のみ")
    parser.add_argument("-y", "--yes", action="store_true", help="確認プロンプトを省略")
    args = parser.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        sys.exit(f"バージョンは x.y.z 形式で指定してください: {args.version!r}")

    branch = "v" + args.version.rpartition(".")[0]
    digest = fetch_manifest_hash(branch, args.version, args.arch)
    new_versions = [{
        "version": args.version,
        "url": f"{CDN_BASE}/{branch}/releases/{args.arch}/alpine-minirootfs-{args.version}-{args.arch}.tar.gz",
        "hash": digest,
        "is_tested": not args.untested,
    }]

    new_json = json.dumps({"versions": new_versions}, indent=2, ensure_ascii=False) + "\n"

    print("== 更新後の versions.json ==")
    print(new_json)
    if args.dry_run:
        print("(dry-run のため gist は更新していません)")
        return
    if not args.yes and input("gist を更新しますか? [y/N] ").strip().lower() != "y":
        sys.exit("中止しました")

    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fp:
        fp.write(new_json)
        tmp_path = fp.name
    proc = subprocess.run(
        ["gh", "gist", "edit", GIST_ID, "--filename", GIST_FILE, tmp_path],
        capture_output=True, text=True,
    )
    if proc.returncode != 0:
        sys.exit(f"gh gist edit 失敗:\n{proc.stderr}")

    verify = subprocess.run(
        ["gh", "gist", "view", GIST_ID, "-f", GIST_FILE],
        capture_output=True, text=True,
    )
    if verify.returncode != 0 or json.loads(verify.stdout) != {"versions": new_versions}:
        sys.exit("更新後の gist 内容が期待値と一致しません")
    print(f"gist を更新しました: {args.version} (is_tested={new_versions[0]['is_tested']})")


if __name__ == "__main__":
    main()
