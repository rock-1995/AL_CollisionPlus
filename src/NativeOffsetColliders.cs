using MagicaCloth2;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace ALCollisionPlus;

// Contact at (particle + offset) against C is identical to contact at the
// original particle against (C - offset). Native spring constraints stay intact.
internal sealed class NativeOffsetColliders : IDisposable
{
    private static int pass;
    private static readonly Dictionary<IntPtr, SourceState> sources = new();
    internal static void BeginPass() => pass++;

    // One native read per source/pass, shared by every receiving breast.
    private sealed class SourceState
    {
        internal readonly ColliderComponent Source;
        internal readonly Transform Transform;
        internal readonly MagicaCapsuleCollider Capsule;
        internal int References, Version, Flags;
        internal Vector3 Size, Center;
        internal NMatrix Inverse;
        internal bool Active;
        private int updated = -1;
        internal SourceState(ColliderComponent source) { Source=source;Transform=source.transform;Capsule=source.TryCast<MagicaCapsuleCollider>(); }
        internal void Update()
        {
            if(updated==pass)return;updated=pass;
            Active=Source!=null && Source.isActiveAndEnabled;
            if(!Active)return;
            var size=Source.GetSize();var center=Source.center;
            int flags=Capsule==null?0:(int)Capsule.direction|(Capsule.reverseDirection?4:0)|(Capsule.alignedOnCenter?8:0);
            if(Version==0||size.x!=Size.x||size.y!=Size.y||size.z!=Size.z||center.x!=Center.x||center.y!=Center.y||center.z!=Center.z||flags!=Flags)
            {Size=size;Center=center;Flags=flags;Version++;}
            var m=Transform.worldToLocalMatrix;
            Inverse=new(m.m00,m.m10,m.m20,m.m30,m.m01,m.m11,m.m21,m.m31,m.m02,m.m12,m.m22,m.m32,m.m03,m.m13,m.m23,m.m33);
        }
    }

    private sealed class Copy : IDisposable
    {
        internal readonly ColliderComponent Proxy;
        private readonly Transform transform;
        private readonly MagicaCapsuleCollider proxyCapsule;
        private readonly SourceState state;
        private readonly IntPtr sourceId;
        private int version;
        private bool active;

        internal Copy(ColliderComponent source)
        {
            sourceId=source.Pointer;
            if(!sources.TryGetValue(sourceId,out state))sources.Add(sourceId,state=new SourceState(source));
            state.References++;
            var obj = new GameObject("ALCollisionPlus_OffsetContact");
            obj.SetActive(false); transform = obj.transform;
            transform.SetParent(state.Transform, false);
            if (state.Capsule != null) Proxy = proxyCapsule = obj.AddComponent<MagicaCapsuleCollider>();
            else if (source.TryCast<MagicaSphereCollider>() != null) Proxy = obj.AddComponent<MagicaSphereCollider>();
            else if (source.TryCast<MagicaPlaneCollider>() != null) Proxy = obj.AddComponent<MagicaPlaneCollider>();
            else
            {
                UnityEngine.Object.Destroy(obj);
                if(--state.References==0)sources.Remove(sourceId);
                throw new NotSupportedException("Unknown native collider: " + source.GetType().Name);
            }
            Proxy.enabled = false; obj.SetActive(true);
        }

        internal void Update(Vector3 offset)
        {
            state.Update();
            if(!state.Active) {if(active && Proxy!=null)Proxy.enabled=false;active=false;return;}
            if (version != state.Version)
            {
                Proxy.center = state.Center;
                if (proxyCapsule != null)
                {
                    proxyCapsule.direction = (MagicaCapsuleCollider.Direction)(state.Flags & 3);
                    proxyCapsule.reverseDirection = (state.Flags & 4)!=0;
                    proxyCapsule.alignedOnCenter = (state.Flags & 8)!=0;
                    proxyCapsule.radiusSeparation = true;
                }
                Proxy.SetSize(state.Size); Proxy.UpdateParameters(); version=state.Version;
            }
            var local=NVector.TransformNormal(new NVector(-offset.x,-offset.y,-offset.z),state.Inverse);
            transform.localPosition = new Vector3(local.X,local.Y,local.Z);
            if(!active){Proxy.enabled=true;active=true;}
        }

        public void Dispose()
        {
            if(--state.References==0)sources.Remove(sourceId);
            if (Proxy == null) return;
            Proxy.enabled = false; UnityEngine.Object.Destroy(Proxy.gameObject);
        }
    }

    private readonly Dictionary<IntPtr, Copy> copies = new();
    private readonly HashSet<IntPtr> used = new();
    private readonly List<IntPtr> stale = new();
    internal void Replace(List<ColliderComponent> targets, Vector3 offset)
    {
        used.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            var source = targets[i];
            used.Add(source.Pointer);
            if (!copies.TryGetValue(source.Pointer, out var copy)) copies.Add(source.Pointer, copy = new Copy(source));
            copy.Update(offset); targets[i] = copy.Proxy;
        }
        stale.Clear();
        foreach(var entry in copies) if(!used.Contains(entry.Key)) stale.Add(entry.Key);
        foreach(var key in stale) {copies[key].Dispose();copies.Remove(key);}
    }
    public void Dispose() { foreach (var copy in copies.Values) copy.Dispose(); copies.Clear(); }
}
