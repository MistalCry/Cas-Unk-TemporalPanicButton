using System.Collections.Generic;
using UnityEngine;

namespace TemporalPanicButton
{
    /// <summary>
    /// 直接接入游戏自己的 Settings 列表，而不是使用 BepInEx 配置文件。
    /// 这样设置项会出现在 Settings -> Game 中，也能避免插件启动阶段过早初始化游戏设置系统。
    /// </summary>
    internal static class ModSettings
    {
        public const string EnabledSettingName = "temporalpanicbutton";
        public const string IntelligenceProgressionSettingName = "temporalpanicbuttonintprogression";
        public const string DurationSettingName = "temporalpanicbuttonduration";
        public const string CooldownSettingName = "temporalpanicbuttoncooldown";
        public const string TriggerMinesSettingName = "temporalpanicbuttonmines";
        public const string TriggerTurretsSettingName = "temporalpanicbuttonturrets";
        public const string ManualTriggerSettingName = "temporalpanicbuttonmanual";
        public const string ManualTriggerKeySettingName = "temporalpanicbuttonmanualkey";
        public const string MedicalFocusSettingName = "temporalpanicbuttonmedicalfocus";
        public const string EffectIntensitySettingName = "temporalpanicbuttoneffect";
        public const string VolumeSettingName = "temporalpanicbuttonvolume";

        private const float DefaultDuration = 5f;
        // 旧测试版曾把默认时长写成 7 秒，读到这个旧值时会自动迁移到当前默认值。
        private const float LegacyDefaultDuration = 7f;
        private const float MinDuration = 1f;
        private const float MaxDuration = 20f;
        private const float DefaultCooldown = 15f;
        private const float MinCooldown = 0f;
        private const float MaxCooldown = 300f;
        private const int IntelligenceUnlockLevel = 7;
        private const int IntelligenceMaxLevel = 20;
        private const float UnlockDuration = 4f;
        private const float MaxIntelligenceDuration = 20f;
        private const float UnlockCooldown = 240f;
        private const float MaxIntelligenceCooldown = 120f;
        // 智力成长不是线性增长：前期变化较小，后期提升更明显。
        private const float ProgressionCurvePower = 1.3f;
        private const float DefaultEffectIntensity = 1f;
        private const float MinEffectIntensity = 0f;
        private const float MaxEffectIntensity = 2f;
        private const float DefaultVolume = 0.9f;
        private const float MinVolume = 0f;
        private const float MaxVolume = 1f;
        private const KeyCode DefaultManualTriggerKey = KeyCode.V;

        private static readonly Dictionary<string, string> LocaleFallbacks =
            new Dictionary<string, string>
            {
                { "gameset" + EnabledSettingName, "Temporal Panic Button" },
                { "gameset" + EnabledSettingName + "dsc", "Trigger emergency time stop when mines or turrets catch the player." },
                { "gameset" + IntelligenceProgressionSettingName, "Intelligence Growth Mode" },
                { "gameset" + IntelligenceProgressionSettingName + "dsc", "Scale time stop duration and cooldown from the player's Intelligence level." },
                { "gameset" + DurationSettingName, "Time Stop Duration" },
                { "gameset" + DurationSettingName + "dsc", "How long the emergency time stop lasts. Locked while Intelligence Growth Mode is enabled." },
                { "gameset" + CooldownSettingName, "Time Stop Cooldown" },
                { "gameset" + CooldownSettingName + "dsc", "Minimum time before another emergency time stop can trigger. Locked while Intelligence Growth Mode is enabled." },
                { "gameset" + TriggerMinesSettingName, "Trigger On Mines" },
                { "gameset" + TriggerMinesSettingName + "dsc", "Start time stop when the player steps on a mine." },
                { "gameset" + TriggerTurretsSettingName, "Trigger On Turrets" },
                { "gameset" + TriggerTurretsSettingName + "dsc", "Start time stop when a turret or gun mine fires." },
                { "gameset" + ManualTriggerSettingName, "Manual Time Stop" },
                { "gameset" + ManualTriggerSettingName + "dsc", "Allow pressing a key to trigger time stop manually." },
                { "gameset" + ManualTriggerKeySettingName, "Manual Time Stop Key" },
                { "gameset" + ManualTriggerKeySettingName + "dsc", "Key used to trigger time stop manually." },
                { "gameset" + MedicalFocusSettingName, "Steady Medical Hands" },
                { "gameset" + MedicalFocusSettingName + "dsc", "Ignore pain and low-consciousness medical minigame speed penalties during time stop." },
                { "gameset" + EffectIntensitySettingName, "Time Stop Effect Intensity" },
                { "gameset" + EffectIntensitySettingName + "dsc", "Strength of the screen flash and shockwave effect." },
                { "gameset" + VolumeSettingName, "Time Stop Sound Volume" },
                { "gameset" + VolumeSettingName + "dsc", "Volume for optional start and end sound effects." }
            };

        public static bool Enabled => GetBool(EnabledSettingName, true);
        public static bool LocalIntelligenceProgression => GetBool(IntelligenceProgressionSettingName, true);
        public static bool IntelligenceProgression => GetEffectiveIntelligenceProgression();
        public static float Duration => GetDuration();
        public static float Cooldown => IntelligenceProgression ? GetProgressionStats().Cooldown : GetFloat(CooldownSettingName, DefaultCooldown, MinCooldown, MaxCooldown);
        public static int IntelligenceLevel => GetIntelligenceLevel();
        public static bool TimeStopUnlocked => !IntelligenceProgression || IntelligenceLevel >= IntelligenceUnlockLevel;
        public static bool TriggerMines => GetBool(TriggerMinesSettingName, true);
        public static bool TriggerTurrets => GetBool(TriggerTurretsSettingName, true);
        public static bool ManualTrigger => GetBool(ManualTriggerSettingName, true);
        public static KeyCode ManualTriggerKey => GetKey(ManualTriggerKeySettingName, DefaultManualTriggerKey);
        public static bool MedicalFocus => GetBool(MedicalFocusSettingName, true);
        public static float EffectIntensity => GetFloat(EffectIntensitySettingName, DefaultEffectIntensity, MinEffectIntensity, MaxEffectIntensity);
        public static float Volume => GetFloat(VolumeSettingName, DefaultVolume, MinVolume, MaxVolume);

        public static void AddMissingSettings(List<Setting> settings)
        {
            if (settings == null)
                return;

            // Settings.DefaultSettings 可能被多个模组同时修改。
            // 这里只补缺少的选项，避免覆盖其他模组或提前重建设置列表。
            AddBool(settings, EnabledSettingName, true);
            AddBool(settings, IntelligenceProgressionSettingName, true);
            AddFloat(settings, DurationSettingName, DefaultDuration, MinDuration, MaxDuration, FormatDurationSetting);
            AddFloat(settings, CooldownSettingName, DefaultCooldown, MinCooldown, MaxCooldown, FormatCooldownSetting);
            AddBool(settings, TriggerMinesSettingName, true);
            AddBool(settings, TriggerTurretsSettingName, true);
            AddBool(settings, ManualTriggerSettingName, true);
            AddKey(settings, ManualTriggerKeySettingName, DefaultManualTriggerKey);
            AddBool(settings, MedicalFocusSettingName, true);
            AddFloat(settings, EffectIntensitySettingName, DefaultEffectIntensity, MinEffectIntensity, MaxEffectIntensity, FormatMultiplier);
            AddFloat(settings, VolumeSettingName, DefaultVolume, MinVolume, MaxVolume, FormatPercent);
        }

        public static string ProgressionSummary()
        {
            if (!IntelligenceProgression)
                return "Manual";

            ProgressionStats stats = GetProgressionStats();
            return "INT " + IntelligenceLevel + "  " + stats.Duration.ToString("0.#") + "s / " + stats.Cooldown.ToString("0") + "s";
        }

        public static void SyncProgressionLockedSettings()
        {
            SyncProgressionLockedSettings(null);
        }

        public static bool TryGetLocaleFallback(string key, out string value)
        {
            return LocaleFallbacks.TryGetValue(key, out value);
        }

        private static bool GetBool(string name, bool fallback)
        {
            SettingBool setting = Settings.Get<SettingBool>(name);
            return setting == null ? fallback : setting.value;
        }

        private static bool GetEffectiveIntelligenceProgression()
        {
            return GetEffectiveIntelligenceProgression(null);
        }

        private static bool GetEffectiveIntelligenceProgression(List<Setting> settings)
        {
            if (Runtime.KrokMpBridge.TryGetHostIntelligenceProgression(out bool hostValue))
                return hostValue;

            return settings == null
                ? LocalIntelligenceProgression
                : GetBoolFromList(settings, IntelligenceProgressionSettingName, true);
        }

        private static float GetFloat(string name, float fallback, float min, float max)
        {
            SettingFloat setting = Settings.Get<SettingFloat>(name);
            float value = setting == null ? fallback : setting.value;
            return Mathf.Clamp(value, min, max);
        }

        private static float GetDuration()
        {
            if (IntelligenceProgression)
                return GetProgressionStats().Duration;

            SettingFloat setting = Settings.Get<SettingFloat>(DurationSettingName);
            if (setting == null)
                return DefaultDuration;

            if (Mathf.Approximately(setting.value, LegacyDefaultDuration))
                setting.value = DefaultDuration;

            return Mathf.Clamp(setting.value, MinDuration, MaxDuration);
        }

        private static int GetIntelligenceLevel()
        {
            Body body = PlayerCamera.main == null ? null : PlayerCamera.main.body;
            if (body == null || body.skills == null)
                return IntelligenceUnlockLevel;

            return body.skills.INT;
        }

        private static ProgressionStats GetProgressionStats()
        {
            int intelligence = IntelligenceLevel;
            if (intelligence < IntelligenceUnlockLevel)
                return new ProgressionStats(0f, UnlockCooldown);

            // INT 7 解锁时停，INT 20 达到成长上限；中间用曲线插值，而不是简单线性增长。
            float progress = Mathf.InverseLerp(IntelligenceUnlockLevel, IntelligenceMaxLevel, Mathf.Clamp(intelligence, IntelligenceUnlockLevel, IntelligenceMaxLevel));
            float shaped = Mathf.Pow(progress, ProgressionCurvePower);
            float duration = Mathf.Lerp(UnlockDuration, MaxIntelligenceDuration, shaped);
            float cooldown = Mathf.Lerp(UnlockCooldown, MaxIntelligenceCooldown, shaped);
            return new ProgressionStats(duration, cooldown);
        }

        private static KeyCode GetKey(string name, KeyCode fallback)
        {
            SettingKeybind setting = Settings.Get<SettingKeybind>(name);
            return setting == null || setting.value == KeyCode.None ? fallback : setting.value;
        }

        private static void AddBool(List<Setting> settings, string name, bool value, System.Action apply = null)
        {
            SettingBool existing = settings.Find(setting => setting != null && setting.name == name) as SettingBool;
            if (existing != null)
            {
                existing.apply = apply;
                return;
            }

            settings.Add(new SettingBool
            {
                name = name,
                category = Setting.SettingCategory.Game,
                value = value,
                apply = apply
            });
        }

        private static void AddFloat(List<Setting> settings, string name, float value, float min, float max, System.Func<float, string> formatter)
        {
            SettingFloat existing = settings.Find(setting => setting != null && setting.name == name) as SettingFloat;
            if (existing != null)
            {
                existing.min = min;
                existing.max = max;
                existing.formatValue = formatter;
                existing.value = Mathf.Clamp(existing.value, min, max);
                return;
            }

            settings.Add(new SettingFloat
            {
                name = name,
                category = Setting.SettingCategory.Game,
                value = value,
                min = min,
                max = max,
                formatValue = formatter
            });
        }

        private static void AddKey(List<Setting> settings, string name, KeyCode value)
        {
            SettingKeybind existing = settings.Find(setting => setting != null && setting.name == name) as SettingKeybind;
            if (existing != null)
                return;

            settings.Add(new SettingKeybind
            {
                name = name,
                category = Setting.SettingCategory.Game,
                value = value
            });
        }

        private static bool Contains(List<Setting> settings, string name)
        {
            return settings.Exists(setting => setting != null && setting.name == name);
        }

        private static void SyncProgressionLockedSettings(List<Setting> settings)
        {
            bool progression = GetEffectiveIntelligenceProgression(settings);
            ProgressionStats stats = GetProgressionStats();

            // 游戏设置 UI 没有真正的禁用滑条状态，所以成长模式下把计算值写回滑条，
            // 再通过格式化文本显示 LOCKED，模拟“被成长系统接管”的效果。
            SyncFloatSetting(settings, DurationSettingName, progression, stats.Duration, MinDuration, MaxDuration, DefaultDuration);
            SyncFloatSetting(settings, CooldownSettingName, progression, stats.Cooldown, MinCooldown, MaxCooldown, DefaultCooldown);
        }

        private static bool GetBoolFromList(List<Setting> settings, string name, bool fallback)
        {
            SettingBool setting = settings?.Find(item => item != null && item.name == name) as SettingBool;
            return setting == null ? fallback : setting.value;
        }

        private static void SyncFloatSetting(List<Setting> settings, string name, bool locked, float lockedValue, float min, float max, float fallback)
        {
            SettingFloat setting = settings == null
                ? Settings.Get<SettingFloat>(name)
                : settings.Find(item => item != null && item.name == name) as SettingFloat;
            if (setting == null)
                return;

            setting.min = min;
            setting.max = max;

            if (locked)
            {
                setting.value = Mathf.Clamp(lockedValue, min, max);
                return;
            }

            setting.value = Mathf.Clamp(setting.value, min, max);
            if (name == DurationSettingName && Mathf.Approximately(setting.value, LegacyDefaultDuration))
                setting.value = fallback;
        }

        private static string FormatSeconds(float value)
        {
            return value.ToString("0.#") + " s";
        }

        private static string FormatDurationSetting(float value)
        {
            if (!IntelligenceProgression)
                return FormatSeconds(value);

            return "LOCKED " + GetProgressionStats().Duration.ToString("0.#") + " s";
        }

        private static string FormatCooldownSetting(float value)
        {
            if (!IntelligenceProgression)
                return FormatSeconds(value);

            return "LOCKED " + GetProgressionStats().Cooldown.ToString("0") + " s";
        }

        private static string FormatMultiplier(float value)
        {
            return value.ToString("0.##") + "x";
        }

        private static string FormatPercent(float value)
        {
            return (value * 100f).ToString("0") + "%";
        }

        /// <summary>
        /// 当前最终生效的时停时长与冷却；来源可能是手动设置，也可能是智力成长计算。
        /// </summary>
        public readonly struct ProgressionStats
        {
            public readonly float Duration;
            public readonly float Cooldown;

            public ProgressionStats(float duration, float cooldown)
            {
                Duration = duration;
                Cooldown = cooldown;
            }
        }
    }
}
