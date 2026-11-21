"""Regression guards for updater security and release publication policy."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


class UpdaterSecurityPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.updater = (ROOT / "tools" / "UpdateManager.vb").read_text(encoding="utf-8-sig")
        cls.workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")

    def test_unsigned_update_fails_closed(self):
        self.assertIn("Private Const SignedUpdateVerificationReady As Boolean = False", self.updater)
        self.assertIn("RequireSignedUpdate()", self.updater)
        self.assertRegex(self.updater, r"(?s)Function HandleSelfUpdateMode\(\).*?RequireSignedUpdate\(\)")
        self.assertRegex(self.updater, r"(?s)Function StageAndLaunchAsync\(.*?RequireSignedUpdate\(\)")

    def test_staging_is_not_in_user_profile(self):
        self.assertIn("Environment.SpecialFolder.CommonApplicationData", self.updater)
        self.assertIn("CreateProtectedStage()", self.updater)
        self.assertIn("AssertProtectedDirectory(stage)", self.updater)

    def test_hash_uses_same_open_zip_handle_for_extraction(self):
        self.assertIn("Using zipHandle As New FileStream", self.updater)
        self.assertIn("SHA256.HashData(zipHandle)", self.updater)
        self.assertIn("New ZipArchive(zipHandle, ZipArchiveMode.Read, True)", self.updater)

    def test_finish_rechecks_exe_hash(self):
        self.assertIn("Using trustedExe As New FileStream", self.updater)
        self.assertIn("SHA256.HashData(trustedExe)", self.updater)
        self.assertIn("Staged EXE changed after verification", self.updater)

    def test_stable_not_published_on_main_push(self):
        self.assertRegex(self.workflow, r"(?m)^  stable-release:\s*\n")
        stable_section = self.workflow.split("  stable-release:", 1)[1].split("  rc-release:", 1)[0]
        self.assertIn("if: false", stable_section)
        self.assertNotIn("if: github.event_name == 'push'", stable_section)


if __name__ == "__main__":
    unittest.main()
