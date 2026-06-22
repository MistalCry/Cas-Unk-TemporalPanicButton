using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 把 Dynamite 的倒计时交给 DynamiteFuseCutFeature 管理。
    /// 原版 Invoke 计时无法理解时停，所以点燃后需要取消原计时并使用模组自己的可暂停计时。
    /// </summary>
    [HarmonyPatch(typeof(CustomItemBehaviour), "Update")]
    internal static class CustomItemBehaviourUpdatePatch
    {
        private static bool Prefix(CustomItemBehaviour __instance)
        {
            if (!TimeStopController.IsActive)
                return true;

            // 只拦 Dynamite 的原版计时更新，其他 CustomItemBehaviour 继续正常运行。
            return !DynamiteFuseCutFeature.IsDynamite(__instance);
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.UseItem))]
    internal static class BodyUseItemPatch
    {
        private static void Postfix(Body __instance, Item item)
        {
            DynamiteFuseCutFeature.AfterUseItem(__instance, item);
        }
    }

    [HarmonyPatch(typeof(Body), "UseItemInHand")]
    internal static class BodyUseItemInHandPatch
    {
        private static void Postfix(Body __instance)
        {
            if (__instance == null)
                return;

            DynamiteFuseCutFeature.AfterUseItem(__instance, __instance.GetItem(__instance.handSlot));
        }
    }

    [HarmonyPatch(typeof(CustomItemBehaviour), "DynamiteExplode")]
    internal static class CustomItemBehaviourDynamiteExplodePatch
    {
        private static bool Prefix(CustomItemBehaviour __instance)
        {
            // 如果原版 Invoke 在时停中抵达爆炸方法，取消这次爆炸并交给模组计时器重新安排。
            return !DynamiteFuseCutFeature.TryDelayDynamiteExplosion(__instance);
        }
    }
}
