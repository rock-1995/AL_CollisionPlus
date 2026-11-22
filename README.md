# AL Collision Plus

**Version 0.2.0** — breast collision for **AmanatsuLocation**, using the game's existing **MagicaCloth2 BoneSpring** solver.

[Download](https://github.com/rock-1995/AL_CollisionPlus/releases/latest) · [Settings](docs/CONFIGURATION.md) · [Changelog](CHANGELOG.md) · [Build and validation](docs/DEVELOPMENT.md)

## Installation

Requires BepInEx 6 for IL2CPP. Exit the game and extract the release ZIP into the game directory. Restart after installation so breast teams are initialized with collision enabled.

AL Pregnancy is optional. If installed, use **[AL Pregnancy 0.3.4](https://github.com/rock-1995/AL_Pregnancy.dll/releases/tag/0.3.4)** or a compatible later version providing the native collision input bridge. Older pregnancy versions cannot refresh an inactive belly collision mesh for this plugin.

## Controls

Use your BepInEx configuration manager or `BepInEx/config/local.al.collisionplus.cfg`.

| Setting | Effect |
| --- | --- |
| `Enabled` | Master switch. Turning it off removes this plugin's added collider references and restores the breast parameters it changed. |
| `BreastAbdomen` | Breast–abdomen contact, including pregnancy deformation. |
| `BreastBody` | Breast contact with other body parts and other characters' native body colliders. Own breast colliders are excluded to avoid feedback. |
| `BreastGround` | Breast contact with nearby upward-facing scene surfaces. |

All three contact types are enabled by default. For abdomen contact only, leave `BreastAbdomen` on and turn the other two off.

`BreastRadiusHeightRatio` adjusts native breast collision-point radius; `0.012` means 1.2% of character height. `CollisionLimitHeightRatio` adjusts the spring's sphere-contact distance limit. See the [settings reference](docs/CONFIGURATION.md) before tuning.

## Contact behavior

Five spheres follow the low-detail abdomen shape. Other body contact uses existing native body colliders. A small number of downward ground queries supplies native plane colliders. The game's solver moves the breast bones; this plugin does not run the old triangle-contact solver or write additional breast-bone offsets.

Only moving `bust02` and tip points are newly enabled for collision. The torso-attached breast root keeps its original eligibility. The abdomen, legs, and hair are not driven by this plugin.

Liquid collision remains in AL Pregnancy's existing SkinnedCollisionHelper/Obi path. Its new bridge also refreshes belly inputs when liquid collision objects are inactive, without enabling those objects or submitting extra liquid meshes.

## Upgrading from 0.1.x

The master and three contact switches, character limit, ground mask, and ground-normal setting retain existing values. Old strength, compression, coverage, contact-iteration, response, speed, and added-offset settings no longer control this version. Native parameters have new keys.

Native spheres/capsules approximate the surface; contact differs from the old triangle-based correction. Extreme body shapes or poses may still clip. A scene collider is required for ground contact. Pregnancy shape comes from the most recently completed deformation; game update ordering can make this the previous completed pose.

Keep the previous DLL and configuration to compare or roll back. Replace DLLs only with the game closed.

## Diagnostics

`Diagnostics` and `ShowStatusOverlay` are off by default. The log reports `Native breast collision ready` after checking built-team collision attributes, or `Native breast collision unavailable` with a reason. Diagnostic preparation timings exclude the native solver and total frame time.

Offline geometry and binding tests pass, and the user reported a successful trial. No quantified game FPS improvement or comprehensive pose/garment compatibility is claimed.
