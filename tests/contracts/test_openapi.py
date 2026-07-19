import json
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
OPENAPI_PATH = REPOSITORY_ROOT / "docs" / "openapi" / "v1.openapi.json"


class OpenApiContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.document = json.loads(OPENAPI_PATH.read_text(encoding="utf-8"))

    def test_document_declares_openapi_31_and_versioned_routes(self) -> None:
        self.assertEqual("3.1.0", self.document["openapi"])
        self.assertIn("post", self.document["paths"]["/v1/sync"])
        self.assertIn("post", self.document["paths"]["/v1/installations/current/credential"])
        self.assertIn("delete", self.document["paths"]["/v1/installations/current"])

    def test_sync_request_contains_only_approved_presence_fields(self) -> None:
        properties = self.document["components"]["schemas"]["SyncRequest"]["properties"]
        self.assertEqual(
            {
                "pluginVersion",
                "character",
                "location",
                "visibility",
                "knownSnapshotVersion",
            },
            set(properties),
        )

    def test_visibility_wire_values_are_stable(self) -> None:
        mode = self.document["components"]["schemas"]["VisibilitySelection"]["properties"]["mode"]
        self.assertEqual(["private", "public", "custom"], mode["enum"])

    def test_location_transmits_no_coordinates(self) -> None:
        properties = self.document["components"]["schemas"]["LocationScope"]["properties"]
        self.assertEqual(
            {"currentWorldId", "territoryId", "mapId", "instanceId"},
            set(properties),
        )


if __name__ == "__main__":
    unittest.main()
