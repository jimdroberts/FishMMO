using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// A character's hired mercenary (the Mercenaries module): which kit, under what name and look, whether it is resting,
	/// and the state it was saved in (resource values and buffs), so it comes back as it left.
	/// </summary>
	/// <remarks>
	/// <para><b>One row per character, replaced on every save.</b> A character may hire one mercenary at a time, so the
	/// character is the key. No version column: a character is resident on one scene server at a time, and that server's
	/// save runs on the character's keyed save lane before the session is released, so there is no second writer to
	/// arbitrate against (as for <see cref="CharacterMarkData"/>).</para>
	/// <para><b>No row means no mercenary.</b> A death releases it: the row is deleted. The gear is not here; it is the
	/// character's own items in the <see cref="ItemContainerType.MercenaryEquipment"/> container, which outlive any hire.</para>
	/// <para><b>The party seat is not here either.</b> It is on the owner's <c>character_party</c> row, so it lives and
	/// dies with the membership. A resting mercenary holds no seat.</para>
	/// </remarks>
	public sealed class CharacterMercenaryData
	{
		public long CharacterID;
		public int KitID;
		public string Name;
		public bool Resting;
		public byte Stance;
		public int AppearanceSeed;
		public int NameSeed;
		public byte NameGender;
		public int NameBiomeID;
		public byte NameVariant;
		public int RaceTemplateID;
		public int[] ResourceTemplateIDs = Array.Empty<int>();
		public float[] ResourceValues = Array.Empty<float>();
		public int[] BuffTemplateIDs = Array.Empty<int>();
		public float[] BuffRemaining = Array.Empty<float>();
		public float[] BuffTickTime = Array.Empty<float>();
		public int[] BuffStacks = Array.Empty<int>();
		public int[] BuffTickCount = Array.Empty<int>();

		/// <summary>A copy, so a snapshot taken on the main thread is never changed under the worker writing it.</summary>
		public CharacterMercenaryData Clone()
		{
			CharacterMercenaryData copy = (CharacterMercenaryData)MemberwiseClone();
			copy.ResourceTemplateIDs = (int[])(ResourceTemplateIDs ?? Array.Empty<int>()).Clone();
			copy.ResourceValues = (float[])(ResourceValues ?? Array.Empty<float>()).Clone();
			copy.BuffTemplateIDs = (int[])(BuffTemplateIDs ?? Array.Empty<int>()).Clone();
			copy.BuffRemaining = (float[])(BuffRemaining ?? Array.Empty<float>()).Clone();
			copy.BuffTickTime = (float[])(BuffTickTime ?? Array.Empty<float>()).Clone();
			copy.BuffStacks = (int[])(BuffStacks ?? Array.Empty<int>()).Clone();
			copy.BuffTickCount = (int[])(BuffTickCount ?? Array.Empty<int>()).Clone();
			return copy;
		}
	}
}
