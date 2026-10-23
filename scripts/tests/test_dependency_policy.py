"""Guard the SDK line and package ownership declared by this repository."""
import json
import pathlib
import unittest
import xml.etree.ElementTree as xml


ROOT = pathlib.Path(__file__).resolve().parents[2]


class DependencyPolicyTests(unittest.TestCase):
    def test_sdk_rolls_forward_only_within_declared_major_and_minor(self):
        sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]
        self.assertIn(sdk["rollForward"], {"disable", "patch", "latestPatch", "feature", "latestFeature"})
        self.assertFalse(sdk.get("allowPrerelease", True), "release builds must select a stable SDK")

    def test_mathnet_has_one_package_source_and_no_copied_binary(self):
        project = xml.parse(ROOT / "Connect-A-Pic-Core" / "CAP-Core.csproj")
        packages = [item for item in project.iter("PackageReference") if item.get("Include") == "MathNet.Numerics"]
        self.assertEqual(1, len(packages))
        self.assertFalse((ROOT / "Connect-A-Pic-Core" / "MathNet.Numerics.dll").exists())
        copies = [item for item in project.iter() if item.get("Update") == "MathNet.Numerics.dll"]
        self.assertEqual([], copies)


if __name__ == "__main__":
    unittest.main()
