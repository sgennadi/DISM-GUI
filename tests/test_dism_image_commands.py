"""Regression tests for the pinned upstream DISM command-line quoting bug."""
import ast
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "apply_modern.py"


def patch_function():
    tree = ast.parse(SCRIPT.read_text(encoding="utf-8"))
    function = next(node for node in tree.body
                    if isinstance(node, ast.FunctionDef)
                    and node.name == "repair_dism_image_commands")
    module = ast.Module(body=[function], type_ignores=[])
    scope = {}
    exec(compile(ast.fix_missing_locations(module), str(SCRIPT), "exec"), scope)
    return scope["repair_dism_image_commands"]


class DismImageCommandsTest(unittest.TestCase):
    def test_mount_and_apply_are_corrected(self):
        mount = ('            DISM.StartInfo.Arguments = "/Mount-WIM '
                 '/WimFile:""" & strWIM & """" & " /index:""" & strIndex '
                 '& " /MountDir:" & """" & strFolderName & """"')
        read_only = mount.replace("/Mount-WIM ", "/Mount-WIM /ReadOnly ")
        apply = ('            strDISMArguments = "/Apply-Image '
                 '/ImageFile:""" & strSource & """" & " /index:""" '
                 '& strAppendIndex & " /ApplyDir:" & strDest & """" & ""')
        verified = apply.replace(' & ""', ' & " /Verify"')
        source = "\n".join([read_only, mount, apply, verified]) + "\n"
        output = patch_function()(source)
        self.assertEqual(output.count('" /Index:" & strIndex & " /MountDir:"'), 2)
        self.assertEqual(output.count('" /Index:" & strAppendIndex & " /ApplyDir:"""'), 2)
        self.assertNotIn('" /index:"""', output)
        self.assertIn('strDest & """" & " /Verify"', output)

    def test_unrecognized_upstream_fails_closed(self):
        with self.assertRaises(RuntimeError):
            patch_function()("No matching commands here\n")


if __name__ == "__main__":
    unittest.main()
