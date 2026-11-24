#!/usr/bin/env python3
"""
sync_wiki_skill.py

Regenerate the CloudGlyph wiki-writing skill from the `Wiki` submodule and install
the assembled copy into this repository's `.claude` directory.

The Wiki submodule owns the skill modules, the installer and the parity check; this
script only fixes the paths for the host repository and adds a check mode.

Usage:
    python sync_wiki_skill.py                 # parity check + install (en)
    python sync_wiki_skill.py --lang zh       # install the Chinese instruction build
    python sync_wiki_skill.py --check         # report whether the installed copy is current
    python sync_wiki_skill.py --no-parity     # skip the parity smoke test

`--check` regenerates the assembled skill in the submodule (an ignored file) and
compares it with `.claude/skills/cloud-glyph-wiki-create/SKILL.md`; it exits 0 when
they match and 1 when the installed copy is missing or stale. It does not install.

The install writes:
    .claude/skills/cloud-glyph-wiki-create/SKILL.md   (plus its Templates/)
    .claude/agents/wiki-reviewer.md
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent
SKILLS = REPO_ROOT / "Wiki" / "skills"
INSTALLER = SKILLS / "claude_install.py"
GENERATOR = SKILLS / "gen_skill.py"
PARITY = SKILLS / "check_parity.py"
INSTALLED = REPO_ROOT / ".claude" / "skills" / "cloud-glyph-wiki-create" / "SKILL.md"


def run(script: Path, *args: str) -> int:
    """Run one submodule script from its own directory and return its exit code."""
    if not script.is_file():
        print(
            f"error: {script} is missing. The Wiki submodule is not initialized; "
            "run `git submodule update --init Wiki`.",
            file=sys.stderr,
        )
        return 2

    result = subprocess.run(
        [sys.executable, str(script), *args],
        cwd=script.parent,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return result.returncode


def check(lang: str) -> int:
    """Report whether the installed copy matches a freshly assembled skill."""
    code = run(GENERATOR, "--lang", lang)
    if code != 0:
        return code

    generated = SKILLS / "SKILL.md"
    if not generated.is_file():
        print(f"error: {generated} was not produced", file=sys.stderr)
        return 2

    if not INSTALLED.is_file():
        print(f"not installed: {INSTALLED}")
        return 1

    if generated.read_bytes() == INSTALLED.read_bytes():
        print(f"up to date ({lang}): {INSTALLED}")
        return 0

    print(
        f"stale ({lang}): {INSTALLED}\n"
        "run `python sync_wiki_skill.py" + ("" if lang == "en" else f" --lang {lang}") + "` to install."
    )
    return 1


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Regenerate the CloudGlyph skill from Wiki/ and install it into .claude/."
    )
    parser.add_argument(
        "--lang",
        choices=("en", "zh"),
        default="en",
        help="instruction language to generate (default: en)",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="compare the installed copy with a fresh assembly; do not install",
    )
    parser.add_argument(
        "--no-parity",
        action="store_true",
        help="skip the check_parity smoke test",
    )
    args = parser.parse_args()

    if not SKILLS.is_dir():
        print(
            f"error: {SKILLS} is missing. The Wiki submodule is not initialized; "
            "run `git submodule update --init Wiki`.",
            file=sys.stderr,
        )
        return 2

    if not args.no_parity:
        code = run(PARITY)
        if code != 0:
            print("parity check failed; fix the skill modules before syncing.", file=sys.stderr)
            return code

    if args.check:
        return check(args.lang)

    code = run(INSTALLER, str(REPO_ROOT), "--lang", args.lang)
    if code != 0:
        return code

    print(f"installed ({args.lang}) into {REPO_ROOT / '.claude'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
