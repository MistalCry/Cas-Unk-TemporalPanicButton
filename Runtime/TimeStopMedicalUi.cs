namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 时停中医疗 UI 的辅助逻辑。
    /// 这里不直接治疗，只负责让治疗目标和小游戏惩罚符合“发动者仍可操作”的规则。
    /// </summary>
    internal static class TimeStopMedicalUi
    {
        public static bool ShouldBoostMedicalMinigames()
        {
            return TimeStopController.IsLocalPlayerInOwnTimeStop && ModSettings.MedicalFocus;
        }

        public static void SyncSelectedLimbFromWoundView()
        {
            // 部分原版医疗流程读 PlayerCamera.selectedLimb，伤口界面则另外记录高亮 limb。
            // 应用治疗前先同步目标，避免界面看着是 A，实际治疗到 B。
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
