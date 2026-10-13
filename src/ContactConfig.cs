using BepInEx.Configuration;

namespace ALCollisionPlus;

internal sealed class ContactConfig
{
    internal ConfigEntry<float> Strength,MaxOffset,Compression,Transition,MaxSpeed,Response,Coverage,Capture,MinInfluence;
    internal ConfigEntry<float> Alignment,Radius,ConflictTolerance,GroundNormalY;
    internal ConfigEntry<int> Iterations;
    internal ConfigEntry<bool> ConflictGuard;
    internal ContactConfig(ConfigFile config,string prefix,string section,bool ground=false)
    {
        ConfigEntry<float> Float(string suffix,float value,float min,float max,string text) =>
            config.Bind(section,prefix+suffix,value,new ConfigDescription(text,new AcceptableValueRange<float>(min,max)));
        Strength=Float("Strength",1,0,1,"Strength for this contact type, applied once after solving the group. A value of 0 disables the correction.");
        MaxOffset=Float("MaxOffsetHeightRatio",.09859826f,.001f,.15f,"Maximum added displacement per breast as a fraction of character height, scaled by strength. Combined contact types use the largest individual limit.");
        Compression=Float("CompressionHeightRatio",.0013973799f,0,.02f,"Allowed soft penetration depth as a fraction of character height. The default is about 0.14%. No correction is applied within this depth.");
        Transition=Float("SoftTransitionHeightRatio",.003f,0,.02f,"Depth of the gradual response beyond the allowed penetration, as a fraction of character height. Larger values soften the transition. A value of 0 disables it.");
        MaxSpeed=Float("MaxSpeedHeightRatio",.06f,.005f,1,"Maximum change in added displacement per second, in units of character height. Combined contacts and recovery use the slowest applicable speed to smooth transitions.");
        Response=Float("ResponsePerSecond",8,1,80,"Response rate. Smaller values feel softer. This acts alongside the per-second speed limit. Combined contacts and recovery use the slowest applicable rate.");
        Coverage=Float("MinContactCoverage",.2f,0,1,"Effective contact coverage threshold. Sparse intersections are weighted down to keep local overlap from moving the entire breast.");
        Iterations=config.Bind(section,prefix+"ContactIterations",8,new ConfigDescription("Solver iterations for a fixed set of contact surfaces. Contacts are collected once per frame. Strength and coverage attenuation are each applied once.",new AcceptableValueRange<int>(1,16)));
        Capture=Float("SearchDistanceHeightRatio",.103777304f,.005f,.2f,"Search distance as a fraction of character height, used to find surfaces already penetrated.");
        MinInfluence=Float("MinSkinInfluence",.77074236f,.25f,1,"Minimum skinning weight used to convert bone displacement. Larger values reduce the effect of lightly weighted seams on whole-breast displacement.");
        if(ground)
        {
            GroundNormalY=Float("MinNormalY",.35f,.05f,1,"Minimum upward component of a support surface normal. Larger values favor level ground. Walls, character colliders, and triggers are excluded.");
            return;
        }
        Alignment=Float("NormalAlignment",.30131006f,-1,1,"Minimum alignment between the contact normal and the direction separating the centers. Higher values enforce closer alignment. A value of -1 disables this filter.");
        string radiusKey=prefix=="BreastAbdomen"?"ContactMarginHeightRatio":prefix+"EdgeToleranceHeightRatio";
        Radius=config.Bind(section,radiusKey,.003126201f,new ConfigDescription("Triangle-edge search tolerance as a fraction of character height. Breast–abdomen contact keeps the existing configuration key and value.",new AcceptableValueRange<float>(.0001f,.01f)));
        ConflictGuard=config.Bind(section,prefix+"ConflictGuard",false,"Conflicting-surface guard. Reduces a correction if it would deepen the worst local overlap beyond the tolerance. Defaults to off for compatibility with the established breast–abdomen preset.");
        ConflictTolerance=Float("ConflictToleranceHeightRatio",.007117904f,0,.01f,"Extra local penetration allowed by the conflicting-surface guard, as a fraction of character height. Applies when the guard is enabled.");
    }
    internal ContactSettings Settings(float height) => new(
        height*(Radius?.Value??0),height*MaxOffset.Value,Strength.Value,height*Compression.Value,
        Coverage.Value,Alignment?.Value??0,height*Transition.Value,height*Capture.Value,MinInfluence.Value,
        ConflictGuard?.Value==true?height*ConflictTolerance.Value:float.PositiveInfinity,Iterations.Value,
        Response.Value,height*MaxSpeed.Value,GroundNormalY?.Value??.35f);
}
