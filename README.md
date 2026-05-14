# Multiclass Reborn

Multiclass Reborn is a clean-room replacement for the Vintage Story `multiclass` mod domain.

It keeps the same mod id and persistent player data keys so existing worlds can migrate cleanly, while the source layout, class names, and implementation are newly written for this project.

## Current Goals

- Preserve existing `multiclass` save data.
- Keep the compatibility mod id and asset domain as `multiclass` so existing worlds and item codes migrate cleanly.
- Let players learn extra classes through commands, the class picker, or class slot runes.
- Let players forget extra classes when server rules allow it.
- Apply extra class trait stats at a configurable secondary scale.
- Optionally expose extra traits for recipe gating.
- Keep vanilla and modded class trait text readable in the Traits tab and class picker.
- Optionally suppress duplicate extra-class stat stacking while preserving the harshest duplicate negative trait.

## Incomplete / Follow-up Goals

- Verify trait wrapping across several UI scales with large third-party class descriptions.
- Add user-facing config documentation for duplicate stat suppression.
- Consider synchronizing server config values to the client so scaled preview text always matches server settings.
- Review whether rune item names should remain user-facing or be renamed to glyphstones without breaking compatibility.
