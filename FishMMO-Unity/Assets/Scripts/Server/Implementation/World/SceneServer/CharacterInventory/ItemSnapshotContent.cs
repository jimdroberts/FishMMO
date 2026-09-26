using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Decides whether a character's containers still say exactly what the last confirmed item
	/// snapshot wrote — the test that lets the periodic snapshot skip a character that has not
	/// changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A comparison of content, deliberately not a dirty flag.</b> A flag is only as good as the
	/// list of places that set it, and items change through more doors than any list would hold: the
	/// container primitives, a stack amount edited in place, an identity arriving from a write-back,
	/// a slot repaired by the snapshot itself. The snapshot exists precisely to backstop a write
	/// that some path forgot to make, so a skip that trusted every path to have raised a flag would
	/// switch the backstop off exactly where it is needed. Comparing what the containers hold with
	/// what the database was last confirmed to hold needs no path's co-operation.
	/// </para>
	/// <para>
	/// <b>The other half of the proof is in the journal.</b> The confirmed rows are only a
	/// description of the database while no later write has landed for the character, so
	/// <c>ItemWriteJournal</c> drops the confirmation the moment any later batch claims its sequence,
	/// and records one only while the snapshot's own sequence is still the character's newest.
	/// Everything that writes <c>character_item</c> for a resident character passes that claim.
	/// </para>
	/// <para>
	/// Versions are not compared: the snapshot bumps every item's version as it captures, so they
	/// never repeat, and they say nothing about what the row holds. An item with no identity never
	/// matches, so a character holding one is always written — that write is what issues the id.
	/// </para>
	/// </remarks>
	public static class ItemSnapshotContent
	{
		/// <summary>
		/// True when <paramref name="current"/> states exactly the rows <paramref name="confirmed"/>
		/// does, for the same containers, in the same order.
		/// </summary>
		/// <remarks>
		/// Order-sensitive on purpose. Both lists come from the same walk — inventory, bank,
		/// equipment, each by slot — so equal content in a different order would itself be a sign
		/// that something moved.
		/// </remarks>
		/// <param name="confirmedContainers">The containers the confirmed snapshot spoke for.</param>
		/// <param name="confirmed">The rows it wrote, with the identities the database issued.</param>
		/// <param name="currentContainers">The containers the character has now.</param>
		/// <param name="current">The rows its containers would produce now.</param>
		/// <returns>True only when a snapshot now would write nothing different.</returns>
		public static bool Matches(
			IReadOnlyList<ItemContainerType> confirmedContainers,
			IReadOnlyList<CharacterItemData> confirmed,
			IReadOnlyList<ItemContainerType> currentContainers,
			IReadOnlyList<CharacterItemData> current)
		{
			if (confirmedContainers == null || confirmed == null || currentContainers == null || current == null)
			{
				return false;
			}

			if (confirmedContainers.Count != currentContainers.Count || confirmed.Count != current.Count)
			{
				return false;
			}

			for (int i = 0; i < confirmedContainers.Count; ++i)
			{
				if (confirmedContainers[i] != currentContainers[i])
				{
					return false;
				}
			}

			for (int i = 0; i < confirmed.Count; ++i)
			{
				CharacterItemData a = confirmed[i];
				CharacterItemData b = current[i];

				// No identity means the database has not issued this item one yet: write it.
				if (a.ID <= 0 || b.ID <= 0)
				{
					return false;
				}

				if (a.ID != b.ID ||
					a.Container != b.Container ||
					a.Slot != b.Slot ||
					a.TemplateID != b.TemplateID ||
					a.Seed != b.Seed ||
					a.Amount != b.Amount)
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// The rows a snapshot wrote, restated with the identities the database issued for the rows
		/// that arrived without one.
		/// </summary>
		/// <remarks>
		/// Those are the identities the write-back hands the live items, so recording them is what lets
		/// the next comparison match once the write-back has landed — rather than finding an item with
		/// no identity in the record and writing the whole character again for it.
		/// </remarks>
		/// <param name="written">The rows as sent.</param>
		/// <param name="assigned">The identities the write issued, by container and slot. May be null.</param>
		/// <returns>A new list; <paramref name="written"/> is not modified.</returns>
		public static List<CharacterItemData> WithIssuedIdentities(
			IReadOnlyList<CharacterItemData> written,
			IReadOnlyList<CharacterItemIdAssignment> assigned)
		{
			var rows = new List<CharacterItemData>(written?.Count ?? 0);
			if (written == null)
			{
				return rows;
			}

			Dictionary<(ItemContainerType, int), long> issued = null;
			if (assigned != null && assigned.Count > 0)
			{
				issued = new Dictionary<(ItemContainerType, int), long>(assigned.Count);
				for (int i = 0; i < assigned.Count; ++i)
				{
					issued[(assigned[i].Container, assigned[i].Slot)] = assigned[i].ID;
				}
			}

			for (int i = 0; i < written.Count; ++i)
			{
				CharacterItemData row = written[i];
				if (row.ID <= 0 && issued != null && issued.TryGetValue((row.Container, row.Slot), out long id))
				{
					row = new CharacterItemData(
						id: id,
						version: row.Version,
						characterID: row.CharacterID,
						container: row.Container,
						templateID: row.TemplateID,
						slot: row.Slot,
						seed: row.Seed,
						amount: row.Amount);
				}
				rows.Add(row);
			}
			return rows;
		}
	}
}
