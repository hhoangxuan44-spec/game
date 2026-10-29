#!/usr/bin/env python3
"""Copy an APK, swapping one entry and dropping the old signature (META-INF/*.SF|*.RSA|*.DSA|*.EC|MANIFEST.MF).

usage: repack.py <in.apk> <out.apk> <entry-name> <replacement-file>
"""
import sys
import zipfile

SIG_SUFFIXES = (".SF", ".RSA", ".DSA", ".EC")


def main(src, dst, entry, repl):
    with open(repl, "rb") as f:
        new = f.read()
    seen = False
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w") as zout:
        for info in zin.infolist():
            name = info.filename
            if name.startswith("META-INF/") and (name.endswith(SIG_SUFFIXES) or name == "META-INF/MANIFEST.MF"):
                continue
            data = new if name == entry else zin.read(name)
            seen = seen or name == entry
            out = zipfile.ZipInfo(name, date_time=info.date_time)
            out.compress_type = info.compress_type
            out.external_attr = info.external_attr
            zout.writestr(out, data)
    if not seen:
        sys.exit("entry not found in apk: " + entry)


if __name__ == "__main__":
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    main(*sys.argv[1:])
