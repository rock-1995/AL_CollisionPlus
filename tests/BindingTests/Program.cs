using ALCollisionPlus;
using MagicaCloth2;
using UnityEngine;
using System.Reflection;
using NT=Il2CppSystem.Collections.Generic.List<UnityEngine.Transform>;
using NC=Il2CppSystem.Collections.Generic.List<MagicaCloth2.ColliderComponent>;

int checks=0;
void Check(bool success,string message){checks++;if(!success)throw new Exception(message);}
object Call(string method,params object[] args)=>typeof(NativeSpringBinding).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
MagicaCloth Make()=>new(){Process=new()};
bool Prepare(MagicaCloth cloth,string bone="cf_j_bust01_L",ClothProcess.ClothType type=ClothProcess.ClothType.BoneSpring)
{
    cloth.Process.cloth=cloth;
    var root=new Transform(){name=bone};var child=new Transform(){name="cf_j_bust02_L"};child.children.Add(new(){name="cf_y_bust02_tip_L"});root.children.Add(child);
    var roots=new NT();roots.Add(root);
    var args=new object[]{cloth.Process,type,roots,cloth.SerializeData.colliderCollisionConstraint.collisionBones,false};
    Call("BeforeCreate",args);
    bool prepared=(bool)args[4];
    Call("AfterCreate",cloth.Process,prepared);
    return prepared;
}
var noHook=Make();noHook.Process.cloth=noHook;
using(var binding=new NativeSpringBinding(noHook)) Check(!binding.IsReady,"Unprepared native team must not be accepted");
var other=Make();Check(!Prepare(other,"cf_j_arm01_L"),"Nonbreast team modified");
Check(other.SerializeData.colliderCollisionConstraint.collisionBones.Count==0,"Nonbreast collision bones were changed");
var cloth=Make();var userBone=new Transform(){name="existing"};cloth.SerializeData.colliderCollisionConstraint.collisionBones.Add(userBone);
Check(Prepare(cloth),"Breast hook did not prepare");
Check(cloth.SerializeData.colliderCollisionConstraint.collisionBones.Count==3,"Original collision bone not retained");
var nativeCollider=new ColliderComponent();var ownA=new ColliderComponent();var ownB=new ColliderComponent();
cloth.SerializeData.colliderCollisionConstraint.colliderList.Add(nativeCollider);
var originalRadius=cloth.SerializeData.radius;var originalLimit=cloth.SerializeData.colliderCollisionConstraint.limitDistance;
using(var binding=new NativeSpringBinding(cloth))
{
    Check(binding.IsReady,"Prepared selected attributes rejected");
    Check(binding.Apply(new(){nativeCollider,ownA},.2f,.8f),"Apply failed");
    Check(binding.AddedCount==1,"Existing native obstacle falsely owned");
    Check(cloth.SerializeData.colliderCollisionConstraint.colliderList.Count==2,"Obstacle list count wrong");
    Check(cloth.SerializeData.radius!=originalRadius&&cloth.SerializeData.radius.value==.2f,"Radius not overridden");
    int changes=cloth.ParameterChanges;
    binding.Apply(new(){nativeCollider,ownA},.2f,.8f);
    Check(cloth.ParameterChanges==changes,"Identical Apply caused repeated parameter refresh");
    binding.Apply(new(){ownB},.3f,.8f);
    Check(!cloth.SerializeData.colliderCollisionConstraint.colliderList.Contains(ownA)&&
          cloth.SerializeData.colliderCollisionConstraint.colliderList.Contains(nativeCollider)&&
          cloth.SerializeData.colliderCollisionConstraint.colliderList.Contains(ownB),"Switching targets removed native obstacle or left stale own obstacle");
    binding.Apply(new(),.3f,.8f);
    Check(cloth.SerializeData.colliderCollisionConstraint.colliderList.Count==1,"Disable did not remove owned obstacles");
    Check(cloth.SerializeData.radius==originalRadius&&cloth.SerializeData.colliderCollisionConstraint.limitDistance==originalLimit,"Disable did not restore original curves");
    binding.Apply(new(){ownA},.3f,.8f);
    MagicaManager.Team.teamDataArray[1]=new(){scaleRatio=2};
    binding.Apply(new(){ownA},.3f,.8f);
    Check(MathF.Abs(cloth.SerializeData.radius.value-.15f)<1e-6f,"World radius not compensated for native team scale");
    MagicaManager.Team.teamDataArray[1]=new(){scaleRatio=1};
    var oldList=cloth.SerializeData.colliderCollisionConstraint.colliderList;
    var replacement=new NC();replacement.Add(nativeCollider);cloth.SerializeData.colliderCollisionConstraint.colliderList=replacement;
    binding.Apply(new(){ownB},.3f,.8f);
    Check(!oldList.Contains(ownA),"Replaced list still has stale own obstacle");
    Check(replacement.Contains(ownB)&&replacement.Contains(nativeCollider),"New list not bound correctly");
    // An equal-count native rewrite must not defeat identity-based reconciliation.
    replacement[1]=new ColliderComponent();
    var unrelated=replacement[1];
    binding.Apply(new(){ownB},.3f,.8f);
    Check(replacement.Contains(ownB)&&replacement.Contains(unrelated),"Equal-count rewrite was not reconciled");
    // If another owner clones the list, our copied memberships still belong to us.
    var copied=new NC();for(int i=0;i<replacement.Count;i++)copied.Add(replacement[i]);
    cloth.SerializeData.colliderCollisionConstraint.colliderList=copied;
    binding.Apply(new(){ownB},.3f,.8f);
    Check(!replacement.Contains(ownB)&&copied.Contains(ownB),"Ownership did not migrate across a copied list");
    var externallyReplacedRadius=new CurveSerializeData(.123f);cloth.SerializeData.radius=externallyReplacedRadius;
    binding.Dispose();
    Check(cloth.SerializeData.radius==externallyReplacedRadius,"Dispose overwrote another owner's new radius");
    Check(copied.Count==2&&copied.Contains(nativeCollider)&&copied.Contains(unrelated)&&!copied.Contains(ownB),"Dispose removed original collider or left copied ownership");
}
var invalidScale=Make();Prepare(invalidScale);MagicaManager.Team.teamDataArray[1]=new(){scaleRatio=float.NaN};
using(var binding=new NativeSpringBinding(invalidScale))
{
    Check(!binding.Apply(new(){ownA},.2f,.8f),"Invalid scale accepted");
    Check(invalidScale.SerializeData.colliderCollisionConstraint.colliderList.Count==0&&binding.AddedCount==0&&invalidScale.ParameterChanges==0,"Invalid scale left partial collision registration");
}
MagicaManager.Team.teamDataArray[1]=new(){scaleRatio=1};
var disabled=Make();Prepare(disabled);disabled.Process.ProxyMeshContainer.shareVirtualMesh.attributes.Values=new byte[]{0x11,0x12};
using(var binding=new NativeSpringBinding(disabled)) Check(!binding.IsReady,"Native DisableCollision flag was ignored");
var stale=Make();Prepare(stale);
using(var binding=new NativeSpringBinding(stale))
{
    stale.Process=new(){cloth=stale};
    Check(!binding.IsReady,"Replaced process accepted using old initialization proof");
}
Console.WriteLine($"PASS: {checks} assertions against production NativeSpringBinding managed ownership/eligibility logic. Native hooks, IL2CPP, solver, and actual team construction are NOT validated by these doubles.");
