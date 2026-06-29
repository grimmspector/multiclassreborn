using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#nullable disable

namespace multiclassreborn
{
    // Public class-query helpers for optional integrations with other mods.
    public static class MulticlassCompatibility
    {
        private const string StateTreeCode = "multiclassreborn";
        private const string ExtraClassesKey = "extraClasses";
        private const string MainClassKey = "characterClass";
        private const string NoClassCode = "none";

        // Returns whether the player has the class as either their main or an extra class.
        public static bool HasClass(EntityPlayer player, string classCode)
        {
            if (player == null || string.IsNullOrWhiteSpace(classCode)) return false;

            string normalizedCode = NormalizeClassCode(classCode);
            return GetAllClassCodes(player).Contains(normalizedCode, StringComparer.OrdinalIgnoreCase);
        }

        // Returns whether the class is one of the player's learned extra classes.
        public static bool IsExtraClass(EntityPlayer player, string classCode)
        {
            if (player == null || string.IsNullOrWhiteSpace(classCode)) return false;

            string normalizedCode = NormalizeClassCode(classCode);
            return GetExtraClassCodes(player).Contains(normalizedCode, StringComparer.OrdinalIgnoreCase);
        }

        // Returns the main class followed by all distinct learned extra classes.
        public static IReadOnlyCollection<string> GetAllClassCodes(EntityPlayer player)
        {
            if (player == null) return Array.Empty<string>();

            List<string> classCodes = new List<string>();
            string mainClassCode = NormalizeClassCode(player.WatchedAttributes.GetString(MainClassKey, NoClassCode));

            if (!string.IsNullOrWhiteSpace(mainClassCode) && !mainClassCode.Equals(NoClassCode, StringComparison.OrdinalIgnoreCase))
            {
                classCodes.Add(mainClassCode);
            }

            classCodes.AddRange(GetExtraClassCodes(player));

            return classCodes
                .Where(classCode => !string.IsNullOrWhiteSpace(classCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Reads a snapshot of the extra-class list without exposing mutable player state.
        public static IReadOnlyCollection<string> GetExtraClassCodes(EntityPlayer player)
        {
            if (player == null) return Array.Empty<string>();

            ITreeAttribute stateTree = player.WatchedAttributes.GetTreeAttribute(StateTreeCode);
            StringArrayAttribute extraClasses = stateTree?[ExtraClassesKey] as StringArrayAttribute;

            return (extraClasses?.value ?? Array.Empty<string>())
                .Select(NormalizeClassCode)
                .Where(classCode => !string.IsNullOrWhiteSpace(classCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Uses the same normalization rules as class selection and commands.
        private static string NormalizeClassCode(string classCode)
        {
            return (classCode ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
