import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "release"))
import tag_release as release


class ReleaseTests(unittest.TestCase):
    def test_tag_and_section_boundaries(self):
        for tag in ("v0.0.1", "V0.0.1"):
            self.assertEqual(release.TAG.fullmatch(tag)[1], "0.0.1")
        for tag in ("main", "v0.0.1-rc.1", "versions/v0.0.1", "v0.0.1.0"):
            self.assertIsNone(release.TAG.fullmatch(tag))
        section = "### 修改\n\n- 修复\n\n```\n## 示例\n```"
        self.assertEqual(release.release_notes("## V0.0.10\n十\n## v0.0.1\n" + section, "0.0.1"), section)
        for text in ("## V0.0.10\n十", "## V0.0.1\n\n", "## V0.0.1\na\n## v0.0.1\nb"):
            with self.assertRaises(ValueError):
                release.release_notes(text, "0.0.1")

    def test_rejects_wrong_project_version_before_accessing_release(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, {
            "GITHUB_REF_TYPE": "tag", "GITHUB_REF_NAME": "V2.0.0",
            "GITHUB_OUTPUT": str(Path(folder) / "outputs"),
        }), patch.object(sys, "argv", ["tag_release.py", "prepare"]), patch.object(release, "run", return_value="1.0.0"):
            with self.assertRaisesRegex(ValueError, "1.0.0.*V2.0.0"):
                release.main()

    def test_resume_and_verify_public_assets(self):
        tag, commit, notes, version = "v0.0.1", "abc", "本版说明", "0.0.1"
        state = dict(tag_name=tag, body=notes, target_commitish=commit, draft=True, prerelease=False, assets=[])
        newer = dict(tag_name="V0.0.10", draft=False, prerelease=False)
        uploads = []
        with tempfile.TemporaryDirectory() as folder, patch.object(release, "ASSETS", Path(folder)):
            for name in release.asset_names(version):
                (Path(folder) / name).write_bytes(name.encode())
            def gh(*args):
                if args[:2] == ("release", "upload"):
                    path = args[3]
                    if path.name == release.RECEIPT and path.name not in uploads:
                        uploads.append(path.name)
                        raise IOError("上传中断")
                    data = path.read_bytes()
                    state["assets"].append(dict(name=path.name, id=1, size=len(data), digest=release.sha(data)))
                    uploads.append(path.name)
                elif args[:2] == ("release", "edit"):
                    self.assertIn("--latest=false", args)
                    state["draft"] = False
                elif args[0] == "api":
                    return (Path(folder) / release.RECEIPT).read_text()
                else:
                    self.fail(args)
            with patch.object(release, "gh", gh), patch.object(release, "releases", return_value=[state, newer]):
                with self.assertRaises(IOError):
                    release.publish(tag, commit, notes, version, [newer], state)
                self.assertTrue(state["draft"])
                release.publish(tag, commit, notes, version, [newer], state)
                self.assertEqual(uploads.count(f"MyDebugTools-{version}-win-x64.exe"), 1)
                release.check_public(state, tag, commit, notes, version)
                state["assets"][0]["size"] += 1
                with self.assertRaises(ValueError):
                    release.check_public(state, tag, commit, notes, version)


if __name__ == "__main__":
    unittest.main()
