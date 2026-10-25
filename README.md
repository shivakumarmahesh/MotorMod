# MotorMod

Rideable motorcycles for Valheim. Build one with the hammer, feed it Surtling
cores, ride it across the land and over the sea, pop wheelies, and leave a
trail of fire behind you. Works in single player, on a hosted world and on
dedicated servers. Built with [BepInEx](https://github.com/BepInEx/BepInEx)
and [Jötunn](https://github.com/Valheim-Modding/Jotunn).

## Pictures

![Draugr Cycle at night in the Ashlands](https://raw.githubusercontent.com/shivakumarmahesh/MotorMod/master/docs/pictures/draugr-ashlands-night-ride.jpg)
![Wraith Cycle wheelie on the sea](https://raw.githubusercontent.com/shivakumarmahesh/MotorMod/master/docs/pictures/wraith-ocean-wheelie.jpg)
![Hel Cycle at night on the sea](https://raw.githubusercontent.com/shivakumarmahesh/MotorMod/master/docs/pictures/hel-ocean-night-ride.jpg)

More in [docs/pictures](https://github.com/shivakumarmahesh/MotorMod/tree/master/docs/pictures).

## The bikes

| Bike | Trail | Built with |
|---|---|---|
| **Hel Cycle** | Fire on both tyres, thicker at speed; enemies catch fire | A skeleton trophy |
| **Wraith Cycle** | The same fire, burning blue | A wraith trophy |
| **Draugr Cycle** | The same fire, burning green | A draugr trophy |

All three ride the same: fast (65 km/h, 130 km/h with boost), long and heavy. Trails only hurt
enemies: never players, tamed animals or buildings.

## Riding

| Key | Action |
|---|---|
| E | Get on / get off (Space also gets off); on the saddlebags: open them |
| Shift+E | Fill the tank with Surtling cores (from your bag, then the saddlebags); only while the bike stands still |
| 1-8 | Add a Surtling core from the hotbar while looking at the standing bike |
| W / S | Throttle / brake, then reverse |
| A / D | Steer: the bars and front wheel turn, and the bike leans into turns |
| Shift | Boost: double top speed, double fuel use |
| Right mouse | Rev the engine |
| Left Ctrl | Wheelie: hold to lift the front, ease off to keep it up |
| F7 | Sea soundtrack off / on |

- **Stopped**, the rider puts both feet on the ground; they go back on the
  pegs as soon as the bike moves.
- **Wheelies**: gravity pulls the front back down, so you keep it up by
  feathering the key. Hold too long and the front just drops; you never fall
  off. Wheelies longer than 2 seconds show their time and your best.
- **Water**: bikes ride on water, but only stay up while planing: at boost
  speed (Shift+W) or in a wheelie. Anything slower sinks fast: cruising
  without boost it is swamped in about 3 seconds, standing still in 1.5, and
  1.5 seconds after that it breaks apart. Braking to a stop takes longer than
  that, so a bike that stops on deep water is lost. Get up to boost speed
  from the shore. In shallow water it just sits on the bottom.
- **Crashes**: ramming a creature at speed hurts it, and crashing into a
  building damages the building.

## Fuel

The bikes run on Surtling cores and nothing else. A full tank holds five
cores and goes 6.7 km; new bikes are built with an empty tank.

| | Normal | Boost |
|---|---|---|
| One core | 1.33 km | 670 m |
| A full tank (5 cores) | 6.7 km | 3.3 km |
| Full tank at top speed | about 6 minutes | about 1.5 minutes |

- Fuel burns by the distance you drive; boost burns it twice as fast, and
  standing still burns nothing. At sea you need boost to stay up, so one
  tank crosses about 3.3 km of water.
- Filling only works while the bike stands still: Shift+E takes cores from
  your bag, then from the saddlebags; a core used from the hotbar (1-8) goes
  in too. A rolling bike shows no fill prompt and takes nothing.
- Looking at a bike shows its fuel; while riding, the panel a saddled lox
  shows becomes the fuel gauge. You are warned at 20 % and 10 %.
- Empty, the engine dies and the bike rolls to a stop. On water that means
  it sinks: there is no refuelling at sea, so watch the gauge before you set
  out.

## Flaming skulls

Wear a burning skull instead of your own head (the Ghost Rider look). Made at
the workbench, they are only for looks and give no armour:

| Skull | Flames | Materials |
|---|---|---|
| Hel Skull | orange | Skeleton trophy 1, Surtling core 1, Leather scraps 2 |
| Wraith Skull | blue | Skeleton trophy 1, Greydwarf eye 5, Leather scraps 2 |
| Draugr Skull | green | Skeleton trophy 1, Guck 5, Leather scraps 2 |

The skull is the game's own skeleton skull; hair and beard are hidden while
you wear it. Its size and place can be tuned in the `[FlamingSkull]` settings.

## Saddlebags

Every bike has leather saddlebags with two slots. Look at the bags and press E
to open them. They stay shut while someone rides the bike. Shift+E takes
cores from them when your own bag has none, so spare fuel can ride along.
What is in them does not count toward your carry weight.

## Durability

Bikes use the game's own building durability, like boats and carts:

- With the hammer in hand, looking at a bike shows its condition bar. The
  hammer's repair tool repairs it when a forge is nearby. Nothing repairs
  itself.
- Monster hits, weapons and hard crashes wear it down, and the bike shows it
  the way the game's ships do: below 75 % it turns sooty and lets off a burst
  of smoke with a metal crunch; below 25 % it is black with soot and smokes
  from the engine until it is repaired. Looking at a bike shows its
  condition next to its fuel.
- A head-on crash at boost speed breaks a bike outright whatever its
  condition, and a crash that breaks a bike ends in a fireball. At plain top
  speed a head-on costs about a quarter.
- A broken bike drops **everything it cost** loose where it broke: on land
  the materials lie on the ground; a sunk bike's go down to the seabed, and
  in deep water they are gone. Only what the saddlebags held comes up in a
  floating leather saddlebag, which waits for you and vanishes once emptied.
- The hammer can take a bike apart near a forge (everything back, on the
  ground), but never while someone is riding it. Wards protect bikes like
  any building.

## Building a bike

Equip the hammer, open the **Misc** tab and stand near a **forge**:

| Bike | Materials | Weight |
|---|---|---|
| Hel Cycle | Silver 40, Iron 10, Bone fragments 50, Surtling core 8, Skeleton trophy 1, Chain 4 | 753.5 |
| Wraith Cycle | Silver 40, Iron 10, Bone fragments 50, Surtling core 8, Wraith trophy 1, Chain 4 | 754 |
| Draugr Cycle | Silver 40, Iron 10, Bone fragments 50, Surtling core 8, Draugr trophy 1, Chain 4 | 754 |

Metal bars weigh 12 each (silver 14) and cannot go through portals, so plan a
cart or a boat for the silver.

## Installing (players)

Everyone who joins a world with bikes needs the same MotorMod version.

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   (5.4.2351 or newer) and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
   (2.30 or newer). A mod manager such as r2modman does both for you.
2. Download `MotorMod-<version>.zip` from the
   [releases page](https://github.com/shivakumarmahesh/MotorMod/releases) and extract it into `BepInEx/plugins/`,
   so you get `BepInEx/plugins/MotorMod/MotorMod.dll` and a `bundles` folder
   next to it.
3. Start the game. The bikes appear in the hammer's Misc tab.

**macOS**: BepInEx runs under Rosetta on Apple Silicon. Follow the macOS steps
on the BepInExPack page; `tools/mac/start_valheim.sh` in this repository is an
optional Steam launcher that asks each time whether to start vanilla or
modded.

**Linux clients** are untested (the bikes' shaders are built for Windows and
macOS).

## Installing on a dedicated server

See [docs/SERVER.md](docs/SERVER.md), with step-by-step notes for
Indifferent Broccoli and other hosts. In short: the server needs BepInEx,
Jötunn and the MotorMod folder; it needs no music.

## Sea soundtrack

Ride on water with friends nearby (or alone in your own world) and a shared
soundtrack plays: everyone hears the same song at the same moment. Leave the
water and it fades out over half a minute; back on it sooner and the same song
carries on.

1. Put MP3, OGG or WAV files in `BepInEx/config/MotorMod/music`
   (created on the first start; the console command `motor music open` opens
   it).
2. Give your friends the same files: zip the folder and send it. Songs are
   matched by file name, and the playlist only uses songs that everyone
   listening has.
3. New files are picked up when you join a world, or at once with
   `motor music rescan`.

The mod measures each new song once (length, and loudness so every song plays
equally loud). The soundtrack follows the game's music volume slider; F7
switches it off for you. More in [docs/MUSIC.md](docs/MUSIC.md).

## Settings

All settings live in `BepInEx/config/com.shiva.valheim.motormod.cfg`, created
on the first start. Each bike has its own section (speed, acceleration,
turning, wheelie difficulty, rider and saddlebag placement); shared sections
cover riding (including how far the handlebars turn), the headlight, the
trail, durability, fuel, water, wheelie, the flaming skulls and the
soundtrack. On a server, the fuel rules come from the server's file. Edit the
file with the game closed, or use `motor reload` in the console. Each
player's own settings apply to the bikes they ride.

## Console

Open the console with F5. `motor list` lists the bikes, `motor <bike>`
(hel, wraith, draugr) spawns one in front of you, `motor remove` removes the
nearest, `motor repair` repairs bikes within 10 m, `motor fuel 50` sets their
tanks to 50 %, and `motor music status` shows what the soundtrack is doing.
Spawning, repairing, refuelling and removing bikes are cheats: they need
`devcommands` (on a server, only admins can turn that on).
[docs/cheat-sheet.md](docs/cheat-sheet.md) lists handy test commands.

## Building from source

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Credits and licence

The bike model is by BatCat and the saddlebag by Red_Ilya, under Creative
Commons Attribution licences. Full credits, including the engine sounds, are
in [CREDITS.md](CREDITS.md).

The code is under the MIT licence (see [LICENSE](LICENSE)).

MotorMod is an unofficial fan mod. Valheim is © Iron Gate AB; the mod reuses
the game's own effects and objects (the campfire and torch flames, the
skeleton trophy, the cargo crate, the chest window) at runtime and ships none
of its assets.
