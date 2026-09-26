namespace FishMMO.Database.Data
{
	/// <summary>
	/// One character row to write in a batched save, with the claim that authorises it.
	/// </summary>
	/// <remarks>
	/// The batched sibling of calling <c>PersistOwnedAsync</c> or <c>PersistAsync</c> once per
	/// character. <see cref="Ownership"/> decides which of the two a row behaves as: with a claim the
	/// write additionally requires that the row is still claimed by that server under that token;
	/// without one it is guarded by the monotonic version alone.
	/// </remarks>
	public readonly struct CharacterPersistRequest
	{
		/// <summary>The snapshot to write. Its <c>Version</c> must exceed the stored version.</summary>
		public readonly CharacterData Data;

		/// <summary>The claim this server holds for the character, or null to write ungated.</summary>
		public readonly CharacterSessionLeaseData? Ownership;

		/// <summary>
		/// Initializes a new batched-save entry.
		/// </summary>
		/// <param name="data">The snapshot to write.</param>
		/// <param name="ownership">The claim this server holds, or null to write ungated.</param>
		public CharacterPersistRequest(CharacterData data, CharacterSessionLeaseData? ownership)
		{
			Data = data;
			Ownership = ownership;
		}
	}
}
