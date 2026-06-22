using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 时停核心状态机。
    /// Harmony 补丁只调用这里的静态入口；真正的协程、冷却、世界冻结、延迟射击/投掷和多人重叠规则都由这个实例统一管理。
    /// </summary>
    internal sealed class TimeStopController : MonoBehaviour
    {
        private static TimeStopController instance;
        private static readonly List<RemoteTriggerSuppression> RemoteTriggerSuppressions = new List<RemoteTriggerSuppression>();
        private const float RemoteTriggerSuppressionRadius = 3f;
        private const float RemoteMineGraceSeconds = 1f;
        private const float RemoteTurretReloadGraceSeconds = 7f;
        private const uint LocalStopKey = uint.MaxValue;

        private readonly Dictionary<uint, ActiveStop> multiplayerStops = new Dictionary<uint, ActiveStop>();
        private readonly Dictionary<int, PlayerVitalsSnapshot> protectedBodyVitalsSnapshots = new Dictionary<int, PlayerVitalsSnapshot>();
        // 每个内部 stopKey 记录最后处理过的 stopId，防止 KrokMP 回声包把同一次时停重复应用。
        private readonly Dictionary<uint, uint> lastStopIdsByKey = new Dictionary<uint, uint>();
        private readonly PendingTimeStopActions pendingActions = new PendingTimeStopActions();
        private WorldFreezeService worldFreeze;

        private Body playerBody;
        private Rigidbody2D playerRb;
        private float originalTimeScale = 1f;
        private Coroutine routine;
        private float cooldownRemaining;
        private float cooldownTotal;
        private bool cooldownPendingUntilStopEnds;
        private float remainingSeconds;
        private float activeTotalSeconds;
        private bool isMultiplayerStop;
        private bool freezeDirty;
        private bool localEmpoweredEffectsStarted;
        private PlayerVitalsSnapshot playerVitalsSnapshot;

        public static bool IsActive => instance != null && instance.routine != null;
        public static bool IsPlayableContext => HasPlayableLocalBody();
        public static bool IsLocalPlayerEmpowered => !IsActive || instance.IsLocalPlayerAllowedDuringStop();
        public static bool IsLocalPlayerInOwnTimeStop => IsActive && instance.IsLocalPlayerAllowedDuringStop();
        public static bool IsReplayingPendingShots => instance != null && instance.pendingActions.IsReplayingShots;
        public static float ActiveRemainingSeconds => instance == null ? 0f : instance.remainingSeconds;
        public static float ActiveTotalSeconds => instance == null ? 0f : instance.activeTotalSeconds;
        public static float CooldownRemainingSeconds => instance == null ? 0f : instance.cooldownRemaining;
        public static float CooldownTotalSeconds => instance == null ? 0f : instance.cooldownTotal;

        public static bool TryGetActiveDisplay(out bool isLocalStop, out float remaining, out float total)
        {
            isLocalStop = false;
            remaining = 0f;
            total = 0f;

            return instance != null && instance.TryGetActiveDisplayInternal(out isLocalStop, out remaining, out total);
        }

        public static bool IsMultiplayerClientEmpowered(uint clientId)
        {
            if (instance == null || instance.routine == null)
                return true;

            if (!instance.isMultiplayerStop)
                return true;

            return instance.HasActiveStopForClient(clientId);
        }

        public static bool IsBodyEmpowered(Body body)
        {
            if (instance == null || instance.routine == null)
                return true;

            return instance.IsBodyAllowedDuringStop(body);
        }

        public static void Install(MonoBehaviour owner)
        {
            if (instance != null)
                return;

            instance = owner.gameObject.AddComponent<TimeStopController>();
        }

        public static void Uninstall()
        {
            if (instance == null)
                return;

            instance.ClearRunState();

            Destroy(instance);
            instance = null;
        }

        public static bool TryTrigger(HazardKind hazard, Vector2 origin)
        {
            return TryTrigger(hazard, origin, null);
        }

        public static bool TryTrigger(HazardKind hazard, Vector2 origin, Action beforeStartFeedback)
        {
            if (instance == null || !ModSettings.Enabled)
                return false;

            ModSettings.SyncProgressionLockedSettings();
            if (!ModSettings.TimeStopUnlocked)
                return false;

            if (hazard == HazardKind.Mine && !ModSettings.TriggerMines)
                return false;

            if (hazard == HazardKind.Turret && !ModSettings.TriggerTurrets)
                return false;

            if (hazard == HazardKind.Manual && !ModSettings.ManualTrigger)
                return false;

            if (hazard != HazardKind.Manual && IsRemoteTriggerSuppressed(hazard, origin))
                return false;

            if (hazard != HazardKind.Manual && KrokMpCallGuard.IsKrokMpCallStackActive())
                return false;

            // KrokMP 真正可用时，时停归属交给主机广播。
            // 如果联机已经运行但模组握手还没完成，宁可不触发，也不要产生本地单机时停导致不同步。
            if (KrokMpBridge.IsMultiplayerFeatureUsable)
                return instance.TriggerMultiplayer(hazard, origin, beforeStartFeedback);

            if (KrokMpBridge.IsNetworkRunning)
                return false;

            return instance.Trigger(origin, beforeStartFeedback);
        }

        public static bool TryTriggerForMultiplayerClient(uint clientId, HazardKind hazard, Vector2 origin)
        {
            if (instance == null || !ModSettings.Enabled || clientId == uint.MaxValue)
                return false;

            ModSettings.SyncProgressionLockedSettings();
            if (!ModSettings.TimeStopUnlocked)
                return false;

            if (hazard == HazardKind.Mine && !ModSettings.TriggerMines)
                return false;

            if (hazard == HazardKind.Turret && !ModSettings.TriggerTurrets)
                return false;

            if (hazard != HazardKind.Manual && IsRemoteTriggerSuppressed(hazard, origin))
                return true;

            if (instance.HasActiveStopForClient(clientId))
                return true;

            if (!KrokMpBridge.HasPresenceForClient(clientId))
                return false;

            return KrokMpBridge.TryAnnounceTriggerForClient(clientId, hazard, origin, ModSettings.Duration);
        }

        public static bool ReceiveMultiplayerTimeStop(string payload)
        {
            if (instance == null || !ModSettings.Enabled)
                return false;

            if (KrokMpBridge.TryParsePayload(payload, out MultiplayerTimeStopEvent stopEvent))
                return instance.ReceiveMultiplayerTimeStop(stopEvent);

            return false;
        }

        public static bool QueueShotDuringStop(FireInfo info)
        {
            return QueueShotDuringStop(info, false, true);
        }

        public static bool QueueShotDuringStop(FireInfo info, bool replayOnClients, bool playDelayedSound)
        {
            if (instance == null || !IsActive)
                return false;

            return instance.pendingActions.QueueShot(info, replayOnClients, playDelayedSound);
        }

        public static void QueueThrownItemDuringStop(Item item, Vector2 velocity, float angularVelocity)
        {
            if (instance == null || item == null || item.rb == null || !IsActive)
                return;

            instance.pendingActions.QueueThrownItem(item, velocity, angularVelocity);
        }

        public static bool MaintainQueuedThrownItem(Item item)
        {
            if (instance == null || item == null || !IsActive)
                return false;

            return instance.pendingActions.MaintainQueuedThrow(item);
        }

        public static bool MaintainFrozenRigidbody(Rigidbody2D body)
        {
            if (instance == null || body == null || !IsActive || instance.worldFreeze == null)
                return false;

            return instance.worldFreeze.MaintainIfFrozen(body);
        }

        public static bool MaintainNetworkSyncedItemDuringStop(Item item, Vector2 velocity, float angularVelocity)
        {
            if (instance == null || item == null || item.rb == null || !IsActive)
                return false;

            instance.pendingActions.UpdateQueuedThrowVelocityIfMoving(item, velocity, angularVelocity);

            if (instance.pendingActions.MaintainQueuedThrow(item))
                return true;

            if (instance.worldFreeze != null && instance.worldFreeze.MaintainIfFrozen(item.rb))
                return true;

            // 有些物品是在初始冻结之后才从玩家手里丢到世界里的。
            // 这些物品也要加入悬停队列，避免 KrokMP 刚体同步先把它们拉回下落状态。
            instance.pendingActions.QueueThrownItem(item, velocity, angularVelocity);
            return instance.pendingActions.MaintainQueuedThrow(item);
        }

        public static bool ProtectBodyVitalsDuringStop(Body body)
        {
            if (instance == null || body == null || !IsActive)
                return false;

            return instance.ProtectBodyVitalsDuringStopInternal(body);
        }

        private bool Trigger(Vector2 origin, Action beforeStartFeedback)
        {
            // 单人时停中重复触发会被当成“已处理”返回，防止陷阱继续执行原版伤害。
            if (routine != null)
                return true;

            if (cooldownRemaining > 0f)
                return false;

            beforeStartFeedback?.Invoke();
            routine = StartCoroutine(TimeStopRoutine(origin));
            return true;
        }

        private bool TriggerMultiplayer(HazardKind hazard, Vector2 origin, Action beforeStartFeedback)
        {
            if (HasLocalMultiplayerStop())
                return true;

            if (cooldownRemaining > 0f || cooldownPendingUntilStopEnds)
                return false;

            ModSettings.SyncProgressionLockedSettings();
            beforeStartFeedback?.Invoke();
            return KrokMpBridge.TryAnnounceLocalTrigger(hazard, origin, ModSettings.Duration);
        }

        private bool ReceiveMultiplayerTimeStop(MultiplayerTimeStopEvent stopEvent)
        {
            if (stopEvent.Duration <= 0f)
                return false;

            bool isLocalOrigin = KrokMpBridge.IsLocalOrigin(stopEvent.OriginInstanceId) || stopEvent.CasterClientId == KrokMpBridge.LocalClientId;
            uint stopKey = isLocalOrigin ? LocalStopKey : GetRemoteStopKey(stopEvent);

            if (lastStopIdsByKey.TryGetValue(stopKey, out uint lastStopId) && lastStopId == stopEvent.StopId)
                return true;

            lastStopIdsByKey[stopKey] = stopEvent.StopId;
            // 远端陷阱事件可能在多个客户端视角都可见。
            // 对同一位置的同类危险做短暂抑制，避免一发炮台子弹被多个客户端重复触发时停。
            if (stopEvent.Hazard != HazardKind.Manual && !isLocalOrigin)
                SuppressRemoteTrigger(stopEvent.Hazard, stopEvent.Origin, stopEvent.Duration);

            float endTime = Time.realtimeSinceStartup + stopEvent.Duration;
            multiplayerStops[stopKey] = new ActiveStop(stopEvent.CasterClientId, stopEvent.StopId, endTime, stopEvent.Duration, stopEvent.Origin, isLocalOrigin);
            StartCasterPose(stopEvent, isLocalOrigin);

            if (isLocalOrigin)
            {
                ModSettings.SyncProgressionLockedSettings();
                cooldownTotal = ModSettings.Cooldown;
                cooldownRemaining = 0f;
                cooldownPendingUntilStopEnds = cooldownTotal > 0f;
            }

            freezeDirty = true;
            if (routine == null)
                routine = StartCoroutine(MultiplayerTimeStopRoutine(stopEvent.Origin));
            else
                RebuildMultiplayerDisplay(stopEvent.Origin);

            return true;
        }

        private IEnumerator TimeStopRoutine(Vector2 origin)
        {
            isMultiplayerStop = false;
            ModSettings.SyncProgressionLockedSettings();
            remainingSeconds = ModSettings.Duration;
            activeTotalSeconds = remainingSeconds;
            cooldownTotal = ModSettings.Cooldown;
            cooldownRemaining = 0f;
            cooldownPendingUntilStopEnds = cooldownTotal > 0f;
            BeginTimeStop(origin);

            while (remainingSeconds > 0f)
            {
                // 每帧恢复医疗快照：流血、疼痛、意识下降不能恶化，但玩家主动治疗得到的改善会保留。
                playerVitalsSnapshot?.Restore();
                remainingSeconds -= Time.unscaledDeltaTime;
                yield return null;
            }

            routine = null;
            StopTimeStop(true);
        }

        private IEnumerator MultiplayerTimeStopRoutine(Vector2 origin)
        {
            isMultiplayerStop = true;
            BeginTimeStop(origin);

            while (HasActiveMultiplayerStops())
            {
                UpdateMultiplayerDisplay();
                RefreshMultiplayerFreezeState();
                UpdateLocalEmpowermentEffects(GetLocalStopOrigin(), false);
                MaintainMultiplayerVitals();
                worldFreeze?.Maintain();
                if (IsLocalPlayerAllowedDuringStop())
                    ProtectBodyVitalsDuringStopInternal(playerBody);

                yield return null;
            }

            routine = null;
            StopTimeStop(true);
            isMultiplayerStop = false;
        }

        private void Update()
        {
            if (!HasPlayableLocalBody())
            {
                // 回到主菜单或丢失可操作身体时，必须立刻清掉特效、HUD、冷却、冻结对象和排队动作。
                ClearRunStateIfNeeded();
                TimeStopEffect.ClearImmediate();
                return;
            }

            TemporalPanicButtonPlugin.TryInstallKrokMpCompatibilityPatches();
            KrokMpBridge.Warmup();
            HandleManualTriggerInput();

            if (cooldownRemaining > 0f && (routine == null || (isMultiplayerStop && !HasLocalMultiplayerStop())))
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.unscaledDeltaTime);
        }

        private void HandleManualTriggerInput()
        {
            if (!ModSettings.Enabled || !ModSettings.ManualTrigger || (routine != null && !isMultiplayerStop))
                return;

            if (isMultiplayerStop && IsLocalPlayerAllowedDuringStop())
                return;

            KeyCode key = ModSettings.ManualTriggerKey;
            if (key == KeyCode.None || IsManualTriggerBlockedByUi() || !Input.GetKeyDown(key))
                return;

            Vector2 origin = GetManualTriggerOrigin();
            TryTrigger(HazardKind.Manual, origin);
        }

        private static bool IsManualTriggerBlockedByUi()
        {
            if (PauseHandler.main != null && PauseHandler.paused)
                return true;

            if (IsConsoleOpen())
                return true;

            PlayerCamera camera = PlayerCamera.main;
            if (camera == null)
                return false;

            return IsGameObjectActive(camera.containerMenu) ||
                   IsGameObjectActive(camera.tradeMenu);
        }

        private static bool IsConsoleOpen()
        {
            try
            {
                FieldInfo instanceField = typeof(ConsoleScript).GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object console = instanceField == null ? null : instanceField.GetValue(null);
                if (console == null)
                    return false;

                FieldInfo activeField = typeof(ConsoleScript).GetField("active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return activeField != null && activeField.GetValue(console) is bool active && active;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGameObjectActive(GameObject obj)
        {
            return obj != null && obj.activeInHierarchy;
        }

        private void LateUpdate()
        {
            if (routine != null)
            {
                // 本模组不把全局 timeScale 当作主要冻结手段。
                // 保持它为 1，才能让 UI、玩家动画和医疗小游戏继续运行。
                Time.timeScale = 1f;
                if (!isMultiplayerStop || IsLocalPlayerAllowedDuringStop())
                    ProtectBodyVitalsDuringStopInternal(playerBody);

                worldFreeze?.Maintain();
                if (isMultiplayerStop)
                    MaintainMultiplayerVitals();
            }
        }

        private void BeginTimeStop(Vector2 origin)
        {
            originalTimeScale = Time.timeScale;
            playerBody = PlayerCamera.main == null ? null : PlayerCamera.main.body;
            playerRb = playerBody == null ? null : playerBody.rb;
            playerVitalsSnapshot = null;
            localEmpoweredEffectsStarted = false;
            if (worldFreeze == null)
                worldFreeze = new WorldFreezeService(IsPlayerRigidbody, IsAllowedPlayerBehaviour);

            // 先播放开始音效再冻结世界，避免附近行为刚被冻结就吞掉第一声联机时停音效。
            TimeStopAudio.PlayStart();
            FreezeWorld();
            TimeStopHazardAudio.PauseSoundCannonAudio();

            Time.timeScale = 1f;
            UpdateLocalEmpowermentEffects(origin, false);
            TimeStopEffect.Begin(origin, ModSettings.EffectIntensity);
        }

        private void StopTimeStop(bool replayShots)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            Time.timeScale = Mathf.Approximately(originalTimeScale, 0f) ? 1f : originalTimeScale;
            TimeStopPoseVisual.StopPose();
            RestoreWorld();
            TimeStopHazardAudio.ResumeSoundCannonAudio();
            TimeStopAudio.PlayEnd();
            TimeStopEffect.EndAtScreenCenter(ModSettings.EffectIntensity);
            TimeStopBulletPreview.Clear();

            if (replayShots)
            {
                // 先恢复世界，再释放延迟投掷物和延迟射击，避免它们仍被冻结状态拦住。
                pendingActions.ReleaseQueuedThrows();
                pendingActions.ReplayQueuedShots();
            }

            StartPendingCooldown();

            pendingActions.Clear();
            playerBody = null;
            playerRb = null;
            playerVitalsSnapshot = null;
            protectedBodyVitalsSnapshots.Clear();
            remainingSeconds = 0f;
            activeTotalSeconds = 0f;
            isMultiplayerStop = false;
            freezeDirty = false;
            localEmpoweredEffectsStarted = false;
        }

        private void ClearRunStateIfNeeded()
        {
            if (routine == null &&
                cooldownRemaining <= 0f &&
                cooldownTotal <= 0f &&
                remainingSeconds <= 0f &&
                activeTotalSeconds <= 0f &&
                multiplayerStops.Count == 0 &&
                !pendingActions.HasPendingActions)
                return;

            ClearRunState();
        }

        private void ClearRunState()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            Time.timeScale = Mathf.Approximately(originalTimeScale, 0f) ? 1f : originalTimeScale;
            TimeStopPoseVisual.StopPose();
            RestoreWorld();
            TimeStopHazardAudio.ResumeSoundCannonAudio();
            TimeStopEffect.ClearImmediate();
            TimeStopBulletPreview.Clear();

            pendingActions.Clear();
            multiplayerStops.Clear();
            protectedBodyVitalsSnapshots.Clear();
            lastStopIdsByKey.Clear();
            RemoteTriggerSuppressions.Clear();
            playerBody = null;
            playerRb = null;
            playerVitalsSnapshot = null;
            cooldownRemaining = 0f;
            cooldownTotal = 0f;
            cooldownPendingUntilStopEnds = false;
            remainingSeconds = 0f;
            activeTotalSeconds = 0f;
            isMultiplayerStop = false;
            freezeDirty = false;
            localEmpoweredEffectsStarted = false;
        }

        private static void SuppressRemoteTrigger(HazardKind hazard, Vector2 origin, float duration)
        {
            PruneRemoteTriggerSuppressions();
            float grace = hazard == HazardKind.Turret ? RemoteTurretReloadGraceSeconds : RemoteMineGraceSeconds;
            float endTime = Time.realtimeSinceStartup + Mathf.Max(0.5f, duration) + grace;

            for (int i = 0; i < RemoteTriggerSuppressions.Count; i++)
            {
                if (RemoteTriggerSuppressions[i].Hazard != hazard ||
                    (RemoteTriggerSuppressions[i].Origin - origin).sqrMagnitude > RemoteTriggerSuppressionRadius * RemoteTriggerSuppressionRadius)
                    continue;

                RemoteTriggerSuppressions[i] = new RemoteTriggerSuppression(hazard, origin, Mathf.Max(RemoteTriggerSuppressions[i].EndTime, endTime));
                return;
            }

            RemoteTriggerSuppressions.Add(new RemoteTriggerSuppression(hazard, origin, endTime));
        }

        private static bool IsRemoteTriggerSuppressed(HazardKind hazard, Vector2 origin)
        {
            PruneRemoteTriggerSuppressions();
            float radiusSqr = RemoteTriggerSuppressionRadius * RemoteTriggerSuppressionRadius;
            for (int i = 0; i < RemoteTriggerSuppressions.Count; i++)
            {
                if (RemoteTriggerSuppressions[i].Hazard == hazard &&
                    (RemoteTriggerSuppressions[i].Origin - origin).sqrMagnitude <= radiusSqr)
                    return true;
            }

            return false;
        }

        private static void PruneRemoteTriggerSuppressions()
        {
            if (RemoteTriggerSuppressions.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            for (int i = RemoteTriggerSuppressions.Count - 1; i >= 0; i--)
            {
                if (RemoteTriggerSuppressions[i].EndTime <= now)
                    RemoteTriggerSuppressions.RemoveAt(i);
            }
        }

        private void FreezeWorld()
        {
            if (worldFreeze == null)
                worldFreeze = new WorldFreezeService(IsPlayerRigidbody, IsAllowedPlayerBehaviour);

            worldFreeze.Freeze();
        }

        private bool IsPlayerRigidbody(Rigidbody2D body)
        {
            if (body == null)
                return true;

            Limb limb = body.GetComponent<Limb>();
            if (limb != null)
                return IsBodyAllowedDuringStop(limb.body);

            Body bodyComponent = body.GetComponent<Body>();
            if (bodyComponent != null)
                return IsBodyAllowedDuringStop(bodyComponent);

            return body == playerRb && IsLocalPlayerAllowedDuringStop();
        }

        private bool IsAllowedPlayerBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null)
                return false;

            Body body = behaviour as Body;
            if (body == null)
                body = behaviour.GetComponentInParent<Body>();

            if (body != null)
                return IsBodyAllowedDuringStop(body);

            Limb limb = behaviour as Limb;
            if (limb == null)
                limb = behaviour.GetComponentInParent<Limb>();

            return limb != null && IsBodyAllowedDuringStop(limb.body);
        }

        private bool IsBodyAllowedDuringStop(Body body)
        {
            if (body == null)
                return false;

            if (!isMultiplayerStop)
                return body == playerBody;

            // 本机身体不能依赖 KrokMP 的 clientId 反查。
            // 该映射如果滞后或误指远端发动者，会让没有权限的本机玩家错误保持可动。
            if (body == playerBody)
                return HasLocalMultiplayerStop();

            // 远端发动者在本客户端保持动画，其他没有时停窗口的身体冻结。
            uint clientId = KrokPlayerResolver.GetClientIdForBody(body);
            if (clientId == uint.MaxValue)
                return false;

            return HasActiveStopForClient(clientId);
        }

        private bool IsLocalPlayerAllowedDuringStop()
        {
            if (!isMultiplayerStop)
                return true;

            return HasLocalMultiplayerStop();
        }

        private bool HasActiveStopForClient(uint clientId)
        {
            PruneExpiredMultiplayerStops();
            foreach (KeyValuePair<uint, ActiveStop> pair in multiplayerStops)
            {
                if (!pair.Value.IsLocal && pair.Value.CasterClientId == clientId)
                    return true;
            }

            return false;
        }

        private static uint GetRemoteStopKey(MultiplayerTimeStopEvent stopEvent)
        {
            // uint.MaxValue 被保留给本机自己的时停；即使 KrokMP 报出这个值，远端 key 也要避开。
            if (stopEvent.CasterClientId != uint.MaxValue)
                return stopEvent.CasterClientId;

            return uint.MaxValue - 1;
        }

        private bool HasLocalMultiplayerStop()
        {
            PruneExpiredMultiplayerStops();
            return multiplayerStops.TryGetValue(LocalStopKey, out ActiveStop stop) && stop.IsLocal;
        }

        private bool HasActiveMultiplayerStops()
        {
            PruneExpiredMultiplayerStops();
            return multiplayerStops.Count > 0;
        }

        private void PruneExpiredMultiplayerStops()
        {
            if (multiplayerStops.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            List<uint> expired = null;
            foreach (KeyValuePair<uint, ActiveStop> pair in multiplayerStops)
            {
                if (pair.Value.EndTime > now)
                    continue;

                if (expired == null)
                    expired = new List<uint>();

                expired.Add(pair.Key);
            }

            if (expired == null)
                return;

            for (int i = 0; i < expired.Count; i++)
            {
                if (expired[i] == LocalStopKey)
                    StartPendingCooldown();

                multiplayerStops.Remove(expired[i]);
            }

            // 可行动身体集合变了，下一轮要重新建立冻结快照。
            freezeDirty = true;
        }

        private void MaintainMultiplayerVitals()
        {
            if (!isMultiplayerStop)
                return;

            foreach (KeyValuePair<uint, ActiveStop> pair in multiplayerStops)
            {
                Body body = pair.Value.IsLocal ? (playerBody ?? PlayerCamera.main?.body) : KrokPlayerResolver.GetBodyForClientId(pair.Value.CasterClientId);
                ProtectBodyVitalsDuringStopInternal(body);
            }
        }

        private bool ProtectBodyVitalsDuringStopInternal(Body body)
        {
            if (body == null)
                return false;

            if (!ShouldProtectBodyVitalsDuringStop(body))
                return false;

            int bodyKey = body.GetInstanceID();
            if (!protectedBodyVitalsSnapshots.TryGetValue(bodyKey, out PlayerVitalsSnapshot snapshot) || snapshot == null)
            {
                snapshot = PlayerVitalsSnapshot.Capture(body);
                if (snapshot == null)
                    return false;

                protectedBodyVitalsSnapshots[bodyKey] = snapshot;
            }

            snapshot.Restore();
            return true;
        }

        private bool ShouldProtectBodyVitalsDuringStop(Body body)
        {
            if (body == null)
                return false;

            if (!isMultiplayerStop)
                return body == playerBody;

            Body localBody = playerBody ?? PlayerCamera.main?.body;
            if (body == localBody)
                return true;

            uint clientId = KrokPlayerResolver.GetClientIdForBody(body);
            return clientId != uint.MaxValue;
        }

        private void StartPendingCooldown()
        {
            if (!cooldownPendingUntilStopEnds)
                return;

            cooldownPendingUntilStopEnds = false;
            cooldownRemaining = Mathf.Max(cooldownRemaining, cooldownTotal);
        }

        private void UpdateMultiplayerDisplay()
        {
            float now = Time.realtimeSinceStartup;
            float bestRemaining = 0f;
            float bestTotal = 0f;

            foreach (KeyValuePair<uint, ActiveStop> pair in multiplayerStops)
            {
                float remaining = pair.Value.EndTime - now;
                if (remaining <= bestRemaining)
                    continue;

                bestRemaining = remaining;
                bestTotal = pair.Value.Duration;
            }

            remainingSeconds = Mathf.Max(0f, bestRemaining);
            activeTotalSeconds = Mathf.Max(0.1f, bestTotal);
        }

        private bool TryGetActiveDisplayInternal(out bool isLocalStop, out float remaining, out float total)
        {
            isLocalStop = false;
            remaining = 0f;
            total = 0f;

            if (routine == null)
                return false;

            if (!isMultiplayerStop)
            {
                isLocalStop = true;
                remaining = remainingSeconds;
                total = activeTotalSeconds;
                return remaining > 0f;
            }

            PruneExpiredMultiplayerStops();
            float now = Time.realtimeSinceStartup;
            if (multiplayerStops.TryGetValue(LocalStopKey, out ActiveStop localStop) && localStop.IsLocal)
            {
                // HUD 优先显示本机自己的时停；只有本机没有时停窗口时才显示别人的时停。
                isLocalStop = true;
                remaining = Mathf.Max(0f, localStop.EndTime - now);
                total = Mathf.Max(0.1f, localStop.Duration);
                return remaining > 0f;
            }

            float bestRemaining = 0f;
            float bestTotal = 0f;
            foreach (KeyValuePair<uint, ActiveStop> pair in multiplayerStops)
            {
                float stopRemaining = pair.Value.EndTime - now;
                if (stopRemaining <= bestRemaining)
                    continue;

                bestRemaining = stopRemaining;
                bestTotal = pair.Value.Duration;
            }

            remaining = Mathf.Max(0f, bestRemaining);
            total = Mathf.Max(0.1f, bestTotal);
            return remaining > 0f;
        }

        private void RebuildMultiplayerDisplay(Vector2 origin)
        {
            if (!isMultiplayerStop)
                return;

            UpdateMultiplayerDisplay();
            RefreshMultiplayerFreezeState();
            TimeStopEffect.Pulse(origin, ModSettings.EffectIntensity);
        }

        private void RefreshMultiplayerFreezeState()
        {
            if (!isMultiplayerStop || !freezeDirty)
                return;

            freezeDirty = false;
            // 从干净状态重新冻结，确保刚获得或失去权限的玩家、limb、Body 行为和附近对象都落到正确状态。
            RestoreWorld();
            FreezeWorld();
        }

        private void UpdateLocalEmpowermentEffects(Vector2 origin, bool playAudioOnStart)
        {
            bool allowed = IsLocalPlayerAllowedDuringStop();
            if (allowed)
            {
                if (playerVitalsSnapshot == null)
                    playerVitalsSnapshot = PlayerVitalsSnapshot.Capture(playerBody);

                if (!localEmpoweredEffectsStarted)
                {
                    localEmpoweredEffectsStarted = true;
                    if (!isMultiplayerStop)
                        TimeStopPoseVisual.StartPose(playerBody, origin, Mathf.Min(2f, ModSettings.Duration));

                    if (playAudioOnStart)
                        TimeStopAudio.PlayStart();
                }

                return;
            }

            if (!localEmpoweredEffectsStarted)
                return;

            TimeStopPoseVisual.StopPose(playerBody);
            localEmpoweredEffectsStarted = false;
            playerVitalsSnapshot = null;
        }

        private void StartCasterPose(MultiplayerTimeStopEvent stopEvent, bool isLocalOrigin)
        {
            Body casterBody = isLocalOrigin ? (playerBody ?? PlayerCamera.main?.body) : KrokPlayerResolver.GetBodyForClientId(stopEvent.CasterClientId);
            if (casterBody == null)
                return;

            TimeStopPoseVisual.StartPose(casterBody, stopEvent.Origin, Mathf.Min(2f, stopEvent.Duration));
        }

        private Vector2 GetLocalStopOrigin()
        {
            if (isMultiplayerStop && multiplayerStops.TryGetValue(LocalStopKey, out ActiveStop stop) && stop.IsLocal)
                return stop.Origin;

            return GetManualTriggerOrigin();
        }

        private void RestoreWorld()
        {
            worldFreeze?.Restore();
        }

        private static Vector2 GetManualTriggerOrigin()
        {
            if (PlayerCamera.main != null && PlayerCamera.main.body != null)
                return PlayerCamera.main.body.transform.position;

            return Vector2.zero;
        }

        private static bool HasPlayableLocalBody()
        {
            PlayerCamera camera = PlayerCamera.main;
            if (camera == null || camera.body == null)
                return false;

            // 玩家被别人时停冻结时，Body 组件可能被故意禁用。
            // 不能把这种情况误判成“回到主菜单”，否则控制器会立刻清掉正在进行的时停。
            return camera.body.gameObject != null && camera.body.gameObject.activeInHierarchy;
        }

        private readonly struct RemoteTriggerSuppression
        {
            public readonly HazardKind Hazard;
            public readonly Vector2 Origin;
            public readonly float EndTime;

            public RemoteTriggerSuppression(HazardKind hazard, Vector2 origin, float endTime)
            {
                Hazard = hazard;
                Origin = origin;
                EndTime = endTime;
            }
        }

        private readonly struct ActiveStop
        {
            public readonly uint CasterClientId;
            public readonly uint StopId;
            public readonly float EndTime;
            public readonly float Duration;
            public readonly Vector2 Origin;
            public readonly bool IsLocal;

            public ActiveStop(uint casterClientId, uint stopId, float endTime, float duration, Vector2 origin, bool isLocal)
            {
                CasterClientId = casterClientId;
                StopId = stopId;
                EndTime = endTime;
                Duration = duration;
                Origin = origin;
                IsLocal = isLocal;
            }
        }
    }

    /// <summary>
    /// 时停触发来源类型。
    /// 设置开关、KrokMP 消息和远端陷阱抑制都会用这个枚举保持同一套语义。
    /// </summary>
    internal enum HazardKind
    {
        Mine,
        Turret,
        Manual
    }
}
