# Configuration

Settings are in `BepInEx/config/local.al.collisionplus.cfg` and a compatible BepInEx configuration manager.

| Section / key | Default | Purpose |
| --- | --- | --- |
| 01 / `Enabled` | true | Enable the native collision bridge. |
| 01 / `BreastAbdomen` | true | Supply abdomen colliders to breast teams. |
| 01 / `BreastBody` | true | Supply other native body colliders. Own breast colliders are excluded. |
| 01 / `BreastGround` | true | Supply nearby supporting scene planes. |
| 02 Native Physics / `BreastRadiusHeightRatio` | 0.012 | Moving breast collision-point radius relative to height; range 0.001–0.05. |
| 02 Native Physics / `CollisionLimitHeightRatio` | 0.06 | BoneSpring sphere-contact limit relative to height; range 0.001–0.15. |
| 02C / `BreastGroundMinNormalY` | 0.35 | Minimum upward surface-normal component; range 0.05–1. Existing values are retained. |
| 03 / `MaxCharacters` | 4 | Maximum loaded high-detail characters; range 1–12. |
| 04 / `GroundLayerMask` | -5 | Unity layer mask; character colliders and triggers are excluded separately. |
| 04 / `Diagnostics` | false | Log binding and preparation statistics. |
| 04 / `ShowStatusOverlay` | false | Display native binding status. |

Increase radius gradually if sparse points miss contact; excessive radius can hold breasts away from the visible surface. The sphere-contact limit is not a universal maximum for movement: capsule and plane contacts follow different native constraint paths and the game's spring behavior.

The 0.1.x surface-projection settings are no longer used. Changing old strength, compression, contact iterations, coverage, response, speed, or added-offset keys will not tune this solver.

Turning the master switch off detaches this plugin's obstacles and restores its radius/limit overrides. It does not rewrite running native vertex attributes. Stock breast teams have no original obstacle list; interaction with another mod adding its own breast obstacles requires separate testing.
