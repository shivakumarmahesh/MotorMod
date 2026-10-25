#!/bin/bash
# Harness launcher for Claude's terminal use. Starts Valheim in harness mode (sandboxed save folder, throwaway
# character, local Harness world, small window, hands-off), waits until the mod reports ready, and restores the
# window preferences when the game exits so your next normal launch is unchanged.
#   harness.sh start | stop | status | log | restore-prefs      (HARNESS_ARGS="-batchmode" start  hides the window, but then the rider renders stale: bikes only)
V="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
P="$V/BepInEx/plugins/MotorMod/harness"
PREFS=com.coffeestain.valheim-steam
SNAP="$P/screen_prefs.snapshot"
KEYS=("Screenmanager Fullscreen mode" "Screenmanager Resolution Width" "Screenmanager Resolution Height" "Screenmanager Resolution Use Native")
case "$1" in
  start)
    if pgrep -f MacOS/Valheim >/dev/null; then echo "Valheim is already running"; exit 1; fi
    if ! pgrep -x steam_osx >/dev/null; then echo "Steam is not running; start Steam first (the game quits without it)"; exit 1; fi
    mkdir -p "$P"; rm -f "$P/ready.json" "$P/done.json" "$P/request.json"
    # remember your window settings, but not while they still hold the harness size from a session that has not restored them yet
    cur_w=$(/usr/bin/defaults read "$PREFS" "Screenmanager Resolution Width" 2>/dev/null)
    if [ "$cur_w" != "1280" ] || [ ! -s "$SNAP" ]; then
      : > "$SNAP"; for k in "${KEYS[@]}"; do v=$(/usr/bin/defaults read "$PREFS" "$k" 2>/dev/null); [ -n "$v" ] && printf '%s\t%s\n' "$k" "$v" >> "$SNAP"; done
    fi
    cd "$V" && rm -f BepInEx/LogOutput.log
    ( export SteamAppId=892970 SteamGameId=892970
      /usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh ./valheim.app/Contents/MacOS/Valheim -console -motorharness -screen-fullscreen 0 -screen-width 1280 -screen-height 800 ${HARNESS_ARGS-} >/dev/null 2>&1
      "$0" restore-prefs ) >/dev/null 2>&1 &
    disown
    echo "started $(date '+%T'); waiting for $P/ready.json"
    for i in $(seq 1 150); do
      [ -f "$P/ready.json" ] && { echo "ready after $((i*2)) s: $(cat "$P/ready.json")"; exit 0; }
      pgrep -f MacOS/Valheim >/dev/null || { echo "the game exited before it was ready; see BepInEx/LogOutput.log"; exit 1; }
      sleep 2
    done
    echo "not ready after 300 s; check $V/BepInEx/LogOutput.log"; exit 1 ;;
  stop)
    pgrep -f MacOS/Valheim >/dev/null || { echo "not running"; exit 0; }
    echo '{"quit":true}' > "$P/request.json"
    for i in $(seq 1 10); do pgrep -f MacOS/Valheim >/dev/null || { "$0" restore-prefs; echo "stopped"; exit 0; }; sleep 2; done
    pkill -f MacOS/Valheim; sleep 2; "$0" restore-prefs; echo "killed" ;;
  restore-prefs)
    [ -f "$SNAP" ] || exit 0
    while IFS=$'\t' read -r k v; do [ -n "$k" ] && [ -n "$v" ] && /usr/bin/defaults write "$PREFS" "$k" -int "$v"; done < "$SNAP"
    echo "window preferences restored" ;;
  status)
    pgrep -f MacOS/Valheim >/dev/null && echo "running" || echo "not running"
    [ -f "$P/ready.json" ] && cat "$P/ready.json"
    [ -f "$P/done.json" ] && tail -n 20 "$P/done.json" ;;
  log) grep -n "Harness\|HARNESS\|Motorcycles\|Exception\|Error" "$V/BepInEx/LogOutput.log" | tail -n 40 ;;
  *) echo "usage: harness.sh start|stop|status|log|restore-prefs"; exit 1 ;;
esac
