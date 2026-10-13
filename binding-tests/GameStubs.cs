using N=System.Numerics;
namespace UnityEngine
{
    public class Object { static int next; readonly int id=++next; public string name="";public IntPtr Pointer=>new(id);public int GetInstanceID()=>id;public static void Destroy(Object o){} }
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 down=>new(0,-1,0);}
    public static class Time {public static float unscaledTime,unscaledDeltaTime=1f/60;public static int frameCount;}
    public class Collider:Object {public bool isTrigger;public Transform transform=new();}
    public struct RaycastHit {public Collider collider;public Vector3 point,normal;}
    public enum QueryTriggerInteraction {Ignore}
    public struct Quaternion{public static Quaternion identity=>new();}
    public static class Physics
    {
        public static RaycastHit[] Hits=Array.Empty<RaycastHit>();
        public static int RayQueries,BoundsQueries;
        public static int OverlapBoxNonAlloc(Vector3 center,Vector3 half,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider> results,Quaternion rotation,int mask,QueryTriggerInteraction mode)
        {
            BoundsQueries++;int count=0;
            foreach(var hit in Hits)if(Math.Abs(hit.point.x-center.x)<=half.x && Math.Abs(hit.point.y-center.y)<=half.y && Math.Abs(hit.point.z-center.z)<=half.z)
            {if(count==64)break;results[count++]=hit.collider;}
            return count;
        }
        public static int RaycastNonAlloc(Vector3 origin,Vector3 direction,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit> results,float distance,int layerMask,QueryTriggerInteraction interaction)
        {RayQueries++;for(int i=0;i<Hits.Length;i++)results[i]=Hits[i];return Hits.Length;}
    }
    public struct Matrix4x4
    {
        public float m00,m10,m20,m30,m01,m11,m21,m31,m02,m12,m22,m32,m03,m13,m23,m33;
        public static Matrix4x4 From(N.Matrix4x4 m)=>new(){m00=m.M11,m10=m.M12,m20=m.M13,m30=m.M14,m01=m.M21,m11=m.M22,m21=m.M23,m31=m.M24,m02=m.M31,m12=m.M32,m22=m.M33,m32=m.M34,m03=m.M41,m13=m.M42,m23=m.M43,m33=m.M44};
    }
    public class Transform:Object
    {
        public Transform parent;public N.Matrix4x4 Matrix=N.Matrix4x4.Identity;
        public Vector3 localPosition;
        public Vector3 position{get=>localPosition;set=>localPosition=value;}
        public int MatrixReads;
        public Matrix4x4 localToWorldMatrix{get{MatrixReads++;return Matrix4x4.From(Matrix);}}
        public Matrix4x4 worldToLocalMatrix{get{N.Matrix4x4.Invert(Matrix,out var m);return Matrix4x4.From(m);}}
        public Vector3 lossyScale=>new(1,1,1);
        public bool IsChildOf(Transform t){for(var a=this;a!=null;a=a.parent)if(a==t)return true;return false;}
    }
    public class GameObject:Object
    {
        public bool activeInHierarchy=true;public readonly List<object> Components=new();
        public T[] GetComponentsInChildren<T>(bool includeInactive)=>Components.OfType<T>().ToArray();
    }
    public class Mesh:Object {public Vector3[] vertices=Array.Empty<Vector3>(),normals=Array.Empty<Vector3>();public BoneWeight[] boneWeights=Array.Empty<BoneWeight>();public int[] triangles=Array.Empty<int>();public int vertexCount=>vertices.Length;public void MarkDynamic(){}}
    public struct BoneWeight {public int boneIndex0,boneIndex1,boneIndex2,boneIndex3;public float weight0,weight1,weight2,weight3;}
    public class SkinnedMeshRenderer:Object
    {
        public Mesh sharedMesh;public Transform[] bones;public Transform transform=new();public SkinnedCollisionHelper Helper;
        public T GetComponent<T>() where T:class=>Helper as T;
        public void BakeMesh(Mesh mesh,bool useScale){mesh.vertices=sharedMesh.vertices.ToArray();mesh.normals=sharedMesh.normals.ToArray();}
    }
}
public class SkinnedCollisionHelper:UnityEngine.Object
{
    public bool _isInit;public WeightList[] _nodeWeights;public UnityEngine.Mesh _meshCalc;
    public UnityEngine.Vector3[] _newVert;public UnityEngine.SkinnedMeshRenderer _skinnedMeshRenderer;
    public class WeightList{public UnityEngine.Transform Transform;public NativeList<VertexWeight> Weights;}
    public struct VertexWeight {public int Index;public UnityEngine.Vector3 LocalPosition,LocalNormal;public float Weight;}
}
namespace MagicaCloth2
{
    public class Data {public List<UnityEngine.Transform> rootBones=new();public float blendWeight=1;}
    public class MagicaCloth:UnityEngine.Object {public Data SerializeData=new();public ClothProcess Process=new();public bool isActiveAndEnabled=true;}
    public struct VertexAttribute {public bool IsMove()=>false;public bool IsDisableCollision()=>true;}
    public struct Int4{public int x;}
    public struct Weight{public Int4 boneIndices;}
    public class VirtualMesh {public List<VertexAttribute> attributes=new(){new(),new()};public List<Weight> boneWeights=new(){new(),new()};public List<int> skinBoneTransformIndices=new(){0,1};public int VertexCount=2;}
    public class Container {public VirtualMesh shareVirtualMesh=new();public int GetTransformCount()=>0;public UnityEngine.Transform GetTransformFromIndex(int n)=>null;}
    public class ClothProcess
    {public enum ClothType {MeshCloth=0,BoneCloth=1,BoneSpring=10}public int TeamId=1,RunningChecks;public bool Running=true;public ClothType clothType=ClothType.BoneSpring;public Container ProxyMeshContainer=new();public bool IsRunning(){RunningChecks++;return Running;}public bool IsSkipWriting()=>false;public bool IsCameraCullingInvisible()=>false;public bool IsDistanceCullingInvisible()=>false;}
}
namespace Character
{
    public class HumanMagicaSpringSetting
    {
        public enum Type {Bust,Hip,Arm,Leg,Belly,Back,Spine,Waist}public enum Side {L,R,All}
        public class Entry{public MagicaCloth2.MagicaCloth Spring;}
        public Entry[] Entries;public readonly Dictionary<Type,Entry[]> Additional=new();
        public Entry[] Get(Type t,Side s)=>t==Type.Bust?Entries:Additional.GetValueOrDefault(t);
        public MagicaCloth2.MagicaCloth[] GetSpringAll()=>(Entries??Array.Empty<Entry>()).Concat(Additional.Values.SelectMany(e=>e)).Select(e=>e.Spring).ToArray();
    }
    public class HumanBody{public HumanMagicaSpringSetting magicaSpringSetting;public UnityEngine.GameObject objHitBody,objHitHead;public UnityEngine.GameObject GetObjBone()=>objHitBody;}
    public class HairData{public bool HasValue=true;public UnityEngine.GameObject Obj;}
    public class HumanHair{public HairData[] Hairs=Array.Empty<HairData>();}
    public class Human{public static List<Human> _list=new();public IntPtr Pointer=>new(GetHashCode());public HumanBody Body;public HumanHair Hair;public UnityEngine.GameObject GameObject;public UnityEngine.Transform Transform;public bool Disposed,IsReloading;public bool IsActiveAndEnabled=true,HiPoly=true;}
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T>
    {
        readonly T[] items;public Il2CppStructArray(int count){items=new T[count];}public Il2CppStructArray(T[] data){items=data;}
        public T this[int i]{get=>items[i];set=>items[i]=value;}public Span<T> AsSpan()=>items.AsSpan();
    }
    public class Il2CppReferenceArray<T>{readonly T[] items;public Il2CppReferenceArray(int count){items=new T[count];}public T this[int i]{get=>items[i];set=>items[i]=value;}}
}

public class NativeList<T>:IEnumerable<T>
{
    public bool UseSpan=true;public int IndexerReads,SpanReads;
    private T[] data;
    private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<T> array;
    public int _size;
    public int Count=>_size;
    public object _items{get{SpanReads++;return UseSpan?array:null;}}
    public NativeList(IEnumerable<T> items){data=items.ToArray();_size=data.Length;array=new(data);}
    public T this[int i]{get{IndexerReads++;return data[i];}set=>data[i]=value;}
    public static implicit operator NativeList<T>(List<T> items)=>new(items);
    public IEnumerator<T> GetEnumerator()=>data.Take(_size).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
}
