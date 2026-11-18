#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How a system's first mouth is opened when it is not come to over the ground before it (2026-10-18: "Caves should be
	/// able to appear behind waterfalls and under water"): <see cref="CaveSystem.Plan"/> takes it.
	/// </summary>
	public struct CaveOpening
	{
		public CaveMouthKind Kind;
		/// <summary>Submerged: the surface over the mouth (a lake's, or the sea's), world y. Shelf: the fall's pool.</summary>
		public float Water;
		/// <summary>Shelf: the mouth's floor, world y (the shelf's level).</summary>
		public float Floor;
		/// <summary>The widest bore the mouth may open to, metres (no wider than its water hides); 0 for the profile's own.</summary>
		public float MaxRadius;
		/// <summary>Shelf: where the way to it ends, on the bank beside the pool (world).</summary>
		public Vector3 Landing;

		/// <summary>A mouth under <paramref name="water"/>'s surface, its bore no wider than <see cref="CaveSystem.SubmergedRadius"/>.</summary>
		public static CaveOpening Submerged(float water) => new CaveOpening { Kind = CaveMouthKind.Submerged, Water = water, MaxRadius = CaveSystem.SubmergedRadius };

		/// <summary>A mouth behind a waterfall's curtain (<see cref="CaveFallMouth"/>).</summary>
		public static CaveOpening Behind(in CaveFallMouth fall)
			=> new CaveOpening { Kind = CaveMouthKind.Shelf, Water = fall.Pool, Floor = fall.Floor, MaxRadius = fall.MaxRadius, Landing = fall.Landing };
	}

	public sealed partial class CaveSystem
	{
		// ── Under water ───────────────────────────────────────────

		/// <summary>The deepest under its water a mouth may lie, metres: a dive of a breath, there and up the sump.</summary>
		public const float MaxDive = 14f;
		/// <summary>The water left over a submerged mouth's arch at the least, metres: the opening lies wholly under the surface.</summary>
		public const float SubmergedClear = 0.4f;
		/// <summary>The widest bore a submerged mouth opens to, metres: a swimmer's hole in the cliff, not a harbour.</summary>
		public const float SubmergedRadius = 2.4f;
		/// <summary>How far over the dry limit (the water's surface and <see cref="AboveSea"/>) the sump rises before it may level, metres.</summary>
		public const float SumpClear = 0.5f;

		/// <summary>
		/// Whether a point (system frame) lies in the flooded part of a submerged mouth's system: under the water its mouth
		/// opens beneath. The sump: everything past it stands over <see cref="LowestFloor"/>, dry.
		/// </summary>
		public bool Flooded(Vector3 p) => HasSump && Origin.y + p.y < WaterLevel;

		/// <summary>True for a system entered through a submerged mouth (its trunk a sump rising from it to dry chambers).</summary>
		public bool HasSump => Mouths.Count > 0 && Mouths[0].Kind == CaveMouthKind.Submerged;

		/// <summary>
		/// Whether a mouth's ground before it is graded to its floor (<see cref="Approach"/>): only a mouth come to over the
		/// ground, of a dry cave (a submerged one is dived to, a shelf behind a waterfall graded by <see cref="CaveFallMouth"/>).
		/// </summary>
		public bool GradesApproachOf(int mouth) => GradesApproach && Mouths[mouth].Kind == CaveMouthKind.Ground;
	}

	/// <summary>
	/// A cave mouth behind a waterfall (2026-10-18): set in the fall's headwall under its lip, behind the curtain, its floor a
	/// shelf's height over the plunge pool, and the shelf: a level walkway along the foot of the basin's wall from the mouth,
	/// beside the pool, to the dry bank where the way to it ends. Found on the fall's own carved ground, deterministically, by
	/// the planner (where to site the cave) and by the shaper (to plan and grade it) alike.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Behind the curtain.</b> The mouth faces downstream from the face where the ground falls to the shelf's level on the
	/// fall's own line, its bore no wider than the curtain hides (<see cref="MaxRadius"/>: the arch's foot within the lip's
	/// width) and no taller than the face has rock over it. The curtain is drawn at runtime pushed clear of the terrain
	/// (InlandWaterRenderer's sheet), so it falls in front of the shelf, over the mouth.
	/// </para>
	/// <para>
	/// <b>The shelf.</b> Walked along the contour of its own level round the basin's wall, either way from the mouth, a step
	/// a metre: each step finds where the ground crosses the level on a half circle ahead, and the walkway's middle lies
	/// <see cref="CaveSystem.ShelfOffset"/> out from that toward the water. It ends where the walkway's own ground stands near
	/// its level, dry and clear of the fall's reach (the bank); the shorter side is taken. <see cref="Grade"/> levels the
	/// walkway into the ground: filled out over the pool's edge, cut into the wall's foot.
	/// </para>
	/// </remarks>
	public struct CaveFallMouth
	{
		/// <summary>The mouth's floor point (world): x, z at the face, y its floor.</summary>
		public Vector3 Mouth;
		/// <summary>The way the mouth faces (downstream), degrees about +y.</summary>
		public float Yaw;
		/// <summary>The fall's pool's level and the mouth's (the shelf's) floor, world y.</summary>
		public float Pool, Floor;
		public float Drop, Width;
		/// <summary>The widest bore the mouth may open to: its arch hidden by the curtain, rock over it under the lip.</summary>
		public float MaxRadius;
		/// <summary>The lip's point on the river (world plan).</summary>
		public Vector2 Lip;
		/// <summary>The walkway's middle line (world, y its level): from the mouth's threshold out to the landing.</summary>
		public List<Vector3> Shelf;
		/// <summary>Where the way to the mouth ends: the shelf's end on the bank.</summary>
		public Vector3 Landing;

		/// <summary>The least fall a cave is set behind, metres: the shelf, a bore and its roof under the lip.</summary>
		public const float MinDrop = 10f;
		/// <summary>How far over the pool the shelf (and the mouth's floor) stands, metres.</summary>
		public const float Rise = 1.5f;
		/// <summary>The walkway's half width, metres.</summary>
		public const float HalfWidth = 1.2f;
		/// <summary>How far out from the wall's foot the walkway's middle runs, metres.</summary>
		public const float Offset = 1.4f;
		/// <summary>The longest shelf walked before a side is given up, metres (a step a metre).</summary>
		public const int MostSteps = 70;
		/// <summary>The grades the shelf's fill (out over the pool) and cut (into the wall) ease back to the ground at, rise over run.</summary>
		public const float FillGrade = 1f, CutGrade = 2.5f;
		/// <summary>The most the shelf cuts or fills the ground, metres; past it the ground is left as it is.</summary>
		public const float MaxGive = 8f;

		/// <summary>
		/// The cave mouth a fall has room for behind its curtain, and its shelf; false where the fall is too low or too narrow,
		/// there is no face at the shelf's level under its lip, or no shelf reaches a dry bank.
		/// </summary>
		/// <param name="ground">The finished ground at (east, north).</param>
		/// <param name="wet">True where a river or lake stands at (east, north).</param>
		public static bool Find(RiverPath river, FallLedges.FallSpan fall, Func<float, float, float> ground, Func<float, float, bool> wet, out CaveFallMouth found)
		{
			found = default;
			if (river == null || ground == null || fall.Lip < 0 || fall.Foot >= river.Count || fall.Foot <= fall.Lip)
			{
				return false;
			}
			int lip = fall.Lip;
			float pool = river.Surface[fall.Foot], drop = river.Surface[lip] - pool, width = Mathf.Max(1f, river.Width[lip]);
			if (drop < MinDrop)
			{
				return false;
			}
			// Downstream as the water leaves the lip (FallLedges.PlanFall's line), and left of it.
			int next = Math.Min(fall.Foot, lip + 1);
			var t = new Vector2(river.X[next] - river.X[lip], river.Z[next] - river.Z[lip]);
			if (t.sqrMagnitude < 1e-8f)
			{
				return false;
			}
			t.Normalize();
			var lipAt = new Vector2(river.X[lip], river.Z[lip]);
			float floor = pool + Rise;
			// No wider than the curtain hides (the arch's foot a metre inside the lip's width), no taller than the rock under the
			// lip covers (the shelf, the bore and three metres of roof under the river's bed).
			float maxRadius = Mathf.Min((width - 1f) / (2f * (1f + CaveSystem.ArchSpread)), 0.5f * (drop - Rise - Mathf.Max(0f, river.Depth[lip]) - CaveSystem.MinCover - 1f), 3f);
			if (maxRadius < CaveSystem.MinBore + 0.2f)
			{
				return false;
			}
			// The face at the shelf's level, on the fall's own line: the first point downstream of the lip down to it.
			float run = river.S[fall.Foot] - river.S[lip] + 8f, face = float.NaN;
			for (float u = 0f; u <= run; u += 0.25f)
			{
				Vector2 p = lipAt + t * u;
				if (ground(p.x, p.y) <= floor)
				{
					face = u;
					break;
				}
			}
			if (float.IsNaN(face))
			{
				return false;
			}
			Vector2 mouth = lipAt + t * face;
			float lane = 1.2f * maxRadius + 1f;
			List<Vector3> best = null;
			foreach (float side in new[] { 1f, -1f })
			{
				List<Vector3> shelf = Walk(mouth, new Vector2(-t.y, t.x) * side, floor, river, fall, ground, wet);
				if (shelf != null && (best == null || shelf.Count < best.Count))
				{
					best = shelf;
				}
			}
			if (best == null)
			{
				return false;
			}
			// From the threshold out before the opening (inside the curtain), back to the wall's foot beside it, and on.
			best.InsertRange(0, new[] { new Vector3(mouth.x + t.x * lane, floor, mouth.y + t.y * lane), new Vector3(mouth.x + t.x * Offset, floor, mouth.y + t.y * Offset) });
			found = new CaveFallMouth
			{
				Mouth = new Vector3(mouth.x, floor, mouth.y),
				Yaw = Mathf.Repeat(Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg, 360f),
				Pool = pool,
				Floor = floor,
				Drop = drop,
				Width = width,
				MaxRadius = maxRadius,
				Lip = lipAt,
				Shelf = best,
				Landing = best[best.Count - 1],
			};
			return true;
		}

		/// <summary>
		/// The shelf from the mouth one way along the wall's foot (<paramref name="along"/>, a unit across the river): each step a
		/// metre on along the contour of its level, the walkway's middle <see cref="Offset"/> out toward the low side; ends on the
		/// bank, or null where the contour runs into rising ground, out of the reach, or on too long.
		/// </summary>
		private static List<Vector3> Walk(Vector2 mouth, Vector2 along, float level, RiverPath river, FallLedges.FallSpan fall,
			Func<float, float, float> ground, Func<float, float, bool> wet)
		{
			var shelf = new List<Vector3>();
			Vector2 at = mouth, dir = along;
			for (int k = 0; k < MostSteps; k++)
			{
				// The contour ahead: on a half circle a metre round the last point, the crossing of the level nearest straight on.
				float bestTurn = float.MaxValue;
				Vector2 nextAt = default;
				float previous = float.NaN, previousTurn = 0f;
				for (float turn = -90f; turn <= 90f; turn += 5f)
				{
					Vector2 p = at + Rotate(dir, turn);
					float s = ground(p.x, p.y) - level;
					if (!float.IsNaN(previous) && (s >= 0f) != (previous >= 0f))
					{
						float f = previous / (previous - s);
						float crossing = Mathf.Lerp(previousTurn, turn, f);
						if (Mathf.Abs(crossing) < Mathf.Abs(bestTurn))
						{
							bestTurn = crossing;
							nextAt = at + Rotate(dir, crossing);
						}
					}
					previous = s;
					previousTurn = turn;
				}
				if (bestTurn == float.MaxValue)
				{
					return null;
				}
				dir = (nextAt - at).normalized;
				at = nextAt;
				// The low side: toward the water, away from the wall.
				var left = new Vector2(-dir.y, dir.x);
				Vector2 low = ground(at.x + left.x * 1.5f, at.y + left.y * 1.5f) < ground(at.x - left.x * 1.5f, at.y - left.y * 1.5f) ? left : -left;
				Vector2 middle = at + low * Offset, edge = at + low * (Offset + HalfWidth);
				shelf.Add(new Vector3(middle.x, level, middle.y));
				// On the bank: the walkway's own ground near its level, dry, and clear of the fall's channel (no way is laid in it).
				if (k >= 2 && ground(middle.x, middle.y) >= level - 0.75f && !wet(middle.x, middle.y) && !wet(edge.x, edge.y)
					&& OffFall(middle, river, fall))
				{
					return shelf;
				}
			}
			return null;
		}

		/// <summary>Whether a point lies clear of a fall's channel from its lip to past its pool (FallReaches' extent, and a metre).</summary>
		private static bool OffFall(Vector2 p, RiverPath river, FallLedges.FallSpan fall)
		{
			int from = Math.Max(0, fall.Lip - 3), to = Math.Min(river.Count - 1, fall.Plunge + 6);
			for (int k = from; k < to; k++)
			{
				var a = new Vector2(river.X[k], river.Z[k]);
				var b = new Vector2(river.X[k + 1], river.Z[k + 1]);
				Vector2 ab = b - a;
				float s = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				float half = 0.5f * Mathf.Max(river.Width[k], river.Width[k + 1]) + FallReaches.MarginMetres + 1f;
				if ((a + ab * s - p).sqrMagnitude < half * half)
				{
					return false;
				}
			}
			return true;
		}

		private static Vector2 Rotate(Vector2 v, float degrees)
		{
			float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			// Clockwise seen from above (as yaw), so a positive turn bends right.
			return new Vector2(c * v.x + s * v.y, -s * v.x + c * v.y);
		}

		/// <summary>How far (in plan) a point lies from the shelf's middle line.</summary>
		public float DistanceToShelf(float x, float z)
		{
			float best = float.MaxValue;
			var p = new Vector2(x, z);
			for (int i = 0; i + 1 < Shelf.Count; i++)
			{
				var a = new Vector2(Shelf[i].x, Shelf[i].z);
				var b = new Vector2(Shelf[i + 1].x, Shelf[i + 1].z);
				Vector2 ab = b - a;
				float s = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				best = Mathf.Min(best, (a + ab * s - p).magnitude);
			}
			return best;
		}

		/// <summary>
		/// The ground at (x, z) with the shelf graded into it, given its height <paramref name="h"/> there now: level with the
		/// mouth's floor over the walkway, then easing back to the ground (filled at <see cref="FillGrade"/> out over the pool,
		/// cut at <see cref="CutGrade"/> into the wall), never by more than <see cref="MaxGive"/>. Idempotent, as
		/// <see cref="CaveSystem.Approach"/> is: a clamp into a band fixed by position alone.
		/// </summary>
		public float Grade(float x, float z, float h)
		{
			float beyond = Mathf.Max(0f, DistanceToShelf(x, z) - HalfWidth);
			float fill = beyond * FillGrade, cut = beyond * CutGrade;
			float s = Mathf.Clamp01((fill - 0.7f * MaxGive) / (0.3f * MaxGive));
			if (s >= 1f)
			{
				return h;
			}
			float widen = 1f / (1f - s * s * (3f - 2f * s) + 1e-4f);
			return Mathf.Clamp(h, Floor - fill * widen, Floor + cut * widen);
		}

		/// <summary>The plan box (world x/z) <see cref="Grade"/> can change the ground in.</summary>
		public Rect GradeArea
		{
			get
			{
				float reach = HalfWidth + MaxGive / FillGrade + 1f;
				float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
				foreach (Vector3 p in Shelf)
				{
					x0 = Mathf.Min(x0, p.x - reach);
					z0 = Mathf.Min(z0, p.z - reach);
					x1 = Mathf.Max(x1, p.x + reach);
					z1 = Mathf.Max(z1, p.z + reach);
				}
				return Rect.MinMaxRect(x0, z0, x1, z1);
			}
		}

		/// <summary>The falls of a river a cave may be set behind, and the mouth each has room for.</summary>
		public static List<CaveFallMouth> Along(RiverPath river, Func<float, float, float> ground, Func<float, float, bool> wet)
		{
			var mouths = new List<CaveFallMouth>();
			if (river == null || river.Count < 3)
			{
				return mouths;
			}
			foreach (FallLedges.FallSpan fall in FallLedges.FindFalls(river, MinDrop))
			{
				if (Find(river, fall, ground, wet, out CaveFallMouth mouth))
				{
					mouths.Add(mouth);
				}
			}
			return mouths;
		}
	}
}
#endif
