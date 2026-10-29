#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The later drafts' linear places and forms (2026-10-26, phase I2f-b): a camp's windbreak on its windward arc, a frontier's barrier
	/// across the scene with its ditch and forts, marker lines set post by post along the ways.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Windbreaks.</b> Round a camp or a small settlement (<see cref="WindbreakKinds"/>) on the arc of its bounds facing into the
	/// scene's prevailing wind (SceneGround.AeolianFor; on an airless world, or with none given, a bearing drawn by the site's own
	/// hash), <see cref="WindbreakHalfArc"/> either side of it: a snow-block wall, a bough break, any wall, fence or hedge.
	/// </para>
	/// <para>
	/// <b>Frontiers.</b> One long line across the scene through the biome's ground (a drawn bearing through the middle of that ground),
	/// cut where it leaves the biome; its ways break through it as through any wall, a brick barrier is broken through every 40–200 m
	/// (<see cref="Breaches"/>) besides, its outer side ditched (<see cref="DitchGround"/>, carved and restored as the channels are), a
	/// square fort inside it every 300–600 m where one fits. The outer side is the one with fewer settlements.
	/// </para>
	/// <para>
	/// <b>Markers.</b> A marker line's posts, wands or cairns one at each station of its spacing along a way's verge, never joined: a
	/// wand line's ends its end cairns, every fifth serge post its lamp hook, every sixth tamga pole its torch bracket
	/// (<see cref="BiomeLinearFeatures.MarkerVariant"/>), ahu each its own height.
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestLinearFeatures
	{
		/// <summary>The kinds of site a <see cref="BiomeLinearPlace.Windbreaks"/> style shelters: camps and small settlements.</summary>
		public static readonly POIType[] WindbreakKinds =
		{
			POIType.Camp, POIType.Village, POIType.HuntingLodge, POIType.Hermitage, POIType.Waystation, POIType.AbandonedFarm,
		};

		/// <summary>How far either side of straight upwind a windbreak's arc reaches, degrees.</summary>
		public const float WindbreakHalfArc = 65f;
		/// <summary>How far past a site's outline its windbreak stands, metres.</summary>
		public const float WindbreakMargin = 3f;
		/// <summary>A frontier's line: how far apart its stations stand, metres, and its shortest run.</summary>
		public const float FrontierStationMetres = 8f, FrontierMinRunMetres = 60f;
		/// <summary>A frontier's ditch: how far outside its line its middle runs, its half width and its depth, metres.</summary>
		public const float DitchOutMetres = 7f, DitchHalfMetres = 3f, DitchDepthMetres = 1.4f;
		/// <summary>A frontier's forts: the least and most distance between them along its line, metres.</summary>
		public const float FortMinMetres = 300f, FortMaxMetres = 600f;

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>
		/// A site's windward arc (pure): its outline grown by <paramref name="margin"/>, from <paramref name="halfArc"/>° one side of the
		/// bearing <paramref name="upwind"/> (degrees, clockwise from +z) to as far the other, sampled every <paramref name="stepDegrees"/>,
		/// an open line.
		/// </summary>
		public static List<Vector2> WindwardArc(Vector2 centre, float[] outline, float radius, float margin, float upwind, float halfArc, float stepDegrees = 6f)
		{
			var arc = new List<Vector2>();
			int n = Mathf.Max(2, Mathf.CeilToInt(2f * halfArc / Mathf.Max(1f, stepDegrees)));
			for (int k = 0; k <= n; k++)
			{
				float bearing = Mathf.Repeat(upwind - halfArc + 2f * halfArc * k / n, 360f);
				float reach = PointOfInterestOutline.ReachToward(outline, radius, bearing) + margin;
				float b = bearing * Mathf.Deg2Rad;
				arc.Add(centre + new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * reach);
			}
			return arc;
		}

		/// <summary>The bearing a wind blowing toward <paramref name="windTo"/> comes from, degrees clockwise from +z (pure); NaN for none.</summary>
		public static float UpwindBearing(Vector2 windTo)
		{
			if (windTo.sqrMagnitude < 1e-8f)
			{
				return float.NaN;
			}
			return Mathf.Repeat(Mathf.Atan2(-windTo.x, -windTo.y) * Mathf.Rad2Deg, 360f);
		}

		/// <summary>
		/// Where a barrier of <paramref name="modules"/> modules <paramref name="module"/> metres long is broken through (pure): a gap of
		/// one to three modules after every 40–200 m of wall, each drawn by its own hash of <paramref name="seed"/>.
		/// </summary>
		public static bool[] Breaches(int modules, float module, int seed)
		{
			var open = new bool[Mathf.Max(0, modules)];
			if (!(module > 0f))
			{
				return open;
			}
			int k = 0, gap = 0;
			while (k < open.Length)
			{
				float run = 40f + 160f * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, gap, 0x425243, 1));
				k += Mathf.Max(1, Mathf.RoundToInt(run / module));
				int width = 1 + (int)(BiomeLinearFeatures.Hash(seed, gap, 0x425243, 2) % 3u);
				for (int w = 0; w < width && k < open.Length; w++, k++)
				{
					open[k] = true;
				}
				gap++;
			}
			return open;
		}

		/// <summary>
		/// The ground a ditch leaves (pure): <paramref name="depth"/> under the ground at its middle (<paramref name="d"/> = 0), easing
		/// up in a cosine to the ground at <paramref name="half"/>; the ground as it was beyond. Lowers only.
		/// </summary>
		public static float DitchGround(float d, float ground, float depth, float half)
		{
			if (!(half > 0f) || d >= half)
			{
				return ground;
			}
			return ground - depth * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(d / half)));
		}

		/// <summary>
		/// The along-line distances a frontier's forts stand at on a run <paramref name="length"/> metres long (pure): the first 150–300 m
		/// in, each next 300–600 m on, none within 40 m of an end, every gap drawn by its own hash of <paramref name="seed"/>.
		/// </summary>
		public static List<float> FortStations(float length, int seed)
		{
			var at = new List<float>();
			float s = 0.5f * FortMinMetres + 0.5f * (FortMaxMetres - FortMinMetres) * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 0, 0x464F52, 0));
			for (int k = 1; s < length - 40f; k++)
			{
				at.Add(s);
				s += FortMinMetres + (FortMaxMetres - FortMinMetres) * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, k, 0x464F52, 0));
			}
			return at;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>The scene's prevailing wind on the plan, or zero (no air; or the scene's body could not be read, noted).</summary>
		private static Vector2 SceneWind(SceneScope scope)
		{
			try
			{
				AeolianSettings wind = SceneGround.AeolianFor(scope.Request, SolarSystemProfile.Resolve(scope.Request.Body));
				return wind != null ? new Vector2(wind.WindX, wind.WindZ) : Vector2.zero;
			}
			catch (Exception e)
			{
				scope.Notes?.Add($"Linear features: the scene's wind could not be read ({e.Message}): windbreaks face a drawn bearing.");
				return Vector2.zero;
			}
		}

		/// <summary>The windbreaks on the windward arc of each camp and small settlement whose biome names one.</summary>
		private static int LayWindbreaks(SceneScope scope)
		{
			int laid = 0;
			bool windRead = false;
			foreach (PointOfInterestRecord record in scope.Points.Points)
			{
				if (Array.IndexOf(WindbreakKinds, record.Kind) < 0 || !BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate biome))
				{
					continue;
				}
				foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.Windbreaks))
				{
					int seed = unchecked((int)BiomeLinearFeatures.Hash(record.SiteSeed, 0x57424B, index, 0));
					if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
					{
						continue;
					}
					if (!windRead)
					{
						scope.WindTo = SceneWind(scope);
						windRead = true;
					}
					float upwind = UpwindBearing(scope.WindTo);
					if (float.IsNaN(upwind))
					{
						upwind = BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 2, 0, 0)) * 360f;
					}
					var centre = new Vector2(record.Position.x, record.Position.z);
					List<Vector2> arc = WindwardArc(centre, record.Outline, record.Radius, WindbreakMargin, upwind, WindbreakHalfArc);
					var layer = new Layer(scope.Context(record), biome, style, seed) { KeepOutsFree = true };
					layer.Lay(arc, false);
					layer.Report();
					laid += layer.Laid;
					break;
				}
			}
			return laid;
		}

		/// <summary>One frontier a biome (and one brick barrier a scene): its first admitted frontier style, laid across its ground.</summary>
		private static int LayFrontiers(SceneScope scope, List<BiomeTemplate> biomes)
		{
			int laid = 0;
			Rect area = SceneArea(scope.Terrains);
			if (area.width <= 0f)
			{
				return 0;
			}
			foreach (BiomeTemplate biome in biomes)
			{
				foreach ((BiomeLinearStyle style, int index) in StylesAt(biome, BiomeLinearPlace.Frontier))
				{
					int seed = unchecked((int)BiomeLinearFeatures.Hash(scope.Seed, BiomeRegistry.IDOf(biome), 0x46524E + index, 0));
					if (!BiomeLinearFeatures.Admits(style.Density, BiomeLinearFeatures.Hash(seed, 1, 0, 0)))
					{
						continue;
					}
					if (!FrontierLine(scope, biome, area, seed, out List<Vector2> line))
					{
						scope.Notes?.Add($"Linear features: {Label(biome)} lays a frontier but has too little ground in the scene for one.");
						break;
					}
					if (!scope.TakeCap(style.Form))
					{
						scope.Notes?.Add($"Linear features: {Label(biome)} {BiomeLinearFeatures.Id(style)}: the scene already has its one.");
						break;
					}
					bool Ours(Vector2 p) => scope.BiomeAt(p.x, p.y) == biome && p.x > area.xMin + 4f && p.x < area.xMax - 4f && p.y > area.yMin + 4f && p.y < area.yMax - 4f;
					List<List<Vector2>> runs = Runs(line, Ours, FrontierMinRunMetres);
					// The outer side: the one fewer settlements stand on.
					float sum = 0f;
					Vector2 axis = line[line.Count - 1] - line[0];
					foreach (PointOfInterestRecord r in scope.Points.Points)
					{
						if (Array.IndexOf(SettlementKinds, r.Kind) >= 0)
						{
							Vector2 rel = new Vector2(r.Position.x, r.Position.z) - line[0];
							sum += Mathf.Sign(axis.x * rel.y - axis.y * rel.x);
						}
					}
					// Left of the line positive: settlements on the left keep the left inside, so the ditch goes right.
					float inside = sum >= 0f ? 1f : -1f;
					int before = laid;
					for (int r = 0; r < runs.Count; r++)
					{
						PointOfInterestSiteContext context = scope.Context();
						if (style.Kind == BiomeLinearKind.Wall || style.Kind == BiomeLinearKind.ClearanceRow)
						{
							Ditch(context, runs[r], -inside);
						}
						var layer = new Layer(context, biome, style, unchecked(seed + r * 0x9E37));
						layer.Lay(runs[r], false);
						if (style.Form == BiomeLinearForm.BrickBarrier)
						{
							layer.Forts(runs[r], inside, unchecked(seed ^ r * 0x5bd1));
						}
						layer.Report();
						laid += layer.Laid;
					}
					scope.Notes?.Add($"Linear features: {Label(biome)}'s frontier ({BiomeLinearFeatures.Id(style)}) of {runs.Count} run(s), {laid - before} piece(s).");
					break;
				}
			}
			return laid;
		}

		/// <summary>
		/// A frontier's line through a biome's ground: a drawn bearing through the middle of the ground it holds in the scene (sampled on
		/// a 32 × 32 lattice), drawn a little off it, its stations every <see cref="FrontierStationMetres"/> from edge to edge. False when
		/// the biome holds under a twentieth of the samples.
		/// </summary>
		private static bool FrontierLine(SceneScope scope, BiomeTemplate biome, Rect area, int seed, out List<Vector2> line)
		{
			line = null;
			const int n = 32;
			Vector2 sum = Vector2.zero;
			int hits = 0;
			for (int j = 0; j < n; j++)
			{
				for (int i = 0; i < n; i++)
				{
					var p = new Vector2(area.xMin + area.width * (i + 0.5f) / n, area.yMin + area.height * (j + 0.5f) / n);
					if (scope.BiomeAt(p.x, p.y) == biome)
					{
						sum += p;
						hits++;
					}
				}
			}
			if (hits < n * n / 20)
			{
				return false;
			}
			Vector2 middle = sum / hits;
			float bearing = BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 2, 0, 0)) * 180f * Mathf.Deg2Rad;
			var dir = new Vector2(Mathf.Sin(bearing), Mathf.Cos(bearing));
			Vector2 across = Left(dir) * ((BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(seed, 3, 0, 0)) - 0.5f) * 0.2f * Mathf.Min(area.width, area.height));
			float reach = new Vector2(area.width, area.height).magnitude;
			Vector2 a = middle + across - dir * reach, b = middle + across + dir * reach;
			line = new List<Vector2>();
			foreach (Station st in Stations(new[] { a, b }, FrontierStationMetres))
			{
				if (area.Contains(st.At))
				{
					line.Add(st.At);
				}
			}
			return line.Count >= 2;
		}

		/// <summary>A frontier's ditch along its outer side (<paramref name="side"/>: +1 left of the line), carved in stretches and recorded for restore.</summary>
		private static int Ditch(PointOfInterestSiteContext context, List<Vector2> run, float side)
		{
			List<Vector2> middle = Offset(run, side * DitchOutMetres);
			int carved = 0;
			const int chunk = 24;
			for (int start = 0; start + 1 < middle.Count; start += chunk)
			{
				int end = Mathf.Min(middle.Count - 1, start + chunk);
				var segments = new List<(Vector2 A, Vector2 B)>();
				float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
				for (int i = start; i < end; i++)
				{
					Vector2 a = middle[i], b = middle[i + 1];
					segments.Add((a, b));
					x0 = Mathf.Min(x0, Mathf.Min(a.x, b.x));
					x1 = Mathf.Max(x1, Mathf.Max(a.x, b.x));
					z0 = Mathf.Min(z0, Mathf.Min(a.y, b.y));
					z1 = Mathf.Max(z1, Mathf.Max(a.y, b.y));
				}
				Rect area = Rect.MinMaxRect(x0 - DitchHalfMetres - 1f, z0 - DitchHalfMetres - 1f, x1 + DitchHalfMetres + 1f, z1 + DitchHalfMetres + 1f);
				carved += PointOfInterestFieldCarve.CarveTo(context, area, (x, z, g) =>
				{
					var p = new Vector2(x, z);
					if (context.Water != null && context.Water.KindAt(x, z) != WaterKind.None)
					{
						return 0f;
					}
					float best = float.MaxValue;
					foreach ((Vector2 a, Vector2 b) in segments)
					{
						best = Mathf.Min(best, HedgeDistance(p, a, b, out _));
					}
					return DitchGround(best, g, DitchDepthMetres, DitchHalfMetres) - g;
				});
			}
			return carved;
		}

		// ── Laying one line: markers and forts ────────────────────────

		private sealed partial class Layer
		{
			/// <summary>A marker line's markers along a line, one at each station of the style's spacing where nothing stops it.</summary>
			private void Markers(IReadOnlyList<Vector2> line)
			{
				string id = StructurePieces.MarkerPieceFor(style.Form);
				if (id == null)
				{
					return;
				}
				List<Station> stations = Stations(line, Mathf.Max(4f, BiomeLinearFeatures.SpacingOf(style)));
				context.Palette = StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, id);
				float yScale = BiomeLinearFeatures.HeightOf(style) / Mathf.Max(0.1f, BiomeLinearFeatures.DefaultHeight(style.Form));
				for (int k = 0; k < stations.Count; k++)
				{
					uint h = BiomeLinearFeatures.Hash(seed, k, lines, 0x4D4B52);
					Vector2 at = stations[k].At + Left(stations[Mathf.Min(stations.Count - 1, k + 1)].At - stations[Mathf.Max(0, k - 1)].At)
						* ((BiomeLinearFeatures.Unit(h) - 0.5f) * 0.6f);
					if (Blocked(at) || style.Breaks > 0f && BiomeLinearFeatures.Unit(BiomeLinearFeatures.Mix(h ^ 0x42u)) < style.Breaks)
					{
						continue;
					}
					int variant = BiomeLinearFeatures.MarkerVariant(style.Form, k, stations.Count);
					GameObject prefab = StructureKit.Prefab(id, variant);
					if (prefab == null)
					{
						Missing(id);
						return;
					}
					Vector2 d = stations[Mathf.Min(stations.Count - 1, k + 1)].At - stations[Mathf.Max(0, k - 1)].At;
					float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + (BiomeLinearFeatures.Unit(BiomeLinearFeatures.Mix(h ^ 0x59u)) - 0.5f) * 30f;
					// Ahu each its own height (0.8–1.5 m), the others a hand either way.
					float own = style.Form == BiomeLinearForm.Ahu ? Mathf.Lerp(0.75f, 1.35f, BiomeLinearFeatures.Unit(BiomeLinearFeatures.Mix(h ^ 0x48u)))
						: 0.94f + 0.12f * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Mix(h ^ 0x48u));
					float s = yScale * own;
					context.AddProp(prefab, new Vector3(at.x, Ground(at) - 0.05f, at.y), Quaternion.Euler(0f, yaw, 0f), new Vector3(style.Form == BiomeLinearForm.Ahu ? s : 1f, s, style.Form == BiomeLinearForm.Ahu ? s : 1f));
					Laid++;
				}
			}

			/// <summary>
			/// A brick barrier's forts along a run: one at each of its <see cref="FortStations"/>, its centre <see cref="StructurePieces.FortInset"/>
			/// inside the line (<paramref name="inside"/>: +1 left), its gate facing in, where its whole ground is free, dry and gentle; seated on
			/// the lowest of it.
			/// </summary>
			public void Forts(IReadOnlyList<Vector2> run, float inside, int fortSeed)
			{
				GameObject[] prefabs = { StructureKit.Prefab("LinearBrickFort", 0), StructureKit.Prefab("LinearBrickFort", 1) };
				if (prefabs[0] == null || prefabs[1] == null)
				{
					Missing("LinearBrickFort");
					return;
				}
				float[] along = Along(run);
				BiomeStructurePalette palette = StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, "LinearBrickFort");
				const float half = StructurePieces.FortSide * 0.5f;
				int f = 0;
				foreach (float s in FortStations(along[along.Length - 1], fortSeed))
				{
					Vector2 p = PointAt(run, along, s), q = PointAt(run, along, Mathf.Min(along[along.Length - 1], s + 2f)), o = PointAt(run, along, Mathf.Max(0f, s - 2f));
					Vector2 dir = (q - o).sqrMagnitude > 1e-6f ? (q - o).normalized : Vector2.up;
					Vector2 inward = Left(dir) * inside;
					Vector2 centre = p + inward * StructurePieces.FortInset;
					float lowest = float.MaxValue, highest = float.MinValue;
					bool fits = true;
					for (int j = -2; j <= 2 && fits; j++)
					{
						for (int i = -2; i <= 2 && fits; i++)
						{
							Vector2 at = centre + dir * (half * i / 2f) + inward * (half * j / 2f);
							if (Blocked(at))
							{
								fits = false;
								break;
							}
							float g = Ground(at);
							lowest = Mathf.Min(lowest, g);
							highest = Mathf.Max(highest, g);
						}
					}
					if (!fits || highest - lowest > 2.5f)
					{
						continue;
					}
					// Its gate (−z) toward the barrier's inside, away from the line: its +z points back at the wall.
					Vector2 face = -inward;
					int v = BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(fortSeed, f++, 0x46, 0)) < 0.5f ? 0 : 1;
					BiomeStructurePalette was = context.Palette;
					context.Palette = palette;
					context.AddProp(prefabs[v], new Vector3(centre.x, lowest - 0.1f, centre.y), Quaternion.LookRotation(new Vector3(face.x, 0f, face.y), Vector3.up), Vector3.one);
					context.Palette = was;
					Laid++;
				}
			}
		}
	}
}
#endif
