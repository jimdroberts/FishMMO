using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Identifies a single claimed character session for a batched lease refresh.
	/// </summary>
	/// <remarks>
	/// Carries the full ownership triple rather than just the character ID so the
	/// refresh can verify the caller still owns the session it is extending. A server
	/// that lost the session (released it, or had it claimed away after its lease
	/// expired) must not be able to extend the new owner's lease.
	/// </remarks>
	public readonly struct CharacterSessionLeaseData
	{
		/// <summary>Character whose session lease should be extended.</summary>
		public readonly long CharacterID;

		/// <summary>Server that claims to own the session.</summary>
		public readonly long OwnerServerID;

		/// <summary>Ownership token returned by the original claim.</summary>
		public readonly Guid OwnerToken;

		/// <summary>
		/// Initializes a new lease-refresh entry.
		/// </summary>
		/// <param name="characterID">Character whose session lease should be extended.</param>
		/// <param name="ownerServerID">Server that claims to own the session.</param>
		/// <param name="ownerToken">Ownership token returned by the original claim.</param>
		public CharacterSessionLeaseData(long characterID, long ownerServerID, Guid ownerToken)
		{
			CharacterID = characterID;
			OwnerServerID = ownerServerID;
			OwnerToken = ownerToken;
		}

		/// <summary>
		/// Whether every field is populated well enough to be worth sending to the database.
		/// </summary>
		public bool IsValid => CharacterID > 0 && OwnerServerID > 0 && OwnerToken != Guid.Empty;
	}
}
