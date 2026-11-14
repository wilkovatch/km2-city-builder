#!/usr/bin/env bash
# Makes the variant of an exported build that includes Python: a copy of the build with Python, pip and the base
# requirements (pythonInstaller/requirements.txt) in its "python" folder, where the program looks for its own Python,
# so that it does not have to download it on the first start.
# The Python versions are the ones the program downloads (pythonInstaller/installer.ps1 on Windows,
# PythonManager.portablePythonUrl on Linux), read from those files so that both variants behave the same.
# The notices required by the Python licenses (see the Third-Party Code wiki page) are added to License.txt.
# Usage: bundle-python.sh <windows|linux> <exported zip> <output zip>, from the root of the repository
set -euo pipefail

platform="$1"
src=$(realpath "$2")
dst=$(realpath -m "$3")
root=$(pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
export PYTHONDONTWRITEBYTECODE=1
requirements="$root/pythonInstaller/requirements.txt"
# the modules the program checks (one per line; the carriage returns of a Windows checkout are removed)
modules=$(tr -d '\r' < "$root/pythonInstaller/imports.txt" | grep -v '^\s*$')

case "$platform" in
windows)
    url=$(grep -oP '(?<=\$pythonUrl = ")[^"]+' pythonInstaller/installer.ps1)
    version=$(grep -oP '(?<=python-)[0-9.]+(?=-embed)' <<< "$url")
    mkdir -p "$work/python"
    curl -fsSL -o "$work/python.zip" "$url"
    unzip -q "$work/python.zip" -d "$work/python"
    rm "$work/python.zip"
    # as in installer.ps1: without the ._pth file the interpreter also uses Lib/site-packages, where pip and the packages go
    for pth in "$work"/python/python*._pth; do mv "$pth" "$pth.bak"; done
    # the bundled interpreter can't run here, so the Windows packages are installed by the Python of the runner
    python3 -m pip install --quiet --no-compile --disable-pip-version-check --only-binary=:all: \
        --platform win_amd64 --implementation cp --python-version "${version%.*}" \
        --target "$work/python/Lib/site-packages" pip -r "$requirements"
    rm -rf "$work/python/Lib/site-packages/bin"
    for module in $modules; do
        [ -d "$work/python/Lib/site-packages/$module" ] || { echo "::error::$module was not installed"; exit 1; }
    done
    licenses='python/LICENSE.txt'
    sitePackages='python/Lib/site-packages'
    changes='the python*._pth file is renamed to python*._pth.bak, so that the packages in Lib/site-packages are found;
pip and the packages listed in pythonInstaller/requirements.txt are added.'
    ;;
linux)
    url=$(grep -oP '(?<=portablePythonUrl = ")[^"]+' Scripts/PythonManager.cs)
    version=$(grep -oP '(?<=cpython-)[0-9.]+' <<< "$url")
    # the stripped build is the same build without the debug symbols, less than half the size
    curl -fsSL "${url/install_only.tar.gz/install_only_stripped.tar.gz}" | tar -xz -C "$work"
    # the licenses of the libraries built into Python are only in the full archive
    curl -fsSL "${url/install_only.tar.gz/debug-full.tar.zst}" | zstd -dc | tar -x -C "$work" python/licenses
    ls "$work"/python/licenses/LICENSE.*.txt > /dev/null
    # Berkeley DB (Sleepycat license, which requires offering its source code) is only linked into the dbm.ndbm
    # module, which neither the program nor the cores use, so both are left out
    rm -f "$work"/python/lib/python3.*/lib-dynload/_dbm.*.so "$work/python/licenses/LICENSE.bdb.txt"
    "$work/python/bin/python3" -m pip install --quiet --no-compile --disable-pip-version-check -r "$requirements"
    "$work/python/bin/python3" -c "$(sed 's/.*/import &/' <<< "$modules" | paste -sd ';')"
    minor=${version%.*}
    licenses="python/lib/python$minor/LICENSE.txt (Python) and python/licenses (the libraries built into it)"
    sitePackages="python/lib/python$minor/site-packages"
    changes='the dbm.ndbm module (lib-dynload/_dbm*.so, linked with Berkeley DB) and its license are removed;
the debug symbols are stripped (as in the install_only_stripped build of python-build-standalone);
the packages listed in pythonInstaller/requirements.txt are added.'
    ;;
*)
    echo "Unknown platform: $platform"
    exit 1
    ;;
esac

# the notice, with the same heading as the others in License.txt (see PostprocessBuild.ProcessLicense)
heading=' The below notice is for "Python" '
pad=$(( (90 - ${#heading}) / 2 ))
(( pad < 2 )) && pad=2
bar=$(printf '=%.0s' $(seq "$pad"))
mkdir -p "$work/license"
unzip -q "$src" License.txt -d "$work/license"
cat >> "$work/license/License.txt" << EOF

$bar$heading$bar

This build includes Python $version in the "python" folder, from $url,
with pip and the packages listed in pythonInstaller/requirements.txt.
Python is licensed under the Python Software Foundation License Version 2. Its license and the licenses of the
libraries built into it are in $licenses.
The licenses of pip and of the other packages are in their *.dist-info folders in $sitePackages.

Summary of the changes made to Python:
$changes

EOF

cp "$src" "$dst"
(cd "$work" && zip -qry9 "$dst" python)
(cd "$work/license" && zip -q "$dst" License.txt)
echo "Created $dst with Python $version"
