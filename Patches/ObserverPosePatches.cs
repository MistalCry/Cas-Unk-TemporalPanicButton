using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 挂住用于短暂时停站姿和 Observer 位置修正的视觉更新点。
    /// 这些补丁不改逻辑结果，只负责让画面在合适的帧上表现出时停姿态。
    /// </summary>
    [HarmonyPatch(typeof(Body), "HandleVisuals")]
    internal static class BodyHandleVisualsPatch
    {
        private static void Prefix(Body __instance)
        {
            // 先让姿态变量在原版视觉更新前生效，等原版画面跑完再恢复。
            TimeStopPoseVisual.PrepareAttackPose(__instance);
        }

        private static void Postfix(Body __instance)
        {
            TimeStopPoseVisual.FinishAttackPose(__instance);
        }
    }

    [HarmonyPatch(typeof(Observer), "Update")]
    internal static class ObserverUpdatePatch
    {
        private static void Postfix()
        {
            // Observer 的 transform 由它自己的 Update 驱动，所以要在原版逻辑后再挪位置。
            TimeStopPoseVisual.ApplyObserver();
        }
    }
}
