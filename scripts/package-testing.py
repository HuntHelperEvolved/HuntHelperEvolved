#!/usr/bin/env python3
"""Create and check the testing ZIP. Never include symbols or local settings."""
import json
import pathlib
import sys
import zipfile
root=pathlib.Path(__file__).resolve().parents[1]
source=pathlib.Path(sys.argv[1])
output=pathlib.Path(sys.argv[2])
allowed={"HuntHelperEvolved.dll", "HuntHelperEvolved.json", "HuntHelperEvolved.deps.json", "KamiToolKit.dll", "System.Speech.dll", "runtimes/win/lib/net9.0/System.Speech.dll"}
with zipfile.ZipFile(source) as src:
    names=set(src.namelist())
    assert allowed.issubset(names), "Required runtime file missing"
    with zipfile.ZipFile(output,"w",zipfile.ZIP_DEFLATED) as dst:
        for name in sorted(allowed):
            data=src.read(name)
            for encoding in ("utf-8","utf-16-le"):
                assert str(pathlib.Path.home()).encode(encoding) not in data, "Local build path in artifact"
            dst.writestr(name,data)
        for name in ("LICENSE","THIRD-PARTY-NOTICES.md"):
            dst.writestr(name,(root/name).read_bytes())
with zipfile.ZipFile(output) as artifact:
    manifest=json.loads(artifact.read("HuntHelperEvolved.json"))
    print("Verified testing package version",manifest["AssemblyVersion"],"with",len(artifact.namelist()),"files.")
