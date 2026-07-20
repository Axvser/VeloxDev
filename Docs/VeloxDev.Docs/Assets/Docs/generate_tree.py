"""
generate_tree.py

Scans each language directory under Assets/Docs/ for index.md files,
builds a hierarchical tree.json index used by the Avalonia Markdown viewer.
Pages are ordered by directory name; numeric prefixes like "1_QuickStart"
are stripped in the displayed title but preserved in the path for file loading.
"""

import json
import os
import re

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# Matches leading "N_" or "NN_" prefix (e.g. "1_", "12_")
_PREFIX_RE = re.compile(r"^\d+_(.+)$")


def _title(name: str) -> str:
    """Strip numeric prefix from a directory name to get the display title.
    e.g. "1_QuickStart" → "QuickStart", "Welcome" → "Welcome".
    """
    m = _PREFIX_RE.match(name)
    return m.group(1) if m else name


def _scan(dir_path: str, lang_root: str) -> list[dict]:
    """Scan *dir_path* for subdirectories that contain index.md and return
    them as a list of ``{title, path, children}`` dicts.

    Sorting is natural (OS order) — use numeric prefixes to control sequence.
    *lang_root* is the language root — paths are computed relative to it.
    """
    nodes: list[dict] = []
    for entry in sorted(os.listdir(dir_path)):
        child_path = os.path.join(dir_path, entry)
        if not os.path.isdir(child_path):
            continue
        if not os.path.isfile(os.path.join(child_path, "index.md")):
            continue

        rel_path = os.path.relpath(child_path, lang_root).replace("\\", "/")
        children = _scan(child_path, lang_root)

        nodes.append({
            "title": _title(entry),
            "path": rel_path,
            "children": children,
        })
    return nodes


def main():
    for lang in sorted(os.listdir(SCRIPT_DIR)):
        lang_dir = os.path.join(SCRIPT_DIR, lang)
        if not os.path.isdir(lang_dir):
            continue
        if lang.startswith(".") or lang == "__pycache__":
            continue

        pages: list[dict] = []
        for entry in sorted(os.listdir(lang_dir)):
            child_path = os.path.join(lang_dir, entry)
            if not os.path.isdir(child_path):
                continue
            if not os.path.isfile(os.path.join(child_path, "index.md")):
                continue

            children = _scan(child_path, lang_dir)
            pages.append({
                "title": _title(entry),
                "path": entry,
                "children": children,
            })

        tree = {"Pages": pages}
        tree_path = os.path.join(lang_dir, "tree.json")
        with open(tree_path, "w", encoding="utf-8") as f:
            json.dump(tree, f, ensure_ascii=False, indent=2)
        n = len(pages)
        print(f"[generate_tree] Updated: {tree_path} ({n} root pages)")


if __name__ == "__main__":
    main()
