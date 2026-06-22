using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 让伤口处理行为跟医疗界面当前选中的肢体保持一致。
    /// 这样玩家在时停中从伤口页切换治疗时，不会因为两个 UI 记住的目标不一致而治错肢体。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ApplyWoundItem))]
    internal static class PlayerCameraApplyWoundItemPatch
    {
        private static void Prefix()
        {
            if (TimeStopController.IsLocalPlayerInOwnTimeStop)
                TimeStopMedicalUi.SyncSelectedLimbFromWoundView();
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.WoundSpecialAction))]
    internal static class PlayerCameraWoundSpecialActionPatch
    {
        private static void Prefix()
        {
            if (TimeStopController.IsLocalPlayerInOwnTimeStop)
                TimeStopMedicalUi.SyncSelectedLimbFromWoundView();
        }
    }
}
