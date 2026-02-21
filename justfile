# justfile for docker-rep2-win

# Windows の場合は PowerShell を使用
set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

version := "0.0.0-dev"
PROJECT := "docker-rep2-win/docker-rep2-win.csproj"
SETUP_PROJECT := "docker-rep2-win-setup/docker-rep2-win-setup.csproj"

default:
    @just build

list:
    @just --list

build rid="win-x64":
    dotnet build {{PROJECT}} -r {{rid}} --no-self-contained /p:Version={{version}}

clean:
    @echo "Cleaning build artifacts..."
    {{ if os() == "windows" { \
        "if (Test-Path publish) { Remove-Item -Recurse -Force publish }; " + \
        "Get-ChildItem -Path . -Include bin,obj -Recurse | Remove-Item -Recurse -Force" \
    } else { \
        "rm -rf publish; find . -type d -name 'bin' -or -name 'obj' | xargs rm -rf" \
    } }}
    @echo "Done."

run:
    dotnet run --project {{PROJECT}}

publish version=version:
    @just _publish win-x64 {{version}}
    @just _publish win-arm64 {{version}}
    @echo "Done! Native AOT setup binaries are in publish/win-x64 and publish/win-arm64"

publish-win-x64 version=version:
    @just _publish win-x64 {{version}}

_publish arch version:
    @echo "--- Building {{arch}} (v{{version}}) ---"
    dotnet publish {{PROJECT}} -c Release -r {{arch}} --self-contained false -o publish/temp-{{arch}} /p:Version={{version}} /p:PublishReadyToRun=true /p:PublishSingleFile=false
    
    @echo "--- Removing debug symbols (.pdb) ---"
    {{ if os() == "windows" { \
        "Get-ChildItem -Path publish/temp-" + arch + " -Filter *.pdb | Remove-Item -Force" \
    } else { \
        "rm -f publish/temp-" + arch + "/*.pdb" \
    } }}

    @echo "--- Zipping payload into publish directory ---"
    {{ if os() == "windows" { \
        "if (-not (Test-Path publish)) { New-Item -ItemType Directory -Path publish }; " + \
        "Compress-Archive -Path publish/temp-" + arch + "/* -DestinationPath (Join-Path (Get-Location) 'publish/payload.zip') -Force" \
    } else { \
        "(cd publish/temp-" + arch + " && zip -r ../payload.zip .)" \
    } }}
    
    @echo "--- Building Setup Wrapper ---"
    dotnet publish {{SETUP_PROJECT}} -c Release -r {{arch}} -p:PublishAot=true -o publish/{{arch}} /p:Version={{version}}
    
    @echo "--- Cleaning up ---"
    {{ if os() == "windows" { \
        "Remove-Item publish/temp-" + arch + " -Recurse -Force; if (Test-Path publish/payload.zip) { Remove-Item publish/payload.zip -Force }" \
    } else { \
        "rm -rf publish/temp-" + arch + " publish/payload.zip" \
    } }}

eol-check:
    #!/usr/bin/env python3
    import os
    exts = (".cs", ".xaml", ".csproj", ".sln", ".json", ".manifest", ".txt", ".md", ".editorconfig", ".gitignore", ".gitattributes")
    for r, ds, fs in os.walk("."):
        for d in [".git", "obj", "bin", ".vs"]:
            if d in ds: ds.remove(d)
        for f in fs:
            if f.endswith(exts) or f == "justfile":
                p = os.path.join(r, f)
                try:
                    with open(p, "rb") as file:
                        c = file.read()
                        res = None
                        if b"\n" in c and b"\r\n" not in c: res = "LF only"
                        elif b"\n" in c and c.count(b"\n") > c.count(b"\r\n"): res = "Mixed"
                        if res: print(f"{res}: {p}")
                except: pass

eol-fix:
    #!/usr/bin/env python3
    import os
    exts = (".cs", ".xaml", ".csproj", ".sln", ".json", ".manifest", ".txt", ".md", ".editorconfig", ".gitignore", ".gitattributes")
    for r, ds, fs in os.walk("."):
        for d in [".git", "obj", "bin", ".vs"]:
            if d in ds: ds.remove(d)
        for f in fs:
            if f.endswith(exts) or f == "justfile":
                p = os.path.join(r, f)
                try:
                    with open(p, "rb") as file: c = file.read()
                    if b"\n" in c:
                        nc = c.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
                        if nc != c:
                            with open(p, "wb") as file: file.write(nc)
                            print(f"Fixed: {p}")
                except: pass
