#!/usr/bin/env python3
"""
verify_version_commits.py

Gate the version commit before a push. Check the commits between the branch
upstream (or origin/<branch>) and the local branch tip:

  1. every commit that changes <Version> in a Src/ package csproj must be a
     dedicated [version][repo] commit;
  2. a [version] commit must change <Version> and touch only .csproj files;
  3. the [version] commit must be the newest commit in the unpushed range.

Rules live in memory/specifications/version-management-specifications.md §六.

Usage:
    python DevTools/verify_version_commits.py
    python DevTools/verify_version_commits.py --repo SubRepos/Easel
    python DevTools/verify_version_commits.py --branch master
    python DevTools/verify_version_commits.py --base origin/master
    python DevTools/verify_version_commits.py --fix

--repo checks another worktree (a submodule) instead of the current one; the
branch defaults to that repository's current branch. Run verify_pushes.py to
check the parent and every submodule in one command.

--fix moves one misplaced [version] commit to the tip (rebase --onto, then
cherry-pick). It refuses when the worktree is dirty, the range contains merge
commits, or more than one commit carries a version change. If the move
conflicts, it aborts and restores the branch.

Exit codes:
    0  legal (or fixed)
    1  illegal / fix refused
    2  environment or usage error
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

REPO_ROOT: Path | None = None

VERSION_LINE = re.compile(r"<Version(?:\s[^>]*)?>\s*([^<]*?)\s*</Version>")
VERSION_SUBJECT = re.compile(
    r"^\[version\]\[repo\](?:\[Part \d+/\d+\])?\s+\S"
)


class GitError(RuntimeError):
    """A git command failed when success was required."""


@dataclass
class VersionEdit:
    path: str
    old: str | None
    new: str | None


@dataclass
class CommitInfo:
    sha: str
    subject: str
    parents: list[str]
    files: list[str]
    edits: list[VersionEdit]

    @property
    def keyword(self) -> bool:
        return bool(VERSION_SUBJECT.match(self.subject))

    @property
    def version_topic(self) -> bool:
        return bool(self.edits) or self.subject.startswith("[version]")


@dataclass
class Problem:
    commit: CommitInfo
    kind: str
    message: str


def run_git(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    assert REPO_ROOT is not None
    result = subprocess.run(
        ["git", *args],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if check and result.returncode != 0:
        command = "git " + " ".join(args)
        detail = result.stderr.strip() or result.stdout.strip()
        raise GitError(f"{command} failed: {detail}")
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
        raise GitError("not inside a git worktree")
    return Path(result.stdout.strip())


def rev_parse(ref: str) -> str:
    return run_git("rev-parse", "--verify", f"{ref}^{{commit}}").stdout.strip()


def resolve_base(branch: str, explicit: str | None) -> tuple[str, str]:
    candidates: list[str] = []
    if explicit:
        candidates = [explicit]
    else:
        upstream = run_git(
            "rev-parse",
            "--abbrev-ref",
            "--symbolic-full-name",
            f"{branch}@{{upstream}}",
            check=False,
        )
        upstream_name = upstream.stdout.strip()
        if upstream.returncode == 0 and upstream_name and upstream_name != branch:
            candidates.append(upstream_name)
        candidates.append(f"origin/{branch}")

    for candidate in candidates:
        result = run_git("rev-parse", "--verify", f"{candidate}^{{commit}}", check=False)
        if result.returncode == 0:
            return candidate, result.stdout.strip()

    raise GitError(
        f"cannot find an unpushed baseline for {branch}; pass --base <ref> "
        f"(tried: {', '.join(candidates)})"
    )


def is_package_csproj(path: str) -> bool:
    normalized = path.replace("\\", "/")
    return (
        normalized.startswith("Src/")
        and not normalized.startswith("Src/Test/")
        and normalized.endswith(".csproj")
    )


def parse_version_edits(patch: str) -> list[VersionEdit]:
    edits: list[VersionEdit] = []
    path: str | None = None
    olds: list[str] = []
    news: list[str] = []

    def flush() -> None:
        nonlocal olds, news
        if path is not None and is_package_csproj(path):
            for index in range(max(len(olds), len(news))):
                edits.append(
                    VersionEdit(
                        path=path,
                        old=olds[index] if index < len(olds) else None,
                        new=news[index] if index < len(news) else None,
                    )
                )
        olds, news = [], []

    for line in patch.splitlines():
        if line.startswith("+++ "):
            flush()
            candidate = line[4:].strip()
            if candidate.startswith("b/"):
                candidate = candidate[2:]
            path = None if candidate == "/dev/null" else candidate
            continue
        if path is None or not line or line[0] not in "+-":
            continue
        if line.startswith(("+++", "---")):
            continue
        match = VERSION_LINE.fullmatch(line[1:].strip())
        if match:
            (olds if line[0] == "-" else news).append(match.group(1).strip())
    flush()
    return edits


def read_commit(sha: str) -> CommitInfo:
    subject = run_git("show", "-s", "--format=%s", sha).stdout.rstrip("\n")
    parents = run_git("rev-list", "--parents", "-n", "1", sha).stdout.split()[1:]

    if parents:
        parent = parents[0]
        patch = run_git(
            "diff", "--no-color", "--no-ext-diff", "-U0",
            parent, sha, "--", "*.csproj",
        ).stdout
        files = run_git("diff", "--name-only", parent, sha).stdout.splitlines()
    else:
        patch = run_git(
            "show", "--format=", "--no-color", "--no-ext-diff", "-U0",
            sha, "--", "*.csproj",
        ).stdout
        files = run_git("ls-tree", "-r", "--name-only", sha).stdout.splitlines()

    return CommitInfo(
        sha=sha,
        subject=subject,
        parents=parents,
        files=files,
        edits=parse_version_edits(patch),
    )


def analyze(branch: str, base_sha: str) -> tuple[list[CommitInfo], list[Problem]]:
    branch_sha = rev_parse(f"refs/heads/{branch}")
    shas = run_git("rev-list", "--reverse", f"{base_sha}..{branch_sha}").stdout.split()
    commits = [read_commit(sha) for sha in shas]
    problems: list[Problem] = []
    tip = shas[-1] if shas else None

    for index, commit in enumerate(commits):
        if commit.edits and not commit.keyword:
            message = (
                "first line must be `[version][repo] English subject`; "
                "no space inside or between the brackets"
                if commit.subject.startswith("[version]")
                else "changes <Version> without the [version] type; split the "
                "version bump into its own [version][repo] commit"
            )
            problems.append(Problem(commit, "missing-keyword", message))
        if commit.subject.startswith("[version]") and not commit.edits:
            problems.append(
                Problem(
                    commit,
                    "keyword-without-version",
                    "uses [version] but changes no <Version>; use the type that "
                    "matches the change, or include the version bump",
                )
            )
        if commit.subject.startswith("[version]"):
            stray = [path for path in commit.files if not path.endswith(".csproj")]
            if stray:
                problems.append(
                    Problem(
                        commit,
                        "mixed-files",
                        "version commit touches non-csproj files: "
                        + ", ".join(stray)
                        + "; split them out",
                    )
                )
        if commit.version_topic and commit.sha != tip:
            problems.append(
                Problem(
                    commit,
                    "not-latest",
                    f"the version change is at position {index + 1} of "
                    f"{len(commits)}; it must be the newest unpushed commit "
                    f"({tip[:8]})",
                )
            )

    return commits, problems


def format_base_label(base_name: str, base_sha: str) -> str:
    if re.fullmatch(r"[0-9a-f]{40}", base_name):
        return base_name[:8]
    return f"{base_name} ({base_sha[:8]})"


def describe_edit(edit: VersionEdit) -> str:
    if edit.old and edit.new:
        return f"{edit.path}: {edit.old} -> {edit.new}"
    if edit.new:
        return f"{edit.path}: +{edit.new}"
    return f"{edit.path}: -{edit.old}"


def print_report(
    base_label: str, branch: str, commits: list[CommitInfo], problems: list[Problem]
) -> None:
    print(f"repo   {REPO_ROOT}")
    print(f"base   {base_label}")
    print(f"branch {branch}")
    print(f"range  {len(commits)} unpushed commit(s)")
    print()

    for index, commit in enumerate(commits, 1):
        related = [problem for problem in problems if problem.commit is commit]
        status = "ILLEGAL" if related else "ok"
        print(f"{index:>3}. {commit.sha[:8]}  {commit.subject}  [{status}]")
        for edit in commit.edits:
            print(f"       version  {describe_edit(edit)}")
        for problem in related:
            print(f"       x {problem.message}")

    print()
    if problems:
        print(f"verdict: ILLEGAL ({len(problems)} problem(s))")
        print("fix: correct the problems above, or run --fix for a single "
              "misplaced [version] commit.")
    else:
        print("verdict: OK")


def check_fix_preconditions(
    branch: str, commits: list[CommitInfo]
) -> tuple[CommitInfo | None, str]:
    version_commits = [commit for commit in commits if commit.version_topic]
    if not version_commits:
        return None, "nothing to fix: no version commit in the unpushed range"
    if len(version_commits) > 1:
        return None, (
            "cannot auto-fix: more than one commit carries a version change; "
            "squash or reorder them manually with `git rebase -i`"
        )

    commit = version_commits[0]
    if not commit.edits:
        return None, "cannot auto-fix: the [version] commit changes no <Version>"
    if not commit.keyword:
        return None, (
            "cannot auto-fix: the version change lacks the "
            "[version][repo] type; split it into its own commit first"
        )
    if any(not path.endswith(".csproj") for path in commit.files):
        return None, (
            "cannot auto-fix: the version commit mixes non-csproj files; "
            "split those files out first"
        )
    if commit.sha == commits[-1].sha:
        return None, "nothing to fix: the version commit is already the newest commit"
    if not commit.parents:
        return None, "cannot auto-fix: the version commit has no parent"

    merges = [item for item in commits if len(item.parents) > 1]
    if merges:
        return None, (
            "cannot auto-fix: the unpushed range contains merge commits; "
            "reorder with `git rebase -i --rebase-merges`"
        )

    current = run_git("symbolic-ref", "--short", "HEAD", check=False)
    if current.returncode != 0 or current.stdout.strip() != branch:
        return None, f"cannot auto-fix: check out {branch} first"

    dirty = run_git(
        "status", "--porcelain", "--untracked-files=no", "--ignore-submodules=dirty"
    ).stdout.strip()
    if dirty:
        return None, (
            "cannot auto-fix: the worktree has uncommitted changes; "
            "commit or stash them first"
        )

    git_dir = Path(run_git("rev-parse", "--absolute-git-dir").stdout.strip())
    for marker in (
        "rebase-merge",
        "rebase-apply",
        "MERGE_HEAD",
        "CHERRY_PICK_HEAD",
        "REVERT_HEAD",
    ):
        if (git_dir / marker).exists():
            return None, (
                f"cannot auto-fix: {marker} is present; finish the "
                "in-progress operation first"
            )

    return commit, ""


def apply_fix(branch: str, base_name: str) -> int:
    base_sha = rev_parse(base_name)
    base_label = format_base_label(base_name, base_sha)
    base_arg = base_name[:8] if re.fullmatch(r"[0-9a-f]{40}", base_name) else base_name
    commits, problems = analyze(branch, base_sha)
    if not problems:
        print("nothing to fix: all unpushed commits are legal")
        return 0

    commit, reason = check_fix_preconditions(branch, commits)
    if commit is None:
        print(reason, file=sys.stderr)
        return 1

    original = rev_parse(f"refs/heads/{branch}")
    backup_ref = (
        "refs/backup/verify-version-commits/"
        f"{datetime.now():%Y%m%d-%H%M%S}"
    )
    run_git("update-ref", backup_ref, original)

    rebase = run_git(
        "rebase", "--empty=keep", "--onto", commit.parents[0], commit.sha,
        branch, check=False,
    )
    if rebase.returncode != 0:
        run_git("rebase", "--abort", check=False)
        run_git("reset", "--hard", original, check=False)
        print(
            f"rebase failed while dropping {commit.sha[:8]}; "
            f"the branch is back at {original[:8]}",
            file=sys.stderr,
        )
        print(
            f"resolve manually: `git rebase -i {base_arg}`, move the "
            "[version] commit to the tip, then rerun the check",
            file=sys.stderr,
        )
        print(f"backup: {backup_ref}", file=sys.stderr)
        return 1

    cherry_pick = run_git("cherry-pick", commit.sha, check=False)
    if cherry_pick.returncode != 0:
        run_git("cherry-pick", "--abort", check=False)
        run_git("reset", "--hard", original, check=False)
        print(
            f"cherry-pick failed while reapplying {commit.sha[:8]}; "
            f"the branch was restored to {original[:8]}",
            file=sys.stderr,
        )
        print(
            f"resolve manually: `git rebase -i {base_arg}`, move the "
            "[version] commit to the tip, then rerun the check",
            file=sys.stderr,
        )
        print(f"backup: {backup_ref}", file=sys.stderr)
        return 1

    print(f"moved {commit.sha[:8]} to the tip of {branch}")
    print(f"backup: {backup_ref}  (delete with `git update-ref -d {backup_ref}`)")
    print()

    commits, problems = analyze(branch, base_sha)
    print_report(base_label, branch, commits, problems)
    return 0 if not problems else 1


def main() -> int:
    global REPO_ROOT

    parser = argparse.ArgumentParser(
        description="Check the unpushed commits for the version-commit rules."
    )
    parser.add_argument(
        "--repo",
        default=None,
        help="repository path to check (default: the current worktree)",
    )
    parser.add_argument(
        "--branch",
        default=None,
        help="branch whose unpushed commits are checked (default: the current branch)",
    )
    parser.add_argument(
        "--base",
        default=None,
        help="unpushed baseline (default: branch upstream, then origin/<branch>)",
    )
    parser.add_argument(
        "--fix",
        action="store_true",
        help="move a single misplaced [version] commit to the tip",
    )
    args = parser.parse_args()

    REPO_ROOT = discover_root() if args.repo is None else Path(args.repo).resolve()
    if not (REPO_ROOT / ".git").exists():
        print(f"error: {REPO_ROOT} is not a git worktree", file=sys.stderr)
        return 2

    if args.branch is None:
        current = run_git("symbolic-ref", "--short", "HEAD", check=False)
        branch = current.stdout.strip()
        if current.returncode != 0 or not branch:
            print(
                f"error: {REPO_ROOT} is not on a named branch; pass --branch",
                file=sys.stderr,
            )
            return 2
        args.branch = branch

    exists = run_git(
        "rev-parse", "--verify", f"refs/heads/{args.branch}", check=False
    )
    if exists.returncode != 0:
        print(f"error: branch {args.branch} does not exist", file=sys.stderr)
        return 2

    base_name, base_sha = resolve_base(args.branch, args.base)
    base_label = format_base_label(base_name, base_sha)

    current = run_git("symbolic-ref", "--short", "HEAD", check=False)
    current_name = current.stdout.strip()
    if current.returncode == 0 and current_name and current_name != args.branch:
        print(
            f"note: checking {args.branch}, not the current branch ({current_name})",
            file=sys.stderr,
        )

    if args.fix:
        return apply_fix(args.branch, base_name)

    commits, problems = analyze(args.branch, base_sha)
    if not commits:
        print(f"no unpushed commits on {args.branch} since {base_label}")
        return 0

    print_report(base_label, args.branch, commits, problems)
    return 0 if not problems else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except GitError as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(2)
