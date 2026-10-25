#!/bin/bash
# Copy every bike's model and audio from assets/ into Assets/Bikes/<key>/, and every prop's model (the saddlebag) into
# Assets/Props/<key>/. Run before building.
set -e
cd "$(dirname "$0")"; R=../..
for layout in "$R"/assets/models/*/unity/*_layout.json; do
  key=$(basename "$(dirname "$(dirname "$layout")")")
  if grep -q '"kind": "prop"' "$layout"; then
    mkdir -p "Assets/Props/$key"
    cp "$R/assets/models/$key/unity/$key.fbx" "Assets/Props/$key/$key.fbx"
    echo "synced prop $key"
    continue
  fi
  mkdir -p "Assets/Bikes/$key/Audio" "Assets/Bikes/$key/Icons"
  cp "$R/assets/models/$key/unity/$key.fbx" "Assets/Bikes/$key/$key.fbx"
  if [ -d "$R/assets/audio/$key" ]; then rm -f "Assets/Bikes/$key/Audio/"*.wav; cp "$R/assets/audio/$key/"*.wav "Assets/Bikes/$key/Audio/"; fi
  echo "synced $key ($(ls "Assets/Bikes/$key/Audio" 2>/dev/null | wc -l | tr -d ' ') clips)"
done
