#!/bin/bash
set -euo pipefail
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packaging must run on macOS." >&2
  exit 1
fi
case "$(uname -m)" in
  arm64) runtime_id=osx-arm64 ;;
  x86_64) runtime_id=osx-x64 ;;
  *) echo "Unsupported macOS architecture." >&2; exit 1 ;;
esac
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
app_path="${1:-$repo_root/artifacts/macos/CloudScope.app}"
if [[ "$app_path" != *.app ]]; then
  echo "The output path must end in .app." >&2
  exit 1
fi
staging="$(mktemp -d "${TMPDIR:-/tmp}/cloudscope-package.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
app_bundle="$staging/CloudScope.app"
mkdir -p "$app_bundle/Contents/MacOS"
dotnet publish "$repo_root/Source/CloudScope.Avalonia/CloudScope.Avalonia.csproj" \
  -c Release -r "$runtime_id" --self-contained false --artifacts-path "$staging/build" \
  -p:SelfContained=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None -o "$staging/publish"
# The single-file host contains its managed/native dependencies. Keep auxiliary
# project-reference metadata as resources, rather than unsigned code in MacOS.
mv "$staging/publish/CloudScope.Avalonia" "$app_bundle/Contents/MacOS/"
mv "$staging/publish" "$app_bundle/Contents/Resources"
cp "$repo_root/Source/CloudScope.Avalonia/Assets/Info.plist" "$app_bundle/Contents/Info.plist"
plutil -lint "$app_bundle/Contents/Info.plist"
codesign --force --sign - "$app_bundle"
codesign --verify --strict "$app_bundle"
mkdir -p "$(dirname "$app_path")"
# Replace the generated bundle as a whole: merging would retain removed libraries
# and invalidate the signature after a dependency change.
if [[ -e "$app_path" ]]; then
  mv "$app_path" "$staging/previous.app"
fi
if ! ditto "$app_bundle" "$app_path"; then
  rm -rf "$app_path"
  if [[ -e "$staging/previous.app" ]]; then mv "$staging/previous.app" "$app_path"; fi
  exit 1
fi
codesign --verify --strict "$app_path"
printf 'Built %s\n' "$app_path"
