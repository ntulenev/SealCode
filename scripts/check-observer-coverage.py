"""Require observer coverage to match the rest of each assembly/client report.

Uses the same merged Cobertura and LCOV reports as CI. No application source is
excluded to satisfy this comparison; observer business logic is in a normal,
covered client module rather than an excluded browser entry point.
"""

import argparse
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path


def percentage(covered, total):
    return round(100 * covered / total, 2) if total else 100.0


def server_stats(classes):
    lines = [line for item in classes for line in item.findall("./lines/line")]
    branches = [tuple(map(int, match.groups())) for line in lines
                if (match := re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage", "")))]
    return {"lines": percentage(sum(int(line.get("hits")) > 0 for line in lines), len(lines)),
            "branches": percentage(sum(item[0] for item in branches), sum(item[1] for item in branches))}


def observer_class(item):
    name = "/" + item.get("filename", "").replace("\\", "/")
    return "/Observers/" in name or name.endswith("/ObserverLimits.cs")


def client_stats(records):
    return {name: percentage(sum(record.get(covered, 0) for record in records),
                             sum(record.get(total, 0) for record in records))
            for name, covered, total in [("lines", "LH", "LF"), ("branches", "BRH", "BRF"),
                                         ("functions", "FNH", "FNF")]}


def compare(server, client):
    results = {}
    for package in ET.parse(server).findall("./packages/package"):
        name = package.get("name")
        if name not in {"Models", "SealCode"}:
            continue
        classes = package.findall("./classes/class")
        new = [item for item in classes if observer_class(item)]
        old = [item for item in classes if not observer_class(item)]
        if not new or not old:
            raise ValueError(f"Missing observer or comparison coverage for {name}")
        results[name] = {"observers": server_stats(new), "rest": server_stats(old)}
    if set(results) != {"Models", "SealCode"}:
        raise ValueError("The merged server report must contain both observer assemblies")

    records = []
    for block in Path(client).read_text(encoding="utf-8").split("end_of_record"):
        record = {}
        for line in block.splitlines():
            key, _, value = line.partition(":")
            if key == "SF":
                record["file"] = value.replace("\\", "/").rsplit("/", 1)[-1]
            elif key in {"LH", "LF", "BRH", "BRF", "FNH", "FNF"}:
                record[key] = int(value)
        if "file" in record:
            records.append(record)
    observer_files = {"observer-application.js", "observer-link.js", "observer-presence.js", "admin-limits.js"}
    new = [record for record in records if record["file"] in observer_files]
    if {record["file"] for record in new} != observer_files:
        raise ValueError("Observer runtime or admin-limit logic is missing from client coverage")
    results["client"] = {"observers": client_stats(new),
                         "rest": client_stats([record for record in records if record["file"] not in observer_files])}
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("server", type=Path, help="Merged Cobertura.xml")
    parser.add_argument("client", type=Path, help="Client lcov.info")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = compare(args.server, args.client)
    output = json.dumps(result, indent=2) + "\n"
    print(output)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output, encoding="utf-8")
    failures = [f"{group}: {metric} {values['observers'][metric]}% < {minimum}%"
                for group, values in result.items() for metric, minimum in values["rest"].items()
                if values["observers"][metric] < minimum]
    if failures:
        raise SystemExit("Observer coverage regressed:\n" + "\n".join(failures))
