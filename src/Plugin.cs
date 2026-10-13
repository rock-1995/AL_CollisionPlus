using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MagicaCloth2;

namespace ALCollisionPlus;

[BepInPlugin(Id, "AL Collision Plus", Version)]
[BepInDependency("local.al.pregnancy.preview", BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("AmanatsuLocation.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.al.collisionplus", Version = "0.1.11";
    internal static Plugin Instance = null!;
    private Harmony harmony;
    private CollisionDriver driver;
    private UnityEngine.Events.UnityAction beforeRender;
    private bool renderAuditAttached;
    private sealed class HiddenSetting { public bool Browsable = false; }
    internal ConfigEntry<bool> Enabled,BreastAbdomen,BreastBody,BreastGround,Diagnostics,ShowStatusOverlay;
    internal ConfigEntry<int> MaxCharacters,GroundMask;
    internal ContactConfig AbdomenConfig,BodyConfig,GroundConfig;
    internal CollisionRuntime Runtime=null!;
    internal bool TrackStatistics => Diagnostics.Value || ShowStatusOverlay.Value;
    internal Switches Switches => new(BreastAbdomen.Value,BreastBody.Value,BreastGround.Value);
    internal ContactSettings Settings(ContactKind kind,float height) =>
        (kind==ContactKind.BreastAbdomen?AbdomenConfig:kind==ContactKind.BreastBody?BodyConfig:GroundConfig).Settings(height);
    public override void Load()
    {
        Instance = this;
        const string section="01 Switches / 碰撞开关";
        Enabled=Config.Bind(section,"Enabled",true,"插件总开关。关闭后立即清除附加位移，恢复原有物理。");
        BreastAbdomen=Config.Bind(section,"BreastAbdomen",true,"胸部－腹部。沿用已确认的软接触逻辑及参数，仅调整胸部，不移动腹部。");
        BreastBody=Config.Bind(section,"BreastBody",true,"胸部－其他身体部位。使用胸腹同类软接触，仅调整胸部。排除自身蒙皮；腹部始终由胸腹独立开关控制。头发不参与。");
        BreastGround=Config.Bind(section,"BreastGround",true,"胸部－场景地面／承托面。实际压入后柔和顶起胸部，不使用提前接触间隙；整块胸部已在面下方时不追赶上方平台。");
        AbdomenConfig=new(Config,"BreastAbdomen","02 Tuning / 碰撞参数");
        BodyConfig=new(Config,"BreastBody","02B Breast Body / 胸与其他身体");
        GroundConfig=new(Config,"BreastGround","02C Breast Ground / 胸与地面",true);
        MaxCharacters = Config.Bind("03 Performance / 性能", "MaxCharacters", 4, new ConfigDescription("最多参与碰撞的已加载高精度角色数。", new AcceptableValueRange<int>(1,12)));
        GroundMask = Config.Bind("04 Advanced / 高级", "GroundLayerMask", -5, "Unity 场景碰撞层掩码。默认所有层（排除 IgnoreRaycast），自动排除角色碰撞体和触发器。");
        Diagnostics = Config.Bind("04 Advanced / 高级", "Diagnostics", false, new ConfigDescription("Developer diagnostics. Disabled in normal use.",null,new HiddenSetting()));
        ShowStatusOverlay=Config.Bind("04 Advanced / 高级","ShowStatusOverlay",false,new ConfigDescription("Developer status overlay. Disabled in normal use.",null,new HiddenSetting()));
        Runtime = new CollisionRuntime(this);
        Config.SettingChanged += OnSettingChanged;
        driver = AddComponent<CollisionDriver>();
        beforeRender = (Action)(() => Runtime?.ObserveBeforeRender());
        UpdateDiagnostics();
        harmony = new Harmony(Id);
        harmony.Patch(typeof(ClothManager).GetMethod(nameof(ClothManager.OnEarlyClothUpdate)), prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePhysics)));
        harmony.Patch(typeof(ClothManager).GetMethod(nameof(ClothManager.ClothUpdate)), prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeSolve)));
        // OnAfterLateUpdate runs even when the game simulates in BeforeLateUpdate.
        // Its postfix is after Human.LateUpdate (pregnancy) AND Magica's late solver.
        harmony.Patch(typeof(ClothManager).GetMethod(nameof(ClothManager.OnAfterLateUpdate)), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPhysics)));
        Log.LogInfo($"AL Collision Plus {Version} loaded.");
    }

    internal void UpdateDiagnostics()
    {
        bool enabled=TrackStatistics;
        if(enabled==renderAuditAttached || beforeRender==null)return;
        if(enabled)UnityEngine.Application.add_onBeforeRender(beforeRender);
        else UnityEngine.Application.remove_onBeforeRender(beforeRender);
        renderAuditAttached=enabled;
    }

    private static void BeforePhysics() => Instance?.Runtime?.Restore();
    private static void BeforeSolve() => Instance?.Runtime?.Restore();
    private static void AfterPhysics() => Instance?.Runtime?.Step();
    private void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        // Config may reload on a file watcher thread: never access Unity objects here.
        if(Diagnostics.Value && e.ChangedSetting.Definition.Section.StartsWith("01",StringComparison.Ordinal))
            Log.LogInfo($"[Control input] {e.ChangedSetting.Definition.Key}={e.ChangedSetting.BoxedValue}; queued for main-thread confirmation.");
    }
    public override bool Unload()
    {
        Config.SettingChanged -= OnSettingChanged;
        if(renderAuditAttached)UnityEngine.Application.remove_onBeforeRender(beforeRender);
        if(driver!=null)UnityEngine.Object.Destroy(driver);
        Runtime?.Dispose(); harmony?.UnpatchSelf(); Instance=null; return true;
    }
}
