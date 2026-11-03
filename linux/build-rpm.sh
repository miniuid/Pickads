#!/bin/sh
set -eu
cd "$(dirname "$0")"
project_root=$(cd .. && pwd)
build_root=$(mktemp -d)
trap 'rm -rf "$build_root"' EXIT HUP INT TERM
mkdir -p "$build_root/SOURCES/pickads-linux-0.1.0" "$build_root/SPECS"
cp -R pickads packaging assets tests README.md VALIDATION.md "$build_root/SOURCES/pickads-linux-0.1.0/"
cp "$project_root/LICENSE" "$build_root/SOURCES/pickads-linux-0.1.0/LICENSE"
find "$build_root/SOURCES/pickads-linux-0.1.0" -type d -name __pycache__ -exec rm -rf '{}' +
tar -C "$build_root/SOURCES" -czf "$build_root/SOURCES/pickads-linux-0.1.0.tar.gz" pickads-linux-0.1.0
cp packaging/pickads.spec "$build_root/SPECS/"
rpmbuild -ba --define "_topdir $build_root" "$build_root/SPECS/pickads.spec" "$@"
mkdir -p dist
cp "$build_root"/RPMS/noarch/*.rpm "$build_root"/SRPMS/*.rpm "$build_root/SOURCES/pickads-linux-0.1.0.tar.gz" dist/
(cd dist && sha256sum ./*.rpm ./*.tar.gz > SHA256SUMS)
