using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MagicaCloth2;
using UnityEngine;

namespace ALCollisionPlus;

[BepInPlugin(Id, "AL Collision Plus", Version)]
[BepInDependency("local.al.pregnancy.preview", BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("AmanatsuLocation.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.al.collisionplus", Version = "0.2.0";
    internal static Plugin Instance;
    private Harmony harmony;
    private CollisionDriver driver;
    internal NativeCollisionRuntime Runtime;
    internal ConfigEntry<bool> Enabled, BreastAbdomen, BreastBody, BreastGround, Diagnostics, ShowStatusOverlay;
    internal ConfigEntry<int> MaxCharacters, GroundMask;
    internal ConfigEntry<float> RadiusRatio, LimitRatio, GroundNormal;

    public override void Load()
    {
        Instance = this;
        const string switches = "01 Switches / 碰撞开关";
        Enabled = Config.Bind(switches, "Enabled", true, "原生碰撞总开关。关闭后移除本插件附加的碰撞体，恢复胸部原有物理参数。");
        BreastAbdomen = Config.Bind(switches, "BreastAbdomen", true, "胸部与腹部：把随孕肚变化的球体接入游戏原有胸部弹簧求解器。");
        BreastBody = Config.Bind(switches, "BreastBody", true, "胸部与其他身体部位：使用人物已有的原生碰撞体，不执行逐三角形接触求解。");
        BreastGround = Config.Bind(switches, "BreastGround", true, "胸部与地面：查询胸部附近的承托面，由原生平面碰撞体处理反应。");
        RadiusRatio = Config.Bind("02 Native Physics / 原生物理", "BreastRadiusHeightRatio", .012f,
            new ConfigDescription("原生胸部碰撞点半径，占身高比例。原生为稀疏球体碰撞，与旧版表面求解参数不同。", new AcceptableValueRange<float>(.001f, .05f)));
        LimitRatio = Config.Bind("02 Native Physics / 原生物理", "CollisionLimitHeightRatio", .06f,
            new ConfigDescription("原生 BoneSpring 球体碰撞的位移限制，占身高比例。原生胶囊/平面仍受游戏弹簧约束。", new AcceptableValueRange<float>(.001f, .15f)));
        MaxCharacters = Config.Bind("03 Performance / 性能", "MaxCharacters", 4,
            new ConfigDescription("最多接入的已加载高精度角色数。", new AcceptableValueRange<int>(1, 12)));
        GroundMask = Config.Bind("04 Advanced / 高级", "GroundLayerMask", -5, "地面查询的 Unity 层掩码，排除角色与触发器。");
        GroundNormal = Config.Bind("02C Breast Ground / 胸与地面", "BreastGroundMinNormalY", .35f,
            new ConfigDescription("承托面法线的最低向上分量，保留原配置。", new AcceptableValueRange<float>(.05f, 1)));
        Diagnostics = Config.Bind("04 Advanced / 高级", "Diagnostics", false, "记录原生接入数量与准备耗时，用于实机验证。");
        ShowStatusOverlay = Config.Bind("04 Advanced / 高级", "ShowStatusOverlay", false, "显示原生碰撞接入状态。");
        try
        {
        Runtime = new NativeCollisionRuntime(this);
        harmony = new Harmony(Id);
        NativeSpringBinding.Install(harmony, message => Log.LogInfo("[Native spring] " + message));
        // The previous solve completes its jobs before returning. This common boundary
        // works for both BeforeLateUpdate and AfterLateUpdate game configurations.
        harmony.Patch(AccessTools.Method(typeof(ClothManager), nameof(ClothManager.ClothUpdate)),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNativeSolve)) { priority = Priority.First });
        driver = AddComponent<CollisionDriver>();
        Log.LogInfo($"AL Collision Plus {Version}: native BoneSpring collision enabled; no managed triangle solver or bone offsets. Pregnancy fluid collision remains native.");
        }
        catch
        {
            Unload();
            throw;
        }
    }

    private static void BeforeNativeSolve() => Instance?.Runtime?.Step();
    public override bool Unload()
    {
        Runtime?.Dispose();
        harmony?.UnpatchSelf();
        if (driver != null) UnityEngine.Object.Destroy(driver);
        NativeSpringBinding.ClearState();
        Instance = null;
        return true;
    }
}

public sealed class CollisionDriver : MonoBehaviour
{
    public CollisionDriver(IntPtr pointer) : base(pointer) { }
    public void Update() => Plugin.Instance?.Runtime?.PollDisabled();
    public void OnGUI()
    {
        var plugin = Plugin.Instance;
        if (plugin == null || !plugin.ShowStatusOverlay.Value || plugin.Runtime == null) return;
        GUI.Box(new Rect(12, 12, 740, 54), plugin.Runtime.Status);
    }
}
