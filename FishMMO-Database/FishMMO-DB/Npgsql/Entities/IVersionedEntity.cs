// FishMMO Database Entity Architecture Notes
//
// VERSIONING STRATEGIES
// ---------------------
// FishMMO uses two distinct versioning approaches depending on the entity's role:
//
// 1. IVersionedEntity (long Version, logical) -- used by all character sub-entities
//    (CharacterAbilityEntity, CharacterInventoryEntity, etc.).
//    The Version is an application-managed counter incremented by the writer on every save.
//    This enables: (a) duplicate replay detection (same Version rejected), (b) stale-write
//    prevention (only higher Versions overwrite), and (c) cross-node conflict resolution
//    when multiple servers may write the same entity. Each sub-entity maintains an independent
//    Version stream, so concurrent writes to different sub-entities of the same character
//    do not conflict.
//
// 2. uint Version mapped to PostgreSQL xmin system column (physical) -- used by "system"
//    entities such as AccountEntity, AuthTokenEntity, and LoginServerSigningKeyEntity.
//    xmin is the transaction ID of the most recent DML touching the row, automatically
//    maintained by PostgreSQL. No application code needs to read, increment, or compare it;
//    EF Core's concurrency token infrastructure handles conflict detection on save.
//    This is appropriate for rows with low write contention where automatic row-level
//    concurrency is sufficient.
//
// TRADEOFFS
// ---------
// - Logical versioning requires explicit read-compare-write cycles and Version plumbing
//   in the service layer, but enables sophisticated semantics (replay detection, cross-node
//   ordering).
// - xmin requires no application bookkeeping but offers no replay protection and signals
//   conflict only at SaveChanges time (narrower window of applicability for cross-node
//   coordination).
//
// DELETE BEHAVIOR PATTERNS
// ------------------------
// FishMMO uses three DeleteBehavior strategies in entity FK relationships:
//
// NoAction (manual cleanup) -- Used by virtually all character sub-entities
//   (Ability, Achievement, Attribute, Bank, Buff, Equipment, Faction, Friend,
//   Guild, Hotkey, Inventory, ItemCooldown, KnownAbility, Mail, Party, Pet,
//   PetAttribute, PetBuff, Quest, Skill, Archetype).
//   WHY: Characters are soft-deleted (Deleted flag, row remains in the DB), so
//   cascade-deleting sub-entities on character delete would lose data that may
//   be needed for undo or audit. Instead, the service layer explicitly soft-deletes
//   sub-entities (Version-gated UPDATE SET deleted=TRUE) in the character delete
//   flow. NoAction ensures the DB never silently removes sub-entity rows.
//
// Restrict -- Used by the Account FK on CharacterEntity.
//   WHY: An account must not be deletable while it still owns characters.
//   Restrict prevents the accidental removal of an account that has active
//   characters, enforcing referential integrity at the database level.
//
// Cascade -- Used by AuthTokenEntity (FKs to Account and LoginServer) and
//   TwoFactorRecoveryCodeEntity (FK to Account).
//   WHY: Auth tokens and recovery codes are ephemeral, disposable data that
//   should be cleaned up automatically when the parent account or login server
//   is removed. Cascade avoids leaving orphaned rows that would never be read.
//
// Guild and Party entities do NOT use soft-delete -- they are hard-deleted
//   (DELETE FROM) when a character is deleted because guild/party membership
//   is transient state that loses meaning once the character is gone.

namespace FishMMO.Database.Npgsql.Entities
{
	public interface IVersionedEntity
	{
		long ID { get; set; }
		long Version { get; set; }
	}
}