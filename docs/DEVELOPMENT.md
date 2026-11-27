# Build and validation

Run the following against a local AL installation with BepInEx 6 and generated IL2CPP interop assemblies.

```powershell
dotnet build src/AL_CollisionPlus.csproj -c Release -p:GameDir='D:\Games\AmanatsuLocation'
dotnet run --project tests/NativeMathTests.csproj -c Release
dotnet run --project tests/BindingTests/BindingTests.csproj -c Release
```

`AL_GAME_DIR` can replace the build property. The build checks the game-specific
interop path first, then `BepInEx/interop`. You can also set `InteropDir`.

Version 0.3.7 was tested with synthetic motion in Unity 6000.0.65f1. The timed
comparison measured plugin preparation, excluding rendering and native solving.
