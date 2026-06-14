using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Central state machine for local and multiplayer time stops.
    /// Harmony patches call the static surface here; the instance owns coroutines, cooldowns,
    /// world freezing, pending shots/throws, and multiplayer overlap rules.
    /// </summary>
    internal sealed class TimeStopController : MonoBehaviour
    {
        private static TimeStopController instance;
        private static readonly List<RemoteTurretSuppression> RemoteTurretSuppressions = new List<RemoteTurretSuppression>();
        private const float RemoteTurretSuppressionRadius = 3f;
        private const float RemoteTurretReloadGraceSeconds = 7f;

        private readonly Dictionary<uint, ActiveStop> multiplayerStops = new Dictionary<uint, ActiveStop>();
        // Last processed stop id per caster prevents replaying the same KrokMP announcement twice.
        private readonly Dictionary<uint, uint> lastStopIdsByCaster = new Dictionary<uint, uint>();
        private readonly PendingTimeStopActions pendingActions = new PendingTimeStopActions();
        private WorldFreezeService worldFreeze;

        private Body playerBody;
        private Rigidbody2D playerRb;
        private float originalTimeScale = 1f;
        private Coroutine routine;
        private float cooldownRemaining;
        private float cooldownTotal;
        private float remainingSeconds;
        private float activeTotalSeconds;
        private bool isMultiplayerStop;
        private bool freezeDirty;
        private bool localEmpoweredEffectsStarted;
        private PlayerVitalsSnapshot playerVitalsSnapshot;

        public static bool IsActive => instance != null && instance.routine != null;
        public static bool IsPlayableContext => HasPlayableLocalBody();
        public static bool IsLocalPlayerEmpowered => !IsActive || instance.IsLocalPlayerAllowedDuringStop();
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

            if (hazard == HazardKind.Turret && IsRemoteTurretTriggerSuppressed(origin))
                return false;

            // Multiplayer trigger ownership is delegated to KrokMP if it is actually ready.
            // If KrokMP is running but the bridge is not ready, fail closed to avoid desync.
            if (KrokMpBridge.IsAvailable)
                return instance.TriggerMultiplayer(hazard, origin);

            if (KrokMpBridge.IsNetworkRunning)
                return false;

            return instance.Trigger(origin);
        }

        public static void ReceiveMultiplayerTimeStop(string payload)
        {
            if (instance == null || !ModSettings.Enabled)
                return;

            if (KrokMpBridge.TryParsePayload(payload, out MultiplayerTimeStopEvent stopEvent))
                instance.ReceiveMultiplayerTimeStop(stopEvent);
        }

        public static bool QueueShotDuringStop(FireInfo info)
        {
            return QueueShotDuringStop(info, false, true);
        }

        public static bool QueueShotDuringStop(FireInfo info, bool replayOnClients, bool playDelayedSound)
        {
            if (instance == null)
                return false;

            return instance.pendingActions.QueueShot(info, replayOnClients, playDelayedSound);
        }

        public static void QueueThrownItemDuringStop(Item item, Vector2 velocity, float angularVelocity)
        {
            if (instance == null || item == null || item.rb == null || !IsActive)
                return;

            instance.pendingActions.QueueThrownItem(item, velocity, angularVelocity);
        }

        private bool Trigger(Vector2 origin)
        {
            // During a local-only stop, repeat triggers are intentionally ignored but reported
            // as handled so traps do not continue their vanilla action.
            if (routine != null)
                return true;

            if (cooldownRemaining > 0f)
                return false;

            routine = StartCoroutine(TimeStopRoutine(origin));
            return true;
        }

        private bool TriggerMultiplayer(HazardKind hazard, Vector2 origin)
        {
            if (cooldownRemaining > 0f)
                return false;

            ModSettings.SyncProgressionLockedSettings();
            return KrokMpBridge.TryAnnounceLocalTrigger(hazard, origin, ModSettings.Duration);
        }

        private void ReceiveMultiplayerTimeStop(MultiplayerTimeStopEvent stopEvent)
        {
            if (stopEvent.Duration <= 0f)
                return;

            if (lastStopIdsByCaster.TryGetValue(stopEvent.CasterClientId, out uint lastStopId) && lastStopId == stopEvent.StopId && multiplayerStops.ContainsKey(stopEvent.CasterClientId))
                return;

            lastStopIdsByCaster[stopEvent.CasterClientId] = stopEvent.StopId;
            // Remote turret shots can be visible on multiple clients. Suppress the same turret
            // locally for a short grace window so one shot does not become several time stops.
            if (stopEvent.Hazard == HazardKind.Turret && stopEvent.CasterClientId != KrokMpBridge.LocalClientId)
                SuppressRemoteTurretTrigger(stopEvent.Origin, stopEvent.Duration);

            float endTime = Time.realtimeSinceStartup + stopEvent.Duration;
            multiplayerStops[stopEvent.CasterClientId] = new ActiveStop(stopEvent.CasterClientId, stopEvent.StopId, endTime, stopEvent.Duration, stopEvent.Origin);
            StartCasterPose(stopEvent);

            if (stopEvent.CasterClientId == KrokMpBridge.LocalClientId)
            {
                ModSettings.SyncProgressionLockedSettings();
                cooldownTotal = ModSettings.Cooldown;
                cooldownRemaining = cooldownTotal;
            }

            freezeDirty = true;
            if (routine == null)
                routine = StartCoroutine(MultiplayerTimeStopRoutine(stopEvent.Origin));
            else
                RebuildMultiplayerDisplay(stopEvent.Origin);
        }

        private IEnumerator TimeStopRoutine(Vector2 origin)
        {
            isMultiplayerStop = false;
            ModSettings.SyncProgressionLockedSettings();
            remainingSeconds = ModSettings.Duration;
            activeTotalSeconds = remainingSeconds;
            cooldownTotal = ModSettings.Cooldown;
            cooldownRemaining = cooldownTotal;
            BeginTimeStop(origin);

            while (remainingSeconds > 0f)
            {
                // Restore every frame so bleeding/pain/unconsciousness cannot worsen, while
                // beneficial medical changes made by the player are still preserved.
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
                if (IsLocalPlayerAllowedDuringStop())
                    playerVitalsSnapshot?.Restore();

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
                // Returning to the main menu or losing the playable body must clear visual
                // effects, cooldown text, frozen objects, and queued actions immediately.
                ClearRunStateIfNeeded();
                TimeStopEffect.ClearImmediate();
                return;
            }

            TemporalPanicButtonPlugin.TryInstallKrokMpCompatibilityPatches();
            KrokMpBridge.Warmup();
            HandleManualTriggerInput();

            if (cooldownRemaining > 0f && routine == null)
                cooldownRemaining -= Time.unscaledDeltaTime;
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

            PlayerCamera camera = PlayerCamera.main;
            if (camera == null)
                return false;

            return IsGameObjectActive(camera.containerMenu) ||
                   IsGameObjectActive(camera.tradeMenu);
        }

        private static bool IsGameObjectActive(GameObject obj)
        {
            return obj != null && obj.activeInHierarchy;
        }

        private void LateUpdate()
        {
            if (routine != null)
            {
                // The mod does not use global timeScale as its main freeze mechanic; keep it
                // normalized so UI, player animation, and medical minigames can keep running.
                Time.timeScale = 1f;
                if (!isMultiplayerStop || IsLocalPlayerAllowedDuringStop())
                    playerVitalsSnapshot?.Restore();
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

            // Capture the world first, then play audiovisual feedback. This avoids a one-frame
            // window where traps can continue updating after the effect begins.
            FreezeWorld();
            TimeStopHazardAudio.PauseSoundCannonAudio();

            Time.timeScale = 1f;
            TimeStopAudio.PlayStart();
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
                // Deferred physical actions resolve after the frozen world has been restored.
                pendingActions.ReleaseQueuedThrows();
                pendingActions.ReplayQueuedShots();
            }

            pendingActions.Clear();
            playerBody = null;
            playerRb = null;
            playerVitalsSnapshot = null;
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
            lastStopIdsByCaster.Clear();
            RemoteTurretSuppressions.Clear();
            playerBody = null;
            playerRb = null;
            playerVitalsSnapshot = null;
            cooldownRemaining = 0f;
            cooldownTotal = 0f;
            remainingSeconds = 0f;
            activeTotalSeconds = 0f;
            isMultiplayerStop = false;
            freezeDirty = false;
            localEmpoweredEffectsStarted = false;
        }

        private static void SuppressRemoteTurretTrigger(Vector2 origin, float duration)
        {
            PruneRemoteTurretSuppressions();
            float endTime = Time.realtimeSinceStartup + Mathf.Max(0.5f, duration) + RemoteTurretReloadGraceSeconds;

            for (int i = 0; i < RemoteTurretSuppressions.Count; i++)
            {
                if ((RemoteTurretSuppressions[i].Origin - origin).sqrMagnitude > RemoteTurretSuppressionRadius * RemoteTurretSuppressionRadius)
                    continue;

                RemoteTurretSuppressions[i] = new RemoteTurretSuppression(origin, Mathf.Max(RemoteTurretSuppressions[i].EndTime, endTime));
                return;
            }

            RemoteTurretSuppressions.Add(new RemoteTurretSuppression(origin, endTime));
        }

        private static bool IsRemoteTurretTriggerSuppressed(Vector2 origin)
        {
            PruneRemoteTurretSuppressions();
            float radiusSqr = RemoteTurretSuppressionRadius * RemoteTurretSuppressionRadius;
            for (int i = 0; i < RemoteTurretSuppressions.Count; i++)
            {
                if ((RemoteTurretSuppressions[i].Origin - origin).sqrMagnitude <= radiusSqr)
                    return true;
            }

            return false;
        }

        private static void PruneRemoteTurretSuppressions()
        {
            if (RemoteTurretSuppressions.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            for (int i = RemoteTurretSuppressions.Count - 1; i >= 0; i--)
            {
                if (RemoteTurretSuppressions[i].EndTime <= now)
                    RemoteTurretSuppressions.RemoveAt(i);
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

            // In multiplayer, every active caster is allowed to move. Everyone else freezes.
            uint clientId = KrokPlayerResolver.GetClientIdForBody(body);
            if (clientId == uint.MaxValue)
                return false;

            return HasActiveStopForClient(clientId);
        }

        private bool IsLocalPlayerAllowedDuringStop()
        {
            if (!isMultiplayerStop)
                return true;

            return HasActiveStopForClient(KrokMpBridge.LocalClientId);
        }

        private bool HasActiveStopForClient(uint clientId)
        {
            PruneExpiredMultiplayerStops();
            return multiplayerStops.ContainsKey(clientId);
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
                multiplayerStops.Remove(expired[i]);

            // Allowed bodies changed, so rebuild frozen rigidbodies/behaviours on the next loop.
            freezeDirty = true;
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
            uint localClientId = KrokMpBridge.LocalClientId;
            if (multiplayerStops.TryGetValue(localClientId, out ActiveStop localStop))
            {
                // The HUD prioritizes the local player's own stop over someone else's stop.
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
            // Re-freeze from a clean snapshot so a player who gains or loses empowerment gets
            // their limbs, Body behaviour, and nearby objects moved to the correct side.
            RestoreWorld();
            FreezeWorld();
            UpdateLocalEmpowermentEffects(GetLocalStopOrigin(), true);
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

        private void StartCasterPose(MultiplayerTimeStopEvent stopEvent)
        {
            Body casterBody = KrokPlayerResolver.GetBodyForClientId(stopEvent.CasterClientId);
            if (casterBody == null)
                return;

            TimeStopPoseVisual.StartPose(casterBody, stopEvent.Origin, Mathf.Min(2f, stopEvent.Duration));
        }

        private Vector2 GetLocalStopOrigin()
        {
            if (isMultiplayerStop && multiplayerStops.TryGetValue(KrokMpBridge.LocalClientId, out ActiveStop stop))
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

            return camera.body.gameObject != null && camera.body.isActiveAndEnabled;
        }

        private readonly struct RemoteTurretSuppression
        {
            public readonly Vector2 Origin;
            public readonly float EndTime;

            public RemoteTurretSuppression(Vector2 origin, float endTime)
            {
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

            public ActiveStop(uint casterClientId, uint stopId, float endTime, float duration, Vector2 origin)
            {
                CasterClientId = casterClientId;
                StopId = stopId;
                EndTime = endTime;
                Duration = duration;
                Origin = origin;
            }
        }
    }

    /// <summary>
    /// Source category for a time-stop trigger. Used by settings, multiplayer payloads, and trap suppression.
    /// </summary>
    internal enum HazardKind
    {
        Mine,
        Turret,
        Manual
    }
}
