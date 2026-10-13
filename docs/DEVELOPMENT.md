# Development

The plugin targets .NET 6 and references the game's locally generated IL2CPP interop assemblies. Game files, interop assemblies and captured meshes are not distributed in this repository.

Build with a .NET SDK and a local game directory:

```powershell
dotnet build src/AL_CollisionPlus.csproj -c Release -p:GameDir="C:\Games\AmanatsuLocation"
```

The expected local layout is `BepInEx/core` and `BepInEx/interop-AmanatsuLocation/interop` beneath that directory.

Offline geometry checks use .NET 10:

```powershell
dotnet run --project tests/Tests.csproj -c Release
```

The binding checks compile the production binding and runtime code with host API substitutes. They also require a compatible, locally captured AL Pregnancy mesh snapshot:

```powershell
dotnet run --project binding-tests/BindingTests.csproj -c Release -- "C:\LocalData\latest-mesh.json"
```

`reference/Geometry018.cs` is an isolated development-version solver used only for numerical regression checks. It is not compiled into the plugin.

Performance measurements from this harness exclude real Unity/IL2CPP call costs and cannot be treated as an in-game frame-rate measurement. Runtime diagnostic logs report this plugin's collision-pass CPU time, not total frame time.
