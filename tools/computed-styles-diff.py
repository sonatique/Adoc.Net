#!/usr/bin/env python3
"""Diff Chrome's computed styles between two HTML pages.

Launches Chrome headless with the remote-debugging port enabled, opens both
pages, and uses CDP's Runtime.evaluate to dump getComputedStyle() output for
matching elements. Then prints a side-by-side diff of every CSS property that
differs.

Useful for hunting down sub-CSS-source rendering deltas (font metrics, style
inheritance, hidden specificity) that aren't obvious from grepping CSS source.

Usage:
    python tools/computed-styles-diff.py <reference.html> <candidate.html> \\
        [--selectors "h1,h2,p,pre,.sect1"]

Output: per-element table of differing computed properties.
"""
from __future__ import annotations

import argparse
import json
import os
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import websocket  # type: ignore  # websocket-client

CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

# CSS properties most likely to explain layout drift. Reduces noise.
RELEVANT_PROPS = {
    "font-size", "font-family", "font-weight", "font-style", "font-variant",
    "line-height", "letter-spacing", "word-spacing", "text-rendering",
    "margin-top", "margin-right", "margin-bottom", "margin-left",
    "padding-top", "padding-right", "padding-bottom", "padding-left",
    "border-top-width", "border-bottom-width",
    "border-top-style", "border-bottom-style",
    "width", "max-width", "min-width",
    "color", "background-color",
    "display", "box-sizing",
}


def find_free_port() -> int:
    s = socket.socket()
    s.bind(("", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def launch_chrome(port: int, user_data_dir: Path) -> subprocess.Popen:
    user_data_dir.mkdir(parents=True, exist_ok=True)
    cmd = [
        CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
        "--hide-scrollbars", "--window-size=800,1100",
        f"--remote-debugging-port={port}",
        "--remote-allow-origins=*",
        f"--user-data-dir={user_data_dir}",
        "about:blank",
    ]
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    # Wait for the debug port to come up.
    for _ in range(40):
        try:
            urllib.request.urlopen(f"http://localhost:{port}/json/version", timeout=0.5).read()
            return proc
        except Exception:
            time.sleep(0.25)
    proc.kill()
    raise RuntimeError("Chrome failed to start with debug port")


def open_page(port: int, file_url: str) -> str:
    """Open a new tab, navigate to file_url, return its WebSocket debugger URL."""
    # Chrome's /json/new requires PUT in newer versions.
    req = urllib.request.Request(
        f"http://localhost:{port}/json/new?{urllib.parse.quote(file_url, safe=':/')}",
        method="PUT",
    )
    target = urllib.request.urlopen(req).read()
    info = json.loads(target.decode("utf-8"))
    return info["webSocketDebuggerUrl"]


def cdp_call(ws, method: str, params: dict | None = None, msg_id: int = 1) -> dict:
    payload = {"id": msg_id, "method": method}
    if params:
        payload["params"] = params
    ws.send(json.dumps(payload))
    while True:
        msg = json.loads(ws.recv())
        if msg.get("id") == msg_id:
            return msg


def wait_for_load(ws, max_seconds: float = 10.0) -> None:
    """Poll document.readyState until 'complete' (handles font load reflows)."""
    deadline = time.time() + max_seconds
    while time.time() < deadline:
        r = cdp_call(ws, "Runtime.evaluate", {
            "expression": "document.readyState", "returnByValue": True,
        }, msg_id=int(time.time() * 1000) % 1000000)
        if r.get("result", {}).get("result", {}).get("value") == "complete":
            # Extra grace for font loading
            time.sleep(1.5)
            return
        time.sleep(0.2)


def dump_computed_styles(ws, selectors: list[str]) -> dict:
    """For each selector, return computed style of the FIRST match (or None)."""
    js = """
    (() => {
      const sels = SELECTORS_PLACEHOLDER;
      const out = {};
      for (const sel of sels) {
        const el = document.querySelector(sel);
        if (!el) { out[sel] = null; continue; }
        const cs = window.getComputedStyle(el);
        const props = {};
        for (let i = 0; i < cs.length; i++) {
          const name = cs[i];
          props[name] = cs.getPropertyValue(name);
        }
        const r = el.getBoundingClientRect();
        out[sel] = {
          props: props,
          rect: { x: r.x, y: r.y, w: r.width, h: r.height },
          tagName: el.tagName.toLowerCase(),
          className: el.className,
        };
      }
      return JSON.stringify(out);
    })()
    """.replace("SELECTORS_PLACEHOLDER", json.dumps(selectors))
    r = cdp_call(ws, "Runtime.evaluate", {
        "expression": js, "returnByValue": True,
    }, msg_id=42)
    val = r.get("result", {}).get("result", {}).get("value")
    if not val:
        raise RuntimeError(f"evaluate failed: {r}")
    return json.loads(val)


def diff_styles(ref: dict, cand: dict, selectors: list[str]) -> list[str]:
    lines: list[str] = []
    for sel in selectors:
        r = ref.get(sel)
        c = cand.get(sel)
        if r is None and c is None:
            lines.append(f"\n## {sel}: missing on BOTH sides")
            continue
        if r is None:
            lines.append(f"\n## {sel}: missing on REFERENCE only")
            continue
        if c is None:
            lines.append(f"\n## {sel}: missing on CANDIDATE only")
            continue
        # Compare relevant props
        diffs = []
        for prop in sorted(RELEVANT_PROPS):
            rv = r["props"].get(prop, "")
            cv = c["props"].get(prop, "")
            if rv != cv:
                diffs.append((prop, rv, cv))
        # Compare bounding rect
        rr, cr = r["rect"], c["rect"]
        rect_diff = abs(rr["y"] - cr["y"]) > 0.5 or abs(rr["h"] - cr["h"]) > 0.5
        if not diffs and not rect_diff:
            continue
        lines.append(f"\n## {sel}  (ref y={rr['y']:.0f} h={rr['h']:.0f}  vs  cand y={cr['y']:.0f} h={cr['h']:.0f})")
        if rect_diff:
            lines.append(f"   rect deltay={cr['y']-rr['y']:+.1f}  deltah={cr['h']-rr['h']:+.1f}")
        for prop, rv, cv in diffs:
            lines.append(f"   {prop:<22}  REF={rv:<40}  CAND={cv}")
    return lines


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--selectors", default="body,#header,#header h1,.sect1,.sect1 h2,.sect1 .sectionbody,.sect1 .paragraph,.sect1 .paragraph p,.sect1 .listingblock,.sect1 .listingblock pre,.sect1 + .sect1")
    args = parser.parse_args(argv)

    if not args.reference.exists() or not args.candidate.exists():
        print("error: one of the inputs does not exist", file=sys.stderr)
        return 2

    selectors = [s.strip() for s in args.selectors.split(",") if s.strip()]
    port = find_free_port()
    user_data = Path(os.environ.get("TEMP", "/tmp")) / f"chrome-cdp-{port}"

    chrome = launch_chrome(port, user_data)
    try:
        results: dict[str, dict] = {}
        for label, path in [("reference", args.reference), ("candidate", args.candidate)]:
            url = "file:///" + str(path.resolve()).replace("\\", "/")
            ws_url = open_page(port, url)
            ws = websocket.create_connection(ws_url)
            try:
                cdp_call(ws, "Page.enable")
                cdp_call(ws, "Runtime.enable")
                wait_for_load(ws)
                results[label] = dump_computed_styles(ws, selectors)
                print(f"  {label}: dumped {len([k for k in results[label] if results[label][k]])} elements")
            finally:
                ws.close()

        diff_lines = diff_styles(results["reference"], results["candidate"], selectors)
        if not diff_lines:
            print("\nNo differences found across the inspected properties.")
        else:
            print("\n# Computed-style diffs (REF -> CAND)")
            print("\n".join(diff_lines))
    finally:
        chrome.kill()
        chrome.wait(timeout=5)

    return 0


if __name__ == "__main__":
    import urllib.parse
    sys.exit(main(sys.argv[1:]))
