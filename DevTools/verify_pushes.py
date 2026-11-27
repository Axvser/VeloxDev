#!/usr/bin/env python3
"""
verify_pushes.py

Run both push gates for the parent repository and every submodule declared in
.gitmodules:

  - verify_version_commits.py  (version-commit rules)
  - verify_memory_commits.py   (memory edits under memory/modules/)

Each repository is checked on its current branch against its own upstream.
Submodules without memory/modules/ simply report "no unpushed commits modify
memory" and pass.

Usage:
    python DevTools/verify_pushes.py
    python DevTools/verify_pushes.py --fix
    python DevTools/verify_pushes.py --skip-memory

--fix is forwarded to the version gate; it can move a misplaced [version]
commit in each repository. When a submodule is rewritten, update the parent
pointer and rerun.

Exit codes:
    0  all repositories pass
    1  at least one gate reports an illegal commit
    2  environment or usage error
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent


def run_git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if check and result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip()
        raise RuntimeError(f"git {' '.join(args)} failed in {repo}: {detail}")
    return result


def discover_root() -> Path:
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=Path.cwd(),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        raise RuntimeError("not inside a git worktree")
    return Path(result.stdout.strip())


def submodule_paths(root: Path) -> list[Path]:
    result = run_git(
        root,
        "config",
        "-f",
        ".gitmodules",
        "--get-regexp",
        r"^submodule\..*\.path$",
        check=False,
    )
    if result.returncode != 0:
        return []

    paths: list[Path] = []
    for line in result.stdout.splitlines():
        parts = line.split(None, 1)
        if len(parts) == 2:
            paths.append(root / parts[1].strip())
    return paths


def current_branch(repo: Path) -> str | None:
    result = run_git(repo, "symbolic-ref", "--short", "HEAD", check=False)
    if result.returncode != 0:
        return None
    branch = result.stdout.strip()
    return branch or None


def head(repo: Path) -> str:
    return run_git(repo, "rev-parse", "HEAD", check=False).stdout.strip()


def run_gate(repo: Path, script: str, branch: str, fix: bool) -> int:
    command = [
        sys.executable,
        str(TOOLS / script),
        "--repo",
        str(repo),
        "--branch",
        branch,
    ]
    if fix and script.startswith("verify_version"):
        command.append("--fix")

    print(f"\n===== {repo} ({branch}) =====")
    return subprocess.run(command).returncode


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Run the version and memory push gates for the parent and every submodule."
    )
    parser.add_argument("--fix", action="store_true", help="move a misplaced [version] commit")
    parser.add_argument("--skip-version", action="store_true", help="skip the version gate")
    parser.add_argument("--skip-memory", action="store_true", help="skip the memory gate")
    args = parser.parse_args()

    root = discover_root()
    repositories = [root, *submodule_paths(root)]
    exit_code = 0
    moved: list[tuple[Path, str, str]] = []

    for repo in repositories:
        if not (repo / ".git").exists():
            print(f"\n===== {repo} =====")
            print("skipped: not a git worktree")
            continue

        branch = current_branch(repo)
        if branch is None:
            print(f"\n===== {repo} =====")
            print("skipped: detached HEAD; check out a branch first")
            exit_code = max(exit_code, 2)
            continue

        before = head(repo)
        if not args.skip_version:
            exit_code = max(exit_code, run_gate(repo, "verify_version_commits.py", branch, args.fix))
        if not args.skip_memory:
            exit_code = max(exit_code, run_gate(repo, "verify_memory_commits.py", branch, False))

        after = head(repo)
        if before and after and before != after:
            moved.append((repo, before, after))

    if moved:
        print("\nnote: a repository HEAD moved during --fix.")
        for repo, before, after in moved:
            print(f"  {repo}: {before[:8]} -> {after[:8]}  (update the parent pointer, then rerun)")

    print(f"\nverdict: {'OK' if exit_code == 0 else 'FAIL'}")
    return exit_code


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(2)
