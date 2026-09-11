using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// A character as an operator needs to see it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately not <see cref="CharacterData"/>. That type is the game's transfer shape and
	/// carries neither the soft-delete columns nor the session lease, because the game never
	/// loads a deleted character and a server already knows whether it holds the lease itself.
	/// An operator needs exactly those two things: whether the row is deleted, and whether
	/// somebody is holding it.
	/// </para>
	/// <para>
	/// It carries no sub-entity data. Attributes, items and the rest are separate queries, and
	/// loading them for a list of search results would be a join per row for data no list shows.
	/// </para>
	/// </remarks>
	public sealed class CharacterAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Display name, with the deletion marker stripped when the row is deleted.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The name as the row actually stores it, which for a deleted character carries the
		/// <c>_DELETED_</c> marker and a GUID. Shown so an operator can tell the two apart.
		/// </summary>
		public string StoredName { get; set; }

		/// <summary>Owning account name.</summary>
		public string Account { get; set; }

		/// <summary>Level.</summary>
		public int Level { get; set; }

		/// <summary>Race template ID. The panel has no template table, so it shows the ID.</summary>
		public int RaceID { get; set; }

		/// <summary>The character's own access level, separate from the account's.</summary>
		public byte AccessLevel { get; set; }

		/// <summary>Whether this is the account's selected character.</summary>
		public bool Selected { get; set; }

		/// <summary>Whether the row is soft-deleted.</summary>
		public bool Deleted { get; set; }

		/// <summary>When it was soft-deleted, if it was.</summary>
		public DateTime? TimeDeleted { get; set; }

		/// <summary>0 offline, 1 online. Mirrors <c>CharacterSessionState</c>.</summary>
		public int SessionState { get; set; }

		/// <summary>The scene server holding the lease, or 0.</summary>
		public long SessionOwnerServerID { get; set; }

		/// <summary>When the lease lapses. The epoch means no lease was ever taken.</summary>
		public DateTime SessionLeaseExpiresUtc { get; set; }

		/// <summary>The world server the character last belonged to.</summary>
		public long WorldServerID { get; set; }

		/// <summary>Current scene.</summary>
		public string SceneName { get; set; }

		/// <summary>Respawn scene.</summary>
		public string BindScene { get; set; }

		/// <summary>Position.</summary>
		public float X { get; set; }

		/// <summary>Position.</summary>
		public float Y { get; set; }

		/// <summary>Position.</summary>
		public float Z { get; set; }

		/// <summary>Row version, which the persistence layer moves on every save.</summary>
		public long Version { get; set; }

		/// <summary>When the character was created.</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>When the row was last written.</summary>
		public DateTime LastSaved { get; set; }
	}

	/// <summary>
	/// The fields an operator may change on an existing character.
	/// </summary>
	/// <remarks>
	/// Every property is nullable and null means "leave alone", so a form that submits three
	/// fields cannot blank the other four. The name is not here: renaming is its own method
	/// because the unique index makes a collision a distinct outcome the caller must handle.
	/// </remarks>
	public sealed class CharacterAdminEdit
	{
		/// <summary>New X, or null.</summary>
		public float? X { get; set; }

		/// <summary>New Y, or null.</summary>
		public float? Y { get; set; }

		/// <summary>New Z, or null.</summary>
		public float? Z { get; set; }

		/// <summary>New current scene, or null.</summary>
		public string SceneName { get; set; }

		/// <summary>New respawn scene, or null.</summary>
		public string BindScene { get; set; }

		/// <summary>New level, or null.</summary>
		public int? Level { get; set; }

		/// <summary>New character access level, or null.</summary>
		public byte? AccessLevel { get; set; }

		/// <summary>Whether anything at all is set.</summary>
		public bool IsEmpty =>
			X == null && Y == null && Z == null &&
			SceneName == null && BindScene == null &&
			Level == null && AccessLevel == null;
	}

	/// <summary>One page of <see cref="CharacterAdminData"/>, with the total the pager needs.</summary>
	public sealed class CharacterAdminPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<CharacterAdminData> Items { get; set; } = Array.Empty<CharacterAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter, across all pages.</summary>
		public int TotalCount { get; set; }
	}
}
