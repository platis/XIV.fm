#!/usr/bin/env python3
"""Generate the public Dalamud custom-repository manifest from a built XIV.fm manifest."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

VERSION_PATTERN = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")
REPOSITORY = "platis/XIV.fm"


def create_entry(source: dict[str, object], version: str, tag: str, changelog: str) -> dict[str, object]:
    if VERSION_PATTERN.fullmatch(version) is None:
        raise ValueError("version must use MAJOR.MINOR.PATCH with numeric components")

    expected_assembly_version = f"{version}.0"
    actual_assembly_version = source.get("AssemblyVersion")
    if actual_assembly_version != expected_assembly_version:
        raise ValueError(
            f"built AssemblyVersion {actual_assembly_version!r} does not match {expected_assembly_version!r}"
        )

    required_fields = (
        "Author",
        "Name",
        "InternalName",
        "Punchline",
        "Description",
        "Tags",
        "ApplicableVersion",
        "DalamudApiLevel",
    )
    missing = [field for field in required_fields if field not in source]
    if missing:
        raise ValueError(f"built manifest is missing fields: {', '.join(missing)}")

    download_url = f"https://github.com/{REPOSITORY}/releases/download/{tag}/latest.zip"
    return {
        "Author": source["Author"],
        "Name": source["Name"],
        "InternalName": source["InternalName"],
        "AssemblyVersion": actual_assembly_version,
        "Punchline": source["Punchline"],
        "Description": source["Description"],
        "Changelog": changelog.strip(),
        "Tags": source["Tags"],
        "ApplicableVersion": source["ApplicableVersion"],
        "DalamudApiLevel": source["DalamudApiLevel"],
        "DownloadLinkInstall": download_url,
        "DownloadLinkUpdate": download_url,
        "RepoUrl": f"https://github.com/{REPOSITORY}",
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--changelog-file", required=True, type=Path)
    parser.add_argument(
        "--source",
        type=Path,
        default=Path("src/XIV.fm.Plugin/bin/Release/XIV.fm/XIV.fm.json"),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("repository/pluginmaster.json"),
    )
    args = parser.parse_args()

    source = json.loads(args.source.read_text(encoding="utf-8-sig"))
    changelog = args.changelog_file.read_text(encoding="utf-8")
    entry = create_entry(source, args.version, args.tag, changelog)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps([entry], ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Updated {args.output} for {args.tag}")


if __name__ == "__main__":
    main()
