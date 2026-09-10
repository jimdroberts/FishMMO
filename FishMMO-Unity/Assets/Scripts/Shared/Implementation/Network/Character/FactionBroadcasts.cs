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