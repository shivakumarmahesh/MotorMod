# The sea soundtrack

While you ride a bike on water, MotorMod plays a soundtrack, and everyone
riding near you hears the same song at the same moment. The music comes from
audio files each player keeps in a folder; the mod ships none.

## Adding songs

1. Put MP3, OGG or WAV files in `BepInEx/config/MotorMod/music`. The folder is
   created on the first start; `motor music open` in the console (F5) opens
   it.
2. Join a world, or type `motor music rescan`. Each new song is measured once
   in the background (its exact length, and its loudness so every song plays
   equally loud); `motor music status` shows progress. Results are kept in
   `.motormod-index.tsv` in the same folder; deleting that file only makes the
   mod measure again.
3. To remove a song, delete its file.

The folder lives under `BepInEx/config` so a mod update or a mod manager
never deletes your songs. The `MusicFolder` setting in the `[Soundtrack]`
section points the mod somewhere else, for example a shared folder.

## Playing together

- Songs are matched by **file name**. Friends who have the same files hear the
  same song in sync; the easiest way is to zip your music folder and send it.
- The playlist only picks songs that every listener has, so a friend missing a
  song never sits through silence. A friend with no songs at all simply hears
  nothing and doesn't hold anyone back.
- The server (or the player hosting the world) runs one shared playlist and
  needs no songs itself.

## When it plays

- You ride a MotorMod bike on water, and another rider is on the water within
  500 m (riders chain: A near B near C share one soundtrack). On your own, it
  plays only when you are the only player in the world.
- Leaving the water fades it out over 30 seconds (`FadeOutSeconds`). Back
  within 30 seconds (`GraceSeconds`) and the same song carries on in time;
  after longer, the playlist pauses and resumes the same song the next time
  anyone rides on water.
- Songs are shuffled; every song plays once before any repeats, with 3 seconds
  between songs.
- While it plays, the game's own music is held silent.

## Volume and controls

| Control | What it does |
|---|---|
| Music slider (game settings) | Turns the soundtrack up and down |
| `Volume` in `[Soundtrack]` | Extra loudness on top of the slider (1 = balanced against the engines, up to 2) |
| F7 (`ToggleKey`) | Switches the soundtrack off and on for you |
| `motor music status` | What's playing, your songs, who is listening |
| `motor music skip` | Next song (a test tool: needs `devcommands`) |

## Copyright

Only share songs you are allowed to share. The mod itself contains no music.
