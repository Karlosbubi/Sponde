#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 3 ]]; then echo 'Usage: build-rpm.sh <published-linux-folder> <version> <output-folder>' >&2; exit 2; fi
payload=$(realpath "$1")
version=$2
output=$(realpath -m "$3")
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid version' >&2; exit 2; }
[[ -f "$payload/Sponde" ]] || { echo 'Published Linux executable missing' >&2; exit 2; }
if find "$payload" -regextype posix-extended -type f -iregex '.*\.(pdf|xlsx|spondebackup|sqlite3?(-wal|-shm)?)' -print -quit | grep -q .; then echo 'Payload contains private data' >&2; exit 1; fi
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/../../.." && pwd)
work=$(mktemp -d /tmp/sponde-rpm.XXXXXXXX)
trap 'rm -rf -- "$work"' EXIT
source_dir="$work/SOURCES/sponde-$version"
mkdir -p "$source_dir/app" "$work/BUILD" "$work/BUILDROOT" "$work/RPMS" "$work/SPECS" "$work/SRPMS" "$output"
cp -a "$payload/." "$source_dir/app/"
cp "$repo_root/LICENSE" "$source_dir/LICENSE"
cp "$script_dir/sponde.desktop" "$source_dir/"
cp "$script_dir/../assets/sponde.svg" "$script_dir/../assets/sponde.png" "$source_dir/"
printf '%s\n' '#!/usr/bin/env bash' 'exec /usr/lib64/sponde/Sponde "$@"' > "$source_dir/sponde-launcher"
tar -C "$work/SOURCES" -czf "$work/SOURCES/sponde-$version.tar.gz" "sponde-$version"
rpmbuild -bb "$script_dir/sponde.spec" --define "_topdir $work" --define "app_version $version"
find "$work/RPMS" -name '*.rpm' -exec cp -- {} "$output/" \;
