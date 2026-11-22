// These are collection/ownership test doubles. They do not simulate native build,
// native jobs, IL2CPP marshaling, transforms, collision, or the Magica solver.
namespace TestDoubles
{
    public class Handle
    {
        static long next;
        public IntPtr Pointer {get;} = new IntPtr(++next);
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : TestDoubles.Handle
    {
        readonly System.Collections.Generic.List<T> items=new();
        public int Count=>items.Count;
        public T this[int i]{get=>items[i];set=>items[i]=value;}
        public void Add(T item)=>items.Add(item);
        public bool Contains(T item)=>items.Contains(item);
        public void RemoveAt(int i)=>items.RemoveAt(i);
    }
}
namespace UnityEngine
{
    public class Transform : TestDoubles.Handle
    {
        public string name;
        public readonly System.Collections.Generic.List<Transform> children=new();
        public int childCount=>children.Count;
        public Transform GetChild(int i)=>children[i];
    }
}
namespace HarmonyLib
{
    public class Harmony { public void Patch(System.Reflection.MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
    public class HarmonyMethod { public HarmonyMethod(Type type,string method){} }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type type,string method,Type[] args=null)=>
            args==null?type.GetMethod(method):type.GetMethod(method,args);
    }
}
namespace MagicaCloth2
{
    public static class MagicaManager { public static TeamManager Team=new(); }
    public class TeamManager { public TeamData[] teamDataArray={new(),new(){scaleRatio=1}}; }
    public struct TeamData { public float scaleRatio; }
    public class ColliderComponent : TestDoubles.Handle { }
    public class CurveSerializeData : TestDoubles.Handle
    {
        public float value;
        public CurveSerializeData(float value){this.value=value;}
    }
    public class ColliderCollisionConstraint
    {
        public class SerializeData
        {
            public Il2CppSystem.Collections.Generic.List<ColliderComponent> colliderList=new();
            public Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> collisionBones=new();
            public CurveSerializeData limitDistance=new(.05f);
        }
    }
    public class ClothSerializeData
    {
        public ColliderCollisionConstraint.SerializeData colliderCollisionConstraint=new();
        public CurveSerializeData radius=new(.02f);
    }
    public class MagicaCloth : TestDoubles.Handle
    {
        public string name="test cloth";
        public ClothSerializeData SerializeData=new();
        public ClothProcessEX Process;
        public int ParameterChanges;
        public int GetInstanceID()=>Pointer.ToInt32();
        public void OnDestroy(){}
        public void SetParameterChange()=>ParameterChanges++;
    }
    public class ClothInitSerializeData{}
    public class RenderSetupData { public enum BoneConnectionMode {Line} }
    public class ClothProcess { public enum ClothType { BoneSpring=10,Other=0} }
    public class AttributeArray
    {
        public bool IsValid=true;
        public byte[] Values=new byte[]{0x11,2,2};
        public int Count=>Values.Length;
        public VertexAttribute this[int i]=>new(){Value=Values[i]};
    }
    public struct VertexAttribute {public byte Value;}
    public class Mesh {public AttributeArray attributes=new();}
    public class Container {public Mesh shareVirtualMesh=new();}
    public class ClothProcessEX : TestDoubles.Handle
    {
        public MagicaCloth cloth;
        public bool Running=true;
        public int TeamId=1;
        public Container ProxyMeshContainer=new();
        public bool IsRunning()=>Running;
        public void CreateBoneRenderSetupData(ClothInitSerializeData init,ClothProcess.ClothType type,
            Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> a,
            Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> b,
            Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> c,
            RenderSetupData.BoneConnectionMode mode){}
    }
}
