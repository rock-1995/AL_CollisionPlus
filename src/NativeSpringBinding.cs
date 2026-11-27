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
    private static readonly Dictionary<int, (IntPtr Process, int Generation, float Depth)> PreparedProcesses = new();
    private static int initializationGeneration;
    private static readonly Dictionary<int, int> ParameterRevisions = new();
    private static int ownedParameterChange;
    private static Action<string> report;

    private readonly MagicaCloth cloth;
    private readonly IntPtr processPointer;
    private readonly HashSet<IntPtr> additions = new();
    private readonly HashSet<IntPtr> desiredIds = new();
    private readonly HashSet<IntPtr> currentIds = new();
    private readonly List<int> ownedSlots = new();
    private NativeColliders list;
    private CurveSerializeData originalRadius, originalLimit, ownedRadius, ownedLimit;
    private AnimationCurve radiusCurve;
    private float middleDepth = .5f, curveStart = float.NaN, curveTip = float.NaN;
    private int verifiedTeam, verifiedGeneration;
    private bool disposed;
    private string failure, lastReportedFailure;

    internal MagicaCloth Cloth => cloth;
    internal int AddedCount => additions.Count;
    internal string FailureReason => failure;
    internal bool IsReady => VerifyReady();
    internal int ParameterRevision => ParameterRevisions.TryGetValue(cloth.GetInstanceID(), out int value) ? value : 0;
    internal float TeamScale => MagicaManager.Team.teamDataArray[cloth.Process.TeamId].scaleRatio;

    // Read-only; never calls VerifyReady or changes native parameters.
    internal string DiagnosticState()
    {
        if (cloth == null) return "cloth missing";
        var process = cloth.Process;
        var data = cloth.SerializeData;
        var collision = data?.colliderCollisionConstraint;
        int id = process?.TeamId ?? 0;
        bool prepared = process != null && PreparedProcesses.TryGetValue(cloth.GetInstanceID(), out var record) && record.Process == process.Pointer;
        string state = $"cloth={CollisionTrace.Name(cloth)}, clothEnabled={cloth.enabled}, activeInHierarchy={cloth.gameObject.activeInHierarchy}, team={id}, preparedHook={prepared}, processRunning={process?.IsRunning()}, failure={failure??"<none>"}, mode={collision?.mode}, serializedTargets={collision?.colliderList?.Count??0}, ownedTargets={additions.Count}, serializedRadius={data?.radius?.value}, serializedLimit={collision?.limitDistance?.value}";
        if (id <= 0 || !process.IsRunning()) return state;
        var team = MagicaManager.Team.teamDataArray[id];
        state += $", nativeEnabled={team.IsEnable}, teamRunningFlag={team.IsRunning}, cameraCulled={team.IsCameraCullingInvisible}, distanceCulled={team.IsDistanceCullingInvisible}, updateCount={team.updateCount}, scale={CollisionTrace.F(team.scaleRatio)}, blend={CollisionTrace.F(team.blendWeight)}, nativeColliderCount={team.colliderCount}";
        var flags = new List<string>();
        for (int i=0;i<team.colliderCount;i++)
        {
            var value = MagicaManager.Collider.flagArray[team.colliderChunk.startIndex+i].Value;
            flags.Add($"{i}:enabled={(value & ColliderManager.Flag_Enable)!=0}/flags={value}");
        }
        return state+", nativeColliderFlags=["+string.Join(",",flags)+"]";
    }

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
        harmony.Patch(AccessTools.Method(typeof(MagicaCloth), nameof(MagicaCloth.SetParameterChange)),
            postfix: new HarmonyMethod(typeof(NativeSpringBinding), nameof(AfterParameterChange)));
    }

    internal static void ClearState()
    {
        PreparedProcesses.Clear();
        ParameterRevisions.Clear();
        ownedParameterChange = 0;
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
        // BoneSpring translates its root. The contact center is offset into the
        // breast, so contact belongs to this root rather than a downstream point.
        string name = bone.name;
        if (name.StartsWith("cf_j_bust01_", StringComparison.OrdinalIgnoreCase) && !bones.Contains(bone))
            bones.Add(bone);
        for (int i = 0; i < bone.childCount; i++) AddHierarchy(bone.GetChild(i), bones);
    }

    private static void AfterCreate(ClothProcessEX __instance, bool __state)
    {
        if (__state && __instance.cloth != null)
            PreparedProcesses[__instance.cloth.GetInstanceID()] = (__instance.Pointer, ++initializationGeneration, RestDepth(__instance.cloth));
    }

    private static float RestDepth(MagicaCloth cloth)
    {
        var roots = cloth.SerializeData.rootBones;
        if (roots == null || roots.Count == 0) return .5f;
        var root = roots[0]; Transform middle = null, tip = null;
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name.StartsWith("cf_j_bust02_", StringComparison.OrdinalIgnoreCase)) middle = bone;
            if (bone.name.StartsWith("cf_y_bust02_tip_", StringComparison.OrdinalIgnoreCase)) tip = bone;
        }
        if (middle == null || tip == null) return .5f;
        float a = Vector3.Distance(root.position, middle.position), b = Vector3.Distance(middle.position, tip.position);
        return a > 0 && b > 0 ? a / (a + b) : .5f;
    }

    private static void BeforeDestroy(MagicaCloth __instance)
    {
        if (__instance == null) return;
        PreparedProcesses.Remove(__instance.GetInstanceID());
        ParameterRevisions.Remove(__instance.GetInstanceID());
    }

    private static void AfterParameterChange(MagicaCloth __instance)
    {
        if (ownedParameterChange != 0 || __instance == null) return;
        int id = __instance.GetInstanceID();
        if (!PreparedProcesses.ContainsKey(id)) return;
        ParameterRevisions.TryGetValue(id, out int revision);
        ParameterRevisions[id] = unchecked(revision + 1);
    }

    private void PublishParameters()
    {
        // Our collider reconciliation must not request another geometry fit.
        ownedParameterChange++;
        try { cloth.SetParameterChange(); }
        finally { ownedParameterChange--; }
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

        // Read-only proof after the native build. BoneSpring deliberately lets
        // its fixed/root point receive collision and output root translation.
        var attributes = process.ProxyMeshContainer?.shareVirtualMesh?.attributes;
        if (attributes == null || !attributes.IsValid) return Fail("Breast spring proxy attributes are unavailable.");
        int selected = 0;
        middleDepth = prepared.Depth;
        for (int i = 0; i < attributes.Count; i++)
        {
            byte value = attributes[i].Value;
            if ((value & 3) == 0) continue;
            // Downstream points retain the original spring motion; this plugin
            // selects only the root as the offset sphere's receiver.
            if ((value & 0x10) != 0) continue;
            selected++;
        }
        if (selected == 0) return Fail("Native breast team contains no selected collision points.");
        if (!(middleDepth > 0 && middleDepth < 1)) middleDepth = .5f;
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
    internal bool Apply(List<ColliderComponent> ownedTargets, float radius, float limit, float? tipRadius = null)
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
        if (changed && ownedTargets != null)
        {
            // Re-entering contacts used to append at the end. Left/right teams
            // could then solve the same obstacles in different historical order.
            // Restore the caller's order ONLY within our own slots; leave game
            // and other plugins' entries and their positions untouched.
            ownedSlots.Clear(); currentIds.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (entry != null && additions.Contains(entry.Pointer)) ownedSlots.Add(i);
            }
            int slot = 0;
            foreach (var target in ownedTargets)
                if (target != null && additions.Contains(target.Pointer) && currentIds.Add(target.Pointer))
                    list[ownedSlots[slot++]] = target;
        }
        if (desiredIds.Count > 0)
        {
            changed |= OverrideCurve(data, collision, radius / scale, limit <= 0 ? 0 : limit / scale, tipRadius / scale);
        }
        else changed |= RestoreCurves();
        if (changed) PublishParameters();
        lastReportedFailure = null;
        return true;
    }

    private bool OverrideCurve(ClothSerializeData data, ColliderCollisionConstraint.SerializeData collision,
        float radius, float limit, float? tipRadius)
    {
        bool changed = false;
        if (ownedRadius == null) ownedRadius = new CurveSerializeData(radius);
        if (ownedLimit == null) ownedLimit = new CurveSerializeData(limit);
        if (data.radius == null || data.radius.Pointer != ownedRadius.Pointer)
        { originalRadius = data.radius; data.radius = ownedRadius; changed = true; }
        if (collision.limitDistance == null || collision.limitDistance.Pointer != ownedLimit.Pointer)
        { originalLimit = collision.limitDistance; collision.limitDistance = ownedLimit; changed = true; }
        if (tipRadius.HasValue)
        {
            float start = BreastSurfaceFit.CurveStart(radius, tipRadius.Value, middleDepth);
            if (radiusCurve == null) radiusCurve = AnimationCurve.Linear(0, start, 1, tipRadius.Value);
            if (start != curveStart || tipRadius.Value != curveTip)
            {
                float slope = tipRadius.Value - start;
                radiusCurve.MoveKey(0, new Keyframe(0, start, slope, slope));
                radiusCurve.MoveKey(1, new Keyframe(1, tipRadius.Value, slope, slope));
                curveStart = start; curveTip = tipRadius.Value; changed = true;
            }
            if (ownedRadius.value != 1 || !ownedRadius.useCurve || ownedRadius.curve?.Pointer != radiusCurve.Pointer)
            { ownedRadius.SetValue(1, radiusCurve); changed = true; }
        }
        else if (ownedRadius.value != radius || ownedRadius.useCurve)
        { ownedRadius.value = radius; ownedRadius.useCurve = false; changed = true; }
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
            if (changed) PublishParameters();
        }
        disposed = true;
    }
}
