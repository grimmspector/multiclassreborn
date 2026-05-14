using System;
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

        [JsonProperty("SecondaryScale")]
        public float ExtraClassScale = 0.8f;

        public int MaxExtraClasses = 3;

        [JsonProperty("RequireTokens")]
        public bool RequireRunes;

        private const string ConfigFileName = "multiclass.json";

        /// <summary>
        /// Loads the persisted config and rewrites it through our current shape.
        /// This keeps old config files readable while dropping unknown old fields.
        /// </summary>
        public static RebornClassConfig Load(ICoreServerAPI sapi)
        {
            RebornClassConfig config = null;

            try
            {
                config = sapi.LoadModConfig<RebornClassConfig>(ConfigFileName);
            }
            catch (Exception exception)
            {
                sapi.Logger.Warning("[Multiclass Reborn] Could not read config, using defaults: {0}", exception.Message);
            }

            config ??= new RebornClassConfig();
            config.ClampUnsafeValues();
            sapi.StoreModConfig(config, ConfigFileName);

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
    }
}
