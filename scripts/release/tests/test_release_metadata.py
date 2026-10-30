import unittest

from scripts.release.prepare_release import metadata


COMMIT = "a" * 40


class ReleaseMetadataTests(unittest.TestCase):
    def test_tag_release_requires_tag_and_trigger_to_match_checkout(self):
        env = dict(GITHUB_SHA=COMMIT, GITHUB_REF_TYPE="tag", GITHUB_REF_NAME="v2.3.4")
        result = metadata("push", {}, env, "2.3.4", COMMIT, COMMIT)
        self.assertEqual("true", result["stable"])
        self.assertEqual(COMMIT, result["commit"])
        with self.assertRaises(ValueError):
            metadata("push", {}, env, "2.3.4", "b" * 40, COMMIT)
        with self.assertRaises(ValueError):
            metadata("push", {}, env, "2.3.4", COMMIT, "b" * 40)

    def test_dispatch_cannot_override_version_or_source_identity(self):
        payload = dict(sha=COMMIT, version="2.3.4", tag="v2.3.4")
        result = metadata("repository_dispatch", {"client_payload": payload}, {}, "2.3.4", COMMIT, COMMIT)
        self.assertEqual("true", result["stable"])
        for changes in ({"sha": "b" * 40}, {"version": "3.0.0"}, {"tag": "v2.3.4-build.1"}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                metadata("repository_dispatch", {"client_payload": payload | changes}, {}, "2.3.4", COMMIT, COMMIT)

    def test_manual_build_is_prerelease_without_a_stable_manifest_identity(self):
        result = metadata("workflow_dispatch", {}, dict(GITHUB_SHA=COMMIT, GITHUB_RUN_NUMBER="17"),
                          "2.3.4", COMMIT, None)
        self.assertEqual("false", result["stable"])
        self.assertEqual("v2.3.4-build.17", result["tag"])


if __name__ == "__main__":
    unittest.main()
