#!/usr/bin/env python3
"""Summarize recurring CI failures into a sanitized GitHub Issue.

Only allowlisted CI step names and standard compiler error codes are posted.
Raw GitHub Actions logs are neither persisted nor included in the Issue.
"""
from collections import Counter
import json
import os
import re
import subprocess

TITLE = "[CI Diagnostics] Recurring DISM GUI build failures"
KNOWN = {
    "Prepare modernized DISM GUI source": "Source preparation",
    "Pre-build diagnostics": "Pre-build checks",
    "Check patch": "Source patch",
    "Smoke-test WinForms and real read-only WIM mount (x64)": "WIM/UI smoke tests",
    "Publish portable EXE": "Portable EXE publication",
    "Verify embedded EXE icon and no PDB/config clutter": "Artifact validation",
}
ERROR_CODE = re.compile(r"\berror\s+((?:BC|CS|MSB|NETSDK|NU)[0-9]{4,6})\b", re.I)
ADVICE = {
    "BC": "Check VB source, imports, name shadowing and overloads.",
    "CS": "Check C# smoke tests and source contracts.",
    "MSB": "Check generated project and MSBuild targets.",
    "NETSDK": "Check SDK and target framework compatibility.",
    "NU": "Check restore and .NET dependency resolution.",
}

def gh(*args):
    result = subprocess.run(["gh", *args], capture_output=True,
                            text=True, timeout=50, check=False)
    if result.returncode != 0:
        raise RuntimeError("GitHub command failed (exit " + str(result.returncode) + ")")
    return result.stdout

def json_gh(*args):
    return json.loads(gh(*args))

def parse_codes(log_text):
    # Only known compiler codes, never a raw log line or untrusted path.
    # Require the compiler's actual 'error BC12345' wording: do not
    # classify warnings, suggestions or plain mentions of error codes as failures.
    return set(x.upper() for x in ERROR_CODE.findall(log_text))

def signature(run_id):
    details = json_gh("run", "view", str(run_id), "--json", "jobs")
    found = set()
    for job in details.get("jobs", []):
        if job.get("conclusion") != "failure":
            continue
        for step in job.get("steps", []):
            if step.get("conclusion") == "failure":
                found.add("Step: " + KNOWN.get(step.get("name"), "Other CI step"))
    try:
        log = gh("run", "view", str(run_id), "--log-failed")
        found.update(parse_codes(log))
    except (RuntimeError, subprocess.TimeoutExpired):
        # GitHub may expire logs; job step summaries still work.
        pass
    return found

def main():
    # Failures must repeat in separate builds before creating a ticket.
    runs = json_gh("run", "list", "--workflow", "build.yml",
                   "--status", "failure", "--limit", "50",
                   "--json", "databaseId,createdAt")
    from datetime import datetime, timezone, timedelta
    cutoff = datetime.now(timezone.utc) - timedelta(days=14)
    ids = []
    for run in runs:
        try:
            stamp = datetime.fromisoformat(run["createdAt"].replace("Z", "+00:00"))
            if stamp >= cutoff:
                ids.append(int(run["databaseId"]))
        except (KeyError, TypeError, ValueError):
            continue

    occurrences = Counter()
    for run_id in ids[:15]:
        for item in signature(run_id):
            occurrences[item] += 1
    repeated = [(name, n) for name, n in occurrences.most_common() if n >= 2]
    if not repeated:
        print("CI diagnostics: no recurring failures across separate runs.")
        return

    rows = []
    for name, count in repeated[:25]:
        # These values originate exclusively from allowlisted names / compiler codes.
        category = next((k for k in ADVICE if name.startswith(k)), "")
        suggestion = ADVICE.get(
            category, "Reproduce the failed CI step and add a targeted regression test.")
        rows.append("| " + name + " | " + str(count) + " | " + suggestion + " |")
    body = "\n".join([
        "<!-- dism-gui-ci-diagnostics-v1 -->",
        "## Recurring CI errors (last 14 days)", "",
        "Generated from GitHub Actions failures. Only known step names and compiler",
        "error codes are included; **no raw logs or device details are published**.", "",
        "| Signature | Failed runs | Suggested investigation |",
        "|---|---:|---|", *rows, "",
        "Fix the recurring problem, add a regression test and confirm all three",
        "Windows architectures and the read-only mounted-WIM integration test pass."
    ])
    existing = json_gh("issue", "list", "--state", "open", "--limit", "70",
                       "--json", "number,title,body")
    match = next((x for x in existing if x["title"] == TITLE), None)
    if match is None:
        gh("issue", "create", "--title", TITLE, "--body", body)
        print("Created one recurring CI diagnostics issue.")
    elif match.get("body") != body:
        gh("issue", "edit", str(match["number"]), "--body", body)
        print("Updated existing recurring CI diagnostics issue.")
    else:
        print("CI diagnostics issue is already up to date.")

if __name__ == "__main__":
    main()
