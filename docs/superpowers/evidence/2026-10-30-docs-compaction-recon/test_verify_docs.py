import importlib.util
import os
import subprocess
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location("verify_docs", os.path.join(HERE, "verify_docs.py"))
verify_docs = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify_docs)


def git(repo, *args):
    subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", *args], check=True, capture_output=True)


class VerifyDocsTests(unittest.TestCase):
    def make_repo(self, files):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        git(folder.name, "init", "-q")
        for rel, text in files.items():
            path = os.path.join(folder.name, rel)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w", encoding="utf-8") as handle:
                handle.write(text)
        git(folder.name, "add", ".")
        git(folder.name, "commit", "-q", "-m", "init")
        return folder.name

    def test_clean_tree_has_no_findings(self):
        repo = self.make_repo({"docs/README.md": "[a](guides/a.md)\n", "docs/guides/a.md": "text\n"})
        self.assertEqual(verify_docs.check(repo, "", False), ([], []))

    def test_broken_relative_link_is_an_error(self):
        repo = self.make_repo({"docs/README.md": "[gone](missing.md)\n"})
        errors, _ = verify_docs.check(repo, "", False)
        self.assertEqual(len(errors), 1)
        self.assertIn("broken link missing.md", errors[0])

    def test_anchor_and_external_links_are_ignored(self):
        repo = self.make_repo({"docs/README.md": "[x](#top) [y](https://example.com/a.md) [z](mailto:a@b.c)\n"})
        self.assertEqual(verify_docs.check(repo, "", False), ([], []))

    def test_links_in_fenced_code_are_ignored(self):
        repo = self.make_repo({"docs/README.md": "```\n[gone](missing.md)\n```\n"})
        self.assertEqual(verify_docs.check(repo, "", False), ([], []))

    def test_links_in_inline_code_are_ignored(self):
        repo = self.make_repo({"docs/README.md": "use `[gone](missing.md)` here\n"})
        self.assertEqual(verify_docs.check(repo, "", False), ([], []))

    def test_unreachable_doc_is_an_error(self):
        repo = self.make_repo({"docs/README.md": "none\n", "docs/guides/orphan.md": "x\n"})
        errors, _ = verify_docs.check(repo, "", False)
        self.assertEqual(errors, ["docs/guides/orphan.md: not reachable from docs/README.md"])

    def test_doc_reachable_through_a_directory_index_is_accepted(self):
        repo = self.make_repo({
            "docs/README.md": "[adr](adr/)\n",
            "docs/adr/README.md": "[one](0001-x.md)\n",
            "docs/adr/0001-x.md": "x\n",
        })
        self.assertEqual(verify_docs.check(repo, "", False), ([], []))

    def test_transient_file_warns_then_fails_on_request(self):
        repo = self.make_repo({"docs/README.md": "none\n", "docs/superpowers/plans/p.md": "x\n"})
        self.assertEqual(verify_docs.check(repo, "", False)[1], ["docs/superpowers/plans/p.md: transient working file present"])
        self.assertEqual(verify_docs.check(repo, "", True)[0], ["docs/superpowers/plans/p.md: transient working file present"])

    def test_non_markdown_transient_file_is_reported(self):
        repo = self.make_repo({"docs/README.md": "none\n", "docs/superpowers/evidence/x/run.json": "{}\n"})
        self.assertEqual(verify_docs.check(repo, "", True)[0], ["docs/superpowers/evidence/x/run.json: transient working file present"])

    def test_untracked_files_are_checked_before_git_add(self):
        repo = self.make_repo({"docs/README.md": "none\n"})
        with open(os.path.join(repo, "docs", "new.md"), "w", encoding="utf-8") as handle:
            handle.write("[gone](missing.md)\n")
        errors, _ = verify_docs.check(repo, "", False)
        self.assertIn("docs/new.md: not reachable from docs/README.md", errors)
        self.assertIn("docs/new.md:1: broken link missing.md", errors)

    def test_tree_permalink_for_a_directory_is_checked_in_the_tag(self):
        repo = self.make_repo({"old/dir/a.md": "x\n", "docs/README.md": "[d](https://github.com/Czarnak/tia-portal-mcp/tree/arch/old/dir) "
                               "[n](https://github.com/Czarnak/tia-portal-mcp/tree/arch/old/none)\n"})
        git(repo, "tag", "arch")
        self.assertEqual(verify_docs.check(repo, "arch", False)[0], ["docs/README.md:1: old/none is not in tag arch"])

    def test_archive_tag_permalink_must_exist_in_the_tag(self):
        repo = self.make_repo({"old/old.md": "x\n", "docs/README.md": "[o](https://github.com/Czarnak/tia-portal-mcp/blob/arch/old/old.md) "
                               "[m](https://github.com/Czarnak/tia-portal-mcp/blob/arch/old/nope.md)\n"})
        git(repo, "tag", "arch")
        errors, _ = verify_docs.check(repo, "arch", False)
        self.assertEqual(errors, ["docs/README.md:1: old/nope.md is not in tag arch"])


if __name__ == "__main__":
    unittest.main()
