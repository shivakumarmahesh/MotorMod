# Changelog

## 0.8.0 (2026-10-25)

- **Damage shows.** Like the game's ships and carts, a bike has three
  states: clean above 75 % condition, sooty below it, black and smoking from
  the engine below 25 %. Each step down comes with a burst of smoke and a
  metal crunch, and every hit clanks. The hover text shows the condition.
  `DamageLook` in `[Durability]` switches the look off.
- **Fatal crashes end in a fireball.** A crash that breaks the bike goes up in
  the game's dynamite fireball with its bang; the materials still drop loose.
  `CrashDamage` is 100 per m/s above 8 m/s (was 40), so a head-on at boost
  speed breaks a fresh bike and one at plain top speed costs about half.
- Harness: `--condition <bike>` photographs the three states, `--wall` with
  `--boost` is the fatal crash, `--inspect` dumps a prefab's wear objects,
  effects and material shaders.
- **Twice the durability.** A bike has 3000 health (was 1500). A head-on at
  24 m/s or more into the surface (`FatalCrashSpeed`, boost speed on grass)
  breaks it outright whatever its condition.
- Harness: `--gear` dresses the rider (armour, cape, a sheathed weapon),
  `--biome` stages the pictures in a named biome, the rider is a ghost.
- Settings defaults changed (`CrashDamage`, `MaxHealth`): edit the lines or
  delete the settings file to pick them up.
- MIT licence; pictures in `docs/pictures`; a new icon.

## 0.7.0 (2026-10-24)

- **Surtling cores only.** Coal is no longer fuel. A core goes 1.33 km (was
  4 km) and five fill the tank, which now holds 6.67 km (was 20). Shift+E
  pours cores from your bag, then from the saddlebags; the hotbar works as
  before. The `CoalMetres` setting is gone.
- **Boost doubles.** Boost is twice the top speed (was 1.2 times) and burns
  fuel twice as fast (was 1.5): a full tank lasts about 3.3 km under boost.
- **Sinking is quick.** Standing still on water a bike is swamped in 1.5 s
  (was 12), cruising without boost in about 3 s (was 24), and it breaks apart
  1.5 s after that. Braking to a stop takes longer than that, so a bike that
  stops on deep water is lost; it must come up to boost speed from the shore.
  A wheelie still holds it up.
- **No bag for the bike's materials.** A broken bike, whether smashed,
  hammered or sunk, drops everything it cost loose where it broke: on the
  ground, or down to the seabed. Only the saddlebags' contents still come up
  in the floating leather bag, now two slots like the saddlebags.
- **Filling needs a standing bike.** A rolling bike shows no fill prompt and
  takes no fuel until it stops, so there is no refuelling at sea.
- Harness: `--seadry` (was `--seafuel`) runs a bike dry at sea and expects
  it to sink; `--sink`, `--refund` and `--ui` count the loose materials
  instead of a bag; `--water` bikes are held up until a test studies sinking.
- Settings defaults changed (`TankKm`, `BoostBurn`, `SinkSeconds`,
  `BoostMultiplier`): delete `BepInEx/config/com.shiva.valheim.motormod.cfg`
  (or edit those lines) to pick them up; an existing file keeps its old
  values.

## 0.6.0 (2026-10-24)

- **Three bikes.** Thor's Chariot, Greyfire and Tarsvin are gone, with their
  lightning, tar and blue-fire trails, the greydwarf head and the
  `ImpactDamage` setting. The mod is the Hel Cycle, the Wraith Cycle and the
  Draugr Cycle.
- **Blue and green flames really.** The Wraith and Draugr Cycles' trails
  burned orange: the game's torches get their colour from each flame
  particle's own colour data, not from the flame material. Copied now.
- **Feet down at a stop.** Standing still on land, the rider puts both feet on
  the ground beside the bike (on tiptoe where the ground is far); they go back
  on the pegs as soon as the bike moves.
- **Soundtrack fade.** Leaving the water fades the music out over 30 seconds
  (`FadeOutSeconds`, was 1.5), and the host keeps the song running for 30
  seconds (`GraceSeconds`, was 5), so coming back within that time carries on
  in time.
- **Flaming skulls.** New icons: the skull three-quarter from the front with
  its flames burning, on a clear background. The Hel Skull now burns with the
  plain torch's flames like its twins; the campfire's tall tongues it had
  before streaked behind a moving rider.
- Cleanup: the Hel Cycle's prefab is `HelCycle` (was `HellCycle`; bikes saved
  under the old name in a test world do not load), the mod's network names no
  longer carry the old FatBoy prefix, the old-name aliases, the three bikes'
  models, sounds, configs and pipeline code are removed, and the credits list
  only what is used.

## 0.5.0 (2026-10-24)

- **Wraith Cycle** and **Draugr Cycle**: the Hel Cycle with blue and with
  green flames (the same fire damage), each with its own name. Built like the
  Hel Cycle, with a wraith trophy or a draugr trophy instead of the skeleton
  trophy.
- **Flaming skulls** to wear: Hel Skull (orange flames), Wraith Skull (blue)
  and Draugr Skull (green), made at the workbench from a skeleton trophy. The
  skull covers your head; hair and beard are hidden. Only for looks.
- **Handlebars turn**: the bars, fork, front wheel, headlight and front fender
  turn with the steering: up to 22 degrees at walking pace, 5 at top speed
  (settings `HandlebarTurn` and `HandlebarTurnFast`). The rider's shoulders
  turn with the bars, and the bars never turn further than the rider's
  outside hand can follow, so the hands stay on the grips. Other players now
  see your bars turn and your bike lean.
- **More lean** in turns (22 degrees, was 16).

## 0.4.0 (2026-10-23)

- **Fuel.** A full tank goes 20 km. Every bike burns coal (400 m each, 50
  fill the tank), and each also takes its own fuel from the hotbar: Surtling
  core (Hel Cycle, 4 km), refined eitr (Thor's Chariot, 2 km), tar (Tarsvin,
  800 m), greydwarf eye (Greyfire, 800 m). Fuel burns by the distance driven,
  1.5 times as fast with boost; standing still burns nothing. Shift+E fills
  the tank with coal from your bag, then from the saddlebags. Empty, the
  engine dies. While riding, the game's mount panel shows the tank; warnings
  at 20 % and 10 %. New bikes are built empty. The fuel rules come from the
  server.
- **Saddlebags.** Every bike carries leather saddlebags with two slots: look
  at them and press E.
- **Durability the game's way.** Bikes use the game's own building
  durability, like boats and carts: the hammer shows the condition bar and
  repairs them near a forge, nothing repairs itself, and the hover text no
  longer shows a health number. Any broken bike gives back everything it cost
  (it used to be half), in a floating leather saddlebag together with what its
  saddlebags held, on land and at sea. The hammer still takes bikes apart near
  a forge, but never while someone rides.
- Shift+E fills the tank instead of packing the bike up. (The 0.3.0 docs
  called it Alt+E; the key was always Shift+E, the game's alternate use.)
- **Greyfire** replaces the Ghost Lox: the skull on its front is now a
  greydwarf brute's head. Bikes saved as Ghost Lox or Old West load as
  Greyfire, and `motor lox` still works.
- Recipes: Hel Cycle 50 bone fragments (was 30); Thor's Chariot black metal
  100, lox pelt 4 and a thunder stone (was iron 40 and deer hide 4); Tarsvin
  black metal 100 (was iron 35 and black metal 10); Greyfire deer hide 12 and
  a greydwarf brute trophy (was leather scraps 12).
- The Tarsvin hits 1.5 times as hard when it rams creatures or crashes into
  buildings (`ImpactDamage` per bike).
- Console: `motor fuel [percent]` fills the tanks of bikes within 10 m (a
  cheat).

## 0.3.0 (2026-10-23)

- The mod is now **MotorMod** (it was FatBoyMod). New plugin ID
  `com.shiva.valheim.motormod`: settings start fresh in
  `com.shiva.valheim.motormod.cfg`, and the plugin folder is `BepInEx/plugins/MotorMod`.
  Remove an old `BepInEx/plugins/FatBoyMod` folder.
- Bikes renamed after Valheim's world: **Thor's Chariot** (was Chopper),
  **Ghost Lox** (was Old West, now without its shotgun) and **Tarsvin** (was
  Wasteland). Bikes already built under the old names load as the new ones, and
  the old console names still work.
- Water: a foam wake, spray off the front tyre and a rooster tail. Bikes stay
  up only at boost speed or in a wheelie; otherwise they settle, and swamped
  they break apart.
- Sea soundtrack: drop MP3, OGG or WAV files into `BepInEx/config/MotorMod/music`.
  The mod measures each song itself (no extra tools); `motor music open` and
  `motor music rescan` in the console.
- Contact damage: a ram counts only the bike's own speed toward a creature, at
  half the old damage and at most once a second per creature. Crashes damage
  buildings and other breakable objects.
- Headlights light the road without the glowing disc at the lamp.
- The ride log is off by default.
- Shift+E packs up a bike you built and returns all its materials; other
  players' bikes and warded areas are protected (it used to delete any bike
  without a refund). Removed again in 0.4.0.
- `motor <bike>`, `motor repair`, `motor remove` and `motor removeall` are
  cheats now: they need `devcommands`, so players on a server cannot spawn
  free bikes or remove other people's.

## 0.2.0 (2026-10-23)

- Four bikes with their own engine sounds, headlights and trails (fire, chain
  lightning, tar, blue fire) that hurt enemies.
- Wheelies, bike health and repairs, crash and ram damage, riding on water.
- The sea soundtrack: a shared playlist while riding on water.

## 0.1.0 (2026-10-20)

- The first rideable motorcycle.
