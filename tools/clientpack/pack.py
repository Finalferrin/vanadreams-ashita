r"""Pack a clean, updated FINAL FANTASY XI folder into zips the launcher can install.

    python pack.py <ffxi folder> <out folder> [--limit-mb 1024]

Reads the game folder, never writes to it. Writes <out>\<client_ver>\*.zip and
manifest.json. The zips are deterministic (sorted entries, the files' own
timestamps), so an unchanged chunk hashes the same on the next pack and only
changed chunks need uploading after a game update.
"""

import argparse
import hashlib
import json
import os
import re
import sys
import zipfile
from datetime import datetime, timezone

# Not the game: per-player state, logs, and leftovers of other tools.
# The launcher creates the empty folders the client expects (see create_dirs).
EXCLUDE_DIRS = {"user", "sys", "temp", "0", "119", "280"}
EXCLUDE_FILES = {"hooklog.log", "recipes.list"}
CREATE_DIRS = ["USER", "SYS", "TEMP"]

# PlayOnlineViewer, which xiloader needs for polcore.dll: usr holds the player's own PlayOnline login data,
# reshade-shaders is a third-party add-on, tmp is scratch. Empty usr\all and tmp are made at install.
VIEWER_FOLDER = "PlayOnlineViewer"
VIEWER_EXCLUDE_DIRS = {"usr", "reshade-shaders", "tmp"}
VIEWER_CREATE_DIRS = ["usr/all", "tmp"]

# Same rule as the launcher's ClientVersion.NewestStamp.
STAMP = re.compile(r"^(3\d{7}_\d+)\b", re.MULTILINE)


def client_version(root):
    with open(os.path.join(root, "patch.cfg"), encoding="latin-1") as f:
        stamps = STAMP.findall(f.read())
    if not stamps:
        sys.exit("patch.cfg carries no version stamp")
    return max(stamps)


def walk(folder, root):
    """Every file under folder, as (relative path with forward slashes, full path), sorted."""
    out = []
    for dirpath, dirnames, filenames in os.walk(folder):
        dirnames.sort()
        for name in filenames:
            full = os.path.join(dirpath, name)
            out.append((os.path.relpath(full, root).replace("\\", "/"), full))
    out.sort(key=lambda p: p[0].lower())
    return out


def plan(root, limit, exclude_dirs=EXCLUDE_DIRS, exclude_files=EXCLUDE_FILES):
    """Chunks as (name, [(rel, full), ...]). Loose files and small folders share 'base';
    a folder over the limit is cut into numbered parts at file boundaries."""
    base, chunks = [], []
    for entry in sorted(os.listdir(root), key=str.lower):
        full = os.path.join(root, entry)
        if os.path.isfile(full):
            if entry.lower() not in exclude_files:
                base.append((entry, full))
            continue
        if entry.lower() in exclude_dirs:
            continue
        files = walk(full, root)
        size = sum(os.path.getsize(f) for _, f in files)
        if size < 64 * 1024 * 1024:
            base.extend(files)
        elif size <= limit:
            chunks.append((entry, files))
        else:
            part, used, n = [], 0, 1
            for rel, f in files:
                s = os.path.getsize(f)
                if part and used + s > limit:
                    chunks.append((f"{entry}-{n:02d}", part))
                    part, used, n = [], 0, n + 1
                part.append((rel, f))
                used += s
            chunks.append((f"{entry}-{n:02d}", part))
    base.sort(key=lambda p: p[0].lower())
    return [("base", base)] + chunks


def write_zip(path, files):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for rel, full in files:
            t = datetime.fromtimestamp(os.path.getmtime(full)).timetuple()[:6]
            info = zipfile.ZipInfo(rel, date_time=max(t, (1980, 1, 1, 0, 0, 0)))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(full, "rb") as src, z.open(info, "w") as dst:
                while block := src.read(1 << 20):
                    dst.write(block)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while block := f.read(1 << 20):
            h.update(block)
    return h.hexdigest()


def pack_folder(root, dest, limit, prefix="", dest_folder=None, exclude_dirs=EXCLUDE_DIRS, exclude_files=EXCLUDE_FILES):
    chunks = []
    for name, files in plan(root, limit, exclude_dirs, exclude_files):
        zname = prefix + name + ".zip"
        zpath = os.path.join(dest, zname)
        write_zip(zpath, files)
        chunk = {
            "name": zname,
            "size": os.path.getsize(zpath),
            "sha256": sha256(zpath),
            "files": len(files),
            "unpacked": sum(os.path.getsize(f) for _, f in files),
        }
        if dest_folder:
            chunk["dest"] = dest_folder
        chunks.append(chunk)
        print(f"{zname}  {len(files)} files  {chunk['size'] / 2**20:,.0f} MB", flush=True)
    return chunks


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ffxi")
    ap.add_argument("out")
    ap.add_argument("--limit-mb", type=int, default=1024)
    ap.add_argument("--viewer", help="also pack this PlayOnlineViewer folder into the same manifest")
    a = ap.parse_args()

    ver = client_version(a.ffxi)
    dest = os.path.join(a.out, ver)
    os.makedirs(dest, exist_ok=True)
    chunks = pack_folder(a.ffxi, dest, a.limit_mb * 1024 * 1024)
    if a.viewer:
        chunks += pack_folder(a.viewer, dest, a.limit_mb * 1024 * 1024, prefix="pol-", dest_folder=VIEWER_FOLDER,
                              exclude_dirs=VIEWER_EXCLUDE_DIRS, exclude_files=set())

    manifest = {
        "schema": 1,
        "client_ver": ver,
        "packed": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "create_dirs": CREATE_DIRS,
        "viewer_create_dirs": VIEWER_CREATE_DIRS if a.viewer else [],
        "total_size": sum(c["size"] for c in chunks),
        "total_unpacked": sum(c["unpacked"] for c in chunks),
        "chunks": chunks,
    }
    with open(os.path.join(dest, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print(f"client {ver}: {len(chunks)} zips, {manifest['total_size'] / 2**30:.2f} GB -> {dest}")


if __name__ == "__main__":
    main()
