using System.Numerics;
using ALCollisionPlus;

int checks=0;
void Check(bool success,string message)
{
    checks++;
    if(!success) throw new Exception(message);
}
bool Close(float a,float b,float tolerance=2e-4f)=>MathF.Abs(a-b)<tolerance;
void Near(Vector3 a,Vector3 b,string message,float tolerance=2e-4f)=>Check(Vector3.Distance(a,b)<tolerance,message+$"; {a} vs {b}");
Vector3[] Ellipsoid(float width,float height,float depth,float z=0)
{
    var points=new List<Vector3>();
    for(int y=0;y<=40;y++)
    {
        float theta=MathF.PI*y/40, radial=MathF.Sin(theta);
        for(int a=0;a<64;a++)
        {
            float angle=MathF.Tau*a/64;
            points.Add(new Vector3(width*radial*MathF.Cos(angle),height*MathF.Cos(theta),z+depth*radial*MathF.Sin(angle)));
        }
    }
    return points.ToArray();
}
(Vector3[] Centers,float[] Radii) Fit(Vector3[] points)
{
    var centers=new Vector3[NativeBellyFit.SphereCount];
    var radii=new float[NativeBellyFit.SphereCount];
    Check(NativeBellyFit.TryFit(points,centers,radii),"Valid ellipsoid rejected");
    var min=new Vector3(float.PositiveInfinity); var max=new Vector3(float.NegativeInfinity);
    foreach(var p in points){min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
    for(int i=0;i<radii.Length;i++)
    {
        float r=radii[i];var c=centers[i];
        Check(float.IsFinite(r)&&r>0&&float.IsFinite(c.LengthSquared()),"Finite positive sphere expected");
        Check(c.X-r>=min.X-1e-4f&&c.X+r<=max.X+1e-4f,"X sphere overreach");
        Check(c.Y-r>=min.Y-1e-4f&&c.Y+r<=max.Y+1e-4f,"Y sphere overreach");
        Check(c.Z-r>=min.Z-1e-4f&&c.Z+r<=max.Z+1e-4f,"Z sphere overreach");
    }
    return(centers,radii);
}

var normal=Ellipsoid(1.1f,1.4f,.7f); var pregnant=Ellipsoid(1.4f,1.5f,1.2f,.5f);
var before=Fit(normal);var after=Fit(pregnant);
Check(after.Centers.Zip(after.Radii,(c,r)=>c.Z+r).Max()>before.Centers.Zip(before.Radii,(c,r)=>c.Z+r).Max()+.7f,
    "Pregnancy growth did not advance front surface");
var shifted=normal.Select(p=>p+new Vector3(5,-3,2)).ToArray();var moved=Fit(shifted);
for(int i=0;i<before.Radii.Length;i++)
{
    Near(moved.Centers[i],before.Centers[i]+new Vector3(5,-3,2),"Fit translation covariance");
    Check(Close(moved.Radii[i],before.Radii[i]),"Translation changes radius");
}
foreach(float scale in new[]{.01f,.5f,2f,100f})
{
    var scaled=Fit(normal.Select(p=>p*scale).ToArray());
    for(int i=0;i<before.Radii.Length;i++)
    {
        Near(scaled.Centers[i],before.Centers[i]*scale,"Fit scale covariance",.001f);
        Check(Close(scaled.Radii[i],before.Radii[i]*scale,.001f),"Scaled radius mismatch");
    }
}

foreach(var rotation in new[]{Quaternion.Identity,Quaternion.CreateFromYawPitchRoll(.6f,1.2f,.3f),Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI/2)})
foreach(var scale in new[]{Vector3.One,new Vector3(.7f,1.3f,2.1f),new Vector3(-1,2,1)})
{
    var matrix=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(12,4,-7);
    Check(NativeBellyFit.TryCreateFrame(matrix,out var frame,out var inverse),"Valid transformed frame rejected");
    var x=Vector3.TransformNormal(Vector3.UnitX,frame); var y=Vector3.TransformNormal(Vector3.UnitY,frame); var z=Vector3.TransformNormal(Vector3.UnitZ,frame);
    Check(Close(x.Length(),1)&&Close(y.Length(),1)&&Close(z.Length(),1),"Frame retained nonuniform scale");
    Check(Close(Vector3.Dot(x,y),0)&&Close(Vector3.Dot(y,z),0)&&Close(Vector3.Dot(z,x),0),"Frame not orthogonal");
    foreach(var p in new[]{Vector3.Zero,Vector3.One,new Vector3(3,-2,4)})
        Near(Vector3.Transform(Vector3.Transform(p,frame),inverse),p,"Frame inverse round trip");
    // A world rotation/translation of a completed shape must not alter local spheres,
    // including a 90 degree lying pose. This exercises the real frame helper.
    var world=normal.Select(p=>Vector3.Transform(p,matrix)).ToArray();
    var local=world.Select(p=>Vector3.Transform(p,inverse)).ToArray();
    var poseFit=Fit(local);
    var canonical=Fit(normal.Select(p=>p*new Vector3(MathF.Abs(scale.X),MathF.Abs(scale.Y),MathF.Abs(scale.Z))).ToArray());
    for(int i=0;i<before.Radii.Length;i++)
    {
        Near(poseFit.Centers[i],canonical.Centers[i],"Lying/scaled pose changed anatomical fit",3e-4f);
        Check(Close(poseFit.Radii[i],canonical.Radii[i],3e-4f),"Pose changed fitted radius");
    }
}

var scratchC=new Vector3[NativeBellyFit.SphereCount];var scratchR=new float[NativeBellyFit.SphereCount];
Check(!NativeBellyFit.TryFit(Array.Empty<Vector3>(),scratchC,scratchR),"Empty shape accepted");
Check(!NativeBellyFit.TryFit(Enumerable.Repeat(Vector3.One,20).ToArray(),scratchC,scratchR),"Zero-volume shape accepted");
var bad=(Vector3[])normal.Clone();bad[123]=new Vector3(float.NaN,0,0);
Check(!NativeBellyFit.TryFit(bad,scratchC,scratchR),"NaN shape accepted");
bad[123]=new Vector3(float.PositiveInfinity,0,0);
Check(!NativeBellyFit.TryFit(bad,scratchC,scratchR),"Infinite shape accepted");
Check(!NativeBellyFit.TryCreateFrame(Matrix4x4.CreateScale(0),out _,out _),"Singular frame accepted");
var badFrame=Matrix4x4.Identity;badFrame.M22=float.NaN;
Check(!NativeBellyFit.TryCreateFrame(badFrame,out _,out _),"NaN frame accepted");
Array.Fill(scratchC,new Vector3(7,8,9));Array.Fill(scratchR,123);
Check(!NativeBellyFit.TryFit(bad,scratchC,scratchR),"Bad output commit test unexpectedly succeeded");
Check(scratchC.All(c=>c==new Vector3(7,8,9))&&scratchR.All(r=>r==123),"Failed fit partially overwrote a previous valid result");
for(int i=0;i<100;i++) NativeBellyFit.TryFit(normal,scratchC,scratchR);
long allocBefore=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<100;i++) NativeBellyFit.TryFit(normal,scratchC,scratchR);
Check(GC.GetAllocatedBytesForCurrentThread()==allocBefore,"Steady-state fit allocated managed memory");

Console.WriteLine($"PASS: {checks} assertions against production NativeBellyFit. No Unity/Magica runtime is simulated or validated.");
