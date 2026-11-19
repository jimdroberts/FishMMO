using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What kind of stored chute a line is (2026-10-25, phase I2u "falling stone", decisions 79 and 95). APPEND-ONLY: stored in the
	/// scene's biome map (<see cref="RockChute.Kind"/>).
	/// </summary>
	public enum RockChuteKind : byte
	{
		/// <summary>A scree chute: a gully down a cliff face, its runout over the talus cone below (Scree's ScreeChutes).</summary>
		Scree = 0,
		/// <summary>A sciara: the wide black scar down a Strombolian cone's flank the bombs roll down (Volcanic Temple's Sciara).</summary>
		Sciara = 1,
	}

	/// <summary>
	/// One stored chute (I2u): a line stones fall down, head first, its points in the map's <see cref="SceneBiomeMap.ChutePoints"/>
	/// (<see cref="First"/>, <see cref="Count"/>), written by the cut and every repaint (<see cref="RockChutes.FromLandforms"/>).
	/// </summary>
	[Serializable]
	public struct RockChute
	{
		public RockChuteKind Kind;
		/// <summary>Its biome's template id.</summary>
		public int BiomeID;
		/// <summary>Its own hash: its falls' schedule and every stone of them come from it.</summary>
		public uint Seed;
		/// <summary>Its first point in the map's chute points, and how many it has.</summary>
		public int First;
		public int Count;
		/// <summary>Metres along its points, head to runout.</summary>
		public float Length;
		/// <summary>Half its width across, metres: how far to either side of the line its stones fall.</summary>
		public float Half;
		/// <summary>Its fall rate's own factor: 1, or <see cref="RockChutes.WalkingRockBoost"/> near a walking rock (decision 79).</summary>
		public float Boost;
	}

	/// <summary>
	/// One stored talus cone (I2u): where a breathing talus breathes (cold air out of its toe in summer, warm air out of its head in
	/// winter) and where its stones grade from fine to coarse.
	/// </summary>
	[Serializable]
	public struct TalusCone
	{
		public int BiomeID;
		public uint Seed;
		/// <summary>Its apex (head) and toe on the ground, world metres.</summary>
		public Vector3 Apex;
		public Vector3 Toe;
		/// <summary>Its half spread at the toe, metres.</summary>
		public float Spread;
	}

	/// <summary>
	/// The scene's stored chutes and talus cones (2026-10-25, phase I2u, decisions 79 and 95), made at the cut from its landforms: every
	/// run of a chute landform's line oriented downhill (the cut's carved ground), a scree chute's run carried on down the steepest
	/// descent over its cone, resampled to points with their ground; and every talus cone's apex and toe. Pure, given the ground.
	/// </summary>
	public static class RockChutes
	{
		/// <summary>The landform kinds that are chutes (their preset names), and the kind each makes.</summary>
		public static readonly string[] ChuteKinds = { "ScreeChutes", "Sciara" };

		/// <summary>The landform kinds that are talus cones (a Fan; apex at its centre, toe down its axis).</summary>
		public static readonly string[] ConeKinds = { "ScreeCone" };

		/// <summary>The landform whose pads are walking rocks: chutes within <see cref="WalkingRockReach"/> fall <see cref="WalkingRockBoost"/> as often.</summary>
		public const string WalkingRockKind = "WalkingRock";
		public const float WalkingRockReach = 150f, WalkingRockBoost = 2f;

		/// <summary>The spacing of the stored points, metres (the most points a chute keeps stretches it on a long one).</summary>
		public const float PointSpacing = 8f;
		public const int MostPointsPerChute = 256;

		/// <summary>A scree chute's runout: the steepest descent from its low end in steps, until the ground flattens past this slope or it has run this far.</summary>
		public const float RunoutStep = 6f, RunoutMetres = 120f, RunoutLeastDrop = 0.17f;

		/// <summary>The most chutes and cones a scene stores (the rest are dropped, last first).</summary>
		public const int MostChutes = 512, MostCones = 256;

		/// <summary>Talus size grading (decision 79 (d)): a stone's scale at a cone's apex and at its toe.</summary>
		public const float ApexScale = 0.6f, ToeScale = 1.8f;

		/// <summary>True when a landform of this name is a chute, with the chute kind it makes.</summary>
		public static bool IsChute(string landformKind, out RockChuteKind kind)
		{
			kind = RockChuteKind.Scree;
			for (int i = 0; i < ChuteKinds.Length; i++)
			{
				if (string.Equals(ChuteKinds[i], landformKind, StringComparison.OrdinalIgnoreCase))
				{
					kind = (RockChuteKind)i;
					return true;
				}
			}
			return false;
		}

		/// <summary>True when a landform of this name is a talus cone.</summary>
		public static bool IsCone(string landformKind)
		{
			foreach (string k in ConeKinds)
			{
				if (string.Equals(k, landformKind, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The chutes and cones of a scene's landforms (pure in the sites, pads and ground): each run of a chute landform's segments (a run
		/// starts at along 0) one chute, oriented so its head is its highest end, a scree chute carried on down the steepest descent over
		/// its cone (<see cref="Runout"/>), resampled every <see cref="PointSpacing"/> m (fewer on a long one) with the ground there; and
		/// each cone landform's apex and toe. Fills the three lists (cleared first). No chute landform: all empty.
		/// </summary>
		public static void FromLandforms(IReadOnlyList<BiomeLandformSite> sites, IReadOnlyList<BiomeLandformPad> pads, Func<float, float, float> groundAt,
			List<RockChute> chutes, List<Vector4> points, List<TalusCone> cones)
		{
			chutes.Clear();
			points.Clear();
			cones.Clear();
			if (sites == null || groundAt == null)
			{
				return;
			}
			var line = new List<Vector2>();
			for (int s = 0; s < sites.Count; s++)
			{
				BiomeLandformSite site = sites[s];
				string name = site.Form.Kind;
				if (IsCone(name) && cones.Count < MostCones)
				{
					cones.Add(ConeOf(site, groundAt));
					continue;
				}
				if (!IsChute(name, out RockChuteKind kind) || site.Segments == null || site.Segments.Count == 0)
				{
					continue;
				}
				int run = -1;
				float halfSum = 0f;
				int halves = 0;
				for (int k = 0; k <= site.Segments.Count; k++)
				{
					bool end = k == site.Segments.Count || (k > 0 && site.Segments[k].AlongA <= 1e-4f);
					if (end && line.Count >= 2 && chutes.Count < MostChutes)
					{
						Add(kind, site.BiomeID, Mix(site.Seed, (uint)run), line, halves > 0 ? halfSum / halves : 1f, pads, groundAt, chutes, points);
					}
					if (k == site.Segments.Count)
					{
						break;
					}
					BiomeLandformSegment seg = site.Segments[k];
					if (k == 0 || end)
					{
						run++;
						line.Clear();
						halfSum = 0f;
						halves = 0;
						line.Add(seg.A);
					}
					line.Add(seg.B);
					halfSum += Mathf.Max(0.25f, seg.Half);
					halves++;
				}
				line.Clear();
			}
		}

		private static void Add(RockChuteKind kind, int biomeID, uint seed, List<Vector2> run, float half, IReadOnlyList<BiomeLandformPad> pads,
			Func<float, float, float> groundAt, List<RockChute> chutes, List<Vector4> points)
		{
			var path = new List<Vector2>(run);
			if (groundAt(path[0].x, path[0].y) < groundAt(path[path.Count - 1].x, path[path.Count - 1].y))
			{
				path.Reverse();
			}
			if (kind == RockChuteKind.Scree)
			{
				Runout(path, groundAt);
			}
			float length = 0f;
			for (int i = 1; i < path.Count; i++)
			{
				length += Vector2.Distance(path[i - 1], path[i]);
			}
			if (length < 1f)
			{
				return;
			}
			int count = Mathf.Clamp(Mathf.CeilToInt(length / PointSpacing) + 1, 2, MostPointsPerChute);
			int first = points.Count;
			float boost = 1f;
			for (int i = 0; i < count; i++)
			{
				float along = length * i / (count - 1);
				Vector2 p = PointAlong(path, along);
				points.Add(new Vector4(p.x, groundAt(p.x, p.y), p.y, along));
				if (boost < WalkingRockBoost && NearWalkingRock(pads, biomeID, p))
				{
					boost = WalkingRockBoost;
				}
			}
			chutes.Add(new RockChute { Kind = kind, BiomeID = biomeID, Seed = seed, First = first, Count = count, Length = length, Half = Mathf.Max(0.25f, half), Boost = boost });
		}

		/// <summary>
		/// Carries a line on from its last point down the steepest descent of the ground (a scree chute's runout over its cone): steps of
		/// <see cref="RunoutStep"/> m while each drops at least <see cref="RunoutLeastDrop"/> of its length, at most <see cref="RunoutMetres"/>.
		/// </summary>
		public static void Runout(List<Vector2> path, Func<float, float, float> groundAt)
		{
			if (path == null || path.Count == 0 || groundAt == null)
			{
				return;
			}
			Vector2 at = path[path.Count - 1];
			float h = groundAt(at.x, at.y);
			for (float run = 0f; run < RunoutMetres; run += RunoutStep)
			{
				const float e = 2f;
				var g = new Vector2(groundAt(at.x + e, at.y) - groundAt(at.x - e, at.y), groundAt(at.x, at.y + e) - groundAt(at.x, at.y - e));
				if (!(g.sqrMagnitude > 1e-10f))
				{
					return;
				}
				Vector2 next = at - g.normalized * RunoutStep;
				float hn = groundAt(next.x, next.y);
				if (!(h - hn >= RunoutLeastDrop * RunoutStep))
				{
					return;
				}
				path.Add(next);
				at = next;
				h = hn;
			}
		}

		private static bool NearWalkingRock(IReadOnlyList<BiomeLandformPad> pads, int biomeID, Vector2 p)
		{
			for (int i = 0; pads != null && i < pads.Count; i++)
			{
				if (pads[i].BiomeID == biomeID && string.Equals(pads[i].Kind, WalkingRockKind, StringComparison.OrdinalIgnoreCase)
					&& (pads[i].Centre - p).sqrMagnitude <= WalkingRockReach * WalkingRockReach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The point <paramref name="along"/> metres down a polyline (clamped to its ends).</summary>
		public static Vector2 PointAlong(List<Vector2> path, float along)
		{
			for (int i = 1; i < path.Count; i++)
			{
				float d = Vector2.Distance(path[i - 1], path[i]);
				if (along <= d || i == path.Count - 1)
				{
					return d > 1e-6f ? Vector2.Lerp(path[i - 1], path[i], Mathf.Clamp01(along / d)) : path[i];
				}
				along -= d;
			}
			return path[path.Count - 1];
		}

		/// <summary>A cone landform's apex (its centre) and toe (down its axis by its length), on the ground.</summary>
		public static TalusCone ConeOf(in BiomeLandformSite site, Func<float, float, float> groundAt)
		{
			float length = Mathf.Max(0.01f, Mathf.Max(site.Radius, site.HalfLength));
			float yaw = site.Yaw * Mathf.Deg2Rad;
			Vector2 toe = site.Centre + new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)) * length;
			float spread = site.Form.Flank > 0f ? site.Form.Flank : 50f;
			return new TalusCone
			{
				BiomeID = site.BiomeID,
				Seed = site.Seed,
				Apex = new Vector3(site.Centre.x, groundAt(site.Centre.x, site.Centre.y), site.Centre.y),
				Toe = new Vector3(toe.x, groundAt(toe.x, toe.y), toe.y),
				Spread = length * Mathf.Tan(Mathf.Clamp(spread, 5f, 80f) * Mathf.Deg2Rad),
			};
		}

		/// <summary>
		/// Where a point stands on a cone landform (the Fan's own shape, BiomeLandforms.Shape): 0 at its apex … 1 at its toe, and how
		/// fully it is inside the cone (<paramref name="weight"/>: 1 within its spread, easing out over 30° past it; 0 outside).
		/// </summary>
		public static float ConeAlong(in BiomeLandformSite site, float x, float z, out float weight)
		{
			weight = 0f;
			float length = Mathf.Max(0.01f, Mathf.Max(site.Radius, site.HalfLength));
			float yaw = site.Yaw * Mathf.Deg2Rad;
			float dx = x - site.Centre.x, dz = z - site.Centre.y;
			float u = dx * Mathf.Cos(yaw) + dz * Mathf.Sin(yaw);
			float v = -dx * Mathf.Sin(yaw) + dz * Mathf.Cos(yaw);
			float rho = Mathf.Sqrt(u * u + v * v) / length;
			if (rho >= 1f)
			{
				return 1f;
			}
			float phi = Mathf.Abs(Mathf.Atan2(v, u)) * Mathf.Rad2Deg;
			float spread = site.Form.Flank > 0f ? site.Form.Flank : 50f;
			float t = Mathf.Clamp01((phi - (spread - 15f)) / 30f);
			weight = 1f - t * t * (3f - 2f * t);
			return rho;
		}

		/// <summary>Talus size grading's scale at a place along a cone (0 apex … 1 toe): fine at the apex, coarse at the toe.</summary>
		public static float GradeScale(float along) => Mathf.Lerp(ApexScale, ToeScale, Mathf.Clamp01(along));

		/// <summary>The grading's scale for a point a cone holds this fully (<see cref="ConeAlong"/>): 1 outside every cone.</summary>
		public static float GradeAt(float along, float weight) => Mathf.Lerp(1f, GradeScale(along), Mathf.Clamp01(weight));

		/// <summary>A chute run's seed from its landform's and the run's index (FishMMO.Shared.Weather.TurbidityEpisodes.Mix's mixer).</summary>
		public static uint Mix(uint v, uint seed)
		{
			uint h = v * 0x9E3779B1u ^ seed;
			h ^= h >> 16;
			h *= 0x7FEB352Du;
			h ^= h >> 15;
			h *= 0x846CA68Bu;
			h ^= h >> 16;
			return h;
		}
	}
}
