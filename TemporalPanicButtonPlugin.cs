using BepInEx;
using HarmonyLib;
using System.IO;
using System.Reflection;
using TemporalPanicButton.Patches;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton
{
    /// <summary>
    /// BepInEx entry point for the time-stop mod.
    /// It owns Harmony patch lifetime and installs the few MonoBehaviours that need Unity update loops.
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

            // Optional audio files live next to the plugin DLL so released builds do not need
            // to bundle copyrighted sounds. Users can drop their own wav/ogg files here.
            AssetDirectory = Path.Combine(Path.GetDirectoryName(Info.Location), "TemporalPanicButtonAssets");
            Directory.CreateDirectory(AssetDirectory);

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // KrokMP may load before or after this plugin depending on the user's mod setup.
            // Try once immediately; the controller will retry later while the game is running.
            KrokMpCompatibilityPatches.TryInstall(harmony);

            // Runtime components are installed on this plugin GameObject to keep teardown simple.
            TimeStopController.Install(this);
            TimeStopTimerDisplay.Install(this);
            DynamiteFuseCutFeature.Install(this);
            TimeStopAudio.Preload();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Optional audio folder: {AssetDirectory}");
        }

        private void OnDestroy()
        {
            // Cleanly restore the game if BepInEx reloads or the plugin is destroyed.
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

            // Called from Update because KrokMP can appear after initial Awake patching.
            KrokMpCompatibilityPatches.TryInstall(Instance.harmony);
        }
    }
}
