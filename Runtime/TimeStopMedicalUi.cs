namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Small helpers for keeping medical treatment usable while the world is frozen.
    /// </summary>
    internal static class TimeStopMedicalUi
    {
        public static bool ShouldBoostMedicalMinigames()
        {
            return TimeStopController.IsActive && ModSettings.MedicalFocus;
        }

        public static void SyncSelectedLimbFromWoundView()
        {
            // Some vanilla medical paths use PlayerCamera.selectedLimb, while the wound view
            // tracks the highlighted limb separately. Sync them before applying treatment.
            PlayerCamera camera = PlayerCamera.main;
            WoundView view = WoundView.view;
            if (camera == null || view == null || view.body == null || view.body.limbs == null)
                return;

            int index = view.limbLookingAt;
            if (index < 0 || index >= view.body.limbs.Length)
                return;

            Limb limb = view.body.limbs[index];
            if (limb != null && !limb.dismembered)
                camera.selectedLimb = limb;
        }
    }
}
