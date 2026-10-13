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
        const string section="01 Switches / \u78b0\u649e\u5f00\u5173";
        Enabled=Config.Bind(section,"Enabled",true,"Enable the plugin. Disabling it immediately clears added offsets and restores the original physics.");
        BreastAbdomen=Config.Bind(section,"BreastAbdomen",true,"Breast–abdomen contact. Uses the established soft-contact model and settings, adjusting the breasts while leaving the abdomen in place.");
        BreastBody=Config.Bind(section,"BreastBody",true,"Breast contact with other body parts. Uses the same soft-contact model and adjusts only the breasts. Excludes each breast's own skin and hair. The abdomen has a separate switch.");
        BreastGround=Config.Bind(section,"BreastGround",true,"Breast contact with the ground and support surfaces. Applies a soft upward correction after penetration begins. Ignores platforms above a breast that is already entirely below the surface.");
        AbdomenConfig=new(Config,"BreastAbdomen","02 Tuning / \u78b0\u649e\u53c2\u6570");
        BodyConfig=new(Config,"BreastBody","02B Breast Body / \u80f8\u4e0e\u5176\u4ed6\u8eab\u4f53");
        GroundConfig=new(Config,"BreastGround","02C Breast Ground / \u80f8\u4e0e\u5730\u9762",true);
        MaxCharacters = Config.Bind("03 Performance / \u6027\u80fd", "MaxCharacters", 4, new ConfigDescription("Maximum number of loaded high-detail characters processed for collisions.", new AcceptableValueRange<int>(1,12)));
        GroundMask = Config.Bind("04 Advanced / \u9ad8\u7ea7", "GroundLayerMask", -5, "Unity scene layer mask. Defaults to all layers except IgnoreRaycast. Character colliders and triggers are excluded automatically.");
        Diagnostics = Config.Bind("04 Advanced / \u9ad8\u7ea7", "Diagnostics", false, new ConfigDescription("Developer diagnostics. Disabled in normal use.",null,new HiddenSetting()));
        ShowStatusOverlay=Config.Bind("04 Advanced / \u9ad8\u7ea7","ShowStatusOverlay",false,new ConfigDescription("Developer status overlay. Disabled in normal use.",null,new HiddenSetting()));
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
