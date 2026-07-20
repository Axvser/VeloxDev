"""
generate_tree.py

Scans each language directory under Assets/Docs/ for index.md files,
builds a hierarchical tree.json index used by the Avalonia Markdown viewer.
Run automatically on every build via MSBuild BeforeBuild target.
"""

import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))


# ── Priority order for root pages ──────────────────────────────────────
# Directories whose names appear here are sorted in this order at the root
# level; all others are appended alphabetically.  This mirrors the original
# wiki page order (Welcome → Version → Start → Question → Best Practices).
ROOT_PRIORITY = [
    "Welcome", "欢迎",
    "Version", "版本",
    "Start", "开始",
    "Question", "问题",
    "Best Practices", "最佳实践",
]


def _sort_key(name: str) -> str:
    """Return a sort key: priority items first, then alphabetical."""
    try:
        idx = ROOT_PRIORITY.index(name)
        return f"__{idx:03d}__{name}"
    except ValueError:
        return name


def _scan(dir_path: str, lang_root: str) -> list[dict]:
    """Scan *dir_path* for subdirectories that contain index.md and return
    them as a list of ``{title, path, children}`` dicts.

    *lang_root* is the language root (e.g. ``…/en``) – paths are computed
    relative to it so the app can load ``avares://…/Assets/Docs/{lang}/{path}/index.md``.
    """
    entries = sorted(os.listdir(dir_path))

    nodes: list[dict] = []
    for entry in entries:
        child_path = os.path.join(dir_path, entry)
        if not os.path.isdir(child_path):
            continue
        if not os.path.isfile(os.path.join(child_path, "index.md")):
            continue

        # path is relative to lang_root (not the immediate parent)
        rel_path = os.path.relpath(child_path, lang_root).replace("\\", "/")

        children = _scan(child_path, lang_root)

        nodes.append({
            "title": entry,
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

        # Root level: use ROOT_PRIORITY sort; nested levels sort alphabetically.
        entries = sorted(os.listdir(lang_dir), key=_sort_key)

        pages: list[dict] = []
        for entry in entries:
            child_path = os.path.join(lang_dir, entry)
            if not os.path.isdir(child_path):
                continue
            if not os.path.isfile(os.path.join(child_path, "index.md")):
                continue

            rel_path = entry  # root children: just the entry name
            children = _scan(child_path, lang_dir)
            pages.append({
                "title": entry,
                "path": rel_path,
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
