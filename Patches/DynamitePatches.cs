using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Redirects Dynamite timing through DynamiteFuseCutFeature so explosions pause during time stop.
    /// </summary>
    [HarmonyPatch(typeof(CustomItemBehaviour), "Update")]
    internal static class CustomItemBehaviourUpdatePatch
    {
        private static bool Prefix(CustomItemBehaviour __instance)
        {
            if (!TimeStopController.IsActive)
                return true;

            // Block only Dynamite's vanilla timer update; other custom items keep their own logic.
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
            // When vanilla Invoke reaches DynamiteExplode during time stop, cancel and reschedule it.
            return !DynamiteFuseCutFeature.TryDelayDynamiteExplosion(__instance);
        }
    }
}
