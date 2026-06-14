using System.Collections.Generic;
using System.Reflection;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Protects the player's body and limb vitals while time is stopped.
    /// Values are restored only when they get worse; improvements from medical items are kept.
    /// </summary>
    internal sealed class PlayerVitalsSnapshot
    {
        // Body-wide vitals that should not degrade during time stop.
        // The helper name describes which direction is considered beneficial.
        private static readonly VitalsField[] BodyFields =
        {
            BetterHigher("bloodOxygen"),
            BetterHigher("bloodVolume"),
            BetterHigher("bloodPressure"),
            BetterLower("fibrillationProgress"),
            BetterHigher("happiness"),
            BetterHigher("hunger"),
            BetterHigher("thirst"),
            BetterHigher("stamina"),
            BetterHigher("energy"),
            BetterHigher("brainHealth"),
            BetterHigher("consciousness"),
            BetterLower("shock"),
            BetterLower("averagePain"),
            BetterLower("totalBleedSpeed"),
            BetterLower("sicknessAmount"),
            BetterLower("septicShock"),
            BetterLower("hearingLoss"),
            BetterLower("internalBleeding"),
            BetterLower("hemothorax"),
            BetterLower("painShock"),
            BetterLower("traumaAmount"),
            BetterLower("radiationSickness"),
            BetterLower("wetness"),
            BetterHigher("immunity"),
            BetterLower("strokeAmount"),
            BetterLower("venomTotal"),
            BetterLower("venomCurrent"),
            BetterLower("horrifiedLevel"),
            BetterHigher("focusedLevel"),
            BetterLower("dirtyness"),
            BetterHigher("clawHealth"),
            BetterHigher("<bleedClottingSpeed>k__BackingField"),
            BetterLower("<bleedingSpeedMultiplier>k__BackingField"),
            BetterHigher("<thirstBloodPressure>k__BackingField")
        };

        // Limb vitals are captured separately because treatment usually targets a specific limb.
        private static readonly VitalsField[] LimbFields =
        {
            BetterHigher("skinHealth"),
            BetterHigher("muscleHealth"),
            BetterLower("infectionAmount"),
            BetterLower("pain"),
            BetterLower("bleedAmount"),
            BetterLower("furBloodAmount"),
            BetterHigher("disinfectionTime"),
            BetterFalse("dislocated"),
            BetterFalse("broken"),
            BetterFalse("infected"),
            BetterFalse("strokeAffected")
        };

        private readonly Body body;
        private readonly object[] bodyValues;
        private readonly LimbSnapshot[] limbSnapshots;

        private PlayerVitalsSnapshot(Body body)
        {
            this.body = body;
            bodyValues = CaptureFields(body, BodyFields);
            limbSnapshots = CaptureLimbs(body);
        }

        public static PlayerVitalsSnapshot Capture(Body body)
        {
            return body == null ? null : new PlayerVitalsSnapshot(body);
        }

        public void Restore()
        {
            if (body == null)
                return;

            ProtectFields(body, BodyFields, bodyValues);

            for (int i = 0; i < limbSnapshots.Length; i++)
                limbSnapshots[i].Restore();
        }

        private static LimbSnapshot[] CaptureLimbs(Body body)
        {
            if (body == null || body.limbs == null)
                return new LimbSnapshot[0];

            List<LimbSnapshot> snapshots = new List<LimbSnapshot>();
            for (int i = 0; i < body.limbs.Length; i++)
            {
                Limb limb = body.limbs[i];
                if (limb != null)
                    snapshots.Add(new LimbSnapshot(limb));
            }

            return snapshots.ToArray();
        }

        private static VitalsField BetterHigher(string name)
        {
            return new VitalsField(name, true);
        }

        private static VitalsField BetterLower(string name)
        {
            return new VitalsField(name, false);
        }

        private static VitalsField BetterTrue(string name)
        {
            return new VitalsField(name, true);
        }

        private static VitalsField BetterFalse(string name)
        {
            return new VitalsField(name, false);
        }

        private static object[] CaptureFields(object target, VitalsField[] fields)
        {
            object[] values = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                values[i] = fields[i].GetValue(target);

            return values;
        }

        private static void ProtectFields(object target, VitalsField[] fields, object[] protectedValues)
        {
            for (int i = 0; i < fields.Length; i++)
                fields[i].Protect(target, protectedValues, i);
        }

        private sealed class VitalsField
        {
            private readonly FieldInfo field;
            private readonly bool higherOrTrueIsBetter;

            public VitalsField(string name, bool higherOrTrueIsBetter)
            {
                field = typeof(Body).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                        typeof(Limb).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                this.higherOrTrueIsBetter = higherOrTrueIsBetter;
            }

            public object GetValue(object target)
            {
                if (field == null || target == null)
                    return null;

                try
                {
                    return field.GetValue(target);
                }
                catch
                {
                    return null;
                }
            }

            public void Protect(object target, object[] protectedValues, int index)
            {
                if (field == null || target == null || protectedValues == null || index >= protectedValues.Length)
                    return;

                object currentObject = GetValue(target);
                object protectedObject = protectedValues[index];

                if (currentObject is float current && protectedObject is float protectedFloat)
                {
                    ProtectFloat(target, protectedValues, index, current, protectedFloat);
                    return;
                }

                if (currentObject is bool currentBool && protectedObject is bool protectedBool)
                    ProtectBool(target, protectedValues, index, currentBool, protectedBool);
            }

            private void ProtectFloat(object target, object[] protectedValues, int index, float current, float protectedValue)
            {
                float nextProtectedValue = protectedValue;
                // Let healing, hydration, bandaging, and other improvements advance the snapshot.
                // Revert only harmful drift caused by bleeding, pain, shock, etc.
                bool improved = higherOrTrueIsBetter ? current > protectedValue : current < protectedValue;
                bool worsened = higherOrTrueIsBetter ? current < protectedValue : current > protectedValue;

                if (improved)
                    nextProtectedValue = current;
                else if (worsened)
                    SetValue(target, protectedValue);

                protectedValues[index] = nextProtectedValue;
            }

            private void ProtectBool(object target, object[] protectedValues, int index, bool current, bool protectedValue)
            {
                bool nextProtectedValue = protectedValue;
                bool improved = higherOrTrueIsBetter ? current && !protectedValue : !current && protectedValue;
                bool worsened = higherOrTrueIsBetter ? !current && protectedValue : current && !protectedValue;

                if (improved)
                    nextProtectedValue = current;
                else if (worsened)
                    SetValue(target, protectedValue);

                protectedValues[index] = nextProtectedValue;
            }

            private void SetValue(object target, float value)
            {
                try
                {
                    field.SetValue(target, value);
                }
                catch
                {
                }
            }

            private void SetValue(object target, bool value)
            {
                try
                {
                    field.SetValue(target, value);
                }
                catch
                {
                }
            }
        }

        private readonly struct LimbSnapshot
        {
            private readonly Limb limb;
            private readonly object[] values;

            public LimbSnapshot(Limb limb)
            {
                this.limb = limb;
                values = CaptureFields(limb, LimbFields);
            }

            public void Restore()
            {
                if (limb == null)
                    return;

                ProtectFields(limb, LimbFields, values);
            }
        }
    }
}
