using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Dynamite 临时功能控制器。
    /// 负责暂停已点燃炸药的倒计时，并提供“剪短引线”按钮，让下一次使用几乎立刻爆炸。
    /// </summary>
    internal sealed class DynamiteFuseCutFeature : MonoBehaviour
    {
        private const string DynamiteId = "dynamite";
        private const float CutFuseSeconds = 0.1f;

        private static DynamiteFuseCutFeature instance;
        private static readonly HashSet<int> CutFuseItems = new HashSet<int>();
        private static readonly Dictionary<int, LitDynamite> LitDynamites = new Dictionary<int, LitDynamite>();
        private static readonly List<int> IterationIds = new List<int>();
        private static readonly List<int> CleanupIds = new List<int>();

        private GUIStyle buttonStyle;

        public static void Install(MonoBehaviour owner)
        {
            if (instance != null || owner == null)
                return;

            instance = owner.gameObject.AddComponent<DynamiteFuseCutFeature>();
        }

        public static void Uninstall()
        {
            if (instance == null)
                return;

            CutFuseItems.Clear();
            LitDynamites.Clear();
            IterationIds.Clear();
            CleanupIds.Clear();
            Destroy(instance);
            instance = null;
        }

        public static bool IsDynamite(CustomItemBehaviour behaviour)
        {
            return behaviour != null && IsDynamite(behaviour.GetComponent<Item>());
        }

        public static void AfterUseItem(Body body, Item item)
        {
            if (!IsDynamite(item))
                return;

            CustomItemBehaviour behaviour = item.GetComponent<CustomItemBehaviour>();
            if (behaviour == null || !IsLit(behaviour))
                return;

            int id = item.GetInstanceID();
            // 原版通过 Invoke 安排 DynamiteExplode，无法感知时停。
            // 这里取消原版 Invoke，改用本类维护的可暂停倒计时。
            behaviour.CancelInvoke("DynamiteExplode");
            if (LitDynamites.ContainsKey(id))
                return;

            float duration = CutFuseItems.Contains(id) ? CutFuseSeconds : 5f;
            LitDynamites[id] = new LitDynamite(behaviour, item, duration);
            KrokMpBridge.TryServerSyncItem(item, true);
        }

        public static bool TryDelayDynamiteExplosion(CustomItemBehaviour behaviour)
        {
            if (!TimeStopController.IsActive || !IsDynamite(behaviour))
                return false;

            Item item = behaviour.GetComponent<Item>();
            if (item != null)
            {
                int id = item.GetInstanceID();
                if (!LitDynamites.ContainsKey(id))
                    LitDynamites[id] = new LitDynamite(behaviour, item, CutFuseSeconds);
            }

            behaviour.CancelInvoke("DynamiteExplode");
            return true;
        }

        private void Update()
        {
            if (!TimeStopController.IsPlayableContext)
            {
                LitDynamites.Clear();
                CutFuseItems.Clear();
                IterationIds.Clear();
                return;
            }

            if (LitDynamites.Count == 0)
                return;

            IterationIds.Clear();
            CleanupIds.Clear();
            foreach (int id in LitDynamites.Keys)
                IterationIds.Add(id);

            for (int i = 0; i < IterationIds.Count; i++)
            {
                int id = IterationIds[i];
                if (!LitDynamites.TryGetValue(id, out LitDynamite lit))
                    continue;

                if (lit.Behaviour == null || lit.Item == null)
                {
                    CleanupIds.Add(id);
                    continue;
                }

                lit.Behaviour.CancelInvoke("DynamiteExplode");
                if (TimeStopController.IsActive || (PauseHandler.main != null && PauseHandler.paused))
                    continue;

                // 时停外使用 scaled delta，让普通暂停菜单仍然能暂停炸药计时。
                lit.Remaining -= Time.deltaTime;
                if (lit.Remaining > 0f)
                {
                    LitDynamites[id] = lit;
                    continue;
                }

                CleanupIds.Add(id);
                Explode(lit.Behaviour);
            }

            for (int i = 0; i < CleanupIds.Count; i++)
                LitDynamites.Remove(CleanupIds[i]);
        }

        private static void Explode(CustomItemBehaviour behaviour)
        {
            if (behaviour == null)
                return;

            Traverse.Create(behaviour).Method("DynamiteExplode").GetValue();
        }

        private static bool IsLit(CustomItemBehaviour behaviour)
        {
            object[] data = Traverse.Create(behaviour).Field("data").GetValue<object[]>();
            if (data == null || data.Length == 0 || !(data[0] is bool))
                return false;

            return (bool)data[0];
        }

        private void CutFuse(Item item)
        {
            if (!IsDynamite(item))
                return;

            MarkFuseCut(item);
            KrokMpBridge.TryAnnounceDynamiteFuseCut(item);
        }

        public static bool ApplyNetworkFuseCut(uint syncId)
        {
            if (!KrokMpBridge.TryGetItemBySyncId(syncId, out Item item) || !IsDynamite(item))
                return false;

            MarkFuseCut(item);
            KrokMpBridge.TryServerSyncItem(item, true);
            return true;
        }

        private static void MarkFuseCut(Item item)
        {
            if (!IsDynamite(item))
                return;

            int id = item.GetInstanceID();
            CutFuseItems.Add(id);
            if (LitDynamites.TryGetValue(id, out LitDynamite lit))
            {
                lit.Remaining = Mathf.Min(lit.Remaining, CutFuseSeconds);
                LitDynamites[id] = lit;
            }
        }

        private void OnGUI()
        {
            if (!TimeStopController.IsPlayableContext)
                return;

            Body body = PlayerCamera.main == null ? null : PlayerCamera.main.body;
            Item item = body == null ? null : body.GetItem(body.handSlot);
            if (!IsDynamite(item))
                return;

            EnsureStyle();
            Rect rect = new Rect(Screen.width - 156f, Screen.height * 0.5f - 22f, 140f, 44f);
            string text = CutFuseItems.Contains(item.GetInstanceID()) ? "FUSE CUT" : "CUT FUSE";
            if (GUI.Button(rect, text, buttonStyle))
                CutFuse(item);
        }

        private void EnsureStyle()
        {
            if (buttonStyle != null)
                return;

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static bool IsDynamite(Item item)
        {
            return item != null && item.id == DynamiteId;
        }

        private struct LitDynamite
        {
            public readonly CustomItemBehaviour Behaviour;
            public readonly Item Item;
            public float Remaining;

            public LitDynamite(CustomItemBehaviour behaviour, Item item, float remaining)
            {
                Behaviour = behaviour;
                Item = item;
                Remaining = remaining;
            }
        }
    }
}
