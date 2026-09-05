using System.Collections.Generic;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Base for every generator request. The three seed fields decide determinism:
	/// with a <see cref="RegionSeed"/> the result is reproducible on any machine;
	/// without one the generator's own RNG drives the output.
	/// </summary>
	public abstract class GenerationRequest
	{
		/// <summary>Region / world key. When null the generator's own RNG is used.</summary>
		public string RegionSeed { get; set; }

		/// <summary>Per-object key (e.g. a spawn's GUID) for uniqueness within a region. Optional.</summary>
		public string ObjectSeed { get; set; }

		/// <summary>Batch index, mixed into the derived seed so every item of a seeded batch differs.</summary>
		public int? Index { get; set; }
	}

	public sealed class NameRequest : GenerationRequest
	{
		public string Race { get; set; }
		public string Culture { get; set; }
		/// <summary>Optional cross-race modifier key (e.g. "ashen", "bloodborn", "tidebound").</summary>
		public string Modifier { get; set; }
		public CharacterGender Gender { get; set; } = CharacterGender.Unspecified;
		public TitleType TitleType { get; set; } = TitleType.Any;
		/// <summary>Social register of the title: Civil for townsfolk and traders, Martial for soldiers, Mythic for legends.</summary>
		public TitleRegister Register { get; set; } = TitleRegister.Any;
		/// <summary>What this character does ("Banker"); fills the {profession} slot of Civil titles.</summary>
		public string Profession { get; set; }
		/// <summary>Longest title allowed, in characters; 0 is unlimited. Nameplates want about 32.</summary>
		public int MaxTitleLength { get; set; }
		/// <summary>Whether a second clause may be appended ("Lord of Ashford, the Bold").</summary>
		public bool AllowCompoundTitle { get; set; } = true;
		/// <summary>If true, output skips title generation.</summary>
		public bool NameOnly { get; set; }
		/// <summary>If true, output skips name generation.</summary>
		public bool TitleOnly { get; set; }
		/// <summary>If true, a second, ungendered name is drawn from the same phonology as a family name.</summary>
		public bool IncludeFamilyName { get; set; }
	}

	/// <summary>Fields shared by every request that names something from a biome.</summary>
	public abstract class BiomeGenerationRequest : GenerationRequest
	{
		/// <summary>Biome key ("volcanic"); ignored when <see cref="BiomeID"/> is set.</summary>
		public string Biome { get; set; }
		/// <summary>Biome by cached-object ID — what maps, affinities and [TemplateReference] fields carry.</summary>
		public int BiomeID { get; set; }
		/// <summary>Key of a climate variant on the biome or the scene's defaults ("frozen"); ignored when <see cref="Variant"/> is set.</summary>
		public string ClimateVariant { get; set; }
		/// <summary>The climate variant the place is read under right now; its adjectives and prefixes lead the name.</summary>
		public BiomeClimateVariant Variant { get; set; }

		public bool HasBiome => BiomeID != 0 || !string.IsNullOrEmpty(Biome);
	}

	public sealed class CityRequest : BiomeGenerationRequest
	{
		public string Race { get; set; }
		public string Culture { get; set; }
		public CityType CityType { get; set; } = CityType.Any;
		/// <summary>When no biome is given, draw one of the race's home biomes instead of naming without one.</summary>
		public bool UseRaceHomeBiome { get; set; } = true;
	}

	public sealed class DungeonRequest : BiomeGenerationRequest
	{
		/// <summary>Optional owning race, mixed into the seed so two races' dungeons in one biome differ.</summary>
		public string Race { get; set; }
	}

	public sealed class POIRequest : BiomeGenerationRequest
	{
		public POIType POIType { get; set; } = POIType.Any;
	}

	/// <summary>Request for legendary item name generation.</summary>
	public sealed class ItemRequest : GenerationRequest
	{
		/// <summary>Race whose phonology flavours the root syllables.</summary>
		public string Race { get; set; }
		/// <summary>Culture variant — narrows phonology within a race.</summary>
		public string Culture { get; set; }
		public ItemType ItemType { get; set; } = ItemType.Any;

		/// <summary>Optional library context — when set, the builder weaves existing
		/// character/city/dungeon/POI names into item names ("Blade of Aelion").</summary>
		public LibraryContext Library { get; set; }
	}

	/// <summary>Pre-extracted name lists used to ground generated item names in existing world content.</summary>
	public sealed class LibraryContext
	{
		public List<string> CharacterNames { get; set; }
		public List<string> CityNames { get; set; }
		public List<string> DungeonNames { get; set; }
		public List<string> POINames { get; set; }

		/// <summary>True when at least one name list is non-empty.</summary>
		public bool HasAny =>
			(CharacterNames != null && CharacterNames.Count > 0) ||
			(CityNames != null && CityNames.Count > 0) ||
			(DungeonNames != null && DungeonNames.Count > 0) ||
			(POINames != null && POINames.Count > 0);
	}

	public sealed class HybridRequest : GenerationRequest
	{
		public string RaceA { get; set; }
		public string RaceB { get; set; }
		public string CultureA { get; set; }
		public string CultureB { get; set; }
		public CharacterGender Gender { get; set; } = CharacterGender.Unspecified;
		public TitleType TitleType { get; set; } = TitleType.Any;
		public TitleRegister Register { get; set; } = TitleRegister.Any;
		public string Profession { get; set; }
		public int MaxTitleLength { get; set; }
		public bool AllowCompoundTitle { get; set; } = true;
		/// <summary>
		/// Bias toward <see cref="RaceA"/> in [0.0, 1.0]: 0 is fully RaceB, 1 is
		/// fully RaceA. Each candidate syllable from A passes with probability
		/// Dominance and each from B with 1 - Dominance; syllable bounds and the
		/// title race follow the same bias (A wins ties).
		/// </summary>
		public double Dominance { get; set; } = 0.5;
	}

	/// <summary>Outcome of a unique-name batch generation.</summary>
	public sealed class UniqueResult<T>
	{
		public List<T> Items { get; set; } = new();
		public int TargetCount { get; set; }
		public int Attempts { get; set; }
		/// <summary>True when fewer items than requested could be produced.</summary>
		public bool PoolExhausted => Items.Count < TargetCount;
	}
}
