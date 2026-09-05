using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Public facade for the name-generation system: characters, cities,
	/// dungeons, points of interest and legendary items, all built from the
	/// naming templates registered in <see cref="RaceRegistry"/>,
	/// <see cref="BiomeRegistry"/>, <see cref="ModifierRegistry"/> and
	/// <see cref="NameGrammar"/>.
	///
	/// <para>Determinism: when a request carries a <c>RegionSeed</c> (and
	/// optionally <c>ObjectSeed</c> / <c>Index</c>) the result is reproducible on
	/// every peer — the same inputs derive the same <see cref="DeterministicRNG"/>.
	/// Without a seed the instance's own RNG drives the output.</para>
	///
	/// <para>Thread safety: an instance is not thread-safe when its own RNG is
	/// used (no RegionSeed). For multi-threaded use construct one instance per
	/// thread, or always supply a RegionSeed so each call derives its own RNG.</para>
	/// </summary>
	public class NameGenerator
	{
		private readonly DeterministicRNG rng;
		/// <summary>Recent titles, so an unseeded batch does not repeat itself. Seeded requests ignore it.</summary>
		private readonly TitleMemory titleMemory = new TitleMemory();

		/// <summary>
		/// Construct a generator. With no <paramref name="seed"/> the RNG is
		/// time-based; with one it is fixed, so unseeded requests replay too.
		/// </summary>
		public NameGenerator(int? seed = null)
		{
			rng = seed.HasValue ? new DeterministicRNG(seed.Value) : new DeterministicRNG();
		}

		// ── Readiness and registries ───────────────────────────────────

		/// <summary>True once a grammar and at least one race are registered.</summary>
		public static bool IsReady => NameGrammar.IsLoaded && RaceRegistry.Count > 0;

		public static IReadOnlyList<string> SupportedRaces => RaceRegistry.SupportedRaces;
		public static IReadOnlyList<string> SupportedBiomes => BiomeRegistry.NameableBiomes;
		public static IReadOnlyList<string> SupportedModifiers => ModifierRegistry.SupportedModifiers;

		public static IReadOnlyList<string> GetCultures(string race) => RaceRegistry.GetCultures(race);

		// ── Parameter-object API ───────────────────────────────────────

		public CharacterEntry Generate(NameRequest req) => Generate(req, 0);

		private CharacterEntry Generate(NameRequest req, int batchOffset)
		{
			if (req == null) throw new ArgumentNullException(nameof(req));
			RaceTemplate race = RequireRace(req.Race);

			DeterministicRNG draw = DeriveRng(req, batchOffset, "name", race.NamingKey, req.Culture, req.Modifier);
			RacePhonology phonology = RaceRegistry.ResolvePhonology(race.NamingKey, req.Culture, req.Modifier);

			string name = "", meaning = "", title = "", titleCategory = "", familyName = "";
			var fragments = new List<string>();

			if (!req.TitleOnly)
			{
				(name, meaning, fragments) = NameBuilder.Build(phonology, req.Gender, draw);
			}

			if (req.IncludeFamilyName)
			{
				(familyName, _, _) = NameBuilder.Build(phonology, CharacterGender.Unspecified, draw);
			}

			if (!req.NameOnly && req.TitleType != TitleType.None)
			{
				(title, titleCategory) = TitleBuilder.Build(race.NamingKey, TitleOptionsFor(req), meaning, draw,
					string.IsNullOrEmpty(req.RegionSeed) ? titleMemory : null);
			}

			string displayRace = race.Name;
			if (!string.IsNullOrEmpty(req.Culture)) displayRace += $" ({req.Culture})";
			if (!string.IsNullOrEmpty(req.Modifier)) displayRace += $" [{req.Modifier}]";

			return new CharacterEntry
			{
				Name = name,
				FamilyName = familyName,
				Title = title,
				Meaning = meaning,
				Race = displayRace,
				TitleCategory = titleCategory,
				NameFragments = fragments,
			};
		}

		public CityNameEntry Generate(CityRequest req) => Generate(req, 0);

		private CityNameEntry Generate(CityRequest req, int batchOffset)
		{
			if (req == null) throw new ArgumentNullException(nameof(req));
			RaceTemplate race = RequireRace(req.Race);

			// Designer-injected names drain before procedural results.
			CityNameEntry injected = RuntimeInjection.TryPopCity(race.NamingKey, req.CityType);
			if (injected != null) return injected;

			BiomeTemplate biome = ResolveBiomeOrNull(req);
			DeterministicRNG draw = DeriveRng(req, batchOffset, "city", race.NamingKey, req.Culture, biome?.Key ?? req.Biome);
			// A settlement with no stated biome sits in one of its people's home biomes.
			if (biome == null && req.UseRaceHomeBiome)
			{
				biome = race.PickHomeBiome(draw);
			}
			BiomeClimateVariant variant = ResolveVariant(req, biome);
			RacePhonology phonology = RaceRegistry.ResolvePhonology(race.NamingKey, req.Culture);

			var (name, meaning, fragments) = CityNameBuilder.Build(phonology, race.NamingKey, req.CityType, biome?.Key, draw, variant);

			string displayRace = race.Name;
			if (!string.IsNullOrEmpty(req.Culture)) displayRace += $" ({req.Culture})";

			return new CityNameEntry
			{
				Name = name,
				Meaning = meaning,
				Race = displayRace,
				CityType = req.CityType == CityType.Any ? "mixed" : req.CityType.ToString().ToLower(),
				NameFragments = fragments,
			};
		}

		public DungeonNameEntry Generate(DungeonRequest req) => Generate(req, 0);

		private DungeonNameEntry Generate(DungeonRequest req, int batchOffset)
		{
			if (req == null) throw new ArgumentNullException(nameof(req));
			BiomeTemplate biome = RequireBiome(req);

			DungeonNameEntry injected = RuntimeInjection.TryPopDungeon(biome.Key);
			if (injected != null) return injected;

			DeterministicRNG draw = DeriveRng(req, batchOffset, "dungeon", biome.Key, req.Race);
			BiomeClimateVariant variant = ResolveVariant(req, biome);
			var (name, meaning, fragments) = DungeonNameBuilder.Build(biome.Naming.RuntimePhonology, draw, variant);

			return new DungeonNameEntry
			{
				Name = name,
				Meaning = meaning,
				Biome = biome.ResolvedDisplayName,
				NameFragments = fragments,
			};
		}

		public POINameEntry Generate(POIRequest req) => Generate(req, 0);

		private POINameEntry Generate(POIRequest req, int batchOffset)
		{
			if (req == null) throw new ArgumentNullException(nameof(req));
			BiomeTemplate biome = RequireBiome(req);

			POINameEntry injected = RuntimeInjection.TryPopPOI(biome.Key, req.POIType);
			if (injected != null) return injected;

			DeterministicRNG draw = DeriveRng(req, batchOffset, "poi", biome.Key, req.POIType.ToString());
			BiomeClimateVariant variant = ResolveVariant(req, biome);
			var (name, meaning, fragments) = POINameBuilder.Build(biome.Naming.RuntimePhonology, req.POIType, draw, variant);

			return new POINameEntry
			{
				Name = name,
				Meaning = meaning,
				Biome = biome.ResolvedDisplayName,
				POIType = req.POIType == POIType.Any ? "mixed" : req.POIType.ToString().ToLower(),
				NameFragments = fragments,
			};
		}

		/// <summary>Generate a single legendary item name.</summary>
		public ItemNameEntry Generate(ItemRequest req) => Generate(req, 0);

		private ItemNameEntry Generate(ItemRequest req, int batchOffset)
		{
			if (req == null) throw new ArgumentNullException(nameof(req));
			RaceTemplate race = RequireRace(string.IsNullOrEmpty(req.Race) ? "human" : req.Race);

			DeterministicRNG draw = DeriveRng(req, batchOffset, "item", race.NamingKey, req.Culture, req.ItemType.ToString());
			RacePhonology phonology = RaceRegistry.ResolvePhonology(race.NamingKey, req.Culture);
			var (name, meaning, fragments) = ItemNameBuilder.Build(phonology, req.ItemType, draw, req.Library);

			return new ItemNameEntry
			{
				Name = name,
				Meaning = meaning,
				Race = race.Name,
				ItemCategory = req.ItemType == ItemType.Any ? "mixed" : req.ItemType.ToString().ToLower(),
				NameFragments = fragments,
			};
		}

		public CharacterEntry Generate(HybridRequest req)
		{
			if (req == null) throw new ArgumentNullException(nameof(req));
			RaceTemplate raceA = RequireRace(req.RaceA);
			RaceTemplate raceB = RequireRace(req.RaceB);

			DeterministicRNG draw = DeriveRng(req, 0, "hybrid", raceA.NamingKey, raceB.NamingKey, req.CultureA, req.CultureB);

			RacePhonology phonologyA = RaceRegistry.ResolvePhonology(raceA.NamingKey, req.CultureA);
			RacePhonology phonologyB = RaceRegistry.ResolvePhonology(raceB.NamingKey, req.CultureB);
			RacePhonology hybrid = BlendPhonologies(phonologyA, phonologyB, req.Dominance, draw);

			var (name, meaning, fragments) = NameBuilder.Build(hybrid, req.Gender, draw);

			string title = "", titleCategory = "";
			if (req.TitleType != TitleType.None)
			{
				string titleRace = draw.NextDouble() < req.Dominance ? raceA.NamingKey : raceB.NamingKey;
				(title, titleCategory) = TitleBuilder.Build(titleRace, TitleOptionsFor(req), meaning, draw,
					string.IsNullOrEmpty(req.RegionSeed) ? titleMemory : null);
			}

			return new CharacterEntry
			{
				Name = name,
				Title = title,
				Meaning = meaning,
				Race = $"{raceA.Name}/{raceB.Name}",
				TitleCategory = titleCategory,
				NameFragments = fragments,
			};
		}

		// ── Batch helpers ──────────────────────────────────────────────

		public List<CharacterEntry> GenerateBatch(NameRequest req, int count)
		{
			var results = new List<CharacterEntry>(count);
			for (int i = 0; i < count; i++) results.Add(Generate(req, i));
			return results;
		}

		public List<CityNameEntry> GenerateBatch(CityRequest req, int count)
		{
			var results = new List<CityNameEntry>(count);
			for (int i = 0; i < count; i++) results.Add(Generate(req, i));
			return results;
		}

		public List<DungeonNameEntry> GenerateBatch(DungeonRequest req, int count)
		{
			var results = new List<DungeonNameEntry>(count);
			for (int i = 0; i < count; i++) results.Add(Generate(req, i));
			return results;
		}

		public List<POINameEntry> GenerateBatch(POIRequest req, int count)
		{
			var results = new List<POINameEntry>(count);
			for (int i = 0; i < count; i++) results.Add(Generate(req, i));
			return results;
		}

		public List<ItemNameEntry> GenerateBatch(ItemRequest req, int count)
		{
			var results = new List<ItemNameEntry>(count);
			for (int i = 0; i < count; i++) results.Add(Generate(req, i));
			return results;
		}

		public UniqueResult<CharacterEntry> GenerateUnique(NameRequest req, int count, int maxAttempts = 10000)
			=> GenerateUniqueCore(count, maxAttempts, i => Generate(req, i), e => e.FullName);

		public UniqueResult<CityNameEntry> GenerateUnique(CityRequest req, int count, int maxAttempts = 10000)
			=> GenerateUniqueCore(count, maxAttempts, i => Generate(req, i), e => e.Name);

		public UniqueResult<DungeonNameEntry> GenerateUnique(DungeonRequest req, int count, int maxAttempts = 10000)
			=> GenerateUniqueCore(count, maxAttempts, i => Generate(req, i), e => e.Name);

		public UniqueResult<POINameEntry> GenerateUnique(POIRequest req, int count, int maxAttempts = 10000)
			=> GenerateUniqueCore(count, maxAttempts, i => Generate(req, i), e => e.Name);

		public UniqueResult<ItemNameEntry> GenerateUnique(ItemRequest req, int count, int maxAttempts = 10000)
			=> GenerateUniqueCore(count, maxAttempts, i => Generate(req, i), e => e.Name);

		// ── Positional convenience API ─────────────────────────────────

		public string GenerateName(string race, CharacterGender gender = CharacterGender.Unspecified,
			string culture = null, string regionSeed = null)
		{
			return Generate(new NameRequest
			{
				Race = race, Gender = gender, Culture = culture,
				RegionSeed = regionSeed, NameOnly = true,
			}).Name;
		}

		public string GenerateTitle(string race, TitleType titleType = TitleType.Any)
		{
			return Generate(new NameRequest
			{
				Race = race, TitleType = titleType, TitleOnly = true,
			}).Title;
		}

		public CharacterEntry GenerateCharacter(string race,
			CharacterGender gender = CharacterGender.Unspecified,
			TitleType titleType = TitleType.Any,
			string culture = null, string regionSeed = null)
		{
			return Generate(new NameRequest
			{
				Race = race, Gender = gender, TitleType = titleType,
				Culture = culture, RegionSeed = regionSeed,
			});
		}

		public List<string> GenerateNames(string race, int count,
			CharacterGender gender = CharacterGender.Unspecified, string culture = null, string regionSeed = null)
		{
			var req = new NameRequest
			{
				Race = race, Gender = gender, Culture = culture,
				RegionSeed = regionSeed, NameOnly = true,
			};
			return GenerateBatch(req, count).Select(e => e.Name).ToList();
		}

		public List<string> GenerateUniqueNames(string race, int count,
			CharacterGender gender = CharacterGender.Unspecified, int maxAttempts = 10000,
			string culture = null, string regionSeed = null)
		{
			var req = new NameRequest
			{
				Race = race, Gender = gender, Culture = culture,
				RegionSeed = regionSeed, NameOnly = true,
			};
			return GenerateUnique(req, count, maxAttempts).Items.Select(e => e.Name).ToList();
		}

		public List<CharacterEntry> GenerateCharacters(string race, int count,
			CharacterGender gender = CharacterGender.Unspecified,
			TitleType titleType = TitleType.Any,
			string culture = null, string regionSeed = null)
		{
			return GenerateBatch(new NameRequest
			{
				Race = race, Gender = gender, TitleType = titleType,
				Culture = culture, RegionSeed = regionSeed,
			}, count);
		}

		public List<CharacterEntry> GenerateUniqueCharacters(string race, int count,
			CharacterGender gender = CharacterGender.Unspecified,
			TitleType titleType = TitleType.Any, int maxAttempts = 10000,
			string culture = null, string regionSeed = null)
		{
			return GenerateUnique(new NameRequest
			{
				Race = race, Gender = gender, TitleType = titleType,
				Culture = culture, RegionSeed = regionSeed,
			}, count, maxAttempts).Items;
		}

		public CharacterEntry GenerateHybrid(string raceA, string raceB,
			CharacterGender gender = CharacterGender.Unspecified,
			TitleType titleType = TitleType.Any,
			double dominance = 0.5)
		{
			return Generate(new HybridRequest
			{
				RaceA = raceA, RaceB = raceB, Gender = gender,
				TitleType = titleType, Dominance = dominance,
			});
		}

		/// <param name="biome">Biome key the settlement sits in; null draws one of the race's home biomes.</param>
		/// <param name="climateVariant">Key of one of the biome's climate variants ("frozen"); null for none.</param>
		public CityNameEntry GenerateCityName(string race,
			CityType cityType = CityType.Any,
			string culture = null, string regionSeed = null,
			string biome = null, string climateVariant = null)
		{
			return Generate(new CityRequest
			{
				Race = race, CityType = cityType, Culture = culture, RegionSeed = regionSeed,
				Biome = biome, ClimateVariant = climateVariant,
			});
		}

		public List<CityNameEntry> GenerateCityNames(string race, int count,
			CityType cityType = CityType.Any,
			string culture = null, string regionSeed = null,
			string biome = null, string climateVariant = null)
		{
			return GenerateBatch(new CityRequest
			{
				Race = race, CityType = cityType, Culture = culture, RegionSeed = regionSeed,
				Biome = biome, ClimateVariant = climateVariant,
			}, count);
		}

		public List<CityNameEntry> GenerateUniqueCityNames(string race, int count,
			CityType cityType = CityType.Any, int maxAttempts = 10000,
			string culture = null, string regionSeed = null,
			string biome = null, string climateVariant = null)
		{
			return GenerateUnique(new CityRequest
			{
				Race = race, CityType = cityType, Culture = culture, RegionSeed = regionSeed,
				Biome = biome, ClimateVariant = climateVariant,
			}, count, maxAttempts).Items;
		}

		/// <param name="climateVariant">Key of one of the biome's climate variants ("frozen"); null for none.</param>
		public DungeonNameEntry GenerateDungeonName(string biome, string regionSeed = null, string climateVariant = null)
			=> Generate(new DungeonRequest { Biome = biome, RegionSeed = regionSeed, ClimateVariant = climateVariant });

		public List<DungeonNameEntry> GenerateDungeonNames(string biome, int count, string regionSeed = null, string climateVariant = null)
			=> GenerateBatch(new DungeonRequest { Biome = biome, RegionSeed = regionSeed, ClimateVariant = climateVariant }, count);

		public List<DungeonNameEntry> GenerateUniqueDungeonNames(string biome, int count,
			int maxAttempts = 10000, string regionSeed = null, string climateVariant = null)
			=> GenerateUnique(new DungeonRequest { Biome = biome, RegionSeed = regionSeed, ClimateVariant = climateVariant }, count, maxAttempts).Items;

		public POINameEntry GeneratePOIName(string biome,
			POIType poiType = POIType.Any, string regionSeed = null, string climateVariant = null)
			=> Generate(new POIRequest { Biome = biome, POIType = poiType, RegionSeed = regionSeed, ClimateVariant = climateVariant });

		public List<POINameEntry> GeneratePOINames(string biome, int count,
			POIType poiType = POIType.Any, string regionSeed = null, string climateVariant = null)
			=> GenerateBatch(new POIRequest { Biome = biome, POIType = poiType, RegionSeed = regionSeed, ClimateVariant = climateVariant }, count);

		public List<POINameEntry> GenerateUniquePOINames(string biome, int count,
			POIType poiType = POIType.Any, int maxAttempts = 10000,
			string regionSeed = null, string climateVariant = null)
			=> GenerateUnique(new POIRequest { Biome = biome, POIType = poiType, RegionSeed = regionSeed, ClimateVariant = climateVariant }, count, maxAttempts).Items;

		public ItemNameEntry GenerateItemName(string race, ItemType itemType = ItemType.Any,
			string culture = null, string regionSeed = null)
			=> Generate(new ItemRequest { Race = race, ItemType = itemType, Culture = culture, RegionSeed = regionSeed });

		public List<ItemNameEntry> GenerateItemNames(string race, int count,
			ItemType itemType = ItemType.Any, string culture = null, string regionSeed = null)
			=> GenerateBatch(new ItemRequest { Race = race, ItemType = itemType, Culture = culture, RegionSeed = regionSeed }, count);

		public List<ItemNameEntry> GenerateUniqueItemNames(string race, int count,
			ItemType itemType = ItemType.Any, int maxAttempts = 10000,
			string culture = null, string regionSeed = null)
			=> GenerateUnique(new ItemRequest { Race = race, ItemType = itemType, Culture = culture, RegionSeed = regionSeed }, count, maxAttempts).Items;

		private static TitleOptions TitleOptionsFor(NameRequest req) => new TitleOptions
		{
			TitleType = req.TitleType,
			Register = req.Register,
			Gender = req.Gender,
			Profession = req.Profession,
			MaxLength = req.MaxTitleLength,
			AllowCompound = req.AllowCompoundTitle,
		};

		private static TitleOptions TitleOptionsFor(HybridRequest req) => new TitleOptions
		{
			TitleType = req.TitleType,
			Register = req.Register,
			Gender = req.Gender,
			Profession = req.Profession,
			MaxLength = req.MaxTitleLength,
			AllowCompound = req.AllowCompoundTitle,
		};

		// ── Lookups with clear failures ────────────────────────────────

		private static RaceTemplate RequireRace(string race)
		{
			if (!NameGrammar.IsLoaded)
			{
				throw new InvalidOperationException(
					"No NameGrammarTemplate is registered. Load the naming templates before generating names.");
			}
			if (RaceRegistry.TryGet(race, out RaceTemplate template))
			{
				return template;
			}
			throw new ArgumentException(RaceRegistry.Count == 0
				? $"Unknown race '{race}': no RaceTemplate is registered."
				: $"Unknown race '{race}'. Supported: {string.Join(", ", RaceRegistry.SupportedRaces)}");
		}

		/// <summary>The request's biome by ID, else by key; null when it names none or the biome is unknown.</summary>
		private static BiomeTemplate ResolveBiomeOrNull(BiomeGenerationRequest req)
		{
			if (req.BiomeID != 0 && BiomeRegistry.TryGetByID(req.BiomeID, out BiomeTemplate byId))
			{
				return byId;
			}
			return BiomeRegistry.TryGet(req.Biome, out BiomeTemplate byKey) ? byKey : null;
		}

		private static BiomeTemplate RequireBiome(BiomeGenerationRequest req)
		{
			if (!NameGrammar.IsLoaded)
			{
				throw new InvalidOperationException(
					"No NameGrammarTemplate is registered. Load the naming templates before generating names.");
			}
			BiomeTemplate biome = ResolveBiomeOrNull(req);
			if (biome != null)
			{
				if (biome.Naming == null || !biome.Naming.IsUsable)
				{
					throw new ArgumentException($"Biome '{biome.name}' has no usable naming data.");
				}
				return biome;
			}
			string asked = req.BiomeID != 0 ? $"#{req.BiomeID}" : $"'{req.Biome}'";
			throw new ArgumentException(BiomeRegistry.Count == 0
				? $"Unknown biome {asked}: no BiomeTemplate is registered."
				: $"Unknown biome {asked}. Supported: {string.Join(", ", BiomeRegistry.NameableBiomes)}");
		}

		/// <summary>The variant the request names, resolved on the biome; null when none applies.</summary>
		private static BiomeClimateVariant ResolveVariant(BiomeGenerationRequest req, BiomeTemplate biome)
		{
			if (req.Variant != null)
			{
				return req.Variant;
			}
			if (biome == null || string.IsNullOrWhiteSpace(req.ClimateVariant))
			{
				return null;
			}
			return biome.FindOwnVariant(req.ClimateVariant);
		}

		// ── Seed derivation ────────────────────────────────────────────

		/// <summary>
		/// The RNG for one generation call. With a <c>RegionSeed</c> it is derived
		/// from a cross-platform-stable hash of every identifying part of the
		/// request, so the same inputs always yield the same RNG on every peer;
		/// without one the generator's own RNG is used.
		/// </summary>
		private DeterministicRNG DeriveRng(GenerationRequest req, int batchOffset, string kind, params string[] keys)
		{
			if (string.IsNullOrEmpty(req.RegionSeed))
			{
				return rng;
			}

			ulong seed = StableHash.Seed(req.RegionSeed);
			seed = StableHash.Combine(seed, kind);
			if (keys != null)
			{
				for (int i = 0; i < keys.Length; i++)
				{
					seed = StableHash.Combine(seed, keys[i] ?? "");
				}
			}
			if (!string.IsNullOrEmpty(req.ObjectSeed))
			{
				seed = StableHash.Combine(seed, req.ObjectSeed);
			}
			int idx = (req.Index ?? 0) + batchOffset;
			if (idx != 0 || req.Index.HasValue)
			{
				seed = StableHash.Combine(seed, idx);
			}

			return new DeterministicRNG(StableHash.FoldSeed(seed));
		}

		// ── Unique-result helper ───────────────────────────────────────

		private static UniqueResult<T> GenerateUniqueCore<T>(int count, int maxAttempts,
			Func<int, T> gen, Func<T, string> keyOf)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var results = new List<T>(count);
			int attempts = 0;
			while (results.Count < count && attempts < maxAttempts)
			{
				T entry = gen(attempts);
				if (seen.Add(keyOf(entry)))
				{
					results.Add(entry);
				}
				attempts++;
			}
			return new UniqueResult<T>
			{
				Items = results,
				TargetCount = count,
				Attempts = attempts,
			};
		}

		// ── Hybrid blending ────────────────────────────────────────────

		private static RacePhonology BlendPhonologies(RacePhonology a, RacePhonology b,
			double dominance, DeterministicRNG rng)
		{
			if ((a == null || a.Onsets == null || a.Onsets.Length == 0) &&
				(b == null || b.Onsets == null || b.Onsets.Length == 0))
			{
				throw new ArgumentException("BlendPhonologies: both sources are null or empty.");
			}

			return new RacePhonology
			{
				Onsets = BlendWeighted(a?.Onsets, b?.Onsets, dominance, rng),
				Nuclei = BlendWeighted(a?.Nuclei, b?.Nuclei, dominance, rng),
				Codas = BlendWeighted(a?.Codas, b?.Codas, dominance, rng),
				Middles = BlendWeighted(a?.Middles, b?.Middles, dominance, rng),
				SyllMin = dominance >= 0.5 ? (a?.SyllMin ?? b?.SyllMin ?? 1) : (b?.SyllMin ?? a?.SyllMin ?? 1),
				SyllMax = dominance >= 0.5 ? (a?.SyllMax ?? b?.SyllMax ?? 3) : (b?.SyllMax ?? a?.SyllMax ?? 3),
				FeminineSuffixes = BlendWeighted(a?.FeminineSuffixes, b?.FeminineSuffixes, dominance, rng),
				MasculineSuffixes = BlendWeighted(a?.MasculineSuffixes, b?.MasculineSuffixes, dominance, rng),
				Description = $"Hybrid ({dominance:P0}): {a?.Description} + {b?.Description}",
				Tags = (a?.Tags ?? Array.Empty<string>())
					.Concat(b?.Tags ?? Array.Empty<string>()).ToArray(),
			};
		}

		/// <summary>Each entry of A passes with probability bias, each of B with 1 - bias; never empty when either input has entries.</summary>
		private static string[] BlendWeighted(string[] a, string[] b, double bias, DeterministicRNG rng)
		{
			int capacity = (a?.Length ?? 0) + (b?.Length ?? 0);
			var result = new HashSet<string>(capacity, StringComparer.Ordinal);
			if (a != null)
			{
				foreach (string item in a)
				{
					if (rng.NextDouble() < bias) result.Add(item);
				}
			}
			if (b != null)
			{
				foreach (string item in b)
				{
					if (rng.NextDouble() < (1.0 - bias)) result.Add(item);
				}
			}
			if (result.Count == 0)
			{
				if (a != null && a.Length > 0) result.Add(a[0]);
				if (b != null && b.Length > 0) result.Add(b[0]);
			}
			var arr = new string[result.Count];
			result.CopyTo(arr);
			return arr;
		}
	}
}
