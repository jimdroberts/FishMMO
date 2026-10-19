#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Boardwalks along a scene's ways (2026-10-18, phase I2i, decision 27): which stretches of a way are raised on a plank deck
	/// (<see cref="ScenePathPointFlags.Boardwalk"/>), the deck's line over the ground, and the modules and pile bents laid along
	/// it (StructurePieces.BoardwalkSpan / BoardwalkBent) as baked props, collided with and walked on the NavMesh like any deck.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Which stretches.</b> Decided when the ways are planned, before they are carved: a point of a routed way is raised where
	/// its biome raises that class of way (<see cref="BiomeTemplate.Boardwalks"/>, <see cref="BiomeBoardwalkRules.Raises"/>), the
	/// ground is level enough (<see cref="BiomeBoardwalkRules.MaxSlopeDegrees"/>) and dry (rivers keep their bridges and fords),
	/// and it is not on a bridge, a ford or a site's own ground. Runs of such points shorter than <see cref="MinRunMetres"/> stay on
	/// the ground; gaps of up to <see cref="MergeGapMetres"/> between two runs (a hump, a steep step) are bridged by the deck. A
	/// settlement's streets are raised whole where its biome raises Streets (PropsFeature.AddStreets). The flag keeps the path off
	/// the ground under the deck (ScenePathField draws nothing, PathCarver carves nothing, the planner's walkability ignores it), as a
	/// bridge's does.
	/// </para>
	/// <para>
	/// <b>The deck.</b> Stations every <see cref="StructurePieces.BoardwalkModule"/> or a little less along the run; at each the
	/// deck stands <see cref="DeckRiseMetres"/> over the highest ground within half a module and the way's half width, eased to a
	/// grade of no more than <see cref="DeckGrade"/> (never under that line), and over the last <see cref="RampMetres"/> at either
	/// end it comes down to the ground (a ramp of about 11°). A module spans each pair of stations; a bent stands under every
	/// station the deck clears by more than <see cref="BentClearMetres"/>, its piles scaled to the ground. Pure and deterministic.
	/// </para>
	/// </remarks>
	public static class PathBoardwalks
	{
		/// <summary>The shortest run of a way raised, metres.</summary>
		public const float MinRunMetres = 10f;
		/// <summary>The longest gap between two raised runs the deck carries on over, metres.</summary>
		public const float MergeGapMetres = 6f;
		/// <summary>How high the deck stands over the highest ground under it, metres.</summary>
		public const float DeckRiseMetres = 0.9f;
		/// <summary>The steepest the deck runs along its length (rise per metre).</summary>
		public const float DeckGrade = 0.07f;
		/// <summary>How far each end ramps down to the ground, metres.</summary>
		public const float RampMetres = 4.5f;
		/// <summary>The least a deck stands over the ground, metres (at a ramp's foot).</summary>
		public const float MinClearMetres = 0.05f;
		/// <summary>Under this much clearance a station has no bent (the ramp's foot rests on the ground), metres.</summary>
		public const float BentClearMetres = 0.3f;
		/// <summary>The narrowest and widest deck, metres.</summary>
		public const float MinWidthMetres = 1.4f, MaxWidthMetres = 3.2f;

		/// <summary>The flags a point that is never raised carries: a bridge's deck, a ford, a site's own ground.</summary>
		private const ScenePathPointFlags Blocking = ScenePathPointFlags.Bridge | ScenePathPointFlags.Ford | ScenePathPointFlags.InSite;

		// ── Which stretches ───────────────────────────────────────────

		/// <summary>Arc length along a path's points, metres (pure).</summary>
		public static float[] Along(ScenePath path)
		{
			int n = path.Count;
			var s = new float[n];
			for (int i = 1; i < n; i++)
			{
				Vector3 a = path.Points[i - 1], b = path.Points[i];
				s[i] = s[i - 1] + new Vector2(b.x - a.x, b.z - a.z).magnitude;
			}
			return s;
		}

		/// <summary>
		/// The runs of a path raised on a deck (pure): first and last point of each, in order. <paramref name="candidate"/> says
		/// whether a point may be raised (its biome, ground and water); a point on a bridge, a ford or a site's ground never is, and
		/// no gap holding one is closed. Gaps up to <paramref name="mergeGap"/> metres between runs are closed, then runs shorter
		/// than <paramref name="minRun"/> metres dropped.
		/// </summary>
		public static List<(int From, int To)> Runs(ScenePath path, Func<int, bool> candidate, float minRun = MinRunMetres, float mergeGap = MergeGapMetres)
		{
			var runs = new List<(int From, int To)>();
			int n = path != null ? path.Count : 0;
			if (n < 2 || candidate == null)
			{
				return runs;
			}
			float[] s = Along(path);
			bool Blocked(int i) => (path.FlagsAt(i) & Blocking) != 0;
			int start = -1;
			for (int i = 0; i <= n; i++)
			{
				bool on = i < n && !Blocked(i) && candidate(i);
				if (on && start < 0)
				{
					start = i;
				}
				else if (!on && start >= 0)
				{
					runs.Add((start, i - 1));
					start = -1;
				}
			}
			// Close the short gaps that hold nothing that must stay on the ground.
			var merged = new List<(int From, int To)>();
			foreach ((int from, int to) run in runs)
			{
				if (merged.Count > 0)
				{
					(int from, int to) last = merged[merged.Count - 1];
					bool clear = s[run.from] - s[last.to] <= mergeGap;
					for (int k = last.to + 1; clear && k < run.from; k++)
					{
						clear = !Blocked(k);
					}
					if (clear)
					{
						merged[merged.Count - 1] = (last.from, run.to);
						continue;
					}
				}
				merged.Add(run);
			}
			merged.RemoveAll(r => s[r.To] - s[r.From] < minRun);
			return merged;
		}

		/// <summary>Flags every point of each run as on a boardwalk (pure); returns the points flagged.</summary>
		public static int Mark(ScenePath path, IReadOnlyList<(int From, int To)> runs)
		{
			int marked = 0;
			if (path == null || runs == null)
			{
				return 0;
			}
			if (path.Flags == null || path.Flags.Length != path.Count)
			{
				var flags = new byte[path.Count];
				if (path.Flags != null)
				{
					Array.Copy(path.Flags, flags, Mathf.Min(path.Flags.Length, flags.Length));
				}
				path.Flags = flags;
			}
			foreach ((int from, int to) in runs)
			{
				for (int i = Mathf.Max(0, from); i <= to && i < path.Count; i++)
				{
					path.Flags[i] |= (byte)ScenePathPointFlags.Boardwalk;
					marked++;
				}
			}
			return marked;
		}

		/// <summary>The runs of consecutive points a path already carries the boardwalk flag on (pure), first and last of each; single points are not runs.</summary>
		public static List<(int From, int To)> FlaggedRuns(ScenePath path)
		{
			var runs = new List<(int From, int To)>();
			int n = path != null ? path.Count : 0;
			int start = -1;
			for (int i = 0; i <= n; i++)
			{
				bool on = i < n && (path.FlagsAt(i) & ScenePathPointFlags.Boardwalk) != 0;
				if (on && start < 0)
				{
					start = i;
				}
				else if (!on && start >= 0)
				{
					if (i - 1 > start)
					{
						runs.Add((start, i - 1));
					}
					start = -1;
				}
			}
			return runs;
		}

		/// <summary>
		/// Marks a scene's routed ways where their biomes raise them (pure but for the delegates): every path that is not a
		/// settlement's street or a yard. <paramref name="raisesAt"/> gives the biome's setting at a point (null: no biome raises
		/// anything, nothing marked); <paramref name="slopeAt"/> the ground's steepness, degrees; <paramref name="dryAt"/> whether a
		/// point is dry land. Returns the points marked.
		/// </summary>
		public static int MarkWays(IReadOnlyList<ScenePath> paths, Func<float, float, BiomeBoardwalks> raisesAt, Func<float, float, float> slopeAt,
			Func<float, float, bool> dryAt)
		{
			if (paths == null || raisesAt == null)
			{
				return 0;
			}
			int marked = 0;
			foreach (ScenePath path in paths)
			{
				if (path == null || path.Count < 2 || path.Class == ScenePathClass.Street || path.Class == ScenePathClass.Yard)
				{
					continue;
				}
				List<(int From, int To)> runs = Runs(path, i =>
				{
					Vector3 p = path.Points[i];
					return BiomeBoardwalkRules.Raises(raisesAt(p.x, p.z), path.Class)
						&& (slopeAt == null || slopeAt(p.x, p.z) <= BiomeBoardwalkRules.MaxSlopeDegrees)
						&& (dryAt == null || dryAt(p.x, p.z));
				});
				marked += Mark(path, runs);
			}
			return marked;
		}

		// ── The deck ──────────────────────────────────────────────────

		/// <summary>One station of a deck: where along the way, its position on the plan, the ground there, and the deck's height.</summary>
		public struct Station
		{
			public float Along;
			public Vector2 At;
			public float Ground;
			public float Deck;
			/// <summary>The way's half width there, metres.</summary>
			public float Half;
		}

		/// <summary>
		/// The stations of a run (pure): evenly spaced from its first point to its last, as many modules as cover it at no more
		/// than <paramref name="module"/> metres each, on the path's polyline, with the ground, the deck height and the half width
		/// at each.
		/// </summary>
		public static List<Station> Stations(ScenePath path, int from, int to, Func<float, float, float> ground, float module = StructurePieces.BoardwalkModule,
			bool rampStart = true, bool rampEnd = true)
		{
			var stations = new List<Station>();
			if (path == null || ground == null || to <= from || from < 0 || to >= path.Count)
			{
				return stations;
			}
			float[] s = Along(path);
			float length = s[to] - s[from];
			if (!(length > 0.01f))
			{
				return stations;
			}
			int modules = Mathf.Max(1, Mathf.CeilToInt(length / module - 1e-3f));
			int seg = from;
			for (int k = 0; k <= modules; k++)
			{
				float at = s[from] + length * k / modules;
				while (seg < to - 1 && s[seg + 1] < at)
				{
					seg++;
				}
				float span = s[seg + 1] - s[seg];
				float t = span > 1e-5f ? Mathf.Clamp01((at - s[seg]) / span) : 0f;
				Vector3 a = path.Points[seg], b = path.Points[seg + 1];
				var p = new Vector2(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.z, b.z, t));
				float half = Mathf.Lerp(path.HalfWidth != null && seg < path.HalfWidth.Length ? path.HalfWidth[seg] : 1f,
					path.HalfWidth != null && seg + 1 < path.HalfWidth.Length ? path.HalfWidth[seg + 1] : 1f, t);
				stations.Add(new Station { Along = at, At = p, Ground = ground(p.x, p.y), Half = half });
			}
			// The highest ground near each station: half a module along either way, the deck's half width across.
			var highest = new float[stations.Count];
			for (int k = 0; k < stations.Count; k++)
			{
				Vector2 dir = Direction(stations, k);
				var side = new Vector2(-dir.y, dir.x);
				float w = DeckHalfWidth(stations[k].Half);
				float h = stations[k].Ground;
				for (int a = -1; a <= 1; a++)
				{
					for (int c = -1; c <= 1; c++)
					{
						Vector2 q = stations[k].At + dir * (a * 0.5f * module * 0.98f) + side * (c * w);
						h = Mathf.Max(h, ground(q.x, q.y));
					}
				}
				highest[k] = h;
			}
			float[] deck = DeckLine(Distances(stations), highest, Grounds(stations), rampStart, rampEnd);
			for (int k = 0; k < stations.Count; k++)
			{
				Station st = stations[k];
				st.Deck = deck[k];
				stations[k] = st;
			}
			return stations;
		}

		private static float[] Distances(List<Station> stations)
		{
			var d = new float[stations.Count];
			for (int k = 0; k < d.Length; k++)
			{
				d[k] = stations[k].Along;
			}
			return d;
		}

		private static float[] Grounds(List<Station> stations)
		{
			var g = new float[stations.Count];
			for (int k = 0; k < g.Length; k++)
			{
				g[k] = stations[k].Ground;
			}
			return g;
		}

		/// <summary>The way's direction on the plan at a station (unit; along +z for a single station).</summary>
		private static Vector2 Direction(List<Station> stations, int k)
		{
			Vector2 a = stations[Mathf.Max(0, k - 1)].At, b = stations[Mathf.Min(stations.Count - 1, k + 1)].At;
			Vector2 d = b - a;
			return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
		}

		/// <summary>A deck's half width for a way's half width: the way's, held between <see cref="MinWidthMetres"/> and <see cref="MaxWidthMetres"/>.</summary>
		public static float DeckHalfWidth(float half) => 0.5f * Mathf.Clamp(2f * half, MinWidthMetres, MaxWidthMetres);

		/// <summary>
		/// The deck's height at each station (pure): <see cref="DeckRiseMetres"/> over <paramref name="highest"/> (the highest ground
		/// near it), raised where a neighbour's would otherwise climb to it steeper than <see cref="DeckGrade"/> (the line is never
		/// under any station's own), then brought down over the last <see cref="RampMetres"/> at each end to
		/// <see cref="MinClearMetres"/> over <paramref name="ground"/> at the end station, and never under that clearance anywhere. An
		/// end that meets another deck (a street at a junction: <paramref name="rampStart"/>, <paramref name="rampEnd"/> false) keeps
		/// its height to the end.
		/// </summary>
		public static float[] DeckLine(float[] along, float[] highest, float[] ground, bool rampStart = true, bool rampEnd = true)
		{
			int n = along.Length;
			var deck = new float[n];
			for (int k = 0; k < n; k++)
			{
				float best = float.NegativeInfinity;
				for (int j = 0; j < n; j++)
				{
					best = Mathf.Max(best, highest[j] + DeckRiseMetres - DeckGrade * Mathf.Abs(along[k] - along[j]));
				}
				deck[k] = best;
			}
			if (n == 0)
			{
				return deck;
			}
			float start = along[0], end = along[n - 1];
			float footStart = ground[0] + MinClearMetres, footEnd = ground[n - 1] + MinClearMetres;
			float ramp = Mathf.Min(RampMetres, 0.5f * (end - start));
			for (int k = 0; k < n; k++)
			{
				float fromStart = along[k] - start, fromEnd = end - along[k];
				if (rampStart && ramp > 0f && fromStart < ramp)
				{
					deck[k] = Mathf.Lerp(footStart, deck[k], fromStart / ramp);
				}
				if (rampEnd && ramp > 0f && fromEnd < ramp)
				{
					deck[k] = Mathf.Lerp(footEnd, deck[k], fromEnd / ramp);
				}
				deck[k] = Mathf.Max(deck[k], ground[k] + MinClearMetres);
			}
			return deck;
		}

		/// <summary>The y scale of the bent under a station (pure); 0 where the deck clears the ground by too little to need one.</summary>
		public static float BentScale(Station station)
		{
			float clear = station.Deck - station.Ground;
			return clear > BentClearMetres ? StructurePieces.BoardwalkBentScale(clear) : 0f;
		}

		// ── Laying ────────────────────────────────────────────────────

		/// <summary>
		/// Lays one run's deck (<see cref="Stations"/>): a span module between each pair of stations, its deck on the line between
		/// their deck heights, scaled across to the way and along to the gap; a bent under every station but the ends that the deck
		/// clears by more than <see cref="BentClearMetres"/>. <paramref name="add"/> lays a prop (a site's AddProp, in its palette).
		/// Returns the props laid; 0, laying nothing, when the pieces are not generated yet.
		/// </summary>
		public static int Lay(ScenePath path, int from, int to, Func<float, float, float> ground, Action<GameObject, Vector3, Quaternion, Vector3> add,
			int seed, List<string> missing = null, bool rampStart = true, bool rampEnd = true)
		{
			bool footway = path.Class == ScenePathClass.Footpath || path.Class == ScenePathClass.Trail;
			GameObject span = StructureKit.Prefab("BoardwalkSpan", footway ? 1 : 0);
			GameObject[] bents = { StructureKit.Prefab("BoardwalkBent", 0), StructureKit.Prefab("BoardwalkBent", 1) };
			if (span == null || bents[0] == null)
			{
				missing?.Add("a boardwalk's pieces (Structure_BoardwalkSpan, Structure_BoardwalkBent) are not generated yet: Biome Tools → Generate biome art, then regenerate.");
				return 0;
			}
			List<Station> stations = Stations(path, from, to, ground, StructurePieces.BoardwalkModule, rampStart, rampEnd);
			int laid = 0;
			for (int k = 0; k + 1 < stations.Count; k++)
			{
				Station a = stations[k], b = stations[k + 1];
				var pa = new Vector3(a.At.x, a.Deck, a.At.y);
				var pb = new Vector3(b.At.x, b.Deck, b.At.y);
				Vector3 along = pb - pa;
				if (along.sqrMagnitude < 1e-4f)
				{
					continue;
				}
				float width = 2f * DeckHalfWidth(0.5f * (a.Half + b.Half));
				add(span, 0.5f * (pa + pb), Quaternion.LookRotation(along.normalized, Vector3.up),
					new Vector3(width / StructurePieces.BoardwalkWidth, 1f, along.magnitude / StructurePieces.BoardwalkModule));
				laid++;
			}
			// Under every station between, and under an end that does not ramp down (it meets another deck at its height).
			for (int k = rampStart ? 1 : 0; k < (rampEnd ? stations.Count - 1 : stations.Count); k++)
			{
				Station st = stations[k];
				float scale = BentScale(st);
				if (!(scale > 0f))
				{
					continue;
				}
				Vector2 dir = Direction(stations, k);
				float width = 2f * DeckHalfWidth(st.Half);
				uint h = FloatingPlants.Hash(unchecked((uint)seed), Mathf.RoundToInt(st.At.x * 4f), Mathf.RoundToInt(st.At.y * 4f), 0xB0A2);
				GameObject bent = bents[(h % 3u) == 0u && bents[1] != null ? 1 : 0];
				add(bent, new Vector3(st.At.x, st.Deck, st.At.y), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up),
					new Vector3(width / StructurePieces.BoardwalkWidth, scale, 1f));
				laid++;
			}
			return laid;
		}

		/// <summary>
		/// Lays the decks of every routed way's flagged runs (settlements lay their streets' own, PropsFeature): each run in the
		/// structure palette of the biome at its middle (<paramref name="biomeAt"/>, may be null: the kit's). Returns the props laid.
		/// </summary>
		public static int LayWays(IReadOnlyList<ScenePath> paths, PointOfInterestPropSink sink, Func<float, float, float> ground,
			Func<float, float, BiomeTemplate> biomeAt, int seed, List<string> notes)
		{
			if (paths == null || sink == null)
			{
				return 0;
			}
			int laid = 0, runs = 0;
			var missing = new List<string>();
			foreach (ScenePath path in paths)
			{
				if (path == null || path.Class == ScenePathClass.Street || path.Class == ScenePathClass.Yard)
				{
					continue;
				}
				foreach ((int from, int to) in FlaggedRuns(path))
				{
					Vector3 middle = path.Points[(from + to) / 2];
					var context = new PointOfInterestSiteContext
					{
						Sink = sink,
						GroundAt = ground,
						Palette = BiomeTemplateArt.StructurePalette(biomeAt != null ? biomeAt(middle.x, middle.z) : null),
						Notes = notes,
					};
					int n = Lay(path, from, to, ground, context.AddProp, seed ^ path.Id, missing);
					laid += n;
					runs += n > 0 ? 1 : 0;
					if (missing.Count > 0)
					{
						break;
					}
				}
				if (missing.Count > 0)
				{
					break;
				}
			}
			if (missing.Count > 0)
			{
				notes?.Add($"Boardwalks: {missing[0]}");
			}
			else if (laid > 0)
			{
				notes?.Add($"Boardwalks: {runs} run(s) of way raised on {laid:N0} deck module(s) and bent(s).");
			}
			return laid;
		}

		/// <summary>
		/// The biome at a point by the scene's biome map as the cut wrote it beside its terrain, read on first use (a scene with no
		/// boardwalk never loads it); null where there is none.
		/// </summary>
		public static Func<float, float, BiomeTemplate> MapBiomes(string terrainFolder, string sceneName)
		{
			SceneBiomeMap map = null;
			bool read = false;
			return (x, z) =>
			{
				if (!read)
				{
					read = true;
					map = string.IsNullOrEmpty(terrainFolder) || string.IsNullOrEmpty(sceneName) ? null
						: AssetDatabase.LoadAssetAtPath<SceneBiomeMap>($"{terrainFolder}/{WorldEditorAssets.Sanitize(sceneName)} Biome Map.asset");
				}
				return map != null ? map.Sample(new Vector3(x, 0f, z)) : null;
			};
		}
	}
}
#endif
