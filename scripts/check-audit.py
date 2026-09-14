#!/usr/bin/env python3
import json
import sys
report=json.load(open(sys.argv[1], encoding="utf-8"))
if any(log.get("level", "").lower() == "error" for log in report.get("logs", [])):
    raise SystemExit("Dependency audit failed; inspect audit output.")
for project in report.get("projects", []):
    for framework in project.get("frameworks", []):
        for key in ("topLevelPackages", "transitivePackages"):
            if any(p.get("vulnerabilities") for p in framework.get(key, [])):
                raise SystemExit("Vulnerable dependency reported; inspect audit output.")
print("No vulnerable application dependencies reported.")
