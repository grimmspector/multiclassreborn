using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace multiclassreborn.systems
{
    // Server config with legacy key names preserved for old multiclass worlds.
    public class RebornClassConfig
    {
        [JsonProperty("AllowStats")]
        public bool AllowStatBonuses = true;

        [JsonProperty("AllowRecipes")]
        public bool AllowRecipeTraits = true;

        // Enables craftable glyphstone recipes when desired on the server.
        [JsonProperty("EnableGlyphstoneRecipes")]
        public bool EnableGlyphstoneRecipes = false;

        // Allows Aptitude Glyphstones to point at specific classes.
        public bool EnableClassBoundGlyphstones;

        // Removes the generic slot-token path while leaving bound glyphstones available.
        public bool DisableGenericGlyphstones;

        // Multiplies stat changes from extra classes before applying them.
        [JsonProperty("SecondaryScale")]
        public float ExtraClassScale = 0.8f;

        // Keeps only the strongest positive trait bonus per affected stat.
        [JsonProperty("OnlyApplyBestPositiveTraitBonus")]
        public bool OnlyApplyBestPositiveTraitBonus;

        // Keeps only the harshest negative trait penalty per affected stat.
        [JsonProperty("OnlyApplyWorstNegativeTraitPenalty")]
        public bool OnlyApplyWorstNegativeTraitPenalty;

        // Allows forgetting the main class and returning to Commoner.
        [JsonProperty("AllowForgettingBaseClass")]
        public bool AllowForgettingBaseClass;

        // Allows Commoners to choose a new main class without a glyphstone.
        [JsonProperty("AllowCommonersChooseBaseClass")]
        public bool AllowCommonersChooseBaseClass;

        public int MaxExtraClasses = 3;

        // Removes learned extra classes above MaxExtraClasses after that value changes.
        public bool DropExtraClassesOverMax;

        [JsonProperty("RequireTokens")]
        public bool RequireGlyphs;

        // Makes class forgetting free while glyphstones are required for new slots.
        public bool RetrainFree;

        // Aptitude Glyphstones granted on first join when RequireTokens is true.
        public int StartingAptitudeTokens;

        public bool RetrainFreeApplies => RequireGlyphs && RetrainFree;

        private const int MinMaxExtraClasses = 0;
        private const int MaxMaxExtraClasses = 32;
        private const int MinStartingAptitudeTokens = 0;
        private const int MaxStartingAptitudeTokens = 64;
        private const float MinExtraClassScale = 0f;
        private const float MaxExtraClassScale = 3f;
        private const string ConfigFileName = "multiclassreborn.json";
        private const string LegacyConfigFileName = "multiclass.json";
        private static readonly string[] CurrentConfigKeys =
        {
            "AllowStats",
            "AllowRecipes",
            "EnableGlyphstoneRecipes",
            nameof(EnableClassBoundGlyphstones),
            nameof(DisableGenericGlyphstones),
            "SecondaryScale",
            nameof(OnlyApplyBestPositiveTraitBonus),
            nameof(OnlyApplyWorstNegativeTraitPenalty),
            "AllowForgettingBaseClass",
            "AllowCommonersChooseBaseClass",
            nameof(MaxExtraClasses),
            nameof(DropExtraClassesOverMax),
            "RequireTokens",
            nameof(RetrainFree),
            nameof(StartingAptitudeTokens)
        };

        // Keeps older configs usable while upgrading them to the current template.
        public static RebornClassConfig Load(ICoreServerAPI sapi)
        {
            RebornClassConfig config = null;
            string configDirectory = sapi.GetOrCreateDataPath("ModConfig");
            string configPath = Path.Combine(configDirectory, ConfigFileName);
            string legacyConfigPath = Path.Combine(configDirectory, LegacyConfigFileName);

            try
            {
                if (!File.Exists(configPath))
                {
                    RebornClassConfig initialConfig = LoadLegacyConfig(legacyConfigPath, sapi) ?? new RebornClassConfig();
                    WriteCommentedConfig(configPath, initialConfig);
                }

                string configText = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<RebornClassConfig>(configText, BuildJsonSettings(sapi));
                if (config != null)
                {
                    bool changed = config.ClampUnsafeValues(sapi);
                    List<string> missingKeys = FindMissingConfigKeys(configText);
                    if (missingKeys.Count > 0)
                    {
                        sapi.Logger.Warning("[Multiclass Reborn] Config is missing keys added by newer versions ({0}); using defaults and updating the config file.", string.Join(", ", missingKeys));
                    }

                    if (changed || missingKeys.Count > 0 || !HasJsonComments(configText))
                    {
                        WriteCommentedConfig(configPath, config);
                    }
                }
            }
            catch (Exception exception)
            {
                sapi.Logger.Warning("[Multiclass Reborn] Could not read config, using defaults: {0}", exception.Message);
            }

            config ??= new RebornClassConfig();
            config.ClampUnsafeValues(sapi);
            config.LogConflictWarnings(sapi);

            return config;
        }

        // Reads the old multiclass config when Reborn has not created one yet.
        private static RebornClassConfig LoadLegacyConfig(string legacyConfigPath, ICoreServerAPI sapi)
        {
            if (!File.Exists(legacyConfigPath)) return null;

            string configText = File.ReadAllText(legacyConfigPath);
            RebornClassConfig config = JsonConvert.DeserializeObject<RebornClassConfig>(configText, BuildJsonSettings(sapi));
            config?.ClampUnsafeValues(sapi);

            return config;
        }

        // Keeps config values inside supported gameplay ranges.
        private bool ClampUnsafeValues(ICoreServerAPI sapi)
        {
            bool changed = false;

            changed |= ClampIntValue(nameof(MaxExtraClasses), ref MaxExtraClasses, MinMaxExtraClasses, MaxMaxExtraClasses, sapi);
            changed |= ClampIntValue(nameof(StartingAptitudeTokens), ref StartingAptitudeTokens, MinStartingAptitudeTokens, MaxStartingAptitudeTokens, sapi);
            changed |= ClampFloatValue("SecondaryScale", ref ExtraClassScale, MinExtraClassScale, MaxExtraClassScale, sapi);

            return changed;
        }

        // Logs settings that are valid on their own but inert in the current ruleset.
        private void LogConflictWarnings(ICoreServerAPI sapi)
        {
            if (!AllowStatBonuses)
            {
                WarnIfConfigIgnored(sapi, nameof(OnlyApplyBestPositiveTraitBonus), OnlyApplyBestPositiveTraitBonus, "AllowStats is false, so positive trait stat filtering is not applied.");
                WarnIfConfigIgnored(sapi, nameof(OnlyApplyWorstNegativeTraitPenalty), OnlyApplyWorstNegativeTraitPenalty, "AllowStats is false, so negative trait stat filtering is not applied.");
            }

            WarnIfConfigIgnored(sapi, nameof(RetrainFree), RetrainFree && !RequireGlyphs, "RequireTokens is false, so class forgetting is already free.");
            WarnIfConfigIgnored(sapi, nameof(StartingAptitudeTokens), StartingAptitudeTokens > 0 && !RequireGlyphs, "RequireTokens is false, so players start with configured free class capacity instead of Aptitude Glyphstones.");
            WarnIfConfigIgnored(sapi, nameof(StartingAptitudeTokens), StartingAptitudeTokens > 0 && DisableGenericGlyphstones, "DisableGenericGlyphstones is true, so starting Aptitude Glyphstones are not granted.");
            WarnIfConfigIgnored(sapi, nameof(EnableGlyphstoneRecipes), EnableGlyphstoneRecipes && DisableGenericGlyphstones && !EnableClassBoundGlyphstones && RetrainFreeApplies, "DisableGenericGlyphstones is true, EnableClassBoundGlyphstones is false, and RetrainFree is active, so no built-in glyphstone recipes are registered.");
            WarnIfConfigIgnored(sapi, nameof(EnableClassBoundGlyphstones), EnableClassBoundGlyphstones && MaxExtraClasses == 0 && !AllowForgettingBaseClass && !AllowCommonersChooseBaseClass, "MaxExtraClasses is 0 and base-class replacement is disabled, so class-bound glyphstones cannot apply classes.");
        }

        // Keeps conflict logging terse and consistent.
        private static void WarnIfConfigIgnored(ICoreServerAPI sapi, string key, bool ignored, string reason)
        {
            if (!ignored) return;

            sapi?.Logger.Warning("[Multiclass Reborn] Config {0} has no effect: {1}", key, reason);
        }

        // Clamps one integer and logs the correction.
        private static bool ClampIntValue(string key, ref int value, int min, int max, ICoreServerAPI sapi)
        {
            int original = value;
            value = GameMath.Clamp(value, min, max);

            if (value == original) return false;

            sapi?.Logger.Warning("[Multiclass Reborn] Clamped config {0} from {1} to {2}. Valid range is {3}-{4}.", key, original, value, min, max);
            return true;
        }

        // Clamps one float and logs the correction.
        private static bool ClampFloatValue(string key, ref float value, float min, float max, ICoreServerAPI sapi)
        {
            float original = value;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = max;
            }
            else
            {
                value = GameMath.Clamp(value, min, max);
            }

            if (value.Equals(original)) return false;

            sapi?.Logger.Warning("[Multiclass Reborn] Clamped config {0} from {1} to {2}. Valid range is {3}-{4}.",
                key,
                original.ToString(CultureInfo.InvariantCulture),
                value.ToString(CultureInfo.InvariantCulture),
                min.ToString(CultureInfo.InvariantCulture),
                max.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        // Allows bad individual values to fall back without discarding the whole config.
        private static JsonSerializerSettings BuildJsonSettings(ICoreServerAPI sapi)
        {
            return new JsonSerializerSettings()
            {
                Error = (_, args) =>
                {
                    sapi?.Logger.Warning("[Multiclass Reborn] Could not read config value '{0}', using its default: {1}",
                        args.ErrorContext.Path,
                        args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true;
                }
            };
        }

        // Finds keys absent from older configs so the saved template can be upgraded.
        private static List<string> FindMissingConfigKeys(string configText)
        {
            JObject json = JObject.Parse(configText);
            HashSet<string> presentKeys = json.Properties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return CurrentConfigKeys
                .Where(key => !presentKeys.Contains(key))
                .ToList();
        }

        // Detects comments so plain JSON can be upgraded to the readable template.
        private static bool HasJsonComments(string configText)
        {
            return configText?.Contains("//", StringComparison.Ordinal) == true
                || configText?.Contains("/*", StringComparison.Ordinal) == true;
        }

        // Writes the readable config template used for new or plain JSON configs.
        private static void WriteCommentedConfig(string configPath, RebornClassConfig config)
        {
            File.WriteAllText(configPath, BuildConfigText(config));
        }

        // Builds commented JSON while preserving the current config values.
        private static string BuildConfigText(RebornClassConfig config)
        {
            return $@"{{
  // Allows extra-class stat bonuses to be applied. Default: true.
  ""AllowStats"": {JsonBool(config.AllowStatBonuses)},

  // Allows extra-class recipe traits to count for recipes. Default: true.
  ""AllowRecipes"": {JsonBool(config.AllowRecipeTraits)},

  // Enables craftable Aptitude and Retraining Glyphstones. Default: false.
  ""EnableGlyphstoneRecipes"": {JsonBool(config.EnableGlyphstoneRecipes)},

  // Enables JSON-defined class-bound Aptitude Glyphstones. Default: false.
  ""EnableClassBoundGlyphstones"": {JsonBool(config.EnableClassBoundGlyphstones)},

  // Disables generic Aptitude Glyphstones. Retraining follows RetrainFree. Default: false.
  ""DisableGenericGlyphstones"": {JsonBool(config.DisableGenericGlyphstones)},

  // Multiplies extra-class stat changes. Valid range: 0-3. Default: 0.8.
  ""SecondaryScale"": {config.ExtraClassScale.ToString(CultureInfo.InvariantCulture)},

  // Keeps only the strongest positive trait bonus per affected stat. Default: false.
  ""OnlyApplyBestPositiveTraitBonus"": {JsonBool(config.OnlyApplyBestPositiveTraitBonus)},

  // Keeps only the harshest negative trait penalty per affected stat. Default: false.
  ""OnlyApplyWorstNegativeTraitPenalty"": {JsonBool(config.OnlyApplyWorstNegativeTraitPenalty)},

  // Allows players to forget their main class and return to Commoner. Default: false.
  ""AllowForgettingBaseClass"": {JsonBool(config.AllowForgettingBaseClass)},

  // Allows Commoners to choose a new main class without a glyphstone. Default: false.
  ""AllowCommonersChooseBaseClass"": {JsonBool(config.AllowCommonersChooseBaseClass)},

  // Maximum number of extra class slots a player can have. Valid range: 0-32. Default: 3.
  ""MaxExtraClasses"": {config.MaxExtraClasses},

  // Removes learned extra classes above MaxExtraClasses after that value changes. Default: false.
  ""DropExtraClassesOverMax"": {JsonBool(config.DropExtraClassesOverMax)},

  // Requires glyphstones for adding slots and forgetting classes. Default: true.
  ""RequireTokens"": {JsonBool(config.RequireGlyphs)},

  // Makes class forgetting free when RequireTokens is true. Default: false.
  ""RetrainFree"": {JsonBool(config.RetrainFree)},

  // Aptitude Glyphstones granted on first join. Valid range: 0-64. Default: 0.
  ""StartingAptitudeTokens"": {config.StartingAptitudeTokens}
}}
";
        }

        // Formats booleans as lowercase JSON literals.
        private static string JsonBool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
