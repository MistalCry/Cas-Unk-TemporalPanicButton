using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Short visual flourish at the start of a time stop: force the attack pose and move Observer
    /// behind the caster. It snapshots touched fields and restores them each frame after rendering.
    /// </summary>
    internal static class TimeStopPoseVisual
    {
        private const float FadeInSeconds = 0.18f;
        private const float FadeOutSeconds = 0.25f;

        private static Body activeBody;
        private static Observer activeObserver;
        private static Vector3 observerOriginalPosition;
        private static Quaternion observerOriginalRotation;
        private static bool hasObserverSnapshot;
        private static bool hasVisualSnapshot;
        private static float originalAttackRot;
        private static float originalArmOffset;
        private static float originalOverrideLookTime;
        private static Vector2 originalOverrideLookPos;
        private static Vector2 originalVisualBodyOffset;
        private static Vector3 originalTargetLookPos;
        private static float startsAt;
        private static float endsAt;

        public static void StartPose(Body body, Vector2 hazardPosition, float durationSeconds)
        {
            if (body == null || durationSeconds <= 0f)
                return;

            if (activeBody != null && activeBody != body)
                StopPose();

            activeBody = body;
            activeObserver = Observer.main;
            startsAt = Time.unscaledTime;
            endsAt = startsAt + durationSeconds;

            SnapshotObserver();
        }

        public static void StopPose()
        {
            RestoreVisualSnapshot(activeBody);
            RestoreObserver();
            activeBody = null;
            activeObserver = null;
            startsAt = 0f;
            endsAt = 0f;
        }

        public static void StopPose(Body body)
        {
            if (body == null || body != activeBody)
                return;

            StopPose();
        }

        public static void PrepareAttackPose(Body body)
        {
            if (body == null || body != activeBody)
                return;

            float now = Time.unscaledTime;
            if (now >= endsAt)
            {
                StopPose();
                return;
            }

            float weight = GetWeight(now);
            if (weight <= 0.001f)
                return;

            SnapshotVisuals(body);

            // Reuse the game's own attack/look variables instead of posing limbs manually.
            // This keeps animation much less fragile across character shapes.
            Vector2 center = GetBodyCenter(body);
            Vector2 forward = body.isRight ? Vector2.right : Vector2.left;
            Vector2 lookPos = center + forward * 4f + Vector2.up * 0.15f;
            float sign = body.isRight ? 1f : -1f;

            body.overrideLookTime = Mathf.Max(body.overrideLookTime, 0.08f);
            body.overrideLookPos = lookPos;
            body.targetLookPos = lookPos;
            body.attackRot = originalAttackRot - 11.5f * sign * weight;
            body.armOffset = Mathf.Lerp(originalArmOffset, 0f, weight);
            body.visualBodyOffset = originalVisualBodyOffset + forward * (0.35f * weight);

            if (body.armsAnimator != null)
            {
                body.armsAnimator.Play("ArmsSwing", -1, 0.42f);
                body.armsAnimator.Update(0f);
            }
        }

        public static void FinishAttackPose(Body body)
        {
            if (body == null || body != activeBody)
                return;

            RestoreVisualSnapshot(body);
        }

        public static void ApplyObserver()
        {
            if (activeBody == null || activeObserver == null)
                return;

            float now = Time.unscaledTime;
            if (now >= endsAt)
            {
                StopPose();
                return;
            }

            float weight = GetWeight(now);
            if (weight <= 0.001f)
                return;

            Vector2 bodyCenter = GetBodyCenter(activeBody);
            Vector2 forward = activeBody.isRight ? Vector2.right : Vector2.left;
            // Keep Observer far enough away to read as a stand-like silhouette instead of
            // covering the player body.
            Vector2 standPosition = bodyCenter - forward * 7.2f + Vector2.up * 0.3f;

            Transform observerTransform = activeObserver.transform;
            observerTransform.position = Vector3.Lerp(observerTransform.position, new Vector3(standPosition.x, standPosition.y, observerTransform.position.z), 0.9f * weight);

            Vector2 direction = bodyCenter - (Vector2)observerTransform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                observerTransform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private static float GetWeight(float now)
        {
            float fadeIn = Mathf.Clamp01((now - startsAt) / FadeInSeconds);
            float fadeOut = Mathf.Clamp01((endsAt - now) / FadeOutSeconds);
            return Mathf.Min(fadeIn, fadeOut);
        }

        private static Vector2 GetBodyCenter(Body body)
        {
            if (body != null && body.limbs != null && body.limbs.Length > 1)
            {
                Limb torso = body.limbs[1];
                if (torso != null && !torso.dismembered)
                    return torso.transform.position;
            }

            return body == null ? Vector2.zero : (Vector2)body.transform.position;
        }

        private static void SnapshotObserver()
        {
            hasObserverSnapshot = false;
            if (activeObserver == null)
                return;

            observerOriginalPosition = activeObserver.transform.position;
            observerOriginalRotation = activeObserver.transform.rotation;
            hasObserverSnapshot = true;
        }

        private static void RestoreObserver()
        {
            if (!hasObserverSnapshot || activeObserver == null)
                return;

            activeObserver.transform.position = observerOriginalPosition;
            activeObserver.transform.rotation = observerOriginalRotation;
            hasObserverSnapshot = false;
        }

        private static void SnapshotVisuals(Body body)
        {
            if (hasVisualSnapshot || body == null)
                return;

            originalAttackRot = body.attackRot;
            originalArmOffset = body.armOffset;
            originalOverrideLookTime = body.overrideLookTime;
            originalOverrideLookPos = body.overrideLookPos;
            originalVisualBodyOffset = body.visualBodyOffset;
            originalTargetLookPos = body.targetLookPos;
            hasVisualSnapshot = true;
        }

        private static void RestoreVisualSnapshot(Body body)
        {
            if (!hasVisualSnapshot || body == null)
                return;

            body.attackRot = originalAttackRot;
            body.armOffset = originalArmOffset;
            body.overrideLookTime = originalOverrideLookTime;
            body.overrideLookPos = originalOverrideLookPos;
            body.visualBodyOffset = originalVisualBodyOffset;
            body.targetLookPos = originalTargetLookPos;
            hasVisualSnapshot = false;
        }
    }
}
