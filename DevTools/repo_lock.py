#!/usr/bin/env python3
"""Cross-session lease locks for VeloxDev worktrees and diff copies.

The locks live outside every checkout so independent sessions and their
subagents coordinate on one machine. A lock is a directory created with
mkdir (atomic on Windows and POSIX); the owner record inside carries the
lease. Expired leases can be taken over, so a crashed session cannot wedge
the repository.

Scopes:
    main              primary worktree, parent commits/pushes, version bumps,
                      submodule pointers, copying the baseline
    repo:Easel        Easel submodule working tree and its commits/pushes
    repo:Wiki         Wiki submodule working tree and its commits/pushes
    module:<name>     one module's edits and merge; <name> is the Src/ or
                      memory/modules/ name (Easel, WinForms, Core, ...)
    global            dotnet new install/uninstall, NuGet publish/unlist,
                      and other machine-wide writes

The owner is a session id, not a process id: parent sessions export
VELOXDEV_SESSION_ID before dispatching subagents, and subagents reuse it.
Acquiring a scope already held by the same owner is reentrant.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import socket
import sys
import time
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import quote

DEFAULT_TTL_MINUTES = 20.0
DEFAULT_WAIT_SECONDS = 0.0
OWNER_ENV = "VELOXDEV_SESSION_ID"
ROOT_ENV = "VELOXDEV_LOCK_ROOT"
SCOPE_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9:._-]{0,63}$")
OWNER_FILE = "owner.json"


class LockError(RuntimeError):
    pass


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


def iso(value: datetime) -> str:
    return value.replace(microsecond=0).isoformat()


def parse_iso(value: str) -> datetime:
    return datetime.fromisoformat(value)


def default_root() -> Path:
    configured = os.environ.get(ROOT_ENV)
    if configured:
        return Path(configured)
    if os.name == "nt":
        local = os.environ.get("LOCALAPPDATA")
        base = Path(local) if local else Path.home() / "AppData" / "Local"
        return base / "VeloxDev" / "locks"
    runtime = os.environ.get("XDG_RUNTIME_DIR")
    return (Path(runtime) if runtime else Path.home() / ".cache") / "veloxdev-locks"


def normalize_scope(scope: str) -> str:
    scope = scope.strip()
    if not SCOPE_RE.fullmatch(scope):
        raise LockError(
            f"invalid scope {scope!r}: use 1-64 characters from [A-Za-z0-9:._-], "
            "starting with a letter or digit"
        )
    return scope


def scope_directory(root: Path, scope: str) -> Path:
    # ':' is not a legal Windows filename character; percent-encoding keeps
    # distinct scopes from collapsing onto one directory. The scope itself is
    # recorded verbatim inside owner.json.
    safe = quote(scope, safe="-._")
    return root / safe


def read_record(directory: Path) -> dict | None:
    try:
        return json.loads((directory / OWNER_FILE).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def write_record(directory: Path, record: dict) -> None:
    temporary = directory / f"{OWNER_FILE}.{uuid.uuid4().hex}.tmp"
    temporary.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temporary, directory / OWNER_FILE)


def expires_at(record: dict) -> datetime | None:
    value = record.get("expires_at")
    if not isinstance(value, str):
        return None
    try:
        return parse_iso(value)
    except ValueError:
        return None


def is_expired(record: dict) -> bool:
    expiry = expires_at(record)
    return expiry is None or expiry <= utcnow()


def remove_directory(directory: Path) -> None:
    if directory.is_dir():
        shutil.rmtree(directory, ignore_errors=True)
    else:
        try:
            directory.unlink()
        except FileNotFoundError:
            pass


def steal(directory: Path) -> bool:
    """Atomically move an expired lock aside, then let the caller recreate it."""
    stale = directory.with_name(f"{directory.name}.stale-{uuid.uuid4().hex}")
    try:
        os.replace(directory, stale)
    except FileNotFoundError:
        return True
    except OSError:
        return False
    remove_directory(stale)
    return True


def refresh(directory: Path, record: dict, owner: str, ttl_minutes: float, note: str | None) -> None:
    now = utcnow()
    candidate = now + timedelta(minutes=ttl_minutes)
    current = expires_at(record)
    # A reentrant acquire (often a subagent with the default TTL) must not
    # shorten a lease the parent already set longer.
    expiry = max(current, candidate) if current is not None else candidate
    record.update(
        owner=owner,
        host=socket.gethostname(),
        pid=os.getpid(),
        heartbeat_at=iso(now),
        expires_at=iso(expiry),
    )
    if note is not None:
        record["note"] = note
    write_record(directory, record)


def acquire_one(
    root: Path,
    scope: str,
    owner: str,
    ttl_minutes: float,
    note: str | None,
    wait_seconds: float,
) -> tuple[bool, bool, str | None]:
    """Return (ok, newly_created, conflict_detail)."""
    directory = scope_directory(root, scope)
    deadline = time.monotonic() + wait_seconds
    delay = 0.25
    while True:
        try:
            directory.mkdir(parents=True)
        except FileExistsError:
            record = read_record(directory)
            if record is not None and record.get("owner") == owner:
                refresh(directory, record, owner, ttl_minutes, note)
                return True, False, None
            if record is None:
                try:
                    age = time.time() - directory.stat().st_mtime
                except OSError:
                    age = 0.0
                # A creator writes owner.json immediately after mkdir; only
                # treat an ownerless directory as stale after a short grace.
                if age >= 5.0 and steal(directory):
                    continue
            elif is_expired(record) and steal(directory):
                continue
            else:
                detail = describe_holder(record)
                if time.monotonic() >= deadline:
                    return False, False, detail
                time.sleep(delay)
                delay = min(2.0, delay * 1.5)
                continue

            detail = describe_holder(record)
            if time.monotonic() >= deadline:
                return False, False, detail
            time.sleep(delay)
            delay = min(2.0, delay * 1.5)
            continue

        record = {
            "scope": scope,
            "owner": owner,
            "host": socket.gethostname(),
            "pid": os.getpid(),
            "acquired_at": iso(utcnow()),
            "heartbeat_at": iso(utcnow()),
            "expires_at": iso(utcnow() + timedelta(minutes=ttl_minutes)),
            "note": note or "",
        }
        try:
            write_record(directory, record)
        except OSError:
            remove_directory(directory)
            raise
        return True, True, None


def describe_holder(record: dict | None) -> str:
    if record is None:
        return "owner record not written yet"
    owner = record.get("owner", "?")
    expiry = record.get("expires_at", "?")
    note = record.get("note", "")
    suffix = f" ({note})" if note else ""
    return f"held by {owner}, expires {expiry}{suffix}"


def release_one(root: Path, scope: str, owner: str, force: bool) -> None:
    directory = scope_directory(root, scope)
    record = read_record(directory)
    if record is None:
        if directory.exists() and not force:
            raise LockError(f"{scope}: no owner record; use --force only after confirming the holder crashed")
        remove_directory(directory)
        return
    if record.get("owner") != owner and not force:
        raise LockError(
            f"{scope}: held by {record.get('owner', '?')}; do not release another session's lock"
        )
    remove_directory(directory)


def renew_one(root: Path, scope: str, owner: str, ttl_minutes: float) -> datetime:
    directory = scope_directory(root, scope)
    record = read_record(directory)
    if record is None:
        raise LockError(f"{scope}: not held")
    if record.get("owner") != owner:
        raise LockError(f"{scope}: held by {record.get('owner', '?')}")
    refresh(directory, record, owner, ttl_minutes, None)
    return utcnow() + timedelta(minutes=ttl_minutes)


def emit(payload: dict, as_json: bool) -> None:
    if as_json:
        print(json.dumps(payload, ensure_ascii=False, indent=2))
        return
    for key, value in payload.items():
        print(f"{key}: {value}")


def command_acquire(args: argparse.Namespace) -> int:
    if args.ttl <= 0:
        raise LockError("--ttl must be greater than zero")
    if args.wait < 0:
        raise LockError("--wait cannot be negative")
    owner = require_owner(args.owner)
    root = Path(args.root) if args.root else default_root()
    root.mkdir(parents=True, exist_ok=True)
    scopes = sorted({normalize_scope(scope) for scope in args.scope})
    acquired: list[str] = []
    for scope in scopes:
        ok, created, detail = acquire_one(root, scope, owner, args.ttl, args.note, args.wait)
        if not ok:
            for held in acquired:
                release_one(root, held, owner, force=False)
            emit(
                {
                    "acquired": False,
                    "scope": scope,
                    "owner": owner,
                    "detail": detail,
                    "released": ", ".join(acquired),
                },
                args.json,
            )
            return 1
        if created:
            acquired.append(scope)
    expires = iso(utcnow() + timedelta(minutes=args.ttl))
    emit(
        {
            "acquired": True,
            "owner": owner,
            "scopes": ", ".join(scopes),
            "expires_at": expires,
            "ttl_minutes": args.ttl,
        },
        args.json,
    )
    return 0


def command_renew(args: argparse.Namespace) -> int:
    if args.ttl <= 0:
        raise LockError("--ttl must be greater than zero")
    owner = require_owner(args.owner)
    root = Path(args.root) if args.root else default_root()
    scopes = sorted({normalize_scope(scope) for scope in args.scope})
    expires = None
    for scope in scopes:
        expires = renew_one(root, scope, owner, args.ttl)
    emit(
        {
            "renewed": True,
            "owner": owner,
            "scopes": ", ".join(scopes),
            "expires_at": iso(expires),
        },
        args.json,
    )
    return 0


def command_release(args: argparse.Namespace) -> int:
    owner = require_owner(args.owner)
    root = Path(args.root) if args.root else default_root()
    scopes = sorted({normalize_scope(scope) for scope in args.scope})
    # Validate every scope before removing any, so a typo does not half-release.
    for scope in scopes:
        directory = scope_directory(root, scope)
        record = read_record(directory)
        if record is not None and record.get("owner") != owner and not args.force:
            raise LockError(f"{scope}: held by {record.get('owner', '?')}")
    for scope in scopes:
        release_one(root, scope, owner, args.force)
    emit({"released": True, "owner": owner, "scopes": ", ".join(scopes)}, args.json)
    return 0


def command_status(args: argparse.Namespace) -> int:
    root = Path(args.root) if args.root else default_root()
    rows: list[dict] = []
    if root.exists():
        for directory in sorted(root.iterdir()):
            if not directory.is_dir() or ".stale-" in directory.name:
                continue
            record = read_record(directory)
            if record is None:
                rows.append({"scope": directory.name, "owner": None, "remaining_seconds": None, "note": "no owner record"})
                continue
            expiry = expires_at(record)
            remaining = None if expiry is None else max(0, int((expiry - utcnow()).total_seconds()))
            rows.append(
                {
                    "scope": record.get("scope", directory.name),
                    "owner": record.get("owner"),
                    "host": record.get("host"),
                    "expires_at": record.get("expires_at"),
                    "remaining_seconds": remaining,
                    "note": record.get("note", ""),
                }
            )
    if args.json:
        print(json.dumps({"root": str(root), "locks": rows}, ensure_ascii=False, indent=2))
        return 0
    if not rows:
        print(f"no locks under {root}")
        return 0
    for row in rows:
        remaining = row.get("remaining_seconds")
        remaining_text = "expired" if remaining == 0 else f"{remaining}s" if remaining is not None else "?"
        print(
            f"{row['scope']:<24} {row.get('owner') or '?':<24} "
            f"{remaining_text:>8}  {row.get('note', '')}"
        )
    return 0


def require_owner(owner: str | None) -> str:
    value = owner or os.environ.get(OWNER_ENV)
    if not value:
        raise LockError(
            f"missing owner: export {OWNER_ENV} or pass --owner; "
            "parent sessions set it once and subagents reuse the same value"
        )
    return value.strip()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="VeloxDev cross-session lease locks")
    subparsers = parser.add_subparsers(dest="command", required=True)

    def add_common(sub: argparse.ArgumentParser) -> None:
        sub.add_argument("--owner", default=None, help=f"session id (default: ${OWNER_ENV})")
        sub.add_argument("--scope", action="append", required=True, metavar="SCOPE")
        sub.add_argument("--root", default=None, help=f"lock root (default: ${ROOT_ENV} or the platform cache)")
        sub.add_argument("--json", action="store_true")

    acquire = subparsers.add_parser("acquire", help="acquire one or more scopes")
    add_common(acquire)
    acquire.add_argument("--ttl", type=float, default=DEFAULT_TTL_MINUTES, help="lease minutes")
    acquire.add_argument("--wait", type=float, default=DEFAULT_WAIT_SECONDS, help="seconds to wait for a holder")
    acquire.add_argument("--note", default=None, help="short task note for status")
    acquire.set_defaults(handler=command_acquire)

    renew = subparsers.add_parser("renew", help="extend leases held by this owner")
    add_common(renew)
    renew.add_argument("--ttl", type=float, default=DEFAULT_TTL_MINUTES, help="new lease minutes")
    renew.set_defaults(handler=command_renew)

    release = subparsers.add_parser("release", help="release scopes held by this owner")
    add_common(release)
    release.add_argument("--force", action="store_true", help="only after confirming the holder crashed")
    release.set_defaults(handler=command_release)

    status = subparsers.add_parser("status", help="show every lock on this machine")
    status.add_argument("--root", default=None)
    status.add_argument("--json", action="store_true")
    status.set_defaults(handler=command_status)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.handler(args)
    except LockError as error:
        emit({"ok": False, "error": str(error)}, getattr(args, "json", False))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
