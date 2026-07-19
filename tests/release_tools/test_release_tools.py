from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


def load_script(name: str):
    path = REPOSITORY_ROOT / "scripts" / name
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


set_version_script = load_script("set-version.py")
pluginmaster_script = load_script("write-pluginmaster.py")


class SetVersionTests(unittest.TestCase):
    def test_updates_exact_version_element(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / "plugin.csproj"
            project.write_text("<Project><Version>0.1.0</Version></Project>\n", encoding="utf-8")

            previous = set_version_script.set_version(project, "0.1.1", require_increase=True)

            self.assertEqual("0.1.0", previous)
            self.assertIn("<Version>0.1.1</Version>", project.read_text(encoding="utf-8"))

    def test_rejects_non_increasing_release(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / "plugin.csproj"
            project.write_text("<Project><Version>1.2.3</Version></Project>\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "must be greater"):
                set_version_script.set_version(project, "1.2.3", require_increase=True)


class PluginMasterTests(unittest.TestCase):
    def test_generates_versioned_public_download_links(self) -> None:
        source = {
            "Author": "platis",
            "Name": "XIV.fm",
            "InternalName": "XIV.fm",
            "AssemblyVersion": "0.1.0.0",
            "Punchline": "Music presence.",
            "Description": "Description.",
            "Tags": ["music"],
            "ApplicableVersion": "any",
            "DalamudApiLevel": 15,
        }

        entry = pluginmaster_script.create_entry(source, "0.1.0", "v0.1.0", "Initial test build.")

        expected = "https://github.com/platis/XIV.fm/releases/download/v0.1.0/latest.zip"
        self.assertEqual(expected, entry["DownloadLinkInstall"])
        self.assertEqual("XIV.fm", entry["InternalName"])
        self.assertEqual("0.1.0.0", entry["AssemblyVersion"])

    def test_rejects_mismatched_built_version(self) -> None:
        with self.assertRaisesRegex(ValueError, "does not match"):
            pluginmaster_script.create_entry(
                {
                    "AssemblyVersion": "0.1.0.0",
                },
                "0.1.1",
                "v0.1.1",
                "Notes",
            )


if __name__ == "__main__":
    unittest.main()
