"""Read-only local Docker/Kubernetes collector; standard library only."""
import argparse
import json
import subprocess
import time
import urllib.request

LIMIT = 25
TIMEOUT = 8


def run(args):
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=TIMEOUT, check=True).stdout


def source(status="disabled", metrics="notRequested", rows=(), total=0):
    return {"status": status, "metrics": metrics, "total": total, "rows": list(rows)}


def row(name, state, detail="", cpu=None, memory=None):
    return {"name": str(name)[:253], "state": str(state)[:80], "detail": str(detail)[:120],
            "cpu": str(cpu)[:32] if cpu is not None else None,
            "memory": str(memory)[:32] if memory is not None else None}


def json_lines(output):
    return [json.loads(line) for line in output.splitlines() if line.strip()]


ERRORS = (OSError, subprocess.SubprocessError, ValueError, KeyError, TypeError, AttributeError)


def docker_snapshot(context, runner=run):
    if not context:
        return source()
    cmd = ["docker", "--context=" + context]
    try:
        containers = json_lines(runner(cmd + ["ps", "-a", "--format", "{{json .}}"]))
        containers.sort(key=lambda item: item["Names"])
        rows = [row(item["Names"], item.get("State", "unknown"), item.get("Status", ""))
                for item in containers[:LIMIT]]
    except ERRORS:
        return source("unavailable")
    metrics = "notRequested"
    if any(item.get("State") == "running" for item in containers):
        try:
            stats = {item["Name"]: item for item in json_lines(
                runner(cmd + ["stats", "--no-stream", "--format", "{{json .}}"]))}
            for entry in rows:
                stat = stats.get(entry["name"])
                if stat:
                    entry["cpu"] = str(stat["CPUPerc"])[:32]
                    entry["memory"] = str(stat["MemUsage"])[:32]
            metrics = "available" if stats else "unavailable"
        except ERRORS:
            metrics = "unavailable"
    return source("ok", metrics, rows, len(containers))


def ready_condition(status):
    return next((item.get("status") for item in status.get("conditions", [])
                 if item.get("type") == "Ready"), None)


def pod_row(item):
    status = item.get("status", {})
    containers = status.get("containerStatuses", [])
    ready = sum(bool(c.get("ready")) for c in containers)
    expected = len(item.get("spec", {}).get("containers", []))
    restarts = sum(c.get("restartCount", 0) for c in
                   containers + status.get("initContainerStatuses", []))
    waiting = next((c.get("state", {}).get("waiting", {}).get("reason")
                    for c in containers + status.get("initContainerStatuses", [])
                    if c.get("state", {}).get("waiting", {}).get("reason")), None)
    phase = status.get("phase", "Unknown")
    state = "Terminating" if item.get("metadata", {}).get("deletionTimestamp") else (
        waiting or ("NotReady" if phase == "Running" and ready_condition(status) != "True" else phase))
    return row(item["metadata"]["name"], state,
               f"Ready {ready}/{expected}; Restarts {restarts}")


def node_row(item):
    status = item.get("status", {})
    ready = ready_condition(status)
    state = {"True": "Ready", "False": "NotReady"}.get(ready, "Unknown")
    detail = "Scheduling disabled" if item.get("spec", {}).get("unschedulable") else ""
    return row(item["metadata"]["name"], state, detail)


def kube_snapshot(context, namespace, kind, runner=run):
    if not context:
        return source()
    cmd = ["kubectl", "--context=" + context, "--request-timeout=5s"]
    if kind == "pods":
        cmd += ["--namespace=" + namespace]
    try:
        items = json.loads(runner(cmd + ["get", kind, "-o", "json"]))["items"]
        items.sort(key=lambda item: item["metadata"]["name"])
        rows = [(pod_row if kind == "pods" else node_row)(item) for item in items[:LIMIT]]
    except ERRORS:
        return source("unavailable")
    metrics = "notRequested"
    if items:
        try:
            top = runner(cmd + ["top", kind, "--no-headers"])
            stats = {}
            for line in top.splitlines():
                parts = line.split()
                if kind == "pods" and len(parts) == 3:
                    stats[parts[0]] = (parts[1], parts[2])
                elif kind == "nodes" and len(parts) >= 5:
                    stats[parts[0]] = (parts[1], parts[3])
            for entry in rows:
                if entry["name"] in stats:
                    entry["cpu"], entry["memory"] = (v[:32] for v in stats[entry["name"]])
            metrics = "available" if stats else "unavailable"
        except ERRORS:
            metrics = "unavailable"
    return source("ok", metrics, rows, len(items))


def collect(docker_context, kube_context, namespace, runner=run):
    return {"docker": docker_snapshot(docker_context, runner),
            "pods": kube_snapshot(kube_context, namespace, "pods", runner),
            "nodes": kube_snapshot(kube_context, namespace, "nodes", runner)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--docker-context")
    parser.add_argument("--kube-context")
    parser.add_argument("--namespace", default="default")
    parser.add_argument("--port", type=int, default=5081)
    parser.add_argument("--interval", type=int, default=20)
    parser.add_argument("--once", action="store_true")
    args = parser.parse_args()
    if not (args.docker_context or args.kube_context):
        parser.error("Select at least one explicit Docker or Kubernetes context.")
    if not 1 <= args.port <= 65535 or not 10 <= args.interval <= 30:
        parser.error("Port must be 1–65535; interval must be 10–30 seconds.")
    # No remote endpoint, proxy, or redirects.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):
            return None
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    url = f"http://127.0.0.1:{args.port}/api/infrastructure"
    while True:
        payload = collect(args.docker_context, args.kube_context, args.namespace)
        request = urllib.request.Request(url, json.dumps(payload, ensure_ascii=False).encode(),
                                         {"Content-Type": "application/json"}, method="POST")
        try:
            with opener.open(request, timeout=5) as response:
                print("CloudHub: " + ", ".join(f"{k}={v['status']} ({v['total']})"
                                              for k, v in payload.items()), flush=True)
        except OSError:
            print("CloudHub API nicht erreichbar oder Messung abgelehnt.", flush=True)
            if args.once:
                return 1
        if args.once:
            return 0
        time.sleep(args.interval)


if __name__ == "__main__":
    raise SystemExit(main())
