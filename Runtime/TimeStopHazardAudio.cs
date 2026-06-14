using System.Collections.Generic;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Tracks and pauses Sound Cannon audio while its behaviour update is frozen.
    /// Without this, a charge sound can finish during time stop and then be missing after resume.
    /// </summary>
    internal static class TimeStopHazardAudio
    {
        private static readonly HashSet<string> SoundCannonClips = new HashSet<string>
        {
            "sonarcharge",
            "sonarmegaouchfixed",
            "sonarouch",
            "sonarouchblocked"
        };

        private static readonly HashSet<string> SharedSoundCannonClips = new HashSet<string>
        {
            "tinnitus"
        };

        private static readonly List<AudioSource> trackedSoundCannonSources = new List<AudioSource>();
        private static readonly HashSet<AudioSource> pausedSoundCannonSources = new HashSet<AudioSource>();
        private static readonly HashSet<PlayedSound> pausedPlayedSounds = new HashSet<PlayedSound>();

        public static void RegisterSoundCannonAudio(string clipName, AudioSource source)
        {
            if (source == null || string.IsNullOrEmpty(clipName) || !IsSoundCannonAudio(clipName, source))
                return;

            CleanupDestroyedSources();

            if (!trackedSoundCannonSources.Contains(source))
                trackedSoundCannonSources.Add(source);

            if (TimeStopController.IsActive)
                PauseSoundCannonAudio();
        }

        public static void PauseSoundCannonAudio()
        {
            CleanupDestroyedSources();

            for (int i = 0; i < trackedSoundCannonSources.Count; i++)
            {
                AudioSource source = trackedSoundCannonSources[i];
                if (source == null || !source.isPlaying || pausedSoundCannonSources.Contains(source))
                    continue;

                PlayedSound playedSound = source.GetComponent<PlayedSound>();
                if (playedSound != null && playedSound.enabled)
                {
                    playedSound.enabled = false;
                    pausedPlayedSounds.Add(playedSound);
                }

                source.Pause();
                pausedSoundCannonSources.Add(source);
            }
        }

        public static void ResumeSoundCannonAudio()
        {
            foreach (AudioSource source in pausedSoundCannonSources)
            {
                if (source != null)
                    source.UnPause();
            }

            pausedSoundCannonSources.Clear();

            foreach (PlayedSound playedSound in pausedPlayedSounds)
            {
                if (playedSound != null)
                    playedSound.enabled = true;
            }

            pausedPlayedSounds.Clear();
            CleanupDestroyedSources();
        }

        private static void CleanupDestroyedSources()
        {
            for (int i = trackedSoundCannonSources.Count - 1; i >= 0; i--)
            {
                AudioSource source = trackedSoundCannonSources[i];
                if (source != null)
                    continue;

                trackedSoundCannonSources.RemoveAt(i);
            }
        }

        private static bool IsSoundCannonAudio(string clipName, AudioSource source)
        {
            if (SoundCannonClips.Contains(clipName))
                return true;

            // "tinnitus" is shared by other hazards, so only treat it as Sound Cannon audio
            // when the source is spatially close to an actual SoundCannon.
            return SharedSoundCannonClips.Contains(clipName) && IsNearSoundCannon(source.transform.position);
        }

        private static bool IsNearSoundCannon(Vector3 position)
        {
            SoundCannon[] cannons = Object.FindObjectsOfType<SoundCannon>();
            for (int i = 0; i < cannons.Length; i++)
            {
                SoundCannon cannon = cannons[i];
                if (cannon != null && Vector2.Distance(cannon.transform.position, position) <= 2f)
                    return true;
            }

            return false;
        }
    }
}
