using System;
using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 冻结非炮台类陷阱。
    /// 这些陷阱没有“延迟结算”的需求，时停期间直接停住，恢复后再继续原版逻辑。
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

            // 音波炮既有追踪逻辑也有蓄力音效。
            // 冻结 Update 的同时暂停音效，避免恢复后出现静音开火或音效错位。
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
            // AudioSource 是原版 Sound.Play 创建的，必须等返回后才能登记追踪。
            TimeStopHazardAudio.RegisterSoundCannonAudio(clip, __result);
        }
    }
}
