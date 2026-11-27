using MagicaCloth2;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using HarmonyLib;

namespace ALCollisionPlus;

// Shared geometry reads, but independent approach history for each breast.
internal sealed class NativeBodyContacts : IDisposable
{
    private static int pass;
    private static readonly Dictionary<IntPtr, Source> sources = new();
    internal static void BeginPass() => pass++;
    internal static void Install(Harmony harmony)
    {
        var callback=new HarmonyMethod(typeof(NativeBodyContacts),nameof(ParametersChanged));
        harmony.Patch(AccessTools.Method(typeof(ColliderComponent),nameof(ColliderComponent.UpdateParameters)),postfix:callback);
        foreach(var type in new[]{typeof(ColliderComponent),typeof(MagicaSphereCollider),typeof(MagicaCapsuleCollider)})
            foreach(var method in AccessTools.GetDeclaredMethods(type))
                if(method.Name.StartsWith("SetSize",StringComparison.Ordinal))harmony.Patch(method,postfix:callback);
    }
    private static void ParametersChanged(ColliderComponent __instance)
    {
        if(sources.Count>0 && __instance is not null && sources.TryGetValue(__instance.Pointer,out var source))source.Dirty=true;
    }
    private static NVector V(Vector3 v) => new(v.x,v.y,v.z);
    private static Vector3 V(NVector v) => new(v.X,v.Y,v.Z);

    private sealed class Source
    {
        internal readonly ColliderComponent Collider;
        internal readonly Transform Transform;
        internal readonly MagicaCapsuleCollider Capsule;
        internal readonly bool IsPlane;
        internal NVector PlaneNormal;
        internal int References;
        internal bool Active;
        internal ContactGeometry.Shape Previous, Current;
        internal NQuaternion RotationDelta = NQuaternion.Identity;
        private NQuaternion rotation = NQuaternion.Identity;
        internal bool Dirty=true;
        internal Vector3 size,localCenter,Position,Scale;
        internal Quaternion Rotation;
        internal int axis,Version;
        internal bool reverse,aligned;
        private int updated = -1;
        internal Source(ColliderComponent c) { Collider=c;Transform=c.transform;Capsule=c.TryCast<MagicaCapsuleCollider>();IsPlane=c.TryCast<MagicaPlaneCollider>() is not null; }
        internal void Update()
        {
            if(updated==pass)return;
            bool continuous = updated == pass-1 && Active;
            updated=pass;
            // Runtime already limits these inputs to active character rigs.
            // AL can register Magica components on inactive GameObjects; we
            // only read their geometry and submit our own active support plane.
            // Preserve an explicitly disabled component without requiring its
            // GameObject (or membership in another native team) to be active.
            Active=Collider!=null && Collider.enabled;
            if(!Active)return;
            Previous=Current;
            if(Dirty)
            {
                size=Collider.GetSize();localCenter=Collider.center;
                if(Capsule is not null){axis=(int)Capsule.direction;reverse=Capsule.reverseDirection;aligned=Capsule.alignedOnCenter;}
                Dirty=false;Version++;
            }
            Transform.GetPositionAndRotation(out var position,out var q);
            var scale=Transform.lossyScale;
            Position=position;Rotation=q;Scale=scale;
            var nextRotation=new NQuaternion(q.x,q.y,q.z,q.w);
            RotationDelta=continuous?nextRotation*NQuaternion.Conjugate(rotation):NQuaternion.Identity;
            rotation=nextRotation;
            // Magica SimulationPreUpdate mirrors the center, rotates it, THEN
            // applies absolute scale component-wise (not TransformPoint/TRS).
            // Keep this order for nonzero centers and nonuniform/mirrored rigs.
            var sign=new NVector(MathF.Sign(scale.x),MathF.Sign(scale.y),MathF.Sign(scale.z));
            var center=V(position)+NVector.Transform(V(localCenter)*sign,nextRotation)*NVector.Abs(V(scale));
            if(IsPlane)
            {
                Current=new(center,center,0,0);
                PlaneNormal=NVector.Transform(NVector.UnitY,nextRotation);
            }
            else if(Capsule is null)
            {
                float radius=size.x*MathF.Abs(scale.x);
                Current=new(center,center,radius,radius);
            }
            else
            {
                float axisScale=axis==0?scale.x:axis==1?scale.y:scale.z;
                float s=MathF.Abs(axisScale), a=size.x*s, b=size.y*s, length=size.z*s;
                var local=axis==0?NVector.UnitX:axis==1?NVector.UnitY:NVector.UnitZ;
                var direction=NVector.Transform(local,nextRotation)*(axisScale<0?-1:1)*(reverse?-1:1);
                float first=aligned?MathF.Max(0,length*.5f-a):0;
                float second=aligned?MathF.Max(0,length*.5f-b):MathF.Max(0,length-a-b);
                Current=new(center+direction*first,center-direction*second,a,b);
            }
            if(!continuous)Previous=Current;
        }
    }
    private sealed class Contact : IDisposable
    {
        internal readonly Source Source;
        internal bool Active;
        private NVector normal;
        internal NVector EntryNormal => normal;
        internal Contact(ColliderComponent c)
        {
            if(!sources.TryGetValue(c.Pointer,out Source))sources.Add(c.Pointer,Source=new Source(c));
            Source.References++;
        }
        internal void Prepare(NVector previous,NVector current,float radius)
        {
            Source.Update();
            if(!Source.Active){normal=default;Active=false;return;}
            if(Source.IsPlane){normal=Source.PlaneNormal;Active=true;return;}
            if(normal.LengthSquared()>0)normal=NVector.Normalize(NVector.Transform(normal,Source.RotationDelta));
            Active=ContactGeometry.Plane(Source.Previous,Source.Current,previous,current,radius,ref normal,out _,out _);
        }
        internal void Project(ref NVector center,float radius)
        {
            if(!Source.Active)return;
            if(Source.IsPlane)
            {
                float depth=radius-NVector.Dot(center-Source.Current.A,normal);
                if(depth>0)center+=normal*depth;
                return;
            }
            var obstacle=Source.Current.Closest(center,out float obstacleRadius);
            var delta=center-obstacle;
            float reach=radius+obstacleRadius;
            // Another contact can push the receiver into a shape that did not
            // touch its authored position. Test the projected point as well;
            // preselecting by the authored point creates an every-other-frame
            // contact at crowded arm/chest poses. A released entry plane must
            // still stay finite outside the actual sphere/capsule.
            if(!Active && delta.LengthSquared()>=reach*reach)return;
            // Project along the remembered approach direction to the actual
            // curved boundary. Radial projections can choose different exits
            // each frame when a thigh and abdomen overlap; switching to a flat
            // entry plane on the far hemisphere also introduces a discontinuity.
            // The ray/sphere intersection stays on the entry side and only
            // exists within the finite silhouette of this contact.
            if(normal.LengthSquared()>0)
            {
                float signed=NVector.Dot(delta,normal);
                var tangent=delta-normal*signed;
                float boundarySquared=reach*reach-tangent.LengthSquared();
                if(boundarySquared>0)
                {
                    float depth=MathF.Sqrt(boundarySquared)-signed;
                    if(depth>0)center+=normal*depth;
                }
                return;
            }
            float distance=delta.Length();
            if(distance<reach && distance>1e-6f)
                center+=delta*((reach-distance)/distance);
        }
        public void Dispose(){if(--Source.References==0)sources.Remove(Source.Collider.Pointer);}
    }

    private readonly Dictionary<IntPtr,Contact> contacts=new();
    private readonly List<Contact> ordered=new();
    private readonly HashSet<IntPtr> used=new();
    private readonly List<IntPtr> stale=new();
    private MagicaPlaneCollider support;
    private Transform supportTransform;
    private NVector filteredOffset;
    private Quaternion previousRotation=Quaternion.identity;
    private bool hasSupport;

    internal void Replace(List<ColliderComponent> targets,Vector3 previous,Vector3 current,float radius,Vector3 offset,
        Transform frame=null,float response=18,float limit=0,CollisionTrace trace=null,string traceScope=null)
    {
        bool same=targets.Count==ordered.Count;
        if(same)for(int i=0;i<targets.Count;i++)
            if(!ReferenceEquals(targets[i],ordered[i].Source.Collider) && targets[i].Pointer!=ordered[i].Source.Collider.Pointer){same=false;break;}
        if(!same)
        {
            used.Clear();ordered.Clear();
            foreach(var source in targets)
            {
                used.Add(source.Pointer);
                if(!contacts.TryGetValue(source.Pointer,out var contact))contacts.Add(source.Pointer,contact=new Contact(source));
                ordered.Add(contact);
            }
            stale.Clear();foreach(var item in contacts)if(!used.Contains(item.Key))stale.Add(item.Key);
            foreach(var key in stale){contacts[key].Dispose();contacts.Remove(key);}
        }
        targets.Clear();
        var initial=V(current);
        foreach(var contact in ordered)contact.Prepare(V(previous),initial,radius);
        if(trace!=null)TraceContacts(trace,traceScope,initial,radius);
        var solved=initial;
        // Magica averages simultaneous point contacts (and further attenuates
        // opposing normals). Resolve this tiny set of analytic shapes first,
        // then give its existing spring ONE support instead of cancelling pushes.
        for(int iteration=0;iteration<6;iteration++)
        {
            var before=solved;
            foreach(var contact in ordered)contact.Project(ref solved,radius);
            if(NVector.DistanceSquared(before,solved)<=radius*radius*1e-8f)break;
        }
        var wanted=solved-initial;
        float wantedLength=wanted.Length();
        trace?.Write("06-CombinedCorrection",traceScope,$"center={CollisionTrace.V(current)}, previousCenter={CollisionTrace.V(previous)}, radius={CollisionTrace.F(radius)}, rawCorrection={CollisionTrace.V(wanted)}, rawLength={CollisionTrace.F(wantedLength)}, limit={CollisionTrace.F(limit)}, clamped={limit>0 && wantedLength>limit}, rootOffset={CollisionTrace.V(offset)}");
        if(limit>0 && wantedLength>limit)wanted*=limit/wantedLength;
        var rotation=frame!=null?frame.rotation:Quaternion.identity;
        float alpha=response>0?1-MathF.Exp(-response*Time.deltaTime):1;
        if(hasSupport)
        {
            // Smooth only the collision correction in the animated body's
            // frame. Whole-body turns and translation incur no artificial lag.
            var transported=V((rotation*Quaternion.Inverse(previousRotation))*V(filteredOffset));
            filteredOffset=NVector.Lerp(transported,wanted,alpha);
        }
        else filteredOffset=wanted*alpha;
        previousRotation=rotation;hasSupport=true;
        float length=filteredOffset.Length();
        // Release in the receiver's frame, not the moving obstacle's frame.
        // This removes an on/off impulse without following a departed arm.
        if(length<=radius*1e-4f)
        {
            trace?.Write("06-SupportSurface",traceScope,$"released: filteredLength={CollisionTrace.F(length)}, releaseThreshold={CollisionTrace.F(radius*1e-4f)}, limitedCorrection={CollisionTrace.V(wanted)}");
            if(support!=null)support.enabled=false;
            hasSupport=false;filteredOffset=default;return;
        }
        var direction=filteredOffset/length;
        if(support==null)
        {
            var go=new GameObject("ALCollisionPlus_CombinedContact");UnityEngine.Object.DontDestroyOnLoad(go);
            supportTransform=go.transform;support=go.AddComponent<MagicaPlaneCollider>();
        }
        supportTransform.SetPositionAndRotation(V(initial+filteredOffset-direction*radius-V(offset)),
            Quaternion.FromToRotation(Vector3.up,V(direction)));
        if(!support.enabled)support.enabled=true;
        targets.Add(support);
        trace?.Write("06-SupportSurface",traceScope,$"published={CollisionTrace.Name(support)}, filteredCorrection={CollisionTrace.V(filteredOffset)}, alpha={CollisionTrace.F(alpha)}, planePosition={CollisionTrace.V(supportTransform.position)}, planeNormal={CollisionTrace.V(supportTransform.up)}, enabled={support.enabled}, active={support.gameObject.activeInHierarchy}");
    }

    private void TraceContacts(CollisionTrace trace,string scope,NVector center,float radius)
    {
        trace.Read("05-GeometricContact",scope,()=>
        {
            int active=0,touching=0,arms=0,armsActive=0,armsTouching=0;
            var rows=new List<string>();
            foreach(var contact in ordered)
            {
                var source=contact.Source;var c=source.Collider;
                if(c==null){rows.Add("source destroyed: EXCLUDED");continue;}
                bool arm=CollisionTrace.IsArm(c.name);
                if(source.Active)active++;if(contact.Active)touching++;
                if(arm){arms++;if(source.Active)armsActive++;if(contact.Active)armsTouching++;}
                if(!source.Active)
                {
                    rows.Add($"{CollisionTrace.Name(c)}: EXCLUDED, enabled={c.enabled}, activeInHierarchy={c.gameObject.activeInHierarchy}");
                    continue;
                }
                var closest=source.Current.Closest(center,out float obstacleRadius);
                float gap=source.IsPlane?NVector.Dot(center-source.Current.A,source.PlaneNormal)-radius:NVector.Distance(center,closest)-radius-obstacleRadius;
                // Include every arm/palm and every actual overlap, plus totals
                // for distant body obstacles. This does not filter the solver.
                if(arm || contact.Active || gap<0)
                    rows.Add($"{CollisionTrace.Name(c)}: valid=True, activeInHierarchy={c.gameObject.activeInHierarchy}, contact={contact.Active}, gap={CollisionTrace.F(gap)} (negative=overlap), a={CollisionTrace.V(source.Current.A)}, b={CollisionTrace.V(source.Current.B)}, ra={CollisionTrace.F(source.Current.RadiusA)}, rb={CollisionTrace.F(source.Current.RadiusB)}, entryNormal={CollisionTrace.V(contact.EntryNormal)}");
            }
            return $"sources={ordered.Count}, valid={active}, contacts={touching}, armsAndPalms={arms}, validArmsAndPalms={armsActive}, touchingArmsAndPalms={armsTouching}; "+string.Join("; ",rows);
        });
    }
    public void Dispose()
    {
        foreach(var c in contacts.Values)c.Dispose();contacts.Clear();ordered.Clear();
        if(support!=null){support.enabled=false;UnityEngine.Object.Destroy(support.gameObject);support=null;}
        hasSupport=false;filteredOffset=default;
    }
}
