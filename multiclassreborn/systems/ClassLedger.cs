using System.Collections.Generic;
using System.Linq;
using multiclassreborn.items;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

#nullable disable

namespace multiclassreborn.systems
{
    // Cached lookup of class and trait definitions loaded from game assets.
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
        public HashSet<string> ClassBoundOnlyCodes { get; private set; } = new HashSet<string>();
        public Dictionary<string, string> RequiredGlyphstoneByClassCode { get; private set; } = new Dictionary<string, string>();

        // Load every class/trait asset before building maps, since other mods can add
        // character classes through the same config paths.
        public void Reload(ICoreAPI api)
        {
            List<Trait> discoveredTraits = new List<Trait>();
            List<ClassRecord> discoveredClasses = new List<ClassRecord>();

            foreach (IAsset traitAsset in api.Assets.GetMany("config/traits.json", null, true))
            {
                discoveredTraits.AddRange(traitAsset.ToObject<List<Trait>>((JsonSerializerSettings)null));
            }

            foreach (string classAssetPath in ClassAssetPaths)
            {
                foreach (IAsset classAsset in api.Assets.GetMany(classAssetPath, null, true))
                {
                    discoveredClasses.AddRange(ReadClassRecords(classAsset));
                }
            }

            Traits = discoveredTraits;

            // Replacement mods can disable vanilla classes and add enabled copies later.
            List<ClassRecord> enabledRecords = discoveredClasses
                .Where(record => record.ClassDef?.Code != null && record.ClassDef.Enabled)
                .GroupBy(record => record.ClassDef.Code)
                .Select(group => group.Last())
                .ToList();

            EnabledClasses = enabledRecords
                .Select(record => record.ClassDef)
                .ToList();

            TraitByCode = Traits
                .Where(trait => trait?.Code != null)
                .GroupBy(trait => trait.Code)
                .ToDictionary(group => group.Key, group => group.Last());

            ClassByCode = EnabledClasses
                .Where(classDef => classDef?.Code != null)
                .ToDictionary(classDef => classDef.Code, classDef => classDef);

            HashSet<string> classDefinedLocks = enabledRecords
                .Where(record => record.RequiresClassBoundGlyphstone)
                .Select(record => record.ClassDef.Code)
                .ToHashSet();

            ClassBoundOnlyCodes = classDefinedLocks
                .Concat(ReadItemDefinedClassLocks(api))
                .Where(classCode => ClassByCode.ContainsKey(classCode))
                .ToHashSet();

            RequiredGlyphstoneByClassCode = enabledRecords
                .Where(record => !string.IsNullOrWhiteSpace(record.RequiredGlyphstoneCode))
                .ToDictionary(record => record.ClassDef.Code, record => record.RequiredGlyphstoneCode);
        }

        // Item-side locks let content packs reserve classes without patching class JSON.
        private static IEnumerable<string> ReadItemDefinedClassLocks(ICoreAPI api)
        {
            foreach (Item item in api.World?.Items ?? new Item[0])
            {
                if (item is not ClassSlotGlyphItem) continue;

                ItemStack stack = new ItemStack(item);
                if (!ClassSlotGlyphItem.LocksTargetClass(stack)) continue;

                foreach (string classCode in ClassSlotGlyphItem.GetTargetClasses(stack))
                {
                    yield return classCode;
                }
            }
        }

        // Reads Reborn metadata that the Vintage Story class model drops during parsing.
        private static IEnumerable<ClassRecord> ReadClassRecords(IAsset classAsset)
        {
            List<JObject> classArray = classAsset.ToObject<List<JObject>>((JsonSerializerSettings)null);

            foreach (JObject token in classArray)
            {
                CharacterClass classDef = token.ToObject<CharacterClass>();
                bool requiresGlyphstone = token.SelectToken("attributes.multiclassreborn.requireClassBoundGlyphstone")?.Value<bool>() == true;
                string requiredGlyphstone = token.SelectToken("attributes.multiclassreborn.requiredGlyphstone")?.Value<string>()?.Trim().ToLowerInvariant();

                yield return new ClassRecord(classDef, requiresGlyphstone || !string.IsNullOrWhiteSpace(requiredGlyphstone), requiredGlyphstone);
            }
        }

        // Carries the class plus the Reborn metadata found beside it.
        private sealed class ClassRecord
        {
            public readonly CharacterClass ClassDef;
            public readonly bool RequiresClassBoundGlyphstone;
            public readonly string RequiredGlyphstoneCode;

            public ClassRecord(CharacterClass classDef, bool requiresClassBoundGlyphstone, string requiredGlyphstoneCode)
            {
                ClassDef = classDef;
                RequiresClassBoundGlyphstone = requiresClassBoundGlyphstone;
                RequiredGlyphstoneCode = requiredGlyphstoneCode;
            }
        }
    }
}
