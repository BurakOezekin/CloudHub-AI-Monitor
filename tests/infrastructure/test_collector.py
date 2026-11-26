import json
import pathlib
import subprocess
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "scripts"))
from infrastructure_monitor import collect, docker_snapshot, kube_snapshot


class CollectorTests(unittest.TestCase):
    def test_disabled_never_runs_cli(self):
        def fail(_):
            self.fail("Unexpected CLI call")
        self.assertEqual(collect(None, None, "default", fail)["docker"]["status"], "disabled")

    def test_docker_status_and_stats(self):
        def fake(args):
            self.assertIn("--context=desktop-linux", args)
            if "ps" in args:
                return '\n'.join(json.dumps(x) for x in [
                    {"Names": "web", "State": "running", "Status": "Up"},
                    {"Names": "old", "State": "exited", "Status": "Exited"}])
            return json.dumps({"Name": "web", "CPUPerc": "2%", "MemUsage": "50MiB / 1GiB"})
        result = docker_snapshot("desktop-linux", fake)
        self.assertEqual(result["total"], 2)
        self.assertEqual(result["rows"][1]["cpu"], "2%")
        self.assertIsNone(result["rows"][0]["cpu"])

    def test_timeout_does_not_look_like_empty_environment(self):
        def fail(args):
            raise subprocess.TimeoutExpired(args, 8)
        self.assertEqual(docker_snapshot("default", fail)["status"], "unavailable")

    def test_pod_health_and_missing_metrics(self):
        def fake(args):
            self.assertIn("--context=dev", args)
            self.assertIn("--namespace=demo", args)
            if "top" in args:
                raise subprocess.CalledProcessError(1, args)
            return json.dumps({"items": [{"metadata": {"name": "api"},
                "spec": {"containers": [{}]}, "status": {"phase": "Running",
                "containerStatuses": [{"ready": False, "restartCount": 4,
                "state": {"waiting": {"reason": "CrashLoopBackOff"}}}]}}]})
        result = kube_snapshot("dev", "demo", "pods", fake)
        self.assertEqual(result["rows"][0]["state"], "CrashLoopBackOff")
        self.assertIn("Restarts 4", result["rows"][0]["detail"])
        self.assertEqual(result["metrics"], "unavailable")
        self.assertEqual(result["status"], "ok")

    def test_node_metrics_and_ready(self):
        def fake(args):
            self.assertNotIn("--namespace=demo", args)
            if "top" in args:
                return "worker 120m 6% 512Mi 20%"
            return json.dumps({"items": [{"metadata": {"name": "worker"},
                "status": {"conditions": [{"type": "Ready", "status": "False"}]}}]})
        result = kube_snapshot("dev", "demo", "nodes", fake)
        self.assertEqual(result["rows"][0]["state"], "NotReady")
        self.assertEqual(result["rows"][0]["memory"], "512Mi")

    def test_rows_are_limited_but_total_preserved(self):
        def fake(args):
            return "\n".join(json.dumps({"Names": str(i), "State": "exited"}) for i in range(40))
        result = docker_snapshot("default", fake)
        self.assertEqual(result["total"], 40)
        self.assertEqual(len(result["rows"]), 25)


if __name__ == "__main__":
    unittest.main()
