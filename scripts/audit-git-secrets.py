"""Read-only scan of Git objects and versionable files. Never emits secret values."""
import argparse
import collections
import datetime
import json
import math
import os
import pathlib
import re
import subprocess

PATTERNS = {
    "nvidia": re.compile(rb"\bnvapi-[A-Za-z0-9_-]{24,}"),
    "github": re.compile(rb"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})"),
    "api-token": re.compile(rb"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{24,}"),
    "private-key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    "aws": re.compile(rb"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "bearer": re.compile(rb"\bBearer\s+[A-Za-z0-9._~+/-]{24,}", re.I),
    "credential-url": re.compile(rb"(?:https?|postgres(?:ql)?|mysql|mongodb)://[^\s:/]+:[^\s/@]{8,}@", re.I),
    "credential-assignment": re.compile(rb"(?:api[_-]?key|access[_-]?token|auth[_-]?token|password|client[_-]?secret)[\"']?\s*[:=]\s*[\"']([^\"'\r\n]{16,})", re.I),
}


def git(*args):
    return subprocess.check_output(["git", *args])


def scan(data, known):
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        data = data.decode("utf-16", errors="replace").encode("utf-8")
    findings = []
    placeholders = []
    for kind, pattern in PATTERNS.items():
        for match in pattern.finditer(data):
            value = match.group()
            if kind == "credential-assignment":
                value = match.group(1)
                counts = collections.Counter(value)
                entropy = -sum(n / len(value) * math.log2(n / len(value)) for n in counts.values())
                if entropy < 3.5:
                    continue
            # Only uniform masks are excluded; local exact-value comparison still applies.
            if kind in ("api-token", "nvidia", "github"):
                suffix = re.sub(rb"^(?:sk-|nvapi-|gh[pousr]_|github_pat_)", b"", value)
                if suffix and set(suffix) <= {ord("x"), ord("X"), ord("*")}:
                    placeholders.append({"kind": kind, "line": data[:match.start()].count(b"\n") + 1})
                    continue
            findings.append({"kind": kind, "line": data[:match.start()].count(b"\n") + 1})
    for secret in known:
        start = 0
        while (start := data.find(secret, start)) >= 0:
            findings.append({"kind": "local-credential-exact", "line": data[:start].count(b"\n") + 1})
            start += len(secret)
    return findings, placeholders


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=".agentloop/security-history-report.json")
    args = parser.parse_args()
    root = pathlib.Path(git("rev-parse", "--show-toplevel").decode().strip())
    os.chdir(root)
    known = set()
    for path in (root / "local-secrets").glob("*.txt"):
        raw = path.read_bytes()
        encoding = "utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
        value = raw.decode(encoding).strip().encode("utf-8")
        if len(value) >= 16:
            known.add(value)
    for name in ("NVIDIA_API_KEY", "TOKENROUTER_API_KEY", "GITHUB_TOKEN", "GH_TOKEN"):
        value = os.environ.get(name, "").strip().encode()
        if len(value) >= 16:
            known.add(value)
    paths = collections.defaultdict(set)
    for line in git("rev-list", "--objects", "--all", "--reflog").decode("utf-8").splitlines():
        parts = line.split(" ", 1)
        if len(parts) == 2:
            paths[parts[0]].add(parts[1])
    objects = collections.Counter()
    matches = []
    masks = []
    process = subprocess.Popen(["git", "cat-file", "--batch-all-objects", "--batch"], stdout=subprocess.PIPE)
    while header := process.stdout.readline():
        oid, kind, size = header.decode().strip().split()
        data = process.stdout.read(int(size))
        if len(data) != int(size) or process.stdout.read(1) != b"\n":
            raise RuntimeError("Incomplete Git object stream")
        objects[kind] += 1
        findings, placeholders = scan(data, known)
        if placeholders:
            masks.append({"object": oid, "paths": sorted(paths[oid]), "placeholders": placeholders})
        if findings:
            matches.append({"object": oid, "type": kind, "paths": sorted(paths[oid]), "findings": findings})
    if process.wait() != 0:
        raise RuntimeError("Git object scan failed")
    worktree = []
    files = git("ls-files", "--cached", "--others", "--exclude-standard", "-z").decode("utf-8").split("\0")
    for name in sorted(set(filter(None, files))):
        path = root / name
        if path.is_file():
            findings, _ = scan(path.read_bytes(), known)
            if findings:
                worktree.append({"path": name, "findings": findings})
    report = {"at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "head": git("rev-parse", "HEAD").decode().strip(),
              "scope": "all local Git objects including unreachable objects; all refs/reflogs; versionable worktree",
              "objectsScanned": dict(objects), "versionableFilesScanned": len(set(filter(None, files))),
              "knownLocalCredentialsCompared": len(known), "objectMatches": matches, "worktreeMatches": worktree,
              "uniformMaskExamples": masks,
              "limitations": "Pattern and exact-value scan; does not prove arbitrary opaque secrets absent. Ignored runtime files are not scanned."}
    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes((json.dumps(report, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))
    print(json.dumps({"objectsScanned": dict(objects), "objectCandidates": len(matches),
                      "worktreeCandidates": len(worktree), "report": str(output)}))
    return 1 if matches or worktree else 0


if __name__ == "__main__":
    raise SystemExit(main())
