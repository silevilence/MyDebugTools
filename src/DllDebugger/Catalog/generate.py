"""Regenerate the embedded index from Windows SDK 10.0.26100.0 um headers."""
import re
import sys
from pathlib import Path

headers = {
    "kernel32": "processthreadsapi synchapi sysinfoapi fileapi handleapi memoryapi timezoneapi "
                "profileapi consoleapi consoleapi2 errhandlingapi processtopologyapi stringapiset "
                "namedpipeapi ioapiset libloaderapi heapapi",
    "user32": "winuser",
    "gdi32": "wingdi",
}
# Only accept plain WINAPI prototypes. Skip conditional argument lists, nested
# declarators and SAL expressions that we cannot strip without losing fields.
prototype = re.compile(
    r"\b(?:WINBASEAPI|WINUSERAPI|WINGDIAPI|WINAPI)\s+(DECLSPEC_NORETURN\s+)?"
    r"(\w+)\s+(?:WINAPI|APIENTRY)\s+(\w+)\s*\(([^;{}#]*)\)\s*;"
)
rows = {}
for module, names in headers.items():
    for header in names.split():
        source = (Path(sys.argv[1]) / (header + ".h")).read_text(encoding="utf-8-sig", errors="replace")
        source = re.sub(r"/\*.*?\*/|//[^\n]*", " ", source, flags=re.S)
        for noreturn, result, name, args in prototype.findall(source):
            # Reduce balanced SAL arguments from the inside out.
            args = re.sub(r"(_\w+_)\((?:[^()]|\([^()]*\))*\)", r"\1", args)
            fields = []
            for arg in [] if args.strip().lower() in ("void", "") else args.split(","):
                direction = "inout" if "_Inout" in arg else "out" if "_Out" in arg else "in"
                clean = re.sub(r"\b_\w+_\b|\b(?:CONST|const|volatile)\b", " ", arg).strip()
                m = re.fullmatch(r"(\w+(?:\s*\*)*)\s*(?<=\*)\s*(\w+)|(\w+)\s+(\w+)", clean)
                if not m:
                    break
                t, field = (m[1], m[2]) if m[1] else (m[3], m[4])
                fields.append(":".join((field, t.replace(" ", ""), direction)))
            else:
                rows[module, name] = "\t".join((module, header + ".h", name, result,
                                                "noreturn" if noreturn else "-", ",".join(fields) or "-"))
out = Path(__file__).with_name("windows-sdk.tsv")
out.write_bytes(("\n".join(rows[key] for key in sorted(rows)) + "\n").encode())
print(f"{len(rows)} definitions")
