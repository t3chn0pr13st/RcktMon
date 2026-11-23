#!/bin/bash
# Local build/install only: no account access, no service restarts and no public release.
set -euo pipefail
cd "$(dirname "$0")/../.."
case "$(uname -sm)" in
  "Darwin arm64") rid=osx-arm64 ;;
  "Darwin x86_64") rid=osx-x64 ;;
  *) echo "This installer is for macOS only" >&2; exit 1 ;;
esac
base="$HOME/Applications"
studio_source=""
recordings=""
if [[ $# -gt 0 && "$1" != --* ]]; then base="$1"; shift; fi
while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-studio-source) studio_source="${2:?Expected path to the private server checkout}"; shift 2 ;;
        --recordings) recordings="${2:?Expected recordings root}"; shift 2 ;;
        *) echo "usage: $0 [install directory] [--with-studio-source SERVER_ROOT [--recordings RECORDINGS_ROOT]]" >&2; exit 2 ;;
    esac
done
if [[ -n "$studio_source" ]]; then
    studio_source="$(cd "$studio_source" && pwd -P)"
    studio_project="$studio_source/src/RcktMon.Server.SignalStudio/RcktMon.Server.SignalStudio.csproj"
    [[ -f "$studio_project" ]] || { echo "Missing SignalStudio project" >&2; exit 2; }
    if [[ -z "$recordings" && -d "$studio_source/artifacts" ]]; then recordings="$studio_source/artifacts"; fi
elif [[ -n "$recordings" ]]; then
    echo "--recordings requires --with-studio-source" >&2; exit 2
fi
if [[ -n "$recordings" ]]; then recordings="$(cd "$recordings" && pwd -P)"; fi
app="$base/RocketMon.app"
mkdir -p "$base"
if [[ -L "$app" || ( -e "$app" && ( ! -f "$app/Contents/MacOS/rocketmon-desktop.bundle" || "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist" 2>/dev/null || :)" != ru.technopriest.rcktmon ) ) ]]; then
    echo "An unrelated application already exists at $app" >&2; exit 1
fi
if [[ -x "$app/Contents/MacOS/RcktMon" ]] && /usr/sbin/lsof -t "$app/Contents/MacOS/RcktMon" >/dev/null 2>&1; then
    echo "Quit RocketMon yourself before replacing its application bundle." >&2; exit 1
fi
stage="$(mktemp -d "$base/.rocketmon.XXXXXXXX")"
trap 'rm -rf "$stage"' EXIT
bundle="$stage/RocketMon.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
icons="$stage/RocketMon.iconset"
mkdir "$icons"
sips -s format png tools/macos/RocketMon.svg --out "$stage/master.png" >/dev/null
for n in 16 32 128 256 512; do
    sips -z "$n" "$n" "$stage/master.png" --out "$icons/icon_${n}x${n}.png" >/dev/null
    sips -z "$((2*n))" "$((2*n))" "$stage/master.png" --out "$icons/icon_${n}x${n}@2x.png" >/dev/null
done
iconutil -c icns "$icons" -o "$bundle/Contents/Resources/RocketMon.icns"
dotnet publish src/RcktMon.Desktop -c Release -r "$rid" --self-contained true -o "$bundle/Contents/MacOS" --nologo
if [[ -n "$studio_source" ]]; then
    helper="$bundle/Contents/Helpers/SignalStudio"
    (cd "$studio_source" && dotnet publish "$studio_project" -c Release -r "$rid" --self-contained true -p:UseAppHost=true -o "$helper" --nologo)
    [[ -x "$helper/RcktMon.Server.SignalStudio" && -f "$helper/wwwroot/index.html" ]] || { echo "Incomplete market-data helper" >&2; exit 1; }
    # Local bundle only: path to archived data, never credentials or a copy of the recordings.
    [[ -z "$recordings" ]] || printf '%s\n' "$recordings" > "$bundle/Contents/Resources/studio-recordings.path"
fi
git rev-parse HEAD > "$bundle/Contents/MacOS/rocketmon-desktop.bundle"
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>RocketMon</string>
<key>CFBundleDisplayName</key><string>RocketMon</string>
<key>CFBundleIdentifier</key><string>ru.technopriest.rcktmon</string>
<key>CFBundleIconFile</key><string>RocketMon.icns</string>
<key>CFBundleExecutable</key><string>RcktMon</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
plutil -lint "$bundle/Contents/Info.plist" >/dev/null
# The SDK apphost is already ad-hoc signed. This local bundle is not notarized.
if [[ -e "$app" ]]; then mv "$app" "$stage/old.app"; fi
if ! mv "$bundle" "$app"; then
    [[ ! -d "$stage/old.app" ]] || mv "$stage/old.app" "$app"
    exit 1
fi
echo "Installed: $app"
