#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a cut's landform stage did: the landforms it placed and carved, the pads their tops offer, and the record of every
	/// height sample it changed (BiomeLandforms.Carving: restoring it gives back the ground before the stage, bit for bit).
	/// </summary>
	public sealed class SceneLandformRecord
	{
		public readonly List<BiomeLandformSite> Sites = new List<BiomeLandformSite>();
		public readonly List<BiomeLandformPad> Pads = new List<BiomeLandformPad>();
		public BiomeLandforms.Carving Carving = new BiomeLandforms.Carving();
		/// <summary>The sea's surface the stage measured depths from; negative infinity for none.</summary>
		public float SeaLevel = float.NegativeInfinity;

		/// <summary>The sites by name and count, for the cut's note: "Pingo×3, Palsa×12".</summary>
		public string Summary()
		{
			var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
			foreach (BiomeLandformSite site in Sites)
			{
				string kind = site.Form.Kind ?? "?";
				counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
			}
			return string.Join(", ", counts.Select(c => $"{c.Key}×{c.Value}"));
		}
	}

	/// <summary>What the landform planner reads (pure: the cut fills it from the scene, tests by hand).</summary>
	public sealed class LandformPlanInput
	{
		/// <summary>The scene's seed.</summary>
		public uint Seed = 1u;
		/// <summary>The biome grid: cells west to east and south to north, their size, and the world position of cell (0, 0)'s south-west corner.</summary>
		public int CellsX;
		public int CellsZ;
		public float CellMetres = 16f;
		public Vector2 Origin;
		/// <summary>The index into <see cref="Biomes"/> of a cell, −1 for none.</summary>
		public Func<int, int, int> CellBiome;
		/// <summary>Each biome present: its template id and its landforms.</summary>
		public List<(int Id, IReadOnlyList<BiomeLandform> Forms)> Biomes = new List<(int, IReadOnlyList<BiomeLandform>)>();
		/// <summary>The ground before any landform, scene metres at world (x, z).</summary>
		public Func<float, float, float> Ground;
		/// <summary>The sea's surface; negative infinity for none.</summary>
		public float SeaLevel = float.NegativeInfinity;
		/// <summary>The prevailing wind's heading, degrees from +x toward +z (where the wind blows to).</summary>
		public float WindYaw;
		/// <summary>The world's air (None: unknown, every landform's air band passes).</summary>
		public BiomeAtmosphereRequirement Air = BiomeAtmosphereRequirement.None;
		/// <summary>The local climate at (x, y, z): temperature and humidity, −1 … 1; null when unknown (every band passes).</summary>
		public Func<float, float, float, Vector2> Climate;
		/// <summary>How firmly the ground is held at (x, z), 0 … 1 (rivers, lakes and their banks: 1); null for none.</summary>
		public Func<float, float, float> Keep;
		/// <summary>Metres from (x, z) to the nearest river or lake; null for none (positive infinity everywhere).</summary>
		public Func<float, float, float> WaterDistance;
		/// <summary>The scene's world box: min x, min z, max x, max z. A landform's footprint stays inside it.</summary>
		public Vector4 SceneBounds;
	}

	/// <summary>
	/// The sculpted landforms' cut stage (2026-10-18, phase I2g; decisions 25, 26, 36(e), 38–50): every biome's landforms
	/// (BiomeTemplate.Landforms, read through <see cref="BiomeTemplateArt.Landforms"/>) placed on its painted patches and carved into
	/// the scene's ground in the height stage, after erosion and before the final water (SceneGround.Shape) — so lakes fill the
	/// craters and pans and rivers find their way round the mounds — then kept with the scene so the repaint paints them again.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deterministic.</b> The places are a pure function of the scene's seed, the biome field the erosion read (the planet's ground
	/// before any shaping) and that ground; every shape is a pure function of its place (<see cref="BiomeLandforms.Shape"/>). A re-cut
	/// carves the same landforms into the same ground. A repaint never carves: the heights stay as cut, and the paint reads the stored
	/// sites (<see cref="GeneratedLandforms"/>), so a repaint paints exactly what the cut painted.
	/// </para>
	/// <para>
	/// <b>Nothing changes by default.</b> No biome names a landform (every template's list is empty): the stage reads nothing more,
	/// changes no sample, stores nothing and notes nothing, and the cut is byte for byte what it was.
	/// </para>
	/// <para>
	/// <b>Kept off the water.</b> The first water laid (before the rain wore the ground) holds its channels, lakes, banks and bars and
	/// a 12 m margin: no landform is placed there and none carves there. The final water is laid on the carved ground.
	/// </para>
	/// </remarks>
	public static class SceneLandforms
	{
		/// <summary>The margin round the first water's channels, lakes and banks that no landform carves into, metres.</summary>
		public const float WaterMarginMetres = 12f;

		/// <summary>The most tries a scattered landform gets per place it wants.</summary>
		private const int TriesPerSite = 16;

		/// <summary>The most stretches a creek network or a traced line may have.</summary>
		private const int MaximumSegments = 4000;

		// ── The cut ───────────────────────────────────────────────────

		/// <summary>
		/// Places and carves every biome's landforms into <paramref name="field"/> (see the class remarks). Returns null — having read
		/// nothing more and changed nothing — when no biome in <paramref name="biomes"/> names a landform.
		/// </summary>
		public static SceneLandformRecord Carve(SceneGenerationRequest request, SolarSystemProfile system, SceneHeightField field, SceneBiomeField biomes,
			SceneWater water, AeolianSettings wind, float seaLevel, uint seed, List<string> notes)
		{
			if (field == null || biomes == null || biomes.Biomes == null)
			{
				return null;
			}
			var forms = new List<(int Id, IReadOnlyList<BiomeLandform> Forms)>();
			bool any = false;
			foreach (BiomeTemplate biome in biomes.Biomes)
			{
				IReadOnlyList<BiomeLandform> list = biome != null ? BiomeTemplateArt.Landforms(biome) : BiomeLandforms.None();
				any |= BiomeLandforms.Any(list);
				forms.Add((biome != null ? biome.ID : 0, list));
			}
			if (!any)
			{
				return null;
			}

			var grid = new BiomeLandforms.Grid(field.Metres, field.Width, field.Depth, field.Spacing, field.EastOf(0), field.NorthOf(0));
			float[] distance = WaterDistanceGrid(water, field);
			Func<float, float, float> distanceAt = distance != null ? (x, z) => Sample(distance, grid, x, z) : (Func<float, float, float>)null;
			Func<float, float, float> keep = distance != null ? (x, z) => 1f - Mathf.SmoothStep(0f, 1f, Sample(distance, grid, x, z) / WaterMarginMetres) : (Func<float, float, float>)null;

			float[] ground = (float[])field.Metres.Clone();
			var input = new LandformPlanInput
			{
				Seed = seed,
				CellsX = biomes.Width,
				CellsZ = biomes.Height,
				CellMetres = biomes.CellMetres,
				Origin = biomes.Origin,
				CellBiome = (x, z) =>
				{
					byte cell = biomes.CellIndex(x, z);
					return cell == SceneBiomeField.None ? -1 : cell;
				},
				Biomes = forms,
				Ground = (x, z) => Sample(ground, grid, x, z),
				SeaLevel = seaLevel,
				WindYaw = wind != null ? Mathf.Atan2(wind.WindZ, wind.WindX) * Mathf.Rad2Deg : 0f,
				Keep = keep,
				WaterDistance = distanceAt,
				SceneBounds = new Vector4(grid.East0, grid.North0, grid.East0 + (grid.Width - 1) * grid.Spacing, grid.North0 + (grid.Depth - 1) * grid.Spacing),
			};
			bool needsAir = false, needsClimate = false;
			foreach ((int _, IReadOnlyList<BiomeLandform> list) in forms)
			{
				foreach (BiomeLandform form in list)
				{
					needsAir |= form.Air != BiomeAtmosphereRequirement.None;
					needsClimate |= !(form.Temperature.x == 0f && form.Temperature.y == 0f) || !(form.Humidity.x == 0f && form.Humidity.y == 0f);
				}
			}
			if (needsAir && request?.Body != null)
			{
				input.Air = AirMask(PlanetClimateField.For(system, request.Body).Conditions.Atmosphere);
			}
			if (needsClimate && request?.Body != null)
			{
				ScenePlacementClimate climate = ScenePlacementClimate.For(system, request.Body, request.Footprint, request.ResolvedRadiusKm, true);
				input.Climate = SceneGenerator.ClimateOf(climate);
			}

			List<BiomeLandformSite> sites = Plan(input);
			var record = new SceneLandformRecord { SeaLevel = seaLevel };
			record.Sites.AddRange(sites);
			if (sites.Count == 0)
			{
				notes?.Add("Landforms: none placed (no patch had room or met the landforms' conditions).");
				return record;
			}
			BiomeLandforms.Context context = ContextOf(biomes, seaLevel);
			record.Carving = BiomeLandforms.Carve(grid, sites, context, keep);
			record.Pads.AddRange(Pads(sites, context, (x, z) => Sample(field.Metres, grid, x, z)));
			string note = $"Landforms: {sites.Count} carved ({record.Summary()}), {record.Carving.Count:N0} height sample(s) changed" +
				(record.Pads.Count > 0 ? $", {record.Pads.Count} pad(s) offered." : ".");
			notes?.Add(note);
			Debug.Log($"[Scene generator] '{request?.SceneName}' {note}");
			return record;
		}

		/// <summary>The world's atmosphere as its one bit of the air mask a landform's band reads.</summary>
		public static BiomeAtmosphereRequirement AirMask(AtmosphereKind kind)
		{
			switch (kind)
			{
				case AtmosphereKind.None: return BiomeAtmosphereRequirement.Airless;
				case AtmosphereKind.Thin: return BiomeAtmosphereRequirement.Thin;
				case AtmosphereKind.Thick: return BiomeAtmosphereRequirement.Thick;
				default: return BiomeAtmosphereRequirement.Standard;
			}
		}

		/// <summary>The shapes' shared context from a biome field: each biome's weight by its id (thread-safe), and the sea's surface.</summary>
		public static BiomeLandforms.Context ContextOf(SceneBiomeField biomes, float seaLevel)
		{
			var indexOf = new Dictionary<int, int>();
			if (biomes != null)
			{
				for (int b = 0; b < biomes.Biomes.Count; b++)
				{
					if (biomes.Biomes[b] != null && !indexOf.ContainsKey(biomes.Biomes[b].ID))
					{
						indexOf[biomes.Biomes[b].ID] = b;
					}
				}
			}
			int count = biomes != null ? Mathf.Max(1, biomes.Biomes.Count) : 1;
			return new BiomeLandforms.Context
			{
				SeaLevel = seaLevel,
				BiomeWeight = biomes == null ? (Func<int, float, float, float>)null : (id, x, z) =>
				{
					if (!indexOf.TryGetValue(id, out int b))
					{
						return 0f;
					}
					float[] weights = scratch;
					if (weights == null || weights.Length < count)
					{
						weights = scratch = new float[count];
					}
					biomes.WeightsAt(x, z, weights);
					return weights[b];
				},
			};
		}

		[ThreadStatic] private static float[] scratch;

		private static float Sample(float[] values, in BiomeLandforms.Grid grid, float x, float z)
		{
			float gx = Mathf.Clamp((x - grid.East0) / grid.Spacing, 0f, grid.Width - 1);
			float gz = Mathf.Clamp((z - grid.North0) / grid.Spacing, 0f, grid.Depth - 1);
			int x0 = Mathf.Min((int)gx, grid.Width - 2), z0 = Mathf.Min((int)gz, grid.Depth - 2);
			x0 = Mathf.Max(0, x0);
			z0 = Mathf.Max(0, z0);
			int x1 = Mathf.Min(x0 + 1, grid.Width - 1), z1 = Mathf.Min(z0 + 1, grid.Depth - 1);
			float fx = gx - x0, fz = gz - z0;
			float south = Mathf.Lerp(values[z0 * grid.Width + x0], values[z0 * grid.Width + x1], fx);
			float north = Mathf.Lerp(values[z1 * grid.Width + x0], values[z1 * grid.Width + x1], fx);
			return Mathf.Lerp(south, north, fz);
		}

		/// <summary>
		/// Metres from every sample of the field to the first water's nearest channel, lake, bank, bar or flat (a two-pass chamfer
		/// distance); null when the scene has no water laid on this grid.
		/// </summary>
		private static float[] WaterDistanceGrid(SceneWater water, SceneHeightField field)
		{
			SceneWaterGrid wet = water?.Grid;
			if (wet == null || wet.Kind.Length != field.Metres.Length)
			{
				return null;
			}
			bool anyWet = false;
			var distance = new float[wet.Kind.Length];
			for (int i = 0; i < distance.Length; i++)
			{
				bool isWet = wet.Kind[i] != WaterKind.None;
				distance[i] = isWet ? 0f : float.PositiveInfinity;
				anyWet |= isWet;
			}
			if (!anyWet)
			{
				return null;
			}
			Chamfer(distance, field.Width, field.Depth, field.Spacing);
			return distance;
		}

		/// <summary>A two-pass 3×3 chamfer distance transform in place (pure): zeros stay, every other cell gets its distance to the nearest zero.</summary>
		public static void Chamfer(float[] d, int width, int depth, float spacing)
		{
			float a = spacing, b = spacing * 1.41421356f;
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					float v = d[i];
					if (x > 0) v = Mathf.Min(v, d[i - 1] + a);
					if (z > 0)
					{
						v = Mathf.Min(v, d[i - width] + a);
						if (x > 0) v = Mathf.Min(v, d[i - width - 1] + b);
						if (x < width - 1) v = Mathf.Min(v, d[i - width + 1] + b);
					}
					d[i] = v;
				}
			}
			for (int z = depth - 1; z >= 0; z--)
			{
				for (int x = width - 1; x >= 0; x--)
				{
					int i = z * width + x;
					float v = d[i];
					if (x < width - 1) v = Mathf.Min(v, d[i + 1] + a);
					if (z < depth - 1)
					{
						v = Mathf.Min(v, d[i + width] + a);
						if (x < width - 1) v = Mathf.Min(v, d[i + width + 1] + b);
						if (x > 0) v = Mathf.Min(v, d[i + width - 1] + b);
					}
					d[i] = v;
				}
			}
		}

		// ── The pads ──────────────────────────────────────────────────

		/// <summary>
		/// The tops the placed landforms offer points of interest (pure but for the ground it asks): each padded site's flat top or
		/// floor at its carved height; a brine lake's mirror; each rimstone pool; a groove's or stone river's resting boulders.
		/// </summary>
		public static List<BiomeLandformPad> Pads(IReadOnlyList<BiomeLandformSite> sites, BiomeLandforms.Context context, Func<float, float, float> carved)
		{
			var pads = new List<BiomeLandformPad>();
			foreach (BiomeLandformSite site in sites)
			{
				BiomeLandform f = site.Form;
				if (!f.Pad)
				{
					continue;
				}
				switch (f.Form)
				{
					case BiomeLandformForm.Rimstone:
						foreach ((Vector2 centre, float radius, float level) in new BiomeLandforms.Shape(site, context).RimstonePools())
						{
							pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = centre, Radius = radius, Level = level, Water = true });
						}
						break;
					case BiomeLandformForm.Grooves:
					case BiomeLandformForm.StoneRiver:
						if (site.Segments != null)
						{
							for (int i = 0; i < site.Segments.Count; i++)
							{
								BiomeLandformSegment seg = site.Segments[i];
								// The grooves end at their boulder (the line's start is its low end); a stone river strews its stones every stretch.
								if (f.Form == BiomeLandformForm.Grooves ? seg.AlongA == 0f : true)
								{
									pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = seg.A, Radius = Mathf.Max(0.5f, seg.Half), Level = carved(seg.A.x, seg.A.y) });
								}
							}
						}
						break;
					case BiomeLandformForm.PitChain:
						// Every pit of the chain (2026-10-25, I2g-b: a lava tube's skylights).
						foreach (Vector2 pit in PitCentres(site))
						{
							pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = pit, Radius = Mathf.Max(1f, site.HalfWidth), Level = carved(pit.x, pit.y) });
						}
						break;
					case BiomeLandformForm.MultiRing:
					{
						// The mare's middle, and each mare pit (where a lava tube's site goes, decision 55).
						pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = site.Centre, Radius = site.Radius * 0.3f, Level = carved(site.Centre.x, site.Centre.y) });
						foreach (Vector3 pit in new BiomeLandforms.Shape(site, context).BasinPits)
						{
							pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = new Vector2(pit.x, pit.y), Radius = pit.z, Level = carved(pit.x, pit.y) });
						}
						break;
					}
					case BiomeLandformForm.Straight:
					case BiomeLandformForm.Contour:
					case BiomeLandformForm.Arc:
						// A line's far end (a sarn's tip, where the sluice ruin stands).
						if (site.Segments != null && site.Segments.Count > 0)
						{
							BiomeLandformSegment last = site.Segments[site.Segments.Count - 1];
							pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = last.B, Radius = Mathf.Max(1f, last.Half), Level = carved(last.B.x, last.B.y) });
						}
						break;
					default:
					{
						float radius = Mathf.Max(1f, Mathf.Min(site.Radius, site.HalfLength) * Mathf.Max(0.25f, f.Top));
						float level = carved(site.Centre.x, site.Centre.y);
						bool water = false;
						if (f.Form == BiomeLandformForm.Pan && (f.DatumDepth.x != 0f || f.DatumDepth.y != 0f) && !float.IsNaN(site.Level))
						{
							// A brine lake's mirror: below its rim by its datum depth, never under its floor.
							float rim = site.Level + site.Height;
							level = Mathf.Max(site.Level, rim - BiomeLandforms.Pick(f.DatumDepth, BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, 0xB41u))));
							water = true;
						}
						pads.Add(new BiomeLandformPad { Kind = f.Kind, BiomeID = site.BiomeID, Centre = site.Centre, Radius = radius, Level = level, Water = water });
						break;
					}
				}
			}
			return pads;
		}

		/// <summary>A pit chain's pits along its line: one every three half widths, from half a pitch in (pure).</summary>
		public static List<Vector2> PitCentres(in BiomeLandformSite site)
		{
			var pits = new List<Vector2>();
			if (site.Segments == null || site.Segments.Count == 0)
			{
				return pits;
			}
			float pitch = 3f * Mathf.Max(0.05f, site.Segments[0].Half);
			int s = 0;
			for (float at = 0.5f * pitch; pits.Count < 4000; at += pitch)
			{
				while (s < site.Segments.Count && site.Segments[s].AlongB < at)
				{
					s++;
				}
				if (s >= site.Segments.Count)
				{
					break;
				}
				BiomeLandformSegment seg = site.Segments[s];
				float span = Mathf.Max(1e-4f, seg.AlongB - seg.AlongA);
				pits.Add(Vector2.Lerp(seg.A, seg.B, Mathf.Clamp01((at - seg.AlongA) / span)));
			}
			return pits;
		}

		// ── The plan ──────────────────────────────────────────────────

		private sealed class Patch
		{
			public int Biome;
			public int First;
			public readonly List<int> Cells = new List<int>();
			public Vector2 Centroid;
			public Vector4 Box;
			public float AreaKm2;
			public float Diameter;
		}

		private static Vector2 CellCentre(LandformPlanInput input, int cell)
		{
			int x = cell % input.CellsX, z = cell / input.CellsX;
			return input.Origin + new Vector2((x + 0.5f) * input.CellMetres, (z + 0.5f) * input.CellMetres);
		}

		private static List<Patch> Patches(LandformPlanInput input)
		{
			int n = input.CellsX * input.CellsZ;
			var label = new int[n];
			var biomeOf = new int[n];
			for (int z = 0; z < input.CellsZ; z++)
			{
				for (int x = 0; x < input.CellsX; x++)
				{
					biomeOf[z * input.CellsX + x] = input.CellBiome(x, z);
				}
			}
			var patches = new List<Patch>();
			var queue = new Queue<int>();
			for (int start = 0; start < n; start++)
			{
				if (label[start] != 0 || biomeOf[start] < 0)
				{
					continue;
				}
				var patch = new Patch { Biome = biomeOf[start], First = start };
				patches.Add(patch);
				label[start] = patches.Count;
				queue.Enqueue(start);
				while (queue.Count > 0)
				{
					int c = queue.Dequeue();
					patch.Cells.Add(c);
					int cx = c % input.CellsX, cz = c / input.CellsX;
					for (int k = 0; k < 4; k++)
					{
						int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = cz + (k == 2 ? 1 : k == 3 ? -1 : 0);
						if (nx < 0 || nz < 0 || nx >= input.CellsX || nz >= input.CellsZ)
						{
							continue;
						}
						int nc = nz * input.CellsX + nx;
						if (label[nc] == 0 && biomeOf[nc] == patch.Biome)
						{
							label[nc] = patches.Count;
							queue.Enqueue(nc);
						}
					}
				}
				var sum = Vector2.zero;
				float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
				foreach (int c in patch.Cells)
				{
					Vector2 p = CellCentre(input, c);
					sum += p;
					minX = Mathf.Min(minX, p.x);
					maxX = Mathf.Max(maxX, p.x);
					minZ = Mathf.Min(minZ, p.y);
					maxZ = Mathf.Max(maxZ, p.y);
				}
				float half = input.CellMetres * 0.5f;
				patch.Centroid = sum / patch.Cells.Count;
				patch.Box = new Vector4(minX - half, minZ - half, maxX + half, maxZ + half);
				float area = patch.Cells.Count * input.CellMetres * input.CellMetres;
				patch.AreaKm2 = area / 1e6f;
				patch.Diameter = 2f * Mathf.Sqrt(area / Mathf.PI);
			}
			return patches;
		}

		private static Vector2 Gradient(LandformPlanInput input, Vector2 p, float h)
		{
			float gx = (input.Ground(p.x + h, p.y) - input.Ground(p.x - h, p.y)) / (2f * h);
			float gz = (input.Ground(p.x, p.y + h) - input.Ground(p.x, p.y - h)) / (2f * h);
			return new Vector2(gx, gz);
		}

		private static float SlopeAt(LandformPlanInput input, Vector2 p) => Mathf.Atan(Gradient(input, p, Mathf.Max(2f, input.CellMetres * 0.5f)).magnitude) * Mathf.Rad2Deg;

		private static int BiomeAt(LandformPlanInput input, Vector2 p)
		{
			int x = Mathf.FloorToInt((p.x - input.Origin.x) / input.CellMetres), z = Mathf.FloorToInt((p.y - input.Origin.y) / input.CellMetres);
			if (x < 0 || z < 0 || x >= input.CellsX || z >= input.CellsZ)
			{
				return -1;
			}
			return input.CellBiome(x, z);
		}

		private static bool Inside(LandformPlanInput input, Vector2 p, float margin)
		{
			Vector4 b = input.SceneBounds;
			return p.x - margin >= b.x && p.x + margin <= b.z && p.y - margin >= b.y && p.y + margin <= b.w;
		}

		private static bool Admits(BiomeLandform form, LandformPlanInput input, Vector2 p, out float ground)
		{
			ground = input.Ground(p.x, p.y);
			if (!BiomeLandforms.InBand(form.Slope, SlopeAt(input, p)))
			{
				return false;
			}
			if (!(form.Depth.x == 0f && form.Depth.y == 0f))
			{
				if (float.IsNegativeInfinity(input.SeaLevel) || !BiomeLandforms.InBand(form.Depth, input.SeaLevel - ground))
				{
					return false;
				}
			}
			if (input.Climate != null && (!(form.Temperature.x == 0f && form.Temperature.y == 0f) || !(form.Humidity.x == 0f && form.Humidity.y == 0f)))
			{
				Vector2 climate = input.Climate(p.x, ground, p.y);
				if (!BiomeLandforms.InBand(form.Temperature, climate.x) || !BiomeLandforms.InBand(form.Humidity, climate.y))
				{
					return false;
				}
			}
			if (input.Keep != null && input.Keep(p.x, p.y) >= 0.5f)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Places every biome's landforms (pure): for each biome in order, each of its landforms in list order, over each of its
		/// painted patches in grid order. Scattered ones by density with their conditions, kept apart from every landform placed
		/// before them; fitted ones one a patch at its anchor; fields one a patch. Passes (kandu) come last in their biome, cut through
		/// its rings. The same input always gives the same sites in the same order.
		/// </summary>
		public static List<BiomeLandformSite> Plan(LandformPlanInput input)
		{
			var sites = new List<BiomeLandformSite>();
			if (input == null || input.CellBiome == null || input.Ground == null || input.Biomes == null || input.CellsX <= 0 || input.CellsZ <= 0)
			{
				return sites;
			}
			bool any = false;
			foreach ((int _, IReadOnlyList<BiomeLandform> forms) in input.Biomes)
			{
				any |= BiomeLandforms.Any(forms);
			}
			if (!any)
			{
				return sites;
			}
			List<Patch> patches = Patches(input);
			var spacing = new List<(Vector2 At, float Radius)>();
			for (int b = 0; b < input.Biomes.Count; b++)
			{
				(int id, IReadOnlyList<BiomeLandform> forms) = input.Biomes[b];
				if (forms == null || forms.Count == 0)
				{
					continue;
				}
				List<Patch> own = patches.FindAll(p => p.Biome == b);
				if (own.Count == 0)
				{
					continue;
				}
				// The biome's dune fields share its sand by their densities: each a band of one mix noise.
				float duneTotal = 0f;
				int dunes = 0;
				foreach (BiomeLandform form in forms)
				{
					if (form.Carves && form.Family == BiomeLandformFamily.Dunes)
					{
						duneTotal += Mathf.Max(0f, form.Density);
						dunes++;
					}
				}
				float duneRun = 0f;
				var deferred = new List<(int Index, BiomeLandform Form)>();
				for (int e = 0; e < forms.Count; e++)
				{
					BiomeLandform form = forms[e];
					if (!form.Carves || !(form.Air == BiomeAtmosphereRequirement.None || input.Air == BiomeAtmosphereRequirement.None || (form.Air & input.Air) != 0))
					{
						continue;
					}
					if (form.Form == BiomeLandformForm.Pass)
					{
						deferred.Add((e, form));
						continue;
					}
					float mixLow = 0f, mixHigh = 1f;
					if (form.Family == BiomeLandformFamily.Dunes && dunes > 1)
					{
						float share = duneTotal > 0f ? Mathf.Max(0f, form.Density) / duneTotal : 1f / dunes;
						mixLow = duneRun;
						duneRun += share;
						mixHigh = duneRun;
					}
					if (form.Anchor == BiomeLandformAnchor.LeeOf)
					{
						// In the lee of each site of the entry before it (2026-10-25, decision 64: a blue-ice tail behind each nunatak).
						if (e > 0)
						{
							PlaceInLee(input, form, forms[e - 1].Kind, id, b, e, sites);
						}
						continue;
					}
					int inScene = 0;
					int entryStart = sites.Count, entrySpacing = spacing.Count;
					foreach (Patch patch in own)
					{
						if (form.PerScene > 0 && inScene >= form.PerScene)
						{
							break;
						}
						if (form.Form == BiomeLandformForm.MultiRing && patch.Diameter < Mathf.Min(form.Size.x, form.Size.y))
						{
							// A multi-ring basin only in a patch as wide as its smallest (decision 55: the basin detection's own threshold).
							continue;
						}
						uint patchSeed = BiomeLandforms.Hash(input.Seed, (uint)id, (uint)patch.First, (uint)e);
						if (form.Placement == BiomeLandformPlacement.Field || BiomeLandforms.IsField(form.Form))
						{
							BiomeLandformSite field = FieldSite(input, form, id, patch, patchSeed, mixLow, mixHigh, BiomeLandforms.Hash(input.Seed, (uint)id, (uint)patch.First, 0xD0u));
							sites.Add(field);
							inScene++;
							continue;
						}
						int wanted;
						if (form.Placement == BiomeLandformPlacement.Fitted)
						{
							wanted = 1;
						}
						else
						{
							float expected = form.Density * patch.AreaKm2;
							wanted = Mathf.FloorToInt(expected) + (BiomeLandforms.Unit(BiomeLandforms.Hash(patchSeed, 0x11u)) < expected - Mathf.Floor(expected) ? 1 : 0);
						}
						if (form.PerPatch > 0)
						{
							wanted = Mathf.Min(wanted, form.PerPatch);
						}
						if (form.PerScene > 0)
						{
							wanted = Mathf.Min(wanted, form.PerScene - inScene);
						}
						if (wanted <= 0)
						{
							continue;
						}
						List<int> candidates = Candidates(input, form, patch, patchSeed);
						int placed = 0, tries = 0;
						float margin = Margin(form);
						for (int t = 0; t < candidates.Count && placed < wanted && tries < wanted * TriesPerSite; t++)
						{
							uint siteSeed = BiomeLandforms.Hash(patchSeed, (uint)t, 0x5173u);
							Vector2 at = CellCentre(input, candidates[t]);
							if (!Inside(input, at, margin))
							{
								// Too near the scene's edge for its footprint: not a try (the lowest or highest ground is often the edge).
								continue;
							}
							tries++;
							if (form.Anchor == BiomeLandformAnchor.Centroid || form.Placement == BiomeLandformPlacement.Fitted && form.Anchor == BiomeLandformAnchor.Anywhere)
							{
								at = patch.Centroid;
							}
							else if (form.Anchor != BiomeLandformAnchor.Lowest && form.Anchor != BiomeLandformAnchor.Highest)
							{
								at += new Vector2(BiomeLandforms.Unit(BiomeLandforms.Hash(siteSeed, 1u)) - 0.5f, BiomeLandforms.Unit(BiomeLandforms.Hash(siteSeed, 2u)) - 0.5f) * input.CellMetres;
							}
							if (!Admits(form, input, at, out float ground))
							{
								continue;
							}
							if (form.Placement == BiomeLandformPlacement.Chain)
							{
								// A chain's members overlap each other (merged rims) but keep apart from everything else (2026-10-25, decision 71).
								List<BiomeLandformSite> members = Chain(input, form, id, b, patch, at, siteSeed, out float chainReach);
								bool chainClear = members.Count >= 2;
								foreach ((Vector2 other, float radius) in spacing)
								{
									if (chainClear && Vector2.Distance(other, at) < 0.85f * (chainReach + radius))
									{
										chainClear = false;
									}
								}
								if (!chainClear)
								{
									continue;
								}
								spacing.Add((at, chainReach));
								sites.AddRange(members);
								placed++;
								inScene++;
								continue;
							}
							// A size law draws each one's size (decision 72: a crater population, many small, few large).
							float drawn = form.SizePower > 0f ? BiomeLandforms.PowerLawPick(form.Size, form.SizePower, BiomeLandforms.Unit(BiomeLandforms.Hash(siteSeed, 0x9017u))) : float.NaN;
							if (!TrySite(input, form, id, b, patch, at, ground, siteSeed, out BiomeLandformSite site, drawn))
							{
								continue;
							}
							float reach = SpacingRadius(site);
							// A size-law population overlaps itself (a young bowl cuts an old rim) and keeps apart only from others.
							bool clear = true;
							int keepFrom = form.SizePower > 0f ? entrySpacing : spacing.Count;
							for (int q = 0; q < keepFrom; q++)
							{
								if (Vector2.Distance(spacing[q].At, site.Centre) < 0.85f * (reach + spacing[q].Radius))
								{
									clear = false;
									break;
								}
							}
							if (!clear)
							{
								continue;
							}
							spacing.Add((site.Centre, reach));
							sites.Add(site);
							placed++;
							inScene++;
						}
					}
					if (form.Ages && sites.Count - entryStart > 1)
					{
						// Old craters first, so each younger bowl is carved over (cuts the rims of) the older (decision 72); stable.
						var aged = sites.GetRange(entryStart, sites.Count - entryStart);
						var order = new List<int>(aged.Count);
						for (int i = 0; i < aged.Count; i++)
						{
							order.Add(i);
						}
						order.Sort((p, q) =>
						{
							int byAge = BiomeLandforms.CraterAge(aged[q].Seed).CompareTo(BiomeLandforms.CraterAge(aged[p].Seed));
							return byAge != 0 ? byAge : p.CompareTo(q);
						});
						for (int i = 0; i < order.Count; i++)
						{
							sites[entryStart + i] = aged[order[i]];
						}
					}
				}
				// Passes through the biome's rings (kandu), after everything else it placed.
				foreach ((int e, BiomeLandform form) in deferred)
				{
					int count = Mathf.Max(1, form.Count);
					int rings = sites.Count;
					for (int s = 0; s < rings; s++)
					{
						BiomeLandformSite ring = sites[s];
						if (ring.BiomeID != id || ring.Form.Form != BiomeLandformForm.Ring)
						{
							continue;
						}
						for (int k = 0; k < count; k++)
						{
							uint seed = BiomeLandforms.Hash(ring.Seed, (uint)e, (uint)k, 0x7A55u);
							float angle = (k + 0.6f * BiomeLandforms.Unit(seed)) * (360f / count);
							var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
							float half = 0.5f * BiomeLandforms.Pick(form.Width, BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 1u)));
							float depth = BiomeLandforms.Pick(form.Height, BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 2u)));
							float reach = ring.HalfWidth * 1.8f + 2f * half;
							Vector2 a = ring.Centre + dir * Mathf.Max(0f, ring.Radius - reach), bEnd = ring.Centre + dir * (ring.Radius + reach);
							var pass = new BiomeLandformSite
							{
								Form = form,
								BiomeID = id,
								Centre = (a + bEnd) * 0.5f,
								Radius = half,
								HalfLength = Vector2.Distance(a, bEnd) * 0.5f,
								HalfWidth = half,
								Height = depth,
								Yaw = angle,
								Seed = seed,
								Level = !float.IsNaN(ring.Level) ? ring.Level - depth : float.NaN,
								Ground = ring.Ground,
								Segments = Line(new List<Vector2> { a, bEnd }, half, depth, false),
							};
							pass.Bounds = BiomeLandforms.BoundsOf(pass);
							sites.Add(pass);
						}
					}
				}
			}
			return sites;
		}

		/// <summary>
		/// A chain's members (<see cref="BiomeLandformPlacement.Chain"/>, pure but for the ground; 2026-10-25, decision 71): up to
		/// <see cref="BiomeLandform.Count"/> sites of the form along a line through <paramref name="at"/>, straight or bent through
		/// <see cref="BiomeLandform.Lean"/> of a half turn, their sizes tapering to both ends; a member whose place falls outside the
		/// scene, the biome or the form's conditions is left out. <paramref name="reach"/> is the chain's half length plus its largest member.
		/// </summary>
		private static List<BiomeLandformSite> Chain(LandformPlanInput input, BiomeLandform form, int id, int biomeIndex, Patch patch, Vector2 at, uint seed,
			out float reach)
		{
			float U(uint salt) => BiomeLandforms.Unit(BiomeLandforms.Hash(seed, salt));
			var members = new List<BiomeLandformSite>();
			float length = BiomeLandforms.Pick(form.Length, U(21u));
			int most = Mathf.Max(2, form.Count > 0 ? form.Count : 8);
			int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(0.3f * most, most, U(22u))), 2, most);
			float yaw = YawOf(input, form, at, seed) * Mathf.Deg2Rad;
			var axis = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
			var normal = new Vector2(-axis.y, axis.x);
			float bend = Mathf.Clamp(form.Lean, -1f, 1f) * Mathf.PI;
			float biggest = 0f;
			for (int i = 0; i < n; i++)
			{
				uint memberSeed = BiomeLandforms.Hash(seed, (uint)i, 0xC4A1u);
				float t = i / (n - 1f) - 0.5f;
				Vector2 p, side;
				if (Mathf.Abs(bend) > 1e-3f)
				{
					float arcRadius = length / bend, a = t * bend;
					p = at + axis * (arcRadius * Mathf.Sin(a)) + normal * (arcRadius * (1f - Mathf.Cos(a)));
					side = normal * Mathf.Cos(a) - axis * Mathf.Sin(a);
				}
				else
				{
					p = at + axis * (t * length);
					side = normal;
				}
				float size = BiomeLandforms.Pick(form.Size, BiomeLandforms.Unit(BiomeLandforms.Hash(memberSeed, 1u))) * (0.5f + 0.5f * Mathf.Sin(Mathf.PI * (i + 0.5f) / n));
				p += side * (form.Roughness * size * (2f * BiomeLandforms.Unit(BiomeLandforms.Hash(memberSeed, 2u)) - 1f));
				biggest = Mathf.Max(biggest, size);
				if (!Inside(input, p, size) || BiomeAt(input, p) != biomeIndex || !Admits(form, input, p, out float ground))
				{
					continue;
				}
				if (TrySite(input, form, id, biomeIndex, patch, p, ground, memberSeed, out BiomeLandformSite site, size))
				{
					members.Add(site);
				}
			}
			reach = 0.5f * length + biggest;
			return members;
		}

		/// <summary>
		/// Places a lee-anchored entry (<see cref="BiomeLandformAnchor.LeeOf"/>, pure): one for each site the biome's entry named
		/// <paramref name="host"/> placed, downwind of it by the host's reach and most of its own half length, in the host's order.
		/// </summary>
		private static void PlaceInLee(LandformPlanInput input, BiomeLandform form, string host, int id, int biomeIndex, int entry, List<BiomeLandformSite> sites)
		{
			int made = 0;
			int count = sites.Count;
			for (int s = 0; s < count; s++)
			{
				BiomeLandformSite lee = sites[s];
				if (lee.BiomeID != id || !string.Equals(lee.Form.Kind, host, StringComparison.Ordinal))
				{
					continue;
				}
				if (form.PerScene > 0 && made >= form.PerScene)
				{
					break;
				}
				uint seed = BiomeLandforms.Hash(lee.Seed, (uint)entry, 0x1EEu);
				float yaw = YawOf(input, form, lee.Centre, seed) * Mathf.Deg2Rad;
				var axis = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
				float half = 0.5f * BiomeLandforms.Pick(form.Length, BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 11u)));
				Vector2 at = lee.Centre + axis * (Mathf.Max(lee.Radius, lee.HalfLength) + 0.9f * half);
				if (!Inside(input, at, Margin(form)))
				{
					continue;
				}
				if (TrySite(input, form, id, biomeIndex, null, at, input.Ground(at.x, at.y), seed, out BiomeLandformSite site))
				{
					sites.Add(site);
					made++;
				}
			}
		}

		/// <summary>The least distance from the scene's edge a landform's place needs, metres (its footprint, roughly; the site checks exactly).</summary>
		private static float Margin(BiomeLandform form)
		{
			float size = Mathf.Max(form.Size.x, form.Size.y), width = Mathf.Max(form.Width.x, form.Width.y);
			if (BiomeLandforms.IsLine(form.Form))
			{
				return 1.75f * width;
			}
			if (form.Placement == BiomeLandformPlacement.Chain)
			{
				// Most of a chain inside the scene; members past the edge are left out.
				return 0.3f * Mathf.Max(form.Length.x, form.Length.y) + size;
			}
			return size * (form.Family == BiomeLandformFamily.Crater ? 1f : 0.7f) + form.Moat;
		}

		private static float SpacingRadius(in BiomeLandformSite site)
		{
			if (BiomeLandforms.IsLine(site.Form.Form))
			{
				return Mathf.Max(site.HalfWidth, 1f) * 2f;
			}
			if (site.Form.Family == BiomeLandformFamily.Crater)
			{
				return site.Radius * 1.5f;
			}
			if (site.Form.Form == BiomeLandformForm.Ring)
			{
				return site.Radius + site.HalfWidth;
			}
			return Mathf.Max(site.Radius, site.HalfLength) + site.Form.Moat;
		}

		/// <summary>The patch's cells in the order a landform tries them (pure): by its anchor, shuffled by the patch's seed where it is any.</summary>
		private static List<int> Candidates(LandformPlanInput input, BiomeLandform form, Patch patch, uint seed)
		{
			var cells = new List<int>(patch.Cells);
			switch (form.Anchor)
			{
				case BiomeLandformAnchor.Lowest:
				case BiomeLandformAnchor.Highest:
				{
					var heights = new Dictionary<int, float>();
					foreach (int c in cells)
					{
						Vector2 p = CellCentre(input, c);
						heights[c] = input.Ground(p.x, p.y);
					}
					cells.Sort((a, b) =>
					{
						int order = heights[a].CompareTo(heights[b]);
						return order != 0 ? (form.Anchor == BiomeLandformAnchor.Lowest ? order : -order) : a.CompareTo(b);
					});
					return cells;
				}
				case BiomeLandformAnchor.Shore:
				{
					var shore = new List<int>();
					if (!float.IsNegativeInfinity(input.SeaLevel))
					{
						foreach (int c in cells)
						{
							int x = c % input.CellsX, z = c / input.CellsX;
							Vector2 p = CellCentre(input, c);
							if (input.Ground(p.x, p.y) < input.SeaLevel)
							{
								continue;
							}
							for (int k = 0; k < 4; k++)
							{
								int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
								if (nx < 0 || nz < 0 || nx >= input.CellsX || nz >= input.CellsZ)
								{
									continue;
								}
								Vector2 q = input.Origin + new Vector2((nx + 0.5f) * input.CellMetres, (nz + 0.5f) * input.CellMetres);
								if (input.Ground(q.x, q.y) < input.SeaLevel)
								{
									shore.Add(c);
									break;
								}
							}
						}
					}
					Shuffle(shore, seed);
					return shore;
				}
				case BiomeLandformAnchor.SlopeBreak:
				{
					var breaks = new List<int>();
					foreach (int c in cells)
					{
						Vector2 p = CellCentre(input, c);
						float slope = SlopeAt(input, p);
						if (!BiomeLandforms.InBand(form.Slope, slope))
						{
							continue;
						}
						Vector2 g = Gradient(input, p, Mathf.Max(2f, input.CellMetres * 0.5f));
						if (g.sqrMagnitude < 1e-8f)
						{
							continue;
						}
						Vector2 below = p - g.normalized * input.CellMetres * 2f;
						if (SlopeAt(input, below) < slope * 0.5f)
						{
							breaks.Add(c);
						}
					}
					Shuffle(breaks, seed);
					return breaks;
				}
				case BiomeLandformAnchor.PatchEdge:
				{
					// The cells the inset inside the patch's edge (a neighbour inside the scene but outside the patch; decision 70).
					var inPatch = new HashSet<int>(cells);
					List<int> edge = AtInset(input, cells, c => true, n => !inPatch.Contains(n), form.NearWater > 0f ? form.NearWater : 40f);
					Shuffle(edge, seed);
					return edge;
				}
				case BiomeLandformAnchor.ShoreSetback:
				{
					// The cells the setback from the shoreline, inland or (an entry asking water depth) out to sea (decision 74).
					var setback = new List<int>();
					if (!float.IsNegativeInfinity(input.SeaLevel))
					{
						bool seaward = form.Depth.y > 0f || form.Depth.x > 0f;
						bool Wet(int c)
						{
							Vector2 p = CellCentre(input, c);
							return input.Ground(p.x, p.y) < input.SeaLevel;
						}
						setback = AtInset(input, cells, c => Wet(c) == seaward, n => Wet(n) != seaward, form.NearWater > 0f ? form.NearWater : 50f);
					}
					Shuffle(setback, seed);
					return setback;
				}
				case BiomeLandformAnchor.BayMouth:
				{
					// Shallow water with a shore within reach on two opposite sides and open water off one end (decision 74).
					var mouths = new List<int>();
					if (!float.IsNegativeInfinity(input.SeaLevel))
					{
						float reach = 0.5f * Mathf.Max(80f, Mathf.Max(form.Length.x, form.Length.y));
						foreach (int c in cells)
						{
							Vector2 p = CellCentre(input, c);
							float g = input.Ground(p.x, p.y);
							if (g < input.SeaLevel && g > input.SeaLevel - 15f && BayAxis(input, p, reach, out _, out _, out _))
							{
								mouths.Add(c);
							}
						}
					}
					Shuffle(mouths, seed);
					return mouths;
				}
				case BiomeLandformAnchor.NearWater:
				{
					var near = new List<int>();
					if (input.WaterDistance != null)
					{
						float reach = form.NearWater > 0f ? form.NearWater : 50f;
						foreach (int c in cells)
						{
							Vector2 p = CellCentre(input, c);
							if (input.WaterDistance(p.x, p.y) <= reach)
							{
								near.Add(c);
							}
						}
					}
					Shuffle(near, seed);
					return near;
				}
				default:
					Shuffle(cells, seed);
					return cells;
			}
		}

		/// <summary>
		/// The cells of <paramref name="cells"/> (those <paramref name="side"/> admits) whose 4-step distance, breadth-first through admitted
		/// cells from those touching an <paramref name="other"/> cell inside the scene, is about <paramref name="inset"/> metres (pure but
		/// for the predicates), in the list's order.
		/// </summary>
		private static List<int> AtInset(LandformPlanInput input, List<int> cells, Func<int, bool> side, Func<int, bool> other, float inset)
		{
			var steps = new Dictionary<int, int>();
			var queue = new Queue<int>();
			var admitted = new HashSet<int>();
			foreach (int c in cells)
			{
				if (side(c))
				{
					admitted.Add(c);
				}
			}
			foreach (int c in cells)
			{
				if (!admitted.Contains(c))
				{
					continue;
				}
				int x = c % input.CellsX, z = c / input.CellsX;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx >= 0 && nz >= 0 && nx < input.CellsX && nz < input.CellsZ && other(nz * input.CellsX + nx))
					{
						steps[c] = 0;
						queue.Enqueue(c);
						break;
					}
				}
			}
			while (queue.Count > 0)
			{
				int c = queue.Dequeue();
				int x = c % input.CellsX, z = c / input.CellsX;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					int n = nz * input.CellsX + nx;
					if (nx >= 0 && nz >= 0 && nx < input.CellsX && nz < input.CellsZ && admitted.Contains(n) && !steps.ContainsKey(n))
					{
						steps[n] = steps[c] + 1;
						queue.Enqueue(n);
					}
				}
			}
			float tolerance = Mathf.Max(0.5f * input.CellMetres, 0.25f * inset);
			var found = new List<int>();
			foreach (int c in cells)
			{
				if (steps.TryGetValue(c, out int d) && Mathf.Abs((d + 0.5f) * input.CellMetres - inset) <= tolerance)
				{
					found.Add(c);
				}
			}
			return found;
		}

		/// <summary>
		/// The shortest shore-to-shore span through a point in the water, within <paramref name="reach"/> each way, of eight headings,
		/// with open water (no shore within the reach) off at least one side of it (pure but for the ground): a bay's mouth.
		/// </summary>
		private static bool BayAxis(LandformPlanInput input, Vector2 p, float reach, out float yaw, out Vector2 middle, out float half)
		{
			yaw = 0f;
			middle = p;
			half = float.MaxValue;
			float step = Mathf.Max(2f, 0.5f * input.CellMetres);
			float Landfall(Vector2 dir)
			{
				for (float d = step; d <= reach; d += step)
				{
					Vector2 q = p + dir * d;
					if (!Inside(input, q, 0f))
					{
						return float.NaN;
					}
					if (input.Ground(q.x, q.y) >= input.SeaLevel)
					{
						return d;
					}
				}
				return float.NaN;
			}
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI / 8f;
				var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				float ahead = Landfall(dir), behind = Landfall(-dir);
				if (float.IsNaN(ahead) || float.IsNaN(behind) || 0.5f * (ahead + behind) >= half)
				{
					continue;
				}
				var across = new Vector2(-dir.y, dir.x);
				if (!float.IsNaN(Landfall(across)) && !float.IsNaN(Landfall(-across)))
				{
					// Shut in on all four sides: a lagoon or a lake, not a mouth.
					continue;
				}
				half = 0.5f * (ahead + behind);
				middle = p + dir * (0.5f * (ahead - behind));
				yaw = a * Mathf.Rad2Deg;
			}
			return half < float.MaxValue;
		}

		private static void Shuffle(List<int> list, uint seed)
		{
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = (int)(BiomeLandforms.Hash(seed, (uint)i, 0x5Fu) % (uint)(i + 1));
				(list[i], list[j]) = (list[j], list[i]);
			}
		}

		private static float YawOf(LandformPlanInput input, BiomeLandform form, Vector2 at, uint seed)
		{
			switch (form.Align)
			{
				case BiomeLandformAlign.Wind:
					return input.WindYaw + 10f * (BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 0xA1u)) - 0.5f);
				case BiomeLandformAlign.Downslope:
				case BiomeLandformAlign.Contour:
				{
					Vector2 g = Gradient(input, at, Mathf.Max(4f, input.CellMetres));
					float yaw = g.sqrMagnitude > 1e-10f ? Mathf.Atan2(-g.y, -g.x) * Mathf.Rad2Deg : BiomeLandforms.Unit(seed) * 360f;
					return form.Align == BiomeLandformAlign.Contour ? yaw + 90f : yaw;
				}
				case BiomeLandformAlign.SceneAxis:
					return BiomeLandforms.Unit(BiomeLandforms.Hash(input.Seed, 0xA815u)) * 180f + 6f * (BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 0xA2u)) - 0.5f);
				case BiomeLandformAlign.Cardinal:
				case BiomeLandformAlign.Meridian:
					// Square to the compass (2026-10-25, I2g-b): east, or north, within a degree.
					return (form.Align == BiomeLandformAlign.Meridian ? 90f : 0f) + (BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 0xA4u)) - 0.5f);
				default:
					return BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 0xA3u)) * 360f;
			}
		}

		/// <summary>One field site covering a patch (dunes, waves, flow steps, spill fill).</summary>
		private static BiomeLandformSite FieldSite(LandformPlanInput input, BiomeLandform form, int id, Patch patch, uint seed, float mixLow, float mixHigh, uint mixSeed)
		{
			float spacing = BiomeLandforms.Pick(form.Width.x > 0f || form.Width.y > 0f ? form.Width : new Vector2(60f, 60f), BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 3u)));
			Vector2 centre = patch.Centroid;
			float yaw;
			if (form.Align == BiomeLandformAlign.Downslope || form.Align == BiomeLandformAlign.Contour)
			{
				// A field's one heading: its patch's mean way down.
				Vector2 sum = Vector2.zero;
				foreach (int c in patch.Cells)
				{
					sum += Gradient(input, CellCentre(input, c), Mathf.Max(4f, input.CellMetres));
				}
				yaw = sum.sqrMagnitude > 1e-12f ? Mathf.Atan2(-sum.y, -sum.x) * Mathf.Rad2Deg : BiomeLandforms.Unit(seed) * 360f;
				if (form.Align == BiomeLandformAlign.Contour)
				{
					yaw += 90f;
				}
			}
			else
			{
				yaw = YawOf(input, form, centre, seed);
			}
			float grow = input.CellMetres * 2f;
			var site = new BiomeLandformSite
			{
				Form = form,
				BiomeID = id,
				Centre = centre,
				Radius = spacing * 0.5f,
				HalfLength = spacing * 0.5f,
				HalfWidth = spacing * 0.5f,
				Height = BiomeLandforms.Pick(form.Height, BiomeLandforms.Unit(BiomeLandforms.Hash(seed, 4u))),
				Yaw = yaw,
				Seed = seed,
				Level = float.NaN,
				Ground = input.Ground(centre.x, centre.y),
				Bounds = new Vector4(Mathf.Max(input.SceneBounds.x, patch.Box.x - grow), Mathf.Max(input.SceneBounds.y, patch.Box.y - grow),
					Mathf.Min(input.SceneBounds.z, patch.Box.z + grow), Mathf.Min(input.SceneBounds.w, patch.Box.w + grow)),
				MixLow = mixLow,
				MixHigh = mixHigh,
				MixSeed = mixSeed,
			};
			if (form.Form == BiomeLandformForm.Rills)
			{
				// Each rill lattice cell's own way down, from the ground at its centre (the grooves bend with the slopes).
				float cell = BiomeLandforms.Shape.RillCell(spacing);
				site.Segments = new List<BiomeLandformSegment>();
				int x0 = Mathf.FloorToInt(site.Bounds.x / cell) - 1, x1 = Mathf.FloorToInt(site.Bounds.z / cell) + 1;
				int z0 = Mathf.FloorToInt(site.Bounds.y / cell) - 1, z1 = Mathf.FloorToInt(site.Bounds.w / cell) + 1;
				for (int j = z0; j <= z1; j++)
				{
					for (int i = x0; i <= x1; i++)
					{
						var at = new Vector2((i + 0.5f) * cell, (j + 0.5f) * cell);
						Vector2 g = Gradient(input, at, Mathf.Max(2f, cell * 0.25f));
						site.Segments.Add(new BiomeLandformSegment { A = at, B = g.sqrMagnitude > 1e-12f ? -g.normalized : Vector2.zero });
					}
				}
			}
			return site;
		}

		/// <summary>One scattered or fitted landform at a place (false when its line could not be traced or it leaves the scene).</summary>
		private static bool TrySite(LandformPlanInput input, BiomeLandform form, int id, int biomeIndex, Patch patch, Vector2 at, float ground, uint seed,
			out BiomeLandformSite site, float sizeOverride = float.NaN)
		{
			float U(uint salt) => BiomeLandforms.Unit(BiomeLandforms.Hash(seed, salt));
			float size = float.IsNaN(sizeOverride) ? BiomeLandforms.Pick(form.Size, U(10u)) : sizeOverride;
			if (form.Placement == BiomeLandformPlacement.Fitted && form.FitShare > 0f)
			{
				size = Mathf.Clamp(form.FitShare * patch.Diameter, Mathf.Min(form.Size.x, form.Size.y), Mathf.Max(form.Size.x, form.Size.y));
			}
			float length = BiomeLandforms.Pick(form.Length, U(11u));
			float width = BiomeLandforms.Pick(form.Width, U(12u));
			float height = BiomeLandforms.Pick(form.Height, U(13u));
			float radius = Mathf.Max(0.25f, size * 0.5f);
			site = new BiomeLandformSite
			{
				Form = form,
				BiomeID = id,
				Centre = at,
				Radius = radius,
				HalfLength = Mathf.Max(radius, length * 0.5f),
				HalfWidth = Mathf.Max(0.1f, width * 0.5f),
				Height = height,
				Yaw = YawOf(input, form, at, seed),
				Seed = seed,
				Level = float.NaN,
				Ground = ground,
			};
			if (form.Anchor == BiomeLandformAnchor.BayMouth && !float.IsNegativeInfinity(input.SeaLevel))
			{
				// Laid along the mouth's span, shore to shore and a little into each (decision 74: an ayre).
				float reach = 0.5f * Mathf.Max(80f, Mathf.Max(form.Length.x, form.Length.y));
				if (!BayAxis(input, at, reach, out float bayYaw, out Vector2 middle, out float bayHalf))
				{
					return false;
				}
				site.Yaw = bayYaw;
				site.Centre = middle;
				site.HalfLength = bayHalf + 2f * site.HalfWidth;
			}
			bool seaDatum = form.Datum == BiomeLandformDatum.SeaSurface && !float.IsNegativeInfinity(input.SeaLevel);
			float datum = seaDatum ? input.SeaLevel - BiomeLandforms.Pick(form.DatumDepth, U(14u)) : float.NaN;
			Vector2 axis = new Vector2(Mathf.Cos(site.Yaw * Mathf.Deg2Rad), Mathf.Sin(site.Yaw * Mathf.Deg2Rad));
			switch (form.Family)
			{
				case BiomeLandformFamily.Crater:
					// A crater's depth is a share of its diameter; a ring basin's graben are metres deep whatever its size.
					// An ageing crater's share is its class's (decision 72).
					site.Height = form.Form == BiomeLandformForm.RingGraben ? height : (form.Ages ? BiomeLandforms.CraterAgeDepth(BiomeLandforms.CraterAge(seed)) : height) * size;
					site.HalfLength = radius;
					if (form.Form == BiomeLandformForm.RingGraben)
					{
						// The palimpsest settles towards the mean of its own ground (decision 71: pale and near flat).
						site.Level = RingMean(input, at, radius * 0.18f);
					}
					if (form.Form == BiomeLandformForm.Ghost)
					{
						site.Level = RingMean(input, at, radius * 0.95f);
					}
					else if (form.Form == BiomeLandformForm.MultiRing)
					{
						// The mare's level (decision 55): the sunk floor filled back to six tenths of its sink.
						site.Level = RingMean(input, at, radius * 0.3f) - 0.4f * site.Height;
					}
					break;
				case BiomeLandformFamily.Mound:
					if (form.Form == BiomeLandformForm.Platform)
					{
						site.Level = seaDatum ? datum : ground + height;
					}
					if (form.Form == BiomeLandformForm.Star || form.Form == BiomeLandformForm.Cone)
					{
						site.HalfLength = radius;
					}
					break;
				case BiomeLandformFamily.Depression:
					if (form.Form == BiomeLandformForm.Pit && seaDatum)
					{
						site.Level = datum;
					}
					if (form.Form == BiomeLandformForm.Pan || form.Form == BiomeLandformForm.Tank)
					{
						// A pan with a sea datum floors at that level (2026-10-25, I2g-b: an ukanda's at the spring tides' reach).
						site.Level = seaDatum && form.Form == BiomeLandformForm.Pan ? datum : Mathf.Min(ground, RingMean(input, at, radius * 0.9f)) - height;
					}
					break;
				case BiomeLandformFamily.Mesa:
					if (form.Form == BiomeLandformForm.Table && seaDatum)
					{
						// A table standing from the sea at its datum, joined to the nearest shore by a neck MoatDepth under the sea's
						// surface and Width wide (decision 74: a tidal brough and its causeway), when Width is set.
						site.Level = datum;
						if (form.Width.x > 0f || form.Width.y > 0f)
						{
							Vector2 shore = Vector2.zero;
							float best = float.MaxValue, step = Mathf.Max(2f, 0.5f * input.CellMetres);
							for (int k = 0; k < 16; k++)
							{
								var dir = new Vector2(Mathf.Cos(k * Mathf.PI / 8f), Mathf.Sin(k * Mathf.PI / 8f));
								for (float d = radius * 0.8f; d <= radius + 600f && d < best; d += step)
								{
									Vector2 q = at + dir * d;
									if (!Inside(input, q, site.HalfWidth * 1.6f))
									{
										break;
									}
									if (input.Ground(q.x, q.y) >= input.SeaLevel)
									{
										best = d;
										shore = q + dir * site.HalfWidth;
										break;
									}
								}
							}
							if (best < float.MaxValue)
							{
								site.Segments = Line(new List<Vector2> { at, shore }, site.HalfWidth, Mathf.Max(0f, form.MoatDepth), false);
							}
						}
					}
					else if (form.Form == BiomeLandformForm.Table)
					{
						site.Level = Mathf.Max(ground, RingMean(input, at, radius * 0.5f)) + height;
					}
					else if (form.Form == BiomeLandformForm.Rimstone)
					{
						Vector2 head = at - axis * site.HalfLength;
						site.Level = input.Ground(head.x, head.y);
					}
					break;
				case BiomeLandformFamily.Ridge:
				{
					if (form.Form == BiomeLandformForm.Ring)
					{
						site.Level = datum;
						site.HalfLength = radius;
						break;
					}
					List<Vector2> points = RidgeLine(input, form, biomeIndex, site, axis);
					if (points == null || points.Count < 2)
					{
						return false;
					}
					site.Level = datum;
					site.Radius = form.Form == BiomeLandformForm.Scarp ? radius : site.HalfWidth;
					site.Segments = Line(points, site.HalfWidth, height, true);
					if (form.Form == BiomeLandformForm.Graben && form.Count > 1)
					{
						// A set of parallel troughs Moat metres apart, outward from the first (decision 76: an arcuate rille's arcs).
						for (int k = 1; k < form.Count; k++)
						{
							var offset = new List<Vector2>(points.Count);
							for (int i = 0; i < points.Count; i++)
							{
								Vector2 along = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)];
								Vector2 normal = along.sqrMagnitude > 1e-8f ? new Vector2(-along.y, along.x).normalized : Vector2.zero;
								offset.Add(points[i] + normal * (k * Mathf.Max(site.HalfWidth * 2.5f, form.Moat)));
							}
							site.Segments.AddRange(Line(offset, site.HalfWidth, height, true));
						}
					}
					break;
				}
				case BiomeLandformFamily.Channels:
				{
					List<(List<Vector2> Points, int Order)> lines = ChannelLines(input, form, biomeIndex, site, axis);
					if (lines == null || lines.Count == 0)
					{
						return false;
					}
					// A channel floored under the sea's surface (decision 68) cuts to that level.
					site.Level = seaDatum ? datum : float.NaN;
					site.Segments = new List<BiomeLandformSegment>();
					foreach ((List<Vector2> line, int order) in lines)
					{
						// Each line from its mouth (no taper) to its head (tapering); a creek's branches narrower and shallower by their order.
						float half = Mathf.Max(0.5f * Mathf.Min(form.Width.x, form.Width.y), site.HalfWidth * Mathf.Pow(0.55f, order));
						float depth = Mathf.Max(Mathf.Min(form.Height.x, form.Height.y), height * Mathf.Pow(0.65f, order));
						bool taperStart = form.Form == BiomeLandformForm.Spiral || form.Form == BiomeLandformForm.Rille && form.CrownDepth <= 0f;
						site.Segments.AddRange(Line(line, order == 0 ? site.HalfWidth : half, order == 0 ? height : depth, taperStart));
						if (site.Segments.Count > MaximumSegments)
						{
							break;
						}
					}
					if (form.Form == BiomeLandformForm.Rille)
					{
						// A rille narrows to half its width from its head to its toe (decision 76).
						for (int i = 0; i < site.Segments.Count; i++)
						{
							BiomeLandformSegment seg = site.Segments[i];
							seg.Half *= Mathf.Lerp(1f, 0.5f, 0.5f * (seg.AlongA + seg.AlongB) / Mathf.Max(1f, seg.LineLength));
							site.Segments[i] = seg;
						}
					}
					break;
				}
				case BiomeLandformFamily.Lobe:
					if (form.Form == BiomeLandformForm.Fan)
					{
						site.HalfLength = radius;
					}
					break;
			}
			site.Bounds = BiomeLandforms.BoundsOf(site);
			Vector4 bb = site.Bounds;
			Vector4 scene = input.SceneBounds;
			return bb.x >= scene.x && bb.y >= scene.y && bb.z <= scene.z && bb.w <= scene.w;
		}

		private static float RingMean(LandformPlanInput input, Vector2 at, float radius)
		{
			float sum = input.Ground(at.x, at.y);
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI / 4f;
				sum += input.Ground(at.x + Mathf.Cos(a) * radius, at.y + Mathf.Sin(a) * radius);
			}
			return sum / 9f;
		}

		/// <summary>A polyline as stretches with their along-distances (pure).</summary>
		public static List<BiomeLandformSegment> Line(List<Vector2> points, float half, float depth, bool taperStart)
		{
			var segments = new List<BiomeLandformSegment>();
			float total = 0f;
			for (int i = 0; i + 1 < points.Count; i++)
			{
				total += Vector2.Distance(points[i], points[i + 1]);
			}
			float along = 0f;
			for (int i = 0; i + 1 < points.Count; i++)
			{
				var seg = new BiomeLandformSegment(points[i], points[i + 1], half, depth, along, total, taperStart);
				segments.Add(seg);
				along = seg.AlongB;
			}
			return segments;
		}

		private static List<Vector2> RidgeLine(LandformPlanInput input, BiomeLandform form, int biomeIndex, in BiomeLandformSite site, Vector2 axis)
		{
			float length = Mathf.Max(site.HalfLength * 2f, site.HalfWidth * 4f);
			Vector2 at = site.Centre;
			var points = new List<Vector2>();
			switch (form.Form)
			{
				case BiomeLandformForm.Arc:
				{
					float arcRadius = Mathf.Max(site.Radius, length / Mathf.PI);
					Vector2 normal = new Vector2(-axis.y, axis.x);
					Vector2 centre = at + normal * arcRadius;
					float start = Mathf.Atan2(at.y - centre.y, at.x - centre.x);
					float span = length / arcRadius;
					int steps = Mathf.Clamp(Mathf.CeilToInt(length / 15f), 2, 200);
					for (int i = 0; i <= steps; i++)
					{
						float a = start + span * (i / (float)steps - 0.5f);
						points.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * arcRadius);
					}
					return points;
				}
				case BiomeLandformForm.Graben when form.Size.x > 0f || form.Size.y > 0f:
				{
					// An arcuate graben: an arc of the circle its size spans, through its place (decision 76).
					float arcRadius = Mathf.Max(site.Radius, 10f);
					Vector2 normal = new Vector2(-axis.y, axis.x);
					Vector2 centre = at + normal * arcRadius;
					float start = Mathf.Atan2(at.y - centre.y, at.x - centre.x);
					float span = Mathf.Min(length / arcRadius, Mathf.PI);
					int steps = Mathf.Clamp(Mathf.CeilToInt(span * arcRadius / 15f), 2, 200);
					for (int i = 0; i <= steps; i++)
					{
						float a = start + span * (i / (float)steps - 0.5f);
						points.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * arcRadius);
					}
					return points;
				}
				case BiomeLandformForm.Contour:
				case BiomeLandformForm.Scarp:
				{
					float level = !float.IsNegativeInfinity(input.SeaLevel) && (form.DatumDepth.x != 0f || form.DatumDepth.y != 0f)
						? input.SeaLevel - BiomeLandforms.Pick(form.DatumDepth, BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, 0xC0u)))
						: input.Ground(at.x, at.y);
					return Contour(input, biomeIndex, at, level, length, Mathf.Clamp(site.HalfWidth, 4f, 25f));
				}
				default:
				{
					// Straight: centred on its place, or out from the shore down the slope when anchored there.
					int steps = Mathf.Clamp(Mathf.CeilToInt(length / 40f), 1, 200);
					Vector2 from = form.Anchor == BiomeLandformAnchor.Shore ? at : at - axis * (length * 0.5f);
					for (int i = 0; i <= steps; i++)
					{
						points.Add(from + axis * (length * i / steps));
					}
					return points;
				}
			}
		}

		/// <summary>A line traced along the ground's contour at <paramref name="level"/> through a place, both ways (pure but for the ground).</summary>
		private static List<Vector2> Contour(LandformPlanInput input, int biomeIndex, Vector2 at, float level, float length, float step)
		{
			float h = Mathf.Max(2f, step * 0.5f);
			Vector2 Snap(Vector2 p)
			{
				for (int k = 0; k < 4; k++)
				{
					Vector2 g = Gradient(input, p, h);
					float m2 = g.sqrMagnitude;
					if (m2 < 1e-8f)
					{
						break;
					}
					float off = (level - input.Ground(p.x, p.y)) / m2;
					Vector2 move = g * off;
					if (move.magnitude > step)
					{
						move = move.normalized * step;
					}
					p += move;
				}
				return p;
			}
			Vector2 start = Snap(at);
			if (Mathf.Abs(input.Ground(start.x, start.y) - level) > Mathf.Max(1f, step * 0.25f) || !Inside(input, start, 0f))
			{
				return null;
			}
			var forward = new List<Vector2> { start };
			var backward = new List<Vector2>();
			for (int side = 0; side < 2; side++)
			{
				List<Vector2> list = side == 0 ? forward : backward;
				Vector2 p = start;
				Vector2 last = Vector2.zero;
				float run = 0f;
				int outside = 0;
				while (run < length * 0.5f && list.Count < 400)
				{
					Vector2 g = Gradient(input, p, h);
					if (g.sqrMagnitude < 1e-8f)
					{
						break;
					}
					Vector2 dir = new Vector2(-g.y, g.x).normalized * (side == 0 ? 1f : -1f);
					if (last != Vector2.zero && Vector2.Dot(dir, last) < 0f)
					{
						dir = -dir;
					}
					Vector2 next = Snap(p + dir * step);
					if (!Inside(input, next, step * 2f))
					{
						break;
					}
					outside = BiomeAt(input, next) == biomeIndex ? 0 : outside + 1;
					if (outside > 2)
					{
						break;
					}
					run += Vector2.Distance(p, next);
					last = (next - p).normalized;
					p = next;
					list.Add(p);
				}
			}
			backward.Reverse();
			backward.AddRange(forward);
			return backward.Count >= 2 ? backward : null;
		}

		/// <summary>A channel's lines (pure but for the ground), each from its mouth to its head.</summary>
		private static List<(List<Vector2> Points, int Order)> ChannelLines(LandformPlanInput input, BiomeLandform form, int biomeIndex, in BiomeLandformSite site, Vector2 axis)
		{
			var lines = new List<(List<Vector2> Points, int Order)>();
			float length = Mathf.Max(site.HalfLength * 2f, 4f);
			switch (form.Form)
			{
				case BiomeLandformForm.Grooves:
				{
					// Down the slope from its place to the boulder at its foot; stored from the boulder up.
					Vector2 g = Gradient(input, site.Centre, 2f);
					if (g.sqrMagnitude < 1e-8f)
					{
						return null;
					}
					Vector2 down = -g.normalized;
					var line = new List<Vector2> { site.Centre + down * length, site.Centre };
					if (!Inside(input, line[0], 2f))
					{
						return null;
					}
					lines.Add((line, 0));
					return lines;
				}
				case BiomeLandformForm.Geo:
				{
					// From a little out to sea at its shore place straight inland (up the ground) to its head (decision 74).
					Vector2 g = Gradient(input, site.Centre, Mathf.Max(2f, input.CellMetres * 0.5f));
					Vector2 inland = g.sqrMagnitude > 1e-8f ? g.normalized : axis;
					var line = new List<Vector2> { site.Centre - inland * (site.HalfWidth * 2f), site.Centre + inland * length };
					if (!Inside(input, line[0], site.HalfWidth * 3.25f) || !Inside(input, line[1], site.HalfWidth * 3.25f))
					{
						return null;
					}
					lines.Add((line, 0));
					return lines;
				}
				case BiomeLandformForm.Spiral:
				{
					// Troughs spiralling out from the place, one turn and a little over (2026-10-25, decision 64), each its own line.
					int arms = Mathf.Max(1, form.Count);
					float outer = Mathf.Max(site.Radius, site.HalfWidth * 8f);
					float inner = 0.12f * outer;
					float sweep = Mathf.PI * 2f * 1.1f;
					float step = Mathf.Clamp(site.HalfWidth, 10f, 80f);
					// An arm stops where its trough would cross the scene's edge: a channel's box reaches 3.2 half widths past its line
					// (BiomeLandforms.BoundsOf), so a margin of one step let a wide trough's last points push the whole site out of the scene.
					float edge = Mathf.Max(step, site.HalfWidth * 3.25f);
					for (int a = 0; a < arms; a++)
					{
						float phase = (a + 0.3f * BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, (uint)a, 0x591u))) * Mathf.PI * 2f / arms;
						var line = new List<Vector2>();
						int outside = 0;
						for (float phi = 0f; phi <= sweep && line.Count < 800;)
						{
							float r = inner + (outer - inner) * phi / sweep;
							Vector2 p = site.Centre + new Vector2(Mathf.Cos(phi + phase), Mathf.Sin(phi + phase)) * r;
							if (!Inside(input, p, edge))
							{
								break;
							}
							outside = BiomeAt(input, p) == biomeIndex ? 0 : outside + 1;
							if (outside > 3)
							{
								break;
							}
							line.Add(p);
							phi += step / Mathf.Max(r, step);
						}
						if (line.Count >= 2)
						{
							lines.Add((line, 0));
						}
					}
					return lines.Count > 0 ? lines : null;
				}
				case BiomeLandformForm.Rille:
				{
					// From its head down the slope to its toe, the head first (decision 76): the vent hollow stands at its start.
					List<Vector2> line = Downhill(input, biomeIndex, site, length, false);
					if (line == null)
					{
						return null;
					}
					lines.Add((line, 0));
					return lines;
				}
				case BiomeLandformForm.Meander:
				case BiomeLandformForm.StoneRiver:
				case BiomeLandformForm.PitChain:
				{
					List<Vector2> line = Downhill(input, biomeIndex, site, length, form.Form == BiomeLandformForm.StoneRiver);
					if (line == null)
					{
						return null;
					}
					line.Reverse();
					lines.Add((line, 0));
					return lines;
				}
				case BiomeLandformForm.Dendritic:
				{
					var stack = new Stack<(Vector2 At, Vector2 Dir, int Order, float Length)>();
					Vector2 g0 = Gradient(input, site.Centre, 4f);
					Vector2 up0 = g0.sqrMagnitude > 1e-10f ? g0.normalized : axis;
					stack.Push((site.Centre, up0, 0, length));
					int levels = Mathf.Max(1, form.Count);
					int segments = 0;
					while (stack.Count > 0 && segments < MaximumSegments)
					{
						(Vector2 at, Vector2 dir, int order, float len) = stack.Pop();
						float half = Mathf.Max(0.5f, site.HalfWidth * Mathf.Pow(0.55f, order));
						float step = Mathf.Clamp(half * 2f, 4f, 30f);
						var line = new List<Vector2> { at };
						Vector2 p = at;
						float run = 0f;
						uint seed = BiomeLandforms.Hash(site.Seed, (uint)lines.Count, (uint)order);
						int outside = 0;
						while (run < len && line.Count < 400)
						{
							Vector2 g = Gradient(input, p, Mathf.Max(2f, step * 0.5f));
							Vector2 up = g.sqrMagnitude > 1e-10f ? g.normalized : dir;
							Vector2 perp = new Vector2(-dir.y, dir.x);
							float wobble = 0.5f * (BiomeLandforms.Noise(run / (step * 6f), order * 3.1f, seed) - 0.5f);
							Vector2 next = p + (dir * 0.55f + up * 0.45f + perp * wobble).normalized * step;
							if (!Inside(input, next, step))
							{
								break;
							}
							outside = BiomeAt(input, next) == biomeIndex ? 0 : outside + 1;
							if (outside > 1)
							{
								break;
							}
							dir = (next - p).normalized;
							run += step;
							p = next;
							line.Add(p);
						}
						if (line.Count < 2)
						{
							continue;
						}
						lines.Add((line, order));
						segments += line.Count - 1;
						if (order + 1 < levels)
						{
							// Two branches off the stretch, one each side, a little up it.
							for (int k = 0; k < 2; k++)
							{
								float at01 = k == 0 ? 0.4f : 0.72f;
								int idx = Mathf.Clamp(Mathf.RoundToInt(at01 * (line.Count - 1)), 1, line.Count - 1);
								Vector2 baseDir = (line[idx] - line[idx - 1]).normalized;
								float turn = (40f + 20f * BiomeLandforms.Unit(BiomeLandforms.Hash(seed, (uint)k, 0xB2u))) * (k == 0 ? 1f : -1f) * Mathf.Deg2Rad;
								var branch = new Vector2(baseDir.x * Mathf.Cos(turn) - baseDir.y * Mathf.Sin(turn), baseDir.x * Mathf.Sin(turn) + baseDir.y * Mathf.Cos(turn));
								stack.Push((line[idx], branch, order + 1, len * 0.6f));
							}
						}
					}
					return lines.Count > 0 ? lines : null;
				}
				default:
					return null;
			}
		}

		/// <summary>A meandering line down the slope from a place (pure but for the ground); a stone river ends at the sea.</summary>
		private static List<Vector2> Downhill(LandformPlanInput input, int biomeIndex, in BiomeLandformSite site, float length, bool toSea)
		{
			float step = Mathf.Clamp(site.HalfWidth * 1.5f, 2f, 40f);
			float wavelength = Mathf.Max(30f, site.HalfWidth * 12f);
			float phase = BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, 0xE1u)) * Mathf.PI * 2f;
			var line = new List<Vector2> { site.Centre };
			Vector2 p = site.Centre;
			Vector2 dir = Vector2.zero;
			float run = 0f;
			int outside = 0;
			while (run < length && line.Count < 800)
			{
				Vector2 g = Gradient(input, p, Mathf.Max(2f, step * 0.5f));
				Vector2 down = g.sqrMagnitude > 1e-10f ? -g.normalized : dir;
				if (down == Vector2.zero)
				{
					break;
				}
				Vector2 perp = new Vector2(-down.y, down.x);
				Vector2 heading = (down + perp * (0.6f * Mathf.Sin(Mathf.PI * 2f * run / wavelength + phase))).normalized;
				if (dir != Vector2.zero)
				{
					heading = (heading + dir * 0.5f).normalized;
				}
				Vector2 next = p + heading * step;
				if (!Inside(input, next, site.HalfWidth * 3.5f))
				{
					break;
				}
				outside = BiomeAt(input, next) == biomeIndex ? 0 : outside + 1;
				if (outside > 3 && !toSea)
				{
					break;
				}
				dir = heading;
				run += step;
				p = next;
				line.Add(p);
				if (toSea && !float.IsNegativeInfinity(input.SeaLevel) && input.Ground(p.x, p.y) <= input.SeaLevel)
				{
					break;
				}
			}
			return line.Count >= 2 && run >= Mathf.Min(length, site.HalfWidth * 6f) ? line : null;
		}

		// ── The record kept with the scene ────────────────────────────

		/// <summary>Stores a cut's landforms in its scene (an EditorOnly child, <see cref="GeneratedLandforms"/>); nothing when it placed none.</summary>
		public static void Store(Scene scene, SceneLandformRecord record)
		{
			if (!scene.IsValid())
			{
				return;
			}
			foreach (GeneratedLandforms old in Records(scene))
			{
				UnityEngine.Object.DestroyImmediate(old.gameObject);
			}
			if (record == null || record.Sites.Count == 0)
			{
				return;
			}
			var host = new GameObject(GeneratedLandforms.ObjectName) { tag = "EditorOnly" };
			SceneManager.MoveGameObjectToScene(host, scene);
			var stored = host.AddComponent<GeneratedLandforms>();
			stored.Sites.AddRange(record.Sites);
			stored.Pads.AddRange(record.Pads);
			stored.SeaLevel = record.SeaLevel;
			EditorUtility.SetDirty(stored);
		}

		private static List<GeneratedLandforms> Records(Scene scene)
		{
			var records = new List<GeneratedLandforms>();
			if (scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					records.AddRange(root.GetComponentsInChildren<GeneratedLandforms>(true));
				}
			}
			return records;
		}

		/// <summary>The landforms a scene was cut with (empty for a scene cut with none, or before 2026-10-18).</summary>
		public static List<BiomeLandformSite> Sites(Scene scene)
		{
			var sites = new List<BiomeLandformSite>();
			foreach (GeneratedLandforms record in Records(scene))
			{
				sites.AddRange(record.Sites);
			}
			return sites;
		}

		/// <summary>The sea's surface a scene's landforms were measured from (negative infinity for none or no record).</summary>
		public static float SeaLevel(Scene scene)
		{
			foreach (GeneratedLandforms record in Records(scene))
			{
				return record.SeaLevel;
			}
			return float.NegativeInfinity;
		}

		/// <summary>
		/// The pads a scene's landforms offer points of interest (a terp's top, a motte's summit, a mesa's table, a pool): what the site
		/// planner of phase I7 stands the terp village, the motte keep and the hallig house on. Empty for a scene with none.
		/// </summary>
		public static List<BiomeLandformPad> Pads(Scene scene)
		{
			var pads = new List<BiomeLandformPad>();
			foreach (GeneratedLandforms record in Records(scene))
			{
				pads.AddRange(record.Pads);
			}
			return pads;
		}
	}
}
#endif
