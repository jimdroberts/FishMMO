using FishNet.Broadcast;
using FishNet.Serializing;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating the owner's archetype.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct ArchetypeUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the archetype to apply.</summary>
		public int TemplateID;
	}

	/// <summary>Wire format for <see cref="ArchetypeUpdateBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for one field. <c>TemplateID</c> is a deterministic 32-bit hash
	/// (<c>CachedScriptableObject.AddToCache</c>), so it spans the whole signed range and FishNet's
	/// signed-packed form zigzags all but a sixteenth of it past 2^28 and spends FIVE bytes where
	/// unpacked spends exactly four.
	/// </remarks>
	public static class ArchetypeUpdateBroadcastSerializer
	{
		/// <summary>Writes an <see cref="ArchetypeUpdateBroadcast"/>.</summary>
		public static void WriteArchetypeUpdateBroadcast(this Writer writer, ArchetypeUpdateBroadcast value)
		{
			writer.WriteInt32Unpacked(value.TemplateID);
		}

		/// <summary>Reads an <see cref="ArchetypeUpdateBroadcast"/>.</summary>
		public static ArchetypeUpdateBroadcast ReadArchetypeUpdateBroadcast(this Reader reader)
		{
			return new ArchetypeUpdateBroadcast()
			{
				TemplateID = reader.ReadInt32Unpacked(),
			};
		}
	}

	/// <summary>
	/// Observer-targeted broadcast for updating archetype of a specific character.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct CharacterObserverArchetypeUpdateBroadcast : IBroadcast
	{
		/// <summary>Target character ID to apply updates to.</summary>
		public long CharacterID;
		/// <summary>Template ID of the archetype to apply.</summary>
		public int TemplateID;
	}

	/// <summary>Wire format for <see cref="CharacterObserverArchetypeUpdateBroadcast"/>.</summary>
	/// <remarks>
	/// The observer-facing twin of <see cref="ArchetypeUpdateBroadcastSerializer"/>, and the one
	/// that is sent per observer: <c>TemplateID</c> is a full-range deterministic hash, five packed
	/// bytes against four unpacked. <c>CharacterID</c> stays packed — it is a database sequence
	/// value and small, and unpacking a <c>long</c> would COST six or seven bytes.
	/// </remarks>
	public static class CharacterObserverArchetypeUpdateBroadcastSerializer
	{
		/// <summary>Writes a <see cref="CharacterObserverArchetypeUpdateBroadcast"/>.</summary>
		public static void WriteCharacterObserverArchetypeUpdateBroadcast(this Writer writer, CharacterObserverArchetypeUpdateBroadcast value)
		{
			writer.WriteInt64(value.CharacterID);
			writer.WriteInt32Unpacked(value.TemplateID);
		}

		/// <summary>Reads a <see cref="CharacterObserverArchetypeUpdateBroadcast"/>.</summary>
		public static CharacterObserverArchetypeUpdateBroadcast ReadCharacterObserverArchetypeUpdateBroadcast(this Reader reader)
		{
			return new CharacterObserverArchetypeUpdateBroadcast()
			{
				CharacterID = reader.ReadInt64(),
				TemplateID = reader.ReadInt32Unpacked(),
			};
		}
	}
}