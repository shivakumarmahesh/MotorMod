# Console cheat sheet (Valheim 1.0)

Open the console with F5 (fn+F5 on a Mac keyboard), in a loaded world, Modded
launch. Type `devcommands` once per session to unlock everything below. On the
dedicated server you must also be in the admin list. Use a separate test
world; cheats cannot be undone.

## Stay alive

| Command | Effect |
|---|---|
| `god` | invincible (toggle) |
| `ghost` | enemies ignore you (toggle) |
| `heal` | full health and stamina, repeat whenever |
| `puke` | clears eaten food so you can re-eat |
| `debugmode` | then **Z** fly (no stamina use while flying), **B** free build placement, **K** kill nearby enemies |
| `nocost` | building and crafting cost nothing, hammer shows everything |

There is no infinite-stamina command in the game. Fly in debugmode or `heal`
when it runs out; the "Server devcommands" mod adds more if we ever need it.

## Bikes

| Command | Effect |
|---|---|
| `motor <bike>` | spawn a bike: hel, wraith, draugr (spawning, repair, fuel and remove need devcommands); spawned bikes give back only a third when broken, the game's rule for pieces nobody built |
| `motor list` | list the bikes |
| `motor remove` / `motor removeall` | remove the nearest bike / all within 50 m, nothing returned (the hammer or a broken bike returns everything) |
| `motor reload` | re-read BepInEx/config/com.shiva.valheim.motormod.cfg |
| `motor repair` | full health for bikes within 10 m |
| `motor fuel` / `motor fuel 25` | fill the tanks of bikes within 10 m / set them to 25 % |
| `motor music` | soundtrack off/on (same as F7); `motor music status` shows what plays; `motor music skip` next song (needs devcommands) |
| `spawn HelCycle`, `spawn WraithCycle`, `spawn DraugrCycle` | the game's own spawn also works |
| `spawn HelmetHelSkull`, `spawn HelmetWraithSkull`, `spawn HelmetDraugrSkull` | the flaming skulls (orange, blue, green) |

Riding: Left Ctrl is the wheelie (hold to lift, ease off to keep it up).
Shift+E on a standing bike fills the tank with Surtling cores; E on its saddlebags opens them.

## Materials for the recipes

```
spawn Silver 40      spawn Iron 10         spawn Chain 4
spawn BoneFragments 50   spawn SurtlingCore 8   spawn TrophySkeleton 1
spawn TrophyWraith 1 spawn TrophyDraugr 1
spawn LeatherScraps 2    spawn GreydwarfEye 5   spawn Guck 5
```
Fuel: `spawn SurtlingCore 5` fills a tank.
Items drop at your feet; walk over them. Build a forge with `nocost` on, or
`spawn forge` places one. Workbench prefab is `piece_workbench`.

## Mobs and world

| Command | Effect |
|---|---|
| `spawn Greydwarf 5` | five greydwarves; `spawn Troll 1 3` is one level-3 troll |
| `spawn Skeleton 3`, `spawn Draugr 2`, `spawn Wolf 2`, `spawn Lox` | other test dummies |
| `killall` | kills nearby creatures, `killenemies` only hostile ones |
| `removedrops` | clears dropped items nearby |
| `tame` | tames nearby tameable animals |
| `tod 0.5` / `tod -1` | noon / back to normal time |
| `skiptime 3600` | skip an hour; `sleep` skips to morning |
| `env Clear` / `resetenv` | force clear weather / back to normal |
| `wind 0 0` / `resetwind` | calm wind for sound checks |
| `pos`, `goto 100,200` | print position, teleport to x,z |
| `exploremap` / `resetmap` | reveal or hide the map |
| `freefly` | detached camera for screenshots, `ffsmooth 1` smooths it |
| `raiseskill run 100` | max a skill (Ride is `ride`) |
| `event army_eikthyr` / `stopevent` | start or stop a raid |
| `save` | force a world save before risky tests |

## Useful prefab names

Hel Cycle `HelCycle`, cart `Cart`, longship `VikingShip`,
forge `forge`, lox `Lox`, lox saddle item `SaddleLox`.
