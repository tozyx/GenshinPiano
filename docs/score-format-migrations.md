# `.gpiano` format migrations

The current score format is identified by `ScoreDocument.CurrentSchemaVersion`.
All files written by `JsonScoreDocumentSerializer` use that version.

## Loading pipeline

1. Parse the file into a mutable JSON object.
2. Read `schemaVersion`; a missing property is treated as legacy version `0`.
3. Apply every registered migration sequentially until the current version.
4. Deserialize the migrated JSON into `ScoreDocument`.
5. Validate the complete typed document.
6. Materialize automatic note durations.

Files newer than the running application are rejected. This prevents an older
application from silently discarding fields introduced by a newer version.

## When to increment the version

Increment the schema version when a change cannot be represented safely by the
existing defaults, including renamed or removed properties, changed meanings,
new required values, or structural changes.

Adding an optional property with a safe default generally does not require a
new version. Do not increment the version without an actual data migration.

## Adding version 2

1. Change `ScoreDocument.CurrentSchemaVersion` from `1` to `2`.
2. Add an `IScoreSchemaMigration` implementation whose versions are exactly
   `FromVersion = 1` and `ToVersion = 2`.
3. Register it in the default migration list in `ScoreSchemaMigrator` after the
   existing `ScoreSchemaV0ToV1Migration`.
4. Add tests containing a real version-1 JSON fixture and verify the resulting
   version-2 document.
5. Keep all older migration steps. Users may skip multiple application releases.

Migrations operate only on JSON and must be deterministic. They should not use
UI state, localization, network access, or machine-specific paths.
