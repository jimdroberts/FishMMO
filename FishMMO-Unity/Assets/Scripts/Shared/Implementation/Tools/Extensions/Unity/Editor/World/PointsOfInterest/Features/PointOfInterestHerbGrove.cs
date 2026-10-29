#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What makes a herb grove read as one (2026-10-12: "Herb Grove has no herbs?"): beds of its biome's herbs and
	/// flowers grown into the terrain's details round its gathering nodes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The grove's template ("HerbGrove - Stones") lays one to three small standing stones and perhaps a crate;
	/// its herbs were only its gathering nodes, which the server spawns at run time from EditorOnly spawners: two to five
	/// plants under a metre high. In the editor the site was bare forest floor with a few stones on it, and in the game a
	/// couple of plants no different from the floor round them.
	/// </para>
	/// <para>
	/// <b>Grown, not placed</b>, as the farmland's wheat is (<see cref="PointOfInterestFields"/>): the beds are painted
	/// into the terrain's detail layers, so the vegetation renderers draw them with the wind, light and season of the
	/// woods round them. Each bed is one species in a rounded clump with a wavering edge; flowers come up thinly between
	/// the beds, and the grove's own edge is ragged rather than a disc.
	/// </para>
	/// <para>
	/// <b>Of its biome.</b> The herbs are its template's Herb Grove Herbs, which the biome art authoring fills with the wild
	/// kin of the gatherable herbs and fungi offered there (their visuals in <see cref="GatheringResourceCatalogue"/>: a
	/// forest grows silverleaf and morels, a bog mirewort and puffballs, the cold frostbloom; common herbs where none is);
	/// the flowers are the biome's own (its template's Herb Grove Flowers, which the authoring fills with its flower rules,
	/// or a meadow's where it has none). The plants a grove paints take its biome's blade looks: each goes into the tile's
	/// prototype of the plant of the grove's own biome, or one the grove adds for it (<see cref="SiteLayers"/>).
	/// </para>
	/// <para>
	/// <b>The nodes stand clear.</b> Each gathering node (<see cref="GatheringFeature"/>'s spawners, laid before the site
	/// builders run) keeps a bare ring under it, so the plant a player can pick is not lost in a bed of its own kind.
	/// Beds only add to what grows there (the woods' ferns and grass stay), never on water, a trodden way or steep ground.
	/// </para>
	/// <para>Seeded by the site: a re-cut grows the same beds. A terrain repaint clears the details and the sites grow them again.</para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestHerbGrove : IPointOfInterestSiteBuilder
	{
		static PointOfInterestHerbGrove() => PointOfInterestSiteBuilders.Register(new PointOfInterestHerbGrove());

		/// <summary>The steepest ground a bed grows on, degrees.</summary>
		public const float MaxSlopeDegrees = 35.0f;
		/// <summary>A bed's coverage at its heart (0..255, the terrain's coverage mode): about the lushest a biome's own rules write.</summary>
		private const int BedCoverage = 220;
		/// <summary>The flowers' coverage between the beds at most.</summary>
		private const int FlowerCoverage = 110;
		/// <summary>The bare ring kept round each gathering node, metres (at least the cell it stands in).</summary>
		private const float NodeClearing = 0.9f;
		/// <summary>How far the grove's ragged edge wanders about its footprint, metres.</summary>
		private const float EdgeReach = 3.0f;
		/// <summary>The width of the grove's fade at its edge, metres.</summary>
		private const float EdgeFade = 2.0f;
		private const string DetailPrefix = "Detail_";

		public bool Handles(PointOfInterestRecord record) => record.Kind == POIType.HerbGrove;

		private struct Bed
		{
			public Vector2 Centre;
			public float Radius;
			/// <summary>Index into the grove's species.</summary>
			public int Species;
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			DeterministicRNG random = context.Random;
			BiomeTemplate template = context.BiomeAt != null ? context.BiomeAt(record.Position.x, record.Position.z) : null;

			List<GameObject> herbs = BiomeTemplateArt.GroveHerbs(template);
			List<GameObject> flowers = BiomeTemplateArt.GroveFlowers(template);
			if (herbs.Count == 0 && flowers.Count == 0)
			{
				PointOfInterestFeatureKit.Note(context, "no herb or flower detail prefabs to grow the grove with: run the procedural art generator.");
				return;
			}
			var species = new List<GameObject>(herbs);
			foreach (GameObject flower in flowers)
			{
				if (!species.Contains(flower))
				{
					species.Add(flower);
				}
			}

			// The beds: mostly herbs, some flowers, clumps a few metres across spread over the footprint.
			var site = new Vector2(record.Position.x, record.Position.z);
			float radius = Mathf.Max(6.0f, record.Radius);
			int bedCount = 5 + Mathf.Clamp(record.SizeClass, 0, 2) * 3 + random.Next(3);
			var beds = new List<Bed>(bedCount);
			for (int i = 0; i < bedCount; ++i)
			{
				float r = radius * 0.85f * Mathf.Sqrt(random.NextFloat());
				float a = random.NextFloat() * Mathf.PI * 2.0f;
				bool herb = herbs.Count > 0 && (flowers.Count == 0 || random.NextFloat() < 0.75f);
				int index = herb ? random.Next(herbs.Count) : species.IndexOf(flowers[random.Next(flowers.Count)]);
				beds.Add(new Bed
				{
					Centre = site + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r,
					Radius = Mathf.Lerp(1.6f, 3.4f, random.NextFloat()),
					Species = index,
				});
			}
			int sprinkled = flowers.Count > 0 ? species.IndexOf(flowers[random.Next(flowers.Count)]) : -1;
			var noise = new Vector2(random.NextFloat() * 1000.0f, random.NextFloat() * 1000.0f);

			var grove = new Grove
			{
				Context = context,
				Biome = template,
				Species = species,
				Beds = beds,
				Sprinkled = sprinkled,
				Site = site,
				Radius = radius,
				Noise = noise,
				Nodes = NodeSpots(context),
				Ways = context.Points != null && context.Points.Paths != null ? new PathExclusion(context.Points.Paths) : null,
			};
			int cells = 0;
			foreach (Terrain terrain in TerrainsOf(context))
			{
				cells += grove.Paint(terrain);
			}

			var names = new List<string>();
			foreach (GameObject prefab in species)
			{
				names.Add(prefab.name.StartsWith(DetailPrefix, StringComparison.Ordinal) ? prefab.name.Substring(DetailPrefix.Length) : prefab.name);
			}
			context.Notes?.Add($"{record.Name}: a herb grove of {string.Join(", ", names)} grown ({beds.Count} bed(s), {cells:N0} detail cell(s)).");
		}

		// ── What grows there ──────────────────────────────────────────

		/// <summary>Generated prefabs by name (missing ones left out, repeats once), in order.</summary>
		internal static List<GameObject> Load(List<string> names)
		{
			var prefabs = new List<GameObject>(names.Count);
			foreach (string name in names)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
				if (prefab != null && !prefabs.Contains(prefab))
				{
					prefabs.Add(prefab);
				}
			}
			return prefabs;
		}

		/// <summary>Where the site's gathering nodes will stand: its spawners that spawn a <see cref="GatheringNode"/>, world xz.</summary>
		private static List<Vector2> NodeSpots(PointOfInterestSiteContext context)
		{
			var spots = new List<Vector2>();
			if (context.Root == null)
			{
				return spots;
			}
			foreach (ObjectSpawner spawner in context.Root.GetComponentsInChildren<ObjectSpawner>(true))
			{
				if (spawner.Spawnables == null)
				{
					continue;
				}
				foreach (SpawnableSettings spawnable in spawner.Spawnables)
				{
					if (spawnable != null && spawnable.NetworkObject != null && spawnable.NetworkObject.GetComponent<GatheringNode>() != null)
					{
						Vector3 at = spawner.transform.position;
						spots.Add(new Vector2(at.x, at.z));
						break;
					}
				}
			}
			return spots;
		}

		internal static List<Terrain> TerrainsOf(PointOfInterestSiteContext context)
		{
			var terrains = new List<Terrain>();
			if (context.Scene.IsValid())
			{
				foreach (GameObject root in context.Scene.GetRootGameObjects())
				{
					terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
				}
			}
			return terrains;
		}

		// ── The ground ────────────────────────────────────────────────

		/// <summary>One grove's beds, painted tile by tile.</summary>
		private sealed class Grove
		{
			public PointOfInterestSiteContext Context;
			/// <summary>The biome the grove stands in: the one whose blade looks the plants it adds to a tile take.</summary>
			public BiomeTemplate Biome;
			public List<GameObject> Species;
			public List<Bed> Beds;
			/// <summary>The flowers sprinkled between the beds (index into <see cref="Species"/>), or −1.</summary>
			public int Sprinkled;
			public Vector2 Site;
			public float Radius;
			/// <summary>The grove's own offset into the noise, so no two groves share an edge.</summary>
			public Vector2 Noise;
			public List<Vector2> Nodes;
			public PathExclusion Ways;

			/// <summary>Paints the grove on one tile; how many cells it changed.</summary>
			public int Paint(Terrain terrain)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || data.detailResolution <= 0)
				{
					return 0;
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;
				float outer = Radius + EdgeReach * 1.5f;
				if (Site.x + outer < origin.x || Site.x - outer > origin.x + size.x || Site.y + outer < origin.z || Site.y - outer > origin.z + size.z)
				{
					return 0;
				}
				int res = data.detailResolution;
				float texelX = size.x / res, texelZ = size.z / res;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((Site.x - outer - origin.x) / texelX), 0, res - 1);
				int x1 = Mathf.Clamp(Mathf.CeilToInt((Site.x + outer - origin.x) / texelX), 0, res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((Site.y - outer - origin.z) / texelZ), 0, res - 1);
				int z1 = Mathf.Clamp(Mathf.CeilToInt((Site.y + outer - origin.z) / texelZ), 0, res - 1);
				if (x1 < x0 || z1 < z0)
				{
					return 0;
				}

				// Each plant into its own biome's prototype on the tile, measured against the one it used to share (2026-10-16).
				var layers = new PlantLayers[Species.Count];
				for (int s = 0; s < Species.Count; ++s)
				{
					layers[s] = LayersOf(terrain, Species[s], Biome);
				}
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				var maps = new int[Species.Count][,];
				var bases = new int[Species.Count][,];
				for (int s = 0; s < Species.Count; ++s)
				{
					if (layers[s].Own >= 0)
					{
						ReadMaps(data, x0, z0, w, h, layers[s], out maps[s], out bases[s]);
					}
				}
				// Coverage is written on the terrain's own scale (TerrainScatter's rule values are 0..255 of it).
				float unit = Mathf.Max(1, data.maxDetailScatterPerRes) / 255.0f;
				int maxValue = Mathf.Max(1, data.maxDetailScatterPerRes);
				var want = new float[Species.Count];
				var changed = new bool[Species.Count];
				var baseChanged = new bool[Species.Count];
				// At least the cell under a node, however coarse the tile's detail grid.
				float clearing = Mathf.Max(NodeClearing, 0.75f * Mathf.Max(texelX, texelZ));
				int cells = 0;
				for (int j = 0; j < h; ++j)
				{
					for (int i = 0; i < w; ++i)
					{
						var p = new Vector2(origin.x + (x0 + i + 0.5f) * texelX, origin.z + (z0 + j + 0.5f) * texelZ);
						float distance = Vector2.Distance(p, Site);
						// The edge wanders between half a reach inside the footprint and a reach and a half outside it.
						float edge = Radius + EdgeReach * (Mathf.PerlinNoise(Noise.x + p.x * 0.15f, Noise.y + p.y * 0.15f) * 2.0f - 0.5f);
						if (distance > edge)
						{
							continue;
						}

						bool cleared = false;
						foreach (Vector2 node in Nodes)
						{
							cleared |= (p - node).sqrMagnitude < clearing * clearing;
						}
						if (cleared)
						{
							bool any = false;
							for (int s = 0; s < Species.Count; ++s)
							{
								if (maps[s] == null)
								{
									continue;
								}
								if (maps[s][j, i] != 0)
								{
									maps[s][j, i] = 0;
									changed[s] = true;
									any = true;
								}
								// The plant's other biome's prototype too, which the grove used to paint into and clear.
								if (bases[s] != null && bases[s][j, i] != 0)
								{
									bases[s][j, i] = 0;
									baseChanged[s] = true;
									any = true;
								}
							}
							cells += any ? 1 : 0;
							continue;
						}
						if (!Grows(p))
						{
							continue;
						}

						float fade = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01((edge - distance) / EdgeFade));
						Array.Clear(want, 0, want.Length);
						foreach (Bed bed in Beds)
						{
							float wobble = 0.8f + 0.4f * Mathf.PerlinNoise(Noise.x + 37.0f + p.x * 0.5f, Noise.y + 11.0f + p.y * 0.5f);
							float t = Vector2.Distance(p, bed.Centre) / (bed.Radius * wobble);
							if (t < 1.0f)
							{
								want[bed.Species] = Mathf.Max(want[bed.Species], BedCoverage * (1.0f - t * t));
							}
						}
						if (Sprinkled >= 0)
						{
							float clump = Mathf.Clamp01(Mathf.PerlinNoise(Noise.x + 71.0f + p.x * 0.35f, Noise.y + 5.0f + p.y * 0.35f) * 1.6f - 0.3f);
							want[Sprinkled] = Mathf.Max(want[Sprinkled], FlowerCoverage * clump);
						}

						bool grew = false;
						for (int s = 0; s < Species.Count; ++s)
						{
							if (maps[s] == null)
							{
								continue;
							}
							int value = Mathf.Clamp(Mathf.RoundToInt(want[s] * fade * unit), 0, maxValue);
							// Only ever more: the woods' own cover of the same plant stays where it is thicker (measured with the
							// prototype of the plant the grove used to paint into, when its own is another).
							int grown = TerrainDetailBiomes.SiteCover(bases[s] != null ? bases[s][j, i] : 0, maps[s][j, i], bases[s] == null, value);
							if (grown != maps[s][j, i])
							{
								maps[s][j, i] = grown;
								changed[s] = true;
								grew = true;
							}
						}
						cells += grew ? 1 : 0;
					}
				}
				bool dirty = false;
				for (int s = 0; s < Species.Count; ++s)
				{
					if (baseChanged[s])
					{
						data.SetDetailLayer(x0, z0, layers[s].Base, bases[s]);
						dirty = true;
					}
					if (changed[s])
					{
						data.SetDetailLayer(x0, z0, layers[s].Own, maps[s]);
						dirty = true;
					}
				}
				if (dirty)
				{
					EditorUtility.SetDirty(data);
				}
				return cells;
			}

			/// <summary>Whether a bed may grow at a point: dry, gentle ground off the trodden ways.</summary>
			private bool Grows(Vector2 p)
			{
				PointOfInterestSiteContext context = Context;
				// The water grid has no sea in it: the sea and the highest tide are the context's (IsWet).
				if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None || context.IsWet(p.x, p.y, 0.05f))
				{
					return false;
				}
				if (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > MaxSlopeDegrees)
				{
					return false;
				}
				return Ways == null || !Ways.Details(p.x, p.y);
			}
		}

		/// <summary>
		/// The two detail layers a point of interest paints one plant into on a tile (2026-10-16): its own (<see cref="Own"/>,
		/// the plant's prototype of the biome the site stands in) and the one it painted into before (<see cref="Base"/>: the
		/// tile's first prototype of the plant, whose cover a site's growth is still measured against,
		/// <see cref="TerrainDetailBiomes.SiteCover"/>). The same layer when the tile's first prototype of the plant is the site's
		/// own biome's or the tile had none. Both -1 when the plant cannot be painted on the tile.
		/// </summary>
		public struct PlantLayers
		{
			public int Own;
			public int Base;

			/// <summary>One layer: the site paints straight into the plant's first prototype, as every site did before.</summary>
			public bool Shared => Own == Base;

			public static PlantLayers None => new PlantLayers { Own = -1, Base = -1 };
		}

		/// <summary>
		/// A plant's layers on a tile for a site standing in <paramref name="biome"/> (<see cref="SiteLayers"/>), a prototype the
		/// tile lacks added with the settings TerrainScatter gives a scattered plant, copied from a mesh detail the tile has.
		/// </summary>
		public static PlantLayers LayersOf(Terrain terrain, GameObject prefab, BiomeTemplate biome) => SiteLayers(terrain, prefab, biome, ScatteredPrototype);

		/// <summary>
		/// The layers a site of <paramref name="biome"/> paints a plant into on a tile (<see cref="PlantLayers"/>): the tile's
		/// prototype of the plant recorded for the same biome (a rule's or an earlier site's) when it has one; else one of the
		/// site's own, added and recorded for its biome (<see cref="TerrainDetailBiomes.RecordSite"/>), whose blades take that
		/// biome's look of the plant (its Grass Types entry) and keep their material's tints. The added prototype copies the
		/// tile's first of the plant where it has one (another biome's: same sizes, density and render settings, so the plant
		/// draws as it did there), else is <paramref name="fresh"/>'s; either way with a noise seed of its own
		/// (<see cref="TerrainDetailBiomes.SiteSeed"/>). A site with no biome paints into the tile's first prototype of the
		/// plant, or adds one unrecorded, as before.
		/// </summary>
		/// <remarks>
		/// Until 2026-10-16 (I3b) every site took the tile's first prototype of the plant, whichever biome's it was: a grove's
		/// flowers on a tile a neighbouring meadow's rule also grew were drawn with the meadow's look, and two sites of
		/// different biomes on one tile shared the first one's. Limits: each added prototype is one more detail layer on the
		/// tile; a blade plant's own look is one more grass type in the scene (GrassTypeTable, 128: past them a look merges into
		/// the plant's first) and one more channel on the tile (GrassTerrain.MaxChannels, 64: past them it adds into its plant's
		/// channel), each with a warning; the GPU detail scatter keeps a channel per plant, so its prototypes sum into the
		/// plant's one. The bake (TerrainDetailBaker) chooses with the same selection as play, so it matches.
		/// </remarks>
		public static PlantLayers SiteLayers(Terrain terrain, GameObject prefab, BiomeTemplate biome, Func<DetailPrototype[], GameObject, DetailPrototype> fresh)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null || prefab == null)
			{
				return PlantLayers.None;
			}
			DetailPrototype[] prototypes = data.detailPrototypes;
			var names = new string[prototypes.Length];
			var seeds = new int[prototypes.Length];
			for (int i = 0; i < prototypes.Length; i++)
			{
				names[i] = prototypes[i]?.prototype != null ? prototypes[i].prototype.name : null;
				seeds[i] = prototypes[i] != null ? prototypes[i].noiseSeed : 0;
			}
			TerrainDetailBiomes record = terrain.GetComponent<TerrainDetailBiomes>();
			List<TerrainDetailBiomes.Entry> entries = record != null ? record.Prototypes : null;
			int biomeID = BiomeRegistry.IDOf(biome);
			int reuse = TerrainDetailBiomes.SiteLayer(prefab.name, biomeID, names, seeds, entries, out int first);
			if (reuse >= 0)
			{
				return new PlantLayers { Own = reuse, Base = first >= 0 ? first : reuse };
			}
			if (prototypes.Length >= TerrainDetailLayerBudget.MaxLayers)
			{
				/* Unity keeps a detail layer's index in a byte: a layer added past the last would be written into another plant's
				 * (TerrainDetailLayerBudget). The site paints the tile's own prototype of the plant where it has one, else nothing. */
				UnityEngine.Debug.LogError($"[Points of interest] ERROR: '{terrain.name}' already holds {prototypes.Length} detail prototypes (Unity's most per terrain): a site's own '{prefab.name}' is not added{(first >= 0 ? "; it paints the tile's first of the plant" : " and is not painted")}.");
				return first >= 0 ? new PlantLayers { Own = first, Base = first } : PlantLayers.None;
			}
			DetailPrototype added = first >= 0 ? new DetailPrototype(prototypes[first]) : fresh(prototypes, prefab);
			if (added == null)
			{
				return PlantLayers.None;
			}
			added.noiseSeed = TerrainDetailBiomes.SiteSeed(added.noiseSeed, biomeID, prefab.name, names, seeds, entries);
			var grown = new DetailPrototype[prototypes.Length + 1];
			Array.Copy(prototypes, grown, prototypes.Length);
			grown[prototypes.Length] = added;
			data.detailPrototypes = grown;
			data.RefreshPrototypes();
			int own = prototypes.Length;
			if (biomeID != 0)
			{
				if (record == null)
				{
					record = terrain.gameObject.AddComponent<TerrainDetailBiomes>();
				}
				if (record.RecordSite(prefab.name, added.noiseSeed, biomeID))
				{
					EditorUtility.SetDirty(record);
				}
			}
			return new PlantLayers { Own = own, Base = first >= 0 ? first : own };
		}

		/// <summary>
		/// A new prototype for a plant the tile does not grow, with the settings TerrainScatter gives a scattered plant, copied
		/// from a mesh detail the tile already has (the first that is not a counted bush).
		/// </summary>
		private static DetailPrototype ScatteredPrototype(DetailPrototype[] prototypes, GameObject prefab)
		{
			int like = -1;
			for (int i = 0; i < prototypes.Length; ++i)
			{
				GameObject existing = prototypes[i].prototype;
				if (existing == null)
				{
					continue;
				}
				if (like < 0 && prototypes[i].usePrototypeMesh && !TerrainScatter.IsCountedDetail(existing))
				{
					like = i;
				}
			}
			DetailPrototype copy = like >= 0 ? prototypes[like] : new DetailPrototype { usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true, density = 1.0f, useDensityScaling = true };
			string detail = prefab.name.StartsWith(DetailPrefix, StringComparison.Ordinal) ? prefab.name.Substring(DetailPrefix.Length) : prefab.name;
			ProceduralArtCatalogue.TryDetail(detail, out DetailSpec spec);
			return new DetailPrototype
			{
				prototype = prefab,
				usePrototypeMesh = true,
				useInstancing = copy.useInstancing,
				renderMode = copy.renderMode,
				minWidth = 0.8f,
				maxWidth = 1.25f,
				minHeight = 0.8f,
				maxHeight = 1.25f,
				healthyColor = spec.Healthy == default ? new Color(0.9f, 0.95f, 0.9f, 1.0f) : spec.Healthy,
				dryColor = spec.Dry == default ? new Color(0.75f, 0.7f, 0.55f, 1.0f) : spec.Dry,
				noiseSpread = copy.noiseSpread,
				noiseSeed = copy.noiseSeed,
				density = copy.density,
				// Upright, as the biome's plants are.
				alignToGround = 0.0f,
				positionJitter = 1.0f,
				useDensityScaling = copy.useDensityScaling,
			};
		}

		/// <summary>
		/// The plant layers' detail maps over a window, the base's only where it is not the site's own layer (null there).
		/// </summary>
		internal static void ReadMaps(TerrainData data, int x0, int z0, int w, int h, PlantLayers layers, out int[,] own, out int[,] baseMap)
		{
			own = data.GetDetailLayer(x0, z0, w, h, layers.Own);
			baseMap = layers.Shared ? null : data.GetDetailLayer(x0, z0, w, h, layers.Base);
		}
	}
}
#endif
