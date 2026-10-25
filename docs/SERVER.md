# Running MotorMod on a dedicated server

The server needs BepInEx, Jötunn and the MotorMod folder. It needs no music:
the soundtrack's songs live on each player's computer, and the server only
runs the shared playlist.

Every player must run the same MotorMod version as the server. Jötunn checks
this when someone joins and refuses a player without MotorMod or with a
different minor version (0.8.x must match 0.8.x).

## What goes on the server

```
BepInEx/
  plugins/
    Jotunn/            Jotunn.dll (from the Jötunn release)
    MotorMod/
      MotorMod.dll
      bundles/         hel_win, saddlebag_win
```

The `MotorMod-<version>-server.zip` release contains exactly the MotorMod
part. A Linux server loads the `_win` bundles (it never draws anything; it
only needs the bikes' shapes). The `_osx` bundles are for Mac players and can
be left out on the server.

## Indifferent Broccoli

1. Stop the server in the control panel.
2. Turn on BepInEx (mod support) in the panel, start the server once so it
   creates the `BepInEx` folders, then stop it again.
3. Open the panel's file manager (or connect with SFTP) and go to the
   Valheim folder's `BepInEx/plugins` (on our server this was
   `/steamcmd/valheim/BepInEx/plugins/`).
4. Upload the `Jotunn` folder with `Jotunn.dll` inside.
5. Upload the `MotorMod` folder from the server zip.
6. Start the server and open its log (`BepInEx/LogOutput.log`, or the panel's
   console). These lines mean it worked:
   ```
   [Info   :   BepInEx] Loading [Jotunn 2.30.2]
   [Info   :   BepInEx] Loading [MotorMod 0.8.0]
   [Info   :  MotorMod] MotorMod 0.8.0 loaded with 3 bikes
   ```
7. Every player installs the same BepInEx, Jötunn and MotorMod versions (see
   the README) and joins as usual.

## Other hosts

Any host that runs BepInEx works the same way: BepInEx for Valheim on the
server (the BepInExPack page has Linux and Windows server instructions),
then the two plugin folders above. Self-hosted Linux servers start with
`start_server_bepinex.sh` from BepInExPack.

## Settings on a server

The server writes its own `BepInEx/config/com.shiva.valheim.motormod.cfg` on
the first start.

- The **soundtrack's host settings** come from the server's file:
  `RadiusMeters` (how close riders must be to share the music), `GraceSeconds`
  (how long the song runs on after the last rider leaves the water) and
  `GapSeconds`.
- The **fuel rules** (`[Fuel]`: on or off, tank size, boost use) come from
  the server's file too and are sent to every player when they join, so
  everyone rides by the same tank. Only admins can change them in game.
- **Bike handling, damage and trails** come from each rider's own settings
  file, because each bike is simulated on the machine of the player riding it.

## Admin notes

- Spawning bikes with `motor <bike>` needs `devcommands`, which on a server
  only works for players in the admin list. Building them with the hammer
  needs no special rights.
- Bikes are saved in the world like any building. Removing MotorMod from a
  server leaves them as unknown objects in the save; put the mod back and they
  return.
- Updating: replace the `MotorMod` folder on the server and on every player's
  computer.

## Troubleshooting

- **"Incompatible version" when joining**: the player's MotorMod (or Jötunn)
  version differs from the server's. Install the same release everywhere.
- **Bikes are missing from the hammer**: the `bundles` folder is missing or
  incomplete. The log names the bike and the file it looked for.
- **Log file**: `BepInEx/LogOutput.log` on the server; include it with any bug
  report.
