"""Offline tests for measure_write_timing.py. Python 3.11+, stdlib only, Windows. No server is started."""

import copy
import datetime
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import queue
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location("measure_write_timing", os.path.join(HERE, "measure_write_timing.py"))
mwt = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mwt)

HEX = "a" * 64
CASES = ["H1", "H2"]
ARGS = {"dryRun": True, "operations": [{"operationId": "o", "operation": "create_tag"}]}


def make_capture(label, root, tool="hmi_write", contract="2.0"):
    document = {"contractVersion": contract, "success": True, "error": None, "phase": "preview",
                "effects": [{}], "batch": None, "verification": None}
    text = json.dumps(document)
    stamp = datetime.datetime(2026, 10, 29, tzinfo=datetime.timezone.utc)
    record = {"recordVersion": 2, "tool": tool, "accessMode": "full", "responseText": text,
              "responseHash": "sha256:" + hashlib.sha256(text.encode()).hexdigest(),
              "timestamp": stamp.isoformat(), "durationMs": root, "phase": "preview",
              "items": [{"operationId": "o", "operation": "create_tag", "durationMs": None, "status": "skipped"}]}
    return {"label": label, "request": {"name": tool, "arguments": ARGS},
            "response": {"content": [{"type": "text", "text": text}], "structuredContent": document, "isError": False},
            "audit": record, "sourceResponseMtime": stamp.timestamp() + .1}


class ParseArgsTests(unittest.TestCase):
    def test_validate(self):
        args = mwt.parse_args(["validate", "dir"])
        self.assertEqual((args.command, args.archive), ("validate", "dir"))

    def test_run(self):
        args = mwt.parse_args(["run", "--tool", "hmi_write", "--contract-version", "2.0", "--expected-version", "X",
                               "--project", "p.ap21", "--project-sha256", HEX, "--out", "o"])
        self.assertEqual((args.command, args.tool, args.contract_version, args.expected_version, args.runs,
                          args.request_timeout, args.out), ("run", "hmi_write", "2.0", "X", 4, 900, "o"))
        self.assertIn("--worker-timeout-minutes", args.server_arg)


class ScheduleTests(unittest.TestCase):
    def test_schedule_and_counts(self):
        schedule = mwt.build_schedule(CASES, 4)
        self.assertEqual(len(schedule), 8)
        self.assertEqual([row["discarded"] for row in schedule[:2]], [True, True])
        self.assertEqual(schedule[2]["label"], "H1-run1")
        self.assertEqual(mwt.expected_counts(["H1", "H2", "H3", "H4", "H5"], 4),
                         {"writes": 20, "reads": 22, "projections": 60, "measured": 15, "discarded": 5})

    def test_medians_are_independent_per_column(self):
        rows = [{"case": "H1", "discarded": d, "rootMs": r, "itemSumMs": i, "remainderMs": r - i}
                for d, r, i in ((True, 999, 999), (False, 10, 9), (False, 20, 1), (False, 30, 8))]
        self.assertEqual(mwt.summarize(rows)["H1"], {"rootMs": 20, "itemSumMs": 8, "remainderMs": 19})


class ValidateTests(unittest.TestCase):
    def build_archive(self, drop_write=False, drop_read=False):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        root = pathlib.Path(folder.name)
        schedule = mwt.build_schedule(CASES, 2)
        captures = [make_capture(s["label"], 100) for s in schedule]
        rows = [{"label": s["label"], "case": s["case"], "run": s["run"], "discarded": s["discarded"],
                 **mwt.audit_metrics(c["response"], c["audit"], c["sourceResponseMtime"], "hmi_write", "2.0")}
                for s, c in zip(schedule, captures)]
        read_doc = {"success": True}
        read = {"label": "r", "response": {"content": [{"type": "text", "text": json.dumps(read_doc)}],
                                           "structuredContent": read_doc}}
        reads = [read] * (len(captures) + 2)
        summary = {"tool": "hmi_write", "contractVersion": "2.0", "cases": CASES, "runs": 2,
                   "rows": rows, "mediansMs": mwt.summarize(rows)}
        projections = {"captures": [{"tool": "x", "success": True}] * (3 * len(captures))}
        workloads = {case: ARGS for case in CASES}
        if drop_write:
            captures = captures[:-1]
        if drop_read:
            reads = reads[:-1]
        (root / "workloads.json").write_text(json.dumps(workloads))
        (root / "summary.json").write_text(json.dumps(summary))
        (root / "timing-writes.jsonl").write_text("".join(json.dumps(c) + "\n" for c in captures))
        (root / "hmi-reads.jsonl").write_text("".join(json.dumps(c) + "\n" for c in reads))
        (root / "lifecycle-status-projections.json").write_text(json.dumps(projections))
        return root

    def test_valid_archive_passes(self):
        self.assertIn("PASS", mwt.validate_archive(self.build_archive()))

    def test_wrong_write_count_rejected(self):
        with self.assertRaises(ValueError):
            mwt.validate_archive(self.build_archive(drop_write=True))

    def test_wrong_read_count_rejected(self):
        with self.assertRaises(ValueError):
            mwt.validate_archive(self.build_archive(drop_read=True))


class SafetyTests(unittest.TestCase):
    def test_project_hash_mismatch_refuses(self):
        with tempfile.TemporaryDirectory() as folder:
            project = pathlib.Path(folder) / "p.ap21"
            project.write_bytes(b"data")
            with self.assertRaises(ValueError):
                mwt.check_project(str(project), HEX)
            mwt.check_project(str(project), hashlib.sha256(b"data").hexdigest())

    def test_run_refuses_before_starting_server(self):
        with tempfile.TemporaryDirectory() as folder:
            project = pathlib.Path(folder) / "p.ap21"
            project.write_bytes(b"data")
            args = mwt.parse_args(["run", "--contract-version", "2.0", "--expected-version", "X",
                                   "--project", str(project), "--project-sha256", HEX,
                                   "--workloads", os.path.join(HERE, "workloads", "hmi-write.json"),
                                   "--out", os.path.join(folder, "out")])
            with self.assertRaises(ValueError):
                mwt.run(args)
            self.assertFalse(os.path.exists(os.path.join(folder, "out")))

    def test_audit_checks(self):
        capture = make_capture("H1-run1", 100)
        response, record, mtime = capture["response"], capture["audit"], capture["sourceResponseMtime"]
        self.assertEqual(mwt.audit_metrics(response, record, mtime, "hmi_write", "2.0"),
                         {"rootMs": 100, "itemSumMs": 0, "remainderMs": 100})
        with self.assertRaises(AssertionError):
            mwt.audit_metrics(response, record, mtime + 20, "hmi_write", "2.0")
        with self.assertRaises(AssertionError):
            mwt.audit_metrics(response, record, mtime, "hmi_write", "9.9")
        bad = copy.deepcopy(record)
        bad["responseHash"] = "sha256:" + "0" * 64
        with self.assertRaises(AssertionError):
            mwt.audit_metrics(response, bad, mtime, "hmi_write", "2.0")

    def test_queue_helpers(self):
        responses = queue.Queue()
        mwt.read_responses(io.StringIO('{"method":"notice"}\n{"id":7,"result":{"ok":true}}\n'), responses)
        self.assertEqual(mwt.await_response(responses, 7, 1), {"ok": True})
        with self.assertRaises(RuntimeError):
            mwt.await_response(responses, 8, 1)
        with self.assertRaises(TimeoutError):
            mwt.await_response(queue.Queue(), 1, .001)

    def test_server_info_assertion(self):
        mwt.check_server_info({"name": "TiaMcpServer", "version": "4.2.0.0"}, "4.2.0.0")
        with self.assertRaises(AssertionError):
            mwt.check_server_info({"name": "TiaMcpServer", "version": "1"}, "4.2.0.0")


if __name__ == "__main__":
    unittest.main()
