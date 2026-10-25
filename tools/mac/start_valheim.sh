#!/bin/bash
# Valheim launcher for Steam launch options: asks which way to start, every launch.
# Steam launch option:  /bin/bash ./start_valheim.sh %command%
# Steam on macOS may pass either valheim.app or valheim.app/Contents/MacOS/Valheim as $1; both are handled.
#   Vanilla          native game, no mods at all
#   Modded (no bike) BepInEx + Jotunn, the bike mod stays idle (for telling mod bugs from Mac/Rosetta bugs)
#   Modded           everything, in-game console on
cd "$(dirname "$0")"
LOG="$HOME/Library/Logs/valheim_launcher.log"
echo "$(date '+%F %T') args: $*" >> "$LOG"
choice=$(/usr/bin/osascript -e 'button returned of (display dialog "Start Valheim as:" buttons {"Vanilla", "Modded (no bike)", "Modded"} default button "Vanilla" with title "Valheim" giving up after 30)' 2>/dev/null)
# The harness (sandboxed photo session for Claude) is started from the terminal with tools/mac/harness.sh, never from here.
echo "$(date '+%F %T') choice: '$choice'" >> "$LOG"
case "$choice" in
  "Modded")           exec /usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh "$@" -console ;;
  "Modded (no bike)") exec /usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh "$@" -console -nomotormod ;;
esac
# Vanilla: resolve the real executable inside the .app if Steam gave us the bundle folder
exe="$1"; shift
case "$exe" in
  *.app|*.app/)
    exe="${exe%/}"
    inner=$(/usr/bin/defaults read "$exe/Contents/Info" CFBundleExecutable 2>/dev/null)
    exe="$exe/Contents/MacOS/${inner:-Valheim}"
    ;;
esac
echo "$(date '+%F %T') vanilla exec: $exe $*" >> "$LOG"
exec "$exe" "$@"
