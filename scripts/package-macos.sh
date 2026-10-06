#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/macos/$RID"
STAGE="$OUT/stage"
APP="$STAGE/PaperLike.app"
rm -rf "$OUT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
DOTNET="$(command -v dotnet || true)"
if [[ -z "$DOTNET" && -x "$ROOT/../work/dotnet/dotnet" ]]; then DOTNET="$ROOT/../work/dotnet/dotnet"; fi
if [[ -z "$DOTNET" ]]; then echo "Install the .NET 10 SDK first." >&2; exit 1; fi
"$DOTNET" publish "$ROOT/PaperLike.csproj" -c Release -r "$RID" --self-contained true -p:UseAppHost=true -o "$APP/Contents/MacOS"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>PaperLike</string>
<key>CFBundleDisplayName</key><string>PaperLike</string>
<key>CFBundleIdentifier</key><string>org.paperlike.reader</string>
<key>CFBundleVersion</key><string>0.1.0</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>CFBundleExecutable</key><string>PaperLike</string>
</dict></plist>
PLIST
cat > "$STAGE/Install PaperLike.command" <<'INSTALL'
#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SOURCE="$HERE/PaperLike.app"
TARGET="$HOME/Applications/PaperLike.app"
mkdir -p "$HOME/Applications" "$HOME/Library/Application Support/PaperLike/Books"
if [[ -d "$TARGET" ]]; then
  BACKUP="$HOME/Applications/PaperLike backup $(date +%Y%m%d-%H%M%S).app"
  mv "$TARGET" "$BACKUP"
fi
cp -R "$SOURCE" "$TARGET"
open "$TARGET"
INSTALL
chmod +x "$STAGE/Install PaperLike.command"
cat > "$STAGE/Read Me.txt" <<'TXT'
PaperLike for macOS

Open “Install PaperLike.command” to install the app for your account.
The app goes in ~/Applications. Your EPUB library and progress are saved in:
~/Library/Application Support/PaperLike/

This early build is not signed or notarized. macOS may ask you to confirm that
it is allowed to open. This package is for evaluation, not broad public release.
TXT
# Ad-hoc signature makes the local bundle structurally valid; it is not Developer ID signing/notarization.
codesign --force --deep --sign - "$APP" || true
hdiutil create -volname "PaperLike" -srcfolder "$STAGE" -ov -format UDZO "$OUT/PaperLike-macOS-$RID.dmg"
printf 'Created %s\n' "$OUT/PaperLike-macOS-$RID.dmg"
