using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 加载用户放在 TemporalPanicButtonAssets 里的开始/结束音效。
    /// 音效文件完全可选，缺失时不报错，方便发布包不内置外部声音资源。
    /// </summary>
    internal static class TimeStopAudio
    {
        private static readonly string[] StartFileNames = { "timestop_start.wav", "timestop_start.ogg" };
        private static readonly string[] EndFileNames = { "timestop_end.wav", "timestop_end.ogg" };

        private static AudioClip startClip;
        private static AudioClip endClip;
        private static bool loadingStarted;

        public static void Preload()
        {
            EnsureLoadingStarted();
        }

        public static void PlayStart()
        {
            EnsureLoadingStarted();
            Play(startClip);
        }

        public static void PlayEnd()
        {
            EnsureLoadingStarted();
            Play(endClip);
        }

        private static void EnsureLoadingStarted()
        {
            if (loadingStarted || TemporalPanicButtonPlugin.Instance == null)
                return;

            loadingStarted = true;
            TemporalPanicButtonPlugin.Instance.StartCoroutine(LoadClips());
        }

        private static IEnumerator LoadClips()
        {
            string startPath = FindFirstExisting(StartFileNames);
            string endPath = FindFirstExisting(EndFileNames);

            if (!string.IsNullOrEmpty(startPath))
                yield return LoadClip(startPath, clip => startClip = clip);

            if (!string.IsNullOrEmpty(endPath))
                yield return LoadClip(endPath, clip => endClip = clip);
        }

        private static string FindFirstExisting(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(TemporalPanicButtonPlugin.AssetDirectory, names[i]);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static IEnumerator LoadClip(string path, System.Action<AudioClip> assign)
        {
            // UnityWebRequestMultimedia 在 BepInEx 可用的 Unity 版本中读本地 wav/ogg 最稳。
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace("\\", "/"), GetAudioType(path)))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                    yield break;

                assign(DownloadHandlerAudioClip.GetContent(request));
            }
        }

        private static AudioType GetAudioType(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".ogg" ? AudioType.OGGVORBIS : AudioType.WAV;
        }

        private static void Play(AudioClip clip)
        {
            if (clip == null)
                return;

            AudioSource source = Sound.Play(clip, Vector2.zero, true, false, null, ModSettings.Volume, 1f, true, true);
            if (source != null)
                source.ignoreListenerPause = true;
        }
    }
}
