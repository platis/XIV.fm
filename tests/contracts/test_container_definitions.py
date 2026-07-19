import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
COMPOSE = (REPOSITORY_ROOT / "deploy" / "compose.integration.yaml").read_text(encoding="utf-8")
DOCKERFILE = (REPOSITORY_ROOT / "src" / "XIV.fm.Server.Api" / "Dockerfile").read_text(encoding="utf-8")


class ContainerDefinitionTests(unittest.TestCase):
    def test_all_images_are_digest_pinned(self) -> None:
        image_lines = [line.strip() for line in COMPOSE.splitlines() if line.strip().startswith("image:")]
        from_lines = [line.strip() for line in DOCKERFILE.splitlines() if line.startswith("FROM ")]
        self.assertTrue(image_lines)
        self.assertTrue(from_lines)
        for line in image_lines + from_lines:
            self.assertRegex(line, r"@sha256:[0-9a-f]{64}(?:\s|$)")

    def test_only_api_has_loopback_port_publication(self) -> None:
        self.assertEqual(1, len(re.findall(r'^\s+ports:$', COMPOSE, flags=re.MULTILINE)))
        self.assertIn('"127.0.0.1:${XIVFM_TEST_PORT:-15080}:8080"', COMPOSE)

    def test_runtime_is_non_root(self) -> None:
        self.assertIn("USER app", DOCKERFILE)


if __name__ == "__main__":
    unittest.main()
