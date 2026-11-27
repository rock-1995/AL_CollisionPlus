using Character;
using UnityEngine;

namespace ALCollisionPlus;

internal sealed partial class NativeCollisionRuntime
{
    private float nextTrace, lastNativeStep;
    private bool diagnosticsWereEnabled;
    private CollisionTrace pendingTrace;
    private readonly List<(Spring Spring, string Scope, Vector3 Root, Vector3 Center)> traceReceivers = new();

    private void PollDiagnostics()
    {
        if (!plugin.Diagnostics.Value)
        {
            diagnosticsWereEnabled = false;
            pendingTrace = null; traceReceivers.Clear();
            return;
        }
        if (!diagnosticsWereEnabled) { diagnosticsWereEnabled = true; nextTrace = 0; }
        if (Time.unscaledTime - lastNativeStep < 5 || Time.unscaledTime < nextTrace) return;
        nextTrace = Time.unscaledTime + 5;
        var trace = new CollisionTrace(plugin);
        trace.Write("00-Callback", "runtime", $"CollisionDriver.Update is running. ClothUpdate prefix has not run for {CollisionTrace.F(Time.unscaledTime-lastNativeStep)} seconds.enabled={plugin.Enabled.Value}");
    }

    private CollisionTrace BeginTrace()
    {
        lastNativeStep = Time.unscaledTime;
        if (!plugin.Diagnostics.Value || Time.unscaledTime < nextTrace) return null;
        nextTrace = Time.unscaledTime + 5;
        if (pendingTrace != null) pendingTrace.Write("08-NativeOutput", "runtime", "No ClothUpdate postfix was received for the previous sample.");
        traceReceivers.Clear();
        var trace = pendingTrace = new CollisionTrace(plugin);
        trace.Write("00-Switches", "runtime", $"version={Plugin.Version}, time={CollisionTrace.F(Time.unscaledTime)}, enabled={plugin.Enabled.Value}, body={plugin.BreastBody.Value}, abdomen={plugin.BreastAbdomen.Value}, ground={plugin.BreastGround.Value}, hair={plugin.BreastHair.Value}, radiusScale={CollisionTrace.F(plugin.RadiusScale.Value)}, limitRatio={CollisionTrace.F(plugin.LimitRatio.Value)}, maxActors={plugin.MaxCharacters.Value}");
        return trace;
    }

    private void TraceScan(CollisionTrace trace)
    {
        if (trace == null) return;
        trace.Read("01-CharacterScan", "runtime", () =>
        {
            var humans = Human._list;
            if (humans == null) return $"Human._list=<null>, boundActors={actors.Count}";
            var rows = new List<string> { $"listed={humans.Count}, boundActors={actors.Count}" };
            int accepted = 0;
            for (int i = 0; i < humans.Count; i++)
            {
                var h = humans[i];
                if (h == null) { rows.Add($"index={i}: null"); continue; }
                string reason = h.Disposed ? "Disposed" : h.Transform == null ? "Transform missing" :
                    h.IsReloading ? "IsReloading" : !h.IsActiveAndEnabled ? "Human inactive" :
                    !h.HiPoly ? "HiPoly=false" : accepted >= plugin.MaxCharacters.Value ? "MaxCharacters reached" :
                    h.Body?.GetObjBone() == null ? "Body bone root missing" : "eligible";
                if (reason == "eligible") accepted++;
                rows.Add($"index={i}, actor={CollisionTrace.Name(h.GameObject)}, bound={actors.ContainsKey(h.Pointer)}, result={reason}");
            }
            return string.Join("; ", rows);
        });
    }

    private static string TraceScope(Actor actor, Spring spring = null) =>
        $"actor={actor.Human.GameObject?.GetInstanceID()}" + (spring == null ? "" : $"/root={spring.Root?.name ?? "<null>"}");

    private static void TraceActor(CollisionTrace trace, Actor actor)
    {
        if (trace == null) return;
        string scope = TraceScope(actor);
        trace.Read("02-CharacterBinding", scope, () =>
            $"active={actor.Active}, bone={CollisionTrace.Name(actor.BoneObject)}, hitBody={CollisionTrace.Name(actor.Human.Body?.objHitBody)}, springs={actor.Springs.Count}, bodyArray={actor.Human.Body?.MagicaClothColliders?.Length ?? 0}, cachedColliders={actor.NativeColliders.Count}, palms={actor.Palms.Colliders.Count}, height={CollisionTrace.F(actor.Shape.Height)}, bellyReady={actor.Shape.Ready}");
        if (actor.Springs.Count == 0)
        {
            trace.Write("03-BreastSpherePreparation", scope, "SKIP: native breast settings did not provide springs");
            TraceSkipped(trace, scope, "No breast receiver springs");
        }
        foreach (var spring in actor.Springs)
        {
            string receiver = TraceScope(actor, spring);
            trace.Read("02-BreastBinding", receiver, () =>
                $"middle={CollisionTrace.Name(spring.ContactBone)}, tip={CollisionTrace.Name(spring.ContactTip)}, radiusReady={spring.RadiusReady}, cachedRevision={spring.RadiusRevision}, nativeRevision={spring.Binding.ParameterRevision}; {spring.Binding.DiagnosticState()}");
            trace.Read("02-ArmColliders", receiver, () =>
                string.Join("; ", actor.NativeColliders.Where(c => CollisionTrace.IsArm(c.Component.name)).Select(c =>
                    $"{CollisionTrace.Name(c.Component)}, enabled={c.Component.enabled}, activeInHierarchy={c.Component.gameObject.activeInHierarchy}, selfCandidate={spring.SelfColliders.Any(s => s.Component.Pointer == c.Component.Pointer)}")));
        }
        actor.Shape.TraceInputs(trace, scope);
    }

    private void TraceCollection(CollisionTrace trace, Actor actor, Spring spring)
    {
        if (trace == null) return;
        string scope = TraceScope(actor, spring);
        trace.Read("04-CandidateSelection", scope, () =>
        {
            var rows = new List<string> { $"selected={spring.BodyTargets.Count}, ground={spring.Ground.enabled}, breastRadius={CollisionTrace.F(spring.Radius)}" };
            foreach (var target in actors.Values)
                foreach (var candidate in target.NativeColliders)
                {
                    var c = candidate.Component;
                    if (c == null || !CollisionTrace.IsArm(c.name)) continue;
                    bool selected = spring.BodyTargets.Any(t => t.Pointer == c.Pointer);
                    string reason = selected ? "selected (validity checked in stage 05)" : !target.Active ? "target actor inactive" :
                        !plugin.BreastBody.Value ? "BreastBody=false" :
                        ReferenceEquals(actor,target) && c.transform.IsChildOf(spring.Root) ? "self descendant" : "not selected by runtime categories";
                    rows.Add($"actor={target.Human.GameObject.GetInstanceID()} {CollisionTrace.Name(c)}: {reason}");
                }
            return string.Join("; ", rows);
        });
    }

    private static void TraceSkipped(CollisionTrace trace, string scope, string reason)
    {
        if (trace == null) return;
        foreach (var stage in new[] { "04-CandidateSelection", "05-GeometricContact", "06-CombinedCorrection", "07-NativeCommit", "08-NativeOutput" })
            trace.Write(stage, scope, "SKIP: " + reason);
    }

    private void TraceBeforeSolve(CollisionTrace trace, Actor actor, Spring spring)
    {
        if (trace == null) return;
        traceReceivers.Add((spring, TraceScope(actor,spring), spring.Root.position, spring.Root.TransformPoint(spring.LocalOffset)));
    }

    private void TraceSubmission(CollisionTrace trace, Actor actor, Spring spring, bool applied, float radius)
    {
        if (trace == null) return;
        trace.Read("07-NativeCommit", TraceScope(actor,spring), () => $"Apply={applied}, ownedTargets={spring.Targets.Count}, requestedRadius={CollisionTrace.F(radius)}, requestedLimit={CollisionTrace.F(actor.Shape.Height*plugin.LimitRatio.Value)}; {spring.Binding.DiagnosticState()}");
    }

    private void CompleteTrace()
    {
        var trace = pendingTrace;
        if (trace == null) return;
        pendingTrace = null;
        foreach (var entry in traceReceivers)
            trace.Read("08-NativeOutput", entry.Scope, () =>
            {
                var s = entry.Spring;
                return $"observer=ClothUpdate-post, outputFrame={Time.frameCount}, rootBefore={CollisionTrace.V(entry.Root)}, rootAfter={CollisionTrace.V(s.Root.position)}, rootDelta={CollisionTrace.V(s.Root.position-entry.Root)}, centerBefore={CollisionTrace.V(entry.Center)}, centerAfter={CollisionTrace.V(s.Root.TransformPoint(s.LocalOffset))}; {s.Binding.DiagnosticState()}";
            });
        trace.Write("09-SampleComplete", "runtime", "These are before-and-after snapshots from the same frame. Displacement includes original spring motion. The same status is sampled every 5 seconds.");
        traceReceivers.Clear();
    }
}
