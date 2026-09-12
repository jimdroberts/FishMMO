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

		/// <summary>Writes an array of <see cref="AchievementUpdateBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for AchievementUpdateBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteAchievementUpdateBroadcastArray(this Writer writer, AchievementUpdateBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteAchievementUpdateBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="AchievementUpdateBroadcast"/>.</summary>
		public static AchievementUpdateBroadcast[] ReadAchievementUpdateBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}

			AchievementUpdateBroadcast[] value = new AchievementUpdateBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadAchievementUpdateBroadcast();
			}

			return value;
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