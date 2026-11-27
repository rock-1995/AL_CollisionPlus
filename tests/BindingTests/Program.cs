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
    var root=new Transform(){name=bone};var child=new Transform(){name="cf_j_bust02_L",position=new(.4f,0,0)};child.children.Add(new(){name="cf_y_bust02_tip_L",position=new(1,0,0)});root.children.Add(child);
    var roots=new NT();roots.Add(root);
    cloth.SerializeData.rootBones=roots;
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
Check(cloth.SerializeData.colliderCollisionConstraint.collisionBones.Count==2,"Original collision bone not retained or extra tip sphere enabled");
Check(cloth.SerializeData.colliderCollisionConstraint.collisionBones.Contains(cloth.SerializeData.rootBones[0]) &&
    !cloth.SerializeData.colliderCollisionConstraint.collisionBones.Contains(cloth.SerializeData.rootBones[0].GetChild(0)),
    "Offset contact must select the root that translates the breast, not the downstream point");
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
    binding.Apply(new(){ownA,ownB},.2f,.8f);
    binding.Apply(new(){ownB},.2f,.8f);
    binding.Apply(new(){ownA,ownB},.2f,.8f);
    var ordered=cloth.SerializeData.colliderCollisionConstraint.colliderList;
    Check(ordered[0]==nativeCollider && ordered[1]==ownA && ordered[2]==ownB,"Contact reentry changed solver order or moved a native collider");
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
    foreach(float disabledLimit in new[]{0f,-2f})
    {
        binding.Apply(new(){ownA},.3f,disabledLimit);
        Check(cloth.SerializeData.colliderCollisionConstraint.limitDistance.value==0,"Nonpositive limit still restricts displacement");
        Check(binding.AddedCount==1,"Disabling displacement limit removed collision");
    }
    binding.Apply(new(){ownA},.3f,5f);
    Check(cloth.SerializeData.colliderCollisionConstraint.limitDistance.value==5f,"Positive out-of-slider limit not restored");
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
var curved=Make();Prepare(curved);
using(var binding=new NativeSpringBinding(curved))
{
    MagicaManager.Team.teamDataArray[1]=new(){scaleRatio=2};
    binding.Apply(new(){ownA},.12f,.8f,.03f);
    Check(MathF.Abs(curved.SerializeData.radius.Evaluate(.4f)*2-.12f)<1e-6f,"Middle radius changed by depth curve or native scale");
    Check(MathF.Abs(curved.SerializeData.radius.Evaluate(1)*2-.03f)<1e-6f,"Tip radius changed by depth curve or native scale");
    Check(binding.ParameterRevision==0,"Plugin's own parameter publication caused geometry invalidation");
    int changes=curved.ParameterChanges;
    for(int i=0;i<100;i++) binding.Apply(new(){ownA},.12f,.8f,.03f);
    Check(curved.ParameterChanges==changes && binding.ParameterRevision==0,"Stable frames refreshed parameters/geometry");
    curved.SetParameterChange();
    Check(binding.ParameterRevision==1,"Native parameter update did not invalidate cached fit");
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
