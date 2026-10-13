# AL Collision Plus

Soft breast collisions for **AmanatsuLocation**, using the game's existing body collision surfaces. Contact allows a small amount of compression, then moves the breast smoothly away from the surface.

[Download](https://github.com/rock-1995/AL_CollisionPlus/releases/latest) · [Parameter reference](docs/CONFIGURATION.md) · [Changelog](CHANGELOG.md)

Requires **BepInEx 6 for IL2CPP**. AL Pregnancy is optional; when present, collision follows its deformed body collision mesh.

## Controls

Settings are available through your BepInEx configuration manager or `BepInEx/config/local.al.collisionplus.cfg`.

| Setting | Effect |
| --- | --- |
| `Enabled` | Master switch. Turning it off removes this plugin's added displacement. |
| `BreastAbdomen` | Breast–abdomen contact, including a pregnant belly. |
| `BreastBody` | Breast contact with other body parts, including the opposite breast. Abdomen contact remains controlled separately. |
| `BreastGround` | Breast contact with the ground and upward-facing scene surfaces. |

All three contact types are enabled by default. For abdomen contact only, leave `BreastAbdomen` on and turn the other two off.

Only breast physics roots are moved. The plugin does not move the abdomen, legs or hair, and does not add hair collisions.

## Adjusting contact

Each contact type has its own settings. Names begin with `BreastAbdomen`, `BreastBody` or `BreastGround`.

| To change… | Adjust… |
| --- | --- |
| Overall effect | `Strength` — lower values soften the correction; zero disables its contribution. |
| Allowed soft overlap | `CompressionHeightRatio` — higher values allow more compression before correction begins. |
| How gently contact starts | `SoftTransitionHeightRatio` — higher values make onset softer. |
| Maximum breast movement | `MaxOffsetHeightRatio` — limits the added displacement. |
| Speed of movement | `MaxSpeedHeightRatio` and `ResponsePerSecond` — lower values move and return more slowly. |

`HeightRatio` distances are relative to character height: `0.01` means 1%. Search distance controls how deeply overlapping surfaces can be found; it is not an extra gap around the body.

## Contact behavior

- Small overlaps are intentional to approximate soft compression. This is a bone-based correction, not a full soft-body simulation.
- When several types touch at once, their corrections share a displacement limit rather than stacking three full offsets.
- Ground contact requires a scene collider and actual penetration. Character colliders, triggers and walls are excluded. A surface above the entire breast is not treated as support beneath it.
- Deep penetration beyond the configured search or movement limits may remain. Clothing is not used as a separate collision surface.

There is no status overlay or periodic diagnostic output during normal use.
