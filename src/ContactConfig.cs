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
        Strength=Float("Strength",1,0,1,"本类型强度。整组接触求解后只应用一次，0 不修正。");
        MaxOffset=Float("MaxOffsetHeightRatio",.09859826f,.001f,.15f,"整侧胸部最大附加位移，占角色身高比例。实际上限还乘以强度；多类接触共用最大的单类上限，不累加三个上限。");
        Compression=Float("CompressionHeightRatio",.0013973799f,0,.02f,"允许局部软压入，占身高比例。范围内不推离；默认约 0.14%。");
        Transition=Float("SoftTransitionHeightRatio",.003f,0,.02f,"超出允许压入后，从轻到重的过渡深度，占身高比例。越大越软，0 关闭渐进过渡。");
        MaxSpeed=Float("MaxSpeedHeightRatio",.06f,.005f,1,"附加位移每秒最多变化多少个身高。多类接触及回位期间采用相关类型中较慢的速度，防止切换接触面时跳动。");
        Response=Float("ResponsePerSecond",8,1,80,"响应速度。越小越缓和；与每秒限速同时生效。多类接触及回位期间采用相关类型中较慢的响应。");
        Coverage=Float("MinContactCoverage",.2f,0,1,"有效接触覆盖阈值。少数相交点按覆盖率减弱，避免局部交错把整侧胸部拉走。");
        Iterations=config.Bind(section,prefix+"ContactIterations",8,new ConfigDescription("固定接触面的求解次数。每帧只收集一次接触，强度和覆盖衰减只应用一次。",new AcceptableValueRange<int>(1,16)));
        Capture=Float("SearchDistanceHeightRatio",.103777304f,.005f,.2f,"搜索距离，占身高比例。用于找已经压入的表面，不是提前接触间隙。");
        MinInfluence=Float("MinSkinInfluence",.77074236f,.25f,1,"骨骼位移换算所用的最小蒙皮权重。越大越抑制弱权重接缝放大整侧胸部位移。");
        if(ground)
        {
            GroundNormalY=Float("MinNormalY",.35f,.05f,1,"承托面法线的最低向上分量。越大越偏向水平地面；墙面、角色碰撞体和触发器不作为地面。");
            return;
        }
        Alignment=Float("NormalAlignment",.30131006f,-1,1,"接触面推离方向与部位中心分离方向的最低一致度。越大筛选越严格，-1 关闭方向筛选。");
        string radiusKey=prefix=="BreastAbdomen"?"ContactMarginHeightRatio":prefix+"EdgeToleranceHeightRatio";
        Radius=config.Bind(section,radiusKey,.003126201f,new ConfigDescription("三角形边缘搜索容差，占身高比例；不是表面外侧接触厚度。胸腹保留旧键名及数值。",new AcceptableValueRange<float>(.0001f,.01f)));
        ConflictGuard=config.Bind(section,prefix+"ConflictGuard",false,"冲突面保护：修正使最深局部重叠增加超过容差时，缩小本次修正。默认关闭，沿用已确认的胸腹预设。");
        ConflictTolerance=Float("ConflictToleranceHeightRatio",.007117904f,0,.01f,"冲突面保护允许的额外局部压入，占身高比例；只在对应保护开启时生效。");
    }
    internal ContactSettings Settings(float height) => new(
        height*(Radius?.Value??0),height*MaxOffset.Value,Strength.Value,height*Compression.Value,
        Coverage.Value,Alignment?.Value??0,height*Transition.Value,height*Capture.Value,MinInfluence.Value,
        ConflictGuard?.Value==true?height*ConflictTolerance.Value:float.PositiveInfinity,Iterations.Value,
        Response.Value,height*MaxSpeed.Value,GroundNormalY?.Value??.35f);
}
