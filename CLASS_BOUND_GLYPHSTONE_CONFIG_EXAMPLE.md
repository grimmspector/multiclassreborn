# Class-Bound Glyphstone Config Example

This example is for repository documentation and support links. It is not part of the packaged mod release.

Use this as a minimal known-good setup when testing class-bound glyphstones:

```json
{
  "EnableClassBoundGlyphstones": true,
  "RequireTokens": true,
  "RetrainFree": true
}
```

`RequireTokens` is required for class-bound glyphstones. `RetrainFree` is optional, but useful while testing because it keeps class forgetting free even though glyphstone-gated class learning is enabled.

Keep the rest of `ModConfig/multiclassreborn.json` as generated unless the server needs a specific rule change. Run `/multiclass regenconfig` if the file is missing newer keys or has values the loader cannot read.
