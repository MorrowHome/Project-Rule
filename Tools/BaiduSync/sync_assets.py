#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把百度网盘某个目录里的美术资源同步进 Unity 工程的 Art/Sprites。

分两步走：
  1) 用 BaiduPCS-Go 把远端目录整体拉到本地暂存区 Tools/BaiduSync/.staging/
     （BaiduPCS-Go 自带增量：已存在的同名文件默认跳过，所以重复跑很快）
  2) 把暂存区里的 PNG 复制进 Unity 的 Art/Sprites，保留相对目录结构；
     逐文件比对内容，只有「新增」和「确实变了」的才落盘

之所以要暂存区而不是直接下进 Assets/：Unity 会把落进 Assets 的任何文件都导入一遍，
先把非 PNG、半截文件挡在外面，Assets 里只会出现最终要的那批图。

用法：
    python sync_assets.py                 # 完整同步
    python sync_assets.py --dry-run       # 只测远端目录能不能读到，不落地
    python sync_assets.py --refresh       # 覆盖暂存区的同名文件（远端改了图时用）
    python sync_assets.py --clean         # 清空暂存区后重新拉一遍
    python sync_assets.py --all-files     # 非 PNG 也搬进去（默认只搬 PNG）
    python sync_assets.py --remote /别的/路径   # 临时换远端目录，不改配置
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parents[1]
EXE_NAME = "BaiduPCS-Go.exe" if os.name == "nt" else "BaiduPCS-Go"
EXE = TOOL_DIR / "bin" / EXE_NAME
STAGING = TOOL_DIR / ".staging"
DEFAULT_CONFIG = TOOL_DIR / "config.json"


def decode(raw: bytes) -> str:
    """BaiduPCS-Go 在 Windows 上可能吐 UTF-8 也可能吐 GBK，都试一遍。"""
    for enc in ("utf-8", "gbk"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            pass
    return raw.decode("utf-8", "replace")


def mask(args: list[str]) -> str:
    """打印命令行前把 cookie 打码，别让它留在终端记录里。"""
    return " ".join(
        "-cookies=***" if a.startswith("-cookies=") else a for a in args
    )


def pcs(args: list[str], *, echo: bool = True) -> tuple[int, str]:
    """跑一次 BaiduPCS-Go，返回 (退出码, 合并后的输出)。"""
    if echo:
        print(f"  $ BaiduPCS-Go {mask(args)}")
    proc = subprocess.run([str(EXE), *args], capture_output=True)
    return proc.returncode, decode(proc.stdout) + decode(proc.stderr)


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def same_content(a: Path, b: Path) -> bool:
    if a.stat().st_size != b.stat().st_size:
        return False
    return sha256(a) == sha256(b)


def load_config(path: Path) -> dict:
    if not path.exists():
        sys.exit(
            f"找不到配置文件：{path}\n"
            f"把 config.example.json 复制成 config.json 再填。"
        )
    try:
        cfg = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        sys.exit(f"配置文件不是合法 JSON：{path}\n  {exc}")
    missing = [k for k in ("remote_dir", "local_dir") if not cfg.get(k)]
    if missing:
        sys.exit(f"配置缺少字段：{', '.join(missing)}")
    cfg.setdefault("cookies", "")
    cfg.setdefault("png_only", True)
    cfg.setdefault("strip_remote_root", True)
    cfg.setdefault("threads", 8)
    cfg.setdefault("parallel_files", 3)
    cfg.setdefault("rename_map", {})
    cfg.setdefault("rename_files", False)
    cfg.setdefault("extra_args", [])
    return cfg


def resolve_local(raw: str) -> Path:
    p = Path(raw)
    return (p if p.is_absolute() else REPO_ROOT / p).resolve()


def ensure_login(cookies: str) -> None:
    print("[1/3] 登录百度账号")
    code, out = pcs(["login", f"-cookies={cookies}"])
    if code != 0 or "失败" in out or "错误" in out:
        sys.exit(f"登录失败，检查 config.json 里的 cookies 是否过期：\n{out.strip()}")
    print("  已登录")


def download(remote: str, refresh: bool, threads: int, parallel: int,
             extra: list[str], png_only: bool) -> None:
    print(f"[2/3] 拉取远端目录 → 暂存区\n  远端 {remote}\n  暂存 {STAGING}")
    STAGING.mkdir(parents=True, exist_ok=True)
    base = remote.rstrip("/")
    if png_only:
        # 只拉 *.png。远端每个动作目录里还混着 psd 源文件和 mp4 参考视频，
        # 整目录下会白拉几十上百 MB。通配符覆盖 1~3 层深，配合 --fullpath
        # 让落盘仍保留 动作/序列/ 的层级。
        targets = [f"{base}/{stars}.png" for stars in ("*", "*/*", "*/*/*")]
    else:
        targets = [base]
    args = [
        "download", *targets,
        f"--saveto={STAGING}",
        "--fullpath",
        f"-p={threads}",
        f"-l={parallel}",
        "--mtime",
        *extra,
    ]
    if refresh:
        args.append("--ow")
    code, out = pcs(args)
    tail = "\n".join(out.strip().splitlines()[-15:])
    if tail:
        print("  " + tail.replace("\n", "\n  "))
    if code != 0:
        sys.exit(f"下载失败（退出码 {code}）。远端路径写对了吗？")


def apply_map(rel: Path, mapping: dict) -> "Path | None":
    """把文件相对路径按 rename_map 改名。

    键是远端相对目录路径（如 "跑步/跑步序列"），值是目标目录名。
    取最长匹配前缀，这样映射既能压平层级（值只有一层），也能只换掉其中一段。
    值写成 null 表示「这个目录不要」。
    """
    for i in range(len(rel.parts) - 1, 0, -1):
        key = "/".join(rel.parts[:i])
        if key in mapping:
            target = mapping[key]
            if target is None:
                return None
            return Path(target) / Path(*rel.parts[i:])
    return rel


def natural_key(name: str) -> list:
    """让 蹬墙跳-2.png 排在 蹬墙跳-10.png 前面（纯字典序会反过来）。"""
    return [int(t) if t.isdigit() else t.lower()
            for t in re.split(r"(\d+)", name)]


def renumber(plan: list) -> list:
    """把每个目标目录里的帧按自然序重编成 Hero_<目录名>_NN.png。

    <目录名> 就是映射后的 state 名（Idle / Run / WallJump…），
    所以这条规则产出的路径正好是 HeroAnimatorBuilder 引用的那种。
    """
    by_dir: dict = defaultdict(list)
    for src, rel in plan:
        by_dir[rel.parent].append(src)

    out = []
    for parent, sources in by_dir.items():
        sources.sort(key=lambda p: natural_key(p.name))
        state = parent.name if parent.name not in ("", ".") else ""
        for i, src in enumerate(sources, 1):
            stem = f"Hero_{state}_{i:02d}" if state else f"Hero_{i:02d}"
            out.append((src, parent / (stem + src.suffix.lower())))
    return out


def install(remote: str, local: Path, png_only: bool, strip_root: bool,
            rename_map: dict, rename_files: bool) -> dict:
    print(f"[3/3] 安装到 Unity\n  目标 {local}")

    dupes = [name for name, n in Counter(
        v for v in rename_map.values() if v is not None
    ).items() if n > 1]
    if dupes:
        sys.exit("rename_map 里有两个中文目录名映射到了同一个英文名，会互相覆盖："
                 + ", ".join(dupes))

    src_root = STAGING
    if strip_root:
        # 下载用了 --fullpath，暂存区里是按网盘完整路径落的（规则游戏/主角/...），
        # 这里把 remote_dir 那两层剥掉，只留 动作/序列/ 开始的部分。
        cand = STAGING / Path(remote.strip("/"))
        if cand.is_dir():
            src_root = cand

    if not src_root.is_dir():
        sys.exit(f"暂存区里没找到内容：{src_root}\n远端目录是不是空的？")

    local.mkdir(parents=True, exist_ok=True)
    local_prefix = str(local) + os.sep
    skipped = excluded = 0
    unmapped: set[str] = set()

    # 第一遍：只算「每个文件该落到哪」，不碰磁盘
    plan = []
    for src in sorted(p for p in src_root.rglob("*") if p.is_file()):
        if png_only and src.suffix.lower() != ".png":
            skipped += 1
            continue
        rel = src.relative_to(src_root)
        if rename_map:
            mapped = apply_map(rel, rename_map)
            if mapped is None:
                excluded += 1
                continue
            if mapped == rel:
                unmapped.update(
                    seg for seg in rel.parts[:-1] if not seg.isascii())
            rel = mapped
        plan.append((src, rel))

    if rename_files:
        plan = renumber(plan)

    # 第二遍：落盘
    added, updated, unchanged = [], [], 0
    for src, rel in plan:
        dst = (local / rel).resolve()
        # 远端来的文件名不可信，挡住 ../ 之类的越界路径
        if not str(dst).startswith(local_prefix):
            print(f"  ! 跳过越界路径：{rel}")
            skipped += 1
            continue
        if dst.exists():
            if same_content(src, dst):
                unchanged += 1
                continue
            shutil.copy2(src, dst)
            updated.append(str(rel))
            print(f"  更新 {rel}")
        else:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
            added.append(str(rel))
            print(f"  新增 {rel}")

    return {
        "added": added, "updated": updated,
        "unchanged": unchanged, "skipped": skipped,
        "excluded": excluded, "unmapped": unmapped,
    }


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace",
                              line_buffering=True)
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser(
        description="百度网盘 → Unity Art/Sprites 美术资源同步",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--config", type=Path, default=DEFAULT_CONFIG,
                    help="配置文件路径（默认 Tools/BaiduSync/config.json）")
    ap.add_argument("--remote", help="临时覆盖远端目录，不改配置文件")
    ap.add_argument("--dry-run", action="store_true",
                    help="只测远端目录能不能读到，不下载不落地")
    ap.add_argument("--refresh", action="store_true",
                    help="覆盖暂存区里的同名文件（远端改图后用）")
    ap.add_argument("--clean", action="store_true",
                    help="先清空暂存区再拉")
    ap.add_argument("--all-files", action="store_true",
                    help="非 PNG 也搬进 Unity（默认只搬 PNG）")
    ap.add_argument("--install-only", action="store_true",
                    help="跳过登录与下载，直接把暂存区已有内容装进 Unity（调 rename_map 时用）")
    args = ap.parse_args()

    if not EXE.exists():
        sys.exit(f"找不到 BaiduPCS-Go：{EXE}\n参见 README.md 的『安装』一节。")

    cfg = load_config(args.config)
    cookies = cfg["cookies"].strip()
    if not cookies and not args.install_only:
        sys.exit(
            "配置里的 cookies 是空的。\n"
            "从浏览器 F12 → Network → 任意网盘请求的 Request Headers 里\n"
            "复制 Cookie 整段（必须含 BDUSS=...），填进 config.json 的 cookies 字段。"
        )

    remote = args.remote or cfg["remote_dir"]
    local = resolve_local(cfg["local_dir"])

    if args.install_only:
        print("[跳过] 登录与下载，直接安装暂存区里已有的内容")
    else:
        ensure_login(cookies)

        if args.dry_run:
            # 只用 ls 验证「路径能不能读到」。别用 download --test —— 它会逐个文件去
            # 解析下载直链，几百张图能卡上好几分钟，而这里只需要确认目录存在。
            print(f"[2/3] 列远端目录（不落地）\n  远端 {remote}")
            code, out = pcs(["ls", remote, *cfg["extra_args"]])
            print("  " + out.strip().replace("\n", "\n  "))
            print("\n路径可读，没有文件被写入。" if code == 0
                  else "\n远端读取失败：路径写错了，或者 cookie 没有该目录的权限。")
            return 0 if code == 0 else 1

        if args.clean and STAGING.exists():
            print(f"  清空暂存区 {STAGING}")
            shutil.rmtree(STAGING)

        download(remote, args.refresh, cfg["threads"], cfg["parallel_files"],
                 cfg["extra_args"], not args.all_files)

    result = install(remote, local, not args.all_files,
                     cfg["strip_remote_root"], cfg["rename_map"],
                     cfg["rename_files"])

    print(
        f"\n完成：新增 {len(result['added'])} / 更新 {len(result['updated'])} / "
        f"未变 {result['unchanged']} / 跳过 {result['skipped']} / "
        f"按映射排除 {result['excluded']}"
    )
    if result["unmapped"]:
        print("\n⚠ 这些目录名在 rename_map 里没有对应项，已按中文原名落下：")
        for name in sorted(result["unmapped"]):
            print(f"    {name}")
    if result["added"] or result["updated"]:
        print("回 Unity 编辑器等它自动导入（或 Ctrl+R 刷新），新图会生成 .meta，记得一起提交。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
