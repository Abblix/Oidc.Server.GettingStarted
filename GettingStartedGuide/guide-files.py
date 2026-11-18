"""Puts the guide's files into a solution the way a reader does, and checks that they can be put there.

The guide writes some files whole (WHOLE_FILES) and edits others that `dotnet new mvc` generated
(EDITED_FILES). Its code blocks come from these files by region. A reader of an edited file keeps
the generated file and writes the guide's regions into it, so guide.sh does the same instead of
copying the stored file over the generated one.

A region is placed against the generated file. Starting at a generated line equal to its own first
line, it replaces the generated lines up to the one equal to its own last line; otherwise it is new
lines, put where it stands among the stored lines. Where the stored file and the generated one
differ with no region in between, the stored line is a stale copy of the template (a year in a
footer, a newer template) and the generated line stays, as it does for the reader.

Region markers: a line that, stripped of its indentation, is `// #region <name>`, `# #region <name>`
or `@* #region <name> *@` opens a region, and `// #endregion`, `# #endregion` or `@* #endregion *@`
closes the innermost open one. Whether the documentation site includes each region is not checked.

Usage:
  python guide-files.py check            fails when a region cannot be placed in today's template
  python guide-files.py apply <solution> writes the guide's files into a solution guide.sh created
Both need the .NET SDK the guide uses.
"""

import difflib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent

# Files the guide shows whole: the reader writes the file as shown, so it is copied as it is
WHOLE_FILES = {
    "OpenIDProviderApp/Program.cs",
    "OpenIDProviderApp/appsettings.json",
    "OpenIDProviderApp/TestUserStorage.cs",
    "OpenIDProviderApp/Controllers/AuthController.cs",
    "OpenIDProviderApp/Views/Auth/Login.cshtml",
    "TestClientApp/appsettings.json",
    "TestClientApp/appsettings.Development.json",
    "TestClientApp/Views/Home/Index.cshtml",
}

# Files the guide edits: the generated file with the guide's regions put in
EDITED_FILES = {
    "TestClientApp/Program.cs",
    "TestClientApp/Controllers/HomeController.cs",
    "TestClientApp/Views/Shared/_Layout.cshtml",
}

# Not files the guide writes: the script that follows the guide, and this one
NOT_GUIDE_FILES = {"guide.sh", "guide-files.py"}

PROJECTS = ("OpenIDProviderApp", "TestClientApp")

OPENERS = (("// #region ", ""), ("# #region ", ""), ("@* #region ", " *@"))
CLOSERS = ("// #endregion", "# #endregion", "@* #endregion *@")


class Region:
    """One top-level region of a stored file, standing in its place among the stored lines."""

    def __init__(self, name):
        self.name = name
        self.lines = []
        # Whether the stored file sets the region off by a blank line, kept when it is put in as new lines
        self.blank_before = False
        self.blank_after = False

    @property
    def first_line(self):
        return next(line for line in self.lines if line.strip())

    @property
    def last_line(self):
        return next(line for line in reversed(self.lines) if line.strip())

    def __eq__(self, other):
        return isinstance(other, Region) and other.name == self.name

    def __hash__(self):
        return hash(self.name)


def read_lines(path):
    lines = path.read_text(encoding="utf-8-sig").replace("\r\n", "\n").split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    return lines


def region_opened(stripped):
    for prefix, suffix in OPENERS:
        if stripped.startswith(prefix) and stripped.endswith(suffix):
            name = stripped[len(prefix):len(stripped) - len(suffix)].strip()
            if name:
                return name
    return None


def outline(name, lines, problems):
    """The stored lines outside every region, each top-level region as one Region in its place."""
    items, open_regions, names = [], [], set()
    for number, line in enumerate(lines, start=1):
        stripped = line.strip()
        opened = region_opened(stripped)
        if opened is not None:
            if opened in names:
                problems.append(f"{name}:{number}: region '{opened}' is opened twice")
            names.add(opened)
            if not open_regions:
                region = Region(opened)
                region.blank_before = bool(items) and isinstance(items[-1], str) and not items[-1].strip()
                items.append(region)
            open_regions.append(opened)
        elif stripped in CLOSERS:
            if not open_regions:
                problems.append(f"{name}:{number}: #endregion closes no region")
            else:
                open_regions.pop()
        elif open_regions:
            items[-1].lines.append(line)
        else:
            if items and isinstance(items[-1], Region):
                items[-1].blank_after = not stripped
            items.append(line)
    for opened in open_regions:
        problems.append(f"{name}: region '{opened}' is never closed")
    for item in items:
        if isinstance(item, Region) and not any(line.strip() for line in item.lines):
            problems.append(f"{name}: region '{item.name}' is empty")
    return items


def place(name, generated, stored, problems):
    """The generated file with the stored file's regions put in, as a reader of the guide does."""
    items = outline(name, stored, problems)
    if problems:
        return None
    # Blank lines are layout: matched, they would cut one replaced block into pieces
    template = [(index, line) for index, line in enumerate(generated) if line.strip()]
    template_lines = [line for _, line in template]
    sequence = [item for item in items if isinstance(item, Region) or item.strip()]

    hunks = []
    matcher = difflib.SequenceMatcher(a=template_lines, b=sequence, autojunk=False)
    for tag, a1, a2, b1, b2 in matcher.get_opcodes():
        if tag == "equal":
            continue
        # Adjacent differences are one change: a region replacing template lines can come out as
        # a deletion followed by an insertion
        if hunks and hunks[-1][1] == a1 and hunks[-1][3] == b1:
            hunks[-1] = (hunks[-1][0], a2, hunks[-1][2], b2)
        else:
            hunks.append((a1, a2, b1, b2))

    replacements = {}  # first generated index -> (last generated index, region)
    insertions = {}    # generated index the lines go before -> (lines, region)
    for a1, a2, b1, b2 in hunks:
        added = sequence[b1:b2]
        regions = [item for item in added if isinstance(item, Region)]
        if not regions:
            continue  # a stale copy of the template: the generated lines stay
        for line in added:
            if not isinstance(line, Region):
                problems.append(f"{name}: next to a region, outside it and not in the template: {line.strip()}")
        removed = template[a1:a2]
        position = 0
        for region in regions:
            starts = template_lines.count(region.first_line)
            if starts > 1:
                problems.append(f"{name}: region '{region.name}' starts at a line the template holds "
                                f"{starts} times: {region.first_line.strip()}")
                continue
            if position < len(removed) and removed[position][1] == region.first_line:
                ends = [offset for offset in range(position, len(removed))
                        if removed[offset][1] == region.last_line]
                if len(ends) != 1:
                    problems.append(f"{name}: region '{region.name}' must end at one template line after "
                                    f"its start, found {len(ends)}: {region.last_line.strip()}")
                    continue
                replacements[removed[position][0]] = (removed[ends[0]][0], region)
                position = ends[0] + 1
            elif starts == 1:
                problems.append(f"{name}: region '{region.name}' starts at a template line it does not "
                                f"replace in order: {region.first_line.strip()}")
            elif position < len(removed):
                insertions.setdefault(removed[position][0], []).append(region)
            else:
                after = removed[-1][0] if removed else (template[a1 - 1][0] if a1 > 0 else -1)
                insertions.setdefault(after + 1, []).append(region)
        for _, line in removed[position:]:
            problems.append(f"{name}: the template's line is missing and no region replaces it: {line.strip()}")

    if problems:
        return None
    result, index = [], 0
    while index <= len(generated):
        for region in insertions.get(index, []):
            if region.blank_before and result and result[-1].strip():
                result.append("")
            result.extend(region.lines)
            if region.blank_after and index < len(generated) and generated[index].strip():
                result.append("")
        if index == len(generated):
            break
        if index in replacements:
            last, region = replacements[index]
            result.extend(region.lines)
            index = last + 1
        else:
            result.append(generated[index])
            index += 1
    return result


def guide_files(problems):
    on_disk = {path.relative_to(ROOT).as_posix() for path in ROOT.rglob("*") if path.is_file()} - NOT_GUIDE_FILES
    for name in sorted(WHOLE_FILES & EDITED_FILES):
        problems.append(f"{name}: listed both as written whole and as edited")
    for name in sorted((WHOLE_FILES | EDITED_FILES) - on_disk):
        problems.append(f"{name}: listed, but there is no such file")
    for name in sorted(on_disk - WHOLE_FILES - EDITED_FILES):
        problems.append(f"{name}: neither written whole nor edited, so the guide cannot say what to do with it")


def generate_templates(target):
    for project in PROJECTS:
        subprocess.run(
            ["dotnet", "new", "mvc", "-n", project, "-o", str(target / project), "--no-restore"],
            check=True, stdout=subprocess.DEVNULL)


def put_files(solution, problems):
    """Writes the guide's files into a solution whose projects `dotnet new mvc` generated."""
    results = {}
    for name in sorted(EDITED_FILES):
        generated = solution / name
        if not generated.is_file():
            problems.append(f"{name}: the guide edits it, but dotnet new mvc generated no such file")
            continue
        results[name] = place(name, read_lines(generated), read_lines(ROOT / name), problems)
    return results


def report(problems, success):
    for problem in problems:
        print(f"guide-files: {problem}", file=sys.stderr)
    if problems:
        sys.exit(1)
    print(f"guide-files: {success}")


def main():
    problems = []
    guide_files(problems)
    command = sys.argv[1:2]
    if command == ["check"] and len(sys.argv) == 2:
        if not problems:
            with tempfile.TemporaryDirectory() as temp:
                generate_templates(Path(temp))
                put_files(Path(temp), problems)
        report(problems, "every region of the edited files has its place in today's template")
    elif command == ["apply"] and len(sys.argv) == 3:
        solution = Path(sys.argv[2])
        results = {} if problems else put_files(solution, problems)
        if not problems:
            for name in sorted(WHOLE_FILES):
                (solution / name).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / name, solution / name)
            for name, lines in results.items():
                (solution / name).write_text("\n".join(lines) + "\n", encoding="utf-8")
        report(problems, "the guide's files are in place")
    else:
        print(__doc__, file=sys.stderr)
        sys.exit(2)


main()
