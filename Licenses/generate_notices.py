#!/usr/bin/env python3
"""Builds FishMMO's third-party notice files from Licenses/components.json.

Outputs (all paths relative to the repository root):

  THIRD-PARTY-NOTICES.md                                   every product, human-readable
  Licenses/THIRD-PARTY-NOTICES-<Product>.txt               one plain-text file per shipped product
  FishMMO-Unity/Assets/WebGLTemplates/FishMMO/THIRD-PARTY-NOTICES.txt
                                                           the WebGL file, copied into the template so
                                                           every WebGL build carries it at its root

Edit Licenses/components.json (and Licenses/texts/ for licence texts), then run:

  python3 Licenses/generate_notices.py           write the files
  python3 Licenses/generate_notices.py --check   exit 1 when any file is out of date

Standard library only. Paths are resolved from this script's location.
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DATA = os.path.join(HERE, "components.json")
MARKDOWN = os.path.join(ROOT, "THIRD-PARTY-NOTICES.md")
WEBGL_TEMPLATE_COPY = os.path.join(ROOT, "FishMMO-Unity", "Assets", "WebGLTemplates", "FishMMO", "THIRD-PARTY-NOTICES.txt")

RULE = "-" * 79
DOUBLE_RULE = "=" * 79
GENERATED = "Generated from Licenses/components.json by Licenses/generate_notices.py. Do not edit by hand."

INTRO = (
    "FishMMO's own code is licensed under the MIT License (see LICENSE or LICENSE.txt).\n"
    "That licence covers first-party FishMMO code only. The third-party components\n"
    "listed below remain under their own licences, which are reproduced in full at\n"
    "the end of this file."
)

UNITY_NOTE = (
    "The Unity engine and Unity packages are used under Unity's own terms; the\n"
    "third-party notices of Unity packages for code compiled into the player (for\n"
    "example FXAA and Clipper) are listed here."
)

GAME_PRODUCTS = ("client", "webgl", "server")


def read_text(relative):
    with open(os.path.join(HERE, relative), encoding="utf-8-sig") as handle:
        text = handle.read().replace("\r\n", "\n").replace("\r", "\n")
    return text.strip("\n") + "\n"


def load():
    with open(DATA, encoding="utf-8") as handle:
        data = json.load(handle)
    texts = data["texts"]
    for component in data["components"]:
        for key in component["texts"]:
            if key not in texts:
                raise SystemExit(f"components.json: '{component['name']}' names unknown text '{key}'")
        for product in component["products"]:
            if product not in data["products"]:
                raise SystemExit(f"components.json: '{component['name']}' names unknown product '{product}'")
    return data


def components_for(data, product):
    return [c for c in data["components"] if product in c["products"]]


def texts_for(data, components):
    keys = []
    for component in components:
        for key in component["texts"]:
            if key not in keys:
                keys.append(key)
    return keys


def text_label(data, key):
    return f"[{key}] {data['texts'][key]['title']}"


def component_lines(data, index, component):
    lines = [f"{index}. {component['name']} {component['version']}".rstrip()]
    lines.append(f"   {component['url']}")
    lines.append(f"   Licence: {component['license']}")
    for line in component["copyright"]:
        lines.append(f"   {line}")
    for package in component.get("packages", []):
        lines.append(f"   Package: {package}")
    lines.append("   Licence text: " + "; ".join(text_label(data, k) for k in component["texts"]))
    if component.get("unverified"):
        lines.append("   UNVERIFIED: see the note.")
    if component.get("notes"):
        lines.append(f"   Note: {component['notes']}")
    return lines


def render_text_file(data, product):
    info = data["products"][product]
    components = components_for(data, product)
    out = [
        "FishMMO - Third-party notices",
        info["title"],
        DOUBLE_RULE,
        "",
        INTRO,
        "",
    ]
    if product in GAME_PRODUCTS:
        out += [UNITY_NOTE, ""]
    out += [
        GENERATED,
        "",
    ]
    if product == "updater":
        out += [
            "The Updater is self-contained: it carries the .NET runtime. That runtime's own",
            "licence and third-party notices ship beside it as Updater-dotnet-LICENSE.txt",
            "and Updater-dotnet-THIRD-PARTY-NOTICES.txt, and form part of this notice.",
            "",
        ]
    out += [DOUBLE_RULE, "COMPONENTS", DOUBLE_RULE, ""]
    for index, component in enumerate(components, 1):
        out += component_lines(data, index, component)
        out.append("")
    out += [DOUBLE_RULE, "LICENCE TEXTS", DOUBLE_RULE, ""]
    for key in texts_for(data, components):
        out += [RULE, text_label(data, key), RULE, "", read_text(data["texts"][key]["file"]).rstrip("\n"), ""]
    return "\n".join(out).rstrip("\n") + "\n"


def md_escape(text):
    return text.replace("|", "\\|")


def render_markdown(data):
    out = [
        "# Third-party notices",
        "",
        INTRO.replace("\n", " "),
        "",
        UNITY_NOTE.replace("\n", " "),
        "",
        f"_{GENERATED}_",
        "",
        "Per-product plain-text copies (the files that ship with each build) are in",
        "[Licenses/](Licenses/). Entries marked **UNVERIFIED** record the best available",
        "information and still need checking against the upstream source.",
        "",
        "## Contents",
        "",
    ]
    for product, info in data["products"].items():
        anchor = product
        out.append(f"- [{info['title']}](#{anchor})")
    out.append("- [Licence texts](#licence-texts)")
    out.append("")

    for product, info in data["products"].items():
        out.append(f'<a id="{product}"></a>')
        out.append("")
        out.append(f"## {info['title']}")
        out.append("")
        if info.get("file"):
            out.append(f"Shipped as `Licenses/{info['file']}`.")
            out.append("")
        if product == "updater":
            out.append("The Updater also ships the .NET runtime pack's own `LICENSE.TXT` and "
                       "`THIRD-PARTY-NOTICES.TXT` (as `Updater-dotnet-LICENSE.txt` and "
                       "`Updater-dotnet-THIRD-PARTY-NOTICES.txt`), copied from the runtime pack the publish used.")
            out.append("")
        for component in components_for(data, product):
            title = f"{component['name']} {component['version']}".rstrip()
            if component.get("unverified"):
                title += " (UNVERIFIED)"
            out.append(f"### {md_escape(title)}")
            out.append("")
            out.append(f"- Upstream: <{component['url']}>")
            out.append(f"- Licence: `{component['license']}`")
            for line in component["copyright"]:
                out.append(f"- {md_escape(line)}")
            for package in component.get("packages", []):
                out.append(f"- Package: {md_escape(package)}")
            refs = ", ".join(f"[{data['texts'][k]['title']}](#text-{k.lower()})" for k in component["texts"])
            out.append(f"- Licence text: {refs}")
            if component.get("notes"):
                out.append(f"- Note: {md_escape(component['notes'])}")
            out.append("")

    out.append('<a id="licence-texts"></a>')
    out.append("")
    out.append("## Licence texts")
    out.append("")
    out.append("Each distinct licence text appears once; the components above refer to it.")
    out.append("")
    for key, info in data["texts"].items():
        out.append(f'<a id="text-{key.lower()}"></a>')
        out.append("")
        out.append(f"### {info['title']}")
        out.append("")
        out.append("````text")
        out.append(read_text(info["file"]).rstrip("\n"))
        out.append("````")
        out.append("")
    return "\n".join(out).rstrip("\n") + "\n"


def outputs(data):
    files = {MARKDOWN: (render_markdown(data), False)}
    for product, info in data["products"].items():
        if not info.get("file"):
            continue
        # A UTF-8 byte order mark: browsers and some editors otherwise guess a legacy
        # encoding for text/plain served without a charset, which garbles the (c) signs
        # and accented names in the licence texts.
        files[os.path.join(HERE, info["file"])] = (render_text_file(data, product), True)
    files[WEBGL_TEMPLATE_COPY] = files[os.path.join(HERE, data["products"]["webgl"]["file"])]
    return files


def encode(text, bom):
    return (b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8")


def main(argv):
    check = "--check" in argv
    data = load()
    stale = []
    for path, (text, bom) in outputs(data).items():
        wanted = encode(text, bom)
        current = None
        if os.path.exists(path):
            with open(path, "rb") as handle:
                current = handle.read()
        if current == wanted:
            continue
        stale.append(os.path.relpath(path, ROOT))
        if not check:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as handle:
                handle.write(wanted)
    if check:
        if stale:
            print("Out of date (run python3 Licenses/generate_notices.py):")
            for path in stale:
                print("  " + path)
            return 1
        print("Third-party notices are up to date.")
        return 0
    for path in stale:
        print("wrote " + path)
    if not stale:
        print("Third-party notices are up to date.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
