#!/usr/bin/env bash
# Relive:GO (RL:GO) Official Linux Launcher & Auto-Updater for CS:GO Legacy (AppID 4465480)
set -euo pipefail

MANIFEST_URL="https://raw.githubusercontent.com/nat649/relive-go-launcher/main/manifest-linux.json"
GC_URL="https://rlgo.specs.qzz.io"
CONFIG_FILE="${XDG_CONFIG_HOME:-$HOME/.config}/relive-go/launcher.conf"
mkdir -p "$(dirname "$CONFIG_FILE")"

echo "=========================================="
echo "   RELIVE:GO (RL:GO) - Linux Launcher"
echo "=========================================="

# Live server status
HEALTH=$(curl -fsS --max-time 4 "${GC_URL}/api/health" 2>/dev/null || true)
if [ -n "$HEALTH" ]; then
  echo "[OK] Serveur central Relive:GO en ligne (${GC_URL})"
else
  echo "[!] Serveur central Relive:GO injoignable (mode hors-ligne)"
fi

# Auto-detect CS:GO Legacy folder
detect_csgo_dir() {
  local candidates=(
    "$HOME/.local/share/Steam"
    "$HOME/.steam/steam"
    "$HOME/.steam/debian-installation"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
  )
  local libs=()
  for s in "${candidates[@]}"; do
    [ -d "$s" ] && libs+=("$s")
    if [ -f "$s/steamapps/libraryfolders.vdf" ]; then
      while IFS= read -r p; do
        [ -d "$p" ] && libs+=("$p")
      done < <(grep -oP '"path"\s+"\K[^"]+' "$s/steamapps/libraryfolders.vdf" 2>/dev/null || true)
    fi
  done

  for lib in "${libs[@]}"; do
    for name in "csgo legacy" "Counter-Strike Global Offensive"; do
      if [ -f "$lib/steamapps/common/$name/csgo/steam.inf" ]; then
        echo "$lib/steamapps/common/$name"
        return 0
      fi
    done
  done
  return 1
}

GAME_DIR=""
if [ -f "$CONFIG_FILE" ]; then
  GAME_DIR=$(grep -E '^game_dir=' "$CONFIG_FILE" | head -n1 | cut -d= -f2- || true)
fi

if [ -z "$GAME_DIR" ] || [ ! -f "$GAME_DIR/csgo/steam.inf" ]; then
  GAME_DIR=$(detect_csgo_dir || true)
fi

if [ -z "$GAME_DIR" ] || [ ! -f "$GAME_DIR/csgo/steam.inf" ]; then
  read -r -p "Chemin complet du dossier CS:GO Legacy (ex: ~/.local/share/Steam/steamapps/common/csgo legacy): " GAME_DIR
  GAME_DIR="${GAME_DIR/#\~/$HOME}"
fi

if [ ! -f "$GAME_DIR/csgo/steam.inf" ]; then
  echo "[ERREUR] Dossier CS:GO Legacy invalide : $GAME_DIR"
  exit 1
fi

echo "game_dir=$GAME_DIR" > "$CONFIG_FILE"
echo "[OK] Dossier CS:GO Legacy : $GAME_DIR"

# Fetch manifest-linux.json and verify SHA-256
echo "[...] Vérification des mises à jour sur GitHub..."
MANIFEST=$(curl -fsSL "${MANIFEST_URL}?t=$(date +%s)")

python3 - "$GAME_DIR" "$MANIFEST" <<'PYEOF'
import hashlib, json, os, shutil, sys, urllib.request

game_dir = sys.argv[1]
manifest = json.loads(sys.argv[2])

def sha256_file(path):
    if not os.path.isfile(path):
        return ""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest().lower()

outdated = []
for f in manifest.get("files", []):
    dest = os.path.join(game_dir, f["path"])
    if sha256_file(dest) != f["sha256"].lower():
        outdated.append(f)

if not outdated:
    print(f"[OK] Jeu à jour (v{manifest.get('version')}) — SHA-256 vérifié !")
    sys.exit(0)

print(f"[MAJ] Mise à jour v{manifest.get('version')} ({len(outdated)} fichier(s))...")
for idx, f in enumerate(outdated, 1):
    dest = os.path.join(game_dir, f["path"])
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if f.get("backup") and os.path.isfile(dest) and not os.path.isfile(dest + ".bak"):
        shutil.copy2(dest, dest + ".bak")
        print(f"  [BACKUP] {f['path']}.bak créé")
    print(f"  [{idx}/{len(outdated)}] Téléchargement de {f['path']}...")
    tmp = dest + ".dl"
    req = urllib.request.Request(f["url"], headers={"User-Agent": "ReliveGOLauncher-Linux/1.0"})
    with urllib.request.urlopen(req) as resp, open(tmp, "wb") as out:
        shutil.copyfileobj(resp, out)
    if sha256_file(tmp) != f["sha256"].lower():
        os.remove(tmp)
        raise SystemExit(f"[ERREUR] Hash SHA-256 invalide pour {f['path']}")
    os.replace(tmp, dest)
    if f.get("executable"):
        os.chmod(dest, 0o755)

print("[OK] Mise à jour terminée !")
PYEOF

chmod +x "$GAME_DIR/csgo_linux64" 2>/dev/null || true
echo "[GO] Lancement de Relive:GO (CS:GO Legacy Linux)..."
cd "$GAME_DIR"
exec ./csgo.sh -steam -novid -tickrate 128 "$@"
