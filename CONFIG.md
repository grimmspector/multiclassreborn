# Config Reference

Multiclass Reborn writes its server config to `ModConfig/multiclassreborn.json`. The file supports comments, and the loader preserves hand-written comments instead of rewriting the file every time the server starts.

The config is server-side. Clients receive the relevant choices through player state and do not need their own matching config file.

## General Class Effects

`AllowStats` controls whether extra-class stat bonuses and penalties are applied. Default: `true`.

When `AllowStats` is `false`, `SecondaryScale`, `OnlyApplyBestPositiveTraitBonus`, and `OnlyApplyWorstNegativeTraitPenalty` have no gameplay effect because no extra-class stat changes are applied.

`AllowRecipes` controls whether extra-class recipe traits count for recipe checks. Default: `true`.

`SecondaryScale` multiplies stat changes from extra classes before they are applied. Default: `0.8`. Valid range: `0` to `3`.

`OnlyApplyBestPositiveTraitBonus` keeps only the strongest positive trait bonus per affected stat. Default: `false`.

`OnlyApplyWorstNegativeTraitPenalty` keeps only the harshest negative trait penalty per affected stat. Default: `false`.

## Extra Class Slots

`MaxExtraClasses` is the maximum number of extra classes a player can learn. Default: `3`. Valid range: `0` to `32`.

When `MaxExtraClasses` is `0`, normal extra-class learning is disabled. Class-bound glyphstones can still replace a player's base class only when `AllowForgettingBaseClass` or `AllowCommonersChooseBaseClass` allows that replacement.

`DropExtraClassesOverMax` removes learned extra classes above `MaxExtraClasses` after that value changes. Default: `false`.

This setting only prunes when the reviewed max changes. It does not remove classes just because a player joins with an old state that is already within the current limit.

## Glyphstone Access

`RequireTokens` controls whether players need Aptitude Glyphstones to add extra-class slots and Retraining Glyphstones to forget classes. Default: `false`.

`RequireTokens` must be `true` when `EnableClassBoundGlyphstones` is `true`. If class-bound glyphstones are enabled while `RequireTokens` is `false`, Multiclass Reborn forces `RequireTokens` to `true`, logs that repair, and updates the config file on disk.

When `RequireTokens` is `false`, players use the configured `MaxExtraClasses` as free class capacity. `RetrainFree` has no effect because forgetting is already free, and `StartingAptitudeTokens` has no effect because starting slots are not item-gated.

`RetrainFree` makes class forgetting free while glyphstones are still required for adding slots. Default: `false`.

`RetrainFree` only applies when `RequireTokens` is `true`.

`StartingAptitudeTokens` is the number of Aptitude Glyphstones granted on first join. Default: `0`. Valid range: `0` to `64`.

Starting tokens are only granted when `RequireTokens` is `true` and `DisableGenericGlyphstones` is `false`.

`EnableGlyphstoneRecipes` registers built-in crafting recipes for glyphstones. Default: `false`.

When `DisableGenericGlyphstones` is `true`, `EnableClassBoundGlyphstones` is `false`, and `RetrainFree` is active, `EnableGlyphstoneRecipes` has no effect because generic Aptitude recipes are skipped, class-bound recipes are disabled, and Retraining Glyphstones are not needed.

## Generic Glyphstones

`DisableGenericGlyphstones` disables generic Aptitude Glyphstone gameplay. Default: `false`.

When enabled, the generic Aptitude Glyphstone recipe and trader offers are removed, starting Aptitude Glyphstones are not granted, and generic Aptitude Glyphstones cannot add class slots.

This setting does not disable class-bound glyphstones or Retraining Glyphstones. Use `EnableClassBoundGlyphstones` to control class-bound glyphstones. Retraining Glyphstones are available whenever `RequireTokens` is `true` and `RetrainFree` is `false`.

## Class-Bound Glyphstones

`EnableClassBoundGlyphstones` enables JSON-defined class-bound Aptitude Glyphstones. Default: `false`.

Class-bound glyphstones require `RequireTokens` to be `true`. Multiclass Reborn repairs that dependency automatically when loading the config.

When disabled, class-bound glyphstone items can still exist, but confirming them will not learn or replace a class.

When `EnableGlyphstoneRecipes` and `EnableClassBoundGlyphstones` are both `true`, Multiclass Reborn registers built-in recipes for enabled vanilla class-bound glyphstones.

When `EnableClassBoundGlyphstones` is `true`, `MaxExtraClasses` is `0`, `AllowForgettingBaseClass` is `false`, and `AllowCommonersChooseBaseClass` is `false`, class-bound glyphstones cannot apply classes because they have no extra-class slot path or base-class replacement path.

For item JSON, class locks, and built-in class-bound recipes, see `CLASS_BOUND_GLYPHSTONES.md`.

## Base Class Rules

`AllowForgettingBaseClass` allows players to forget their main class and return to Commoner. Default: `false`.

If `RequireTokens` is `true` and `RetrainFree` is `false`, base-class forgetting consumes a Retraining Glyphstone.

`AllowCommonersChooseBaseClass` allows Commoners to choose a new main class without a glyphstone. Default: `false`.

This setting only applies to players whose current main class is Commoner. It also allows class-bound glyphstones to replace the base class for Commoners when `MaxExtraClasses` is `0`.

## Startup Warnings

The server logs warnings for two kinds of config issues:

- Incorrect numeric or unreadable values are still clamped or defaulted as before.
- Valid values that cannot do anything because another setting disables their feature are logged as config conflict warnings.

Conflict warnings do not change the config file. They are meant to point server owners at settings that can be removed or paired with the feature flag they depend on.
