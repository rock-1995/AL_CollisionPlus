# Changelog

## 0.2.0

- Move breast collision response to the game's existing MagicaCloth2 BoneSpring solver; remove the managed triangle-contact solver and additional bone-offset pass.
- Supply five deforming abdomen spheres, native body colliders, and nearby ground planes. Preserve the three contact switches.
- Enable moving breast collision points during native initialization, verify their generated attributes, and exclude own-breast feedback.
- Add native radius/distance settings and restore owned collider references and parameter overrides when disabled.
- Integrate AL Pregnancy 0.3.4's input bridge so inactive liquid helpers still provide current abdomen geometry without additional liquid mesh submission.
- Replace the old solver tests with portable geometry and native-binding ownership regressions. Contact shape differs from 0.1.x; no quantified FPS improvement is claimed.

## 0.1.11

First public release.

- Independent breast–abdomen, breast–other-body and breast–ground contact controls.
- Per-type soft compression, contact strength, response, speed and displacement settings.
- Support for body collision surfaces deformed by AL Pregnancy.
- Live native-array reads avoid a native list-indexer call for every collision weight.
- A conservative ground-volume query skips individual rays when no supporting collider is nearby.
- Diagnostics and the status overlay are disabled by default. Diagnostic timers and render callbacks are inactive during normal use.

Development versions before this release were used for local tuning and testing.
