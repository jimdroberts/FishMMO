using System;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>The kind of country a biome is, as its living things are coloured: its theme.</summary>
	public enum BiomeLookClass : byte
	{
		Temperate = 0,
		Forest = 1,
		Jungle = 2,
		Wetland = 3,
		Arid = 4,
		Desert = 5,
		Cold = 6,
		Alpine = 7,
		Coastal = 8,
		Ice = 9,
		Volcanic = 10,
		Cave = 11,
		Alien = 12,
		Aquatic = 13,
		/// <summary>
		/// Not a theme: a biome template whose look class has not been authored yet (<see cref="BiomeTemplate.LookClass"/>).
		/// Its looks fall back on the biome's name (<see cref="BiomeLooks.ClassOf(string, float, float)"/>), with a warning.
		/// Never passed to <see cref="BiomeLooks.For"/>.
		/// </summary>
		Unset = 255,
	}

	/// <summary>
	/// A biome's choice of one of its two rare fantastic looks (<see cref="BiomeLooks.FirstFantastic"/>): <see cref="Auto"/>
	/// leaves it to the biome's hash, as every biome had it before the choice existed; the others name one outright.
	/// The order is <c>BiomeLooks.Fantastic</c>'s, plus one.
	/// </summary>
	public enum BiomeRareLook : byte
	{
		Auto = 0,
		Moonsilver = 1,
		Arcane = 2,
		Gilded = 3,
		Bloodleaf = 4,
		Frostazure = 5,
		Emberleaf = 6,
		Witchmoss = 7,
		Duskbloom = 8,
		Faerie = 9,
		Copperleaf = 10,
	}

	/// <summary>
	/// What the living things of a biome look like on a given world: <see cref="Variants"/> distinct looks for its grass,
	/// flowers, small plants, fungi and lichen, the stones they grow on, and the small animals among them (2026-10-15:
	/// "Grass, flowers, critters, and other detail objects look identical on all biomes and/or planets. We need significant
	/// (at least 10) variations for every biome/planet type to fit atmosphere and fantasy vibe").
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A theme, moved by the world.</b> Each biome takes its class's theme (<see cref="ClassOf"/>: a meadow's greens, a
	/// desert's sage and straw, a cave's glowing fungi), and the world it lies on moves that theme as physics would:
	/// </para>
	/// <list type="bullet">
	/// <item>Its <b>star</b> decides what colour leaves evolve to be. They absorb where their light is richest and give back
	/// the rest: green under a sun like ours, olive to amber under a K star, dark plum to near black under a red dwarf, teal
	/// under a hot white star.</item>
	/// <item>Its <b>air</b>: thin air keeps plants low, pale and waxy against the light; thick air lets them grow tall and
	/// broad; a world whose sky snows methane or nitrogen grows nothing earthly at all.</item>
	/// <item>Its <b>warmth and water</b>: a frozen world's hardy plants run blue-green and silver, a hot one's gold, a dry
	/// one's dusty; a world hot inside spreads ember and ash.</item>
	/// </list>
	/// <para>
	/// <b>Twelve looks, by abundance.</b> Ten are the biome's own (common, lush, sun-bleached, golden, russet, silvered…) by
	/// falling weight, so a place has a character and its rarer patches stand out; two are rare touches of the fantastic
	/// chosen per biome (moonsilver, arcane violet, gilded, bloodleaf…). Grass, flowers and the rest pick a look per patch
	/// of ground, the animals one per group.
	/// </para>
	/// <para>
	/// <b>Deterministic and pure:</b> the same biome on the same world is always the same twelve looks, on the server, every
	/// client and in tests. Colours are sRGB; the shaders take them through <c>FloraPaletteMap</c> in linear light.
	/// </para>
	/// </remarks>
	public static class BiomeLooks
	{
		/// <summary>How many looks every biome has on every world.</summary>
		public const int Variants = 12;

		/// <summary>The two rare fantastic looks are the last two.</summary>
		public const int FirstFantastic = 10;

		/// <summary>What of a world moves its biomes' looks.</summary>
		public struct WorldTraits
		{
			public AtmosphereKind Atmosphere;
			/// <summary>Mean surface temperature on the climate scale: 0 at freezing, about 33 K a unit.</summary>
			public float MeanTemperature;
			public float Water;
			public float InternalHeat;
			public Condensate Condensate;
			/// <summary>The surface temperature of the star it lives under, K.</summary>
			public float StarKelvin;
			public uint Seed;
			/// <summary>
			/// The colour of the world's bare ground (BodyColours: ice, sulphur, rust, tholin, regolith), sRGB, and how far its
			/// mineral ground is drawn toward it: as far as its globe is (<see cref="GroundRecolour"/>) where the air is thin or
			/// absent, so the ground underfoot is the world seen from space; 0 under an ordinary or thick air, whose biomes'
			/// own ground is what its globe shows.
			/// </summary>
			public Color Ground;
			public float GroundStrength;

			/// <summary>The home world under the reference sun.</summary>
			public static WorldTraits Earthlike => Of(BiomeWorldConditions.Earthlike, StarBody.ReferenceTemperatureK, 0u);

			public static WorldTraits Of(in BiomeWorldConditions conditions, float starKelvin, uint seed) => new WorldTraits
			{
				Atmosphere = conditions.Atmosphere,
				MeanTemperature = conditions.MeanTemperature,
				Water = conditions.Water,
				InternalHeat = conditions.InternalHeat,
				Condensate = conditions.Condensate,
				StarKelvin = starKelvin,
				Seed = seed,
			};
		}

		/// <summary>The world a body is, for its biomes' looks: its conditions, its system's primary star, and its name.</summary>
		public static WorldTraits WorldOf(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return WorldTraits.Earthlike;
			}
			StarBody star = system != null ? system.PrimaryStar : null;
			WorldTraits world = WorldTraits.Of(BiomeWorldConditions.For(system, body), star != null ? star.TemperatureK : StarBody.ReferenceTemperatureK,
				Hash(body.ResolvedName));
			if (body.Atmosphere == AtmosphereKind.None || body.Atmosphere == AtmosphereKind.Thin)
			{
				world.Ground = BodyColours.Of(system, body).Globe;
				world.GroundStrength = GroundRecolour;
			}
			return world;
		}

		/// <summary>
		/// How tall and broad living things grow on a world, against the home world's: thin air keeps them low (half again
		/// shorter), thick air lets them stand taller and broader, no air at all leaves only the hardiest, half height. The
		/// one thing about grass's SHAPE a world changes: each biome's grass types are already the height their biome grows
		/// (a tundra's short, a savanna's tall), so a biome's theme must not scale them again, and a patch's look never
		/// does (2026-10-15: per-patch heights stepped at every patch's edge).
		/// </summary>
		public static Vector2 GrowthOf(in WorldTraits world)
		{
			switch (world.Atmosphere)
			{
				case AtmosphereKind.None: return new Vector2(0.5f, 0.85f);
				case AtmosphereKind.Thin: return new Vector2(0.6f, 0.9f);
				case AtmosphereKind.Thick: return new Vector2(1.25f, 1.2f);
				default: return Vector2.one;
			}
		}

		/// <summary>How far a bare or thin-aired world's mineral ground is drawn to its own colour: the globe's share (PlanetSurfaceBaker).</summary>
		public const float GroundRecolour = 0.75f;

		/// <summary>What a terrain ground layer is made of, as its look is concerned: shares of living green, of lichen, of mineral.</summary>
		public struct GroundShare
		{
			/// <summary>Grass sward, moss, heath: takes the patch's leaves.</summary>
			public float Foliage;
			/// <summary>Lichen and coral: takes the patch's fungi-and-lichen colour.</summary>
			public float Accent;
			/// <summary>Rock, sand, soil, ash: takes the world's own ground colour, where its air is thin or absent.</summary>
			public float Mineral;
		}

		/// <summary>A ground's makeup as the template stores it (<see cref="TerrainTextureLayer.GroundMakeup"/>: x living, y lichen, z mineral).</summary>
		public static GroundShare ShareOf(Vector3 makeup) => new GroundShare { Foliage = makeup.x, Accent = makeup.y, Mineral = makeup.z };

		/// <summary>
		/// What a ground layer is made of, from its name (the generated layers are Ground_&lt;Kind&gt;): living ground is plant
		/// matter and wears the planet's leaves — grass, moss and heath wholly, litter and peat partly (dead leaves keep some of
		/// their colour); lichen and coral wear the lichen colour; everything else is mineral. Snow and ice take only a little
		/// of the world's ground (a frost on a rust world is still white), lava none (it glows its own colour).
		/// </summary>
		/// <remarks>
		/// Since 2026-10-15 a FALLBACK only. A ground's makeup is a property of its surface: each generated ground declares it
		/// (SurfaceCatalogue.MakeupOf, written to equal this for every ground), the biome art authoring writes it onto every
		/// template slot holding that ground (<see cref="TerrainTextureLayer.GroundMakeup"/>), and the terrain array bake
		/// carries it per layer (<c>TerrainArraySet.LayerMakeup</c>). A layer read by its name here is one whose set was baked
		/// before that, or hand-made art whose slot nobody has given a makeup.
		/// </remarks>
		public static GroundShare GroundShareOf(string layerName)
		{
			string n = (layerName ?? string.Empty).ToLowerInvariant();
			if (n.Contains("lava") || n.Contains("pahoehoe"))
			{
				return new GroundShare();
			}
			if (n.Contains("lichen"))
			{
				return new GroundShare { Accent = 1f };
			}
			if (n.Contains("coral"))
			{
				return new GroundShare { Accent = 0.6f, Mineral = 0.4f };
			}
			float living =
				n.Contains("grassdry") ? 0.55f :
				n.Contains("grass") || n.Contains("moss") || n.Contains("sphagnum") ? 1f :
				n.Contains("heath") ? 0.85f :
				n.Contains("jungle") ? 0.7f :
				n.Contains("forestfloor") || n.Contains("forest floor") ? 0.4f :
				n.Contains("needle") || n.Contains("peat") ? 0.3f :
				n.Contains("litter") ? 0.25f : 0f;
			float frozen = n.Contains("snow") || n.Contains("ice") || n.Contains("frost") ? 0.3f : 1f;
			return new GroundShare { Foliage = living, Mineral = (1f - living) * frozen };
		}

		/// <summary>One look: what grows, flowers and runs in one patch of a biome.</summary>
		[Serializable]
		public struct Variant
		{
			public string Name;
			/// <summary>How common it is against the biome's others (they need not sum to 1).</summary>
			public float Weight;
			/// <summary>Grass and leaves at their tips, sRGB.</summary>
			public Color Foliage;
			/// <summary>
			/// What living green does with this look, as a LINEAR multiplier on the colour it already has: this look's leaves
			/// against the same biome's common look on the home world (<see cref="ShiftOf"/>). Grass, leaves and turf take this,
			/// not <see cref="Foliage"/> outright: their own colour already carries the ground's climate (a dry patch's straw, a
			/// wet one's green) and must keep it; the shift moves it by what the planet and the patch change. White for the
			/// common look of an Earth-like world.
			/// </summary>
			public Color Shift;
			/// <summary>Grass and stems at the ground, sRGB.</summary>
			public Color Root;
			/// <summary>Grass height, blade width and how far it bows, against the type's own.</summary>
			public float Height, Width, Bend;
			/// <summary>Its flowers' colour, sRGB, and their size against the type's own.</summary>
			public Color Bloom;
			public float BloomSize;
			/// <summary>What is not leaf: fungus caps, lichen, litter, cactus skin, shells. sRGB.</summary>
			public Color Accent;
			/// <summary>A tint the stones among it take, about white. sRGB.</summary>
			public Color Mineral;
			/// <summary>The small animals of the patch: back, belly and markings (sRGB), the markings' pattern, their size.</summary>
			public Color Coat, Belly, Marking;
			public CoatPattern Pattern;
			public float Size;
		}

		/// <summary>How an animal's markings lie.</summary>
		public enum CoatPattern : byte
		{
			Plain = 0,
			Stripes = 1,
			Spots = 2,
			Bands = 3,
			Mottled = 4,
		}

		/// <summary>A biome's looks on one world.</summary>
		public sealed class Look
		{
			public BiomeLookClass Class;
			public Variant[] Variants;
			/// <summary>Running share of the weights, 0 … 1, for a weighted pick by a uniform number.</summary>
			public float[] Cumulative;

			/// <summary>The look a uniform number in [0, 1) picks, by weight.</summary>
			public int Pick(float uniform)
			{
				for (int i = 0; i < Cumulative.Length; i++)
				{
					if (uniform < Cumulative[i])
					{
						return i;
					}
				}
				return Cumulative.Length - 1;
			}
		}

		// ── Classes ─────────────────────────────────────────────────

		/// <summary>
		/// A biome's class: its template's <see cref="BiomeTemplate.LookClass"/>, the one source since 2026-10-15 (the biome
		/// art authoring writes it from the spec table, which names every biome's class). A template not authored yet falls
		/// back on its name (<see cref="ClassFromName"/>) and says so once.
		/// </summary>
		public static BiomeLookClass ClassOf(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return BiomeLookClass.Temperate;
			}
			BiomeLookClass authored = biome.LookClass;
			return authored <= BiomeLookClass.Aquatic ? authored : ClassFromName(biome);
		}

		/// <summary>Whether a template without a look class has been reported (once a session, as every template fallback is).</summary>
		private static bool warnedFallback;
		private static readonly object warnLock = new object();

		/// <summary>
		/// The fallback for a template whose look class has not been authored: the old reading of its name and climate
		/// (<see cref="ClassOf(string, float, float)"/>), with one warning a session. The only caller of the keyword table
		/// outside the authoring's tests.
		/// </summary>
		private static BiomeLookClass ClassFromName(BiomeTemplate biome)
		{
			float temperature = 0.5f * (biome.MinTemperature + biome.MaxTemperature);
			float humidity = 0.5f * (biome.MinHumidity + biome.MaxHumidity);
			BiomeLookClass c = ClassOf(biome.ResolvedDisplayName, temperature, humidity);
			lock (warnLock)
			{
				if (!warnedFallback)
				{
					warnedFallback = true;
					Debug.LogWarning($"[Biome templates] '{biome.name}' has no look class: read from its name as {c} (said once; other templates may be the same). Run Biomes → Authoring → Author biome art to write it onto the templates.");
				}
			}
			return c;
		}

		/// <summary>
		/// The old keyword reading of a biome's class from its name, the way it reads to a player (keywords, as the ambient
		/// life reads habitats), or from its climate where the name says nothing: <paramref name="temperature"/> and
		/// <paramref name="humidity"/> are the middle of its envelope. Since 2026-10-15 only the fallback for a template not
		/// yet authored (<see cref="ClassOf(BiomeTemplate)"/>); the spec table's look classes were written to equal it for
		/// every biome, so authoring changed no biome's looks.
		/// </summary>
		public static BiomeLookClass ClassOf(string name, float temperature, float humidity)
		{
			string n = (name ?? string.Empty).ToLowerInvariant();
			if (Any(n, "tholin", "methane", "nitrogen", "regolith", "dust sea", "cryovolcanic", "radiation", "rille", "runaway", "cloud deck", "tidal fracture", "ice geyser"))
			{
				return BiomeLookClass.Alien;
			}
			if (Any(n, "ocean", "reef", "abyss", "seamount", "underwater", "coastal water", "lake", "river", "vent"))
			{
				return BiomeLookClass.Aquatic;
			}
			if (Any(n, "ice", "glacier", "snow", "permanent", "frozen"))
			{
				return BiomeLookClass.Ice;
			}
			if (Any(n, "volcan", "lava", "molten", "sulphur", "geyser", "ash", "wasteland"))
			{
				return BiomeLookClass.Volcanic;
			}
			if (n.Contains("cave"))
			{
				return BiomeLookClass.Cave;
			}
			if (Any(n, "swamp", "bog", "marsh", "wetland", "mangrove", "estuary", "peat"))
			{
				return BiomeLookClass.Wetland;
			}
			if (Any(n, "jungle", "bamboo", "oasis", "rainforest"))
			{
				return BiomeLookClass.Jungle;
			}
			if (Any(n, "desert", "dune", "salt", "badlands"))
			{
				return BiomeLookClass.Desert;
			}
			if (Any(n, "savanna", "steppe", "scrub"))
			{
				return BiomeLookClass.Arid;
			}
			if (Any(n, "taiga", "tundra"))
			{
				return BiomeLookClass.Cold;
			}
			if (Any(n, "alpine", "mountain", "scree", "rocky terrain"))
			{
				return BiomeLookClass.Alpine;
			}
			if (Any(n, "beach", "coast"))
			{
				return BiomeLookClass.Coastal;
			}
			if (Any(n, "forest", "wood", "karst"))
			{
				return BiomeLookClass.Forest;
			}
			if (Any(n, "meadow", "grass", "plain", "farm", "hill", "valley", "castle", "fortress", "palace", "ruin", "temple"))
			{
				return BiomeLookClass.Temperate;
			}
			if (temperature > 0.5f && humidity < -0.3f)
			{
				return BiomeLookClass.Desert;
			}
			if (humidity < -0.2f)
			{
				return BiomeLookClass.Arid;
			}
			if (temperature < -0.4f)
			{
				return BiomeLookClass.Cold;
			}
			return humidity > 0.5f ? BiomeLookClass.Wetland : BiomeLookClass.Temperate;
		}

		private static bool Any(string text, params string[] words)
		{
			foreach (string word in words)
			{
				if (text.Contains(word))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A class's theme: its leaves, flowers, fungi and soil, all sRGB HSV, and how tall and varied it grows.</summary>
		private struct Theme
		{
			public Vector3 Foliage;
			/// <summary>How far its looks spread in hue, against an ordinary meadow's.</summary>
			public float Spread;
			public Vector3[] Blooms;
			public Vector3 Accent;
			public Color Soil;
			public float Height;
		}

		private static Vector3 V(float h, float s, float v) => new Vector3(h, s, v);

		private static readonly Theme[] Themes = BuildThemes();

		private static Theme[] BuildThemes()
		{
			var t = new Theme[14];
			t[(int)BiomeLookClass.Temperate] = new Theme
			{
				Foliage = V(0.26f, 0.55f, 0.52f), Spread = 1f, Height = 1f,
				// Poppy, buttercup, violet, daisy, campion, cornflower.
				Blooms = new[] { V(0f, 0.75f, 0.85f), V(0.14f, 0.8f, 0.95f), V(0.75f, 0.45f, 0.8f), V(0.1f, 0.05f, 0.95f), V(0.92f, 0.35f, 0.92f), V(0.6f, 0.55f, 0.85f) },
				Accent = V(0.07f, 0.55f, 0.55f), Soil = new Color(0.36f, 0.28f, 0.2f),
			};
			t[(int)BiomeLookClass.Forest] = new Theme
			{
				Foliage = V(0.3f, 0.6f, 0.4f), Spread = 1f, Height = 1.1f,
				// Bluebell, wood anemone, foxglove, primrose.
				Blooms = new[] { V(0.66f, 0.55f, 0.75f), V(0.1f, 0.05f, 0.95f), V(0.85f, 0.45f, 0.75f), V(0.15f, 0.7f, 0.9f) },
				Accent = V(0.06f, 0.6f, 0.6f), Soil = new Color(0.28f, 0.21f, 0.15f),
			};
			t[(int)BiomeLookClass.Jungle] = new Theme
			{
				Foliage = V(0.34f, 0.75f, 0.38f), Spread = 1.4f, Height = 1.35f,
				Blooms = new[] { V(0f, 0.85f, 0.9f), V(0.08f, 0.85f, 0.95f), V(0.83f, 0.7f, 0.8f), V(0.95f, 0.6f, 0.95f), V(0.14f, 0.9f, 0.95f), V(0.5f, 0.6f, 0.8f) },
				Accent = V(0.12f, 0.7f, 0.7f), Soil = new Color(0.3f, 0.2f, 0.13f),
			};
			t[(int)BiomeLookClass.Wetland] = new Theme
			{
				Foliage = V(0.21f, 0.45f, 0.38f), Spread = 0.9f, Height = 1.2f,
				// Marsh marigold, water crowfoot, flag iris, forget-me-not.
				Blooms = new[] { V(0.15f, 0.85f, 0.95f), V(0.1f, 0.03f, 0.95f), V(0.78f, 0.5f, 0.7f), V(0.58f, 0.4f, 0.85f) },
				Accent = V(0.18f, 0.4f, 0.45f), Soil = new Color(0.22f, 0.21f, 0.14f),
			};
			t[(int)BiomeLookClass.Arid] = new Theme
			{
				Foliage = V(0.16f, 0.45f, 0.62f), Spread = 0.9f, Height = 0.9f,
				Blooms = new[] { V(0.13f, 0.85f, 0.95f), V(0.06f, 0.75f, 0.9f), V(0.9f, 0.55f, 0.85f), V(0f, 0.05f, 0.95f) },
				Accent = V(0.08f, 0.35f, 0.6f), Soil = new Color(0.62f, 0.5f, 0.35f),
			};
			t[(int)BiomeLookClass.Desert] = new Theme
			{
				Foliage = V(0.2f, 0.3f, 0.55f), Spread = 0.8f, Height = 0.6f,
				// Cactus magenta, brittlebush, globemallow, desert lily.
				Blooms = new[] { V(0.9f, 0.7f, 0.9f), V(0.13f, 0.9f, 0.95f), V(0.03f, 0.8f, 0.9f), V(0.95f, 0.3f, 0.95f) },
				Accent = V(0.08f, 0.45f, 0.65f), Soil = new Color(0.76f, 0.62f, 0.44f),
			};
			t[(int)BiomeLookClass.Cold] = new Theme
			{
				Foliage = V(0.32f, 0.4f, 0.38f), Spread = 0.9f, Height = 0.55f,
				// Moss campion, arctic poppy, cotton white, fireweed.
				Blooms = new[] { V(0.92f, 0.3f, 0.92f), V(0.13f, 0.6f, 0.95f), V(0.1f, 0.03f, 0.97f), V(0.86f, 0.55f, 0.8f) },
				Accent = V(0.05f, 0.6f, 0.5f), Soil = new Color(0.4f, 0.36f, 0.3f),
			};
			t[(int)BiomeLookClass.Alpine] = new Theme
			{
				Foliage = V(0.34f, 0.3f, 0.5f), Spread = 0.8f, Height = 0.6f,
				// Gentian, edelweiss, saxifrage, globeflower.
				Blooms = new[] { V(0.62f, 0.7f, 0.85f), V(0.1f, 0.03f, 0.98f), V(0.8f, 0.45f, 0.85f), V(0.14f, 0.75f, 0.95f) },
				Accent = V(0.1f, 0.5f, 0.65f), Soil = new Color(0.48f, 0.46f, 0.44f),
			};
			t[(int)BiomeLookClass.Coastal] = new Theme
			{
				Foliage = V(0.24f, 0.4f, 0.55f), Spread = 0.8f, Height = 0.75f,
				// Sea thrift, horned poppy, sea campion, sea lavender.
				Blooms = new[] { V(0.92f, 0.4f, 0.95f), V(0.12f, 0.7f, 0.95f), V(0.1f, 0.03f, 0.97f), V(0.72f, 0.4f, 0.8f) },
				Accent = V(0.08f, 0.3f, 0.7f), Soil = new Color(0.7f, 0.64f, 0.5f),
			};
			t[(int)BiomeLookClass.Ice] = new Theme
			{
				Foliage = V(0.42f, 0.25f, 0.55f), Spread = 0.6f, Height = 0.35f,
				// Frostbloom blue, snow white, pale lilac, purple saxifrage, arctic gold, glacier cyan.
				Blooms = new[] { V(0.55f, 0.35f, 0.95f), V(0.1f, 0.02f, 0.99f), V(0.8f, 0.25f, 0.95f), V(0.87f, 0.45f, 0.85f), V(0.13f, 0.45f, 0.98f), V(0.5f, 0.5f, 0.9f) },
				// Sunburst lichen on the rocks of every frozen coast.
				Accent = V(0.07f, 0.75f, 0.75f), Soil = new Color(0.86f, 0.88f, 0.92f),
			};
			t[(int)BiomeLookClass.Volcanic] = new Theme
			{
				Foliage = V(0.18f, 0.35f, 0.35f), Spread = 0.8f, Height = 0.7f,
				Blooms = new[] { V(0.02f, 0.85f, 0.95f), V(0.08f, 0.9f, 1f), V(0.14f, 0.9f, 0.95f), V(0.95f, 0.7f, 0.7f) },
				Accent = V(0f, 0.6f, 0.4f), Soil = new Color(0.14f, 0.12f, 0.12f),
			};
			t[(int)BiomeLookClass.Cave] = new Theme
			{
				Foliage = V(0.5f, 0.45f, 0.45f), Spread = 1.2f, Height = 0.7f,
				// Glowing: what grows without the sun shows itself.
				Blooms = new[] { V(0.5f, 0.7f, 1f), V(0.78f, 0.6f, 1f), V(0.33f, 0.6f, 1f), V(0.9f, 0.5f, 1f) },
				Accent = V(0.75f, 0.4f, 0.6f), Soil = new Color(0.3f, 0.28f, 0.3f),
			};
			t[(int)BiomeLookClass.Alien] = new Theme
			{
				Foliage = V(0.7f, 0.4f, 0.45f), Spread = 1.3f, Height = 0.7f,
				Blooms = new[] { V(0.5f, 0.8f, 1f), V(0.85f, 0.7f, 1f), V(0.12f, 0.8f, 1f), V(0.62f, 0.5f, 0.95f) },
				// Tholin: the red-brown that sunlight makes of methane.
				Accent = V(0.07f, 0.6f, 0.55f), Soil = new Color(0.5f, 0.4f, 0.32f),
			};
			t[(int)BiomeLookClass.Aquatic] = new Theme
			{
				Foliage = V(0.38f, 0.55f, 0.35f), Spread = 1f, Height = 1f,
				// Anemone pink, coral orange, sea-glass, kelp gold, urchin violet, foam white.
				Blooms = new[] { V(0.95f, 0.5f, 0.9f), V(0.08f, 0.7f, 0.9f), V(0.5f, 0.5f, 0.9f), V(0.13f, 0.6f, 0.8f), V(0.75f, 0.5f, 0.7f), V(0.1f, 0.05f, 0.95f) },
				Accent = V(0.02f, 0.6f, 0.6f), Soil = new Color(0.6f, 0.56f, 0.45f),
			};
			return t;
		}

		/// <summary>The biome's own looks, falling in weight: name, hue/saturation/value offsets, weight, height, width, bend.</summary>
		/// <remarks>
		/// The common look is about two patches in five: a meadow is a meadow, with others through it rather than a quilt
		/// of twelve (2026-10-15: twelve near-equal looks read as "chunks of colours"). Their hue offsets are gentle for the
		/// same reason (a rarer look is a tint of the common one, not another plant); lightness and saturation carry most of
		/// the difference.
		/// </remarks>
		private static readonly (string name, float dh, float ds, float dv, float weight, float height, float width, float bend)[] Own =
		{
			("Common", 0f, 0f, 0f, 3f, 1f, 1f, 1f),
			("Lush", 0.012f, 0.1f, -0.08f, 0.9f, 1.15f, 1.05f, 0.95f),
			("Sun-bleached", -0.025f, -0.18f, 0.14f, 0.75f, 0.85f, 0.9f, 1.1f),
			("Blue-tinged", 0.03f, -0.02f, -0.02f, 0.55f, 1f, 1.15f, 1f),
			("Golden", -0.05f, 0.05f, 0.08f, 0.5f, 0.8f, 1.25f, 1.2f),
			("Silvered", 0.006f, -0.28f, 0.08f, 0.35f, 0.7f, 0.8f, 1.3f),
			("Russet", -0.08f, 0.08f, -0.06f, 0.35f, 1.05f, 1f, 1f),
			("Young", -0.006f, 0.18f, 0.16f, 0.4f, 0.6f, 1.1f, 0.8f),
			("Shade", 0.04f, -0.04f, -0.16f, 0.45f, 1.3f, 0.9f, 0.9f),
			("Tall", -0.012f, 0.02f, -0.12f, 0.3f, 1.6f, 1.1f, 1.05f),
		};

		/// <summary>The fantastic touches, of which each biome has two: name and sRGB HSV.</summary>
		private static readonly (string name, Vector3 hsv)[] Fantastic =
		{
			("Moonsilver", V(0.55f, 0.08f, 0.88f)),
			("Arcane", V(0.77f, 0.55f, 0.65f)),
			("Gilded", V(0.12f, 0.7f, 0.82f)),
			("Bloodleaf", V(0.98f, 0.7f, 0.45f)),
			("Frostazure", V(0.54f, 0.5f, 0.78f)),
			("Emberleaf", V(0.04f, 0.8f, 0.62f)),
			("Witchmoss", V(0.42f, 0.75f, 0.35f)),
			("Duskbloom", V(0.85f, 0.45f, 0.4f)),
			("Faerie", V(0.48f, 0.4f, 0.85f)),
			("Copperleaf", V(0.07f, 0.6f, 0.55f)),
		};

		/// <summary>Weights of the two fantastic looks: rare, so they read as found.</summary>
		private const float FantasticWeightA = 0.1f, FantasticWeightB = 0.06f;

		// ── The looks ───────────────────────────────────────────────

		/// <summary>
		/// A biome's looks on a world, from the biome asset: its look class (<see cref="ClassOf(BiomeTemplate)"/>) and its
		/// choice of rare looks (<see cref="BiomeTemplate.RareLookA"/>, <see cref="BiomeTemplate.RareLookB"/>), told apart from
		/// its kin by its key.
		/// </summary>
		public static Look Of(BiomeTemplate biome, in WorldTraits world)
		{
			if (biome == null)
			{
				return For(BiomeLookClass.Temperate, 0u, world);
			}
			return For(ClassOf(biome), Hash(biome.Key), world, biome.RareLookA, biome.RareLookB);
		}

		/// <summary>The fantastic looks' names, in <see cref="BiomeRareLook"/>'s order (less its <c>Auto</c>).</summary>
		public static string FantasticName(int index) => index >= 0 && index < Fantastic.Length ? Fantastic[index].name : null;

		/// <summary>How many fantastic looks there are to choose a biome's two from.</summary>
		public static int FantasticCount => Fantastic.Length;

		/// <summary>
		/// The looks of a biome of class <paramref name="c"/>, told apart from its kin by <paramref name="biomeHash"/>, on a world.
		/// <paramref name="rareA"/> and <paramref name="rareB"/> name its two rare fantastic looks outright; <see cref="BiomeRareLook.Auto"/>
		/// (the default) leaves each to the hash, so a biome that chooses nothing has exactly the looks it always had.
		/// </summary>
		public static Look For(BiomeLookClass c, uint biomeHash, in WorldTraits world, BiomeRareLook rareA = BiomeRareLook.Auto, BiomeRareLook rareB = BiomeRareLook.Auto)
		{
			if ((int)c >= Themes.Length)
			{
				c = BiomeLookClass.Temperate;
			}
			Look look = Build(c, biomeHash, world, rareA, rareB);
			// The same biome on the home world under the reference sun, with this world's seed: the shift is what the planet
			// changes, never the per-world nudge every biome takes anyway. (The common look, which the rare choice never moves.)
			WorldTraits home = WorldTraits.Earthlike;
			home.Seed = world.Seed;
			Color reference = Build(c, biomeHash, home, rareA, rareB).Variants[0].Foliage;
			for (int i = 0; i < Variants; i++)
			{
				look.Variants[i].Shift = ShiftOf(look.Variants[i].Foliage, reference);
			}
			return look;
		}

		/// <summary>
		/// A linear multiplier taking <paramref name="home"/> (sRGB) to <paramref name="foliage"/> (sRGB): per channel, then its
		/// brightness held within 0.7 … 1.35 and each channel within 0.2 … 5, so a shift tints and never blacks out or blazes.
		/// </summary>
		public static Color ShiftOf(Color foliage, Color home)
		{
			float r = Clamp(ToLinear(foliage.r) / Math.Max(ToLinear(home.r), 1e-3f), 0.2f, 5f);
			float g = Clamp(ToLinear(foliage.g) / Math.Max(ToLinear(home.g), 1e-3f), 0.2f, 5f);
			float b = Clamp(ToLinear(foliage.b) / Math.Max(ToLinear(home.b), 1e-3f), 0.2f, 5f);
			float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
			float scale = Clamp(luminance, 0.7f, 1.35f) / Math.Max(luminance, 1e-4f);
			return new Color(r * scale, g * scale, b * scale, 1f);
		}

		private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

		/// <summary>The sRGB transfer curve, decoded (managed: the shift is worked out off the engine too).</summary>
		private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);

		private static Look Build(BiomeLookClass c, uint biomeHash, in WorldTraits world, BiomeRareLook rareA, BiomeRareLook rareB)
		{
			Theme theme = Themes[(int)c];
			uint seed = biomeHash * 0x9E3779B1u ^ world.Seed * 0x85EBCA77u ^ 0x2545F491u;

			// The world moves the theme. (A crater or an impact basin is classed by its climate: on a living world it grows.)
			Vector3 foliage = theme.Foliage;
			float height = theme.Height, width = 1f, spread = theme.Spread;
			WorldShift(ref foliage, ref height, ref width, ref spread, world, c);
			Vector3 accent = theme.Accent;
			Color soil = theme.Soil;
			if (world.InternalHeat > 0.5f)
			{
				float ember = Mathf.InverseLerp(0.5f, 1f, world.InternalHeat) * 0.5f;
				accent = LerpHsv(accent, V(0.02f, 0.7f, 0.45f), ember);
				soil = Color.Lerp(soil, new Color(0.14f, 0.12f, 0.12f), ember);
			}
			if (Cryogenic(world))
			{
				accent = LerpHsv(accent, Themes[(int)BiomeLookClass.Alien].Accent, 0.6f);
				soil = Color.Lerp(soil, Themes[(int)BiomeLookClass.Alien].Soil, 0.5f);
			}
			// Each biome its own: a nudge of its hue, and its own pair of the fantastic.
			foliage.x = Wrap(foliage.x + (Unit(seed, 1u) - 0.5f) * 0.04f * spread);
			// The pair is the biome's to choose (BiomeTemplate.RareLookA/B); what it leaves to the hash is drawn as it always
			// was, and the second never repeats the first.
			int fantasticA = rareA != BiomeRareLook.Auto && (int)rareA <= Fantastic.Length ? (int)rareA - 1 : (int)(Next(seed, 2u) % (uint)Fantastic.Length);
			int fantasticB = rareB != BiomeRareLook.Auto && (int)rareB <= Fantastic.Length && (int)rareB - 1 != fantasticA ? (int)rareB - 1
				: (int)((fantasticA + 1 + Next(seed, 3u) % (uint)(Fantastic.Length - 1)) % (uint)Fantastic.Length);

			var look = new Look { Class = c, Variants = new Variant[Variants], Cumulative = new float[Variants] };
			Vector3[] blooms = WorldBlooms(theme.Blooms, world);
			int bloomStart = (int)(Next(seed, 4u) % (uint)blooms.Length);
			for (int i = 0; i < Variants; i++)
			{
				uint vs = seed + (uint)i * 0x27D4EB2Fu;
				Variant v;
				// Filled in by For against the home world; white until then.
				v.Shift = Color.white;
				Vector3 leaf;
				if (i < FirstFantastic)
				{
					var o = Own[i];
					v.Name = o.name;
					v.Weight = o.weight;
					leaf = Clamp(V(Wrap(foliage.x + o.dh * spread), foliage.y + o.ds, foliage.z + o.dv));
					v.Height = height * o.height;
					v.Width = width * o.width;
					v.Bend = o.bend;
				}
				else
				{
					var f = Fantastic[i == FirstFantastic ? fantasticA : fantasticB];
					v.Name = f.name;
					v.Weight = i == FirstFantastic ? FantasticWeightA : FantasticWeightB;
					// Fantastic, but grown on this world: a quarter of the way back to its leaves.
					leaf = LerpHsv(f.hsv, foliage, 0.25f);
					v.Height = height * (i == FirstFantastic ? 1.1f : 0.9f);
					v.Width = width;
					v.Bend = 1f;
				}
				// A little of its own on top, so no two patches are quite alike.
				leaf.x = Wrap(leaf.x + (Unit(vs, 5u) - 0.5f) * 0.02f);
				leaf.z = Mathf.Clamp01(leaf.z + (Unit(vs, 6u) - 0.5f) * 0.06f);
				v.Foliage = Hsv(leaf);
				v.Root = Hsv(V(Wrap(leaf.x - 0.02f), Mathf.Clamp01(leaf.y * 0.9f), leaf.z * 0.45f));

				Vector3 bloom = blooms[(bloomStart + i * 5) % blooms.Length];
				bloom.x = Wrap(bloom.x + (Unit(vs, 7u) - 0.5f) * 0.05f);
				bloom.y = Mathf.Clamp01(bloom.y + (Unit(vs, 8u) - 0.5f) * 0.3f);
				bloom.z = Mathf.Clamp01(bloom.z - 0.2f * Unit(vs, 19u));
				if (i >= FirstFantastic)
				{
					bloom = LerpHsv(bloom, Fantastic[i == FirstFantastic ? fantasticA : fantasticB].hsv, 0.6f);
					bloom.z = Mathf.Max(bloom.z, 0.85f);
				}
				v.Bloom = Hsv(Clamp(bloom));
				v.BloomSize = 0.8f + 0.5f * Unit(vs, 9u) + (i >= FirstFantastic ? 0.2f : 0f);

				Vector3 cap = V(Wrap(accent.x + (Unit(vs, 10u) - 0.5f) * 0.12f * spread), accent.y + (Unit(vs, 11u) - 0.5f) * 0.3f,
					accent.z + (Unit(vs, 12u) - 0.5f) * 0.3f);
				if (i >= FirstFantastic)
				{
					cap = LerpHsv(cap, Fantastic[i == FirstFantastic ? fantasticA : fantasticB].hsv, 0.5f);
				}
				v.Accent = Hsv(Clamp(cap));

				// Stone takes a little of the soil it lies in, and the fantastic ones a vein of their colour.
				Color stone = Color.Lerp(Color.white, Normalised(soil), 0.3f);
				float grain = 0.92f + 0.16f * Unit(vs, 13u);
				v.Mineral = new Color(stone.r * grain, stone.g * grain, stone.b * grain, 1f);
				if (i >= FirstFantastic)
				{
					v.Mineral = Color.Lerp(v.Mineral, Normalised(Hsv(Fantastic[i == FirstFantastic ? fantasticA : fantasticB].hsv)), 0.25f);
				}

				// Animals wear the ground and the green they hide in; their markings borrow the flowers.
				// Five sources a coat is drawn from, in turn: the soil, the leaves, the soil darker and paler, the fungi.
				Color source;
				switch ((i + (int)(Next(seed, 18u) % 5u)) % 5)
				{
					case 0: source = soil; break;
					case 1: source = v.Root * 1.8f; break;
					case 2: source = soil * 0.45f; break;
					case 3: source = Color.Lerp(soil, new Color(0.9f, 0.88f, 0.82f), 0.5f); break;
					default: source = Color.Lerp(soil, v.Accent, 0.6f); break;
				}
				float hide = 0.25f + 0.5f * Unit(vs, 14u);
				Color coat = Color.Lerp(soil, source, 0.5f + 0.5f * hide);
				// Half its brightness its own, so even on black ground some run dark and some sandy.
				Color.RGBToHSV(coat, out float coatH, out float coatS, out float coatV);
				coat = Hsv(V(coatH, coatS, Mathf.Lerp(coatV, Mathf.Lerp(0.15f, 0.8f, Unit(vs, 15u)), 0.5f)));
				if (i >= FirstFantastic)
				{
					coat = Color.Lerp(coat, Hsv(Fantastic[i == FirstFantastic ? fantasticA : fantasticB].hsv), 0.45f);
				}
				v.Coat = coat;
				v.Belly = Color.Lerp(coat, new Color(0.92f, 0.88f, 0.8f), 0.45f);
				Color bloomRgb = v.Bloom;
				v.Marking = (i & 1) == 0 ? Color.Lerp(bloomRgb, coat, 0.4f) : coat * 0.35f;
				v.Marking.a = 1f;
				v.Pattern = (CoatPattern)((i + (int)(Next(seed, 16u) % 5u)) % 5);
				v.Size = 0.85f + 0.35f * Unit(vs, 17u);
				look.Variants[i] = v;
			}
			float total = 0f;
			for (int i = 0; i < Variants; i++)
			{
				total += look.Variants[i].Weight;
			}
			float running = 0f;
			for (int i = 0; i < Variants; i++)
			{
				running += look.Variants[i].Weight / total;
				look.Cumulative[i] = i == Variants - 1 ? 1f : running;
			}
			return look;
		}

		/// <summary>True for a world whose sky snows something other than water or acid: nothing earthly grows there.</summary>
		public static bool Cryogenic(in WorldTraits world)
			=> world.Condensate == Condensate.Methane || world.Condensate == Condensate.Nitrogen || world.Condensate == Condensate.Ammonia;

		/// <summary>
		/// The world's pull on a theme's leaves, height and width. The star first: leaves absorb where their light is richest
		/// (red dwarfs: across the whole visible, so dark; hot stars: the blue, so they give back teal); then the air,
		/// the condensate, and the warmth.
		/// </summary>
		private static void WorldShift(ref Vector3 foliage, ref float height, ref float width, ref float spread, in WorldTraits world, BiomeLookClass c)
		{
			float k = world.StarKelvin > 0f ? world.StarKelvin : StarBody.ReferenceTemperatureK;
			float cool = Smooth(5800f, 4000f, k);
			foliage.x = Wrap(foliage.x - 0.07f * cool);
			foliage.y = Mathf.Clamp01(foliage.y + 0.05f * cool);
			float red = Smooth(4000f, 3000f, k);
			if (red > 0f)
			{
				foliage = LerpHsv(foliage, V(0.88f, 0.45f, 0.22f), 0.8f * red);
			}
			float hot = Smooth(6000f, 9000f, k);
			if (hot > 0f)
			{
				foliage = LerpHsv(foliage, V(0.48f, foliage.y, foliage.z), 0.75f * hot);
			}
			switch (world.Atmosphere)
			{
				case AtmosphereKind.None:
					height *= 0.5f;
					foliage.y *= 0.6f;
					break;
				case AtmosphereKind.Thin:
					// Waxy and low against the light the air no longer stops.
					height *= 0.6f;
					foliage.y *= 0.7f;
					foliage.z = Mathf.Clamp01(foliage.z + 0.08f);
					break;
				case AtmosphereKind.Thick:
					height *= 1.25f;
					width *= 1.2f;
					foliage.y = Mathf.Clamp01(foliage.y + 0.05f);
					foliage.z = Mathf.Clamp01(foliage.z - 0.05f);
					break;
			}
			if (Cryogenic(world) && c != BiomeLookClass.Alien)
			{
				foliage = LerpHsv(foliage, Themes[(int)BiomeLookClass.Alien].Foliage, 0.9f);
				spread *= 1.2f;
			}
			else if (world.Condensate == Condensate.SulphuricAcid)
			{
				foliage = LerpHsv(foliage, V(0.17f, 0.5f, 0.5f), 0.5f);
			}
			// Frost-hardy: blue-green and silver on a frozen world; gold on a hot one; dusty on a dry one.
			float frozen = Smooth(-0.5f, -2f, world.MeanTemperature);
			if (frozen > 0f)
			{
				foliage = LerpHsv(foliage, V(0.45f, foliage.y * 0.85f, foliage.z), 0.25f * frozen);
			}
			float baked = Smooth(1f, 2.5f, world.MeanTemperature);
			if (baked > 0f)
			{
				foliage = LerpHsv(foliage, V(0.12f, foliage.y, Mathf.Clamp01(foliage.z + 0.05f)), 0.2f * baked);
			}
			if (world.Water < 0.1f)
			{
				foliage.y *= 0.85f;
			}
			foliage = Clamp(foliage);
		}

		/// <summary>The theme's flowers on a world: under a sky that snows methane, they glow.</summary>
		private static Vector3[] WorldBlooms(Vector3[] blooms, in WorldTraits world)
		{
			if (!Cryogenic(world))
			{
				return blooms;
			}
			Vector3[] glow = Themes[(int)BiomeLookClass.Cave].Blooms;
			var moved = new Vector3[blooms.Length];
			for (int i = 0; i < blooms.Length; i++)
			{
				moved[i] = LerpHsv(blooms[i], glow[i % glow.Length], 0.6f);
			}
			return moved;
		}

		// ── Colour helpers ──────────────────────────────────────────

		private static Color Hsv(Vector3 hsv)
		{
			Color c = Color.HSVToRGB(Wrap(hsv.x), Mathf.Clamp01(hsv.y), Mathf.Clamp01(hsv.z));
			c.a = 1f;
			return c;
		}

		private static Vector3 Clamp(Vector3 hsv) => V(Wrap(hsv.x), Mathf.Clamp01(hsv.y), Mathf.Clamp01(hsv.z));

		/// <summary>A lerp of HSV that goes the short way round the hue circle.</summary>
		private static Vector3 LerpHsv(Vector3 a, Vector3 b, float t)
		{
			float dh = b.x - a.x;
			dh -= Mathf.Round(dh);
			return V(Wrap(a.x + dh * t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.z, b.z, t));
		}

		/// <summary>A colour scaled so its brightest channel is 1: its hue and saturation alone.</summary>
		private static Color Normalised(Color c)
		{
			float m = Mathf.Max(1e-4f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
			return new Color(c.r / m, c.g / m, c.b / m, 1f);
		}

		private static float Wrap(float h) => h - Mathf.Floor(h);

		private static float Smooth(float from, float to, float x)
		{
			float t = Mathf.Clamp01((x - from) / (to - from));
			return t * t * (3f - 2f * t);
		}

		// ── Hashing ─────────────────────────────────────────────────

		/// <summary>FNV-1a of a string: stable across runs and platforms (string.GetHashCode is not).</summary>
		public static uint Hash(string text)
		{
			uint h = 2166136261u;
			if (text != null)
			{
				foreach (char ch in text)
				{
					h = (h ^ ch) * 16777619u;
				}
			}
			return h;
		}

		private static uint Next(uint seed, uint salt)
		{
			uint x = seed ^ (salt * 0x9E3779B9u);
			x ^= x >> 16;
			x *= 0x7FEB352Du;
			x ^= x >> 15;
			x *= 0x846CA68Bu;
			x ^= x >> 16;
			return x;
		}

		private static float Unit(uint seed, uint salt) => (Next(seed, salt) >> 8) * (1f / 16777216f);
	}
}
