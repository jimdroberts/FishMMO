using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a player character.
	/// </summary>
	public class CharacterEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Character display name.</summary>
		/// <remarks>
		/// [COLLATE NOCASE for case insensitive compare. this way we can't both create 'Archer' and 'archer' as characters]
		/// note: the collation has been added in the DbContext OnModelCreating function since the Postgres provider
		/// doesn't support collations via attributes
		/// </remarks>
		public string Name { get; set; }
		/// <summary>Lowercase copy of <see cref="Name"/> for case-insensitive lookups.</summary>
		public string NameLowercase { get; set; }
		/// <summary>Account name that owns this character.</summary>
		public string Account { get; set; }
		/// <summary>Whether this character is the currently selected/active character for the account.</summary>
		public bool Selected { get; set; }
		/// <summary>World server ID this character belongs to.</summary>
		public long WorldServerID { get; set; }
		/// <summary>Current scene name the character is in.</summary>
		public string SceneName { get; set; }
		/// <summary>Current scene handle the character is in.</summary>
		public long SceneHandle { get; set; }
		/// <summary>Bind point scene name.</summary>
		public string BindScene { get; set; }
		/// <summary>
		/// Bind point world position X.
		/// Uses <c>float</c> for Unity <c>Vector3</c> compatibility.
		/// </summary>
		public float BindX { get; set; }
		/// <summary>Bind point world position Y.</summary>
		public float BindY { get; set; }
		/// <summary>Bind point world position Z.</summary>
		public float BindZ { get; set; }
		/// <summary>Instance ID of the current instance the character occupies (0 if over-world).</summary>
		public long InstanceID { get; set; }
		/// <summary>
		/// Instance spawn position X.
		/// Uses <c>float</c> for Unity <c>Vector3</c> compatibility.
		/// </summary>
		public float InstanceX { get; set; }
		/// <summary>Instance spawn position Y.</summary>
		public float InstanceY { get; set; }
		/// <summary>Instance spawn position Z.</summary>
		public float InstanceZ { get; set; }
		/// <summary>Instance spawn rotation X component.</summary>
		public float InstanceRotX { get; set; }
		/// <summary>Instance spawn rotation Y component.</summary>
		public float InstanceRotY { get; set; }
		/// <summary>Instance spawn rotation Z component.</summary>
		public float InstanceRotZ { get; set; }
		/// <summary>Instance spawn rotation W component (quaternion).</summary>
		public float InstanceRotW { get; set; }
		/// <summary>Character race template ID.</summary>
		public int RaceID { get; set; }
		/// <summary>
		/// Character level.
		/// </summary>
		/// <remarks>
		/// This column is the AUTHORITATIVE home of a character's level, and it is new.
		///
		/// Before it, level did not exist anywhere in FishMMO: no column, no attribute template,
		/// no experience curve, no UI. The guild roster's level column (AREAS E2) could therefore
		/// not be "joined to wherever level is authoritative", because there was nowhere to join
		/// to — so a home was made here, on the character row, next to <see cref="RaceID"/>, which
		/// is the other per-character fact the roster reads.
		///
		/// It is deliberately NOT a denormalised copy of anything: there is no other store to
		/// drift from. Until a progression system exists to write it, every character reads back
		/// the default of 1, and the roster renders 1. That is a truthful "no progression yet",
		/// not a stale cache.
		/// </remarks>
		public int Level { get; set; }
		/// <summary>Character model/prefab index.</summary>
		public int ModelIndex { get; set; }
		/// <summary>
		/// Current world position X.
		/// Uses <c>float</c> for direct compatibility with Unity's <c>Vector3</c>.
		/// Precision is approximately 1.5 cm at 10 km from origin; suitable
		/// for most game worlds but may exhibit jitter past ~100 km.
		/// </summary>
		public float X { get; set; }
		/// <summary>Current world position Y.</summary>
		public float Y { get; set; }
		/// <summary>Current world position Z.</summary>
		public float Z { get; set; }
		/// <summary>Current rotation X component.</summary>
		public float RotX { get; set; }
		/// <summary>Current rotation Y component.</summary>
		public float RotY { get; set; }
		/// <summary>Current rotation Z component.</summary>
		public float RotZ { get; set; }
		/// <summary>Current rotation W component (quaternion).</summary>
		public float RotW { get; set; }
		/// <summary>Access level / permission tier for this character.</summary>
		public byte AccessLevel { get; set; }

		/// <summary>
		/// Distributed session state used to coordinate ownership/claims across servers.
		/// </summary>
		public CharacterSessionState SessionState { get; set; }

		/// <summary>
		/// Current owning Scene Server instance ID (0 means unowned).
		/// </summary>
		public long SessionOwnerServerId { get; set; }

		/// <summary>
		/// Ownership token that changes every successful claim (Guid.Empty means unowned).
		/// Used to prevent stale releases/transitions.
		/// </summary>
		public Guid SessionOwnerToken { get; set; }

		/// <summary>
		/// Lease expiry timestamp for dead-server recovery.
		/// A server should extend this periodically while it still owns the character.
		/// </summary>
		public DateTime SessionLeaseExpiresUtc { get; set; }

		/// <summary>
		/// When this character last switched open-world channel (UTC), or
		/// <see cref="DateTime.MinValue"/> if it never has.
		/// </summary>
		/// <remarks>
		/// Persisted rather than held in memory because a channel switch is implemented as a
		/// disconnect: the character is released here and re-routed by the world server, quite
		/// possibly to a different scene server. Any cooldown kept per connection — or even per
		/// process — is therefore reset by the very act it is meant to limit, and only ever
		/// throttles switches that <em>failed</em>. This is the only place the limit can live
		/// and still mean anything.
		/// </remarks>
		public DateTime LastChannelSwitchUtc { get; set; }

		/// <summary>Character flags bitmask (e.g. for status effects or customization flags).</summary>
		public int Flags { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Timestamp of the last save operation (UTC).</summary>
		public DateTime LastSaved { get; set; }
		/// <summary>Soft delete flag.</summary>
		public bool Deleted { get; set; }
		/// <summary>Timestamp when the character was soft-deleted (UTC), or null if not deleted.</summary>
		public DateTime? TimeDeleted { get; set; }

		// foreign keys
		/// <summary>Navigation collection of character ability entries.</summary>
		public ICollection<CharacterAbilityEntity> Abilities { get; set; }
		/// <summary>Navigation collection of character known ability entries.</summary>
		public ICollection<CharacterKnownAbilityEntity> KnownAbilities { get; set; }
		/// <summary>Navigation collection of character attribute entries.</summary>
		public ICollection<CharacterAttributeEntity> Attributes { get; set; }
		/// <summary>Navigation collection of character achievements.</summary>
		public ICollection<CharacterAchievementEntity> Achievements { get; set; }
		/// <summary>Navigation collection of active character buffs.</summary>
		public ICollection<CharacterBuffEntity> Buffs { get; set; }
		/// <summary>Navigation collection of character inventory slots.</summary>
		public ICollection<CharacterInventoryEntity> Inventory { get; set; }
		/// <summary>Navigation collection of character equipment slots.</summary>
		public ICollection<CharacterEquipmentEntity> Equipment { get; set; }
		/// <summary>Navigation collection of character bank/chest slots.</summary>
		public ICollection<CharacterBankEntity> Bank { get; set; }
		/// <summary>Navigation reference to the guild membership record.</summary>
		public CharacterGuildEntity Guild { get; set; }
		/// <summary>Navigation reference to the party membership record.</summary>
		public CharacterPartyEntity Party { get; set; }
		/// <summary>Navigation collection of character faction standings.</summary>
		public ICollection<CharacterFactionEntity> Faction { get; set; }
		/// <summary>Navigation collection of character friend entries.</summary>
		public ICollection<CharacterFriendEntity> Friends { get; set; }
		/// <summary>Navigation collection of character item cooldowns.</summary>
		public ICollection<CharacterItemCooldownEntity> ItemCooldowns { get; set; }
		/// <summary>Navigation collection of pet buff entries.</summary>
		public ICollection<CharacterPetBuffEntity> PetBuffs { get; set; }
		/// <summary>Navigation reference to the character's pet.</summary>
		public CharacterPetEntity Pet { get; set; }
		/// <summary>Navigation collection of character quest progress entries.</summary>
		public ICollection<CharacterQuestEntity> Quests { get; set; }
		/// <summary>Navigation collection of character skill entries.</summary>
		public ICollection<CharacterSkillEntity> Skills { get; set; }
		/// <summary>Navigation collection of character archetype entries.</summary>
		public ICollection<CharacterArchetypeEntity> Archetypes { get; set; }
	}
}