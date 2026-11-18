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
		/// <summary>Upper bound accepted for an achievement array length, against a corrupt stream.</summary>
		/// <remarks>
		/// <para>
		/// Registry-bounded. One entry is the progress on one <c>AchievementTemplate</c>, and a
		/// character holds at most one <c>Achievement</c> per template — <c>IAchievementController</c>
		/// keys them by template — so the largest legitimate array, the whole set sent at login in
		/// one <see cref="AchievementUpdateMultipleBroadcast"/>, cannot exceed the number of
		/// achievement templates authored in the project. That is currently a handful of assets
		/// under <c>Assets/Templates/Entity/Achievements</c>.
		/// </para>
		/// <para>
		/// No constant caps the template count, so there is nothing exact to reference and this is a
		/// deliberately generous ceiling rather than a derived limit. Achievements are also the
		/// category most likely to be added to in bulk — they tend to arrive a hundred at a time
		/// with a content patch — which is precisely why the number is three orders of magnitude
		/// above today's content instead of snug against it. Truncating this array would show a
		/// player their achievement list with entries missing and their progress apparently reset,
		/// which is worse than the allocation being prevented.
		/// </para>
		/// <para>
		/// Server → client, so the honest threat is a mangled or truncated stream — a reader that
		/// has lost alignment reading some other field as a length prefix — not a hostile client,
		/// which cannot send this message to itself.
		/// </para>
		/// </remarks>
		public const int MaxAchievements = 4096;

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
		/// <remarks>
		/// A length past <see cref="MaxAchievements"/> cannot be allocated and cannot be
		/// resynchronised past either — the entries behind it are only locatable by trusting the
		/// count just rejected — so the array comes back empty and no progress is applied. Only the
		/// sender's own -1, which is not a length at all, means null.
		/// </remarks>
		public static AchievementUpdateBroadcast[] ReadAchievementUpdateBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}
			if (length > MaxAchievements)
			{
				return System.Array.Empty<AchievementUpdateBroadcast>();
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