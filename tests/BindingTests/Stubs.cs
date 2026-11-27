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
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));}
    public struct Keyframe
    {
        public float time, value;
        public Keyframe(float time, float value, float a, float b) { this.time=time; this.value=value; }
    }
    public class AnimationCurve : TestDoubles.Handle
    {
        public Keyframe[] keys;
        public static AnimationCurve Linear(float t0,float v0,float t1,float v1) => new(){keys=new[]{new Keyframe(t0,v0,0,0),new Keyframe(t1,v1,0,0)}};
        public int MoveKey(int i, Keyframe key) { keys[i]=key; return i; }
        public float Evaluate(float t) => keys[0].value + (keys[1].value - keys[0].value) * t;
    }
    public class Transform : TestDoubles.Handle
    {
        public string name;
        public Vector3 position;
        public IEnumerable<T> GetComponentsInChildren<T>(bool include) where T:class {yield return this as T;foreach(var child in children)foreach(var item in child.GetComponentsInChildren<T>(include))yield return item;}
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
    public static class MagicaManager
    {
        public static TeamManager Team = new();
        public static ColliderManager Collider = new();
    }
    public class TeamManager { public TeamData[] teamDataArray={new(),new(){scaleRatio=1}}; }
    public struct TeamData
    {
        public float scaleRatio;
        public float blendWeight => 1;
        public bool IsEnable => true;
        public bool IsRunning => true;
        public bool IsCameraCullingInvisible => false;
        public bool IsDistanceCullingInvisible => false;
        public int updateCount => 1;
        public int colliderCount => 0;
        public ColliderChunk colliderChunk => new();
    }
    public struct ColliderChunk { public int startIndex => 0; }
    public class ColliderManager
    {
        public const int Flag_Enable = 1;
        public VertexAttribute[] flagArray = Array.Empty<VertexAttribute>();
    }
    public class ColliderComponent : TestDoubles.Handle { }
    public class CurveSerializeData : TestDoubles.Handle
    {
        public float value;
        public bool useCurve;
        public UnityEngine.AnimationCurve curve;
        public CurveSerializeData(float value){this.value=value;}
        public void SetValue(float value, UnityEngine.AnimationCurve curve) { this.value=value; this.curve=curve; useCurve=true; }
        public float Evaluate(float t)=>value*(useCurve?curve.Evaluate(t):1);
    }
    public class ColliderCollisionConstraint
    {
        public class SerializeData
        {
            public int mode => 0;
            public Il2CppSystem.Collections.Generic.List<ColliderComponent> colliderList=new();
            public Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> collisionBones=new();
            public CurveSerializeData limitDistance=new(.05f);
        }
    }
    public class ClothSerializeData
    {
        public Il2CppSystem.Collections.Generic.List<UnityEngine.Transform> rootBones=new();
        public ColliderCollisionConstraint.SerializeData colliderCollisionConstraint=new();
        public CurveSerializeData radius=new(.02f);
    }
    public class MagicaCloth : TestDoubles.Handle
    {
        public string name="test cloth";
        public bool enabled => true;
        public GameObject gameObject { get; } = new();
        public ClothSerializeData SerializeData=new();
        public ClothProcessEX Process;
        public int ParameterChanges;
        public int GetInstanceID()=>Pointer.ToInt32();
        public void OnDestroy(){}
        public void SetParameterChange(){ParameterChanges++;typeof(ALCollisionPlus.NativeSpringBinding).GetMethod("AfterParameterChange",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,new object[]{this});}
    }
    public class GameObject { public bool activeInHierarchy => true; }
    public class ClothInitSerializeData{}
    public class RenderSetupData { public enum BoneConnectionMode {Line} }
    public class ClothProcess { public enum ClothType { BoneSpring=10,Other=0} }
    public class AttributeArray
    {
        public bool IsValid=true;
        public byte[] Values=new byte[]{1,0x12,0x12};
        public int Count=>Values.Length;
        public VertexAttribute this[int i]=>new(){Value=Values[i]};
    }
    public struct VertexAttribute {public byte Value;}
    public class DepthArray { public bool IsCreated=true; public float[] Values={0,.4f,1}; public int Length=>Values.Length; public float this[int i]=>Values[i]; }
    public class Mesh {public AttributeArray attributes=new(); public DepthArray vertexDepths=new();}
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
namespace ALCollisionPlus
{
    internal static class CollisionTrace
    {
        internal static string Name(MagicaCloth2.MagicaCloth cloth) => cloth.name;
        internal static string F(float value) =>
            value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
