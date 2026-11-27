# Configuration

Open BepInEx Configuration Manager or edit `BepInEx/config/local.al.collisionplus.cfg`
with the game closed.

| Key | Default | Purpose |
| --- | --- | --- |
| `Enabled` | true | Enable the plugin. Turning it off detaches added colliders and restores the original breast settings. |
| `BreastAbdomen` | true | Contact with the abdomen, including pregnancy belly inputs. |
| `BreastBody` | true | Contact with arms, palms, legs, and other characters' breasts. |
| `BreastGround` | true | Contact with nearby supporting surfaces. |
| `BreastHair` | true | Allow breasts to push hair in one direction. |
| `BreastRadiusScale` | 1 | Scale the breast spheres fitted to the body mesh. Values at or below zero disable added collision. |
| `CollisionLimitHeightRatio` | 0.06 | Limit the combined correction as a fraction of height. Values at or below zero remove this limit. |
| `BreastGroundMinNormalY` | 0.35 | Minimum upward component of a support-surface normal. |
| `MaxCharacters` | 4 | Number of loaded high-detail characters to process. Values at or below zero disable added collision. |
| `GroundLayerMask` | -5 | Unity layer mask for ground queries. Characters and triggers are excluded. |
| `Diagnostics` | false | Log contact processing and measured displacement every five seconds. |
| `ShowStatusOverlay` | false | Display native binding status. |

The radius multiplier replaces `BreastRadiusHeightRatio` from 0.2.x. Start with
the default, then adjust gradually. Extreme shapes or poses can still clip.
