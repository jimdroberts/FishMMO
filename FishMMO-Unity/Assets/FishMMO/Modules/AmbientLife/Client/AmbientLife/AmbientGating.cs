using System;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>The moment the ambient life is drawn in: light, air and season at the viewer (AmbientLifeSystem builds it once a frame).</summary>
	public struct AmbientConditions
	{
		/// <summary>Daylight, 0 night .. 1 full day.</summary>
		public float Light;
		/// <summary>How near dawn or dusk it is, 0 .. 1 (the sun at the horizon).</summary>
		public float Twilight;
		/// <summary>True in the evening half of the day (dusk, not dawn): bats leave the roost at dusk.</summary>
		public bool Evening;
		/// <summary>Live air temperature at the viewer, -1..1 (0 = 0 °C, 1 = 33.1 °C).</summary>
		public float Temperature;
		/// <summary>How hard it is raining, 0..1 (precipitation × its rain share).</summary>
		public float Rain;
		/// <summary>How hard it is snowing (or hailing), 0..1.</summary>
		public float Snow;
		/// <summary>The storm's severity, 0..1 (WeatherFrame.StormSeverity).</summary>
		public float Storm;
		/// <summary>Wind, 0 calm .. 1 = 30 m/s.</summary>
		public float Wind;
		/// <summary>Cloud cover, 0..1.</summary>
		public float Cloud;
		/// <summary>Local summer, -1 deep winter .. 1 high summer (<c>_FishSeason.y</c>).</summary>
		public float Summer;

		/// <summary>A mild, dry, clear summer's day at mid-morning: what a scene without weather gets.</summary>
		public static AmbientConditions MildDay => new AmbientConditions { Light = 1f, Twilight = 0f, Temperature = AmbientLifeCatalogue.Celsius(17f), Summer = 0.5f };

		/// <summary>
		/// Light and twilight from the scene's local solar time (0.5 = noon) and its daylight flag. The sun's height
		/// is taken as -cos(2π·time), which leaves out latitude and season; the daylight flag (which has them) wins
		/// where the two disagree, so a polar summer night is still day.
		/// </summary>
		public static void Daylight(double localTime01, bool isDaylight, out float light, out float twilight, out bool evening)
		{
			float t = (float)(localTime01 - Math.Floor(localTime01));
			float sun = -Mathf.Cos(t * Mathf.PI * 2f);
			// Full light once the sun is a little up; the last of it goes in civil twilight.
			light = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.3f, sun));
			light = isDaylight ? Mathf.Max(light, 0.35f) : Mathf.Min(light, 0.3f);
			twilight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(sun) / 0.25f);
			evening = t > 0.5f;
		}
	}

	/// <summary>
	/// Which ambient creatures belong where and when: habitat from the biome's template, how much at home a kind is at a
	/// place, how many are about in the moment's light and weather, and when one takes fright. Pure functions, so
	/// the rules are tested without a scene.
	/// </summary>
	public static class AmbientGating
	{
		/// <summary>A storm this severe sends everything to cover.</summary>
		public const float StormShelter = 0.65f;

		// ── Habitat ───────────────────────────────────────────────────

		/// <summary>Names of ground nothing lives on, on any world: molten rock, poison, radiation, the wasteland.</summary>
		private static readonly string[] Lifeless =
		{
			"molten", "radiation", "sulphur", "sulfur", "runaway", "tidal fracture", "subsurface", "cloud deck", "wasteland", "lava tube",
		};

		/// <summary>
		/// Names of other worlds' open ground (<see cref="AmbientHabitat.Alien"/>): lifeless to the home world's animals,
		/// home to the creatures of thin and frozen air.
		/// </summary>
		private static readonly string[] AlienGround =
		{
			"dust sea", "impact", "regolith", "cryovolcanic", "tholin", "nitrogen", "methane", "rille",
		};

		// ── World ─────────────────────────────────────────────────────

		/// <summary>
		/// What lives on a world, from the air its planet or moon has: none without air, on molten ground or under a
		/// runaway greenhouse; the crawlers of thin air where the air is thin; the creatures of frozen air where the
		/// weather is methane, nitrogen or ammonia; otherwise the home world's animals, joined by the frozen air's on a
		/// world that never thaws anywhere (the home world's animals keep to whatever their climate bands allow there).
		/// </summary>
		/// <remarks>
		/// Thin air wins over frozen air: a wing needs dense air to beat in, whatever the air is made of.
		/// </remarks>
		public static AmbientWorld WorldOf(in BiomeWorldConditions world)
		{
			if (world.Atmosphere == AtmosphereKind.None || world.IsRunawayGreenhouse || world.HasMoltenRock)
			{
				return AmbientWorld.None;
			}
			if (world.Atmosphere == AtmosphereKind.Thin)
			{
				return AmbientWorld.ThinAir;
			}
			if (world.HasCryogenicAir || world.Condensate == Condensate.Ammonia)
			{
				return AmbientWorld.Cryogenic;
			}
			return world.WarmestTemperature <= BiomeWorldConditions.FreezingTemperature ? AmbientWorld.Earthly | AmbientWorld.Cryogenic : AmbientWorld.Earthly;
		}

		/// <summary>Whether a kind lives on a world at all.</summary>
		public static bool LivesOn(AmbientCreatureKind kind, AmbientWorld world)
		{
			return kind != null && (kind.Worlds & world) != 0;
		}

		/// <summary>Keywords in a biome's name and the country they mean, checked in order (the first few win over the last).</summary>
		private static readonly (string Key, AmbientHabitat Habitat)[] Keywords =
		{
			// Water first: "Coastal Water" is the sea, not the coast; "Ice Shelf" floats on it.
			("coastal water", AmbientHabitat.Sea), ("ocean", AmbientHabitat.Sea), ("reef", AmbientHabitat.Sea), ("abyss", AmbientHabitat.Sea),
			("seamount", AmbientHabitat.Sea), ("underwater", AmbientHabitat.Sea), ("trench", AmbientHabitat.Sea), ("vent", AmbientHabitat.Sea),
			("ice shelf", AmbientHabitat.Ice | AmbientHabitat.Coast), ("ice", AmbientHabitat.Ice), ("glacier", AmbientHabitat.Ice), ("snow", AmbientHabitat.Ice),
			("permafrost", AmbientHabitat.Ice | AmbientHabitat.Tundra),
			("mangrove", AmbientHabitat.Wetland | AmbientHabitat.Coast | AmbientHabitat.Forest),
			("estuary", AmbientHabitat.Wetland | AmbientHabitat.Coast), ("rocky coast", AmbientHabitat.Coast | AmbientHabitat.Mountain),
			("beach", AmbientHabitat.Coast), ("coast", AmbientHabitat.Coast),
			("bamboo", AmbientHabitat.Forest | AmbientHabitat.Jungle), ("jungle", AmbientHabitat.Jungle | AmbientHabitat.Forest),
			("rainforest", AmbientHabitat.Jungle | AmbientHabitat.Forest), ("taiga", AmbientHabitat.Forest), ("forest", AmbientHabitat.Forest),
			("woodland", AmbientHabitat.Woodland), ("savanna", AmbientHabitat.Grassland | AmbientHabitat.Scrub),
			("oasis", AmbientHabitat.Desert | AmbientHabitat.FreshWater | AmbientHabitat.Woodland),
			("peat", AmbientHabitat.Wetland | AmbientHabitat.Tundra), ("bog", AmbientHabitat.Wetland), ("swamp", AmbientHabitat.Wetland),
			("marsh", AmbientHabitat.Wetland), ("wetland", AmbientHabitat.Wetland), ("tundra", AmbientHabitat.Tundra | AmbientHabitat.Grassland),
			("alpine meadow", AmbientHabitat.Grassland | AmbientHabitat.Mountain), ("meadow", AmbientHabitat.Grassland),
			("farmland", AmbientHabitat.Farmland | AmbientHabitat.Grassland), ("farm", AmbientHabitat.Farmland),
			("grassland", AmbientHabitat.Grassland), ("plains", AmbientHabitat.Grassland), ("steppe", AmbientHabitat.Grassland | AmbientHabitat.Scrub),
			("prairie", AmbientHabitat.Grassland), ("valley", AmbientHabitat.Grassland | AmbientHabitat.Woodland),
			("hills", AmbientHabitat.Grassland | AmbientHabitat.Woodland | AmbientHabitat.Scrub), ("scrub", AmbientHabitat.Scrub),
			("karst", AmbientHabitat.Mountain | AmbientHabitat.Scrub), ("high desert", AmbientHabitat.Desert | AmbientHabitat.Scrub | AmbientHabitat.Mountain),
			("salt flat", AmbientHabitat.Desert), ("badlands", AmbientHabitat.Desert | AmbientHabitat.Mountain), ("desert", AmbientHabitat.Desert),
			("dune", AmbientHabitat.Desert), ("geyser", AmbientHabitat.Volcanic | AmbientHabitat.Grassland), ("volcan", AmbientHabitat.Volcanic | AmbientHabitat.Mountain),
			("crater", AmbientHabitat.Mountain | AmbientHabitat.Volcanic), ("castle", AmbientHabitat.Settlement | AmbientHabitat.Grassland),
			// A palace's gardens and courts (an ice palace is ice first, above).
			("palace", AmbientHabitat.Settlement | AmbientHabitat.Grassland),
			("fortress", AmbientHabitat.Settlement | AmbientHabitat.Mountain), ("town", AmbientHabitat.Settlement | AmbientHabitat.Farmland),
			("village", AmbientHabitat.Settlement | AmbientHabitat.Farmland), ("city", AmbientHabitat.Settlement), ("cave", AmbientHabitat.Cave),
			("alpine", AmbientHabitat.Mountain), ("mountain", AmbientHabitat.Mountain), ("scree", AmbientHabitat.Mountain), ("rock", AmbientHabitat.Mountain),
			("cliff", AmbientHabitat.Mountain), ("lake", AmbientHabitat.FreshWater), ("river", AmbientHabitat.FreshWater | AmbientHabitat.Grassland),
		};

		/// <summary>
		/// What a point of interest adds to the country it stands in: a village, town or camp people live in brings the
		/// birds and rats that live off people (and the fields round it); a temple or shrine the pigeons and sparrows of its
		/// courts; a ruin, a graveyard or an abandoned farm the owls, bats, crows and lizards of broken walls.
		/// </summary>
		public static AmbientHabitat HabitatOfSite(PointOfInterestGroup group, PointOfInterestTraits traits)
		{
			if ((traits & PointOfInterestTraits.Settlement) != 0)
			{
				return AmbientHabitat.Settlement | AmbientHabitat.Farmland;
			}
			switch (group)
			{
				case PointOfInterestGroup.Ruins:
				case PointOfInterestGroup.Dead:
					return AmbientHabitat.Ruins;
				case PointOfInterestGroup.Sacred:
					return AmbientHabitat.Settlement;
				default:
					return AmbientHabitat.None;
			}
		}

		/// <summary>
		/// The country a biome offers, from its asset name: keywords, so a biome added later under an ordinary name
		/// ("Birch Woodland", "Salt Marsh") is understood. None for a name no keyword matches (the caller falls back
		/// on the climate, <see cref="HabitatOfClimate"/>); Barren for lifeless ground. Since 2026-10-16 only the reading
		/// the spec table's habitats were written to equal, and the fallback for a template never authored
		/// (<see cref="HabitatOfTemplate"/>).
		/// </summary>
		public static AmbientHabitat HabitatOf(string biomeName)
		{
			if (string.IsNullOrEmpty(biomeName))
			{
				return AmbientHabitat.None;
			}
			string name = biomeName.ToLowerInvariant();
			foreach (string dead in Lifeless)
			{
				if (name.Contains(dead))
				{
					return AmbientHabitat.Barren;
				}
			}
			foreach (string alien in AlienGround)
			{
				if (name.Contains(alien))
				{
					return AmbientHabitat.Alien;
				}
			}
			foreach ((string key, AmbientHabitat habitat) in Keywords)
			{
				if (name.Contains(key))
				{
					return habitat;
				}
			}
			return AmbientHabitat.None;
		}

		/// <summary>
		/// The country a biome asset offers: its asset name read first (<see cref="HabitatOf"/>), then its display name; and
		/// ground that only forms under another world's air (its <paramref name="atmosphere"/> leaves out the home world's
		/// Standard) is <see cref="AmbientHabitat.Alien"/> as well, whatever else its name says: an ice geyser field is ice
		/// and alien ground, a nameless thin-air plain alien ground alone. None for a home-world biome no keyword matches
		/// (the caller falls back on the climate).
		/// </summary>
		/// <remarks>
		/// It used to call every biome without Standard air Barren outright, so a dust sea, a regolith plain, a cryovolcanic
		/// plain, a nitrogen ice field, an ice geyser field, an impact basin or a rille (Airless | Thin) never reached the
		/// alien-ground keywords, and the thin and frozen airs' creatures (whose own country that is) could never live there.
		/// Which world a kind lives on is the world's air (<see cref="WorldOf"/>), which already keeps every creature off an
		/// airless one; the lifeless names (molten, sulphur, radiation, the wasteland…) are still Barren on any world.
		/// </remarks>
		public static AmbientHabitat HabitatOfBiome(string assetName, string displayName, BiomeAtmosphereRequirement atmosphere)
		{
			AmbientHabitat habitat = HabitatOf(assetName);
			if (habitat == AmbientHabitat.None)
			{
				habitat = HabitatOf(displayName);
			}
			bool otherAir = atmosphere != BiomeAtmosphereRequirement.None && (atmosphere & BiomeAtmosphereRequirement.Standard) == 0;
			if (otherAir && (habitat & AmbientHabitat.Barren) == 0)
			{
				habitat |= AmbientHabitat.Alien;
			}
			return habitat;
		}

		/// <summary>
		/// The country a biome offers: its template's <see cref="BiomeTemplate.Habitat"/>, the one source since 2026-10-16 (the
		/// biome art authoring writes it from the spec table, which names for every biome what its name gave,
		/// <see cref="HabitatOfBiome"/>), with Alien read live from the template's air (<see cref="BiomeTemplate.LiveHabitat"/>,
		/// never stored). None for no biome, and for one authored as offering none on ground that is not alien (the caller falls
		/// back on the climate, as for a name no keyword matched). A template never authored is read from its name, with one
		/// warning a session, and takes its Alien bit from its air all the same.
		/// </summary>
		public static AmbientHabitat HabitatOfTemplate(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return AmbientHabitat.None;
			}
			if (biome.Habitat != BiomeHabitat.None || biome.IsAuthored(BiomeTemplate.Authored.Habitat))
			{
				return (AmbientHabitat)(int)biome.LiveHabitat;
			}
			AmbientHabitat named = HabitatOfBiome(biome.name, biome.ResolvedDisplayName, biome.Atmosphere);
			AmbientHabitat habitat = (AmbientHabitat)(int)BiomeHabitats.Live((BiomeHabitat)(int)named, biome.Atmosphere, biome.Requires);
			lock (warnLock)
			{
				if (!warnedFallback)
				{
					warnedFallback = true;
					Debug.LogWarning($"[Biome templates] '{biome.name}' has no wildlife habitat: read from its name as {habitat} (said once; other templates may be the same). Run Biomes → Authoring → Author biome art to write it onto the templates.");
				}
			}
			return habitat;
		}

		/// <summary>Whether a template without a habitat has been reported (once a session, as every template fallback is).</summary>
		private static bool warnedFallback;
		private static readonly object warnLock = new object();

		/// <summary>The country a climate would grow, for ground whose biome is unknown: cold tundra, dry desert or scrub, else grass and trees.</summary>
		public static AmbientHabitat HabitatOfClimate(float temperature, float humidity)
		{
			if (temperature < -0.55f)
			{
				return AmbientHabitat.Ice;
			}
			if (temperature < -0.2f)
			{
				return AmbientHabitat.Tundra | AmbientHabitat.Grassland;
			}
			if (humidity < -0.5f)
			{
				return temperature > 0.4f ? AmbientHabitat.Desert : AmbientHabitat.Scrub | AmbientHabitat.Grassland;
			}
			if (humidity > 0.4f)
			{
				return temperature > 0.6f ? AmbientHabitat.Jungle | AmbientHabitat.Forest : AmbientHabitat.Forest | AmbientHabitat.Woodland | AmbientHabitat.Wetland;
			}
			return AmbientHabitat.Grassland | AmbientHabitat.Woodland;
		}

		/// <summary>
		/// How much at home a kind is at a place: 0 (not at all) .. its preferred weight. The habitat must be one it
		/// lives in; the painted climate must be in its band (soft edges a tenth wide); Barren ground has nothing, and
		/// snow and ice nothing but the seabirds that pass along an icy coast and the creatures of frozen worlds.
		/// Which world a kind lives on is asked first (<see cref="LivesOn"/>), by placement.
		/// </summary>
		/// <param name="seaNear">Sea within reach of the place (seabirds and shore crabs need it).</param>
		/// <param name="freshNear">A lake or river within reach (frogs, ducks).</param>
		/// <param name="treesNear">Trees in reach (squirrels).</param>
		public static float HabitatWeight(AmbientCreatureKind kind, AmbientHabitat habitat, float temperature, bool seaNear, bool freshNear, bool treesNear)
		{
			if (kind == null || !kind.Enabled || habitat == AmbientHabitat.None || (habitat & AmbientHabitat.Barren) != 0)
			{
				return 0f;
			}
			if ((kind.NeedsSea && !seaNear) || (kind.NeedsFreshWater && !freshNear) || (kind.NeedsTrees && !treesNear))
			{
				return 0f;
			}
			if ((habitat & AmbientHabitat.Ice) != 0 && (kind.Habitats & AmbientHabitat.Ice) == 0)
			{
				return 0f;
			}
			// Open sea: only what flies over it (or floats on it), never a ground animal.
			if (habitat == AmbientHabitat.Sea && (kind.Habitats & AmbientHabitat.Sea) == 0)
			{
				return 0f;
			}
			if ((habitat & kind.Habitats) == 0)
			{
				return 0f;
			}
			float warm = kind.Climate.x <= -1f ? 1f : Mathf.Clamp01((temperature - kind.Climate.x) / 0.1f + 0.5f);
			float cool = kind.Climate.y >= 1f ? 1f : Mathf.Clamp01((kind.Climate.y - temperature) / 0.1f + 0.5f);
			float climate = Mathf.Min(warm, cool);
			float place = (habitat & kind.Prefers) != 0 ? Mathf.Max(1f, kind.PreferWeight) : 1f;
			// Snow and ice keep only a sprinkling of seabirds along the shore; what is at home on the ice is not thinned.
			if ((habitat & AmbientHabitat.Ice) != 0 && (kind.Prefers & AmbientHabitat.Ice) == 0)
			{
				place *= 0.15f;
			}
			return place * climate;
		}

		// ── Activity ──────────────────────────────────────────────────

		/// <summary>
		/// The share of a kind's animals about in the moment, 0..1: the hour (its daily rhythm), the cold (a limit
		/// below which none are about), the heat (most shelter from a hot noon; heat lovers come out for it), rain
		/// (most shelter; frogs come out), snow, wind (it grounds what flies), the season (hibernators gone in
		/// winter, migrants fewer) and storms (everything takes cover).
		/// </summary>
		/// <remarks>
		/// Each group draws a fixed rank when it is placed and is drawn while its rank is under this share, so as the
		/// light goes the bats come out one group at a time and every player sees the same ones.
		/// </remarks>
		/// <summary>
		/// The share of a nocturnal ground creature's night numbers about in full daylight: the odd rat disturbed from its
		/// nest, no more. It was a third (2026-10-10, so a noon walk was not empty of them), which put rats, mice, toads and
		/// frogs about all day; the day now has its own animals (2026-10-14).
		/// </summary>
		public const float DaytimeNocturnalShare = 0.04f;

		/// <summary>The share of a crepuscular kind's numbers about at noon (rabbits lie up in the day) and in the dark.</summary>
		public const float CrepuscularNoonShare = 0.06f, CrepuscularNightShare = 0.4f;

		public static float Activity(AmbientCreatureKind kind, in AmbientConditions c)
		{
			if (kind == null || !kind.Enabled || c.Storm >= StormShelter)
			{
				return 0f;
			}
			float hour;
			switch (kind.Activity)
			{
				case AmbientActivity.Diurnal:
					// Birds sing and feed hardest in the first light; a few stir on a bright night, none in the dark.
					hour = Mathf.Lerp(0f, 1f, Mathf.InverseLerp(0.2f, 0.75f, c.Light)) * (1f + (c.Evening ? 0.1f : 0.35f) * c.Twilight * c.Light);
					break;
				case AmbientActivity.Nocturnal:
					// The night's: the odd ground animal turned out by day, and nothing that flies (an owl sleeps in its tree,
					// fireflies and moths keep to the dark).
					hour = Mathf.Lerp(1f, kind.Flies ? 0f : DaytimeNocturnalShare, c.Light);
					break;
				case AmbientActivity.Crepuscular:
					// Dawn and dusk: lying up through the day, a fair number out through the night.
					hour = Mathf.Lerp(CrepuscularNightShare, CrepuscularNoonShare, c.Light) + 0.7f * c.Twilight;
					break;
				case AmbientActivity.Dusk:
					// Out as the light goes, busiest in the first hours of dark, back before dawn.
					hour = (1f - c.Light) * (c.Evening ? 0.75f + 0.35f * c.Twilight : 0.6f);
					break;
				default:
					hour = 1f;
					break;
			}
			float cold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(kind.ColdLimit - 0.04f, kind.ColdLimit + 0.06f, c.Temperature));
			float heat;
			if (kind.HeatLover)
			{
				// Warmth gets an ectotherm going; a hot sunny noon is when it basks in the open.
				heat = 0.6f + 0.6f * Mathf.InverseLerp(0.45f, 0.9f, c.Temperature) * c.Light;
			}
			else
			{
				// Past about 28 °C in the sun, mammals and birds wait out the heat in the shade.
				heat = 1f - 0.8f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.84f, 1f, c.Temperature)) * c.Light;
			}
			float wet;
			if (kind.RainLover)
			{
				wet = 1f + 1.5f * c.Rain;
			}
			else if (kind.RainLimit > 0f)
			{
				wet = Mathf.Clamp01(1f - c.Rain / kind.RainLimit);
			}
			else
			{
				wet = 1f;
			}
			float snow = 1f - 0.85f * Mathf.Clamp01(c.Snow * 1.5f);
			// What flies is grounded by wind: birds in a gale, bats and big insects in a strong breeze, and a swarm by little
			// more than a breath (a midge column breaks up past about 3 m/s).
			float calm = kind.Family == AmbientFamily.Swarms ? 0.1f : kind.Family == AmbientFamily.Bats ? 0.3f : 0.5f;
			float gale = kind.Family == AmbientFamily.Swarms ? 0.25f : kind.Family == AmbientFamily.Bats ? 0.55f : 0.85f;
			float wind = kind.Flies ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(calm, gale, c.Wind)) : 1f;
			float winter = Mathf.Clamp01(-c.Summer);
			float season = 1f;
			if (kind.Hibernates)
			{
				season = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, winter));
			}
			else if (kind.Migrates)
			{
				season = 1f - 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.8f, winter));
			}
			float thermals = 1f;
			if (kind.NeedsThermals)
			{
				// Thermals rise off sun-warmed ground: strong under a clear sky toward midday, none under overcast.
				float sun = c.Light * (1f - 0.85f * c.Cloud) * (1f - Mathf.Clamp01(c.Rain * 4f));
				thermals = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, sun));
			}
			float storm = 1f - Mathf.InverseLerp(0.35f, StormShelter, c.Storm);
			return Mathf.Clamp01(hour * cold * heat * wet * snow * wind * season * thermals * storm);
		}

		/// <summary>
		/// Birds out in the rain sit it out under cover rather than fly: the share (0..1) of a flock's or a bird's
		/// time it spends perched instead of aloft or on open ground. Night roosts them the same way.
		/// </summary>
		public static float Shelter(in AmbientConditions c)
		{
			return Mathf.Clamp01(Mathf.Max(Mathf.InverseLerp(0.1f, 0.35f, c.Rain + c.Snow), Mathf.InverseLerp(0.4f, 0.15f, c.Light)));
		}

		/// <summary>
		/// <see cref="Shelter(in AmbientConditions)"/> for one kind: a night bird (an owl) roosts by day instead, and is
		/// out hunting in the dark.
		/// </summary>
		public static float Shelter(AmbientCreatureKind kind, in AmbientConditions c)
		{
			if (kind == null || kind.Activity != AmbientActivity.Nocturnal)
			{
				return Shelter(c);
			}
			return Mathf.Clamp01(Mathf.Max(Mathf.InverseLerp(0.1f, 0.35f, c.Rain + c.Snow), Mathf.InverseLerp(0.5f, 0.8f, c.Light)));
		}

		// ── Fear ──────────────────────────────────────────────────────

		/// <summary>
		/// The distance at which a kind takes fright from someone approaching at a speed: its flight initiation
		/// distance, longer the faster the approach (field studies: a running person flushes birds at up to half as
		/// far again as a walking one). 0 for kinds that never flush.
		/// </summary>
		public static float FlightDistance(AmbientCreatureKind kind, float approachSpeed)
		{
			if (kind == null || kind.FlushMetres <= 0f)
			{
				return 0f;
			}
			return kind.FlushMetres * (1f + 0.5f * Mathf.Clamp01(approachSpeed / 6f));
		}

		/// <summary>
		/// Whether an animal takes fright from someone at <paramref name="threat"/>. Birds aloft and bats never flush
		/// (they are already flying). Height protects: a bird high in a tree lets people come closer than one on the
		/// ground (flight distance falls with perch height), so metres of height count for more than metres across.
		/// </summary>
		public static bool ShouldFlush(AmbientCreatureKind kind, Vector3 animal, Vector3 threat, float approachSpeed, bool airborne)
		{
			if (airborne)
			{
				return false;
			}
			float reach = FlightDistance(kind, approachSpeed);
			if (reach <= 0f)
			{
				return false;
			}
			float dx = animal.x - threat.x, dz = animal.z - threat.z;
			float dy = (animal.y - threat.y) * 1.6f;
			return dx * dx + dy * dy + dz * dz < reach * reach;
		}

		/// <summary>
		/// The way an animal flees: straight away from the threat, turned up to 40° either side by its own seed (a
		/// rabbit does not run the same line twice), horizontal and normalised.
		/// </summary>
		public static Vector3 FleeDirection(Vector3 animal, Vector3 threat, uint seed)
		{
			var away = new Vector3(animal.x - threat.x, 0f, animal.z - threat.z);
			if (away.sqrMagnitude < 1e-6f)
			{
				float a = SeaLifePlacement.Unit(seed) * Mathf.PI * 2f;
				away = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			}
			away.Normalize();
			float turn = (SeaLifePlacement.Unit(SeaLifePlacement.Mix(seed ^ 0xF1EEu)) * 2f - 1f) * 40f * Mathf.Deg2Rad;
			float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
			return new Vector3(away.x * c - away.z * s, 0f, away.x * s + away.z * c);
		}
	}
}
