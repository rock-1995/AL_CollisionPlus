# Parameter reference

Settings use one of three prefixes: `BreastAbdomen`, `BreastBody` or `BreastGround`. The table lists the suffixes shared by all three.

| Suffix | Default | Meaning |
| --- | ---: | --- |
| `Strength` | `1` | Correction strength, from 0 to 1. Applied once after contact solving. |
| `MaxOffsetHeightRatio` | `0.09859826` | Maximum added displacement as a fraction of character height, further scaled by strength. |
| `CompressionHeightRatio` | `0.0013973799` | Permitted local penetration before correction starts. |
| `SoftTransitionHeightRatio` | `0.003` | Depth over which correction gradually reaches full strength. |
| `MaxSpeedHeightRatio` | `0.06` | Maximum change in added displacement per second, relative to height. |
| `ResponsePerSecond` | `8` | Smoothing response. Higher values react faster, subject to the speed limit. |
| `MinContactCoverage` | `0.2` | Reduces the influence of a small number of intersecting sample points. Higher values are more conservative. |
| `ContactIterations` | `8` | Solver iterations on the same contact planes within one frame. Range: 1–16. |
| `SearchDistanceHeightRatio` | `0.103777304` | Search range for overlapping surfaces. Increasing this does not add contact padding. |
| `MinSkinInfluence` | `0.77074236` | Minimum skin weight used when converting surface penetration to bone movement. Higher values limit amplification at weakly weighted edges. |

## Body surfaces

These settings apply to breast–abdomen and breast–other-body contact.

| Setting | Default | Meaning |
| --- | ---: | --- |
| `NormalAlignment` | `0.30131006` | Minimum alignment between the surface's outward direction and separation of the two parts. Higher values reject more faces; `-1` disables this filter. |
| `ConflictGuard` | `false` | Reduces a proposal if it would worsen the deepest local overlap beyond the tolerance. |
| `ConflictToleranceHeightRatio` | `0.007117904` | Extra local compression allowed by the conflict guard. Only used when that guard is enabled. |

The triangle-edge tolerance is `ContactMarginHeightRatio` for abdomen contact and `BreastBodyEdgeToleranceHeightRatio` for other-body contact. Both default to `0.003126201`. This tolerance applies around triangle edges, not as a positive gap outside the surface.

## Ground

`BreastGroundMinNormalY` defaults to `0.35`. It is the minimum upward component of a supporting surface's normal. Larger values restrict contact to flatter surfaces.

`GroundLayerMask` defaults to `-5`, which excludes Unity's Ignore Raycast layer. Character colliders and triggers are also filtered out.

## Combined contact and performance

When multiple types contribute, the largest relevant single-type displacement cap is shared. The slower relevant response and speed limit remain in effect while contact or its return motion persists.

`MaxCharacters` defaults to `4` and limits the number of loaded high-detail characters participating in collision. Work scales with active breast roots, body surfaces and nearby contacts; disabling unused contact types reduces work.

Developer diagnostics and the status overlay are disabled by default and hidden from the configuration-manager UI.
