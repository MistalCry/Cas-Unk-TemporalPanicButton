using BepInEx;
using HarmonyLib;
using System.IO;
using System.Reflection;
using TemporalPanicButton.Patches;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton
{
    /// <summary>
    /// 模组的 BepInEx 入口。
    /// 这里统一管理 Harmony 补丁生命周期，并把需要 Unity Update/OnGUI 的运行时组件挂到插件对象上。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class TemporalPanicButtonPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mistalcry.temporalpanicbutton";
        public const string PluginName = "Temporal Panic Button";
        public const string PluginVersion = "0.1.0";

        private Harmony harmony;

        internal static TemporalPanicButtonPlugin Instance { get; private set; }
        internal static string AssetDirectory { get; private set; }

        private void Awake()
        {
            Instance = this;

            // 自定义音效放在 DLL 同目录，发布包无需内置可能有版权风险的声音文件。
            // 玩家只要把 wav/ogg 放进这个文件夹，TimeStopAudio 就会在启动后异步加载。
            AssetDirectory = Path.Combine(Path.GetDirectoryName(Info.Location), "TemporalPanicButtonAssets");
            Directory.CreateDirectory(AssetDirectory);

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            BodyFreezePatches.TryInstall(harmony);

            // KrokMP 的加载顺序取决于用户装模组的方式。这里先尝试一次，
            // 后续 TimeStopController.Update 还会持续补装，避免联机组件晚加载时失效。
            KrokMpCompatibilityPatches.TryInstall(harmony);

            // 所有运行时 MonoBehaviour 都挂在插件 GameObject 上，卸载时可以集中清理状态。
            TimeStopController.Install(this);
            TimeStopTimerDisplay.Install(this);
            DynamiteFuseCutFeature.Install(this);
            TimeStopAudio.Preload();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Optional audio folder: {AssetDirectory}");
        }

        private void OnDestroy()
        {
            // BepInEx 重载或插件销毁时，必须先恢复世界、HUD、时停效果，再撤销 Harmony patch。
            TimeStopTimerDisplay.Uninstall();
            DynamiteFuseCutFeature.Uninstall();
            TimeStopController.Uninstall();
            harmony?.UnpatchAll(PluginGuid);
            harmony = null;

            if (Instance == this)
                Instance = null;
        }

        internal static void TryInstallKrokMpCompatibilityPatches()
        {
            if (Instance == null || Instance.harmony == null)
                return;

            // 从 Update 调用是为了兼容 KrokMP 晚于本插件加载的情况。
            KrokMpCompatibilityPatches.TryInstall(Instance.harmony);
        }
    }
}
