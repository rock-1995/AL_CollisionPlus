using System.Diagnostics;
using System.Reflection;
using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MagicaCloth2;
using UnityEngine;

namespace ALCollisionPlus;

internal sealed partial class NativeCollisionRuntime : IDisposable
{
    private readonly Plugin plugin;
    private readonly Dictionary<IntPtr, Actor> actors = new();
    private readonly List<Transform> characterRoots = new();
    private readonly List<ColliderComponent> hairTargets = new();
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
        lastNativeStep = Time.unscaledTime;
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
        PollDiagnostics();
        if (!plugin.Enabled.Value || plugin.RadiusScale.Value <= 0 || plugin.MaxCharacters.Value <= 0 || !(plugin.BreastAbdomen.Value || plugin.BreastBody.Value || plugin.BreastGround.Value || plugin.BreastHair.Value))
        {
            if (actors.Count != 0) Clear();
            Status = "Native collision OFF: added colliders removed; original parameters restored";
        }
    }

    internal void Step()
    {
        if (disposed) return;
        PollDisabled();
        var trace = BeginTrace();
        if (!plugin.Enabled.Value || plugin.RadiusScale.Value <= 0 || plugin.MaxCharacters.Value <= 0 || !(plugin.BreastAbdomen.Value || plugin.BreastBody.Value || plugin.BreastGround.Value || plugin.BreastHair.Value) || Time.unscaledTime < retryAt)
        {
            trace?.Write("01-CharacterScan", "runtime", $"SKIP: collision switches, scale, or character limit disabled, or waiting to retry after an error；retryAt={CollisionTrace.F(retryAt)}");
            trace?.Write("02-CharacterBinding", "runtime", "SKIP: runtime prerequisites failed");
            trace?.Write("03-BreastSpherePreparation", "runtime", "SKIP: runtime prerequisites failed");
            TraceSkipped(trace, "runtime", "Runtime prerequisites failed. Added native contacts were skipped.");
            return;
        }
        long start = plugin.Diagnostics.Value ? Stopwatch.GetTimestamp() : 0;
        try
        {
            if (Time.unscaledTime >= nextScan) { Scan(); nextScan = Time.unscaledTime + 1; }
            if (trace != null) TraceScan(trace);
            if (trace != null && actors.Count == 0)
            {
                trace.Write("02-CharacterBinding", "runtime", "SKIP: no bound characters. See stage 01");
                trace.Write("03-BreastSpherePreparation", "runtime", "SKIP: no bound characters");
                TraceSkipped(trace, "runtime", "no bound characters");
            }
            // Stop publishing obstacles from disabled/reloading models immediately,
            // before processing any other actor's spring list.
            foreach (var actor in actors.Values)
            {
                actor.Active = IsActive(actor.Human) && actor.Human.Body?.GetObjBone() == actor.BoneObject;
                if (actor.Active)
                {
                    actor.Shape.RefreshHeight();
                    if (plugin.BreastAbdomen.Value) actor.Shape.Update();
                    if (trace != null) TraceActor(trace, actor);
                    actor.UpdateBreastRadii(trace);
                }
                actor.HasBreastContacts = false;
                foreach (var spring in actor.Springs)
                {
                    bool active = actor.Active && spring.RadiusReady;
                    if (spring.ContactSource.enabled != active) spring.ContactSource.enabled = active;
                    if (!active) continue;
                    spring.Radius = spring.BaseRadius * spring.Binding.TeamScale * plugin.RadiusScale.Value;
                    if (spring.SourceRadius != spring.Radius)
                    {
                        spring.ContactSource.SetSize(new Vector3(spring.Radius, spring.Radius, spring.Radius));
                        spring.ContactSource.UpdateParameters();
                        spring.SourceRadius = spring.Radius;
                    }
                    actor.HasBreastContacts = true;
                }
            }
            int springs = 0, obstacles = 0, waiting = 0;
            NativeBodyContacts.BeginPass();
            string waitingReason = null;
            foreach (var actor in actors.Values)
            {
                foreach (var spring in actor.Springs)
                {
                    string scope = trace == null ? null : TraceScope(actor, spring);
                    spring.Targets.Clear();
                    spring.BodyTargets.Clear();
                    if (!actor.Active || !spring.RadiusReady)
                    {
                        spring.HasPreviousCenter = false;
                        spring.BodyContacts.Dispose();
                        spring.Binding.Apply(spring.Targets, 0, 0);
                        spring.Ground.enabled = false;
                        if (actor.Active) { waiting++; waitingReason = "Breast body geometry is still loading."; }
                        TraceSkipped(trace, scope, actor.Active ? "RadiusReady=false. Body contacts were skipped. See stage 03." : "actor inactive");
                        continue;
                    }
                    foreach (var target in actors.Values)
                    {
                        if (!target.Active) continue;
                        if (plugin.BreastAbdomen.Value && target.Shape.Ready)
                        {
                            var belly = target.Shape.BellyColliders;
                            for (int i = 0; i < belly.Count; i++) spring.BodyTargets.Add(belly[i]);
                        }
                        if (plugin.BreastBody.Value && !ReferenceEquals(actor, target))
                            foreach (var otherSpring in target.Springs)
                                if (otherSpring.ContactSource.enabled) spring.BodyTargets.Add(otherSpring.ContactSource);
                        var candidates = ReferenceEquals(actor, target) ? spring.SelfColliders : target.NativeColliders;
                        foreach (var candidate in candidates)
                        {
                            var c = candidate.Component;
                            bool abdomen = candidate.Abdomen;
                            // A fitted belly replaces the flat abdomen envelope.
                            if (abdomen && (!plugin.BreastAbdomen.Value || target.Shape.Ready)) continue;
                            if (!abdomen && !plugin.BreastBody.Value) continue;
                            if (candidate.Breast && (ReferenceEquals(actor, target) || target.HasBreastContacts)) continue;
                            // Self descendants were excluded once when this rig was bound.
                            // Shared Source.Update checks native validity once per pass.
                            spring.BodyTargets.Add(c);
                        }
                    }
                    float radius = spring.Radius;
                    // BoneSpring rotates as well as translates its root. The
                    // rest-pose offset otherwise retracts the real contact center
                    // after a correct root push. Transport the last solved offset
                    // with the animated parent so a settled contact stays exact.
                    spring.WorldOffset = spring.HasPreviousCenter
                        ? spring.Frame.TransformVector(spring.CompletedLocalOffset)
                        : spring.Root.TransformVector(spring.LocalOffset);
                    // Keep contact geometry anchored to the authored pose;
                    // the solved offset only maps that support to the native root.
                    var center = spring.Root.TransformPoint(spring.LocalOffset);
                    UpdateGround(actor, spring, radius);
                    if (spring.Ground.enabled) spring.BodyTargets.Add(spring.Ground);
                    if (trace != null) TraceCollection(trace, actor, spring);
                    spring.BodyContacts.Replace(spring.BodyTargets, spring.HasPreviousCenter ? spring.PreviousCenter : center, center, radius, spring.WorldOffset, spring.Frame, limit: actor.Shape.Height * plugin.LimitRatio.Value, trace: trace, traceScope: scope);
                    spring.Targets.AddRange(spring.BodyTargets);
                    bool applied = spring.Binding.Apply(spring.Targets, radius, actor.Shape.Height * plugin.LimitRatio.Value);
                    if (trace != null)
                    {
                        TraceSubmission(trace, actor, spring, applied, radius);
                        TraceBeforeSolve(trace, actor, spring);
                    }
                    if (applied)
                    {
                        springs++; obstacles += spring.Targets.Count;
                        if (!spring.Reported)
                        {
                            spring.Reported = true;
                            plugin.Log.LogInfo($"[Native breast contact] root={spring.Root.name}, radius={radius:F4}, targets={spring.Targets.Count}, bodyActors={actors.Count}.");
                        }
                    }
                    else { waiting++; waitingReason = spring.Binding.FailureReason; }
                }
            }
            hairTargets.Clear();
            if (plugin.BreastHair.Value)
                foreach (var source in actors.Values)
                    if (source.Active) foreach (var spring in source.Springs)
                        if (spring.ContactSource.enabled) hairTargets.Add(spring.ContactSource);
            foreach (var actor in actors.Values)
            {
                if (actor.Active) actor.Hair.Update(hairTargets);
                else actor.Hair.Dispose();
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
            trace?.Write("ERROR", "runtime", error.ToString());
        }
    }

    // Cross-character breast obstacles use the last completed native pose, not
    // the animation pose restored before each solve. This lets both sides yield.
    internal void CaptureBreastContacts()
    {
        if (disposed) return;
        foreach (var actor in actors.Values)
            if (actor.Active)
                foreach (var spring in actor.Springs)
                    if (spring.RadiusReady && spring.Root != null && spring.ContactSource != null)
                    {
                        spring.PreviousCenter = spring.Root.TransformPoint(spring.LocalOffset);
                        var solvedOffset = spring.Frame.InverseTransformVector(spring.Root.TransformVector(spring.LocalOffset));
                        // This is a feedback estimate: moving the spring root
                        // changes its rotation again. Under-relax that estimate,
                        // including at low frame rates, instead of immediately
                        // feeding each spring rotation back into the support.
                        spring.CompletedLocalOffset = spring.HasPreviousCenter
                            ? Vector3.Lerp(spring.CompletedLocalOffset, solvedOffset, MathF.Min(.12f, 1-MathF.Exp(-6*Time.deltaTime)))
                            : solvedOffset;
                        spring.HasPreviousCenter = true;
                        spring.ContactSource.transform.position = spring.PreviousCenter;
                    }
        CompleteTrace();
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
        Vector3 point = spring.Root.TransformPoint(spring.LocalOffset);
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
        internal readonly NativeHairContacts Hair;
        internal readonly NativePalmColliders Palms;
        internal readonly List<Spring> Springs = new();
        internal readonly List<BodyCollider> NativeColliders = new();
        internal bool Active, HasBreastContacts;
        private NativeBreastSurface radiusSurface;
        internal Actor(Human human, long signature, GameObject boneObject)
        {
            Human = human; Signature = signature; BoneObject = boneObject;
            Shape = new NativeBodyShape(human);
            Hair = new NativeHairContacts(human);
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
                    if (name.StartsWith("alcollisionplus_", StringComparison.Ordinal)) continue;
                    NativeColliders.Add(new(collider, name.Contains("waist") || name.Contains("spine01"), name.Contains("bust") || name.Contains("mune")));
                }
                Palms = new NativePalmColliders(boneObject);
                foreach (var palm in Palms.Colliders) NativeColliders.Add(new(palm, false, false));
                foreach (var spring in Springs)
                    foreach (var candidate in NativeColliders)
                        if (spring.Root == null || !candidate.Component.transform.IsChildOf(spring.Root))
                            spring.SelfColliders.Add(candidate);
            }
            catch { Dispose(); throw; }
        }
        internal void Detach() { Hair.Dispose(); foreach (var spring in Springs) spring.Binding.Dispose(); }
        internal void UpdateBreastRadii(CollisionTrace trace = null)
        {
            var surface = Shape.GetBreastSurface();
            if (!ReferenceEquals(surface, radiusSurface))
            {
                radiusSurface = surface;
                foreach (var spring in Springs) spring.RadiusReady = false;
            }
            bool needed = false;
            foreach (var spring in Springs)
                if (!spring.RadiusReady || spring.RadiusRevision != spring.Binding.ParameterRevision) { needed = true; break; }
            if (!needed)
            {
                if (trace != null) foreach (var spring in Springs) trace.Write("03-BreastSpherePreparation", TraceScope(this,spring), $"READY cached: baseRadius={CollisionTrace.F(spring.BaseRadius)}, localOffset={CollisionTrace.V(spring.LocalOffset)}, revision={spring.RadiusRevision}");
                return;
            }
            bool ready = false;
            foreach (var spring in Springs)
            {
                string scope = trace == null ? null : TraceScope(this,spring);
                if (spring.RadiusReady && spring.RadiusRevision == spring.Binding.ParameterRevision)
                { trace?.Write("03-BreastSpherePreparation", scope, "READY: cached fit for this side"); continue; }
                if (surface == null)
                { trace?.Write("03-BreastSpherePreparation", scope, "WAIT: BreastSurface missing. The breast mesh has not loaded."); continue; }
                if (!spring.Binding.IsReady)
                { trace?.Write("03-BreastSpherePreparation", scope, "WAIT: native binding: " + spring.Binding.FailureReason); continue; }
                if (spring.ContactBone == null || spring.ContactTip == null)
                { trace?.Write("03-BreastSpherePreparation", scope, $"WAIT: middleMissing={spring.ContactBone==null}, tipMissing={spring.ContactTip==null}"); continue; }
                float scale = spring.Binding.TeamScale;
                if (!(scale > 0) || !float.IsFinite(scale))
                { trace?.Write("03-BreastSpherePreparation", scope, $"WAIT: invalid team scale={CollisionTrace.F(scale)}"); continue; }
                if (!ready)
                {
                    ready = surface.Prepare(trace, scope);
                    if (!ready)
                    {
                        trace?.Write("03-BreastSpherePreparation", TraceScope(this), "WAIT: shared breast skinning preparation failed. Remaining springs will be fitted on a later pass.");
                        return;
                    }
                }
                // Cache serialized distances; subsequent actor scaling belongs to Magica.
                var fitted = surface.Fit(spring.Root, spring.ContactBone, spring.ContactTip);
                spring.BaseRadius = fitted.Radius / scale;
                spring.LocalOffset = spring.Root.InverseTransformPoint(new Vector3(fitted.Center.X,fitted.Center.Y,fitted.Center.Z));
                spring.HasPreviousCenter = false;
                spring.ContactSource.transform.position = new Vector3(fitted.Center.X,fitted.Center.Y,fitted.Center.Z);
                spring.RadiusRevision = spring.Binding.ParameterRevision;
                spring.RadiusReady = true;
                trace?.Write("03-BreastSpherePreparation", scope, $"READY fitted: baseRadius={CollisionTrace.F(spring.BaseRadius)}, teamScale={CollisionTrace.F(scale)}, worldCenter={CollisionTrace.V(fitted.Center)}, localOffset={CollisionTrace.V(spring.LocalOffset)}");
            }
        }
        public void Dispose()
        {
            Detach();
            foreach (var spring in Springs) spring.Dispose();
            Palms?.Dispose();
            Shape.Dispose();
        }
    }

    private sealed class Spring : IDisposable
    {
        internal readonly NativeSpringBinding Binding;
        internal readonly List<ColliderComponent> Targets = new(), BodyTargets = new();
        internal readonly List<BodyCollider> SelfColliders = new();
        internal readonly NativeBodyContacts BodyContacts = new();
        internal Vector3 PreviousCenter;
        internal bool HasPreviousCenter;
        internal readonly Transform Root, Frame, ContactBone, ContactTip;
        internal bool RadiusReady;
        internal int RadiusRevision;
        internal float BaseRadius;
        internal float Radius, SourceRadius = float.NaN;
        internal bool Reported;
        internal Vector3 LocalOffset, WorldOffset, CompletedLocalOffset;
        internal readonly MagicaPlaneCollider Ground;
        internal readonly MagicaSphereCollider ContactSource;
        internal Spring(MagicaCloth cloth)
        {
            Binding = new NativeSpringBinding(cloth);
            var roots = cloth.SerializeData.rootBones;
            Root = roots != null && roots.Count > 0 ? roots[0] : null;
            Frame = Root != null ? Root.parent : null;
            ContactBone = Root;
            if (Root != null) foreach (var bone in Root.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.StartsWith("cf_j_bust02_", StringComparison.OrdinalIgnoreCase)) ContactBone = bone;
                if (bone.name.StartsWith("cf_y_bust02_tip_", StringComparison.OrdinalIgnoreCase)) ContactTip = bone;
            }
            var obj = new GameObject("ALCollisionPlus_NativeGround");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Ground = obj.AddComponent<MagicaPlaneCollider>();
            Ground.enabled = false;
            var contact = new GameObject("ALCollisionPlus_BreastContact");
            UnityEngine.Object.DontDestroyOnLoad(contact);
            ContactSource = contact.AddComponent<MagicaSphereCollider>();
            ContactSource.enabled = false;
        }
        public void Dispose()
        {
            Binding.Dispose();
            BodyContacts.Dispose();
            if (ContactSource != null) { ContactSource.enabled = false; UnityEngine.Object.Destroy(ContactSource.gameObject); }
            if (Ground != null) { Ground.enabled = false; UnityEngine.Object.Destroy(Ground.gameObject); }
        }
    }
}
