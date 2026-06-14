using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Keeps wound item application aligned with the limb currently selected in the medical UI.
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ApplyWoundItem))]
    internal static class PlayerCameraApplyWoundItemPatch
    {
        private static void Prefix()
        {
            if (TimeStopController.IsActive)
                TimeStopMedicalUi.SyncSelectedLimbFromWoundView();
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.WoundSpecialAction))]
    internal static class PlayerCameraWoundSpecialActionPatch
    {
        private static void Prefix()
        {
            if (TimeStopController.IsActive)
                TimeStopMedicalUi.SyncSelectedLimbFromWoundView();
        }
    }
}
