"""Exports every official/source/Maps/<pack>/<id>.kra into content/<pack>/maps/<id>/.

Headless: kritarunner -s mortz.build <repo> [<pack>[/<id>]]   (Mortz.Tools `map build` runs this)
The last line is "mortz-build: <built> built, <failed> failed".
"""

import glob
import os
import traceback

from . import document, export


def build(app, repo, target):
    parts = [part for part in (target or "").replace("\\", "/").split("/") if part]
    sources = os.path.join(repo, "official", "source", "Maps")
    pattern = os.path.join(sources, parts[0] if parts else "*",
                           f"{parts[1]}.kra" if len(parts) > 1 else "*.kra")
    paths = sorted(glob.glob(pattern))
    if not paths:
        print(f"no .kra sources match '{target or ''}' in {sources}")
    built = 0
    for path in paths:
        pack = os.path.basename(os.path.dirname(path))
        map_id = os.path.splitext(os.path.basename(path))[0]
        if _build_one(app, path, os.path.join(repo, "content", pack), f"{pack}/{map_id}", map_id):
            built += 1
    return built, len(paths) - built


def _build_one(app, path, pack_directory, label, map_id):
    doc = app.openDocument(path)
    if doc is None:
        print(f"{label}: error: Krita could not open {path}")
        return False
    try:
        if document.load_annotation(doc).get("id") != map_id:
            print(f"{label}: error: the map id in the document must match the file name")
            return False
        result, written = export.export(doc, pack_directory)
        for issue in result.issues:
            shape = f"[{issue['shape']}] " if issue["shape"] else ""
            print(f"{label}: {issue['severity']}: {shape}{issue['message']}")
        if written:
            print(f"{label}: wrote {written}")
        return written is not None
    except Exception:
        print(f"{label}: error: {traceback.format_exc()}")
        return False
    finally:
        doc.close()


def __main__(args):
    # kritarunner swallows exceptions and its exit code is unreliable, so everything is printed.
    built, failed = 0, 1
    try:
        from krita import Krita
        built, failed = build(Krita.instance(), os.path.abspath(args[0]),
                              args[1] if len(args) > 1 else None)
    except BaseException:
        traceback.print_exc()
    print(f"mortz-build: {built} built, {failed} failed")
