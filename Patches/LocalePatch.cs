using HarmonyLib;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 当游戏本地化表里没有本模组设置项时，补上模组内置的显示文本。
    /// </summary>
    [HarmonyPatch(typeof(Locale), nameof(Locale.GetOther))]
    internal static class LocalePatch
    {
        private static void Postfix(string str, ref string __result)
        {
            // Locale.GetOther 找不到翻译时会原样返回 key，只有这种情况才使用内置后备文本。
            if (__result != str)
                return;

            if (ModSettings.TryGetLocaleFallback(str, out string fallback))
                __result = fallback;
        }
    }
}
