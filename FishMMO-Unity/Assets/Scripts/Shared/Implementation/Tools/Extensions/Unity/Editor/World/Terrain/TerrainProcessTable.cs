#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How each biome's ground wears, as one explicit table: the shared terrain process profiles
	/// (<see cref="TerrainProcessProfile"/>) and which biome wears which, written onto the assets by a
	/// re-runnable Dashboard button.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Grouped by how ground wears, not by what grows on it.</b> Erosion cares whether roots bind
	/// the soil, whether rain comes as downpours, whether hollows drain underground and whether the
	/// rock lies in flat beds — so a temperate forest and a valley share a profile, while karst, which
	/// looks like hills, gets its own because its water goes down instead of across. Thirteen
	/// profiles cover every biome the climate places.
	/// </para>
	/// <para>
	/// <b>Starting values, to be tuned against the result.</b> The profiles are relative to temperate
	/// soil (1, or <see cref="TerrainProcess.Temperate"/>) and set from how each landscape is known to
	/// wear: badlands erode several times faster than vegetated soil and drain through the densest
	/// gully networks of any ground; karst carries almost no surface channels; periglacial slopes
	/// creep fastest. None of it shapes anything until the erosion pass reads it, which is where they
	/// will be calibrated.
	/// </para>
	/// <para>
	/// <b>Explicit, never guessed</b>, exactly as <see cref="BiomeSpecTable"/>: matched on exact asset
	/// names, every changed value logged before → after, and running it again writes nothing.
	/// Biomes not in the table — structures, caves and ruins, and the hand-placed Lake and River the
	/// hydrology pass will place — are never touched, and wear like temperate soil while empty.
	/// </para>
	/// </remarks>
	public static class TerrainProcessTable
	{
		/// <summary>Where the profile assets live; a profile names the file without its extension.</summary>
		public const string Folder = BiomeSpecTable.Folder + "/Terrain Processes";

		/// <summary>One named profile.</summary>
		public readonly struct Profile
		{
			public readonly string Name;
			public readonly string Description;
			public readonly TerrainProcess Values;

			public Profile(string name, string description, float erodibility, float cohesion, float storminess, float talus,
				float soil, float creep, float channel, float depressions, float lakes, float plateaus, float sand = 0f)
			{
				Name = name;
				Description = description;
				Values = new TerrainProcess
				{
					Erodibility = erodibility,
					VegetationCohesion = cohesion,
					Storminess = storminess,
					TalusAngleDegrees = talus,
					SoilDepthMetres = soil,
					SoilCreep = creep,
					ChannelThreshold = channel,
					DepressionKeeping = depressions,
					LakeRetention = lakes,
					PlateauShare = plateaus,
					DuneSand = sand,
				};
			}

			public string AssetPath => $"{Folder}/{Name}.asset";
		}

		/// <summary>
		/// The profiles. Columns: erodibility, vegetation cohesion, storminess, talus angle (°), soil
		/// depth (m), soil creep, channel threshold, depression keeping, lake retention, plateau share, dune sand.
		/// </summary>
		public static readonly Profile[] Profiles =
		{
			new Profile("Temperate", "Vegetated soil under steady rain: rounded hills, a branching drainage, ponds in the hollows. The reference every other profile is measured against.",
				1f, 0.5f, 1f, 34f, 1.5f, 1f, 1f, 0f, 0.6f, 0f),
			new Profile("Rainforest", "Deeply weathered soil under torrential rain, held by dense roots: steep V-shaped valleys and many streams.",
				1.2f, 0.7f, 1.6f, 36f, 3f, 1.4f, 0.7f, 0f, 0.5f, 0f),
			new Profile("Open", "Grass over soil: gentle slopes, sparse channels, shallow seasonal ponds; flat beds occasionally stand as low tables.",
				1f, 0.35f, 1.3f, 33f, 1.2f, 0.8f, 1.2f, 0f, 0.4f, 0.1f, 0.05f),
			new Profile("Arid", "Bare ground under rare flash floods: sharp gullies and dry washes, playas instead of lakes, flat-lying beds worn into benches, mesas and canyons.",
				1.4f, 0.05f, 2.2f, 35f, 0.3f, 0.4f, 0.9f, 0f, 0.1f, 0.8f, 0.6f),
			new Profile("Badlands", "Soft unvegetated beds: the fastest erosion and the densest gully network of any ground, every bed exposed as a ledge.",
				2.6f, 0f, 2.2f, 40f, 0.5f, 0.3f, 0.35f, 0f, 0.05f, 1f, 0.05f),
			new Profile("Mountain", "Thin soil over rock: frost and rockfall dominate, scree stands at its angle, tarns sit in the hollows.",
				0.8f, 0.15f, 1.2f, 37f, 0.3f, 0.6f, 0.9f, 0.05f, 0.7f, 0.2f),
			new Profile("Periglacial", "Frozen ground that thaws at the surface: slopes creep and slump, drainage is poor, lakes everywhere.",
				0.6f, 0.35f, 0.6f, 30f, 1f, 1.8f, 1.4f, 0.1f, 0.9f, 0f),
			new Profile("Wetland", "Waterlogged ground bound by reeds and peat: almost no gradient, broad slow channels, standing water in every hollow.",
				0.5f, 0.8f, 0.8f, 25f, 2f, 0.6f, 2f, 0f, 0.95f, 0f),
			new Profile("Karst", "Soluble rock: water sinks through it, so surface channels are rare, sinkholes stay open and cliffs stand steep.",
				0.7f, 0.5f, 1f, 42f, 0.5f, 0.5f, 3f, 1f, 0.1f, 0.3f),
			new Profile("Volcanic", "Young porous lava: rain soaks in rather than running off, so little is cut; flows stack in benches.",
				0.5f, 0.1f, 1f, 37f, 0.5f, 0.3f, 1.8f, 0.2f, 0.3f, 0.3f),
			new Profile("Coastal", "Sand and shingle above the tide: loose, quickly reworked, dunes at their angle of repose.",
				1.1f, 0.2f, 1.2f, 32f, 3f, 1f, 1.5f, 0f, 0.3f, 0f, 0.7f),
			new Profile("Barren", "Ground with no liquid on it — regolith, ice shells, frozen nitrogen: nothing flows, slopes only creep, and every crater stays a crater.",
				1f, 0f, 1f, 32f, 2f, 0.5f, 2f, 1f, 0f, 0f, 0.4f),
			new Profile("Seabed", "Under the sea: no rain and no runoff, only slow slumping; the hydrology passes leave it alone.",
				0f, 0.3f, 0f, 30f, 2f, 0.3f, 4f, 1f, 0f, 0f),
		};

		/// <summary>Which profile each biome wears, by exact biome asset name.</summary>
		public static readonly (string Biome, string Profile)[] Assignments =
		{
			// Seabed.
			("Deep Ocean", "Seabed"), ("Abyssal Plain", "Seabed"), ("Abyss", "Seabed"), ("Ocean", "Seabed"),
			("Underwater Canyon", "Seabed"), ("Seamount", "Seabed"), ("Coastal Water", "Seabed"), ("Coral Reef", "Seabed"),
			("Subsurface Ocean Vent", "Seabed"),

			// The shore.
			("Beach", "Coastal"), ("Rocky Coast", "Coastal"),
			("Estuary", "Wetland"), ("Mangrove", "Wetland"),

			// Lowland and highland.
			("Forest", "Temperate"), ("Woodland", "Temperate"), ("Valley", "Temperate"), ("Hills", "Temperate"),
			("Jungle", "Rainforest"), ("Bamboo Forest", "Rainforest"),
			("Plains", "Open"), ("Grassland", "Open"), ("Steppe", "Open"), ("Savanna", "Open"), ("Farmland", "Open"),
			("Desert", "Arid"), ("Scrubland", "Arid"), ("High Desert", "Arid"), ("Salt Flat", "Arid"), ("Wasteland", "Arid"),
			("Badlands", "Badlands"),
			("Wetlands", "Wetland"), ("Peat Bog", "Wetland"), ("Swamp", "Wetland"), ("Oasis", "Wetland"),
			("Tundra", "Periglacial"), ("Taiga", "Periglacial"), ("Snow", "Periglacial"),
			("Karst", "Karst"),

			// Mountains.
			("Alpine Meadow", "Mountain"), ("Mountain Slope", "Mountain"), ("Rocky Terrain", "Mountain"), ("Alpine", "Mountain"),
			("Scree", "Mountain"), ("Crater", "Mountain"),
			("Permanent Ice", "Periglacial"), ("Glacier", "Periglacial"), ("Ice Sheet", "Periglacial"),

			// Volcanic ground, on any world.
			("Volcanic", "Volcanic"), ("Geyser Basin", "Volcanic"), ("Sulphur Flats", "Volcanic"), ("Molten Surface", "Volcanic"),
			("Lava Tube", "Volcanic"), ("Runaway Greenhouse Plain", "Volcanic"), ("Sulphuric Cloud Deck", "Volcanic"),

			// Other worlds. Titan's tholin plains and methane seas have a liquid that rains, so they wear as their Earth counterparts do.
			("Tholin Plain", "Arid"), ("Methane Lake", "Wetland"),
			("Regolith Plain", "Barren"), ("Impact Basin", "Barren"), ("Rille", "Barren"), ("Dust Sea", "Barren"),
			("Radiation Plain", "Barren"), ("Nitrogen Ice Field", "Barren"), ("Cryovolcanic Plain", "Barren"),
			("Ice Geyser Field", "Barren"), ("Ice Shelf", "Barren"), ("Tidal Fracture", "Barren"),
		};

		/// <summary>The profile named <paramref name="name"/>; false when the table has none.</summary>
		public static bool TryGetProfile(string name, out Profile profile)
		{
			foreach (Profile p in Profiles)
			{
				if (p.Name == name)
				{
					profile = p;
					return true;
				}
			}
			profile = default;
			return false;
		}

		[DashboardTool(DashboardToolAttribute.Maintenance, "Apply terrain process profiles", Section = "Content", Order = 4,
			Tooltip = "Creates the shared terrain process profiles (how each kind of ground erodes, drains and breaks into benches) in Assets/Templates/Entity/Biomes/Terrain Processes, writes the tabled values onto them, and assigns each tabled biome its profile, logging every change. Re-running it changes nothing once applied; biomes not in the table are left alone.",
			Confirm = "Write the terrain process table: create or update the profiles in Assets/Templates/Entity/Biomes/Terrain Processes and assign them to the tabled biomes? Every change is logged before and after.")]
		public static void ApplyFromDashboard()
		{
			var log = new StringBuilder();
			int changes = Apply(true, log, out List<string> missing);
			if (changes > 0)
			{
				Debug.Log($"[Terrain processes] {changes} change(s) to {Profiles.Length} profiles and {Assignments.Length} tabled biomes.\n{log}");
			}
			else
			{
				Debug.Log($"[Terrain processes] All {Profiles.Length} profiles and {Assignments.Length - missing.Count} tabled biomes already match the table; nothing written.");
			}
			if (missing.Count > 0)
			{
				Debug.LogWarning($"[Terrain processes] {missing.Count} tabled biome(s) have no asset at {BiomeSpecTable.Folder}/<name>.asset and were skipped: {string.Join(", ", missing)}.");
			}
		}

		/// <summary>
		/// Writes the table onto the assets. Returns how many changes it made (or, with
		/// <paramref name="save"/> false, would make).
		/// </summary>
		/// <param name="save">False to report what would change without writing anything.</param>
		/// <param name="log">Receives one line per change, before → after.</param>
		/// <param name="missing">Tabled biome names with no asset: skipped.</param>
		public static int Apply(bool save, StringBuilder log, out List<string> missing)
		{
			missing = new List<string>();
			int changes = 0;
			var profiles = new Dictionary<string, TerrainProcessProfile>();

			if (save)
			{
				WorldEditorAssets.EnsureFolder(Folder);
			}
			foreach (Profile spec in Profiles)
			{
				var asset = AssetDatabase.LoadAssetAtPath<TerrainProcessProfile>(spec.AssetPath);
				if (asset == null)
				{
					log?.AppendLine($"  {spec.Name}: created at {spec.AssetPath}");
					changes++;
					if (!save)
					{
						continue;
					}
					asset = ScriptableObject.CreateInstance<TerrainProcessProfile>();
					asset.name = spec.Name;
					asset.Description = spec.Description;
					asset.Values = spec.Values;
					AssetDatabase.CreateAsset(asset, spec.AssetPath);
				}
				else
				{
					int changed = 0;
					if (asset.Description != spec.Description)
					{
						Note(log, spec.Name, "Description", asset.Description, spec.Description);
						changed++;
					}
					if (!asset.Values.SameAs(spec.Values))
					{
						Note(log, spec.Name, "Values", Describe(asset.Values), Describe(spec.Values));
						changed++;
					}
					if (changed > 0 && save)
					{
						Undo.RecordObject(asset, "Apply terrain process table");
						asset.Description = spec.Description;
						asset.Values = spec.Values;
						EditorUtility.SetDirty(asset);
					}
					changes += changed;
				}
				profiles[spec.Name] = asset;
			}

			foreach ((string biomeName, string profileName) in Assignments)
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>($"{BiomeSpecTable.Folder}/{biomeName}.asset");
				if (biome == null)
				{
					missing.Add(biomeName);
					continue;
				}
				profiles.TryGetValue(profileName, out TerrainProcessProfile profile);
				// A dry run has not created the missing profiles: compare by name, which is what it would assign.
				string current = biome.TerrainProcess != null ? biome.TerrainProcess.name : null;
				bool same = profile != null ? biome.TerrainProcess == profile : current == profileName;
				if (same)
				{
					continue;
				}
				Note(log, biomeName, "TerrainProcess", current ?? "(none)", profileName);
				changes++;
				if (save && profile != null)
				{
					Undo.RecordObject(biome, "Apply terrain process table");
					biome.TerrainProcess = profile;
					EditorUtility.SetDirty(biome);
				}
			}

			if (save && changes > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return changes;
		}

		private static string Describe(in TerrainProcess v)
		{
			return string.Format(CultureInfo.InvariantCulture,
				"erodibility {0}, cohesion {1}, storminess {2}, talus {3}°, soil {4} m, creep {5}, channels {6}, depressions {7}, lakes {8}, plateaus {9}, dune sand {10}",
				v.Erodibility, v.VegetationCohesion, v.Storminess, v.TalusAngleDegrees, v.SoilDepthMetres, v.SoilCreep,
				v.ChannelThreshold, v.DepressionKeeping, v.LakeRetention, v.PlateauShare, v.DuneSand);
		}

		private static void Note(StringBuilder log, string owner, string field, object before, object after)
		{
			log?.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}.{1}: {2} → {3}", owner, field, before, after));
		}
	}
}
#endif
