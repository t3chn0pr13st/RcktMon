#!/bin/bash
# Local build/install only: no account access, no service restarts and no public release.
set -euo pipefail
cd "$(dirname "$0")/../.."
case "$(uname -sm)" in
  "Darwin arm64") rid=osx-arm64 ;;
  "Darwin x86_64") rid=osx-x64 ;;
  *) echo "This installer is for macOS only" >&2; exit 1 ;;
esac
base="${1:-$HOME/Applications}"
app="$base/RocketMon Studio.app"
mkdir -p "$base"
if [[ -e "$app" && ! -f "$app/Contents/MacOS/rocketmon-studio.bundle" ]]; then
    echo "An unrelated application already exists at $app" >&2; exit 1
fi
stage="$(mktemp -d "$base/.rocketmon.XXXXXXXX")"
trap 'rm -rf "$stage"' EXIT
bundle="$stage/RocketMon Studio.app"
mkdir -p "$bundle/Contents/MacOS"
dotnet publish src/RcktMon.Desktop -c Release -r "$rid" --self-contained true -o "$bundle/Contents/MacOS" --nologo
git rev-parse HEAD > "$bundle/Contents/MacOS/rocketmon-studio.bundle"
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>RocketMon Studio</string>
<key>CFBundleDisplayName</key><string>RocketMon Studio</string>
<key>CFBundleIdentifier</key><string>ru.technopriest.rcktmon.studio</string>
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
