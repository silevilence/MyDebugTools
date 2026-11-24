"""Prepare notes and publish from the checked-out tag. Only drafts may be modified."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

NUMBER = r"(?:0|[1-9][0-9]*)"
TAG = re.compile(rf"[vV]({NUMBER}\.{NUMBER}\.{NUMBER})")
OUTPUT = Path("artifacts/release")
ASSETS = OUTPUT / "assets"
RECEIPT = "release-manifest.json"


def release_notes(markdown, version):
    sections = []
    current = None
    fence = None
    for line in markdown.splitlines():
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})(.*)$", line)
        if fence:
            if marker and marker[1].startswith(fence) and not marker[2].strip():
                fence = None
        elif marker:
            fence = marker[1]
        elif heading := re.match(r"^ {0,3}##(?:\s+|$)(.*)$", line):
            label = heading[1].strip()
            match = TAG.fullmatch(label)
            current = [] if (match and match[1] == version) or label == version else None
            if current is not None:
                sections.append(current)
            continue
        if current is not None:
            current.append(line)
    body = "\n".join(sections[0]).strip("\n") if len(sections) == 1 else ""
    if not body.strip():
        raise ValueError(f"changelog.md 需要唯一且非空的 {version} 二级标题，匹配 {len(sections)} 节")
    return body


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, encoding="utf-8").stdout.strip()


def gh(*args):
    result = subprocess.run(["gh", *map(str, args)], check=True, stdout=subprocess.PIPE)
    return result.stdout.decode("utf-8")


def releases():
    pages = json.loads(gh("api", "--paginate", "--slurp", "repos/{owner}/{repo}/releases?per_page=100"))
    return [item for page in pages for item in page]


def sha(data):
    return "sha256:" + hashlib.sha256(data).hexdigest()


def asset_names(version):
    return {f"MyDebugTools-{version}-win-x64.exe", "appcast.xml", "appcast.xml.signature"}


def records(assets):
    return {a["name"]: {"size": a["size"], "digest": a["digest"]} for a in assets}


def check_public(item, tag, commit, notes, version):
    if item["body"].replace("\r\n", "\n").strip("\n") != notes or item["prerelease"]:
        raise ValueError(f"{tag} 已公开，发布说明或正式版状态冲突")
    receipt_asset = next((a for a in item["assets"] if a["name"] == RECEIPT), None)
    if not receipt_asset:
        raise ValueError(f"{tag} 缺少 {RECEIPT}")
    receipt = json.loads(gh("api", "-H", "Accept: application/octet-stream",
                            f"repos/{{owner}}/{{repo}}/releases/assets/{receipt_asset['id']}"))
    actual = records(a for a in item["assets"] if a["name"] != RECEIPT)
    if (set(actual) != asset_names(version) or receipt != {
        "tag": tag, "commit": commit, "notes": sha(notes.encode()), "assets": actual
    } or any(not a["digest"] or a["size"] <= 0 for a in actual.values())):
        raise ValueError(f"{tag} 已公开，提交或资产摘要与清单冲突")


def publish(tag, commit, notes, version, items, draft):
    files = {p.name: p for p in ASSETS.iterdir() if p.is_file() and p.name != RECEIPT}
    if set(files) != asset_names(version):
        raise ValueError(f"发布目录资产不匹配：{sorted(files)}")
    manifest = {name: {"size": p.stat().st_size, "digest": sha(p.read_bytes())} for name, p in files.items()}
    receipt = ASSETS / RECEIPT
    receipt.write_text(json.dumps({"tag": tag, "commit": commit, "notes": sha(notes.encode()),
                                   "assets": manifest}, sort_keys=True), encoding="utf-8")
    files[RECEIPT] = receipt
    manifest[RECEIPT] = {"size": receipt.stat().st_size, "digest": sha(receipt.read_bytes())}
    if draft is None:
        gh("release", "create", tag, "--draft", "--verify-tag", "--target", commit,
           "--title", tag, "--notes-file", OUTPUT / "notes.md")
        existing = {}
    else:
        if draft["target_commitish"] != commit or draft["body"].replace("\r\n", "\n").strip("\n") != notes:
            raise ValueError(f"{tag} 草稿提交或正文冲突")
        existing = records(draft["assets"])
        if existing.keys() - files.keys():
            raise ValueError(f"{tag} 草稿有意外资产")
    for name, path in files.items():
        if existing.get(name) != manifest[name]:
            gh("release", "upload", tag, path, *(["--clobber"] if name in existing else []))
    state = next(i for i in releases() if i["tag_name"] == tag)
    if not state["draft"] or records(state["assets"]) != manifest:
        raise ValueError(f"{tag} 草稿资产不完整或摘要不匹配")
    print(f"{tag} · {commit}", flush=True)
    for name, asset in manifest.items():
        print(f"{name} · {asset['size']} bytes · {asset['digest']}", flush=True)
    newer = any(not i["draft"] and not i["prerelease"] and (m := TAG.fullmatch(i["tag_name"]))
                and tuple(map(int, m[1].split("."))) > tuple(map(int, version.split("."))) for i in items)
    gh("release", "edit", tag, "--draft=false", "--latest=" + str(not newer).lower())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("prepare", "publish"))
    args = parser.parse_args()
    tag = os.environ["GITHUB_REF_NAME"]
    match = TAG.fullmatch(tag)
    env_output = Path(os.environ["GITHUB_OUTPUT"])
    with env_output.open("a", encoding="utf-8") as output:
        if os.environ["GITHUB_REF_TYPE"] != "tag" or not match:
            print("跳过非正式版本", tag)
            return
        version = match[1]
        actual = run("dotnet", "msbuild", "src/Desktop/MyDebugTools.fsproj", "-getProperty:Version")
        if actual != version:
            raise ValueError(f"宿主项目版本 {actual} 与标签 {tag} 不匹配")
        commit = run("git", "rev-parse", "HEAD")
        if run("git", "rev-parse", f"refs/tags/{tag}^{{commit}}") != commit:
            raise ValueError(f"工作目录与标签 {tag} 不一致")
        notes = release_notes(Path("changelog.md").read_text(encoding="utf-8-sig"), version)
        OUTPUT.mkdir(parents=True, exist_ok=True)
        (OUTPUT / "notes.md").write_bytes(notes.encode())
        items = releases()
        matches = [i for i in items if i["tag_name"].lower() == tag.lower()]
        if any(i["tag_name"] != tag for i in matches) or len(matches) > 1:
            raise ValueError(f"{tag} 的数字版本已有其他 Release")
        existing = next(iter(matches), None)
        if existing and not existing["draft"]:
            check_public(existing, tag, commit, notes, version)
            print(f"{tag} {commit} 已公开，正文及平台资产摘要核对通过")
        elif args.action == "prepare":
            print(f"version={version}\nbuild=true", file=output)
        else:
            publish(tag, commit, notes, version, items, existing)


if __name__ == "__main__":
    main()
