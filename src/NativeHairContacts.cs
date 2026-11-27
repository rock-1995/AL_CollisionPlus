using Character;
using MagicaCloth2;
using UnityEngine;
using NativeList = Il2CppSystem.Collections.Generic.List<MagicaCloth2.ColliderComponent>;

namespace ALCollisionPlus;

// Hair receives breast obstacles. No hair collider is ever sent to a breast.
internal sealed class NativeHairContacts : IDisposable
{
    private readonly Human human;
    private readonly Dictionary<int, Binding> bindings = new();
    private float nextScan;
    internal int Count => bindings.Count;
    internal NativeHairContacts(Human human) { this.human = human; }
    internal void Update(List<ColliderComponent> targets)
    {
        if (targets.Count == 0) { Dispose(); nextScan=0; return; }
        if (Time.unscaledTime >= nextScan)
        {
            nextScan=Time.unscaledTime+1;
            var keep=new HashSet<int>();
            var hairs=human.Hair?.Hairs;
            if(hairs!=null)foreach(var hair in hairs)
            {
                var root=hair?.Obj;if(root==null)continue;
                foreach(var cloth in root.GetComponentsInChildren<MagicaCloth>(true))
                {
                    int id=cloth.GetInstanceID();keep.Add(id);
                    if(!bindings.ContainsKey(id))bindings.Add(id,new Binding(cloth));
                }
            }
            foreach(int id in bindings.Keys.Where(id=>!keep.Contains(id)).ToArray()){bindings[id].Dispose();bindings.Remove(id);}
        }
        foreach(var b in bindings.Values)b.Apply(targets);
    }
    public void Dispose(){foreach(var b in bindings.Values)b.Dispose();bindings.Clear();nextScan=0;}

    internal sealed class Binding : IDisposable
    {
        private readonly MagicaCloth cloth;
        private NativeList list;
        private readonly HashSet<IntPtr> owned=new(),desired=new(),current=new();
        private bool enabledMode;
        internal Binding(MagicaCloth cloth){this.cloth=cloth;}
        private bool Remove(bool clear)
        {
            bool changed=false;
            if(list!=null)for(int i=list.Count-1;i>=0;i--)if(list[i]!=null && owned.Contains(list[i].Pointer)){list.RemoveAt(i);changed=true;}
            if(clear)owned.Clear();return changed;
        }
        internal void Apply(List<ColliderComponent> targets)
        {
            if(cloth==null || cloth.Process==null || !cloth.Process.IsRunning())return;
            var data=cloth.SerializeData.colliderCollisionConstraint;
            data.colliderList??=new NativeList();
            bool changed=false;
            if(list==null || list.Pointer!=data.colliderList.Pointer){changed|=Remove(false);list=data.colliderList;}
            desired.Clear();foreach(var c in targets)if(c!=null)desired.Add(c.Pointer);
            current.Clear();
            for(int i=list.Count-1;i>=0;i--)
            {
                var c=list[i];if(c==null)continue;
                if(owned.Contains(c.Pointer)&&!desired.Contains(c.Pointer)){list.RemoveAt(i);changed=true;}
                else current.Add(c.Pointer);
            }
            owned.IntersectWith(desired);
            foreach(var c in targets)if(c!=null&&current.Add(c.Pointer)){list.Add(c);owned.Add(c.Pointer);changed=true;}
            if(data.mode==ColliderCollisionConstraint.Mode.None){data.mode=ColliderCollisionConstraint.Mode.Point;enabledMode=true;changed=true;}
            if(changed)cloth.SetParameterChange();
        }
        public void Dispose()
        {
            bool changed=Remove(true);
            if(cloth==null)return;
            var data=cloth.SerializeData?.colliderCollisionConstraint;
            if(enabledMode && data!=null && data.mode==ColliderCollisionConstraint.Mode.Point){data.mode=ColliderCollisionConstraint.Mode.None;changed=true;}
            if(changed)cloth.SetParameterChange();
        }
    }
}
