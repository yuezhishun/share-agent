#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APPS_DIR="$REPO_ROOT/apps"
DRY_RUN=false

usage() {
  cat <<'EOF'
Usage:
  clean_apps_build_artifacts.sh [apps_dir] [--dry-run]

Examples:
  clean_apps_build_artifacts.sh
  clean_apps_build_artifacts.sh ../apps
  clean_apps_build_artifacts.sh /home/yueyuan/pty-agent/apps --dry-run
EOF
}

for arg in "$@"; do
  case "$arg" in
    --dry-run|-n)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      if [[ "$APPS_DIR" == "$REPO_ROOT/apps" ]]; then
        APPS_DIR="$arg"
      else
        echo "Unknown argument: $arg" >&2
        usage
        exit 1
      fi
      ;;
  esac
done

if [[ ! -d "$APPS_DIR" ]]; then
  echo "Directory not found: $APPS_DIR" >&2
  exit 1
fi

echo "Scanning: $APPS_DIR"
echo "Targets: bin, obj, node_modules"
if [[ "$DRY_RUN" == true ]]; then
  echo "Mode: dry-run (no files will be deleted)"
fi

mapfile -d '' TARGETS < <(
  find "$APPS_DIR" -type d \
    \( -name bin -o -name obj -o -name node_modules \) \
    -prune -print0
)

if [[ "${#TARGETS[@]}" -eq 0 ]]; then
  echo "No build artifact directories found."
  exit 0
fi

for dir in "${TARGETS[@]}"; do
  echo "Delete: $dir"
  if [[ "$DRY_RUN" == false ]]; then
    rm -rf "$dir"
  fi
done

if [[ "$DRY_RUN" == true ]]; then
  echo "Dry-run completed. Found ${#TARGETS[@]} directories."
else
  echo "Done. Deleted ${#TARGETS[@]} directories."
fi
