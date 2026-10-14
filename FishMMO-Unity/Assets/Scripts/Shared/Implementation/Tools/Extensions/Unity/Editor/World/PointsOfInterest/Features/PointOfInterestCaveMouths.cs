#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The growth round a natural cave's mouth (2026-10-12: "Cave entrances are a bit boring, just round rocky holes…
	/// Natural caves look way more overgrown and 'earthy'"): the turf pushed out over its lintel with roots hanging from
	/// it, bushes and ferns crowding its flanks and the bank above it, creeper and grass right up to the rock, and
	/// boulders and a fallen trunk half sunk in the ground beside it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Grown where it can be.</b> The plants are the biome's own (its bushes and ferns, from its art rules; a woodland's
	/// where it has none), painted into the terrain's detail layers as the herb grove's are, thickest at the mouth's
	/// shoulders and over its lintel and thinning away over a dozen metres or so. The way in is left open: nothing grows or
	/// lies in a lane straight out from the mouth as wide as its bore, nor on the trodden ways, water or steep rock.
	/// </para>
	/// <para>
	/// <b>Boulders at the walls' feet.</b> Where the mouth opens in a notch cut into the hillside (CaveSystem.HasNotch),
	/// boulders lie heaped against the foot of its rock walls on the notch's floor, leaving the way in clear, and more at the
	/// walls' outer ends; a fallen trunk beside it now and then (reference photos, 2026-10-12). The turf-and-roots lip once
	/// hung over the opening was taken out ("look unnatural"); vines drape the lintel instead (VinePlacement).
	/// </para>
	/// <para>
	/// By form: a cave in full; a lava tube thinner (the ground about it is young rock); a grotto and a sea cave only their
	/// flanks' growth (the sea's edge); an ice cave and a mine nothing. Seeded by the system, so a repaint grows the same.
	/// </para>
	/// <para>
	/// <b>Some are overgrown</b> (<see cref="Overgrown"/>, 2026-10-13): their mouths grow thicker, and the growth carries
	/// in over the cave's floor as far as the daylight reaches (PointOfInterestCaveDressing).
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestCaveMouths : IPointOfInterestSiteBuilder
	{
		static PointOfInterestCaveMouths() => PointOfInterestSiteBuilders.Register(new PointOfInterestCaveMouths());

		/// <summary>How far round a mouth its growth reaches, metres, before the bore's radius is added.</summary>
		private const float Reach = 12f;
		/// <summary>The steepest ground the growth takes, degrees: banks, not faces.</summary>
		private const float MaxSlope = 52f;
		private const string DetailPrefix = "Detail_";
		private static readonly string[] Ferns = { "Detail_FernBracken", "Detail_Fern", "Detail_FernHartstongue" };
		private static readonly string[] WoodlandBushes = { ProceduralArtCatalogue.BushPrefix + "Hazel", ProceduralArtCatalogue.BushPrefix + "Bramble" };
		private const string Creeper = "Detail_Ivy";

		public bool Handles(PointOfInterestRecord record)
			=> CaveShaper.TryForm(record.Kind, out CaveForm form) && form != CaveForm.Mine && form != CaveForm.IceCave;

		/// <summary>
		/// The share of a form's caves grown over, inside as well as round the mouth: a damp grotto most, a lava tube's young
		/// rock least; never a sea cave (salt and surf), an ice cave or a mine.
		/// </summary>
		internal static float OvergrownShare(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.Cave: return 0.35f;
				case CaveForm.Grotto: return 0.5f;
				case CaveForm.LavaTube: return 0.2f;
				default: return 0f;
			}
		}

		/// <summary>Whether this system is one of its form's overgrown caves: by its seed, so every builder and a re-cut agree.</summary>
		internal static bool Overgrown(CaveSystem system)
			=> system != null && PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(system.Seed, 0x0E, 0x6E, 0x0B)) < OvergrownShare(system.Form);

		/// <summary>The biome at the system's first mouth (its art's name), or null.</summary>
		internal static string BiomeOf(PointOfInterestSiteContext context, CaveSystem system)
		{
			if (context.BiomeAt == null)
			{
				return null;
			}
			Vector3 first = system.Origin + (system.Mouths.Count > 0 ? system.Mouths[0].Floor : Vector3.zero);
			BiomeTemplate template = context.BiomeAt(first.x, first.z);
			return template != null ? template.name : null;
		}

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestShape shape = CaveShaper.ShapeFor(context.Points, context.Record.Id);
			if (shape == null || CaveShaper.IsLink(shape.Values, out _) || context.GroundAt == null)
			{
				return;
			}
			CaveSystem system = CaveSystem.FromValues(shape.Values, shape.Position, shape.Seed);
			if (system == null || system.Form == CaveForm.Mine || system.Form == CaveForm.IceCave)
			{
				return;
			}
			var random = new DeterministicRNG(unchecked(system.Seed * 0x3C6E + 0x0B5E));
			bool coast = system.Form == CaveForm.Grotto || system.Form == CaveForm.SeaCave;
			float lush = system.Form == CaveForm.LavaTube ? 0.5f : coast ? 0.6f : 1f;
			bool overgrown = Overgrown(system);
			if (overgrown)
			{
				lush *= 1.35f;
			}

			string biome = BiomeOf(context, system);
			Growth growth = GrowthFor(biome);

			int props = 0, cells = 0, vines = 0;
			var mouths = new List<Mouth>();
			// The ground round the system, for the vines to find the outcrop's and shell's faces round each mouth.
			CaveGround ground = null;
			if (system.Form != CaveForm.SeaCave)
			{
				Bounds e = system.Extent;
				ground = CaveGround.Sample(context.GroundAt, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f,
					system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, 2f);
			}
			foreach (CaveMouth mouth in system.Mouths)
			{
				var m = new Mouth
				{
					At = system.Origin + mouth.Floor,
					Out = Dir(mouth.Yaw),
					Radius = mouth.Radius,
				};
				mouths.Add(m);
				props += Strew(context, system, mouth, m, growth, random, lush);
				// Ivy and creepers over the lintel and down the jambs, now and then a curtain across the whole opening, and on
				// the rock round it (Jim, 2026-10-12: "occasionally curtain cave entrances, and around the cave entrances");
				// visual only, no colliders. A sea cave's rock is the sea's edge: none there.
				if (ground != null)
				{
					vines += VinePlacement.DrapeCaveMouth(context, system, mouth, biome, random, ground);
				}
			}
			if (growth.Species.Count > 0)
			{
				var ways = context.Points != null && context.Points.Paths != null ? new PathExclusion(context.Points.Paths) : null;
				var noise = new Vector2(random.NextFloat() * 1000f, random.NextFloat() * 1000f);
				foreach (Terrain terrain in PointOfInterestHerbGrove.TerrainsOf(context))
				{
					foreach (Mouth m in mouths)
					{
						cells += Paint(context, terrain, m, growth, ways, noise, lush);
					}
				}
			}
			PointOfInterestFeatureKit.Note(context, $"cave mouth(s){(overgrown ? " (an overgrown cave)" : string.Empty)}: {vines} vine(s), {props} boulder(s) and log(s), {cells:N0} detail cell(s) of {growth.Describe()}.");
		}

		private struct Mouth
		{
			public Vector3 At;
			public Vector3 Out;
			public float Radius;
		}

		/// <summary>What grows round a mouth: the detail prefabs by part (bush, fern, creeper), and the props strewn by it.</summary>
		internal sealed class Growth
		{
			public readonly List<GameObject> Species = new List<GameObject>();
			public readonly List<int> Bushes = new List<int>(), Ferns = new List<int>();
			public int Creeper = -1;
			public readonly List<GameObject> Boulders = new List<GameObject>(), Logs = new List<GameObject>();

			public string Describe()
			{
				var names = new List<string>();
				foreach (GameObject prefab in Species)
				{
					names.Add(prefab.name.StartsWith(DetailPrefix, StringComparison.Ordinal) ? prefab.name.Substring(DetailPrefix.Length) : prefab.name);
				}
				return names.Count > 0 ? string.Join(", ", names) : "nothing (no detail art: Generate Biome Art)";
			}
		}

		/// <summary>The biome's bushes, ferns, boulders and deadwood (from its art rules), with a woodland's ferns and bushes where it has none.</summary>
		internal static Growth GrowthFor(string biome)
		{
			var bushes = new List<string>();
			var ferns = new List<string>();
			var boulders = new List<string>();
			var logs = new List<string>();
			BiomeArtSpec.Entry entry = string.IsNullOrEmpty(biome) ? null : BiomeArtSpec.For(biome);
			if (entry != null)
			{
				foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					if (rule.Prefabs == null)
					{
						continue;
					}
					foreach (string prefab in rule.Prefabs)
					{
						if (string.IsNullOrEmpty(prefab))
						{
							continue;
						}
						List<string> into = prefab.StartsWith(ProceduralArtCatalogue.BushPrefix, StringComparison.Ordinal) ? bushes
							: prefab.StartsWith("Detail_Fern", StringComparison.Ordinal) ? ferns
							: prefab.StartsWith("Boulder_", StringComparison.Ordinal) ? boulders
							: prefab.StartsWith(ProceduralArtCatalogue.DeadwoodPrefix, StringComparison.Ordinal) ? logs
							: null;
						if (into != null && !into.Contains(prefab))
						{
							into.Add(prefab);
						}
					}
				}
			}
			if (ferns.Count == 0)
			{
				ferns.AddRange(Ferns);
			}
			if (bushes.Count == 0)
			{
				bushes.AddRange(WoodlandBushes);
			}
			var growth = new Growth();
			foreach (GameObject prefab in PointOfInterestHerbGrove.Load(bushes))
			{
				growth.Bushes.Add(growth.Species.Count);
				growth.Species.Add(prefab);
			}
			foreach (GameObject prefab in PointOfInterestHerbGrove.Load(ferns))
			{
				growth.Ferns.Add(growth.Species.Count);
				growth.Species.Add(prefab);
			}
			List<GameObject> creeper = PointOfInterestHerbGrove.Load(new List<string> { Creeper });
			if (creeper.Count > 0)
			{
				growth.Creeper = growth.Species.Count;
				growth.Species.Add(creeper[0]);
			}
			growth.Boulders.AddRange(PointOfInterestHerbGrove.Load(boulders));
			growth.Logs.AddRange(PointOfInterestHerbGrove.Load(logs));
			return growth;
		}

		// ── The lip ───────────────────────────────────────────────

		/// <summary>The roof over a floor point: the first rock the cave's field meets going up from a metre over the floor.</summary>
		internal static bool RoofOver(CaveSystem system, Vector3 floor, float reach, out float roof)
		{
			roof = 0f;
			const float step = 0.2f;
			float y0 = floor.y + 1f;
			if (system.Void(new Vector3(floor.x, y0, floor.z)) >= 0f)
			{
				return false;
			}
			for (float y = y0 + step; y <= floor.y + reach; y += step)
			{
				if (system.Void(new Vector3(floor.x, y, floor.z)) >= 0f)
				{
					roof = y - step * 0.5f;
					return true;
				}
			}
			return false;
		}

		// ── Boulders and a fallen trunk ───────────────────────────

		/// <summary>A few boulders half sunk at the mouth's shoulders and perhaps a fallen trunk; how many.</summary>
		private static int Strew(PointOfInterestSiteContext context, CaveSystem system, CaveMouth caveMouth, Mouth mouth, Growth growth, DeterministicRNG random, float lush)
		{
			int placed = 0;
			var across = new Vector3(mouth.Out.z, 0f, -mouth.Out.x);
			if (growth.Boulders.Count > 0 && system.HasNotch)
			{
				// Heaped against the foot of each of the notch's walls, on its floor, and at the walls' outer ends.
				float depth = CaveSystem.NotchDepth(caveMouth);
				for (int sideIndex = 0; sideIndex < 2; sideIndex++)
				{
					float side = sideIndex == 0 ? 1f : -1f;
					int heap = random.Range(2, 5);
					for (int i = 0; i < heap; i++)
					{
						float size = random.Range(0.6f, 1.35f);
						bool outer = i == heap - 1 && random.NextFloat() < 0.7f;
						float w = outer ? random.Range(0.85f * depth, depth + 2f) : random.Range(0.8f, 0.85f * depth);
						float half = CaveSystem.NotchHalfWidthAt(caveMouth, w);
						float u = outer ? half + random.Range(0.2f, 1.2f) : half - 0.45f * size;
						// The way in stays a good bore wide.
						if (!outer && u - 0.6f * size < 0.9f * mouth.Radius)
						{
							continue;
						}
						Vector3 p = mouth.At + mouth.Out * w + across * (side * u);
						float ground = outer ? context.GroundAt(p.x, p.z) : Mathf.Min(context.GroundAt(p.x, p.z), mouth.At.y);
						p.y = ground - 0.3f * size;
						GameObject prefab = growth.Boulders[random.Next(growth.Boulders.Count)];
						context.AddProp(prefab, p, Quaternion.Euler(random.Range(-12f, 12f), random.Range(0f, 360f), random.Range(-12f, 12f)), Vector3.one * size);
						placed++;
					}
				}
			}
			else if (growth.Boulders.Count > 0)
			{
				int boulders = random.Range(2, 5);
				for (int i = 0; i < boulders; i++)
				{
					float side = i % 2 == 0 ? 1f : -1f;
					Vector3 p = mouth.At + across * side * (mouth.Radius + random.Range(1.2f, 4.5f)) + mouth.Out * random.Range(-0.5f, 4f);
					if (!OpenGround(context, p, mouth))
					{
						continue;
					}
					float size = random.Range(0.55f, 1.1f);
					GameObject prefab = growth.Boulders[random.Next(growth.Boulders.Count)];
					p.y = context.GroundAt(p.x, p.z) - 0.3f * size;
					context.AddProp(prefab, p, Quaternion.Euler(random.Range(-10f, 10f), random.Range(0f, 360f), random.Range(-10f, 10f)), Vector3.one * size);
					placed++;
				}
			}
			if (growth.Logs.Count > 0 && random.NextFloat() < 0.6f * lush)
			{
				float side = random.NextFloat() < 0.5f ? 1f : -1f;
				Vector3 p = mouth.At + across * side * (mouth.Radius + random.Range(2.5f, 6f)) + mouth.Out * random.Range(1f, 5f);
				if (OpenGround(context, p, mouth))
				{
					GameObject prefab = growth.Logs[random.Next(growth.Logs.Count)];
					p.y = context.GroundAt(p.x, p.z) - 0.1f;
					// Lying roughly along the hillside, not across the way in.
					float yaw = Mathf.Atan2(across.x, across.z) * Mathf.Rad2Deg + random.Range(-30f, 30f);
					context.AddProp(prefab, p, Quaternion.Euler(0f, yaw, 0f), Vector3.one * random.Range(0.8f, 1.1f));
					placed++;
				}
			}
			return placed;
		}

		/// <summary>Ground a prop may lie on: out of the way in, off the cave's opening, dry and not steep.</summary>
		private static bool OpenGround(PointOfInterestSiteContext context, Vector3 p, Mouth mouth)
		{
			if (InWayIn(new Vector2(p.x, p.z), mouth, 0.8f, mouth.Radius + 1f))
			{
				return false;
			}
			if (context.Water != null && context.Water.KindAt(p.x, p.z) != WaterKind.None)
			{
				return false;
			}
			return context.SlopeAt == null || context.SlopeAt(p.x, p.z) < 30f;
		}

		/// <summary>
		/// In the lane out from the mouth as wide as its bore (plus a margin), from <paramref name="behind"/> metres back of the
		/// mouth's plane (over the opening; the growth goes up over the lintel, a prop does not).
		/// </summary>
		private static bool InWayIn(Vector2 p, Mouth mouth, float margin, float behind)
		{
			var o = new Vector2(mouth.Out.x, mouth.Out.z);
			Vector2 d = p - new Vector2(mouth.At.x, mouth.At.z);
			float along = Vector2.Dot(d, o);
			float side = Mathf.Abs(d.x * o.y - d.y * o.x);
			return along > -behind && along < Reach + 2f && side < mouth.Radius + margin;
		}

		// ── The plants ────────────────────────────────────────────

		/// <summary>Paints one mouth's growth on one tile; how many cells it changed.</summary>
		private static int Paint(PointOfInterestSiteContext context, Terrain terrain, Mouth mouth, Growth growth, PathExclusion ways, Vector2 noise, float lush)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null || data.detailResolution <= 0)
			{
				return 0;
			}
			Vector3 origin = terrain.transform.position;
			Vector3 size = data.size;
			float outer = Reach + mouth.Radius + 3f;
			var centre = new Vector2(mouth.At.x, mouth.At.z);
			if (centre.x + outer < origin.x || centre.x - outer > origin.x + size.x || centre.y + outer < origin.z || centre.y - outer > origin.z + size.z)
			{
				return 0;
			}
			int res = data.detailResolution;
			float texelX = size.x / res, texelZ = size.z / res;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.x - outer - origin.x) / texelX), 0, res - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((centre.x + outer - origin.x) / texelX), 0, res - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((centre.y - outer - origin.z) / texelZ), 0, res - 1);
			int z1 = Mathf.Clamp(Mathf.CeilToInt((centre.y + outer - origin.z) / texelZ), 0, res - 1);
			if (x1 < x0 || z1 < z0)
			{
				return 0;
			}
			int count = growth.Species.Count;
			var layers = new int[count];
			var maps = new int[count][,];
			int w = x1 - x0 + 1, h = z1 - z0 + 1;
			for (int s = 0; s < count; s++)
			{
				layers[s] = PointOfInterestHerbGrove.LayerOf(data, growth.Species[s]);
				maps[s] = data.GetDetailLayer(x0, z0, w, h, layers[s]);
			}
			// The mouth's own hole: nothing is painted over it (the bore is no ground to grow on).
			int holeRes = data.holesResolution;
			bool[,] solid = holeRes > 0 ? data.GetHoles(0, 0, holeRes, holeRes) : null;
			float unit = Mathf.Max(1, data.maxDetailScatterPerRes) / 255f;
			int maxValue = Mathf.Max(1, data.maxDetailScatterPerRes);
			var changed = new bool[count];
			var want = new float[count];
			var o = new Vector2(mouth.Out.x, mouth.Out.z);
			int cells = 0;
			for (int j = 0; j < h; j++)
			{
				for (int i = 0; i < w; i++)
				{
					if (solid != null && !solid[Mathf.Clamp(Mathf.FloorToInt((z0 + j + 0.5f) / res * holeRes), 0, holeRes - 1), Mathf.Clamp(Mathf.FloorToInt((x0 + i + 0.5f) / res * holeRes), 0, holeRes - 1)])
					{
						continue;
					}
					var p = new Vector2(origin.x + (x0 + i + 0.5f) * texelX, origin.z + (z0 + j + 0.5f) * texelZ);
					Vector2 d = p - centre;
					float distance = d.magnitude;
					// The growth's edge wanders; it is thickest at the shoulders and over the lintel (behind the mouth's plane, up the bank).
					float edge = Reach + mouth.Radius + 3f * (Mathf.PerlinNoise(noise.x + p.x * 0.12f, noise.y + p.y * 0.12f) - 0.5f);
					if (distance > edge || InWayIn(p, mouth, 0.4f, 0.5f) || !Grows(context, p, ways))
					{
						continue;
					}
					float along = Vector2.Dot(d, o) / Mathf.Max(0.1f, distance);
					float shoulder = 0.55f + 0.45f * Mathf.Clamp01(1f - along);
					float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - mouth.Radius) / (edge - mouth.Radius)));
					float clump = Mathf.Clamp01(Mathf.PerlinNoise(noise.x + 41f + p.x * 0.35f, noise.y + 17f + p.y * 0.35f) * 1.7f - 0.35f);
					Array.Clear(want, 0, count);
					float strength = lush * shoulder * near;
					// Ferns in drifts everywhere it grows; bushes in clumps a little back from the rock; creeper hard against it.
					if (growth.Ferns.Count > 0)
					{
						int fern = growth.Ferns[Mathf.Abs(Mathf.FloorToInt(Mathf.PerlinNoise(noise.x + 3f + p.x * 0.08f, noise.y + p.y * 0.08f) * 997f)) % growth.Ferns.Count];
						want[fern] = 210f * strength * (0.45f + 0.55f * clump);
					}
					if (growth.Bushes.Count > 0 && distance > mouth.Radius + 1.5f)
					{
						float bushClump = Mathf.Clamp01(Mathf.PerlinNoise(noise.x + 91f + p.x * 0.22f, noise.y + 53f + p.y * 0.22f) * 2.2f - 1.1f);
						int bush = growth.Bushes[Mathf.Abs(Mathf.FloorToInt(Mathf.PerlinNoise(noise.x + 7f + p.x * 0.05f, noise.y + 9f + p.y * 0.05f) * 991f)) % growth.Bushes.Count];
						want[bush] = 120f * strength * bushClump;
					}
					if (growth.Creeper >= 0 && distance < mouth.Radius + 4f)
					{
						want[growth.Creeper] = 170f * lush * (1f - Mathf.Clamp01((distance - mouth.Radius) / 4f));
					}
					bool grew = false;
					for (int s = 0; s < count; s++)
					{
						int value = Mathf.Clamp(Mathf.RoundToInt(want[s] * unit), 0, maxValue);
						// Only ever more: the ground's own cover of the same plant stays where it is thicker.
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
			for (int s = 0; s < count; s++)
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

		private static bool Grows(PointOfInterestSiteContext context, Vector2 p, PathExclusion ways)
		{
			if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None)
			{
				return false;
			}
			if (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > MaxSlope)
			{
				return false;
			}
			return ways == null || !ways.Details(p.x, p.y);
		}

		private static Vector3 Dir(float yaw)
		{
			float r = yaw * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
		}
	}
}
#endif
