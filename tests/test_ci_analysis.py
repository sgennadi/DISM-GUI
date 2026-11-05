"""CI diagnostics pure regression tests (never call GitHub APIs)."""
import unittest
from tools import ci_failure_analysis as analyzer


class AnalyzerTests(unittest.TestCase):
    def test_compiler_codes_are_only_extracted_values(self):
        log = (
            "C:\\Users\\user\\secret.cs(8,1): error CS1061: failed\n"
            "Authorization: bearer opaque-sensitive-value\n"
            "/home/runner/build.cs(9): error BC30456: unknown\n"
            "build error NETSDK1138\n"
        )
        self.assertEqual(
            analyzer.parse_codes(log),
            {"CS1061", "BC30456", "NETSDK1138"},
        )
        for code in analyzer.parse_codes(log):
            self.assertRegex(code, r"^(?:BC|CS|MSB|NETSDK|NU)[0-9]{4,6}$")
            self.assertNotIn("Users", code)
            self.assertNotIn("Authorization", code)

    def test_only_known_step_names_are_approved(self):
        self.assertEqual(
            analyzer.KNOWN["Check patch"], "Source patch"
        )
        self.assertEqual(
            analyzer.KNOWN.get("Untrusted: example@example.com", "Other CI step"),
            "Other CI step",
        )

    def test_warnings_do_not_count_as_compiler_errors(self):
        log = (
            "warning BC40004: variable conflicts with base class\n"
            "warning CS8602: possible null dereference\n"
            "src.vb(3,4): error BC32016: invalid expression\n"
            "##[error]src.cs(5): error CS1061: missing property\n"
            "error BC40004 was mentioned only in a comment\n"
        )
        self.assertEqual(analyzer.parse_codes(log), {"BC32016", "CS1061", "BC40004"})

    def test_no_noncompiler_tokens_exposed(self):
        self.assertEqual(analyzer.parse_codes("API key=xxx123 no error code"), set())


if __name__ == "__main__":
    unittest.main()
