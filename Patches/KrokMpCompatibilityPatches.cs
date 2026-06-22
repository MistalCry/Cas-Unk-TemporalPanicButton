using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 针对 KrokMP 的可选兼容补丁。
    /// 项目不直接引用 KrokMP DLL，所以这里用类型名和反射按需安装，避免单人玩家没有 KrokMP 时启动失败。
    /// </summary>
    internal static class KrokMpCompatibilityPatches
    {
        private const string SoundCannonTrackerTypeName = "KrokoshaCasualtiesMP.KrokoshaSoundCannonNetworkTrackerComponent";

        private static bool installed;
        private static bool failed;

        public static void TryInstall(Harmony harmony)
        {
            if (installed || failed || harmony == null)
                return;

            if (AccessTools.TypeByName("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer") == null)
                return;

            try
            {
                int patched = 0;
                // 不同 KrokMP 版本可能缺少部分类型或方法，PatchMethod 返回 0 表示该项不存在，整体继续安装。
                patched += PatchMethod(harmony, SoundCannonTrackerTypeName, "Update", nameof(SoundCannonTrackerUpdatePrefix));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.JumpPadScript_OnCollisionEnter2D_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.CoilScript_Shock_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.BearTrap_OnCollisionEnter2D_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.SpikeStabberScript_Stab_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.SpikeStabberScript_CheckStab_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.TurretScript_Shoot_MultiplayerPatch", "ApplyShootDamages", nameof(FilterKrokMpTurretDamagePrefix));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.MedicalSync", "Server_SendCharacterHealth", nameof(KrokMpServerSendCharacterHealthPrefix));
                patched += PatchBodyConstructor(harmony, "KrokoshaCasualtiesMP.CharacterHealthStateSyncPacket", nameof(KrokMpHealthPacketBodyPrefix));
                patched += PatchBodyConstructor(harmony, "KrokoshaCasualtiesMP.CharacterHealthPainkillerStateSyncPacket", nameof(KrokMpHealthPacketBodyPrefix));
                patched += PatchMethodPostfix(harmony, "KrokoshaCasualtiesMP.ItemSync", "BetterUseItem", nameof(KrokMpBetterUseItemPostfix));
                patched += PatchMethodPostfix(harmony, "KrokoshaCasualtiesMP.RB2DSyncPacket", "ApplyDirect", nameof(KrokMpRb2dSyncPacketApplyDirectPostfix));
                patched += PatchMethodPostfix(harmony, "KrokoshaCasualtiesMP.ItemOrBuildingCoolDeltaCompressablePacket", "ReadPacketIntoObject", nameof(KrokMpRb2dSyncPacketApplyDirectPostfix));

                installed = patched > 0;
            }
            catch (Exception ex)
            {
                failed = true;
                Debug.LogWarning("Temporal Panic Button: KrokMP compatibility patch failed: " + ex);
            }
        }

        public static bool IsKrokMpSoundCannonTracker(MonoBehaviour behaviour)
        {
            return behaviour != null && behaviour.GetType().FullName == SoundCannonTrackerTypeName;
        }

        private static int PatchMethod(Harmony harmony, string typeName, string methodName, string prefixName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo original = type == null ? null : AccessTools.Method(type, methodName);
            MethodInfo prefix = AccessTools.Method(typeof(KrokMpCompatibilityPatches), prefixName);
            if (original == null || prefix == null)
                return 0;

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            return 1;
        }

        private static int PatchMethodPostfix(Harmony harmony, string typeName, string methodName, string postfixName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo original = type == null ? null : AccessTools.Method(type, methodName);
            MethodInfo postfix = AccessTools.Method(typeof(KrokMpCompatibilityPatches), postfixName);
            if (original == null || postfix == null)
                return 0;

            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            return 1;
        }

        private static int PatchBodyConstructor(Harmony harmony, string typeName, string prefixName)
        {
            Type type = AccessTools.TypeByName(typeName);
            ConstructorInfo original = type == null ? null : AccessTools.Constructor(type, new[] { typeof(Body) });
            MethodInfo prefix = AccessTools.Method(typeof(KrokMpCompatibilityPatches), prefixName);
            if (original == null || prefix == null)
                return 0;

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            return 1;
        }

        private static bool SkipKrokMpTrapPatchDuringTimeStop()
        {
            // KrokMP 的 postfix 通常默认原版陷阱逻辑已经执行。
            // 如果本模组已经在时停中跳过原版逻辑，对应的 KrokMP 网络补丁也必须一起跳过。
            return !TimeStopController.IsActive;
        }

        private static bool SoundCannonTrackerUpdatePrefix(MonoBehaviour __instance)
        {
            if (!TimeStopController.IsActive)
                return true;

            // KrokMP 有自己的音波炮追踪器，需要和原版 SoundCannon 一起冻结。
            TimeStopHazardAudio.PauseSoundCannonAudio();
            return false;
        }

        private static void FilterKrokMpTurretDamagePrefix(ref IEnumerable<Limb> hitLimbs)
        {
            if (!TimeStopController.IsActive || hitLimbs == null)
                return;

            List<Limb> keptLimbs = new List<Limb>();
            foreach (Limb limb in hitLimbs)
            {
                if (limb == null)
                    continue;

                if (!TimeStopController.IsBodyEmpowered(limb.body))
                    keptLimbs.Add(limb);
            }

            hitLimbs = keptLimbs;
        }

        private static void KrokMpBetterUseItemPostfix(Body body, Item item)
        {
            DynamiteFuseCutFeature.AfterUseItem(body, item);
        }

        private static void KrokMpServerSendCharacterHealthPrefix(object nb)
        {
            Body body = GetNetBodyBody(nb);
            if (body != null)
                TimeStopController.ProtectBodyVitalsDuringStop(body);
        }

        private static void KrokMpHealthPacketBodyPrefix(Body body)
        {
            TimeStopController.ProtectBodyVitalsDuringStop(body);
        }

        private static void KrokMpRb2dSyncPacketApplyDirectPostfix(object si)
        {
            if (!TimeStopController.IsActive || si == null)
                return;

            Item item = GetSyncInfoItem(si);
            if (item == null || item.rb == null)
                return;

            TimeStopController.MaintainNetworkSyncedItemDuringStop(item, item.rb.velocity, item.rb.angularVelocity);
        }

        private static Item GetSyncInfoItem(object si)
        {
            try
            {
                PropertyInfo property = si.GetType().GetProperty("item", BindingFlags.Instance | BindingFlags.Public);
                return property == null ? null : property.GetValue(si, null) as Item;
            }
            catch
            {
                return null;
            }
        }

        private static Body GetNetBodyBody(object netBody)
        {
            if (netBody == null)
                return null;

            try
            {
                PropertyInfo property = netBody.GetType().GetProperty("body", BindingFlags.Instance | BindingFlags.Public);
                if (property != null)
                    return property.GetValue(netBody, null) as Body;

                FieldInfo field = netBody.GetType().GetField("<body>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return field == null ? null : field.GetValue(netBody) as Body;
            }
            catch
            {
                return null;
            }
        }

    }
}
