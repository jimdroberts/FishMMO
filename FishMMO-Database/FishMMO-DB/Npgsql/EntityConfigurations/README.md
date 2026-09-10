# Entity Configuration — Namespace Note

All entity configuration classes in this directory use the namespace
`FishMMO.Database.Npgsql.Entities` even though they reside under
`Npgsql/EntityConfigurations/` in the project tree.

This divergence is **intentional**: EF Core discovers `IEntityTypeConfiguration<T>`
implementations by type scanning. `NpgsqlDbContext.OnModelCreating` calls
`modelBuilder.ApplyConfigurationsFromAssembly(typeof(NpgsqlDbContext).Assembly)`,
which relies on the declaring assembly -- not the namespace or folder path. Keeping the
same namespace as the entity models avoids `using` aliases and keeps configuration
files consistent with the entities they configure.

If a future reader finds this surprising, please keep this note in mind rather
than reorganising files to match the folder structure.

## What is here

55 files, one per table, mirroring the folders under `Npgsql/Entities/`. The table name a
configuration declares with `ToTable(...)` is the authority for the schema; the C# names are
`<Name>Entity` / `<Name>EntityConfiguration`.

| Folder | Tables |
|---|---|
| (root) | `deployment_secrets` |
| `Login/` | `accounts`, `auth_tokens`, `connection_token_keys`, `email_queue`, `login_servers`, `login_server_signing_keys`, `two_factor_recovery_codes` |
| `World/` | `world_servers`, `kick_requests` |
| `Scene/` | `scenes`, `scene_servers`, `chat`, `quests`, `group_finder_queue` |
| `Scene/Character/` | `characters` plus 22 per-character tables, including `character_item`, `character_waypoints` and `currency_ledger` |
| `Scene/Guild/` | `guilds`, `guild_rank`, `guild_log`, `guild_application`, `guild_updates` |
| `Scene/Party/` | `parties`, `party_updates` |
| `Scene/Arena/` | `arena_match`, `arena_match_member`, `arena_season`, `arena_rating`, `arena_penalty` |
| `Scene/Housing/` | `plots`, `plot_structures`, `plot_access`, `plot_vault`, `plot_updates` |

The full per-table inventory lives in [`../../README.md`](../../README.md#table-inventory).

## Conventions these files follow

- Timestamps default with `HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)")`, never a
  client clock, so rows written by different scene servers order correctly under clock skew.
- Every index a service's hot query depends on is declared here, with a comment naming the query
  it serves. Services write their upserts as raw SQL with an `ON CONFLICT (columns)` list, so a
  unique index declared here is load-bearing for that statement: change its columns and the upsert
  starts failing at runtime, not at compile time.
- Partial indexes are used wherever the interesting rows are a minority of the table
  (`HasFilter("active = TRUE")`, `HasFilter("tax_due_utc IS NOT NULL")`,
  `HasFilter("owner_character_id <> 0")`, and similar).
- Enum-valued columns are stored as `int` with a comment fixing the numeric meaning, because this
  assembly cannot reference `FishMMO.Shared`. The pairing is pinned by tests
  (`ItemContainerTypeParityTests`, `CurrencyLedgerStateTests`), not by a shared type.

## Configurations added in this window

- **`Scene/Character/CharacterItemEntityConfiguration.cs`** replaced
  `CharacterInventoryEntityConfiguration`, `CharacterEquipmentEntityConfiguration` and
  `CharacterBankEntityConfiguration`, all three deleted. One `character_item` table keyed by the
  item's own id; `(character_id, container, slot)` is a unique **index**, not the upsert conflict
  target, and is checked per row — see the file's comment for why a straight slot swap would trip
  it and how `CharacterItemService.SaveSnapshotAsync` avoids emitting one.
- **`Scene/Character/CharacterWaypointEntityConfiguration.cs`** — composite key
  `(CharacterID, SceneName, Page)`, which is both the upsert's arbiter and the prefix scan the
  character load uses. `Mask` is a bitmask page merged with `mask | EXCLUDED.mask` in
  `CharacterWaypointService`; the table is deliberately **not** versioned last-writer-wins, since
  a character never un-discovers a place. `SceneName` is capped at 64 characters.
- **`Scene/Character/CurrencyLedgerEntityConfiguration.cs`** — append-only history. `Amount` is
  always positive; direction comes from `State` (`Absorbed` = 1, `Returned` = 2, and
  `CurrencyLedgerService.RecordAsync` rejects anything else, including the `Unsettled` default).
  Two composite indexes: `ix_currency_ledger_character_time` for per-character history and
  `ix_currency_ledger_reason_time` for per-sink aggregation over a window.
- **`Scene/GroupFinderQueueEntityConfiguration.cs`** — the shared dungeon *and* arena queue.
  `CharacterID` is uniquely indexed (a character cannot be placed in two groups at once);
  `SceneType` defaults to `2` (`SceneType.Group`) because every row predating arenas was a dungeon
  row; the matcher's covering index is
  `(WorldServerID, SceneType, SceneName, Difficulty, Status, TimeCreated)`, and `LastPulse` is
  indexed for the stale-row reaper. Rows cascade with the character.
- **`Scene/Arena/`** — five tables. `arena_match` has a unique index on `InstanceID` (the hosting
  scene server resolves the match from the instance it loaded); `arena_match_member` is unique on
  `(MatchID, CharacterID)`; `arena_season` uses a **partial** unique index
  (`HasFilter("active = TRUE")`) so at most one season is active; `arena_rating` is unique on
  `(SeasonID, CharacterID)` with a `(SeasonID, Rating)` leaderboard index and defaults of `1500`
  rating / `1500` peak; `arena_penalty` is the deserter lock, one row per character.
- **`Scene/Housing/`** — five tables. `plots` is unique on
  `(WorldServerID, SceneName, PlotKey)` — plots are world-scoped, and `PlotKey` is capped at 64 to
  match `PlotIdentity.MaxPlotKeyLength` — with a filtered unique index
  `ix_plots_one_house_per_character` enforcing one house per character and a filtered `TaxDueUtc`
  index for the tax sweep. `plot_structures`, `plot_access` and `plot_updates` cascade from the
  plot; `plot_vault` deliberately has **no** foreign key to `plots` (it holds what a character is
  owed after the plot is gone) and cascades from the character instead.
