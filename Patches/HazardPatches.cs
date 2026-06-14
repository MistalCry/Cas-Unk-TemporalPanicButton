using System;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Shared helpers for hazard Harmony patches.
    /// Kept small so each patch file can focus on the vanilla type it modifies.
    /// </summary>
    internal static class HazardPatchUtility
    {
        public static bool AllowTrapLogic()
        {
            return !TimeStopController.IsActive;
        }

        public static bool IsRealTurretShot(FireInfo info)
        {
            if (info == null || info.ignoreTrans == null)
                return false;

            // Player firearms and real turret shots both flow through TurretScript.Shoot.
            // The ignore transform tells us whether the shot originated from a turret object.
            return info.ignoreTrans.GetComponentInParent<TurretScript>() != null;
        }

        public static Exception FilterCancelledTrapException(Exception exception)
        {
            if (exception == null)
                return null;

            if (!TimeStopController.IsActive || !IsKrokMpException(exception))
                return exception;

            // KrokMP trap postfixes can run after this mod cancels vanilla trap logic.
            // Only swallow KrokMP-side fallout while time is stopped; keep other errors visible.
            return null;
        }

        private static bool IsKrokMpException(Exception exception)
        {
            string stack = exception.StackTrace;
            if (!string.IsNullOrEmpty(stack) && stack.IndexOf("KrokoshaCasualtiesMP", StringComparison.Ordinal) >= 0)
                return true;

            return exception.TargetSite != null &&
                   exception.TargetSite.DeclaringType != null &&
                   exception.TargetSite.DeclaringType.FullName != null &&
                   exception.TargetSite.DeclaringType.FullName.StartsWith("KrokoshaCasualtiesMP.", StringComparison.Ordinal);
        }
    }
}
