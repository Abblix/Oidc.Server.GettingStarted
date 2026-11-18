"""Checks that what guide.sh builds from these files is what a reader of the guide ends up with.

The guide takes its code from these files by region, or a file whole. A file the guide does not show
whole is one the reader edits: the template's file, with the guide's regions put in. So outside its
regions such a file must be the `dotnet new mvc` file line for line, in the same order: a line the
template lacks is one the reader is never told to write, and a template line missing here is one the
reader keeps while guide.sh builds without it. A region is either new lines, or replaces the
template's lines from the one its own first line repeats to the one its own last line repeats, as
the guide tells the reader to replace that part.

Region markers: a line that, stripped of its indentation, is `// #region <name>`, `# #region <name>`
or `@* #region <name> *@` opens a region, and `// #endregion`, `# #endregion` or `@* #endregion *@`
closes the innermost open one. Whether the documentation site includes each region is not checked here.

Usage: python check-regions.py   (needs the .NET SDK the guide uses)
"""

import difflib
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent

# Files the guide shows whole, so each of their lines is shown
WHOLE_FILES = {
    "OpenIDProviderApp/Program.cs",
    "OpenIDProviderApp/appsettings.json",
    "OpenIDProviderApp/TestUserStorage.cs",
    "OpenIDProviderApp/Controllers/AuthController.cs",
    "OpenIDProviderApp/Views/Auth/Login.cshtml",
    "TestClientApp/appsettings.json",
    "TestClientApp/appsettings.Development.json",
    "TestClientApp/Controllers/HomeController.cs",
    "TestClientApp/Views/Home/Index.cshtml",
}

# Not files the guide writes: the script that follows the guide, and this check
NOT_GUIDE_FILES = {"guide.sh", "check-regions.py"}

PROJECTS = ("OpenIDProviderApp", "TestClientApp")

OPENERS = (("// #region ", ""), ("# #region ", ""), ("@* #region ", " *@"))
CLOSERS = ("// #endregion", "# #endregion", "@* #endregion *@")


class Region:
    """Stands in a file's line sequence for one top-level region."""

    def __init__(self, name):
        self.name = name
        self.first_line = None
        self.last_line = None

    def __eq__(self, other):
        return isinstance(other, Region) and other.name == self.name

    def __hash__(self):
        return hash(self.name)


def read_lines(path):
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n").split("\n")


def region_opened(stripped):
    for prefix, suffix in OPENERS:
        if stripped.startswith(prefix) and stripped.endswith(suffix):
            name = stripped[len(prefix):len(stripped) - len(suffix)].strip()
            if name:
                return name
    return None


def outline(path, problems):
    """The file's lines outside every region, each top-level region as one Region in its place."""
    name_of_file = path.relative_to(ROOT).as_posix()
    items, open_regions, names = [], [], set()
    for number, line in enumerate(read_lines(path), start=1):
        stripped = line.strip()
        name = region_opened(stripped)
        if name is not None:
            if name in names:
                problems.append(f"{name_of_file}:{number}: region '{name}' is opened twice")
            names.add(name)
            if not open_regions:
                items.append(Region(name))
            open_regions.append(name)
        elif stripped in CLOSERS:
            if not open_regions:
                problems.append(f"{name_of_file}:{number}: #endregion closes no region")
            else:
                open_regions.pop()
        elif not open_regions:
            items.append(line)
        elif isinstance(items[-1], Region) and stripped:
            if items[-1].first_line is None:
                items[-1].first_line = line
            items[-1].last_line = line
    for name in open_regions:
        problems.append(f"{name_of_file}: region '{name}' is never closed")
    return items


def compare_with_template(name, items, template, problems):
    """Outside the regions the file must be the template in order; a region may replace template lines."""
    # Blank lines are layout: matched, they would cut one replaced block into pieces
    template = [line for line in template if line.strip()]
    items = [item for item in items if isinstance(item, Region) or item.strip()]
    hunks = []
    matcher = difflib.SequenceMatcher(a=template, b=items, autojunk=False)
    for tag, a1, a2, b1, b2 in matcher.get_opcodes():
        if tag == "equal":
            continue
        # Adjacent differences are one change: a region replacing template lines can come out
        # as a deletion followed by an insertion
        if hunks and hunks[-1][1] == a1 and hunks[-1][3] == b1:
            hunks[-1] = (hunks[-1][0], a2, hunks[-1][2], b2)
        else:
            hunks.append((a1, a2, b1, b2))
    for a1, a2, b1, b2 in hunks:
        removed = [line for line in template[a1:a2] if line.strip()]
        added = items[b1:b2]
        regions = [item for item in added if isinstance(item, Region)]
        written = [item for item in added if not isinstance(item, Region) and item.strip()]
        for line in written:
            problems.append(f"{name}: outside every region and not in the template here: {line.strip()}")
        # A region replaces the template's lines from the one it starts with to the one it ends with,
        # indentation included, as the guide tells the reader to replace that part; any other template
        # line missing here is one the reader keeps and guide.sh drops
        if removed and not (regions
                            and any(region.first_line == removed[0] for region in regions)
                            and regions[-1].last_line == removed[-1]):
            for line in removed:
                problems.append(f"{name}: the template's line is missing and no region replaces it: {line.strip()}")


def generate_templates(target):
    for project in PROJECTS:
        subprocess.run(
            ["dotnet", "new", "mvc", "-n", project, "-o", str(target / project), "--no-restore"],
            check=True, stdout=subprocess.DEVNULL)


def main():
    problems = []
    files = sorted(
        path for path in ROOT.rglob("*")
        if path.is_file() and path.relative_to(ROOT).as_posix() not in NOT_GUIDE_FILES)
    relative = {path.relative_to(ROOT).as_posix() for path in files}
    for missing in sorted(WHOLE_FILES - relative):
        problems.append(f"{missing}: listed as shown whole, but there is no such file")

    with tempfile.TemporaryDirectory() as temp:
        templates = Path(temp)
        generate_templates(templates)
        for path in files:
            name = path.relative_to(ROOT).as_posix()
            items = outline(path, problems)
            if name in WHOLE_FILES:
                continue
            template = templates / name
            compare_with_template(name, items, read_lines(template) if template.is_file() else [], problems)

    for problem in problems:
        print(f"check-regions: {problem}", file=sys.stderr)
    if problems:
        sys.exit(1)
    print("check-regions: the files the guide edits are the template plus their regions, the rest are shown whole")


main()
