#!/bin/sh
# Builds the universal (Apple silicon + Intel) macOS app and packs it into a DMG.
#
#   tools/package-mac.sh                      unsigned (ad hoc), for your own Mac
#   SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" tools/package-mac.sh
#   SIGN_IDENTITY=... NOTARY_PROFILE=notary tools/package-mac.sh
#
# NOTARY_PROFILE names credentials stored with `xcrun notarytool store-credentials`.
# Output: artifacts/OfficeAssistant.dmg
set -eu

cd "$(dirname "$0")/.."
project=src/OfficeAssistant.Mac/OfficeAssistant.Mac.csproj
app="src/OfficeAssistant.Mac/bin/Release/net10.0-macos/Office Assistant.app"
out=artifacts
dmg="$out/OfficeAssistant.dmg"

if [ -n "${SIGN_IDENTITY:-}" ]; then
    dotnet publish "$project" -c Release \
        -p:EnableCodeSigning=true \
        -p:CodesignKey="$SIGN_IDENTITY" \
        -p:UseHardenedRuntime=true \
        -p:CodesignEntitlements=Entitlements.plist
else
    dotnet publish "$project" -c Release
fi

staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"

mkdir -p "$out"
rm -f "$dmg"
hdiutil create -volname "Office Assistant" -srcfolder "$staging" -format UDZO -quiet "$dmg"

if [ -n "${SIGN_IDENTITY:-}" ]; then
    codesign --sign "$SIGN_IDENTITY" --timestamp "$dmg"
    if [ -n "${NOTARY_PROFILE:-}" ]; then
        xcrun notarytool submit "$dmg" --keychain-profile "$NOTARY_PROFILE" --wait
        xcrun stapler staple "$dmg"
    fi
fi

echo "Created $dmg"
