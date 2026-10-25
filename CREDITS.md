# Credits

MotorMod is an unofficial fan mod for Valheim. It stands on other people's
work; thank you to all of them.

## 3D models

The bike is built from a model released under a Creative Commons Attribution
licence. The mod scaled the model, split it into a body, a steering assembly
and two wheels so the wheels can spin and the bars turn, reduced its polygon
count, converted its materials to Unity's, and added a rider seat, grip and
footpeg points. The Wraith Cycle and the Draugr Cycle are the same model with
flames of another colour.

| Bike | Original | Author | Licence | Further changes |
|---|---|---|---|---|
| Hel Cycle, Wraith Cycle, Draugr Cycle | [Ghost Rider's Hell Cycle](https://sketchfab.com/3d-models/ghost-riders-hell-cycle-08867c3f90d5478289b6341f1f1699d3) | [BatCat (TheBattyCatty)](https://sketchfab.com/TheBattyCatty) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | None |

The bike icons in the hammer menu are renders of this model, tinted blue and
green for the twins. The model is fan art inspired by a comic-book motorcycle;
MotorMod has no connection to its rights holders and calls it the Hel Cycle.

### Saddlebags

| Model | Author | Licence | Changes |
|---|---|---|---|
| [Medieval Leather Waist Bag Free](https://sketchfab.com/3d-models/medieval-leather-waist-bag-free-e7b0a876c8f64ca59d76aa7190183b53) | [Red_Ilya](https://sketchfab.com/Red_Ilya) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Joined into one mesh, scaled to 38 cm wide, turned to hang from a bike, leather tinted from red-brown to brown |

The same bag hangs on both sides of every bike, and a larger one floats up
from a wrecked bike.

## Engine sounds

The start-up, idle, throttle blips, acceleration and cruising clips are cut,
cleaned, loudness-balanced and pitch-shifted from the author's own recordings
of two motorcycles, a Harley-Davidson Softail Fat Boy 114 and a Shovelhead.
Those recordings stay out of the repository. The shut-off comes from Freesound:

| Recording | Source | Licence | Used for |
|---|---|---|---|
| Sportster start, idle, revs, shut-off | [Freesound 502690](https://freesound.org/s/502690/) by demodave | [CC0](https://creativecommons.org/publicdomain/zero/1.0/) | Shut-off |

## Valheim

Valheim is © Iron Gate AB. MotorMod reuses the game's own effects and objects
at runtime (the campfire's flames and the blue and green torch flames, the
longship's wake, water splashes, the riding animation, the skeleton trophy's
skull and the leather helmet behind the flaming skulls, the cargo crate's
floating and storage behind the wreck saddlebag, the iron gate's hit and break
effects, the chest window behind the saddlebags); none of the game's files are
included in this repository or in the mod. The bike names follow the game's
world: Hel, the wraiths and the draugr.

## Software

| Project | Licence | Use |
|---|---|---|
| [BepInEx](https://github.com/BepInEx/BepInEx) | LGPL-2.1 | Mod loader (installed separately) |
| [Jötunn](https://github.com/Valheim-Modding/Jotunn) | MIT | Valheim modding library (installed separately) |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | MIT | Runtime patching (ships with BepInEx) |
| [pyloudnorm](https://github.com/csteinmetz1/pyloudnorm) | MIT | Reference for the loudness measurement (ITU-R BS.1770) that the mod reimplements |
| Unity, Blender, Python (numpy, scipy, soundfile, noisereduce, pyloudnorm, UnityPy, Pillow) | their own licences | Asset pipeline, not shipped |

## Music

The sea soundtrack plays songs that players add themselves. MotorMod contains
no music.
