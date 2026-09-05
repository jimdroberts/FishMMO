using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>Which generator a <see cref="SceneObjectNamer"/> draws its name from.</summary>
	public enum SceneObjectNamingMode : byte
	{
		/// <summary>A person: given name, optional family name and title, from the object's race.</summary>
		Character = 0,
		/// <summary>A settlement name from the object's race, optionally flavoured by a biome.</summary>
		City = 1,
		/// <summary>A dungeon name from a biome.</summary>
		Dungeon = 2,
		/// <summary>A landmark, shrine, camp or similar from a biome.</summary>
		PointOfInterest = 3,
		/// <summary>A legendary item name from the object's race.</summary>
		Item = 4,
	}

	/// <summary>How the gender behind a character name is chosen.</summary>
	public enum NamingGenderPolicy : byte
	{
		/// <summary>Random among the genders the race has models for; random between Male and Female when it has no gendered sets.</summary>
		RaceModels = 0,
		/// <summary>Random between Male and Female regardless of models.</summary>
		Random = 1,
		Male = 2,
		Female = 3,
		/// <summary>No gender lean; the name takes no gendered suffix.</summary>
		Unspecified = 4,
	}

	/// <summary>Whether a character name carries a title, and of which kind.</summary>
	public enum NamingTitlePolicy : byte
	{
		None = 0,
		/// <summary>A category chosen by the generator from the name's meaning.</summary>
		Random = 1,
		Honorific = 2,
		Epithet = 3,
		Rank = 4,
		Legend = 5,
	}

	/// <summary>Shape of a character name.</summary>
	public enum CharacterNameFormat : byte
	{
		/// <summary>"Toriton"</summary>
		Given = 0,
		/// <summary>"Toriton Feruald" — a second, ungendered draw from the same phonology.</summary>
		GivenAndFamily = 1,
	}

	/// <summary>
	/// Everything a designer sets on a <see cref="SceneObjectNamer"/>. Authored on
	/// the prefab, so it is identical on both peers and never travels over the wire.
	/// </summary>
	[Serializable]
	public class SceneObjectNamingSettings
	{
		[Tooltip("Which generator the name comes from.")]
		public SceneObjectNamingMode Mode = SceneObjectNamingMode.Character;

		[Header("Race")]
		[Tooltip("Race to name from. Leave unset to use the object's FactionController race.")]
		[TemplateReference(typeof(RaceTemplate))]
		public int RaceOverrideID;
		[Tooltip("Optional culture key within the race, e.g. 'nordic'. Unknown keys fall back to the race's own phonology.")]
		public string Culture;
		[Tooltip("Optional cross-race flavour layered on the phonology.")]
		[TemplateReference(typeof(NameModifierTemplate))]
		public int ModifierID;

		[Header("Character")]
		public NamingGenderPolicy GenderPolicy = NamingGenderPolicy.RaceModels;
		public CharacterNameFormat NameFormat = CharacterNameFormat.GivenAndFamily;
		public NamingTitlePolicy TitlePolicy = NamingTitlePolicy.None;

		[Header("Title")]
		[Tooltip("Social register of the title: Civil for townsfolk and traders, Martial for soldiers, Mythic for legends.")]
		public TitleRegister Register = TitleRegister.Civil;
		[Tooltip("What this character does, e.g. 'Banker'. Empty uses the Interactable's title when the object has one.")]
		public string Profession;
		[Tooltip("Longest title allowed on the nameplate; 0 is unlimited.")]
		public int MaxTitleLength = 32;
		[Tooltip("Whether a second clause may be appended ('Lord of Ashford, the Bold'). Off keeps nameplates short.")]
		public bool AllowCompoundTitle;

		[Header("Places and items")]
		[Tooltip("Biome for Dungeon and Point of Interest names and flavour for City names. Leave unset to read the scene's biome map at the object's position.")]
		[TemplateReference(typeof(BiomeTemplate))]
		public int BiomeID;
		public CityType CityType = CityType.Any;
		public POIType POIType = POIType.Any;
		public ItemType ItemType = ItemType.Any;

		[Header("Determinism")]
		[Tooltip("When set, the name is derived from this and the object seed, so it is the same every time the server starts. Leave empty for a fresh name per spawn.")]
		public string RegionSeed;
		[Tooltip("Distinguishes objects sharing a region seed. Defaults to the object's authored name.")]
		public string ObjectSeed;

		/// <summary>True for the modes that name things from a race rather than a biome.</summary>
		public bool UsesRace =>
			Mode == SceneObjectNamingMode.Character
			|| Mode == SceneObjectNamingMode.City
			|| Mode == SceneObjectNamingMode.Item;

		/// <summary>True for the modes that cannot produce a name without a biome.</summary>
		public bool RequiresBiome =>
			Mode == SceneObjectNamingMode.Dungeon
			|| Mode == SceneObjectNamingMode.PointOfInterest;

		/// <summary>True for the modes whose name is coloured by a biome, required or not.</summary>
		public bool UsesBiome => RequiresBiome || Mode == SceneObjectNamingMode.City;
	}

	/// <summary>
	/// The pure half of scene-object naming: given settings, a race, a seed and a
	/// gender, produce the name. Both peers run exactly this from the same inputs,
	/// which is what lets the server ship a 4-byte seed instead of a string.
	/// </summary>
	public static class SceneObjectNameResolver
	{
		/// <summary>The race a namer draws from: the explicit override when set, else the faction's race.</summary>
		public static RaceTemplate ResolveRace(SceneObjectNamingSettings settings, IFactionController faction)
		{
			if (settings != null && settings.RaceOverrideID != 0)
			{
				if (RaceRegistry.TryGetByID(settings.RaceOverrideID, out RaceTemplate overrideRace))
				{
					return overrideRace;
				}
				overrideRace = RaceTemplate.Get<RaceTemplate>(settings.RaceOverrideID);
				if (overrideRace != null)
				{
					return overrideRace;
				}
			}
			return faction?.RaceTemplate;
		}

		/// <summary>
		/// Picks the gender for a name under the given policy. <see cref="NamingGenderPolicy.RaceModels"/>
		/// draws only among genders the race actually has models for, so a male-only race never
		/// receives a feminine name; a race with no gendered sets falls back to a coin flip.
		/// </summary>
		public static CharacterGender ResolveGender(NamingGenderPolicy policy, RaceTemplate race, DeterministicRNG rng)
		{
			switch (policy)
			{
				case NamingGenderPolicy.Male:
					return CharacterGender.Male;
				case NamingGenderPolicy.Female:
					return CharacterGender.Female;
				case NamingGenderPolicy.Unspecified:
					return CharacterGender.Unspecified;
				case NamingGenderPolicy.Random:
					return rng.Next(2) == 0 ? CharacterGender.Male : CharacterGender.Female;
				default:
				{
					bool hasMale = HasModels(race, CharacterGender.Male);
					bool hasFemale = HasModels(race, CharacterGender.Female);
					if (hasMale && hasFemale)
					{
						return rng.Next(2) == 0 ? CharacterGender.Male : CharacterGender.Female;
					}
					if (hasMale)
					{
						return CharacterGender.Male;
					}
					if (hasFemale)
					{
						return CharacterGender.Female;
					}
					return rng.Next(2) == 0 ? CharacterGender.Male : CharacterGender.Female;
				}
			}
		}

		private static bool HasModels(RaceTemplate race, CharacterGender gender)
		{
			if (race == null)
			{
				return false;
			}
			var models = race.GetModels(gender);
			return models != null && models.Count > 0;
		}

		/// <summary>
		/// The seed a name is generated from. With a region seed it is a stable hash of region +
		/// object, so the same object gets the same name on every server start; otherwise it is a
		/// fresh draw per spawn. Never zero — zero is the wire's "no generated name" sentinel.
		/// </summary>
		public static int DeriveSeed(SceneObjectNamingSettings settings, string fallbackObjectSeed)
		{
			if (settings != null && !string.IsNullOrEmpty(settings.RegionSeed))
			{
				string objectSeed = string.IsNullOrEmpty(settings.ObjectSeed) ? fallbackObjectSeed : settings.ObjectSeed;
				return StableHash.FoldSeed(StableHash.Seed(settings.RegionSeed, objectSeed ?? ""));
			}
			int seed = DeterministicRNG.Shared.Next();
			return seed == 0 ? 1 : seed;
		}

		/// <summary>An RNG for the gender roll, kept apart from the name draw so changing the gender policy does not reshuffle names.</summary>
		public static DeterministicRNG GenderRng(int seed)
		{
			return new DeterministicRNG(StableHash.FoldSeed(StableHash.Combine((ulong)(uint)seed, "gender")));
		}

		/// <summary>
		/// Builds the name. Returns false, with a reason, when the inputs cannot name the object — no
		/// templates loaded, no race for a race-driven mode, no biome for a biome-driven mode — so the
		/// caller can keep the authored name instead of guessing.
		/// </summary>
		/// <summary>
		/// The biome a namer should use: the settings' explicit biome, else the biome the scene's map
		/// (or terrain and climate) reports at the object's position. Null when neither exists.
		/// </summary>
		public static BiomeTemplate ResolveBiome(SceneObjectNamingSettings settings, Vector3 worldPosition,
			WorldSceneSettings scene, out BiomeClimateVariant variant)
		{
			variant = null;
			if (settings == null || !settings.UsesBiome)
			{
				return null;
			}
			BiomeReading reading = BiomeSampler.Read(worldPosition, scene);
			if (settings.BiomeID != 0)
			{
				if (!BiomeRegistry.TryGetByID(settings.BiomeID, out BiomeTemplate chosen))
				{
					chosen = BiomeTemplate.Get<BiomeTemplate>(settings.BiomeID);
				}
				if (chosen != null)
				{
					// An explicit biome still reads under the scene's current climate.
					variant = scene != null ? scene.ResolveVariant(chosen, reading.Climate) : chosen.ResolveOwnVariant(reading.Climate.Temperature, reading.Climate.Humidity);
					return chosen;
				}
			}
			variant = reading.Variant;
			return reading.Biome;
		}

		/// <summary>The variants a biome can show under a scene: its own, else the scene climate's defaults.</summary>
		public static IReadOnlyList<BiomeClimateVariant> VariantsFor(BiomeTemplate biome, WorldSceneSettings scene)
		{
			if (biome == null)
			{
				return System.Array.Empty<BiomeClimateVariant>();
			}
			if (biome.ClimateVariants != null && biome.ClimateVariants.Count > 0)
			{
				return biome.ClimateVariants;
			}
			return scene != null && scene.Climate != null ? scene.Climate.DefaultVariants : System.Array.Empty<BiomeClimateVariant>();
		}

		/// <summary>Index of a variant in <see cref="VariantsFor"/> plus one; 0 when it is not one of them. What the wire carries.</summary>
		public static byte VariantIndexOf(BiomeTemplate biome, WorldSceneSettings scene, BiomeClimateVariant variant)
		{
			if (variant == null)
			{
				return 0;
			}
			IReadOnlyList<BiomeClimateVariant> variants = VariantsFor(biome, scene);
			for (int i = 0; i < variants.Count && i < 254; i++)
			{
				if (variants[i] == variant)
				{
					return (byte)(i + 1);
				}
			}
			return 0;
		}

		/// <summary>The variant a wire index names, or null.</summary>
		public static BiomeClimateVariant VariantAt(BiomeTemplate biome, WorldSceneSettings scene, byte index)
		{
			if (index == 0)
			{
				return null;
			}
			IReadOnlyList<BiomeClimateVariant> variants = VariantsFor(biome, scene);
			return index - 1 < variants.Count ? variants[index - 1] : null;
		}

		/// <param name="autoProfession">Profession to use when the settings name none — the interactable's title, typically.</param>
		/// <param name="biome">The biome to name from, already resolved by <see cref="ResolveBiome"/>; null to use only the settings' explicit biome.</param>
		/// <param name="variant">The climate variant the place reads under; null for none.</param>
		public static bool TryBuild(SceneObjectNamingSettings settings, RaceTemplate race, int seed,
			CharacterGender gender, out string name, out string error, string autoProfession = null,
			BiomeTemplate biome = null, BiomeClimateVariant variant = null)
		{
			name = null;
			error = null;

			if (settings == null)
			{
				error = "no naming settings";
				return false;
			}
			if (seed == 0)
			{
				error = "no seed";
				return false;
			}
			if (!NameGrammar.IsLoaded)
			{
				error = "no NameGrammarTemplate is loaded";
				return false;
			}

			string raceKey = race?.NamingKey;
			if (settings.UsesRace && (race == null || !RaceRegistry.Contains(raceKey)))
			{
				error = race == null
					? $"{settings.Mode} names need a race: set a Race Override or give the object a FactionController with a race"
					: $"race '{race.Name}' has no usable naming data";
				return false;
			}

			if (biome == null && settings.BiomeID != 0)
			{
				if (!BiomeRegistry.TryGetByID(settings.BiomeID, out biome))
				{
					biome = BiomeTemplate.Get<BiomeTemplate>(settings.BiomeID);
				}
			}
			int biomeID = biome != null ? BiomeRegistry.IDOf(biome) : 0;
			if (settings.RequiresBiome && (biome == null || biome.Naming == null || !biome.Naming.IsUsable))
			{
				error = biome == null
					? $"{settings.Mode} names need a biome: set one, or place the object where the scene's biome map covers"
					: $"biome '{biome.name}' has no naming data";
				return false;
			}

			string modifierKey = null;
			if (settings.ModifierID != 0)
			{
				if (!ModifierRegistry.TryGetByID(settings.ModifierID, out NameModifierTemplate modifier))
				{
					modifier = NameModifierTemplate.Get<NameModifierTemplate>(settings.ModifierID);
				}
				modifierKey = modifier?.Key;
			}

			string culture = string.IsNullOrWhiteSpace(settings.Culture) ? null : settings.Culture.Trim();

			try
			{
				var generator = new NameGenerator(seed);
				switch (settings.Mode)
				{
					case SceneObjectNamingMode.Character:
					{
						CharacterEntry entry = generator.Generate(new NameRequest
						{
							Race = raceKey,
							Culture = culture,
							Modifier = modifierKey,
							Gender = gender,
							TitleType = ToTitleType(settings.TitlePolicy),
							Register = settings.Register,
							Profession = string.IsNullOrWhiteSpace(settings.Profession) ? autoProfession : settings.Profession,
							MaxTitleLength = settings.MaxTitleLength,
							AllowCompoundTitle = settings.AllowCompoundTitle,
							NameOnly = settings.TitlePolicy == NamingTitlePolicy.None,
							IncludeFamilyName = settings.NameFormat == CharacterNameFormat.GivenAndFamily,
						});
						name = entry.FullTitle;
						break;
					}
					case SceneObjectNamingMode.City:
						name = generator.Generate(new CityRequest
						{
							Race = raceKey,
							Culture = culture,
							CityType = settings.CityType,
							BiomeID = biomeID,
							Variant = variant,
						}).Name;
						break;
					case SceneObjectNamingMode.Dungeon:
						name = generator.Generate(new DungeonRequest
						{
							BiomeID = biomeID,
							Race = raceKey,
							Variant = variant,
						}).Name;
						break;
					case SceneObjectNamingMode.PointOfInterest:
						name = generator.Generate(new POIRequest
						{
							BiomeID = biomeID,
							POIType = settings.POIType,
							Variant = variant,
						}).Name;
						break;
					case SceneObjectNamingMode.Item:
						name = generator.Generate(new ItemRequest
						{
							Race = raceKey,
							Culture = culture,
							ItemType = settings.ItemType,
						}).Name;
						break;
					default:
						error = $"unknown naming mode {settings.Mode}";
						return false;
				}
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}

			name = name?.Trim();
			if (string.IsNullOrEmpty(name))
			{
				error = "the generator produced an empty name";
				return false;
			}
			return true;
		}

		private static TitleType ToTitleType(NamingTitlePolicy policy)
		{
			switch (policy)
			{
				case NamingTitlePolicy.Honorific: return TitleType.Honorific;
				case NamingTitlePolicy.Epithet: return TitleType.Epithet;
				case NamingTitlePolicy.Rank: return TitleType.Rank;
				case NamingTitlePolicy.Legend: return TitleType.Legend;
				default: return TitleType.Any;
			}
		}
	}
}
