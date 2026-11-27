using System.Globalization;
using UnityEngine;

namespace ALCollisionPlus;

// Created only for a diagnostic sample. Logging never drives physics state.
internal sealed class CollisionTrace
{
    private readonly Plugin plugin;
    internal readonly int Frame = Time.frameCount;
    internal CollisionTrace(Plugin plugin) { this.plugin = plugin; }
    internal void Write(string stage, string scope, string message)
    {
        try { plugin.Log.LogInfo($"[Collision trace f={Frame}] {stage} {scope} | {message}"); }
        catch { /* A logging sink must not detach an otherwise valid native team. */ }
    }
    internal void Read(string stage, string scope, Func<string> read)
    {
        try { Write(stage, scope, read()); }
        catch (Exception e) { Write(stage, scope, $"Diagnostic read failed: {e.GetType().Name}: {e.Message}"); }
    }
    internal static string V(Vector3 v) => FormattableString.Invariant($"({v.x:F5},{v.y:F5},{v.z:F5})");
    internal static string V(System.Numerics.Vector3 v) => FormattableString.Invariant($"({v.X:F5},{v.Y:F5},{v.Z:F5})");
    internal static string F(float v) => v.ToString("G7", CultureInfo.InvariantCulture);
    internal static string Name(UnityEngine.Object obj) => obj == null ? "<null>" : $"{obj.name}#{obj.GetInstanceID()}";
    internal static bool IsArm(string name) => name.Contains("arm", StringComparison.OrdinalIgnoreCase) || name.Contains("Palm", StringComparison.OrdinalIgnoreCase);
}
