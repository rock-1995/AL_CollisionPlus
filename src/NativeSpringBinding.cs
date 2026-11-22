using System;
using System.Collections.Generic;
using HarmonyLib;
using MagicaCloth2;
using UnityEngine;
using NativeTransforms = Il2CppSystem.Collections.Generic.List<UnityEngine.Transform>;
using NativeColliders = Il2CppSystem.Collections.Generic.List<MagicaCloth2.ColliderComponent>;

namespace ALCollisionPlus;

// Owns only additions to the existing breast spring's obstacle list. BoneSpring
// collision eligibility is generated during Init, before native jobs exist.
internal sealed class NativeSpringBinding : IDisposable
{
    private static readonly Dictionary<int, (IntPtr Process, int Generation)> PreparedProcesses = new();
    private static int initializationGeneration;
    private static Action<string> report;

    private readonly MagicaCloth cloth;
    private readonly IntPtr processPointer;
    private readonly HashSet<IntPtr> additions = new();
    private readonly HashSet<IntPtr> desiredIds = new();
    private readonly HashSet<IntPtr> currentIds = new();
    private NativeColliders list;
    private CurveSerializeData originalRadius, originalLimit, ownedRadius, ownedLimit;
    private int verifiedTeam, verifiedGeneration;
    private bool disposed;
    private string failure, lastReportedFailure;

    internal MagicaCloth Cloth => cloth;
    internal int AddedCount => additions.Count;
    internal string FailureReason => failure;
    internal bool IsReady => VerifyReady();

    internal static void Install(Harmony harmony, Action<string> log = null)
    {
        ClearState();
        report = log;
        var create = AccessTools.Method(typeof(ClothProcessEX), nameof(ClothProcessEX.CreateBoneRenderSetupData),
            new[] { typeof(ClothInitSerializeData), typeof(ClothProcess.ClothType), typeof(NativeTransforms),
                typeof(NativeTransforms), typeof(NativeTransforms), typeof(RenderSetupData.BoneConnectionMode) });
        if (create == null) throw new MissingMethodException("MagicaCloth2.ClothProcessEX.CreateBoneRenderSetupData");
        harmony.Patch(create,
            prefix: new HarmonyMethod(typeof(NativeSpringBinding), nameof(BeforeCreate)),
            postfix: new HarmonyMethod(typeof(NativeSpringBinding), nameof(AfterCreate)));
        harmony.Patch(AccessTools.Method(typeof(MagicaCloth), nameof(MagicaCloth.OnDestroy)),
            prefix: new HarmonyMethod(typeof(NativeSpringBinding), nameof(BeforeDestroy)));
    }

    internal static void ClearState()
    {
        PreparedProcesses.Clear();
        initializationGeneration = 0;
        report = null;
    }

    private static bool IsBreastRoot(Transform root) => root != null &&
        root.name.StartsWith("cf_j_bust", StringComparison.OrdinalIgnoreCase);

    // The collisionBones argument, rather than colliderList, controls generation
    // of VertexAttribute.DisableCollision. SetParameterChange cannot rebuild it.
    private static void BeforeCreate(ClothProcessEX __instance, ClothProcess.ClothType ctype,
        NativeTransforms rootTransforms, ref NativeTransforms collisionBones, ref bool __state)
    {
        __state = false;
        if (__instance.cloth != null) PreparedProcesses.Remove(__instance.cloth.GetInstanceID());
        if (ctype != ClothProcess.ClothType.BoneSpring || rootTransforms == null || rootTransforms.Count == 0) return;
        for (int i = 0; i < rootTransforms.Count; i++) if (!IsBreastRoot(rootTransforms[i])) return;
        try
        {
            collisionBones ??= new NativeTransforms();
            for (int i = 0; i < rootTransforms.Count; i++) AddHierarchy(rootTransforms[i], collisionBones);
            var data = __instance.cloth?.SerializeData?.colliderCollisionConstraint;
            if (data == null) throw new InvalidOperationException("Breast spring has no collision settings during Init.");
            data.collisionBones = collisionBones;
            __state = true;
        }
        catch (Exception e)
        {
            report?.Invoke("Native breast collision initialization failed: " + e.Message);
        }
    }

    private static void AddHierarchy(Transform bone, NativeTransforms bones)
    {
        if (bone == null) return;
        // Keep the torso-attached bust01 root's native eligibility unchanged.
        // Its center is inside the body's own chest capsule at neutral pose.
        string name = bone.name;
        if ((name.StartsWith("cf_j_bust02_", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("cf_y_bust02_tip_", StringComparison.OrdinalIgnoreCase)) && !bones.Contains(bone))
            bones.Add(bone);
        for (int i = 0; i < bone.childCount; i++) AddHierarchy(bone.GetChild(i), bones);
    }

    private static void AfterCreate(ClothProcessEX __instance, bool __state)
    {
        if (__state && __instance.cloth != null)
            PreparedProcesses[__instance.cloth.GetInstanceID()] = (__instance.Pointer, ++initializationGeneration);
    }

    private static void BeforeDestroy(MagicaCloth __instance)
    {
        if (__instance != null) PreparedProcesses.Remove(__instance.GetInstanceID());
    }

    internal NativeSpringBinding(MagicaCloth cloth)
    {
        this.cloth = cloth ?? throw new ArgumentNullException(nameof(cloth));
        processPointer = cloth.Process?.Pointer ?? IntPtr.Zero;
        VerifyReady();
    }

    private bool VerifyReady()
    {
        if (disposed || cloth == null) { failure = "Breast spring was destroyed or detached."; return false; }
        var process = cloth.Process;
        if (process == null || process.Pointer != processPointer ||
            !PreparedProcesses.TryGetValue(cloth.GetInstanceID(), out var prepared) || prepared.Process != processPointer)
        {
            return Fail("Breast spring did not pass the native initialization hook; reload this character or restart the game.");
        }
        if (!process.IsRunning() || process.TeamId <= 0) { failure = "Breast spring native team is still building."; return false; }
        if (verifiedTeam == process.TeamId && verifiedGeneration == prepared.Generation) { failure = null; return true; }

        // Read-only proof after the native build: movable selected points must
        // no longer carry DisableCollision (0x10). The fixed root is unchanged.
        var attributes = process.ProxyMeshContainer?.shareVirtualMesh?.attributes;
        if (attributes == null || !attributes.IsValid) return Fail("Breast spring proxy attributes are unavailable.");
        int selected = 0;
        for (int i = 0; i < attributes.Count; i++)
        {
            byte value = attributes[i].Value;
            if ((value & 2) == 0) continue;
            if ((value & 0x10) != 0) return Fail("Native breast points still have DisableCollision; native contact was not activated.");
            selected++;
        }
        if (selected == 0) return Fail("Native breast team contains no selected collision points.");
        verifiedTeam = process.TeamId;
        verifiedGeneration = prepared.Generation;
        failure = null;
        report?.Invoke($"Native breast collision ready: {cloth.name}, team={verifiedTeam}, collisionPoints={selected}.");
        return true;
    }

    private bool Fail(string reason)
    {
        failure = reason;
        if (lastReportedFailure != reason)
        {
            lastReportedFailure = reason;
            report?.Invoke($"Native breast collision unavailable: {cloth.name}: {reason}");
        }
        return false;
    }

    // Invoke at the ClothManager.ClothUpdate prefix, before transform capture and
    // job scheduling. Inputs are WORLD distances. Magica multiplies serialized
    // radius/limit by its team scaleRatio; compensate using the latest native
    // ratio. A scale change is picked up on the following completed native step.
    // Magica consumes SetParameterChange in its normal update.
    internal bool Apply(List<ColliderComponent> ownedTargets, float radius, float limit)
    {
        if (!VerifyReady()) return false;
        desiredIds.Clear();
        if (ownedTargets != null)
            foreach (var target in ownedTargets) if (target != null) desiredIds.Add(target.Pointer);
        float scale = 1;
        if (desiredIds.Count > 0)
        {
            scale = MagicaManager.Team.teamDataArray[cloth.Process.TeamId].scaleRatio;
            if (!(scale > 0) || !float.IsFinite(scale))
                return Fail("Native breast team has no valid scale ratio yet.");
        }
        var data = cloth.SerializeData;
        var collision = data.colliderCollisionConstraint;
        if (collision.colliderList == null) collision.colliderList = new NativeColliders();
        var current = collision.colliderList;
        bool changed = false;
        if (list == null || list.Pointer != current.Pointer)
        {
            // Keep ownership IDs if the game copied the old list into a new one;
            // only the original list membership is removed here.
            changed |= RemoveAdditions(clearOwnership: false);
            list = current;
        }
        currentIds.Clear();
        // Exactly one scan of the native list. Its contents can change in place,
        // including equal-count replacements, so count/reference checks alone
        // would miss a game's rewrite. Hash sets are reused across frames.
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var entry = list[i];
            if (entry == null) continue;
            var id = entry.Pointer;
            if (additions.Contains(id) && !desiredIds.Contains(id))
            {
                list.RemoveAt(i);
                changed = true;
            }
            else currentIds.Add(id);
        }
        additions.IntersectWith(desiredIds);
        if (ownedTargets != null)
            foreach (var target in ownedTargets)
            {
                if (target == null || !currentIds.Add(target.Pointer)) continue;
                list.Add(target);
                additions.Add(target.Pointer);
                changed = true;
            }
        if (desiredIds.Count > 0)
        {
            changed |= OverrideCurve(data, collision, radius / scale, limit / scale);
        }
        else changed |= RestoreCurves();
        if (changed) cloth.SetParameterChange();
        lastReportedFailure = null;
        return true;
    }

    private bool OverrideCurve(ClothSerializeData data, ColliderCollisionConstraint.SerializeData collision,
        float radius, float limit)
    {
        bool changed = false;
        if (ownedRadius == null) ownedRadius = new CurveSerializeData(radius);
        if (ownedLimit == null) ownedLimit = new CurveSerializeData(limit);
        if (data.radius == null || data.radius.Pointer != ownedRadius.Pointer)
        { originalRadius = data.radius; data.radius = ownedRadius; changed = true; }
        if (collision.limitDistance == null || collision.limitDistance.Pointer != ownedLimit.Pointer)
        { originalLimit = collision.limitDistance; collision.limitDistance = ownedLimit; changed = true; }
        if (ownedRadius.value != radius) { ownedRadius.value = radius; changed = true; }
        if (ownedLimit.value != limit) { ownedLimit.value = limit; changed = true; }
        return changed;
    }

    private bool RestoreCurves()
    {
        var data = cloth?.SerializeData;
        if (data == null) return false;
        bool changed = false;
        if (ownedRadius != null && data.radius?.Pointer == ownedRadius.Pointer)
        { data.radius = originalRadius; changed = true; }
        var collision = data.colliderCollisionConstraint;
        if (ownedLimit != null && collision?.limitDistance?.Pointer == ownedLimit.Pointer)
        { collision.limitDistance = originalLimit; changed = true; }
        return changed;
    }

    private bool RemoveAdditions(bool clearOwnership = true)
    {
        bool changed = false;
        if (list != null && additions.Count > 0)
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];
                if (entry != null && additions.Contains(entry.Pointer)) { list.RemoveAt(i); changed = true; }
            }
        if (clearOwnership) additions.Clear();
        return changed;
    }

    public void Dispose()
    {
        if (disposed) return;
        bool changed = RemoveAdditions();
        if (cloth != null)
        {
            changed |= RestoreCurves();
            if (changed) cloth.SetParameterChange();
        }
        disposed = true;
    }
}
