#!/usr/bin/env python3
"""Refresh release evidence using the authenticated GitHub CLI, never gameplay ratings."""
import datetime
import json
import pathlib
import subprocess

root = pathlib.Path(__file__).resolve().parent.parent
catalog = json.loads((root / "catalog.json").read_text())
repos = sorted({entry["repository"] for entries in catalog.values() for entry in entries})
records = json.loads((root / "mac-compatibility.json").read_text())
checked = datetime.datetime.now(datetime.timezone.utc).isoformat()

def native(name):
    value = name.lower()
    return any(marker in value for marker in ("macos", "darwin", "osx", "-mac", "_mac", ".dmg")) and \
        value.endswith((".zip", ".tar.gz", ".tar.xz", ".tgz", ".dmg"))

def windows(name):
    value = name.lower()
    return any(marker in value for marker in ("windows", "win64", "win32", "-win", "_win", ".exe")) and \
        "arm" not in value and value.endswith((".zip", ".tar.gz", ".exe"))

for offset in range(0, len(repos), 25):
    batch = repos[offset:offset + 25]
    parts = []
    for index, repo in enumerate(batch):
        owner, name = repo.split("/", 1)
        parts.append(f'r{index}:repository(owner:{json.dumps(owner)},name:{json.dumps(name)})'
                     '{latestRelease{tagName url releaseAssets(first:100){nodes{name}}}}')
    result = subprocess.run(["gh", "api", "graphql", "-f", "query=query{" + " ".join(parts) + "}"],
                            capture_output=True, text=True)
    response = json.loads(result.stdout or "{}")
    if not response.get("data"):
        raise SystemExit("GitHub release audit failed; existing evidence has been preserved.")
    for index, repo in enumerate(batch):
        repository = response["data"].get(f"r{index}")
        release = repository.get("latestRelease") if repository else None
        if not release:
            records.setdefault(repo, {"mode": "unknown", "requirements": "No published stable release found."})
            records[repo]["checkedAt"] = checked
            continue
        names = [asset["name"] for asset in release["releaseAssets"]["nodes"]]
        mac = sorted(filter(native, names), key=lambda n: (any(x in n.lower() for x in ("debug", "-dbg", "symbols")), not any(x in n.lower() for x in ("arm", "applesilicon", "apple-silicon", "universal")), n.lower().endswith(".dmg"), n))
        win = list(filter(windows, names))
        prior = records.get(repo, {})
        if prior.get("verified") and prior.get("release") == release["tagName"]:
            prior["checkedAt"] = checked
            continue
        records[repo] = {"mode": "native" if mac else "candidate" if win else "unknown",
                        "release": release["tagName"], "assetName": (mac or win or [""])[0], "availableAssets": names,
                        "verified": False, "evidence": release["url"], "checkedAt": checked,
                        "requirements": prior.get("requirements") or
                        "Follow this project's setup guide and supply its required original game data. Gameplay has not been tested."}

(root / "mac-compatibility.json").write_text(json.dumps(records, indent=2) + "\n")
counts = {mode: sum(entry.get("mode") == mode for entry in records.values()) for mode in ("native", "candidate", "unknown")}
print(json.dumps({"repositories": len(repos), **counts}))
