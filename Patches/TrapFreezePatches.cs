using System;
using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Stops non-turret traps from updating or triggering while time is stopped.
    /// These traps should not queue delayed effects; they simply wait for time to resume.
    /// </summary>
    [HarmonyPatch(typeof(JumpPadScript), "OnCollisionEnter2D")]
    internal static class JumpPadScriptOnCollisionEnter2DPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(JumpPadScript), "Update")]
    internal static class JumpPadScriptUpdatePatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(BearTrap), "OnTriggerEnter2D")]
    internal static class BearTrapOnTriggerEnter2DPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(BearTrap), "Update")]
    internal static class BearTrapUpdatePatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(CoilScript), "OnCollisionEnter2D")]
    internal static class CoilScriptOnCollisionEnter2DPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(CoilScript), nameof(CoilScript.Shock))]
    internal static class CoilScriptShockPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(CoilScript), "Update")]
    internal static class CoilScriptUpdatePatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(SpikeStabberScript), "OnTriggerEnter2D")]
    internal static class SpikeStabberScriptOnTriggerEnter2DPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(SpikeStabberScript), nameof(SpikeStabberScript.Stab))]
    internal static class SpikeStabberScriptStabPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(SpikeStabberScript), nameof(SpikeStabberScript.CheckStab))]
    internal static class SpikeStabberScriptCheckStabPatch
    {
        private static bool Prefix()
        {
            return HazardPatchUtility.AllowTrapLogic();
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(SoundCannon), "Update")]
    internal static class SoundCannonUpdatePatch
    {
        private static bool Prefix()
        {
            if (!TimeStopController.IsActive)
                return true;

            // Sound Cannon has both tracking logic and charge audio. Freeze its Update and
            // pause tracked audio together so it does not fire silently after resume.
            TimeStopHazardAudio.PauseSoundCannonAudio();
            return false;
        }

        private static Exception Finalizer(Exception __exception)
        {
            return HazardPatchUtility.FilterCancelledTrapException(__exception);
        }
    }

    [HarmonyPatch(typeof(Sound), nameof(Sound.Play), new Type[]
    {
        typeof(string),
        typeof(Vector2),
        typeof(bool),
        typeof(bool),
        typeof(Transform),
        typeof(float),
        typeof(float),
        typeof(bool),
        typeof(bool)
    })]
    internal static class SoundPlayStringPatch
    {
        private static void Postfix(string clip, AudioSource __result)
        {
            // Register after Sound.Play returns because the AudioSource is created by vanilla code.
            TimeStopHazardAudio.RegisterSoundCannonAudio(clip, __result);
        }
    }
}
