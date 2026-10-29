#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The straight-road way class (2026-10-26, phase I2l-b; decisions 50, 56, 87, 89): which trunk ways are laid dead-straight, to which
	/// profile, and whether a straight line between two ends can hold its grade. Pure: the planner (<see cref="PathNetworkPlanner"/>)
	/// asks it, the carve (<see cref="PathCarver"/>) builds what it decided.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One network a scene.</b> A trunk way (settlement to settlement) is a straight road when the biomes at both its ends lay them
	/// (<see cref="BiomeTemplate.StraightRoads"/>); of those ways only the largest connected set is (<see cref="Network"/>): the
	/// drafts' "≤ 1 network". Every other way is routed round the ground as before, and so is a straight one whose line cannot be held
	/// (<see cref="Profile"/>: open water, a hole, another site, or ground too steep for its cut and fill and its stairs).
	/// </para>
	/// <para>
	/// <b>Its profiles.</b> A plain straight road is cut and filled up to 4 m along its line to its class's grade, and climbs a cliff
	/// that stands across it on one even flight of stairs (<see cref="ScenePathPointFlags.Stairs"/>) no steeper than a walker climbs; a
	/// raised one stands on an embankment over the ground beside it (the sacbe); a cut one is sunk between sheer walls (the via cava),
	/// at most <see cref="MaxCutWays"/> of them a scene, the ways that run through the highest ground (<see cref="Forms"/>). A royal
	/// road's ruts are its surface (<see cref="BiomeStraightRoads.Ruts"/>).
	/// </para>
	/// <para>With no biome laying straight roads (every biome's default) nothing here is asked and every way is routed exactly as before.</para>
	/// </remarks>
	public static class PathStraightRoads
	{
		/// <summary>The most cut roads (vie cave) one scene's network sinks (decision 87).</summary>
		public const int MaxCutWays = 2;

		/// <summary>The longest flight of stairs a straight road climbs a cliff by, metres along it.</summary>
		public const float StairsRunMetres = 36f;

		/// <summary>The most a flight of stairs is cut into the cliff or built out from its foot, metres.</summary>
		public const float StairsCutMetres = 8f;

		/// <summary>The steepest flight of stairs (rise over run): what a walker climbs without hands, the router's own limit.</summary>
		public const float StairsGrade = PathRouter.WalkableGrade;

		/// <summary>A trunk way the network may lay straight: its two settlements (record ids), its length, and the roads its ends lay.</summary>
		public struct Link
		{
			public int A, B;
			public float Length;
			public BiomeStraightRoads Roads;
		}

		/// <summary>
		/// The straight roads a way between two ends lays: none unless both ends' biomes lay them; their form where both name the same,
		/// else the plain straight road; ruts only where both wear them.
		/// </summary>
		public static BiomeStraightRoads Join(in BiomeStraightRoads a, in BiomeStraightRoads b)
		{
			if (!a.IsSet || !b.IsSet)
			{
				return BiomeStraightRoads.None;
			}
			return new BiomeStraightRoads(a.Form == b.Form ? a.Form : BiomeStraightRoadForm.Straight, a.Ruts && b.Ruts);
		}

		/// <summary>The path form a biome's straight-road form builds.</summary>
		public static ScenePathForm FormOf(BiomeStraightRoadForm form)
		{
			switch (form)
			{
				case BiomeStraightRoadForm.Straight: return ScenePathForm.Straight;
				case BiomeStraightRoadForm.Raised: return ScenePathForm.Raised;
				case BiomeStraightRoadForm.Cut: return ScenePathForm.Cut;
				default: return ScenePathForm.Routed;
			}
		}

		/// <summary>
		/// The one network (indices into <paramref name="links"/>, ascending): of the links whose roads are set, the connected set with
		/// the greatest length, ties to the set holding the lowest settlement id. Empty when no link is set.
		/// </summary>
		public static List<int> Network(IReadOnlyList<Link> links)
		{
			var network = new List<int>();
			if (links == null || links.Count == 0)
			{
				return network;
			}
			var parent = new Dictionary<int, int>();
			int Find(int x)
			{
				while (parent[x] != x)
				{
					parent[x] = parent[parent[x]];
					x = parent[x];
				}
				return x;
			}
			foreach (Link l in links)
			{
				if (!l.Roads.IsSet)
				{
					continue;
				}
				if (!parent.ContainsKey(l.A)) parent[l.A] = l.A;
				if (!parent.ContainsKey(l.B)) parent[l.B] = l.B;
				int ra = Find(l.A), rb = Find(l.B);
				if (ra != rb)
				{
					// The lower id roots the set, so its key is the set's lowest settlement.
					if (ra < rb) parent[rb] = ra; else parent[ra] = rb;
				}
			}
			if (parent.Count == 0)
			{
				return network;
			}
			var length = new Dictionary<int, float>();
			foreach (Link l in links)
			{
				if (l.Roads.IsSet)
				{
					int root = Find(l.A);
					length[root] = (length.TryGetValue(root, out float m) ? m : 0f) + l.Length;
				}
			}
			int best = int.MaxValue;
			float bestLength = -1f;
			foreach (KeyValuePair<int, float> set in length)
			{
				if (set.Value > bestLength || set.Value == bestLength && set.Key < best)
				{
					best = set.Key;
					bestLength = set.Value;
				}
			}
			for (int i = 0; i < links.Count; i++)
			{
				if (links[i].Roads.IsSet && Find(links[i].A) == best)
				{
					network.Add(i);
				}
			}
			return network;
		}

		/// <summary>
		/// The form each link of the network is built to (indexed as <paramref name="links"/>; Routed off the network): its roads' own,
		/// except that of the links whose roads are cut only the <see cref="MaxCutWays"/> through the highest ground
		/// (<paramref name="relief"/>, metres; ties to the lower index) are sunk, the rest laid plain straight.
		/// </summary>
		public static ScenePathForm[] Forms(IReadOnlyList<Link> links, IReadOnlyList<int> network, IReadOnlyList<float> relief)
		{
			var forms = new ScenePathForm[links != null ? links.Count : 0];
			if (network == null)
			{
				return forms;
			}
			var cut = new List<int>();
			foreach (int i in network)
			{
				forms[i] = FormOf(links[i].Roads.Form);
				if (forms[i] == ScenePathForm.Cut)
				{
					cut.Add(i);
				}
			}
			cut.Sort((p, q) =>
			{
				float rp = relief != null && p < relief.Count ? relief[p] : 0f, rq = relief != null && q < relief.Count ? relief[q] : 0f;
				int c = rq.CompareTo(rp);
				return c != 0 ? c : p.CompareTo(q);
			});
			for (int k = MaxCutWays; k < cut.Count; k++)
			{
				forms[cut[k]] = ScenePathForm.Straight;
			}
			return forms;
		}

		/// <summary>How high the ground stands over the straight chord between a line's ends, at its highest, metres (0 or more).</summary>
		public static float Relief(IReadOnlyList<float> natural)
		{
			int n = natural != null ? natural.Count : 0;
			if (n < 2)
			{
				return 0f;
			}
			float most = 0f;
			for (int i = 0; i < n; i++)
			{
				float chord = Mathf.Lerp(natural[0], natural[n - 1], i / (float)(n - 1));
				most = Mathf.Max(most, natural[i] - chord);
			}
			return most;
		}

		/// <summary>
		/// The carve's levels along a line (as PathCarver.Targets evens them, before its dip): the ground beside each point evened over
		/// the form's smoothing length and held within its cut and fill. Pure.
		/// </summary>
		public static float[] Levels(IReadOnlyList<float> natural, float spacing, in ScenePathStyle style)
		{
			int n = natural != null ? natural.Count : 0;
			var level = new float[n];
			float sigma = Mathf.Max(0.5f, style.SmoothMetres / Mathf.Max(0.1f, spacing));
			int reach = Mathf.CeilToInt(sigma * 2.5f);
			for (int i = 0; i < n; i++)
			{
				float sum = 0f, weight = 0f;
				for (int k = Mathf.Max(0, i - reach); k <= Mathf.Min(n - 1, i + reach); k++)
				{
					float w = Mathf.Exp(-0.5f * (k - i) * (k - i) / (sigma * sigma));
					sum += natural[k] * w;
					weight += w;
				}
				float even = weight > 0f ? sum / weight : natural[i];
				level[i] = Mathf.Clamp(even, natural[i] - style.MaxCut, natural[i] + style.MaxCut);
			}
			return level;
		}

		/// <summary>
		/// Whether a straight line holds its grade (pure): the carve's levels along it (<see cref="Levels"/>, points
		/// <paramref name="spacing"/> apart) climb no steeper than a routed way of its class may (its grade × PathRouter.GradeLimit), except over cliffs it takes by a flight of stairs —
		/// one even ramp from the level before the cliff to the level past it, widened a point each way until it is no steeper than
		/// <see cref="StairsGrade"/>, no longer than <see cref="StairsRunMetres"/> and nowhere more than <see cref="StairsCutMetres"/>
		/// off the ground. <paramref name="stairs"/> (as long as the line, may be null) is set true on each flight's inner points, the
		/// run the carve lays as the ramp (PathCarver.Stairs). False when some stretch can be held neither way.
		/// </summary>
		public static bool Profile(IReadOnlyList<float> natural, float spacing, in ScenePathStyle style, bool[] stairs)
		{
			int n = natural != null ? natural.Count : 0;
			if (n < 2)
			{
				return n > 0;
			}
			float[] level = Levels(natural, spacing, style);
			// Slope-capped as a routed way of its class is (PathRouter.MaxGradeOf): steeper only on stairs.
			float limit = Mathf.Min(PathRouter.WalkableGrade, style.MaxGrade * PathRouter.GradeLimit) * 1.0001f;
			// A flight never reaches back over the one before it: the carve lays each flagged run as one ramp.
			int floor = 0;
			int i = 0;
			while (i < n - 1)
			{
				if (Mathf.Abs(level[i + 1] - level[i]) <= limit * spacing)
				{
					i++;
					continue;
				}
				// A steep run of segments [s, e]: the flight runs from point a to point b past it.
				int s = i, e = i;
				while (e + 1 < n - 1 && Mathf.Abs(level[e + 2] - level[e + 1]) > limit * spacing)
				{
					e++;
				}
				int a = s, b = e + 1;
				while ((b - a) * spacing <= StairsRunMetres && Mathf.Abs(level[b] - level[a]) > StairsGrade * 0.95f * (b - a) * spacing)
				{
					if (a == floor && b == n - 1)
					{
						break;
					}
					a = Mathf.Max(floor, a - 1);
					b = Mathf.Min(n - 1, b + 1);
				}
				float run = (b - a) * spacing;
				if (run > StairsRunMetres || Mathf.Abs(level[b] - level[a]) > StairsGrade * run)
				{
					return false;
				}
				for (int k = a; k <= b; k++)
				{
					float ramp = Mathf.Lerp(level[a], level[b], (k - a) / (float)(b - a));
					if (Mathf.Abs(natural[k] - ramp) > StairsCutMetres)
					{
						return false;
					}
				}
				if (stairs != null)
				{
					for (int k = a + 1; k < b && k < stairs.Length; k++)
					{
						stairs[k] = true;
					}
				}
				// The next flight starts at this one's top at the earliest: the point between them is no flight's inner point.
				floor = b;
				i = b;
			}
			return true;
		}

		/// <summary>The points of a straight line from <paramref name="a"/> to <paramref name="b"/>, both included, about <paramref name="spacing"/> apart.</summary>
		public static List<Vector2> Line(Vector2 a, Vector2 b, float spacing)
		{
			float length = Vector2.Distance(a, b);
			int n = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.1f, spacing)));
			var line = new List<Vector2>(n + 1);
			for (int k = 0; k <= n; k++)
			{
				line.Add(Vector2.Lerp(a, b, k / (float)n));
			}
			return line;
		}
	}
}
#endif
