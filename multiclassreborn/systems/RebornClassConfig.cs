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

        // Enables craftable glyphstone recipes. Keep false for release builds
        // unless craftable progression is desired on the server.
        [JsonProperty("EnableGlyphstoneRecipes")]
        public bool EnableGlyphstoneRecipes = true;

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

        private const string ConfigFileName = "multiclass.json";

        /// <summary>
        /// Loads the persisted config without stripping its inline comments.
        /// </summary>
        public static RebornClassConfig Load(ICoreServerAPI sapi)
        {
            RebornClassConfig config = null;
            string configPath = Path.Combine(sapi.GetOrCreateDataPath("ModConfig"), ConfigFileName);

            try
            {
                if (!File.Exists(configPath))
                {
                    WriteCommentedConfig(configPath, new RebornClassConfig());
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
        /// Prevents config typos from producing impossible slot or stat states.
        /// </summary>
        private void ClampUnsafeValues()
        {
            if (MaxExtraClasses < 0) MaxExtraClasses = 0;
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
  // This is true for testing; set it false for a release unless recipes are desired.
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
  ""RequireTokens"": {JsonBool(config.RequireGlyphs)}
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
