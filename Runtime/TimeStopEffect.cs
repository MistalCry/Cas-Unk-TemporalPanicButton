using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 时停开始、脉冲和结束时的屏幕特效。
    /// 所有 UI 图像都在运行时生成，避免插件额外携带贴图资源。
    /// </summary>
    internal sealed class TimeStopEffect : MonoBehaviour
    {
        private static TimeStopEffect instance;

        private Image blueWash;
        private Image flash;
        private Image ring;
        private Coroutine transitionRoutine;
        private Coroutine ringRoutine;
        private float activeIntensity;

        public static void Begin(Vector2 worldOrigin, float intensity)
        {
            if (TemporalPanicButtonPlugin.Instance == null || intensity <= 0f)
                return;

            EnsureInstance();
            instance.activeIntensity = Mathf.Clamp(intensity, 0f, 2f);
            instance.StartBlueShift();
            instance.PlayRing(worldOrigin, new Color(0.22f, 0.78f, 1f, 1f), 0.85f, false);
        }

        public static void Pulse(Vector2 worldOrigin, float intensity)
        {
            if (TemporalPanicButtonPlugin.Instance == null || intensity <= 0f)
                return;

            EnsureInstance();
            instance.PlayRing(worldOrigin, new Color(0.45f, 0.9f, 1f, 1f), Mathf.Clamp(intensity, 0f, 2f) * 0.55f, false);
        }

        public static void End(Vector2 worldOrigin, float intensity)
        {
            if (instance == null)
                return;

            instance.activeIntensity = Mathf.Clamp(intensity, 0f, 2f);
            instance.StartResumeFlash();
            instance.PlayRing(worldOrigin, new Color(1f, 0.82f, 0.32f, 1f), instance.activeIntensity, true);
        }

        public static void EndAtScreenCenter(float intensity)
        {
            if (instance == null)
                return;

            instance.activeIntensity = Mathf.Clamp(intensity, 0f, 2f);
            instance.StartResumeFlash();
            instance.PlayRingAtScreenPosition(
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                new Color(1f, 0.82f, 0.32f, 1f),
                instance.activeIntensity,
                true);
        }

        public static void ClearImmediate()
        {
            if (instance == null)
                return;

            instance.StopAllCoroutines();
            instance.transitionRoutine = null;
            instance.ringRoutine = null;

            if (instance.blueWash != null)
                instance.blueWash.color = Color.clear;

            if (instance.flash != null)
                instance.flash.color = Color.clear;

            if (instance.ring != null)
                instance.ring.color = Color.clear;
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject host = new GameObject("Temporal Panic Button Effect");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<TimeStopEffect>();
            instance.BuildCanvas();
        }

        private void BuildCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            blueWash = CreateImage("Blue Shift", transform, CreateSolidSprite());
            StretchToScreen(blueWash.rectTransform);
            blueWash.color = Color.clear;

            flash = CreateImage("Resume Flash", transform, CreateSolidSprite());
            StretchToScreen(flash.rectTransform);
            flash.color = Color.clear;

            ring = CreateImage("Shock Ring", transform, CreateRingSprite());
            RectTransform ringRect = ring.rectTransform;
            ringRect.anchorMin = new Vector2(0.5f, 0.5f);
            ringRect.anchorMax = new Vector2(0.5f, 0.5f);
            ringRect.sizeDelta = new Vector2(96f, 96f);
            ring.color = Color.clear;
        }

        private void StartBlueShift()
        {
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);

            transitionRoutine = StartCoroutine(BlueShiftRoutine());
        }

        private void StartResumeFlash()
        {
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);

            transitionRoutine = StartCoroutine(ResumeFlashRoutine());
        }

        private IEnumerator BlueShiftRoutine()
        {
            float time = 0f;
            float duration = 0.35f;
            Color wash = new Color(0.08f, 0.32f, 0.68f, 0f);
            Color coldFlash = new Color(0.55f, 0.92f, 1f, 0f);

            while (time < duration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / duration);
                wash.a = Mathf.Lerp(0f, 0.28f * activeIntensity, Smooth(t));
                coldFlash.a = Mathf.Lerp(0.5f * activeIntensity, 0f, t);
                blueWash.color = wash;
                flash.color = coldFlash;
                yield return null;
            }

            wash.a = 0.28f * activeIntensity;
            blueWash.color = wash;
            flash.color = Color.clear;
            transitionRoutine = null;
        }

        private IEnumerator ResumeFlashRoutine()
        {
            float time = 0f;
            float duration = 0.55f;
            Color washStart = blueWash.color;
            Color warmFlash = new Color(1f, 0.85f, 0.42f, 0f);

            while (time < duration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / duration);
                Color wash = washStart;
                wash.a = Mathf.Lerp(washStart.a, 0f, Smooth(t));
                warmFlash.a = Mathf.Sin(t * Mathf.PI) * 0.45f * activeIntensity;
                blueWash.color = wash;
                flash.color = warmFlash;
                yield return null;
            }

            blueWash.color = Color.clear;
            flash.color = Color.clear;
            transitionRoutine = null;
        }

        private void PlayRing(Vector2 worldOrigin, Color color, float intensity, bool reverse)
        {
            if (ringRoutine != null)
                StopCoroutine(ringRoutine);

            Vector2 screenPosition = Camera.main == null
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : (Vector2)Camera.main.WorldToScreenPoint(worldOrigin);

            ring.rectTransform.position = screenPosition;
            ringRoutine = StartCoroutine(RingRoutine(color, Mathf.Clamp(intensity, 0f, 2f), reverse));
        }

        private void PlayRingAtScreenPosition(Vector2 screenPosition, Color color, float intensity, bool reverse)
        {
            if (ringRoutine != null)
                StopCoroutine(ringRoutine);

            ring.rectTransform.position = screenPosition;
            ringRoutine = StartCoroutine(RingRoutine(color, Mathf.Clamp(intensity, 0f, 2f), reverse));
        }

        private IEnumerator RingRoutine(Color color, float intensity, bool reverse)
        {
            float time = 0f;
            float duration = reverse ? 0.6f : 0.8f;
            float maxSize = Mathf.Max(Screen.width, Screen.height) * 1.55f;
            float start = reverse ? maxSize * 0.85f : 72f;
            float end = reverse ? 48f : maxSize;

            while (time < duration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / duration);
                float eased = reverse ? 1f - Mathf.Pow(1f - t, 3f) : Smooth(t);
                float size = Mathf.Lerp(start, end, eased);
                color.a = Mathf.Sin(t * Mathf.PI) * 0.72f * intensity;

                ring.color = color;
                ring.rectTransform.sizeDelta = new Vector2(size, size);
                yield return null;
            }

            ring.color = Color.clear;
            ringRoutine = null;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            Image image = obj.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = sprite;
            return image;
        }

        private static void StretchToScreen(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Sprite CreateSolidSprite()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateRingSprite()
        {
            // 程序生成圆环，既不需要外置贴图，也能跟随 UI 分辨率干净缩放。
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.42f;
            float thickness = size * 0.045f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Abs(Vector2.Distance(new Vector2(x, y), center) - radius);
                    float alpha = Mathf.Clamp01(1f - distance / thickness);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
