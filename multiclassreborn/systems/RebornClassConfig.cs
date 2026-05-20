using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
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

        // Aptitude Glyphstones granted on first join when RequireTokens is true.
        public int StartingAptitudeTokens;

        private const int MinMaxExtraClasses = 0;
        private const int MaxMaxExtraClasses = 32;
        private const int MinStartingAptitudeTokens = 0;
        private const int MaxStartingAptitudeTokens = 64;
        private const float MinExtraClassScale = 0f;
        private const float MaxExtraClassScale = 3f;
        private const string ConfigFileName = "multiclassreborn.json";
        private const string LegacyConfigFileName = "multiclass.json";

        // Preserve user comments by only rewriting plain JSON or newly created configs.
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
                    if ((changed && LooksLikeGeneratedConfig(configText)) || !HasJsonComments(configText))
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

        // Detects configs written by this mod so sanitized values can be persisted.
        private static bool LooksLikeGeneratedConfig(string configText)
        {
            return configText?.Contains("// Allows extra-class stat bonuses to be applied.", StringComparison.Ordinal) == true
                && configText.Contains("// Aptitude Glyphstones granted on first join", StringComparison.Ordinal);
        }

        // Detects hand-written comments so the config file is left alone.
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
