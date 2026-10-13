# Changelog

## 0.1.11

First public release.

- Independent breast–abdomen, breast–other-body and breast–ground contact controls.
- Per-type soft compression, contact strength, response, speed and displacement settings.
- Support for body collision surfaces deformed by AL Pregnancy.
- Live native-array reads avoid a native list-indexer call for every collision weight.
- A conservative ground-volume query skips individual rays when no supporting collider is nearby.
- Diagnostics and the status overlay are disabled by default. Diagnostic timers and render callbacks are inactive during normal use.

Development versions before this release were used for local tuning and testing.
