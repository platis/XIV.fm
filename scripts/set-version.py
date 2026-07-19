#!/usr/bin/env python3
"""Set and validate the numeric XIV.fm plugin version."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

VERSION_PATTERN = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")
PROJECT_VERSION_PATTERN = re.compile(r"<Version>([^<]+)</Version>")


def parse_version(value: str) -> tuple[int, int, int]:
    match = VERSION_PATTERN.fullmatch(value)
    if match is None:
        raise ValueError("version must use MAJOR.MINOR.PATCH with numeric components")
    return tuple(int(component) for component in match.groups())


def set_version(project_path: Path, requested: str, require_increase: bool) -> str:
    requested_tuple = parse_version(requested)
    content = project_path.read_text(encoding="utf-8")
    match = PROJECT_VERSION_PATTERN.search(content)
    if match is None:
        raise ValueError(f"Version element not found in {project_path}")

    current = match.group(1)
    current_tuple = parse_version(current)
    if require_increase and requested_tuple <= current_tuple:
        raise ValueError(f"release version {requested} must be greater than current version {current}")

    updated, replacements = PROJECT_VERSION_PATTERN.subn(f"<Version>{requested}</Version>", content, count=1)
    if replacements != 1:
        raise ValueError(f"expected exactly one Version element in {project_path}")

    project_path.write_text(updated, encoding="utf-8")
    return current


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("version")
    parser.add_argument(
        "--project",
        type=Path,
        default=Path("src/XIV.fm.Plugin/XIV.fm.Plugin.csproj"),
    )
    parser.add_argument("--require-increase", action="store_true")
    args = parser.parse_args()

    previous = set_version(args.project, args.version, args.require_increase)
    print(f"Updated XIV.fm version: {previous} -> {args.version}")


if __name__ == "__main__":
    main()
