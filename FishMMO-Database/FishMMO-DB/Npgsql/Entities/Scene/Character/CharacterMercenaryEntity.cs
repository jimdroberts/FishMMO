using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A character's hired mercenary (the Mercenaries module). See <see cref="Data.CharacterMercenaryData"/> for the shape
	/// and its reasons.
	/// </summary>
	public class CharacterMercenaryEntity
	{
		/// <summary>Owning character; the primary key (one mercenary per character).</summary>
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }

		/// <summary>The kit template hired.</summary>
		public int KitID { get; set; }

		/// <summary>The mercenary's name, as it was shown when hired.</summary>
		public string Name { get; set; }

		/// <summary>True while dismissed for now (resting): not out, holding no party seat, returnable for free.</summary>
		public bool Resting { get; set; }

		/// <summary>The stance its owner set.</summary>
		public byte Stance { get; set; }

		/// <summary>The NPC seed its look is drawn from.</summary>
		public int AppearanceSeed { get; set; }

		/// <summary>The name generator's seed, gender, biome and climate variant, so the name is the one hired.</summary>
		public int NameSeed { get; set; }
		public byte NameGender { get; set; }
		public int NameBiomeID { get; set; }
		public byte NameVariant { get; set; }

		/// <summary>The race of the one hired (its models and its name); 0 for the kit's own.</summary>
		public int RaceTemplateID { get; set; }

		/// <summary>Resource attributes (template IDs) and their current values, index for index.</summary>
		public int[] ResourceTemplateIDs { get; set; }
		public float[] ResourceValues { get; set; }

		/// <summary>Buffs, index for index: template, seconds left, seconds to the next tick, stacks, ticks done.</summary>
		public int[] BuffTemplateIDs { get; set; }
		public float[] BuffRemaining { get; set; }
		public float[] BuffTickTime { get; set; }
		public int[] BuffStacks { get; set; }
		public int[] BuffTickCount { get; set; }

		public DateTime TimeHired { get; set; }
		public DateTime TimeUpdated { get; set; }
	}
}
