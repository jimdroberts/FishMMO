using FishNet.Broadcast;
using FishNet.Serializing;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating a single faction value for a character.
	/// Contains the faction template ID and the new value.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct FactionUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the faction to update.</summary>
		public int TemplateID;
		/// <summary>New value for the faction (e.g., reputation or standing).</summary>
		public int NewValue;
	}

	/// <summary>Wire format for <see cref="FactionUpdateBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for one field, and it is the most frequently paid template id in the game.
	/// <c>TemplateID</c> is a deterministic 32-bit hash (<c>CachedScriptableObject.AddToCache</c>),
	/// so it spans the whole signed range and FishNet's signed-packed form zigzags all but a
	/// sixteenth of it past 2^28 and spends FIVE bytes where unpacked spends exactly four.
	/// <c>FactionController.FlushDirtyFactionUpdates</c> sends every dirty faction to the owner AND
	/// to every observer of the character, and a single kill can dirty several factions at once —
	/// so that byte is paid per dirty faction per observer, continuously, during combat.
	/// <c>NewValue</c> stays packed: reputation is bounded and small, and packs to one or two bytes.
	/// </remarks>
	public static class FactionUpdateBroadcastSerializer
	{
		/// <summary>Upper bound accepted for a faction array length, against a corrupt stream.</summary>
		/// <remarks>
		/// <para>
		/// The tightest-anchored of these bounds. One entry is one standing against one
		/// <c>FactionTemplate</c>, and a character holds exactly one standing per template, so the
		/// largest legitimate array cannot exceed the number of faction templates authored in the
		/// project — currently a handful of assets under <c>Assets/Templates/Entity/Factions</c>.
		/// The realistic array is far smaller still: <c>FactionController.FlushDirtyFactionUpdates</c>
		/// builds it from <c>dirtyFactionTemplateIDs</c>, the factions that actually changed this
		/// flush, which is usually one or two.
		/// </para>
		/// <para>
		/// Even so the ceiling is left generous rather than snug. Factions are authored content with
		/// no constant capping their number, the login path does send a character's full standing
		/// set, and truncating it would leave a player hostile or neutral to factions they had
		/// actually earned standing with — a silently wrong world state, which is worse than the
		/// allocation the bound prevents. The bound only needs to be small enough that a corrupt
		/// length cannot become a multi-gigabyte array; at these element sizes 4096 already achieves
		/// that with room to spare.
		/// </para>
		/// <para>
		/// Server → client, so the honest threat is a mangled or truncated stream rather than a
		/// hostile client. Note that the observer variant of this data travels as
		/// <c>CharacterObserverFactionUpdateBroadcast</c> and is not read through here.
		/// </para>
		/// </remarks>
		public const int MaxFactions = 4096;

		/// <summary>Writes a <see cref="FactionUpdateBroadcast"/>.</summary>
		public static void WriteFactionUpdateBroadcast(this Writer writer, FactionUpdateBroadcast value)
		{
			writer.WriteInt32Unpacked(value.TemplateID);
			writer.WriteInt32(value.NewValue);
		}

		/// <summary>Reads a <see cref="FactionUpdateBroadcast"/>.</summary>
		public static FactionUpdateBroadcast ReadFactionUpdateBroadcast(this Reader reader)
		{
			return new FactionUpdateBroadcast()
			{
				TemplateID = reader.ReadInt32Unpacked(),
				NewValue = reader.ReadInt32(),
			};
		}

		/// <summary>Writes an array of <see cref="FactionUpdateBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for FactionUpdateBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteFactionUpdateBroadcastArray(this Writer writer, FactionUpdateBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteFactionUpdateBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="FactionUpdateBroadcast"/>.</summary>
		/// <remarks>
		/// A length past <see cref="MaxFactions"/> cannot be allocated and cannot be resynchronised
		/// past either — the entries behind it are only locatable by trusting the count just
		/// rejected — so the array comes back empty and no standing is applied. Only the sender's
		/// own -1, which is not a length at all, means null.
		/// </remarks>
		public static FactionUpdateBroadcast[] ReadFactionUpdateBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}
			if (length > MaxFactions)
			{
				return System.Array.Empty<FactionUpdateBroadcast>();
			}

			FactionUpdateBroadcast[] value = new FactionUpdateBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadFactionUpdateBroadcast();
			}

			return value;
		}
	}

	/// <summary>
	/// Broadcast for updating multiple faction values for a character at once.
	/// Used for bulk faction updates or synchronization.
	/// </summary>
	public struct FactionUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of faction updates to apply.</summary>
		public FactionUpdateBroadcast[] Factions;
	}

	/// <summary>
	/// Observer-targeted broadcast for updating faction values of a specific character.
	/// </summary>
	public struct CharacterObserverFactionUpdateBroadcast : IBroadcast
	{
		/// <summary>Target character ID to apply updates to.</summary>
		public long CharacterID;
		/// <summary>List of faction updates to apply to the target character.</summary>
		public FactionUpdateBroadcast[] Factions;
	}
}