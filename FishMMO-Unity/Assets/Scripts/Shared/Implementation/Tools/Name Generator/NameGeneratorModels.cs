using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>Category of title to generate.</summary>
	public enum TitleType { Any, Honorific, Epithet, Rank, Legend }

	/// <summary>A generated character entry: given name, optional family name and title, and the derived meaning.</summary>
	public class CharacterEntry
	{
		public string Name { get; set; }
		/// <summary>Optional second name drawn from the same phonology; empty when not requested.</summary>
		public string FamilyName { get; set; }
		public string Title { get; set; }
		public string Meaning { get; set; }
		public string Race { get; set; }
		public string TitleCategory { get; set; }
		public List<string> NameFragments { get; set; } = new();

		/// <summary>Given name plus family name when there is one.</summary>
		public string FullName =>
			string.IsNullOrEmpty(FamilyName) ? Name : $"{Name} {FamilyName}";

		/// <summary>Full name plus title when there is one.</summary>
		public string FullTitle =>
			string.IsNullOrEmpty(Title) ? FullName : $"{FullName}, {Title}";

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => FullTitle;
	}

	/// <summary>Phonology for a single race or cultural variant.</summary>
	public class RacePhonology
	{
		public string[] Onsets { get; set; }
		public string[] Nuclei { get; set; }
		public string[] Codas { get; set; }
		public string[] Middles { get; set; }
		public int SyllMin { get; set; }
		public int SyllMax { get; set; }
		public string[] FeminineSuffixes { get; set; }
		public string[] MasculineSuffixes { get; set; }
		public string Description { get; set; }
		public string[] Tags { get; set; }
		/// <summary>Weighted codas — when set, replaces Codas with weighted selection.</summary>
		public (string item, int weight)[] WeightedCodas { get; set; }
	}

	/// <summary>A generated city entry.</summary>
	public class CityNameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Race { get; set; }
		public string CityType { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}

	/// <summary>City type categories for generation.</summary>
	public enum CityType { Any, Capital, Fortress, Village, Port, Sacred, Ruin }

	/// <summary>Title arrays organized by category for a single race.</summary>
	public class RaceTitles
	{
		public string[] Honorific { get; set; }
		public string[] Epithet { get; set; }
		public string[] Rank { get; set; }
		public string[] Legend { get; set; }
	}

	/// <summary>City suffix sets for a single race.</summary>
	public class RaceCitySuffixes
	{
		public string[] Capital { get; set; }
		public string[] Fortress { get; set; }
		public string[] Village { get; set; }
		public string[] Port { get; set; }
		public string[] Sacred { get; set; }
		public string[] Ruin { get; set; }
	}

	/// <summary>A generated dungeon entry.</summary>
	public class DungeonNameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Biome { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}

	/// <summary>A generated point-of-interest entry.</summary>
	public class POINameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Biome { get; set; }
		public string POIType { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}

	/// <summary>POI category types for generation.</summary>
	public enum POIType
	{
		Any, Landmark, Camp, Shrine, Tower, Bridge,
		Clearing, Spring, Cave, Monument, Wreck
	}

	/// <summary>Phonology + vocabulary for a biome.</summary>
	public class BiomePhonology
	{
		public string[] Onsets { get; set; }
		public string[] Nuclei { get; set; }
		public string[] Codas { get; set; }
		public string[] Middles { get; set; }
		public int SyllMin { get; set; }
		public int SyllMax { get; set; }
		public string[] DungeonSuffixes { get; set; }
		public string[] DungeonPrefixes { get; set; }
		public string[] POISuffixes { get; set; }
		public string[] Adjectives { get; set; }
		public string Description { get; set; }
	}

	/// <summary>Category of legendary item to generate.</summary>
	public enum ItemType { Any, Weapon, Armor, Artifact, Relic, Trinket }

	/// <summary>A generated legendary item entry.</summary>
	public class ItemNameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Race { get; set; }
		public string ItemCategory { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}
}
