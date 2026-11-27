#!/usr/bin/env python3
"""
verify_memory_commits.py

Gate memory edits before a push. Check every unpushed commit that touches
memory/modules/ (and note changes under memory/specifications/):

  - required files per module: architecture.md and extension.md;
  - adapters/ carries the seven platform files;
  - no module index files, date-named files or non-markdown files;
  - density errors: explicit history, mechanical counts, CI artifacts;
  - anchors: backticked repo paths resolve uniquely at the branch tip;
  - duplicate headings and duplicated added sentences across memory files.

Judgment calls from memory-density-specifications.md stay manual: the script
prints a review checklist and flags causal, rhetorical, future and
maintenance-advice phrasing for the agent to confirm.

Every finding names the unpushed commit that introduced it. Fix the text,
fold the fix into that commit (amend at the tip; otherwise fixup + autosquash),
and rerun until the gate passes. Do not fix memory in a separate commit.

Rules live in memory/specifications/memory-maintenance-specifications.md §七
and memory/specifications/memory-density-specifications.md §一.

Usage:
    python DevTools/verify_memory_commits.py
    python DevTools/verify_memory_commits.py --repo SubRepos/Easel
    python DevTools/verify_memory_commits.py --branch master
    python DevTools/verify_memory_commits.py --base origin/master

--repo checks another worktree (a submodule) instead of the current one; the
branch defaults to that repository's current branch. Run verify_pushes.py to
check the parent and every submodule in one command.

Exit codes:
    0  no mechanical errors (review items may still need judgment)
    1  mechanical errors
    2  environment or usage error
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT: Path | None = None

MODULES_PREFIX = "memory/modules/"
SPECS_PREFIX = "memory/specifications/"
PLATFORMS = ("wpf", "avalonia", "winui", "maui", "winforms", "razor", "jalium")
KNOWN_ROOTS = ("Src/", "Examples/", "memory/", "skills/", "SubRepos/", "Assets/", "Docs/")

ANCHOR_EXT = re.compile(
    r"\.(?:cs|xaml|axaml|razor|csproj|props|targets|md|json|ya?ml|ps1|py|"
    r"slnx|xml|txt|cshtml|jalxaml)$",
    re.I,
)
BACKTICK = re.compile(r"`([^`]+)`")
HEADING = re.compile(r"^(#{1,6})\s+(.+?)\s*#*\s*$")
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")

TEXT_RULES = (
    ("error", "explicit history", re.compile(r"已删除|已经删除|曾经|旧版|旧实现|原先")),
    (
        "error",
        "historical date",
        re.compile(r"(?:自\s*)?20\d\d[-年]\d{1,2}[^。\n]{0,12}(?:起|之后|以后)"),
    ),
    (
        "error",
        "mechanical count",
        re.compile(r"\d+\s*(?:个|条|项|处)\s*(?:测试|用例|文件|接口|模块|平台)"),
    ),
    (
        "error",
        "mechanical count",
        re.compile(r"(?:文件数|类型数|接口数|成员数|测试数|用例数|模块数|平台数)"),
    ),
    ("error", "test pass/fail count", re.compile(r"\d+\s*(?:通过|失败|跳过)")),
    ("error", "CI run artifact", re.compile(r"\bTRX\b|run\s*号|run\s*#?\s*\d+", re.I)),
    (
        "error",
        "test/build duration",
        re.compile(r"(?:测试|构建|整套|全量)[^。\n]{0,12}耗时\s*[:：]?\s*\d+"),
    ),
    ("review", "historical phrasing", re.compile(r"以前|不再|原来")),
    ("review", "causal explanation", re.compile(r"因为|所以|否则|原因是|这是由于")),
    (
        "review",
        "rhetorical emphasis",
        re.compile(r"值得单独记住|最重要的|不是抄来的|真正"),
    ),
    (
        "review",
        "future speculation",
        re.compile(r"将来|未来|下次|可能会|也许会|预计"),
    ),
)
ADVICE_RULE = re.compile(r"别忘了|务必|记得|要保持一致|优化时先看")
FIXUP_SUBJECT = re.compile(r"^(?:fixup!|squash!|amend!)")


class GitError(RuntimeError):
    """A git command failed when success was required."""


@dataclass
class AddedLine:
    path: str
    line: int
    text: str


@dataclass
class MemoryCommit:
    sha: str
    subject: str
    parents: list[str]
    files: list[str]
    added: list[AddedLine]

    @property
    def short(self) -> str:
        return self.sha[:8]


@dataclass
class Finding:
    sha: str
    subject: str
    path: str | None
    line: int | None
    severity: str
    message: str


def configure_stdio() -> None:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            try:
                stream.reconfigure(errors="replace")
            except Exception:
                pass


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


def format_base_label(base_name: str, base_sha: str) -> str:
    if re.fullmatch(r"[0-9a-f]{40}", base_name):
        return base_name[:8]
    return f"{base_name} ({base_sha[:8]})"


def parse_added_lines(diff: str) -> list[AddedLine]:
    added: list[AddedLine] = []
    path: str | None = None
    new_line = 0

    for raw in diff.splitlines():
        if raw.startswith("diff --git "):
            path = None
            continue
        if raw.startswith("+++ "):
            candidate = raw[4:].strip()
            if candidate.startswith("b/"):
                candidate = candidate[2:]
            path = None if candidate == "/dev/null" else candidate
            continue
        if raw.startswith("@@ "):
            match = HUNK.match(raw)
            if match:
                new_line = int(match.group(1))
            continue
        if path is None or not raw:
            continue
        if raw.startswith("+") and not raw.startswith("+++"):
            added.append(AddedLine(path, new_line, raw[1:]))
            new_line += 1
        elif raw.startswith("-") and not raw.startswith("---"):
            continue
        elif raw.startswith("\\"):
            continue
        else:
            new_line += 1

    return added


def commit_parents(sha: str) -> list[str]:
    return run_git("rev-list", "--parents", "-n", "1", sha).stdout.split()[1:]


def commit_files(sha: str, parents: list[str]) -> list[str]:
    if parents:
        output = run_git(
            "diff", "-M", "--name-only", parents[0], sha, "--", "memory"
        ).stdout
    else:
        output = run_git("ls-tree", "-r", "--name-only", sha, "--", "memory").stdout
    return [line for line in output.splitlines() if line]


def commit_added(sha: str, parents: list[str]) -> list[AddedLine]:
    if parents:
        patch = run_git(
            "diff", "-M", "--no-color", "--no-ext-diff", "-U0",
            parents[0], sha, "--", "memory",
        ).stdout
    else:
        patch = run_git(
            "show", "--format=", "--no-color", "--no-ext-diff", "-U0",
            sha, "--", "memory",
        ).stdout
    return parse_added_lines(patch)


def collect_commits(base_sha: str, branch: str) -> tuple[list[MemoryCommit], set[str]]:
    shas = run_git("rev-list", "--reverse", f"{base_sha}..{branch}").stdout.split()
    commits: list[MemoryCommit] = []

    for sha in shas:
        parents = commit_parents(sha)
        files = commit_files(sha, parents)
        if not files:
            continue
        subject = run_git("show", "-s", "--format=%s", sha).stdout.rstrip("\n")
        commits.append(
            MemoryCommit(
                sha=sha,
                subject=subject,
                parents=parents,
                files=files,
                added=commit_added(sha, parents),
            )
        )

    return commits, set(shas)


def tip_files(branch: str) -> set[str]:
    output = run_git("ls-tree", "-r", "--name-only", branch).stdout
    return {line for line in output.splitlines() if line}


def submodule_paths() -> set[str]:
    result = run_git(
        "config", "--file", ".gitmodules",
        "--get-regexp", r"^submodule\..*\.path$",
        check=False,
    )
    paths: set[str] = set()
    for line in result.stdout.splitlines():
        _, _, value = line.partition(" ")
        value = value.strip().replace("\\", "/").strip("/")
        if value:
            paths.add(value)
    return paths


def show_at(ref: str, path: str) -> str | None:
    result = run_git("show", f"{ref}:{path}", check=False)
    return result.stdout if result.returncode == 0 else None


def fenced_line_numbers(content: str) -> set[int]:
    fenced: set[int] = set()
    inside = False
    for number, line in enumerate(content.splitlines(), 1):
        stripped = line.lstrip()
        if stripped.startswith("```") or stripped.startswith("~~~"):
            inside = not inside
            fenced.add(number)
        elif inside:
            fenced.add(number)
    return fenced


def heading_lines(content: str) -> dict[str, list[int]]:
    result: dict[str, list[int]] = {}
    for number, line in enumerate(content.splitlines(), 1):
        match = HEADING.match(line)
        if match:
            result.setdefault(match.group(2).strip(), []).append(number)
    return result


def normalize_anchor(token: str) -> str | None:
    token = token.strip().replace("\\", "/")
    if token.startswith("./"):
        token = token[2:]
    token = re.sub(r":\d+(?:-\d+)?$", "", token).rstrip("/")
    if not token or "/" not in token:
        return None
    if any(char in token for char in "<>{}*?|$%"):
        return None
    if "..." in token or "�" in token:
        return None
    if any(char.isspace() for char in token):
        return None
    if token.startswith(("http://", "https://", "www.", "#")):
        return None
    if re.match(r"^[A-Za-z]:/", token):
        return None
    return token


def anchor_status(
    token: str,
    memory_path: str,
    all_tip_files: set[str],
    submodules: set[str],
) -> tuple[str, str] | None:
    """Return (status, detail) for a path-like anchor, or None to skip it."""
    assert REPO_ROOT is not None

    has_extension = bool(ANCHOR_EXT.search(token))
    memory_dir = Path(os.path.normpath(str(Path(memory_path).parent / token)))
    root_dir = Path(os.path.normpath(token))

    for candidate in (memory_dir, root_dir):
        candidate_text = candidate.as_posix()
        if candidate_text in all_tip_files:
            return "ok", ""
        if any(path.startswith(candidate_text + "/") for path in all_tip_files):
            return "ok", ""
        if any(
            candidate_text == sub or candidate_text.startswith(sub + "/")
            for sub in submodules
        ) and (REPO_ROOT / candidate).exists():
            return "ok", ""

    if not has_extension:
        return None

    matches = [
        path
        for path in all_tip_files
        if path == token or path.endswith("/" + token)
    ]
    if len(matches) == 1:
        return "ok", ""
    if len(matches) > 1:
        return "ambiguous", f"anchor matches {len(matches)} files: {token}"
    if token.startswith(KNOWN_ROOTS):
        return "missing", f"anchor does not resolve: {token}"
    return "missing", f"anchor does not resolve (relative path?): {token}"


def added_line_findings(
    commit: MemoryCommit,
    line: AddedLine,
    content_cache: dict[tuple[str, str], str | None],
    fenced_cache: dict[tuple[str, str], set[int]],
    headings_cache: dict[tuple[str, str], dict[str, list[int]]],
    all_tip_files: set[str],
    submodules: set[str],
) -> list[Finding]:
    findings: list[Finding] = []
    if not line.path.startswith(MODULES_PREFIX):
        return findings

    key = (commit.sha, line.path)
    if key not in content_cache:
        content_cache[key] = show_at(commit.sha, line.path)
    content = content_cache[key]
    if content is None:
        return findings

    if key not in fenced_cache:
        fenced_cache[key] = fenced_line_numbers(content)
    if line.line in fenced_cache[key]:
        return findings

    for severity, label, rule in TEXT_RULES:
        match = rule.search(line.text)
        if match:
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    line.path,
                    line.line,
                    severity,
                    f'{label}: "{match.group(0)}"',
                )
            )

    if line.path.endswith("/architecture.md") or line.path.endswith("/pitfalls.md"):
        match = ADVICE_RULE.search(line.text)
        if match:
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    line.path,
                    line.line,
                    "review",
                    f'maintenance advice outside extension.md: "{match.group(0)}"',
                )
            )

    match = HEADING.match(line.text)
    if match:
        if key not in headings_cache:
            headings_cache[key] = heading_lines(content)
        heading = match.group(2).strip()
        numbers = headings_cache[key].get(heading, [])
        if len(numbers) > 1:
            other = next((number for number in numbers if number != line.line), None)
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    line.path,
                    line.line,
                    "error",
                    f'duplicate heading "{heading}"'
                    + (f" (also at line {other})" if other else ""),
                )
            )

    for match in BACKTICK.finditer(line.text):
        token = normalize_anchor(match.group(1))
        if token is None:
            continue
        status = anchor_status(token, line.path, all_tip_files, submodules)
        if status is None:
            continue
        state, detail = status
        if state == "missing":
            severity = "error" if token.startswith(KNOWN_ROOTS) else "review"
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    line.path,
                    line.line,
                    severity,
                    detail,
                )
            )
        elif state == "ambiguous":
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    line.path,
                    line.line,
                    "review",
                    detail,
                )
            )

    return findings


def structural_findings(
    commits: list[MemoryCommit], all_tip_files: set[str]
) -> list[Finding]:
    findings: list[Finding] = []
    module_commits: dict[str, list[MemoryCommit]] = {}

    for commit in commits:
        for path in commit.files:
            if not path.startswith(MODULES_PREFIX):
                continue
            module = path[len(MODULES_PREFIX):].split("/", 1)[0]
            module_commits.setdefault(module, []).append(commit)

    for module, touched in sorted(module_commits.items()):
        prefix = f"{MODULES_PREFIX}{module}/"
        module_files = [path for path in all_tip_files if path.startswith(prefix)]
        oldest = touched[0]

        if not module_files:
            findings.append(
                Finding(
                    oldest.sha,
                    oldest.subject,
                    f"{prefix}architecture.md",
                    None,
                    "review",
                    "module memory is empty or was deleted; confirm the module is gone",
                )
            )
            continue

        for required in ("architecture.md", "extension.md"):
            path = f"{prefix}{required}"
            if path not in module_files:
                findings.append(
                    Finding(
                        oldest.sha,
                        oldest.subject,
                        path,
                        None,
                        "error",
                        f"missing required file {path}",
                    )
                )

        adapter_prefix = f"{prefix}adapters/"
        adapter_files = [
            path for path in module_files if path.startswith(adapter_prefix)
        ]
        if adapter_files:
            for platform in PLATFORMS:
                path = f"{adapter_prefix}{platform}.md"
                if path not in module_files:
                    findings.append(
                        Finding(
                            oldest.sha,
                            oldest.subject,
                            path,
                            None,
                            "error",
                            f"missing adapter memory {path}",
                        )
                    )
            expected = {f"{adapter_prefix}{platform}.md" for platform in PLATFORMS}
            extras = sorted(set(adapter_files) - expected)
            for path in extras:
                findings.append(
                    Finding(
                        oldest.sha,
                        oldest.subject,
                        path,
                        None,
                        "review",
                        "adapter memory outside the seven platform files",
                    )
                )

        for path in module_files:
            relative = path[len(prefix):]
            if "/" in relative:
                continue
            lowered = relative.lower()
            if lowered in ("index.md", "readme.md", "_index.md"):
                findings.append(
                    Finding(
                        oldest.sha,
                        oldest.subject,
                        path,
                        None,
                        "error",
                        "module index files are forbidden",
                    )
                )
            if re.match(r"\d{4}-\d{2}-\d{2}", relative):
                findings.append(
                    Finding(
                        oldest.sha,
                        oldest.subject,
                        path,
                        None,
                        "error",
                        "date-named memory file; name files by topic",
                    )
                )

        for path in module_files:
            if not path.endswith(".md"):
                findings.append(
                    Finding(
                        oldest.sha,
                        oldest.subject,
                        path,
                        None,
                        "review",
                        "non-markdown file under memory/modules",
                    )
                )

    return findings


def spec_findings(commits: list[MemoryCommit]) -> list[Finding]:
    findings: list[Finding] = []
    for commit in commits:
        for path in commit.files:
            if path.startswith(SPECS_PREFIX):
                findings.append(
                    Finding(
                        commit.sha,
                        commit.subject,
                        path,
                        None,
                        "review",
                        "specification changed; confirm the user dictated "
                        "this and the changed text follows the density spec",
                    )
                )
    return findings


def multi_commit_findings(commits: list[MemoryCommit]) -> list[Finding]:
    findings: list[Finding] = []
    file_commits: dict[str, list[MemoryCommit]] = {}

    for commit in commits:
        for path in commit.files:
            file_commits.setdefault(path, []).append(commit)

    for path, touched in sorted(file_commits.items()):
        if len(touched) < 2:
            continue
        latest = touched[-1]
        findings.append(
            Finding(
                latest.sha,
                latest.subject,
                path,
                None,
                "review",
                f"touched by {len(touched)} unpushed commits; if a later "
                "commit only fixes an earlier one, fold it in with "
                "--fixup/--autosquash",
            )
        )

    return findings


def duplicate_sentence_findings(
    commits: list[MemoryCommit],
    content_cache: dict[tuple[str, str], str | None],
    fenced_cache: dict[tuple[str, str], set[int]],
) -> list[Finding]:
    occurrences: dict[str, list[tuple[MemoryCommit, AddedLine]]] = {}

    for commit in commits:
        for line in commit.added:
            if not line.path.startswith(MODULES_PREFIX):
                continue
            key = (commit.sha, line.path)
            if key not in content_cache:
                content_cache[key] = show_at(commit.sha, line.path)
            content = content_cache[key]
            if content is None:
                continue
            if key not in fenced_cache:
                fenced_cache[key] = fenced_line_numbers(content)
            if line.line in fenced_cache[key]:
                continue

            normalized = re.sub(r"\s+", " ", line.text.strip())
            normalized = re.sub(r"^(?:[-*+]\s+|\d+\.\s+)", "", normalized)
            if len(normalized) < 40:
                continue
            if normalized.startswith(("#", "|", ">")):
                continue
            occurrences.setdefault(normalized, []).append((commit, line))

    findings: list[Finding] = []
    for normalized, entries in occurrences.items():
        paths = {entry[1].path for entry in entries}
        if len(paths) < 2:
            continue
        for commit, line in entries:
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    line.path,
                    line.line,
                    "review",
                    "same sentence added to multiple memory files: "
                    + ", ".join(sorted(paths)),
                )
            )

    return findings


def print_report(
    base_label: str,
    branch: str,
    commits: list[MemoryCommit],
    findings: list[Finding],
) -> None:
    print(f"repo   {REPO_ROOT}")
    print(f"base   {base_label}")
    print(f"branch {branch}")
    print(
        f"range  {len(commits)} unpushed commit(s) touch memory"
        if commits
        else "range  no unpushed commits touch memory"
    )

    if commits:
        print()
        for commit in commits:
            print(f"{commit.short}  {commit.subject}")
            related = [
                finding
                for finding in findings
                if finding.sha == commit.sha
            ]
            for finding in related:
                location = finding.path or "(commit)"
                if finding.line is not None:
                    location = f"{location}:{finding.line}"
                print(
                    f"  [{finding.severity.upper():6}] {location}  "
                    f"{finding.message}"
                )
            if not related:
                print("  [ OK   ] mechanical checks")
            print()

    print("Manual review (density spec §一; maintenance spec §五-§七):")
    print("  - every changed sentence must help the next agent act correctly;")
    print("  - change the conflicting sentence instead of appending a contradiction;")
    print("  - anchors are repo paths plus a symbol or quote; line numbers alone")
    print("    are not evidence;")
    print("  - keep behavior numbers; remove mechanical counts;")
    print("  - do not repeat the same fact across architecture.md, extension.md,")
    print("    pitfalls.md or adapters/;")
    print("  - verify each changed assertion against the code at the current tip.")
    print()

    errors = sum(1 for finding in findings if finding.severity == "error")
    reviews = len(findings) - errors
    if errors:
        print(f"verdict: ILLEGAL ({errors} error(s), {reviews} review item(s))")
    elif reviews:
        print(f"verdict: REVIEW ({reviews} review item(s); no mechanical errors)")
    else:
        print("verdict: OK (mechanical checks)")

    if findings:
        blamed = sorted({finding.sha for finding in findings})
        print()
        print("fix: edit the memory text, then fold the fix into the commit")
        print("     that introduced it; do not add a separate fix commit.")
        print("     tip commit:   git add <file> && git commit --amend --no-edit")
        print("     older commit: git add -p <file> && git commit --fixup=<sha>")
        print("                   git rebase -i --autosquash <base>")
        print("     if the fix leaves a commit empty, drop it with")
        print("     `git rebase --skip` when the rebase stops.")
        print("     commits to fold:")
        for sha in blamed:
            subject = next(
                finding.subject for finding in findings if finding.sha == sha
            )
            print(f"       {sha[:8]}  {subject}")
        print("     rerun this check after the rebase; push only when it exits 0")
        print("     and the manual review above is complete.")


def main() -> int:
    global REPO_ROOT

    configure_stdio()

    parser = argparse.ArgumentParser(
        description="Check unpushed memory edits against the memory specifications."
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

    commits, _ = collect_commits(base_sha, args.branch)
    if not commits:
        print(f"no unpushed commits modify memory on {args.branch} since {base_label}")
        return 0

    all_tip_files = tip_files(args.branch)
    submodules = submodule_paths()
    content_cache: dict[tuple[str, str], str | None] = {}
    fenced_cache: dict[tuple[str, str], set[int]] = {}
    headings_cache: dict[tuple[str, str], dict[str, list[int]]] = {}

    findings: list[Finding] = []

    for commit in commits:
        if FIXUP_SUBJECT.match(commit.subject):
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    None,
                    None,
                    "error",
                    "unfolded fixup/squash commit; run "
                    "`git rebase -i --autosquash <base>`",
                )
            )
        if len(commit.parents) > 1:
            findings.append(
                Finding(
                    commit.sha,
                    commit.subject,
                    None,
                    None,
                    "review",
                    "merge commit changes memory; put the memory edit on the "
                    "side branch and re-merge",
                )
            )
        for line in commit.added:
            findings.extend(
                added_line_findings(
                    commit,
                    line,
                    content_cache,
                    fenced_cache,
                    headings_cache,
                    all_tip_files,
                    submodules,
                )
            )

    findings.extend(structural_findings(commits, all_tip_files))
    findings.extend(spec_findings(commits))
    findings.extend(multi_commit_findings(commits))
    findings.extend(
        duplicate_sentence_findings(commits, content_cache, fenced_cache)
    )

    order = {commit.sha: index for index, commit in enumerate(commits)}
    findings.sort(
        key=lambda finding: (
            order.get(finding.sha, len(order)),
            finding.path or "",
            finding.line if finding.line is not None else -1,
            0 if finding.severity == "error" else 1,
            finding.message,
        )
    )

    print_report(base_label, args.branch, commits, findings)
    return 1 if any(finding.severity == "error" for finding in findings) else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except GitError as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(2)
