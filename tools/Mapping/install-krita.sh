#!/bin/sh
set -e
src="$(cd "$(dirname "$0")/Krita" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}/krita"
pykrita="$data/pykrita"
templates="$data/templates"
actions="$data/actions"
mkdir -p "$pykrita" "$templates" "$actions"

rm -rf "$pykrita/mortz" "$pykrita/mortz.desktop" "$templates/mortz"
cp -r "$src/mortz" "$src/mortz.desktop" "$pykrita/"
cp -r "$src/templates/mortz" "$templates/"
cp "$src/mortz.action" "$actions/"

echo "installed the Mortz plug-in into $pykrita and the map template into $templates"
echo "restart Krita, then enable \"Mortz map tools\" in Settings > Configure Krita > Python Plugin Manager"
echo "and show the docker with Settings > Dockers > Mortz map"

if ! python3 -c "import PyQt6.QtCore" 2>/dev/null; then
    echo "warning: PyQt6 is missing, so Krita will not load Python plug-ins (Arch: sudo pacman -S python-pyqt6)"
fi
