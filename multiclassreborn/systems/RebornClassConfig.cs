using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

#nullable disable

namespace multiclassreborn.systems
{
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

        [JsonProperty("RequireTokens")]
        public bool RequireGlyphs;

        // Aptitude Glyphstones granted on first join when RequireTokens is true.
        public int StartingAptitudeTokens;

        private const string ConfigFileName = "multiclassreborn.json";
        private const string LegacyConfigFileName = "multiclass.json";

        /// <summary>
        /// Loads the persisted config without stripping its inline comments.
        /// </summary>
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
                    RebornClassConfig initialConfig = LoadLegacyConfig(legacyConfigPath) ?? new RebornClassConfig();
                    WriteCommentedConfig(configPath, initialConfig);
                }

                string configText = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<RebornClassConfig>(configText);
                if (config != null && !HasJsonComments(configText))
                {
                    config.ClampUnsafeValues();
                    WriteCommentedConfig(configPath, config);
                }
            }
            catch (Exception exception)
            {
                sapi.Logger.Warning("[Multiclass Reborn] Could not read config, using defaults: {0}", exception.Message);
            }

            config ??= new RebornClassConfig();
            config.ClampUnsafeValues();

            return config;
        }

        /// <summary>
        /// Reads the original multiclass config as the first Reborn config seed.
        /// </summary>
        private static RebornClassConfig LoadLegacyConfig(string legacyConfigPath)
        {
            if (!File.Exists(legacyConfigPath)) return null;

            string configText = File.ReadAllText(legacyConfigPath);
            RebornClassConfig config = JsonConvert.DeserializeObject<RebornClassConfig>(configText);
            config?.ClampUnsafeValues();

            return config;
        }

        /// <summary>
        /// Prevents config typos from producing impossible slot or stat states.
        /// </summary>
        private void ClampUnsafeValues()
        {
            if (MaxExtraClasses < 0) MaxExtraClasses = 0;
            if (StartingAptitudeTokens < 0) StartingAptitudeTokens = 0;
            if (ExtraClassScale < 0f) ExtraClassScale = 0f;
        }

        /// <summary>
        /// Tests whether an existing config already has manual comments to preserve.
        /// </summary>
        private static bool HasJsonComments(string configText)
        {
            return configText?.Contains("//", StringComparison.Ordinal) == true
                || configText?.Contains("/*", StringComparison.Ordinal) == true;
        }

        /// <summary>
        /// Writes a readable config template with the current values.
        /// </summary>
        private static void WriteCommentedConfig(string configPath, RebornClassConfig config)
        {
            File.WriteAllText(configPath, BuildConfigText(config));
        }

        /// <summary>
        /// Builds the commented JSON config used by new and upgraded worlds.
        /// </summary>
        private static string BuildConfigText(RebornClassConfig config)
        {
            return $@"{{
  // Allows extra-class stat bonuses to be applied.
  ""AllowStats"": {JsonBool(config.AllowStatBonuses)},

  // Allows extra-class recipe traits to count for recipes.
  ""AllowRecipes"": {JsonBool(config.AllowRecipeTraits)},

  // Enables craftable Aptitude and Retraining Glyphstones.
  ""EnableGlyphstoneRecipes"": {JsonBool(config.EnableGlyphstoneRecipes)},

  // Multiplies stat changes from extra classes before applying them.
  ""SecondaryScale"": {config.ExtraClassScale.ToString(CultureInfo.InvariantCulture)},

  // Keeps only the strongest positive trait bonus per affected stat.
  ""OnlyApplyBestPositiveTraitBonus"": {JsonBool(config.OnlyApplyBestPositiveTraitBonus)},

  // Keeps only the harshest negative trait penalty per affected stat.
  ""OnlyApplyWorstNegativeTraitPenalty"": {JsonBool(config.OnlyApplyWorstNegativeTraitPenalty)},

  // Allows players to forget their main class and return to Commoner.
  ""AllowForgettingBaseClass"": {JsonBool(config.AllowForgettingBaseClass)},

  // Allows Commoners to choose a new main class without a glyphstone.
  ""AllowCommonersChooseBaseClass"": {JsonBool(config.AllowCommonersChooseBaseClass)},

  // Maximum number of extra class slots a player can have.
  ""MaxExtraClasses"": {config.MaxExtraClasses},

  // Requires glyphstones for adding slots and forgetting classes.
  ""RequireTokens"": {JsonBool(config.RequireGlyphs)},

  // Aptitude Glyphstones granted on first join when RequireTokens is true.
  ""StartingAptitudeTokens"": {config.StartingAptitudeTokens}
}}
";
        }

        /// <summary>
        /// Formats booleans as JSON literals.
        /// </summary>
        private static string JsonBool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
