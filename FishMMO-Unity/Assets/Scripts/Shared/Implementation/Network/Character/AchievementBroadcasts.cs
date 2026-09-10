using FishNet.Broadcast;
using FishNet.Serializing;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating a single achievement for a character.
	/// Contains the achievement template ID, value, and tier.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct AchievementUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the achievement to update.</summary>
		public int TemplateID;
		/// <summary>Current value or progress for the achievement.</summary>
		public uint Value;
		/// <summary>Current tier or level of the achievement.</summary>
		public byte Tier;
	}

	/// <summary>Wire format for <see cref="AchievementUpdateBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for one field. <c>TemplateID</c> is a deterministic 32-bit hash
	/// (<c>CachedScriptableObject.AddToCache</c>), so it spans the whole signed range and FishNet's
	/// signed-packed form spends FIVE bytes where unpacked spends exactly four. The login sync
	/// ships every achievement the character has progress on in one
	/// <see cref="AchievementUpdateMultipleBroadcast"/>, and progress messages then arrive
	/// throughout play. <c>Value</c> stays packed — it is an unsigned progress count and small, and
	/// unsigned packing has no zigzag to defeat it — and <c>Tier</c> is already a single byte.
	/// </remarks>
	public static class AchievementUpdateBroadcastSerializer
	{
		/// <summary>Writes an <see cref="AchievementUpdateBroadcast"/>.</summary>
		public static void WriteAchievementUpdateBroadcast(this Writer writer, AchievementUpdateBroadcast value)
		{
			writer.WriteInt32Unpacked(value.TemplateID);
			writer.WriteUInt32(value.Value);
			writer.WriteUInt8Unpacked(value.Tier);
		}

		/// <summary>Reads an <see cref="AchievementUpdateBroadcast"/>.</summary>
		public static AchievementUpdateBroadcast ReadAchievementUpdateBroadcast(this Reader reader)
		{
			return new AchievementUpdateBroadcast()
			{
				TemplateID = reader.ReadInt32Unpacked(),
				Value = reader.ReadUInt32(),
				Tier = reader.ReadUInt8Unpacked(),
			};
		}
	}

	/// <summary>
	/// Broadcast for updating multiple achievements for a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct AchievementUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of achievements to update.</summary>
		public AchievementUpdateBroadcast[] Achievements;
	}
}