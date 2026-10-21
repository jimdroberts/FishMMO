#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a system is like as a whole (2026-10-13: "layouts are pretty predictable and no massive features"): drawn from
	/// its seed by form and size, it weights the planner's choices so two caves of a kind are not the same walk.
	/// </summary>
	public enum CaveCharacter : byte
	{
		/// <summary>A passage down to a cavern, rooms on the way: the old shape, kept for the small and the plain.</summary>
		Classic,
		/// <summary>A few enormous terraced halls, giant columns, wide ways between them.</summary>
		Halls,
		/// <summary>Tall, narrow canyon passages and chasms crossed by bridges.</summary>
		Rift,
		/// <summary>Deep: great pits wound round by a ledge, chasms and climbs down to the levels under them.</summary>
		Abyss,
		/// <summary>Many small passages that branch and meet again.</summary>
		Warren,
		/// <summary>Galleries lined with glowing crystal.</summary>
		Crystal,
		/// <summary>A mine's levels, shafts, inclines and stopes.</summary>
		Workings,
	}

	/// <summary>Rock left standing inside the void: what the field puts back after the passages and chambers are cut.</summary>
	public enum CaveSolidKind : byte
	{
		/// <summary>A natural bridge over a chasm: a flat walkway on an arched span.</summary>
		Bridge,
		/// <summary>A ramp between two terraces of a hall: a sloped walkway on a block.</summary>
		Ramp,
		/// <summary>A giant column from floor to roof, flared at both ends.</summary>
		Column,
		/// <summary>A ledge wound down the wall of a pit, walkable all the way.</summary>
		Spiral,
		/// <summary>
		/// A terrace's slab (2026-10-20, <see cref="CaveTerrace"/>): a capsule in plan along A→B (its walking top on that line,
		/// <see cref="CaveSolid.Width"/> either side), its underside <see cref="CaveSolid.Depth"/> under its top.
		/// </summary>
		Terrace,
		/// <summary>
		/// A breakdown mound (2026-10-20): fallen blocks of the roof heaped on the floor at A, <see cref="CaveSolid.Width"/>
		/// across its foot, <see cref="CaveSolid.Depth"/> high, its blocks drawn from <see cref="CaveSolid.Angle"/> (its salt).
		/// </summary>
		Rubble,
		/// <summary>
		/// A natural arch (2026-10-20): a rib of rock on a half-ellipse from its foot A to its foot B (on the floor), its
		/// underside <see cref="CaveSolid.Depth"/> over them at its crown, the rib <see cref="CaveSolid.Width"/> round.
		/// </summary>
		Arch,
	}

	/// <summary>
	/// One solid. <see cref="A"/>/<see cref="B"/>: a bridge's or ramp's walkway ends at their walking height; a column's foot
	/// and head; a spiral's axis at its bottom (y the pit's floor) and its top (y the ledge's top where it starts).
	/// </summary>
	public struct CaveSolid
	{
		public CaveSolidKind Kind;
		public Vector3 A, B;
		/// <summary>A walkway's half width; a column's radius at its waist; a spiral ledge's width out from the wall.</summary>
		public float Width;
		/// <summary>A bridge's thickness at its middle; a column's flare (radius gained at its ends, share); a spiral's pit radius.</summary>
		public float Depth;
		/// <summary>A bridge's arch (thickness gained toward its ends); a spiral's drop per radian, metres (signed: its hand).</summary>
		public float Arch;
		/// <summary>A spiral's start, the heading from its axis where its top meets the way in, degrees.</summary>
		public float Angle;
	}

	/// <summary>A chasm: a slot cut down through a floor along a line in plan, its own floor far under the one it splits.</summary>
	public struct CaveRift
	{
		/// <summary>The ends of its line, at its floor.</summary>
		public Vector3 A, B;
		/// <summary>The height of its top: a little over the floor it splits, inside that chamber's void.</summary>
		public float Top;
		/// <summary>Half its width at the top and at its floor.</summary>
		public float Width, Narrow;
	}

	/// <summary>A vertical shaft: a pit wound round by a ledge, a narrow chimney with a rope, or a mine's square shaft.</summary>
	public struct CaveShaft
	{
		/// <summary>Its floor's centre at the bottom.</summary>
		public Vector3 Bottom;
		/// <summary>The height of its roof.</summary>
		public float Top;
		public float Radius;
		/// <summary>A mine's: square in section, sides along <see cref="Yaw"/>.</summary>
		public bool Square;
		public float Yaw;
	}

	/// <summary>A crossing built rather than left standing: drawn by the dressing's pieces (their colliders are its deck).</summary>
	public enum CaveCrossingKind : byte
	{
		/// <summary>Planks on ropes.</summary>
		Rope,
		/// <summary>A mine's timber deck on trestles.</summary>
		Trestle,
	}

	public struct CaveCrossing
	{
		public CaveCrossingKind Kind;
		/// <summary>Its deck's ends at walking height (each on solid floor a little past the chasm's edge).</summary>
		public Vector3 A, B;
		public float HalfWidth;
	}

	/// <summary>A climb between two floors (as SceneClimbableKind: append-only, the same order).</summary>
	public enum CaveClimbKind : byte
	{
		Ladder,
		RopeLadder,
		Rope,
	}

	/// <summary>
	/// A ladder or a rope the layout needs to join two floors (system frame): the dressing writes it out as the scene's
	/// climbable with its pieces. <see cref="Out"/> points from the wall to the climber; <see cref="TopExit"/> is the way
	/// off at the top, onto the upper floor.
	/// </summary>
	public struct CaveClimb
	{
		public CaveClimbKind Kind;
		public Vector3 Bottom, Top, Out, TopExit;
	}

	public sealed partial class CaveSystem
	{
		/// <summary>What the system is like (<see cref="CaveCharacter"/>).</summary>
		public CaveCharacter Character;
		public readonly List<CaveSolid> Solids = new List<CaveSolid>();
		public readonly List<CaveRift> Rifts = new List<CaveRift>();
		public readonly List<CaveShaft> Shafts = new List<CaveShaft>();
		public readonly List<CaveCrossing> Crossings = new List<CaveCrossing>();
		public readonly List<CaveClimb> Climbs = new List<CaveClimb>();

		/// <summary>Bucket ids at and under these are rifts (RiftId − index), shafts, solids and climbs; the variety's hollows lie above them (HollowId).</summary>
		private const int RiftId = -2000000, ShaftId = -3000000, SolidId = -4000000, ClimbId = -5000000;

		/// <summary>
		/// Within this of a climb's line the wall's noise is stilled, fading out by <see cref="QuietFade"/>: the rock a ladder or
		/// a rope is laid against is the smooth wall it was measured from (noise a metre and a half deep left no room for a
		/// climber anywhere a climb was wanted).
		/// </summary>
		private const float QuietReach = 1.1f, QuietFade = 2.4f;

		/// <summary>How far a shaft's or a chasm's lip is rounded into the floor it opens through, metres (the union's blend).</summary>
		private const float LipBlend = 0.5f;

		/// <summary>How thick a spiral's ledge is at the wall, metres; it thins toward its lip.</summary>
		private const float LedgeThickness = 1.6f;

		/// <summary>The steepest a spiral ledge's inner lip may fall, rise over run (about 21°).</summary>
		public const float LedgeGrade = 0.38f;

		/// <summary>The roughest any chamber is made (its <see cref="CaveChamber.Rough"/>), for the buckets' reach.</summary>
		private float MaxRough
		{
			get
			{
				float m = 1f;
				foreach (CaveChamber c in Chambers)
				{
					m = Mathf.Max(m, c.Rough);
				}
				return m;
			}
		}

		/// <summary>A rift's void (negative inside): a slot along its line, its sides drawing in from its top to its floor.</summary>
		private static float RiftField(in CaveRift rift, Vector3 p, out float floor)
		{
			var a = new Vector2(rift.A.x, rift.A.z);
			var ab = new Vector2(rift.B.x - rift.A.x, rift.B.z - rift.A.z);
			var ap = new Vector2(p.x, p.z) - a;
			float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
			float across = (ap - ab * t).magnitude;
			floor = Mathf.Lerp(rift.A.y, rift.B.y, t);
			// Straight sides from its floor to its top: a ladder leant on one stands clear of it all the way up.
			float h = Mathf.Clamp01((p.y - floor) / Mathf.Max(0.5f, rift.Top - floor));
			float half = Mathf.Lerp(rift.Narrow, rift.Width, h);
			return Mathf.Max(across - half, Mathf.Max(floor - p.y, p.y - rift.Top));
		}

		/// <summary>A shaft's void (negative inside): round, or square along its yaw.</summary>
		private static float ShaftField(in CaveShaft shaft, Vector3 p)
		{
			float dx = p.x - shaft.Bottom.x, dz = p.z - shaft.Bottom.z;
			float radial;
			if (shaft.Square)
			{
				float r = shaft.Yaw * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
				radial = Mathf.Max(Mathf.Abs(dx * c - dz * s), Mathf.Abs(dx * s + dz * c));
			}
			else
			{
				radial = Mathf.Sqrt(dx * dx + dz * dz);
			}
			return Mathf.Max(radial - shaft.Radius, Mathf.Max(shaft.Bottom.y - p.y, p.y - shaft.Top));
		}

		/// <summary>
		/// A solid's distance (negative inside the rock it leaves), and the walking height on it over the point (a bridge's or
		/// ramp's deck, a spiral's ledge; float.MaxValue for a column, which has none).
		/// </summary>
		private static float SolidField(in CaveSolid s, Vector3 p, out float top)
		{
			top = float.MaxValue;
			switch (s.Kind)
			{
				case CaveSolidKind.Terrace:
					return TerraceField(s, p, out top);
				case CaveSolidKind.Rubble:
					return RubbleField(s, p);
				case CaveSolidKind.Arch:
					return ArchField(s, p);
				case CaveSolidKind.Bridge:
				case CaveSolidKind.Ramp:
				{
					var d = new Vector2(s.B.x - s.A.x, s.B.z - s.A.z);
					float length = Mathf.Max(1e-3f, d.magnitude);
					Vector2 dir = d / length;
					var ap = new Vector2(p.x - s.A.x, p.z - s.A.z);
					float along = Vector2.Dot(ap, dir), across = Mathf.Abs(ap.x * dir.y - ap.y * dir.x);
					float t = Mathf.Clamp01(along / length);
					top = Mathf.Lerp(s.A.y, s.B.y, t);
					float under = s.Kind == CaveSolidKind.Bridge
						? top - (s.Depth + s.Arch * (2f * t - 1f) * (2f * t - 1f))
						: Mathf.Min(s.A.y, s.B.y) - 3f;
					float plan = Mathf.Max(across - s.Width, Mathf.Max(-along, along - length));
					return Mathf.Max(plan, Mathf.Max(p.y - top, under - p.y));
				}
				case CaveSolidKind.Column:
				{
					float h = Mathf.Clamp01((p.y - s.A.y) / Mathf.Max(0.5f, s.B.y - s.A.y));
					float flare = (2f * h - 1f) * (2f * h - 1f);
					float r = s.Width * (1f + s.Depth * flare * flare);
					float radial = new Vector2(p.x - s.A.x, p.z - s.A.z).magnitude;
					return Mathf.Max(radial - r, Mathf.Max(s.A.y - 1.5f - p.y, p.y - (s.B.y + 1.5f)));
				}
				default:
				{
					// The ledge winds down from its top at its start's heading: the turn under the point that it stands nearest.
					float dx = p.x - s.A.x, dz = p.z - s.A.z;
					float radial = Mathf.Sqrt(dx * dx + dz * dz);
					float pitch = Mathf.Max(0.1f, Mathf.Abs(s.Arch)), hand = s.Arch < 0f ? -1f : 1f;
					float theta = Mathf.Atan2(dx, dz) - s.Angle * Mathf.Deg2Rad;
					float phi = Mathf.Repeat(hand * theta, 2f * Mathf.PI);
					float last = (s.B.y - s.A.y) / pitch;
					float inner = s.Depth - s.Width;
					float best = 1e4f;
					int k0 = Mathf.FloorToInt(((s.B.y - p.y) / pitch - phi) / (2f * Mathf.PI));
					for (int k = k0; k <= k0 + 1; k++)
					{
						float at = phi + 2f * Mathf.PI * k;
						// A little behind its start too, level with it: the way in meets it on a floor, whatever its angle.
						if (at < -0.2f || at > last)
						{
							continue;
						}
						at = Mathf.Max(at, 0f);
						float ledge = s.B.y - at * pitch;
						float thick = LedgeThickness * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((radial - inner) / Mathf.Max(0.5f, s.Width)));
						float d = Mathf.Max(Mathf.Max(inner - radial, radial - (s.Depth + 0.5f)), Mathf.Max(p.y - ledge, ledge - thick - p.y));
						if (d < best)
						{
							best = d;
							top = ledge;
						}
					}
					return best;
				}
			}
		}

		/// <summary>The solids near a point, unioned (negative inside rock they leave); 1e4 where none is near.</summary>
		private float Solid(Vector3 p, out float top) => Solid(p, out top, out _);

		/// <summary>As above, with the nearest solid's kind (how rough its sides are, and whether a climb stills them).</summary>
		private float Solid(Vector3 p, out float top, out CaveSolidKind kind)
		{
			top = float.MaxValue;
			kind = CaveSolidKind.Bridge;
			if (Solids.Count == 0 || buckets == null)
			{
				return 1e4f;
			}
			if (!buckets.TryGetValue(BucketKey(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.y / Bucket), Mathf.FloorToInt(p.z / Bucket)), out List<int> ids))
			{
				return 1e4f;
			}
			float d = 1e4f;
			foreach (int id in ids)
			{
				if (id > SolidId || id <= ClimbId)
				{
					continue;
				}
				CaveSolid solid = Solids[SolidId - id];
				float s = SolidField(solid, p, out float t);
				if (s < d)
				{
					d = s;
					top = t;
					kind = solid.Kind;
				}
			}
			return d;
		}

		/// <summary>
		/// Whether a point is within <paramref name="margin"/> of a chasm (in plan, and above its floor) or of a climb's line:
		/// what a passage, a loop or a column laid after them keeps away from, so none cuts across a chasm without a bridge or
		/// crowds a climb.
		/// </summary>
		public bool NearChasmOrClimb(Vector3 p, float margin)
		{
			foreach (CaveRift rift in Rifts)
			{
				var a = new Vector2(rift.A.x, rift.A.z);
				var ab = new Vector2(rift.B.x - rift.A.x, rift.B.z - rift.A.z);
				var ap = new Vector2(p.x, p.z) - a;
				float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				if ((ap - ab * t).magnitude < rift.Width + margin && p.y > Mathf.Min(rift.A.y, rift.B.y) - margin && p.y < rift.Top + margin)
				{
					return true;
				}
			}
			foreach (CaveClimb c in Climbs)
			{
				Vector3 ab = c.Top - c.Bottom, ap = p - c.Bottom;
				float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				if ((ap - ab * t).magnitude < 2.5f + margin)
				{
					return true;
				}
			}
			// The basins, trenches and raises the variety pass sank (2026-10-20).
			if (Hollows.Count > 0 && NearHollow(p, margin))
			{
				return true;
			}
			// The rock left standing: a way laid across a ramp or a bridge, or through a column or a pit, ran into it.
			foreach (CaveSolid s in Solids)
			{
				SolidBounds(s, out Vector3 lo, out Vector3 hi);
				lo -= Vector3.one * margin;
				hi += Vector3.one * (margin + 3f);
				if (p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y && p.z >= lo.z && p.z <= hi.z)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>How much a point's wall noise is stilled by a climb near it, 0 .. 1.</summary>
		private float Quiet(Vector3 p)
		{
			if (Climbs.Count == 0 || buckets == null)
			{
				return 0f;
			}
			if (!buckets.TryGetValue(BucketKey(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.y / Bucket), Mathf.FloorToInt(p.z / Bucket)), out List<int> ids))
			{
				return 0f;
			}
			float quiet = 0f;
			foreach (int id in ids)
			{
				if (id > ClimbId)
				{
					continue;
				}
				CaveClimb c = Climbs[ClimbId - id];
				Vector3 ab = c.Top - c.Bottom, ap = p - c.Bottom;
				float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				float d = (ap - ab * t).magnitude;
				float q = 1f - Mathf.Clamp01((d - QuietReach) / (QuietFade - QuietReach));
				quiet = Mathf.Max(quiet, q * q * (3f - 2f * q));
			}
			return quiet;
		}

		/// <summary>The bare void with the solids put back: what the shell and the holes are measured from.</summary>
		private float SmoothWithSolids(Vector3 p, out float radius, out float floor, out float rough)
		{
			float d = Tube(p, out radius, out floor, out rough);
			if (d > 1e3f)
			{
				return d;
			}
			float s = Solid(p, out float top);
			if (s < 1e3f)
			{
				d = Mathf.Max(d, -s);
				// On a deck or a ledge, its top is the floor the wall noise keeps off.
				if (top != float.MaxValue && p.y >= top - 0.2f && s < 2f)
				{
					floor = Mathf.Max(floor, top);
				}
			}
			return d;
		}

		/// <summary>Grows the buckets by the features' reach (called from <see cref="Prepare"/>).</summary>
		private void AddFeaturesToBuckets(float pad)
		{
			for (int r = 0; r < Rifts.Count; r++)
			{
				CaveRift rift = Rifts[r];
				Vector3 lo = Vector3.Min(rift.A, rift.B), hi = Vector3.Max(rift.A, rift.B);
				hi.y = rift.Top;
				AddToBuckets(lo - Vector3.one * (rift.Width + pad), hi + Vector3.one * (rift.Width + pad), RiftId - r);
			}
			for (int s = 0; s < Shafts.Count; s++)
			{
				CaveShaft shaft = Shafts[s];
				float reach = shaft.Radius * 1.415f + pad;
				AddToBuckets(new Vector3(shaft.Bottom.x - reach, shaft.Bottom.y - pad, shaft.Bottom.z - reach),
					new Vector3(shaft.Bottom.x + reach, shaft.Top + pad, shaft.Bottom.z + reach), ShaftId - s);
			}
			for (int s = 0; s < Solids.Count; s++)
			{
				SolidBounds(Solids[s], out Vector3 lo, out Vector3 hi);
				AddToBuckets(lo - Vector3.one * pad, hi + Vector3.one * pad, SolidId - s);
			}
			for (int c = 0; c < Climbs.Count; c++)
			{
				Vector3 lo = Vector3.Min(Climbs[c].Bottom, Climbs[c].Top), hi = Vector3.Max(Climbs[c].Bottom, Climbs[c].Top);
				AddToBuckets(lo - Vector3.one * QuietFade, hi + Vector3.one * QuietFade, ClimbId - c);
			}
			for (int h = 0; h < Hollows.Count; h++)
			{
				HollowBounds(Hollows[h], out Vector3 lo, out Vector3 hi);
				AddToBuckets(lo - Vector3.one * pad, hi + Vector3.one * pad, HollowId - h);
			}
		}

		private static void SolidBounds(in CaveSolid s, out Vector3 lo, out Vector3 hi)
		{
			switch (s.Kind)
			{
				case CaveSolidKind.Terrace:
				case CaveSolidKind.Rubble:
				case CaveSolidKind.Arch:
					NewSolidBounds(s, out lo, out hi);
					return;
				case CaveSolidKind.Column:
				{
					float r = s.Width * (1f + s.Depth);
					lo = new Vector3(s.A.x - r, s.A.y - 1.5f, s.A.z - r);
					hi = new Vector3(s.A.x + r, s.B.y + 1.5f, s.A.z + r);
					return;
				}
				case CaveSolidKind.Spiral:
				{
					float r = s.Depth + 0.5f;
					lo = new Vector3(s.A.x - r, s.A.y - LedgeThickness, s.A.z - r);
					hi = new Vector3(s.A.x + r, s.B.y, s.A.z + r);
					return;
				}
				default:
				{
					lo = Vector3.Min(s.A, s.B) - new Vector3(s.Width, s.Depth + s.Arch + 3f, s.Width);
					hi = Vector3.Max(s.A, s.B) + new Vector3(s.Width, 0.5f, s.Width);
					return;
				}
			}
		}

		/// <summary>
		/// The void's boxes in the world, a metre proud (CaveVisibility: inside one, the camera is in the cave): each passage
		/// in runs of a few nodes, each chamber, chasm and shaft.
		/// </summary>
		public Bounds[] VoidBoxes()
		{
			var boxes = new List<Bounds>();
			void Grow(ref Bounds b, Vector3 p, Vector3 reach)
			{
				b.Encapsulate(p - reach);
				b.Encapsulate(p + reach);
			}
			foreach (CavePassage passage in Passages)
			{
				for (int i = 0; i < passage.Nodes.Count; i += 5)
				{
					var b = new Bounds(passage.Nodes[i].Centre + Origin, Vector3.zero);
					for (int k = i; k <= Mathf.Min(i + 5, passage.Nodes.Count - 1); k++)
					{
						CaveNode n = passage.Nodes[k];
						float r = n.Radius + 1f;
						Grow(ref b, n.Centre + Origin, new Vector3(r, n.Radius * Mathf.Max(1f, Flat * n.Height) + 1f, r));
					}
					boxes.Add(b);
				}
			}
			foreach (CaveChamber c in Chambers)
			{
				float r = Mathf.Max(c.Radii.x, c.Radii.z) + 1f;
				var b = new Bounds(c.Centre + Origin, Vector3.zero);
				Grow(ref b, c.Centre + Origin, new Vector3(r, c.Radii.y + Mathf.Abs(c.Slope) * r + 1f, r));
				boxes.Add(b);
			}
			foreach (CaveRift rift in Rifts)
			{
				var b = new Bounds(rift.A + Origin, Vector3.zero);
				Grow(ref b, rift.A + Origin, Vector3.one * (rift.Width + 1f));
				Grow(ref b, rift.B + Origin, Vector3.one * (rift.Width + 1f));
				b.Encapsulate(new Vector3(rift.A.x, rift.Top + 1f, rift.A.z) + Origin);
				boxes.Add(b);
			}
			foreach (CaveShaft shaft in Shafts)
			{
				float r = shaft.Radius * 1.415f + 1f;
				var b = new Bounds(shaft.Bottom + Origin, Vector3.zero);
				Grow(ref b, shaft.Bottom + Origin, new Vector3(r, 1f, r));
				b.Encapsulate(new Vector3(shaft.Bottom.x, shaft.Top + 1f, shaft.Bottom.z) + Origin);
				boxes.Add(b);
			}
			// A skylight's shaft (2026-10-20), from the floor under it to a little under the ground: one standing in it is in the
			// cave, and its far walls stay drawn. Not the opening on the ground, where one stands outside.
			foreach (CaveMouth m in Mouths)
			{
				if (m.Kind != CaveMouthKind.Skylight)
				{
					continue;
				}
				float r = m.Radius * 1.5f + 1f;
				var b = new Bounds(m.Floor + Origin, Vector3.zero);
				Grow(ref b, m.Floor + Origin, new Vector3(r, 1f, r));
				b.Encapsulate(new Vector3(m.Floor.x, Mathf.Max(m.Floor.y + 2f, m.Landing.y - 1.5f), m.Floor.z) + Origin);
				boxes.Add(b);
			}
			foreach (CaveHollow h in Hollows)
			{
				HollowBounds(h, out Vector3 lo, out Vector3 hi);
				var b = new Bounds();
				b.SetMinMax(lo + Origin - Vector3.one, hi + Origin + Vector3.one);
				boxes.Add(b);
			}
			return boxes.ToArray();
		}

		/// <summary>Grows a box by the features (for <see cref="Extent"/>).</summary>
		private void EncapsulateFeatures(ref Bounds b, float grow)
		{
			foreach (CaveRift rift in Rifts)
			{
				Vector3 top = new Vector3(0f, rift.Top - Mathf.Min(rift.A.y, rift.B.y), 0f);
				b.Encapsulate(Vector3.Min(rift.A, rift.B) - Vector3.one * (rift.Width + grow));
				b.Encapsulate(Vector3.Max(rift.A, rift.B) + top + Vector3.one * (rift.Width + grow));
			}
			foreach (CaveShaft shaft in Shafts)
			{
				float r = shaft.Radius * 1.415f + grow;
				b.Encapsulate(new Vector3(shaft.Bottom.x - r, shaft.Bottom.y - grow, shaft.Bottom.z - r));
				b.Encapsulate(new Vector3(shaft.Bottom.x + r, shaft.Top + grow, shaft.Bottom.z + r));
			}
			foreach (CaveHollow h in Hollows)
			{
				HollowBounds(h, out Vector3 lo, out Vector3 hi);
				b.Encapsulate(lo - Vector3.one * grow);
				b.Encapsulate(hi + Vector3.one * grow);
			}
		}
	}
}
#endif
