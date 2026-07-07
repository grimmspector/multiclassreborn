using System;
using System.Globalization;
using Vintagestory.API.Config;

#nullable disable

namespace multiclassreborn
{
    // Shared stat-scaling rules for server application and client previews.
    internal static class ExtraClassStatScaling
    {
        // Some trait stats are discrete unlocks, thresholds, or tier values.
        internal static bool ShouldScaleStat(string statCode)
        {
            if (string.IsNullOrWhiteSpace(statCode)) return true;
            if (statCode.Equals("temporalGearTLRepairCost", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.Equals("dodgeGuaranteedCooldown", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.Equals("fallDamageThreshold", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.Equals("armorWalkSpeedAffectedness", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.Equals("armorManipulationSpeedAffectedness", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.StartsWith("can", StringComparison.OrdinalIgnoreCase)) return false;
            if (statCode.IndexOf("DamageTierBonus", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return true;
        }

        // Returns the value an extra-class stat will actually apply.
        internal static double GetAppliedValue(string statCode, double rawValue, float scale, bool isApplied)
        {
            if (!isApplied) return 0;
            return ShouldScaleStat(statCode) ? rawValue * scale : rawValue;
        }

        // Formats a compact signed value for generic stat display.
        internal static string FormatSignedValue(double value, bool asPercent)
        {
            string sign = value > 0 ? "+" : "";
            string suffix = asPercent ? "%" : "";
            double displayValue = asPercent ? value * 100 : value;

            return sign + displayValue.ToString("0.###", CultureInfo.InvariantCulture) + suffix;
        }

        // Returns the localized stat label used by Combat Overhaul-style stat lists.
        internal static string GetStatLabel(string statCode)
        {
            if (string.IsNullOrWhiteSpace(statCode)) return "";

            string label = Lang.GetIfExists("stat-" + statCode);
            if (!string.IsNullOrWhiteSpace(label)) return label;

            label = Lang.GetIfExists("combatoverhaul:stat-" + statCode);
            if (!string.IsNullOrWhiteSpace(label)) return label;

            return statCode;
        }
    }
}
