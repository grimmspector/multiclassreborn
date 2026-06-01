# Class-Bound Glyphstones

Class-bound glyphstones let content-only mods add items that learn one specific class through the existing Multiclass Reborn picker and server validation flow. The Content mod can be in addition to a Classes mod or it can be included with the Classes mod.

## Server Config

Class-bound glyphstones are disabled by default. Server owners must enable them in `ModConfig/multiclassreborn.json`:

```json
"EnableClassBoundGlyphstones": true
```

Class-bound glyphstones require `RequireTokens` to be `true`. If `EnableClassBoundGlyphstones` is true while `RequireTokens` is false, Multiclass Reborn forces `RequireTokens` to true, logs the repair, and updates the config file.

For a short testing config that can be linked from GitHub without shipping in releases, see `CLASS_BOUND_GLYPHSTONE_CONFIG_EXAMPLE.md`.

When disabled, the item can still exist, but confirming it will not learn or replace a class.

Generic Aptitude Glyphstones can also be disabled:

```json
"DisableGenericGlyphstones": true
```

With that option enabled, generic Aptitude Glyphstone recipes and trader offers are removed, starting Aptitude Glyphstones are not granted, and generic Aptitude Glyphstones cannot add class slots. Retraining Glyphstones remain available when `RequireTokens` is `true` and `RetrainFree` is `false`.

## Item JSON

Use the existing Aptitude Glyphstone item class and add a `targetClass` attribute:

```json
{
  "code": "hunter-glyphstone",
  "class": "AptitudeGlyphstone",
  "attributes": {
    "multiclassreborn": {
      "targetClass": "hunter"
    }
  }
}
```

One glyphstone can also offer several valid classes. The player may choose any listed class in the constrained picker, and the glyphstone is consumed after any one of them is learned or used as a base-class replacement:

```json
{
  "code": "metalsmith-glyphstone",
  "class": "AptitudeGlyphstone",
  "attributes": {
    "multiclassreborn": {
      "targetClasses": [ "hunter", "malefactor" ]
    }
  }
}
```

For a modded class, use that class definition's actual class code. In Aldi's Classes, Blacksmith is defined as `blacksmith`, so an Aldi Blacksmith glyphstone would look like this:

```json
{
  "code": "blacksmith-glyphstone",
  "class": "AptitudeGlyphstone",
  "attributes": {
    "multiclassreborn": {
      "targetClass": "blacksmith"
    }
  }
}
```

The `targetClass` or `targetClasses` value is the Vintage Story character class code from the class JSON. Vanilla classes use plain codes such as `hunter`, `malefactor`, or `tailor`. Some modded classes also use plain codes. Only use a domain-prefixed code such as `somemod:exampleclass` when the class JSON itself uses that full code. If another mod replaces an enabled class while keeping the same class code, the glyphstone resolves to that replacement. If another mod adds a new class with a new code, use that new code.

If both `targetClass` and `targetClasses` are present, Multiclass Reborn combines them and removes duplicates.

## Behavior

- Right-click opens the class picker in constrained mode.
- All classes remain visible for comparison.
- Only the configured target class, or one of the configured target classes, can be confirmed.
- The server re-checks the player hotbar for a matching glyphstone before applying anything.
- The glyphstone is consumed only after the class change succeeds.
- A player cannot consume the glyphstone for a class they already have as their base class or an extra class.
- If a target class is disabled or absent, it is simply unavailable. The server rejects attempts to confirm it and does not consume the glyphstone.

When `MaxExtraClasses` is greater than `0`, a valid class-bound glyphstone learns the target as an extra class while respecting the configured maximum.

When `MaxExtraClasses` is `0`, a valid class-bound glyphstone can replace the player's base class only if existing base-class rules allow it. Replacement is allowed when `AllowForgettingBaseClass` is true, or when the player is a Commoner and `AllowCommonersChooseBaseClass` is true.

## Content Mod Scope

Content mods can decide how their glyphstones are obtained through recipes, trader offers, loot tables, or other JSON content. Multiclass Reborn does not disable third-party recipes for class-bound glyphstones. Multiclass Reborn also does not expose other mods' class-bound glyphstones through recipes, traders, loot, or other sources by default; mod authors should patch their own glyphstones into the world.

## Class Locks

Content mods can require class-bound glyphstones through item JSON alone:

```json
{
  "code": "blacksmith-glyphstone",
  "class": "AptitudeGlyphstone",
  "attributes": {
    "multiclassreborn": {
      "targetClass": "blacksmith",
      "locksTargetClass": true
    }
  }
}
```

When `locksTargetClass` is true, every class listed in `targetClass` or `targetClasses` is blocked from generic Aptitude Glyphstone learning and free Commoner base-class selection. Multiclass Reborn builds this lock list at runtime from every loaded item that uses the `AptitudeGlyphstone` item class, so content mods do not need code. Class-bound glyphstones that target the locked class still work. They do not need `locksTargetClass` themselves; any loaded locking glyphstone is enough to reserve that class from generic learning.

This is intentionally item-driven so a content mod can control how a class is obtained without asking the class mod author to patch their class JSON. If two content mods define incompatible locks or progression paths, that is a content-pack compatibility problem for the server owner to resolve.

If the item mod that supplied the lock is removed, the lock disappears at runtime. That is the desired safe fallback: the class returns to generic learning if generic learning is enabled and the class itself is still enabled. Missing or disabled target classes are harmless because locks are filtered against the enabled class ledger.

Class JSON can also declare a lock when a class author wants to own that rule directly:

```json
{
  "code": "exampleclass",
  "attributes": {
    "multiclassreborn": {
      "requireClassBoundGlyphstone": true
    }
  }
}
```

That prevents the class from being learned through the generic Aptitude Glyphstone flow or free Commoner base-class selection. A class-bound glyphstone with the matching `targetClass` can still grant it.

To require one exact glyphstone item, set `requiredGlyphstone` to a full item code:

```json
{
  "code": "exampleclass",
  "attributes": {
    "multiclassreborn": {
      "requiredGlyphstone": "examplemod:exampleclass-glyphstone"
    }
  }
}
```

`requiredGlyphstone` also implies `requireClassBoundGlyphstone`.

## Commands

The item-give commands are admin-only:

- `/multiclass giveglyph <playername>`
- `/multiclass giveretrainglyph <playername>`
- `/multiclass giveboundglyph <playername> <itemcode>`

The class picker still uses player chat commands internally for learn, forget, and glyphstone confirmation. Those commands remain player-accessible so the GUI can function. Moving those actions to admin-only commands would require replacing the GUI command path with network packets.

## Built-In Recipes

When both `EnableGlyphstoneRecipes` and `EnableClassBoundGlyphstones` are true, Multiclass Reborn registers recipes for enabled vanilla classes: Blackguard, Clockmaker, Commoner, Hunter, Malefactor, Tailor.
