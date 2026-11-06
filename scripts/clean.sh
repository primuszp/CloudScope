#!/bin/bash
set -euo pipefail

dry_run=false
case "${1:-}" in
  "") ;;
  --dry-run) dry_run=true ;;
  --help|-h)
    printf 'Usage: scripts/clean.sh [--dry-run]\nRemoves project bin/obj directories and OS metadata. Keeps packaged apps and IDE preferences.\n'
    exit 0 ;;
  *) printf 'Unknown option: %s\n' "$1" >&2; exit 1 ;;
esac
if [[ $# -gt 1 ]]; then
  printf 'Usage: scripts/clean.sh [--dry-run]\n' >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
prefix="$(git -C "$repo_root" rev-parse --show-prefix)"
inside_worktree="$(git -C "$repo_root" rev-parse --is-inside-work-tree)"
if [[ -n "$prefix" || "$inside_worktree" != true ]]; then
  printf 'The cleanup script must belong to the repository root.\n' >&2
  exit 1
fi
if [[ -L "$repo_root/Source" ]]; then
  printf 'Refusing to clean through a symlinked Source directory.\n' >&2
  exit 1
fi

remove_generated() {
  local path="$1"
  local relative="${path#"$repo_root/"}"
  local tracked
  [[ -e "$path" || -L "$path" ]] || return 0
  tracked="$(git -C "$repo_root" --literal-pathspecs ls-files -- "$relative")"
  if [[ -n "$tracked" ]]; then
    printf 'Refusing to remove tracked files: %s\n' "$relative" >&2
    exit 1
  fi
  printf '%s\n' "$relative"
  if ! $dry_run; then rm -rf -- "$path"; fi
}

# Only direct project output directories: no traversal into nested source/vendor trees.
for project in "$repo_root"/Source/*/*.csproj; do
  [[ -f "$project" ]] || continue
  [[ ! -L "${project%/*}" ]] || continue
  remove_generated "${project%/*}/bin"
  remove_generated "${project%/*}/obj"
done

# Do not traverse Git internals or packaged applications, including the running app.
while IFS= read -r -d '' path; do
  remove_generated "$path"
done < <(find "$repo_root" -type d \( -name .git -o -path "$repo_root/artifacts" \) -prune -o \
  -type f \( -name .DS_Store -o -name Thumbs.db \) -print0)
