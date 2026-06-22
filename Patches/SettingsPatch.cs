using HarmonyLib;
using System.Collections.Generic;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 在游戏生成默认设置列表时追加本模组选项。
    /// 不在插件 Awake 里强行初始化 Settings，是为了避免破坏游戏或其他模组的按键设置加载顺序。
    /// </summary>
    [HarmonyPatch(typeof(Settings), nameof(Settings.DefaultSettings))]
    internal static class SettingsPatch
    {
        private static void Postfix(List<Setting> __result)
        {
            ModSettings.AddMissingSettings(__result);
        }
    }
}
