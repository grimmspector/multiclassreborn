using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn.systems
{
    internal class ClassLedger
    {
        private static readonly string[] ClassAssetPaths =
        {
            "config/characterclasses.json",
            "config/characterclasses/gloomeclasses.json"
        };

        public List<Trait> Traits { get; private set; } = new List<Trait>();
        public List<CharacterClass> EnabledClasses { get; private set; } = new List<CharacterClass>();
        public Dictionary<string, Trait> TraitByCode { get; private set; } = new Dictionary<string, Trait>();
        public Dictionary<string, CharacterClass> ClassByCode { get; private set; } = new Dictionary<string, CharacterClass>();

        /// <summary>
        /// Reads the game and mod class definition assets into quick lookup maps.
        /// </summary>
        public void Reload(ICoreAPI api)
        {
            List<Trait> discoveredTraits = new List<Trait>();
            List<CharacterClass> discoveredClasses = new List<CharacterClass>();

            foreach (IAsset traitAsset in api.Assets.GetMany("config/traits.json", null, true))
            {
                discoveredTraits.AddRange(traitAsset.ToObject<List<Trait>>((JsonSerializerSettings)null));
            }

            foreach (string classAssetPath in ClassAssetPaths)
            {
                foreach (IAsset classAsset in api.Assets.GetMany(classAssetPath, null, true))
                {
                    discoveredClasses.AddRange(classAsset.ToObject<List<CharacterClass>>((JsonSerializerSettings)null));
                }
            }

            Traits = discoveredTraits;
            EnabledClasses = discoveredClasses
                .GroupBy(classDef => classDef.Code)
                .Select(group => group.First())
                .Where(classDef => classDef.Enabled)
                .ToList();

            TraitByCode = Traits
                .Where(trait => trait?.Code != null)
                .GroupBy(trait => trait.Code)
                .ToDictionary(group => group.Key, group => group.First());

            ClassByCode = EnabledClasses
                .Where(classDef => classDef?.Code != null)
                .ToDictionary(classDef => classDef.Code, classDef => classDef);
        }
    }
}
