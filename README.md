# Multiclass Reborn

Multiclass Reborn is a clean-room replacement for the Vintage Story `multiclass` mod domain.

It saves the original mod ids data and persistent player data keys so existing worlds can migrate cleanly, while the source layout, class names, and implementation are newly written for this project.

## Completed Goals

- [x] Preserve existing `multiclass` save data.
- [x] Keep the compatibility with mod id as `multiclass`.
- [x] Let players learn extra classes through commands, the class picker, or Aptitude Glyphstones.
- [x] Let players forget extra classes when server rules allow it.
- [x] Apply extra class trait stats at a configurable secondary scale.
- [x] Optionally expose extra traits for recipe gating.
- [x] Keep vanilla and modded class trait text readable in the Traits tab and class picker.
- [x] Optionally suppress duplicate extra-class stat stacking while preserving the harshest duplicate negative trait.
- [x] Rename user-facing rune items to glyphstones while preserving compatibility remaps.
- [x] Verify trait wrapping across several UI scales with large third-party class descriptions.
- [x] Consider synchronizing server config values to the client so scaled preview text always matches server settings.
- [x] Allow various glyph types to be optionally made via recipe.

## New Mechanic
There's a new mechanic, called ***Class Bound Glyphstones***, which are JSON-defined *Aptitude Glyphstones* that teach a specific class or classes when used. This allows servers to create custom glyphstones for extra classes without needing to add new items or use commands. You can see more in the `CLASS_BOUND_GLYPHS.md` documentation.

## Server Config

The default config is written to `ModConfig/multiclassreborn.json`. More details than below in `CONFIG.md`, the configuration can be somewhat complex and this file details what is ignored or conflicts in different scenarios.

- `AllowStats`: Enables stat changes from extra classes. Default: `true`.
- `AllowRecipes`: Allows extra-class recipe traits to count for recipe gating. Default: `true`.
- `EnableGlyphstoneRecipes`: Enables craftable Aptitude and Retraining Glyphstones. Default: `false`.
- `EnableClassBoundGlyphstones`: Enables JSON-defined class-bound Aptitude Glyphstones. Requires `RequireTokens`. Default: `false`.
- `DisableGenericGlyphstones`: Disables generic Aptitude Glyphstones while leaving class-bound glyphstones available. Default: `false`.
- `SecondaryScale`: Multiplies stat changes from extra classes. Valid range: `0` to `3`. Default: `0.8`.
- `OnlyApplyBestPositiveTraitBonus`: Keeps only the strongest positive bonus for each affected stat. Default: `false`.
- `OnlyApplyWorstNegativeTraitPenalty`: Keeps only the harshest negative penalty for each affected stat. Default: `false`.
- `AllowForgettingBaseClass`: Lets players forget their main class and return to Commoner. Default: `false`.
- `AllowCommonersChooseBaseClass`: Lets Commoners choose a new main class without a glyphstone. Default: `false`.
- `MaxExtraClasses`: Sets the maximum number of extra class slots. Valid range: `0` to `32`. Default: `3`.
- `DropExtraClassesOverMax`: Removes learned extra classes above `MaxExtraClasses` after that value changes. Default: `false`.
- `RequireTokens`: Requires glyphstones for adding slots and forgetting classes. Forced to `true` when class-bound glyphstones are enabled. Default: `false`.
- `RetrainFree`: Makes class forgetting free while glyphstones are still required for adding slots. Default: `false`.
- `StartingAptitudeTokens`: Grants Aptitude Glyphstones to new players when glyphstones are required. Valid range: `0` to `64`. Default: `0`.

## Admin Config Commands

- `/multiclass configcheck`: Reports config values that were unreadable, missing, clamped, or forced during the last config load.
- `/multiclass regenconfig`: Rewrites `ModConfig/multiclassreborn.json` as a clean commented file, preserving readable settings and defaulting unreadable or missing settings.

## Mod Compatibility API

Other code mods can use the public `MulticlassCompatibility` service to check both a player's main class and their learned extra classes. Reference `MulticlassReborn.dll` when compiling your mod, and declare `multiclassreborn` as a dependency when calling the service directly.

```csharp
using multiclassreborn;
using Vintagestory.API.Common;

EntityPlayer player = serverPlayer.Entity;

if (MulticlassCompatibility.HasClass(player, "innkeeper"))
{
    // The player has Innkeeper as either their main or an extra class.
}
```

Available methods:

- `HasClass(player, classCode)`: Checks the main and extra classes.
- `IsExtraClass(player, classCode)`: Checks only learned extra classes.
- `GetAllClassCodes(player)`: Returns the main class followed by distinct extra classes.
- `GetExtraClassCodes(player)`: Returns only distinct learned extra classes.

Class-code comparisons are trimmed and case-insensitive. Collection methods return snapshots, so changing the returned collection does not modify player data.

## Incomplete / Follow-up Goals

- Currently none
