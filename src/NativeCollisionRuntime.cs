using System.Diagnostics;
using System.Reflection;
using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MagicaCloth2;
using UnityEngine;

namespace ALCollisionPlus;

internal sealed class NativeCollisionRuntime : IDisposable
{
    private readonly Plugin plugin;
    private readonly Dictionary<IntPtr, Actor> actors = new();
    private readonly List<Transform> characterRoots = new();
    private readonly Il2CppStructArray<RaycastHit> hits = new(64);
    private float nextScan, nextReport;
    private bool disposed;
    private int faultCount, steps;
    private double elapsedMs;
    private float retryAt;
    internal string Status { get; private set; } = "Native collision: waiting for characters";
    internal NativeCollisionRuntime(Plugin plugin)
    {
        this.plugin = plugin;
        // Resolve only once. This keeps the collision plugin usable without the
        // pregnancy plugin and avoids per-vertex/per-frame reflection.
        var type = Type.GetType("ALPregnancy.NativeCollisionBridge, AL_Pregnancy", false);
        var refresh = type?.GetMethod("RefreshInput", BindingFlags.Public | BindingFlags.Static);
        if (refresh != null)
            NativeBodyShape.RefreshInput = (Action<SkinnedCollisionHelper>)Delegate.CreateDelegate(typeof(Action<SkinnedCollisionHelper>), refresh);
        else if (BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.ContainsKey("local.al.pregnancy.preview"))
            throw new InvalidOperationException("Native Collision Plus needs AL_Pregnancy 0.3.4 or later to refresh inactive belly inputs. Install the paired DLLs.");
    }

    internal void PollDisabled()
    {
        if (disposed) return;
        if (!plugin.Enabled.Value || !(plugin.BreastAbdomen.Value || plugin.BreastBody.Value || plugin.BreastGround.Value))
        {
            if (actors.Count != 0) Clear();
            Status = "Native collision OFF: added colliders removed; original parameters restored";
        }
    }

    internal void Step()
    {
        if (disposed) return;
        PollDisabled();
        if (!plugin.Enabled.Value || !(plugin.BreastAbdomen.Value || plugin.BreastBody.Value || plugin.BreastGround.Value) || Time.unscaledTime < retryAt) return;
        long start = plugin.Diagnostics.Value ? Stopwatch.GetTimestamp() : 0;
        try
        {
            if (Time.unscaledTime >= nextScan) { Scan(); nextScan = Time.unscaledTime + 1; }
            // Stop publishing obstacles from disabled/reloading models immediately,
            // before processing any other actor's spring list.
            foreach (var actor in actors.Values)
            {
                actor.Active = IsActive(actor.Human) && actor.Human.Body?.GetObjBone() == actor.BoneObject;
                if (actor.Active)
                {
                    actor.Shape.RefreshHeight();
                    if (plugin.BreastAbdomen.Value) actor.Shape.Update();
                }
            }
            int springs = 0, obstacles = 0, waiting = 0;
            string waitingReason = null;
            foreach (var actor in actors.Values)
            {
                foreach (var spring in actor.Springs)
                {
                    spring.Targets.Clear();
                    if (!actor.Active)
                    {
                        spring.Binding.Apply(spring.Targets, 0, 0);
                        spring.Ground.enabled = false;
                        continue;
                    }
                    foreach (var target in actors.Values)
                    {
                        if (!target.Active) continue;
                        if (plugin.BreastAbdomen.Value && target.Shape.Ready)
                            foreach (var c in target.Shape.BellyColliders) spring.Targets.Add(c);
                        foreach (var candidate in target.NativeColliders)
                        {
                            var c = candidate.Component;
                            if (c == null) continue;
                            bool abdomen = candidate.Abdomen;
                            // A fitted belly replaces the flat abdomen envelope.
                            if (abdomen && (!plugin.BreastAbdomen.Value || target.Shape.Ready)) continue;
                            if (!abdomen && !plugin.BreastBody.Value) continue;
                            if (ReferenceEquals(actor, target) && candidate.Breast) continue;
                            // Do not let a spring push against an obstacle moved by itself.
                            if (ReferenceEquals(actor, target) && spring.Root != null && c.transform.IsChildOf(spring.Root)) continue;
                            spring.Targets.Add(c);
                        }
                    }
                    float radius = actor.Shape.Height * plugin.RadiusRatio.Value;
                    UpdateGround(actor, spring, radius);
                    if (spring.Ground.enabled) spring.Targets.Add(spring.Ground);
                    if (spring.Binding.Apply(spring.Targets, radius, actor.Shape.Height * plugin.LimitRatio.Value))
                    { springs++; obstacles += spring.Targets.Count; }
                    else { waiting++; waitingReason = spring.Binding.FailureReason; }
                }
            }
            faultCount = 0;
            if (plugin.ShowStatusOverlay.Value)
                Status = $"Collision Plus {Plugin.Version}: native BoneSpring | actors={actors.Count}, active springs={springs}, waiting={waiting}, colliders={obstacles}\n" +
                    (waiting > 0 ? waitingReason : "No custom triangle solver / bone offsets; belly uses latest completed deformation");
            if (plugin.Diagnostics.Value)
            {
                elapsedMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                steps++;
                if (Time.unscaledTime >= nextReport)
                {
                    nextReport = Time.unscaledTime + 15;
                    plugin.Log.LogInfo($"[Native collision] actors={actors.Count}, springs={springs}, waiting={waiting}, colliderReferences={obstacles}, preparation={elapsedMs / Math.Max(1, steps):F3}ms/pass. Native solver cost is not included. {waitingReason}");
                    elapsedMs = 0; steps = 0;
                }
            }
        }
        catch (Exception error)
        {
            Clear();
            retryAt = Time.unscaledTime + Math.Min(30, ++faultCount * 3);
            Status = "Native collision unavailable: original physics restored; see LogOutput.log";
            plugin.Log.LogError("[Native collision] Removed added colliders after failure: " + error);
        }
    }

    private static bool IsActive(Human human) => human != null && !human.Disposed && !human.IsReloading &&
        human.IsActiveAndEnabled && human.HiPoly && human.Transform != null;

    private void Scan()
    {
        var keep = new HashSet<IntPtr>(); characterRoots.Clear();
        var humans = Human._list;
        if (humans != null)
            for (int i = 0; i < humans.Count; i++)
            {
                var human = humans[i];
                if (human == null || human.Disposed || human.Transform == null) continue;
                characterRoots.Add(human.Transform);
                if (!IsActive(human) || keep.Count >= plugin.MaxCharacters.Value) continue;
                var body = human.Body;
                var root = body?.GetObjBone();
                if (root == null) continue;
                keep.Add(human.Pointer);
                var setting = body.magicaSpringSetting;
                long signature = ((long)root.GetInstanceID() << 32) ^ (body.objHitBody?.GetInstanceID() ?? 0) ^ (setting?.Pointer.ToInt64() ?? 0);
                var entries = setting?.Get((HumanMagicaSpringSetting.Type)0, HumanMagicaSpringSetting.Side.All);
                if (entries != null) foreach (var entry in entries)
                    if (entry?.Spring != null)
                        signature = unchecked(signature * 31 + entry.Spring.GetInstanceID() + (entry.Spring.Process?.TeamId ?? 0));
                if (actors.TryGetValue(human.Pointer, out var existing))
                {
                    if (existing.Signature == signature) continue;
                    existing.Dispose(); actors.Remove(human.Pointer);
                }
                var actor = new Actor(human, signature, root);
                actors.Add(human.Pointer, actor);
                if (plugin.Diagnostics.Value) plugin.Log.LogInfo($"[Native bind] actor={human.GameObject.GetInstanceID()}, breastTeams={actor.Springs.Count}, bellyReady={actor.Shape.Ready}, height={actor.Shape.Height:F3}");
            }
        foreach (var key in actors.Keys.Where(key => !keep.Contains(key)).ToArray())
        { actors[key].Dispose(); actors.Remove(key); }
    }

    private void UpdateGround(Actor actor, Spring spring, float radius)
    {
        if (!plugin.BreastGround.Value || spring.ContactBone == null)
        { if (spring.Ground.enabled) spring.Ground.enabled = false; return; }
        Vector3 point = spring.ContactBone.position;
        float search = actor.Shape.Height * .12f;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * search, Vector3.down, hits,
            search + radius * 2, plugin.GroundMask.Value, QueryTriggerInteraction.Ignore);
        bool found = false; RaycastHit best = default; float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i]; var collider = hit.collider;
            if (collider == null || collider.isTrigger || hit.normal.y < plugin.GroundNormal.Value) continue;
            bool character = false;
            foreach (var root in characterRoots)
                if (root != null && collider.transform.IsChildOf(root)) { character = true; break; }
            if (character) continue;
            float signed = Vector3.Dot(point - hit.point, hit.normal);
            // Do not catch an overhead platform when the entire breast point is underneath.
            if (signed < -radius || signed > radius * 2) continue;
            float distance = Math.Abs(signed);
            if (distance >= nearest) continue;
            nearest = distance; best = hit; found = true;
        }
        if (!found)
        { if (spring.Ground.enabled) spring.Ground.enabled = false; return; }
        spring.Ground.transform.position = best.point;
        spring.Ground.transform.rotation = Quaternion.FromToRotation(Vector3.up, best.normal);
        if (!spring.Ground.enabled) spring.Ground.enabled = true;
    }

    private void Clear()
    {
        // Detach all teams before destroying obstacles shared with other actors.
        foreach (var actor in actors.Values) actor.Detach();
        foreach (var actor in actors.Values) actor.Dispose();
        actors.Clear(); characterRoots.Clear(); nextScan = 0;
    }
    public void Dispose() { if (disposed) return; Clear(); disposed = true; NativeBodyShape.RefreshInput = null; }

    private readonly record struct BodyCollider(ColliderComponent Component, bool Abdomen, bool Breast);
    private sealed class Actor : IDisposable
    {
        internal readonly Human Human;
        internal readonly long Signature;
        internal readonly GameObject BoneObject;
        internal readonly NativeBodyShape Shape;
        internal readonly List<Spring> Springs = new();
        internal readonly List<BodyCollider> NativeColliders = new();
        internal bool Active;
        internal Actor(Human human, long signature, GameObject boneObject)
        {
            Human = human; Signature = signature; BoneObject = boneObject;
            Shape = new NativeBodyShape(human);
            try
            {
                var entries = human.Body.magicaSpringSetting?.Get((HumanMagicaSpringSetting.Type)0, HumanMagicaSpringSetting.Side.All);
                var seen = new HashSet<int>();
                if (entries != null) foreach (var entry in entries)
                    if (entry?.Spring != null && seen.Add(entry.Spring.GetInstanceID())) Springs.Add(new Spring(entry.Spring));
                var colliders = human.Body.MagicaClothColliders;
                if (colliders != null) foreach (var collider in colliders)
                {
                    if (collider == null) continue;
                    string name = collider.name.ToLowerInvariant();
                    NativeColliders.Add(new(collider, name.Contains("waist") || name.Contains("spine01"), name.Contains("bust") || name.Contains("mune")));
                }
            }
            catch { Dispose(); throw; }
        }
        internal void Detach() { foreach (var spring in Springs) spring.Binding.Dispose(); }
        public void Dispose()
        {
            Detach();
            foreach (var spring in Springs) spring.Dispose();
            Shape.Dispose();
        }
    }

    private sealed class Spring : IDisposable
    {
        internal readonly NativeSpringBinding Binding;
        internal readonly List<ColliderComponent> Targets = new();
        internal readonly Transform Root, ContactBone;
        internal readonly MagicaPlaneCollider Ground;
        internal Spring(MagicaCloth cloth)
        {
            Binding = new NativeSpringBinding(cloth);
            var roots = cloth.SerializeData.rootBones;
            Root = roots != null && roots.Count > 0 ? roots[0] : null;
            ContactBone = Root;
            if (Root != null) foreach (var bone in Root.GetComponentsInChildren<Transform>(true))
                if (bone.name.ToLowerInvariant().Contains("bust02") && !bone.name.ToLowerInvariant().Contains("tip")) { ContactBone = bone; break; }
            var obj = new GameObject("ALCollisionPlus_NativeGround");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Ground = obj.AddComponent<MagicaPlaneCollider>();
            Ground.enabled = false;
        }
        public void Dispose()
        {
            Binding.Dispose();
            if (Ground != null) { Ground.enabled = false; UnityEngine.Object.Destroy(Ground.gameObject); }
        }
    }
}
