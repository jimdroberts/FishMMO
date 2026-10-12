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
	/// <b>Of its biome.</b> The herbs are the wild kin of the gatherable herbs and fungi that grow there (their visuals in
	/// <see cref="GatheringResourceCatalogue"/>: a forest grows silverleaf and morels, a bog mirewort and puffballs, the
	/// cold frostbloom); the flowers are the biome's own (its flower rules in <see cref="BiomeArtSpec"/>). A biome with
	/// neither takes common herbs and a meadow's flowers.
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
		private const string FlowersPrefix = "Detail_Flowers";
		private const string MeadowFlowers = "Detail_FlowersMeadow";
		private static readonly string[] CommonHerbs = { "Detail_BroadHerb", "Detail_HerbRuderal" };

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
			string biome = null;
			if (context.BiomeAt != null)
			{
				BiomeTemplate template = context.BiomeAt(record.Position.x, record.Position.z);
				biome = template != null ? template.name : null;
			}

			List<GameObject> herbs = Load(HerbsFor(biome));
			List<GameObject> flowers = Load(FlowersFor(biome));
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

		/// <summary>
		/// The detail prefabs of the herbs and fungi gathered in a biome (the visuals of the catalogue's resources that grow
		/// there), else common herbs.
		/// </summary>
		public static List<string> HerbsFor(string biome)
		{
			var herbs = new List<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				if ((resource.Family == GatheringFamily.Herb || resource.Family == GatheringFamily.Fungus)
					&& resource.Suits(biome)
					&& !string.IsNullOrEmpty(resource.Visual)
					&& resource.Visual.StartsWith(DetailPrefix, StringComparison.Ordinal)
					&& !herbs.Contains(resource.Visual))
				{
					herbs.Add(resource.Visual);
				}
			}
			if (herbs.Count == 0)
			{
				herbs.AddRange(CommonHerbs);
			}
			return herbs;
		}

		/// <summary>The detail prefabs of a biome's own flowers (its flower rules), else a meadow's.</summary>
		public static List<string> FlowersFor(string biome)
		{
			var flowers = new List<string>();
			BiomeArtSpec.Entry entry = string.IsNullOrEmpty(biome) ? null : BiomeArtSpec.For(biome);
			if (entry != null)
			{
				foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) pair in entry.Rules())
				{
					BiomeArtSpec.Scatter rule = pair.rule;
					if (rule.Channel != PrefabSpawnChannel.DetailLayer || rule.Prefabs == null)
					{
						continue;
					}
					foreach (string prefab in rule.Prefabs)
					{
						if (!string.IsNullOrEmpty(prefab) && prefab.StartsWith(FlowersPrefix, StringComparison.Ordinal) && !flowers.Contains(prefab))
						{
							flowers.Add(prefab);
						}
					}
				}
			}
			if (flowers.Count == 0)
			{
				flowers.Add(MeadowFlowers);
			}
			return flowers;
		}

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

				var layers = new int[Species.Count];
				for (int s = 0; s < Species.Count; ++s)
				{
					layers[s] = LayerOf(data, Species[s]);
				}
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				var maps = new int[Species.Count][,];
				for (int s = 0; s < Species.Count; ++s)
				{
					maps[s] = data.GetDetailLayer(x0, z0, w, h, layers[s]);
				}
				// Coverage is written on the terrain's own scale (TerrainScatter's rule values are 0..255 of it).
				float unit = Mathf.Max(1, data.maxDetailScatterPerRes) / 255.0f;
				int maxValue = Mathf.Max(1, data.maxDetailScatterPerRes);
				var want = new float[Species.Count];
				var changed = new bool[Species.Count];
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
								if (maps[s][j, i] != 0)
								{
									maps[s][j, i] = 0;
									changed[s] = true;
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
							int value = Mathf.Clamp(Mathf.RoundToInt(want[s] * fade * unit), 0, maxValue);
							// Only ever more: the woods' own cover of the same plant stays where it is thicker.
							if (value > maps[s][j, i])
							{
								maps[s][j, i] = value;
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
					if (changed[s])
					{
						data.SetDetailLayer(x0, z0, layers[s], maps[s]);
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
				if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None)
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
		/// A plant's detail layer on a tile: the tile's own when it has one (the woods already grow it), else added with the
		/// settings TerrainScatter gives a scattered plant, copied from a mesh detail the tile already has.
		/// </summary>
		internal static int LayerOf(TerrainData data, GameObject prefab)
		{
			DetailPrototype[] prototypes = data.detailPrototypes;
			int like = -1;
			for (int i = 0; i < prototypes.Length; ++i)
			{
				GameObject existing = prototypes[i].prototype;
				if (existing == null)
				{
					continue;
				}
				if (existing == prefab || existing.name == prefab.name)
				{
					return i;
				}
				if (like < 0 && prototypes[i].usePrototypeMesh && !TerrainScatter.IsCountedDetail(existing))
				{
					like = i;
				}
			}
			DetailPrototype copy = like >= 0 ? prototypes[like] : new DetailPrototype { usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true, density = 1.0f, useDensityScaling = true };
			string detail = prefab.name.StartsWith(DetailPrefix, StringComparison.Ordinal) ? prefab.name.Substring(DetailPrefix.Length) : prefab.name;
			ProceduralArtCatalogue.TryDetail(detail, out DetailSpec spec);
			var added = new DetailPrototype
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
				// Upright, as the biome's plants are (BiomeArtSpec.Plants).
				alignToGround = 0.0f,
				positionJitter = 1.0f,
				useDensityScaling = copy.useDensityScaling,
			};
			var grown = new DetailPrototype[prototypes.Length + 1];
			Array.Copy(prototypes, grown, prototypes.Length);
			grown[prototypes.Length] = added;
			data.detailPrototypes = grown;
			data.RefreshPrototypes();
			return prototypes.Length;
		}
	}
}
#endif
