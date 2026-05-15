# Multiclass Reborn

Multiclass Reborn is a clean-room replacement for the Vintage Story `multiclass` mod domain.

It keeps the same mod id and persistent player data keys so existing worlds can migrate cleanly, while the source layout, class names, and implementation are newly written for this project.

## Release Status

Version `0.1.0` is ready for a first Mod DB deployment package.

The mod keeps the compatibility mod id `multiclass` while using the `multiclassreborn` asset domain for new assets. Legacy rune item codes are remapped to the current glyphstone items so existing worlds can continue forward cleanly.

## Completed Goals

- [x] Preserve existing `multiclass` save data.
- [x] Keep the compatibility mod id as `multiclass`.
- [x] Let players learn extra classes through commands, the class picker, or Aptitude Glyphstones.
- [x] Let players forget extra classes when server rules allow it.
- [x] Apply extra class trait stats at a configurable secondary scale.
- [x] Optionally expose extra traits for recipe gating.
- [x] Keep vanilla and modded class trait text readable in the Traits tab and class picker.
- [x] Optionally suppress duplicate extra-class stat stacking while preserving the harshest duplicate negative trait.
- [x] Rename user-facing rune items to glyphstones while preserving compatibility remaps.

## Server Config

The default config is written to `ModConfig/multiclass.json`.

- `AllowStats`: Enables stat changes from extra classes.
- `AllowRecipes`: Allows extra-class recipe traits to count for recipe gating.
- `EnableGlyphstoneRecipes`: Enables craftable Aptitude and Retraining Glyphstones.
- `SecondaryScale`: Multiplies stat changes from extra classes.
- `OnlyApplyBestPositiveTraitBonus`: Keeps only the strongest positive bonus for each affected stat.
- `OnlyApplyWorstNegativeTraitPenalty`: Keeps only the harshest negative penalty for each affected stat.
- `AllowForgettingBaseClass`: Lets players forget their main class and return to Commoner.
- `AllowCommonersChooseBaseClass`: Lets Commoners choose a new main class without a glyphstone.
- `MaxExtraClasses`: Sets the maximum number of extra class slots.
- `RequireTokens`: Requires glyphstones for adding slots and forgetting classes.
- `StartingAptitudeTokens`: Grants Aptitude Glyphstones to new players when glyphstones are required.

## Incomplete / Follow-up Goals

- Verify trait wrapping across several UI scales with large third-party class descriptions.
- Consider synchronizing server config values to the client so scaled preview text always matches server settings.
