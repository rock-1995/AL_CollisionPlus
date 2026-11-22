# Build and validation

The plugin targets .NET 6 and uses local game/BepInEx interop assemblies. Game, Unity, MagicaCloth, and BepInEx dependencies are not distributed here.

```powershell
dotnet build src/AL_CollisionPlus.csproj -c Release -p:GameDir='D:\Games\AmanatsuLocation'
dotnet run --project tests/NativeMathTests.csproj -c Release
dotnet run --project tests/BindingTests/BindingTests.csproj -c Release
```

`AL_GAME_DIR` can replace `GameDir`. The build prefers `BepInEx/interop-AmanatsuLocation/interop`, falling back to `BepInEx/interop`; `InteropDir` can be supplied explicitly.

The initialization hook adds `bust02` and tip transforms to `collisionBones` before setup data is built. Completed proxy attributes are checked before obstacles are added. Updates run before the common `ClothManager.ClothUpdate` boundary, leaving transform capture, integration, and response to MagicaCloth. Active native job arrays are not modified.

Geometry tests compile production fit code and cover transforms, scale, growth, invalid inputs, and steady-state allocation. Binding tests compile production ownership logic with native-access doubles and cover list changes, ownership, restoration, and scale. They do not execute IL2CPP hooks, build real native teams, or validate visual contact.

The paired DLLs compiled against the target installation with zero warnings/errors. A user trial was reported successful. No measured FPS gain is asserted. Private character captures and workstation paths are excluded from public tests/releases.

See [MagicaSoft's BoneSpring collision documentation](https://magicasoft.jp/en/mc2_magicacloth_collidercollision/) for native behavior.
