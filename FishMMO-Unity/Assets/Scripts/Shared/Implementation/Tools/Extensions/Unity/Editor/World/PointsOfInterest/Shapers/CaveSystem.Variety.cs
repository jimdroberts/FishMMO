#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A system's personality (2026-10-20: "We would like more random terraces, pits, etc in caves too. Realism and
	/// deterministic randomness is good too. We want caves to feel unique"): a mix of these drawn from its seed on top of its
	/// <see cref="CaveCharacter"/>, weighted by form and size (<see cref="CaveSystem.TraitsFor"/>), so two caves of one
	/// character are still not alike. Each is a process real caves are shaped by, and lays the features it leaves.
	/// </summary>
	[Flags]
	public enum CaveTrait : ushort
	{
		None = 0,
		/// <summary>
		/// Stepped floors: benches and steps along the walls of rooms and wide passages, ramped up from the floor (old
		/// stream levels cut down through their own fill, a floor broken into slabs along its bedding).
		/// </summary>
		Terraced = 1 << 0,
		/// <summary>Potholes scoured into the floors, vadose shafts dropping off them, great pits wound round by a ledge.</summary>
		Pitted = 1 << 1,
		/// <summary>Breakdown: domes where the roof fell in, the fallen blocks heaped in a mound under each, dead ends choked by collapse.</summary>
		Breakdown = 1 << 2,
		/// <summary>Keyhole passages: a vadose trench cut down through the floor of a phreatic tube, the way running in it.</summary>
		Fissured = 1 << 3,
		/// <summary>Galleries: shelves high on the walls of tall chambers (an older, higher level of the system), reached by a climb.</summary>
		Galleried = 1 << 4,
		/// <summary>Rimstone: flights of shallow gour pools stepping down a floor.</summary>
		Rimstone = 1 << 5,
		/// <summary>A maze: many short side passages, dead ends and stacked alcoves off the ways.</summary>
		Labyrinthine = 1 << 6,
		/// <summary>Dripstone columns, stalagmite and stalactite grown together, standing in the rooms.</summary>
		Pillared = 1 << 7,
		/// <summary>Natural arches: ribs of rock left spanning a hall over the way across it.</summary>
		Arched = 1 << 8,
		/// <summary>A lava tube's benches: shelves along its walls where the flow's level stood as it drained.</summary>
		Benched = 1 << 9,
		/// <summary>An ice cave's cold trap: its deepest chamber's floor sunk in a broad basin the cold air pools in.</summary>
		ColdSink = 1 << 10,
		/// <summary>A mine's stopes worked their own ways: pillars left standing, raises driven up from the back, ore chambers.</summary>
		Stoped = 1 << 11,
		/// <summary>A mine's old drifts fallen in: chokes of rubble at their ends and spurs that end in one.</summary>
		Collapsed = 1 << 12,
		/// <summary>A mine's winzes sunk from its drifts, the lowest flooded where it lies under the drainage adit's level.</summary>
		Sumped = 1 << 13,
	}

	/// <summary>A void the layout adds to the field beside the passages and chambers (<see cref="CaveSystem.Hollows"/>).</summary>
	public enum CaveHollowKind : byte
	{
		/// <summary>A basin sunk in a floor, its sides at a walk (<see cref="CaveSystem.BowlGrade"/>), its bottom flat.</summary>
		Bowl,
		/// <summary>A breakdown dome: a vault raised over part of a chamber where its roof fell in.</summary>
		Dome,
		/// <summary>A keyhole's trench: a slot cut down through a passage's floor along a line, ramped in and out at its ends.</summary>
		Trench,
		/// <summary>A mine's raise: a shaft driven up from a stope's back, ending in the rock.</summary>
		Raise,
		/// <summary>
		/// An aven: a round shaft rising from a roof into the rock, narrowing as it goes, where water once fell in down a joint
		/// (2026-10-20). Nothing is walked in it; nothing can fall into it.
		/// </summary>
		Aven,
	}

	/// <summary>
	/// One added void (system frame). Bowl: <see cref="A"/> its rim's centre on the floor, <see cref="Radius"/> the rim's,
	/// <see cref="Depth"/> its bottom under <see cref="A"/>, <see cref="Top"/> the height its void reaches up to (a little
	/// over the floor, so it opens cleanly through a sloping one). Dome: <see cref="A"/> on the floor under its crown,
	/// <see cref="Radius"/> across, <see cref="Depth"/> its half-height (its crown stands 1.4 of it over the floor),
	/// <see cref="Top"/> the height under which it adds nothing (the floor and the walls' feet are the chamber's). Trench:
	/// <see cref="A"/>/<see cref="B"/> its ends on the passage's floor, <see cref="Radius"/> its half width, <see cref="Depth"/>
	/// how far under that floor its bottom lies between its ramps. Raise: <see cref="A"/> its foot's centre, <see cref="Top"/>
	/// its end, <see cref="Radius"/> its half side, square along <see cref="Yaw"/>. Aven: <see cref="A"/> its foot's centre (a
	/// little into the roof it rises from), <see cref="Top"/> its end, <see cref="Radius"/> at its foot (0.6 of it at its top).
	/// </summary>
	public struct CaveHollow
	{
		public CaveHollowKind Kind;
		public Vector3 A, B;
		public float Radius, Depth, Top, Yaw;
	}

	/// <summary>What a terrace is (<see cref="CaveTerrace"/>).</summary>
	public enum CaveTerraceKind : byte
	{
		/// <summary>A low step in a floor (0.5–1.4 m), ramped.</summary>
		Step,
		/// <summary>A bench along a wall (1.2–3 m; a lava tube's along its passages), ramped, or climbed where tall.</summary>
		Bench,
		/// <summary>A gallery: a shelf high on a tall chamber's wall (3.5–8 m), overhanging the floor, reached by a climb.</summary>
		Gallery,
	}

	/// <summary>
	/// A terrace the layout raised (system frame): its slab, a <see cref="CaveSolidKind.Terrace"/> in
	/// <see cref="CaveSystem.Solids"/> (its walking top runs from the slab's A to its B at <see cref="CaveSolid.Width"/> either
	/// side of that line, its inner edge the side toward the room), and the way up onto it: a
	/// <see cref="CaveSolidKind.Ramp"/> (index in Solids) or a climb (index in <see cref="CaveSystem.Climbs"/>), −1 for none.
	/// For the dressing: formations on its top and at its foot, glow along its lip, its ramp kept clear.
	/// </summary>
	public struct CaveTerrace
	{
		public CaveTerraceKind Kind;
		public int Solid, Ramp, Climb;
		/// <summary>How far its top stands over the floor at its inner edge, metres.</summary>
		public float Height;
		/// <summary>How wide its walking top is from its inner edge to the foot of the wall behind it (at the floor's level), metres.</summary>
		public float Walk;
		/// <summary>The way out from its inner edge, into the room or passage it stands in (horizontal, unit).</summary>
		public Vector3 Out;
	}

	/// <summary>What a pit is (<see cref="CavePit"/>).</summary>
	public enum CavePitKind : byte
	{
		/// <summary>A pothole scoured into a floor: a small basin, walked into and out of.</summary>
		Pothole,
		/// <summary>A gour: one of a flight of shallow rimstone pools.</summary>
		Gour,
		/// <summary>A sinkhole: a broad basin where the floor settled into a void under it.</summary>
		Sinkhole,
		/// <summary>An ice cave's cold trap: a broad basin in its deepest chamber, where the cold air pools and the ice is thickest.</summary>
		ColdTrap,
		/// <summary>A vadose shaft dropping off a floor, a rope or ladder down its wall.</summary>
		Shaft,
		/// <summary>A great pit in a hall's floor, a ledge wound down its wall to its bottom.</summary>
		Ledged,
		/// <summary>A mine's winze: a square shaft sunk at a drift's end, a ladder down it; flooded when <see cref="CavePit.Water"/> is set.</summary>
		Winze,
	}

	/// <summary>
	/// A pit the layout sank (system frame), and its way out (2026-10-13: no fall damage, but nothing can be fallen into
	/// without one). Basins (pothole, gour, sinkhole, cold trap) are a <see cref="CaveHollowKind.Bowl"/> in
	/// <see cref="CaveSystem.Hollows"/> (<see cref="Hole"/>), their sides walked; shafts and winzes a
	/// <see cref="CaveShaft"/> (<see cref="Hole"/> in Shafts) with a climb (<see cref="Way"/> in Climbs); a ledged pit a
	/// shaft with a <see cref="CaveSolidKind.Spiral"/> (<see cref="Way"/> in Solids).
	/// </summary>
	public struct CavePit
	{
		public CavePitKind Kind;
		/// <summary>Its rim's centre, on the floor it opens in.</summary>
		public Vector3 Rim;
		public float Radius;
		/// <summary>Its bottom's height.</summary>
		public float Bottom;
		public int Hole, Way;
		/// <summary>The water standing in it, world y; NaN when dry (a mine's sump under its drainage level).</summary>
		public float Water;
	}

	public sealed partial class CaveSystem
	{
		/// <summary>The system's personality (<see cref="CaveTrait"/>): what its <see cref="Variety"/> pass lays.</summary>
		public CaveTrait Traits;
		/// <summary>The voids added to the field beside its passages and chambers: basins, domes, keyhole trenches, raises.</summary>
		public readonly List<CaveHollow> Hollows = new List<CaveHollow>();
		/// <summary>The terraces raised in its rooms and passages, with their ways up (for the dressing).</summary>
		public readonly List<CaveTerrace> Terraces = new List<CaveTerrace>();
		/// <summary>The pits sunk in its floors, with their ways out (for the dressing).</summary>
		public readonly List<CavePit> Pits = new List<CavePit>();

		/// <summary>Bucket ids at and under this (HollowId − index, above <see cref="RiftId"/>) are hollows.</summary>
		private const int HollowId = -1000000;

		/// <summary>The grade a basin's sides fall at, rise over run (about 32°: walked down into and up out of).</summary>
		public const float BowlGrade = 0.62f;
		/// <summary>The grade a keyhole's trench ramps down into and up out of at its ends, rise over run (20°).</summary>
		public const float TrenchGrade = 0.36f;
		/// <summary>The grade the layout ramps a terrace at, rise over run (about 17°, a hall's ramps'), and the most it allows.</summary>
		public const float TerraceRampGrade = 0.3f, TerraceRampMost = 0.36f;
		/// <summary>The half width of the lane kept clear along every way (the dressing's own), metres.</summary>
		public const float WayLane = 1.4f;

		// ── Personality ───────────────────────────────────────────

		private const int TraitCount = 14;

		/// <summary>
		/// A system's traits from its seed (hashed: a fresh stream's first draw follows its seed), by form, size and character:
		/// each trait on with its own odds, then at least two (one for a grotto or a sea cave) and at most three, four or five
		/// (by size) kept,
		/// the nearest misses added and the narrowest hits dropped, so a cave always has a personality and never every one.
		/// </summary>
		public static CaveTrait TraitsFor(CaveForm form, int size, CaveCharacter character, int seed)
		{
			float[] odds = TraitOdds(form, Mathf.Clamp(size, 0, 2), character);
			uint s = unchecked((uint)seed);
			var margin = new float[TraitCount];
			CaveTrait traits = CaveTrait.None;
			int count = 0;
			for (int i = 0; i < TraitCount; i++)
			{
				margin[i] = odds[i] > 0f ? odds[i] - Unit(s, 0x7A110u + (uint)i) : float.NegativeInfinity;
				if (margin[i] > 0f)
				{
					traits |= (CaveTrait)(1 << i);
					count++;
				}
			}
			bool awash = form == CaveForm.Grotto || form == CaveForm.SeaCave;
			int least = awash || form == CaveForm.Mine ? 1 : 2, most = awash ? 2 : form == CaveForm.Mine ? 3 : 3 + Mathf.Clamp(size, 0, 2);
			while (count < least)
			{
				int best = -1;
				for (int i = 0; i < TraitCount; i++)
				{
					if ((traits & (CaveTrait)(1 << i)) == 0 && !float.IsNegativeInfinity(margin[i]) && (best < 0 || margin[i] > margin[best]))
					{
						best = i;
					}
				}
				if (best < 0)
				{
					break;
				}
				traits |= (CaveTrait)(1 << best);
				count++;
			}
			while (count > most)
			{
				int worst = -1;
				for (int i = 0; i < TraitCount; i++)
				{
					if ((traits & (CaveTrait)(1 << i)) != 0 && (worst < 0 || margin[i] < margin[worst]))
					{
						worst = i;
					}
				}
				traits &= ~(CaveTrait)(1 << worst);
				count--;
			}
			return traits;
		}

		/// <summary>
		/// Each trait's odds (in <see cref="CaveTrait"/>'s bit order) for a form, size and character: limestone caves the whole
		/// karst range, ice caves smoother (cold traps, few chasms), lava tubes their benches, breakdown and arches (no
		/// solution features), awash caves little but breakdown and sea arches, mines only their workings.
		/// </summary>
		private static float[] TraitOdds(CaveForm form, int size, CaveCharacter character)
		{
			// Terraced, Pitted, Breakdown, Fissured, Galleried, Rimstone, Labyrinthine, Pillared, Arched, Benched, ColdSink, Stoped, Collapsed, Sumped
			float[] o;
			switch (form)
			{
				case CaveForm.Mine: o = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.65f, 0.5f, 0.55f }; break;
				case CaveForm.LavaTube: o = new[] { 0f, 0.15f, 0.45f, 0f, 0.15f, 0f, 0.1f, 0.05f, 0.3f, 0.85f, 0f, 0f, 0f, 0f }; break;
				case CaveForm.IceCave: o = new[] { 0.3f, 0.3f, 0.25f, 0.15f, 0.2f, 0f, 0.2f, 0.3f, 0.2f, 0f, 0.75f, 0f, 0f, 0f }; break;
				case CaveForm.Grotto: o = new[] { 0.3f, 0f, 0.2f, 0f, 0f, 0.35f, 0f, 0.25f, 0f, 0f, 0f, 0f, 0f, 0f }; break;
				case CaveForm.SeaCave: o = new[] { 0.2f, 0f, 0.35f, 0f, 0f, 0f, 0f, 0f, 0.35f, 0f, 0f, 0f, 0f, 0f }; break;
				default: o = new[] { 0.45f, 0.45f, 0.35f, 0.3f, 0.3f, 0.3f, 0.3f, 0.35f, 0.25f, 0f, 0f, 0f, 0f, 0f }; break;
			}
			switch (character)
			{
				case CaveCharacter.Halls: o[8] += 0.25f; o[4] += 0.25f; o[7] += 0.15f; break;
				case CaveCharacter.Rift: o[3] += 0.4f; o[0] += 0.1f; break;
				case CaveCharacter.Abyss: o[1] += 0.4f; o[2] += 0.1f; break;
				case CaveCharacter.Warren: o[6] += 0.45f; o[2] += 0.1f; break;
				case CaveCharacter.Crystal: o[7] += 0.2f; o[5] += 0.2f; break;
				case CaveCharacter.Classic: o[0] += 0.1f; o[2] += 0.1f; break;
			}
			if (size <= 0)
			{
				// A small cave has no tall chambers for a gallery or room for an arch.
				o[4] *= 0.3f;
				o[8] *= 0.5f;
			}
			return o;
		}

		/// <summary>Whether a system has a trait.</summary>
		public bool Has(CaveTrait trait) => (Traits & trait) != 0;

		/// <summary>
		/// Whether a mine's passage is timbered (sets of posts and caps) at a distance along it, or bare rock: within 25 m of
		/// its portal always (the weathered rock there needs it), past that in stretches of 18–36 m drawn from the seed, more
		/// of them in a mine whose drifts have fallen in (<see cref="CaveTrait.Collapsed"/>). Pure: for the dressing to set its
		/// timber by. False for anything but a mine.
		/// </summary>
		public bool Timbered(int passage, float along)
		{
			if (Form != CaveForm.Mine)
			{
				return false;
			}
			if (passage == 0 && along < 25f)
			{
				return true;
			}
			uint h = Mix(unchecked((uint)Seed), 0x7111u + (uint)passage * 0x9E37u);
			float stretch = Mathf.Lerp(18f, 36f, Unit(h, 1));
			int index = Mathf.FloorToInt((along + stretch * Unit(h, 2)) / stretch);
			return Unit(h, 3u + (uint)index) < (Has(CaveTrait.Collapsed) ? 0.65f : 0.45f);
		}

		private static uint Mix(uint h, uint salt)
		{
			h ^= salt * 0x9E3779B9u;
			h ^= h >> 16;
			h *= 0x7FEB352Du;
			h ^= h >> 15;
			h *= 0x846CA68Bu;
			h ^= h >> 16;
			return h;
		}

		// ── The hollows' field ────────────────────────────────────

		/// <summary>
		/// A hollow's void (negative inside), and the floor the wall noise is kept off near it: a basin's and a trench's the
		/// floor they are sunk in (their sides stay smooth, walkable), a dome's its chamber's, a raise's none (as rough as the rock).
		/// </summary>
		private static float HollowField(in CaveHollow h, Vector3 p, out float floor)
		{
			switch (h.Kind)
			{
				case CaveHollowKind.Bowl:
				{
					float r = new Vector2(p.x - h.A.x, p.z - h.A.z).magnitude;
					float bottom = h.A.y - h.Depth;
					float flat = Mathf.Max(0.3f * h.Radius, h.Radius - h.Depth / BowlGrade);
					float side = bottom + Mathf.Max(0f, r - flat) * BowlGrade;
					floor = h.A.y;
					return Mathf.Max(Mathf.Max(bottom - p.y, (side - p.y) * BowlCos), p.y - h.Top);
				}
				case CaveHollowKind.Dome:
				{
					var e = new Vector3((p.x - h.A.x) / h.Radius, (p.y - (h.A.y + 0.4f * h.Depth)) / h.Depth, (p.z - h.A.z) / h.Radius);
					var e2 = new Vector3(e.x / h.Radius, e.y / h.Depth, e.z / h.Radius);
					float k0 = e.magnitude, k1 = Mathf.Max(1e-6f, e2.magnitude);
					floor = h.A.y;
					return Mathf.Max(k0 * (k0 - 1f) / k1, h.Top - p.y);
				}
				case CaveHollowKind.Trench:
				{
					var ab = new Vector2(h.B.x - h.A.x, h.B.z - h.A.z);
					float length = Mathf.Max(1e-3f, ab.magnitude);
					Vector2 dir = ab / length;
					var ap = new Vector2(p.x - h.A.x, p.z - h.A.z);
					float along = Vector2.Dot(ap, dir), across = Mathf.Abs(ap.x * dir.y - ap.y * dir.x);
					float t = Mathf.Clamp01(along / length);
					float line = Mathf.Lerp(h.A.y, h.B.y, t);
					float s = Mathf.Clamp(along, 0f, length);
					float ramp = Mathf.Clamp01(Mathf.Min(s, length - s) * TrenchGrade / Mathf.Max(0.1f, h.Depth));
					float bottom = line - h.Depth * ramp;
					floor = line;
					float plan = Mathf.Max(across - h.Radius, Mathf.Max(-along, along - length));
					return Mathf.Max(plan, Mathf.Max(bottom - p.y, p.y - (line + TrenchCap)));
				}
				case CaveHollowKind.Aven:
				{
					float t = Mathf.Clamp01((p.y - h.A.y) / Mathf.Max(0.5f, h.Top - h.A.y));
					float radial = new Vector2(p.x - h.A.x, p.z - h.A.z).magnitude - h.Radius * Mathf.Lerp(1f, 0.6f, t);
					floor = float.MinValue;
					return Mathf.Max(radial, Mathf.Max(h.A.y - p.y, p.y - h.Top));
				}
				default:
				{
					float dx = p.x - h.A.x, dz = p.z - h.A.z;
					float r = h.Yaw * Mathf.Deg2Rad, sn = Mathf.Sin(r), cs = Mathf.Cos(r);
					float radial = Mathf.Max(Mathf.Abs(dx * cs - dz * sn), Mathf.Abs(dx * sn + dz * cs));
					floor = float.MinValue;
					return Mathf.Max(radial - h.Radius, Mathf.Max(h.A.y - p.y, p.y - h.Top));
				}
			}
		}

		/// <summary>The cosine of <see cref="BowlGrade"/>'s angle: a basin's side measured square to it.</summary>
		private static readonly float BowlCos = 1f / Mathf.Sqrt(1f + BowlGrade * BowlGrade);

		/// <summary>How far over its floor line a trench's void reaches, metres: it opens cleanly through the floor.</summary>
		private const float TrenchCap = 0.3f;

		/// <summary>How a hollow meets the void round it: a basin's rim and a trench's or raise's lip crisp, a dome softly.</summary>
		private float HollowBlend(CaveHollowKind kind) => kind == CaveHollowKind.Dome ? 1.5f * blend : kind == CaveHollowKind.Aven ? blend : kind == CaveHollowKind.Bowl ? 1.5f * LipBlend : LipBlend;

		private static float HollowRadius(in CaveHollow h) => h.Kind == CaveHollowKind.Trench ? h.Radius * 2f : h.Radius;

		private static void HollowBounds(in CaveHollow h, out Vector3 lo, out Vector3 hi)
		{
			switch (h.Kind)
			{
				case CaveHollowKind.Bowl:
				{
					float r = h.Radius + Mathf.Max(0f, h.Top - h.A.y) / BowlGrade + 0.5f;
					lo = new Vector3(h.A.x - r, h.A.y - h.Depth, h.A.z - r);
					hi = new Vector3(h.A.x + r, h.Top, h.A.z + r);
					return;
				}
				case CaveHollowKind.Dome:
					lo = new Vector3(h.A.x - h.Radius, Mathf.Min(h.A.y, h.Top), h.A.z - h.Radius);
					hi = new Vector3(h.A.x + h.Radius, h.A.y + 1.4f * h.Depth, h.A.z + h.Radius);
					return;
				case CaveHollowKind.Trench:
					lo = Vector3.Min(h.A, h.B) - new Vector3(h.Radius, h.Depth, h.Radius);
					hi = Vector3.Max(h.A, h.B) + new Vector3(h.Radius, TrenchCap, h.Radius);
					return;
				default:
				{
					float r = h.Radius * 1.415f;
					lo = new Vector3(h.A.x - r, h.A.y, h.A.z - r);
					hi = new Vector3(h.A.x + r, h.Top, h.A.z + r);
					return;
				}
			}
		}

		/// <summary>Whether a point is within <paramref name="margin"/> of a basin, a trench or a raise (what later ways keep off).</summary>
		private bool NearHollow(Vector3 p, float margin)
		{
			foreach (CaveHollow h in Hollows)
			{
				// Domes and avens are in the roof: a way under them is not in them.
				if (h.Kind == CaveHollowKind.Dome || h.Kind == CaveHollowKind.Aven)
				{
					continue;
				}
				HollowBounds(h, out Vector3 lo, out Vector3 hi);
				lo -= Vector3.one * margin;
				hi += Vector3.one * (margin + 2f);
				if (p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y && p.z >= lo.z && p.z <= hi.z)
				{
					return true;
				}
			}
			return false;
		}

		// ── The new solids' fields ────────────────────────────────

		/// <summary>A terrace's slab (negative inside): a capsule in plan along A→B, its top on that line, its underside Depth under it.</summary>
		private static float TerraceField(in CaveSolid s, Vector3 p, out float top)
		{
			var d = new Vector2(s.B.x - s.A.x, s.B.z - s.A.z);
			var ap = new Vector2(p.x - s.A.x, p.z - s.A.z);
			float t = Mathf.Clamp01(Vector2.Dot(ap, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
			float plan = (ap - d * t).magnitude - s.Width;
			top = Mathf.Lerp(s.A.y, s.B.y, t);
			return Mathf.Max(plan, Mathf.Max(p.y - top, (top - s.Depth) - p.y));
		}

		/// <summary>
		/// A breakdown mound (negative inside): a low heap of fallen debris with blocks of the roof lying tumbled on it,
		/// each a rounded box at its own tilt, the largest toward the middle. All from the solid's own values (its
		/// <see cref="CaveSolid.Angle"/> the heap's salt), so a stored system draws the same heap.
		/// </summary>
		private static float RubbleField(in CaveSolid s, Vector3 p)
		{
			float R = Mathf.Max(0.5f, s.Width), H = Mathf.Max(0.3f, s.Depth);
			float block = Mathf.Clamp(0.45f * R, 0.8f, 2.6f);
			float dx = p.x - s.A.x, dz = p.z - s.A.z, dy = p.y - s.A.y;
			float plan = Mathf.Sqrt(dx * dx + dz * dz);
			if (plan > R + 2f * block + 1f || dy > H + 2f * block + 1f)
			{
				return Mathf.Max(plan - (R + 2f * block), dy - (H + 2f * block));
			}
			// The fine debris: a flattened mound under the blocks.
			var m = new Vector3(dx / R, dy / (0.45f * H), dz / R);
			float best = (m.magnitude - 1f) * Mathf.Min(R, 0.45f * H);
			uint salt = (uint)Mathf.RoundToInt(Mathf.Abs(s.Angle));
			int count = Mathf.Clamp(Mathf.RoundToInt(1.6f * R + 3f), 5, 16);
			for (int i = 0; i < count; i++)
			{
				uint h = Mix(salt, 0xB10Cu + (uint)i * 0x85EBu);
				float a = Unit(h, 1) * 2f * Mathf.PI, rr = R * 0.85f * Mathf.Sqrt(Unit(h, 2));
				float size = block * Mathf.Lerp(0.45f, 1f, Unit(h, 3)) * Mathf.Lerp(1f, 0.6f, rr / R);
				var half = new Vector3(size * Mathf.Lerp(0.6f, 1f, Unit(h, 4)), size * Mathf.Lerp(0.35f, 0.7f, Unit(h, 5)), size * Mathf.Lerp(0.5f, 0.9f, Unit(h, 6)));
				float cone = H * (1f - rr / R);
				float cx = dx - Mathf.Sin(a) * rr, cy = dy - (cone + 0.1f * half.y), cz = dz - Mathf.Cos(a) * rr;
				float yaw = Unit(h, 7) * 2f * Mathf.PI, tilt = (Unit(h, 8) - 0.5f) * 0.8f;
				float sy = Mathf.Sin(yaw), cyw = Mathf.Cos(yaw), st = Mathf.Sin(tilt), ct = Mathf.Cos(tilt);
				float lx = cyw * cx - sy * cz, lz = sy * cx + cyw * cz;
				float ly = ct * cy - st * lz;
				lz = st * cy + ct * lz;
				const float round = 0.15f;
				float qx = Mathf.Abs(lx) - (half.x - round), qy = Mathf.Abs(ly) - (half.y - round), qz = Mathf.Abs(lz) - (half.z - round);
				float outside = new Vector3(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f), Mathf.Max(qz, 0f)).magnitude;
				float box = outside + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f) - round;
				best = Mathf.Min(best, box);
			}
			return best;
		}

		/// <summary>
		/// A natural arch (negative inside): a rib of rock on a half-ellipse in the vertical plane through its feet A and B,
		/// its underside <see cref="CaveSolid.Depth"/> over the line between them at its crown, the rib
		/// <see cref="CaveSolid.Width"/> round, thickening toward its feet, which run on down into the floor.
		/// </summary>
		private static float ArchField(in CaveSolid s, Vector3 p)
		{
			var ab = new Vector2(s.B.x - s.A.x, s.B.z - s.A.z);
			float span = Mathf.Max(0.5f, ab.magnitude);
			Vector2 u = ab / span;
			float half = 0.5f * span;
			var mid = new Vector2(0.5f * (s.A.x + s.B.x), 0.5f * (s.A.z + s.B.z));
			var o = new Vector2(p.x - mid.x, p.z - mid.y);
			float x = Vector2.Dot(o, u), z = o.x * u.y - o.y * u.x;
			float baseY = Mathf.Lerp(s.A.y, s.B.y, Mathf.Clamp01((x + half) / span));
			float y = p.y - baseY;
			float rise = s.Depth + s.Width;
			float best = float.MaxValue, thick = s.Width;
			const int segments = 20;
			float t0 = -0.3f, t1 = Mathf.PI + 0.3f;
			Vector2 prev = new Vector2(Mathf.Cos(t0) * half, Mathf.Sin(t0) * rise);
			for (int k = 1; k <= segments; k++)
			{
				float t = Mathf.Lerp(t0, t1, k / (float)segments);
				var next = new Vector2(Mathf.Cos(t) * half, Mathf.Sin(t) * rise);
				Vector2 seg = next - prev;
				var q = new Vector2(x, y) - prev;
				float f = Mathf.Clamp01(Vector2.Dot(q, seg) / Mathf.Max(1e-6f, seg.sqrMagnitude));
				float d = (q - seg * f).sqrMagnitude;
				if (d < best)
				{
					best = d;
					float at = Mathf.Lerp(t - (t1 - t0) / segments, t, f);
					thick = s.Width * (1f + 0.7f * (1f - Mathf.Clamp01(Mathf.Sin(at))));
				}
				prev = next;
			}
			return Mathf.Sqrt(best + z * z) - thick;
		}

		private static void NewSolidBounds(in CaveSolid s, out Vector3 lo, out Vector3 hi)
		{
			switch (s.Kind)
			{
				case CaveSolidKind.Terrace:
					lo = Vector3.Min(s.A, s.B) - new Vector3(s.Width, s.Depth, s.Width);
					hi = Vector3.Max(s.A, s.B) + new Vector3(s.Width, 0.5f, s.Width);
					return;
				case CaveSolidKind.Rubble:
				{
					float r = s.Width + 2f * Mathf.Clamp(0.45f * s.Width, 0.8f, 2.6f);
					lo = new Vector3(s.A.x - r, s.A.y - 1f, s.A.z - r);
					hi = new Vector3(s.A.x + r, s.A.y + s.Depth + 2f * Mathf.Clamp(0.45f * s.Width, 0.8f, 2.6f), s.A.z + r);
					return;
				}
				default:
				{
					float r = 1.7f * s.Width + 0.5f;
					lo = Vector3.Min(s.A, s.B) - new Vector3(r, 0.3f * (s.Depth + s.Width) + r, r);
					hi = Vector3.Max(s.A, s.B) + new Vector3(r, s.Depth + s.Width + r, r);
					return;
				}
			}
		}

		/// <summary>How rough a solid's sides are against the walls' noise: a terrace's as the wall, an arch's less, rubble's blocks least.</summary>
		private static float SolidGrain(CaveSolidKind kind) => kind == CaveSolidKind.Rubble ? 0.3f : kind == CaveSolidKind.Arch ? 0.7f : 1f;

		// ── Joining and storage ───────────────────────────────────

		/// <summary>Takes another system's hollows, terraces and pits into this one's frame, their indices moved past this one's lists.</summary>
		private void AbsorbVariety(CaveSystem other, Vector3 shift, int solids, int shafts, int climbs, int hollows)
		{
			foreach (CaveHollow h in other.Hollows)
			{
				CaveHollow moved = h;
				moved.A += shift;
				moved.B += shift;
				moved.Top += shift.y;
				Hollows.Add(moved);
			}
			foreach (CaveTerrace t in other.Terraces)
			{
				CaveTerrace moved = t;
				moved.Solid += solids;
				moved.Ramp = t.Ramp < 0 ? -1 : t.Ramp + solids;
				moved.Climb = t.Climb < 0 ? -1 : t.Climb + climbs;
				Terraces.Add(moved);
			}
			foreach (CavePit pit in other.Pits)
			{
				CavePit moved = pit;
				moved.Rim += shift;
				moved.Bottom += shift.y;
				bool basin = pit.Kind <= CavePitKind.ColdTrap;
				moved.Hole += basin ? hollows : shafts;
				moved.Way = pit.Way < 0 ? -1 : pit.Way + (pit.Kind == CavePitKind.Ledged ? solids : climbs);
				Pits.Add(moved);
			}
		}

		/// <summary>The values a system of this format stores after its climbs: its traits, hollows, terraces and pits.</summary>
		private void WriteVariety(List<float> v)
		{
			v.Add((float)(ushort)Traits);
			v.Add(Hollows.Count);
			v.Add(Terraces.Count);
			v.Add(Pits.Count);
			foreach (CaveHollow h in Hollows)
			{
				v.AddRange(new[] { (float)h.Kind, h.A.x, h.A.y, h.A.z, h.B.x, h.B.y, h.B.z, h.Radius, h.Depth, h.Top, h.Yaw });
			}
			foreach (CaveTerrace t in Terraces)
			{
				v.AddRange(new[] { (float)t.Kind, t.Solid, t.Ramp, t.Climb, t.Height, t.Walk, t.Out.x, t.Out.z });
			}
			foreach (CavePit p in Pits)
			{
				v.AddRange(new[] { (float)p.Kind, p.Rim.x, p.Rim.y, p.Rim.z, p.Radius, p.Bottom, p.Hole, p.Way, float.IsNaN(p.Water) ? NoWater : p.Water });
			}
		}

		/// <summary>Reads what <see cref="WriteVariety"/> wrote from <paramref name="o"/>; the offset past it, or −1 where the values run short.</summary>
		private int ReadVariety(float[] v, int o)
		{
			if (o + 4 > v.Length)
			{
				return -1;
			}
			Traits = (CaveTrait)(ushort)Mathf.RoundToInt(v[o]);
			int hollows = Mathf.RoundToInt(v[o + 1]), terraces = Mathf.RoundToInt(v[o + 2]), pits = Mathf.RoundToInt(v[o + 3]);
			o += 4;
			if (o + 11 * hollows + 8 * terraces + 9 * pits > v.Length)
			{
				return -1;
			}
			Vector3 V(int at) => new Vector3(v[at], v[at + 1], v[at + 2]);
			for (int i = 0; i < hollows; i++, o += 11)
			{
				Hollows.Add(new CaveHollow { Kind = (CaveHollowKind)Mathf.RoundToInt(v[o]), A = V(o + 1), B = V(o + 4), Radius = v[o + 7], Depth = v[o + 8], Top = v[o + 9], Yaw = v[o + 10] });
			}
			for (int i = 0; i < terraces; i++, o += 8)
			{
				Terraces.Add(new CaveTerrace
				{
					Kind = (CaveTerraceKind)Mathf.RoundToInt(v[o]), Solid = Mathf.RoundToInt(v[o + 1]), Ramp = Mathf.RoundToInt(v[o + 2]), Climb = Mathf.RoundToInt(v[o + 3]),
					Height = v[o + 4], Walk = v[o + 5], Out = new Vector3(v[o + 6], 0f, v[o + 7]),
				});
			}
			for (int i = 0; i < pits; i++, o += 9)
			{
				Pits.Add(new CavePit
				{
					Kind = (CavePitKind)Mathf.RoundToInt(v[o]), Rim = V(o + 1), Radius = v[o + 4], Bottom = v[o + 5], Hole = Mathf.RoundToInt(v[o + 6]), Way = Mathf.RoundToInt(v[o + 7]),
					Water = v[o + 8] <= NoWater ? float.NaN : v[o + 8],
				});
			}
			return o;
		}

		// ── Checking a way ────────────────────────────────────────

		/// <summary>
		/// Whether a way along these points (system frame; heights a guess, the first a little over its floor) can be walked on
		/// the noisy field as the mesher cuts it: every half metre a floor found within reach under the last (none: a drop), 2.2 m
		/// of headroom over it, and no step steeper than <paramref name="steepest"/> degrees (the walk check allows 42°, the
		/// NavMesh agent 45°). <paramref name="why"/> says where it fails.
		/// </summary>
		internal bool WalkFits(IList<Vector3> path, out string why, float steepest = 40f)
		{
			why = null;
			float previous = float.NaN;
			Vector3 last = default;
			for (int i = 0; i < path.Count; i++)
			{
				Vector3 a = i == 0 ? path[0] : path[i - 1], b = path[i];
				int steps = i == 0 ? 1 : Mathf.Max(1, Mathf.CeilToInt(new Vector2(b.x - a.x, b.z - a.z).magnitude / 0.5f));
				for (int s = i == 0 ? 0 : 1; s <= steps; s++)
				{
					Vector3 p = i == 0 ? b : Vector3.Lerp(a, b, s / (float)steps);
					float from = float.IsNaN(previous) ? p.y + 1.2f : previous + 1.2f;
					float floor = FloorBelow(new Vector3(p.x, from, p.z), 3.5f, 0.02f);
					if (float.IsNaN(floor))
					{
						why = $"no floor under {p} (rock at {from:0.0}, or a drop)";
						return false;
					}
					// To a centimetre: the march's tenth of a metre is a tenth of a step's slope over half a metre.
					float lo = floor - 0.02f, hi = floor;
					for (int k = 0; k < 4; k++)
					{
						float mid = 0.5f * (lo + hi);
						if (Void(new Vector3(p.x, mid, p.z)) < 0f) hi = mid; else lo = mid;
					}
					floor = hi;
					// Do not jump over a thin roof into the open pocket above it.
					for (float h = 0.1f; h <= 2.21f; h += 0.1f)
					{
						if (Void(new Vector3(p.x, floor + h, p.z)) >= 0f)
						{
							why = $"{h:0.0} m of headroom at {p}";
							return false;
						}
					}
					if (!float.IsNaN(previous))
					{
						float run = new Vector2(p.x - last.x, p.z - last.z).magnitude;
						if (run > 0.2f && Mathf.Atan2(Mathf.Abs(floor - previous), run) * Mathf.Rad2Deg > steepest)
						{
							why = $"a step of {floor - previous:0.00} m in {run:0.00} m at {p}";
							return false;
						}
					}
					previous = floor;
					last = p;
				}
			}
			return true;
		}

		/// <summary>A pit's way out (<see cref="CavePit"/>): from its bottom to its rim, on its sides, its climb or its ledge. Why not, else null.</summary>
		public string PitWayOut(in CavePit pit)
		{
			switch (pit.Kind)
			{
				case CavePitKind.Shaft:
				case CavePitKind.Winze:
				{
					if (pit.Way < 0 || pit.Way >= Climbs.Count)
					{
						return "no climb";
					}
					CaveClimb c = Climbs[pit.Way];
					if (new Vector2(c.Bottom.x - pit.Rim.x, c.Bottom.z - pit.Rim.z).magnitude > pit.Radius + 0.5f || Mathf.Abs(c.Bottom.y - pit.Bottom) > 1f)
					{
						return "its climb does not start at its bottom";
					}
					return ClimbFits(c) ? null : "its climb does not fit";
				}
				case CavePitKind.Ledged:
					return pit.Way >= 0 && pit.Way < Solids.Count && Solids[pit.Way].Kind == CaveSolidKind.Spiral ? null : "no ledge";
				default:
				{
					string why = "no side walked";
					for (int k = 0; k < 8; k++)
					{
						float a = k * Mathf.PI / 4f;
						var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
						var path = new List<Vector3> { new Vector3(pit.Rim.x, pit.Bottom + 0.2f, pit.Rim.z), pit.Rim + dir * (pit.Radius + 1.5f) };
						if (WalkFits(path, out why))
						{
							return null;
						}
					}
					return why;
				}
			}
		}

		/// <summary>The way up onto a terrace, from the floor at its ramp's foot (or under its climb) along its top: the points <see cref="WalkFits"/> walks.</summary>
		public List<Vector3> TerraceWalk(in CaveTerrace terrace)
		{
			var path = new List<Vector3>();
			CaveSolid slab = Solids[terrace.Solid];
			Vector3 toRoom = terrace.Out;
			// A line along its top a little in from its inner edge.
			float inset = Mathf.Min(1.2f, Mathf.Max(0.6f, 0.5f * terrace.Walk - 0.3f));
			Vector3 Edge(Vector3 spine) => spine + toRoom * (slab.Width - inset);
			Vector3 a = Edge(slab.A), b = Edge(slab.B);
			if (terrace.Ramp >= 0)
			{
				CaveSolid ramp = Solids[terrace.Ramp];
				path.Add(ramp.A + Vector3.up * 0.2f);
				path.Add(ramp.B);
				bool fromB = (ramp.B - slab.B).sqrMagnitude < (ramp.B - slab.A).sqrMagnitude;
				Vector3 near = fromB ? b : a, far = fromB ? a : b;
				Vector3 run = Flat3(far - near);
				path.Add(near - run * 0.2f);
				path.Add(far - run * 0.5f);
			}
			else
			{
				Vector3 run = Flat3(b - a);
				path.Add(a + run * 1f);
				path.Add(b - run * 1f);
			}
			return path;
		}

		/// <summary>How far a terrace's slab runs on past its walking top into the wall behind it, metres.</summary>
		private const float TerraceOverhang = 2.5f;

		private static Vector3 Flat3(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
		}

		// ── The pass ──────────────────────────────────────────────

		/// <summary>
		/// The variety pass over a planned system (2026-10-20): what its traits lay — side passages, a mine's stopes, collapses
		/// and winzes, breakdown domes and mounds, terraces, galleries, pits, rimstone, a cold trap, columns, arches, keyhole
		/// trenches — each on its own stream (so the plan's own draws and one pass's count never move another's), each kept off
		/// the ways, the mouths, the chamber middles, the chasms and the climbs, and each checked on the field as it then stands
		/// (walkable, with a way out of whatever can be fallen into) and taken back out whole where it fails.
		/// </summary>
		private void Vary(CaveGround ground, Profile pf, int siteId, int size)
		{
			PrepareField();
			new Variety(this, ground, pf, siteId, size).Run();
			PrepareField();
		}

		private sealed class Variety
		{
			private readonly CaveSystem system;
			private readonly Profile pf;
			private readonly int siteId, size;
			private readonly Walker walker;
			private readonly List<(Vector3 a, Vector3 b)> lanes = new List<(Vector3, Vector3)>();
			private readonly List<(Vector3 at, float radius, float height)> keeps = new List<(Vector3, float, float)>();
			/// <summary>Which of the layout's climbs fitted the field before the pass (one that did not is not the pass's to keep).</summary>
			private readonly bool[] fitted;
			/// <summary>The site's own chamber (its camp's, den's or gathering's: <see cref="TryChamberOf"/>), −1 for none.</summary>
			private readonly int siteChamber = -1;
			/// <summary>
			/// The system as the layout left it, before the pass (2026-10-21): where a way near a feature no longer walks, whether it
			/// walked before (the feature broke it) or never did (the layout's own fault, not the pass's to keep).
			/// </summary>
			private readonly CaveSystem before;

			private struct Mark
			{
				public int Passages, Chambers, Solids, Shafts, Climbs, Hollows, Terraces, Pits;
			}

			public Variety(CaveSystem system, CaveGround ground, Profile pf, int siteId, int size)
			{
				this.system = system;
				this.pf = pf;
				this.siteId = siteId;
				this.size = Mathf.Clamp(size, 0, 2);
				walker = new Walker(system, ground, pf, Stream(0x3A1Cu));
				if (system.TryChamberOf(siteId, out CaveChamber own))
				{
					siteChamber = system.Chambers.FindIndex(c => c.Centre == own.Centre && c.Kind == own.Kind);
				}
				fitted = new bool[system.Climbs.Count];
				for (int i = 0; i < fitted.Length; i++)
				{
					fitted[i] = system.ClimbFits(system.Climbs[i]);
				}
				before = FromValues(system.ToValues(), system.Origin, system.Seed);
				if (before != null)
				{
					before.LavaHeld = true;
					before.Prepare();
				}
			}

			private DeterministicRNG Stream(uint salt) => new DeterministicRNG(unchecked((int)Mix(unchecked((uint)system.Seed), salt)));

			private float MinFloor => system.LowestFloor - system.Origin.y;

			public void Run()
			{
				if (system.Has(CaveTrait.Labyrinthine)) SidePassages(Stream(0x51DEu));
				if (system.Has(CaveTrait.Stoped)) Stopes(Stream(0x570Eu));
				if (system.Has(CaveTrait.Collapsed)) Collapses(Stream(0xC011u));
				if (system.Has(CaveTrait.Sumped)) Winzes(Stream(0x5B3Fu));
				if (system.Has(CaveTrait.Breakdown)) Breakdown(Stream(0xB4EAu));
				if (system.Has(CaveTrait.Terraced) || system.Has(CaveTrait.Benched)) Terraces(Stream(0x7E44u));
				if (system.Has(CaveTrait.Galleried)) Galleries(Stream(0x6A11u));
				Pits(Stream(0x9175u));
				if (system.Has(CaveTrait.Rimstone)) Gours(Stream(0x60B5u));
				if (system.Has(CaveTrait.ColdSink)) ColdTrap(Stream(0xC01Du));
				if (system.Has(CaveTrait.Pillared)) Pillars(Stream(0x9111u));
				if (system.Has(CaveTrait.Arched)) Arches(Stream(0xA4C8u));
				if (system.Has(CaveTrait.Fissured)) Keyholes(Stream(0xF155u));
				Avens(Stream(0xA7E2u));
			}

			private Mark Save() => new Mark
			{
				Passages = system.Passages.Count, Chambers = system.Chambers.Count, Solids = system.Solids.Count, Shafts = system.Shafts.Count,
				Climbs = system.Climbs.Count, Hollows = system.Hollows.Count, Terraces = system.Terraces.Count, Pits = system.Pits.Count,
			};

			private void Restore(Mark m)
			{
				system.Passages.RemoveRange(m.Passages, system.Passages.Count - m.Passages);
				system.Chambers.RemoveRange(m.Chambers, system.Chambers.Count - m.Chambers);
				system.Solids.RemoveRange(m.Solids, system.Solids.Count - m.Solids);
				system.Shafts.RemoveRange(m.Shafts, system.Shafts.Count - m.Shafts);
				system.Climbs.RemoveRange(m.Climbs, system.Climbs.Count - m.Climbs);
				system.Hollows.RemoveRange(m.Hollows, system.Hollows.Count - m.Hollows);
				system.Terraces.RemoveRange(m.Terraces, system.Terraces.Count - m.Terraces);
				system.Pits.RemoveRange(m.Pits, system.Pits.Count - m.Pits);
				system.PrepareField();
			}

			/// <summary>
			/// Whether every climb near a feature just laid still fits the field (a basin's rim or a slab beside one may crowd it):
			/// the layout's that fitted before the pass, and every one the pass laid itself; and every way near it still walks
			/// (<see cref="WaysHold"/>).
			/// </summary>
			private bool ClimbsHold(Vector3 near, float reach)
			{
				if (!WaysHold(near, reach))
				{
					return false;
				}
				for (int i = 0; i < system.Climbs.Count; i++)
				{
					CaveClimb c = system.Climbs[i];
					if (i < fitted.Length && !fitted[i])
					{
						continue;
					}
					float d = Mathf.Min(new Vector2(c.Bottom.x - near.x, c.Bottom.z - near.z).magnitude, new Vector2(c.Top.x - near.x, c.Top.z - near.z).magnitude);
					if (d < reach + 6f && !system.ClimbFits(c))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>
			/// Whether every way near a feature just laid still walks (2026-10-21: a labyrinth's side passage dipping under a way at
			/// another floor opened a hole in its floor; a basin beside one steepened it): each passage's stretch between two nodes
			/// within <paramref name="reach"/> (and 6 m) in plan and 25 m in height, each chamber's middle (headroom over its floor),
			/// each terrace's walk the pass laid. A way the layout left that did not walk before the pass is the layout's own.
			/// </summary>
			private bool WaysHold(Vector3 near, float reach)
			{
				float r = reach + 6f;
				var path = new List<Vector3>(2) { default, default };
				for (int p = 0; p < system.Passages.Count; p++)
				{
					List<CaveNode> nodes = system.Passages[p].Nodes;
					bool layouts = before != null && p < before.Passages.Count;
					for (int i = 0; i + 1 < nodes.Count; i++)
					{
						CaveNode a = nodes[i], b = nodes[i + 1];
						if (Mathf.Abs(0.5f * (a.Floor + b.Floor) - near.y) > 25f || PlanDistance(near, a.Centre, b.Centre) > r)
						{
							continue;
						}
						path[0] = new Vector3(a.Centre.x, a.Floor + 0.3f, a.Centre.z);
						path[1] = new Vector3(b.Centre.x, b.Floor + 0.3f, b.Centre.z);
						if (!system.WalkFits(path, out _) && (!layouts || before.WalkFits(path, out _)))
						{
							return false;
						}
					}
				}
				for (int c = 0; c < system.Chambers.Count; c++)
				{
					CaveChamber chamber = system.Chambers[c];
					if (Mathf.Abs(chamber.Floor - near.y) > 25f || new Vector2(chamber.Centre.x - near.x, chamber.Centre.z - near.z).magnitude > r)
					{
						continue;
					}
					var middle = new List<Vector3> { new Vector3(chamber.Centre.x, chamber.Floor + 0.3f, chamber.Centre.z) };
					if (!system.WalkFits(middle, out _) && (before == null || c >= before.Chambers.Count || before.WalkFits(middle, out _)))
					{
						return false;
					}
				}
				foreach (CaveTerrace terrace in system.Terraces)
				{
					List<Vector3> walk = system.TerraceWalk(terrace);
					// A long terrace can start beyond the feature's radius while its far end crosses it.
					bool nearby = false;
					for (int i = 0; i < walk.Count && !nearby; i++)
					{
						Vector3 a = i == 0 ? walk[0] : walk[i - 1], b = walk[i];
						nearby = Mathf.Abs(0.5f * (a.y + b.y) - near.y) < 25f && PlanDistance(near, a, b) < r + 30f;
					}
					if (nearby && !system.WalkFits(walk, out _))
					{
						return false;
					}
				}
				return true;
			}

			private static float PlanDistance(Vector3 p, Vector3 a, Vector3 b)
			{
				var ab = new Vector2(b.x - a.x, b.z - a.z);
				var ap = new Vector2(p.x - a.x, p.z - a.z);
				float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
				return (ap - ab * t).magnitude;
			}

			// ── Geometry ───────────────────────────────────────────

			private static Vector3 Dir(float yaw)
			{
				float r = yaw * Mathf.Deg2Rad;
				return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			}

			private static float Yaw(Vector3 dir) => Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f);

			/// <summary>How far a chamber reaches from its middle in a direction, at its floor (as the layout's own).</summary>
			private static float ReachOf(in CaveChamber c, Vector3 dir)
			{
				float yr = c.Yaw * Mathf.Deg2Rad;
				float along = dir.x * Mathf.Sin(yr) + dir.z * Mathf.Cos(yr);
				float across = dir.x * Mathf.Cos(yr) - dir.z * Mathf.Sin(yr);
				float r = 1f / Mathf.Sqrt(along * along / (c.Radii.x * c.Radii.x) + across * across / (c.Radii.z * c.Radii.z) + 1e-9f);
				return r * Mathf.Sqrt(1f - 0.16f);
			}

			/// <summary>Whether a point lies in a chamber's own volume, grown by <paramref name="grow"/> metres.</summary>
			private static bool InChamber(in CaveChamber c, Vector3 p, float grow)
			{
				float yr = c.Yaw * Mathf.Deg2Rad;
				Vector3 o = p - c.Centre;
				float along = o.x * Mathf.Sin(yr) + o.z * Mathf.Cos(yr), across = o.x * Mathf.Cos(yr) - o.z * Mathf.Sin(yr);
				var e = new Vector3(along / (c.Radii.x + grow), (o.y - c.Slope * along) / (c.Radii.y + grow), across / (c.Radii.z + grow));
				return e.sqrMagnitude < 1f;
			}

			private bool InAnyChamber(Vector3 p, float grow)
			{
				foreach (CaveChamber c in system.Chambers)
				{
					if (InChamber(c, p, grow))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Whether a point lies in or just round the bore of a run of a passage's nodes.</summary>
			private static bool InRun(CavePassage passage, int from, int to, Vector3 p, float grow)
			{
				for (int i = Mathf.Max(0, from); i <= Mathf.Min(passage.Nodes.Count - 1, to); i++)
				{
					CaveNode n = passage.Nodes[i];
					float r = n.Radius * Mathf.Max(1f, n.Height) + grow;
					if ((p - n.Centre).sqrMagnitude < r * r)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>
			/// The middle a chamber keeps clear: the site's own chamber its camp, den, gathering or dungeon's door (the dressing
			/// keeps 0.6 of a cavern's breadth for the door); any other only where its ways meet (their lanes keep the rest).
			/// </summary>
			private float Keep(int index)
			{
				CaveChamber c = system.Chambers[index];
				float small = Mathf.Min(c.Radii.x, c.Radii.z);
				if (index == siteChamber)
				{
					return c.Kind == CaveChamberKind.Cavern ? Mathf.Max(6f, 0.6f * small) : Mathf.Max(4.5f, 0.35f * small);
				}
				return c.Kind == CaveChamberKind.Hall ? Mathf.Max(3f, 0.12f * small) : 2f;
			}

			private static Vector3 FloorPoint(in CaveChamber c, Vector3 p) => new Vector3(p.x, FloorOf(c, p), p.z);

			private bool Dry(Vector3 p) => float.IsNaN(system.WaterLevel) || p.y + system.Origin.y > system.WaterLevel + 0.3f;

			// ── Keeping clear ──────────────────────────────────────

			/// <summary>
			/// The ways (every passage's line on its floor, from each passage's end into a chamber to its middle, every ramp,
			/// bridge and crossing) and the places kept (each chamber's middle, each mouth and its approach) as they now stand.
			/// </summary>
			private void Lanes()
			{
				lanes.Clear();
				keeps.Clear();
				foreach (CavePassage p in system.Passages)
				{
					for (int i = 0; i + 1 < p.Nodes.Count; i++)
					{
						CaveNode a = p.Nodes[i], b = p.Nodes[i + 1];
						lanes.Add((new Vector3(a.Centre.x, a.Floor, a.Centre.z), new Vector3(b.Centre.x, b.Floor, b.Centre.z)));
					}
				}
				for (int ci = 0; ci < system.Chambers.Count; ci++)
				{
					CaveChamber c = system.Chambers[ci];
					Vector3 middle = new Vector3(c.Centre.x, c.Floor, c.Centre.z);
					foreach (CavePassage p in system.Passages)
					{
						foreach (CaveNode n in new[] { p.Nodes[0], p.Nodes[p.Nodes.Count - 1] })
						{
							if (InChamber(c, new Vector3(n.Centre.x, FloorOf(c, n.Centre) + 1f, n.Centre.z), n.Radius + 2f) && Mathf.Abs(n.Floor - FloorOf(c, n.Centre)) < 3f)
							{
								lanes.Add((new Vector3(n.Centre.x, n.Floor, n.Centre.z), middle));
							}
						}
					}
					keeps.Add((middle, Keep(ci), 6f));
				}
				foreach (CaveSolid s in system.Solids)
				{
					if (s.Kind == CaveSolidKind.Ramp || s.Kind == CaveSolidKind.Bridge)
					{
						lanes.Add((s.A, s.B));
					}
				}
				foreach (CaveCrossing c in system.Crossings)
				{
					lanes.Add((c.A, c.B));
				}
				foreach (CaveMouth m in system.Mouths)
				{
					keeps.Add((m.Floor, m.Zone + 2f * m.Radius + 6f, 40f));
				}
			}

			/// <summary>Whether a point (on a floor) is <paramref name="margin"/> clear of every lane, kept place and layout feature.</summary>
			private bool Clear(Vector3 p, float margin, float height = 4f)
			{
				float reach = WayLane + margin;
				foreach ((Vector3 a, Vector3 b) in lanes)
				{
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if ((ap - ab * t).sqrMagnitude < reach * reach && Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) < height)
					{
						return false;
					}
				}
				foreach ((Vector3 at, float radius, float h) in keeps)
				{
					float r = radius + margin;
					if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < r * r && Mathf.Abs(p.y - at.y) < h)
					{
						return false;
					}
				}
				return !system.NearChasmOrClimb(p + Vector3.up, margin);
			}

			/// <summary>Whether every point of the void among <paramref name="samples"/> is <paramref name="own"/> (what a fill or a cut may reach).</summary>
			private bool OnlyOwn(List<Vector3> samples, Func<Vector3, bool> own)
			{
				foreach (Vector3 q in samples)
				{
					if (system.Smooth(q) < 0.5f && !own(q))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Points through a capsule (plan, A→B, <paramref name="half"/> either side) from <paramref name="low"/> under its line up to it.</summary>
			private static List<Vector3> CapsuleSamples(Vector3 a, Vector3 b, float half, float low, float step = 1f)
			{
				var samples = new List<Vector3>();
				Vector3 d = Flat3(b - a), side = new Vector3(d.z, 0f, -d.x);
				float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				for (float u = -half; u <= length + half + 0.01f; u += step)
				{
					for (float v = -half; v <= half + 0.01f; v += step)
					{
						float along = Mathf.Clamp(u, 0f, length);
						if ((u - along) * (u - along) + v * v > half * half)
						{
							continue;
						}
						Vector3 q = a + d * u + side * v;
						float top = Mathf.Lerp(a.y, b.y, length > 1e-3f ? along / length : 0f);
						for (float y = top - low; y <= top + 0.01f; y += step)
						{
							samples.Add(new Vector3(q.x, y, q.z));
						}
					}
				}
				return samples;
			}

			/// <summary>Whether every point along a line in plan is <paramref name="margin"/> clear (<see cref="Clear"/>), a sample a metre.</summary>
			private bool LineClear(Vector3 a, Vector3 b, float margin, float drop = 0f)
			{
				float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				int steps = Mathf.Max(1, Mathf.CeilToInt(length));
				for (int s = 0; s <= steps; s++)
				{
					Vector3 p = Vector3.Lerp(a, b, s / (float)steps) - Vector3.up * drop;
					if (!Clear(p, margin))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>
			/// Lays a climb, its top brought to the floor found a pace off it, checked against the field as it then stands (the
			/// layout's own rule); false, and nothing kept, where it does not fit.
			/// </summary>
			private bool AddClimb(CaveClimb climb)
			{
				system.Climbs.Add(climb);
				system.PrepareField();
				float landing = system.FloorBelow(climb.Top + climb.TopExit * 0.6f + Vector3.up * 1.2f, 2.5f);
				if (!float.IsNaN(landing) && Mathf.Abs(landing - climb.Top.y) < 0.8f)
				{
					climb.Top.y = landing;
					system.Climbs[system.Climbs.Count - 1] = climb;
					system.PrepareField();
					if (system.ClimbFits(climb))
					{
						return true;
					}
				}
				system.Climbs.RemoveAt(system.Climbs.Count - 1);
				system.PrepareField();
				return false;
			}

			/// <summary>A point a share <paramref name="f"/> of the way from a chamber's middle to its wall, in a random direction.</summary>
			private static Vector3 PointIn(in CaveChamber c, DeterministicRNG r, float fMin, float fMax, out Vector3 dir, out float reach)
			{
				float a = r.Range(0f, 2f * Mathf.PI);
				Vector3 fwd = Dir(c.Yaw), side = new Vector3(fwd.z, 0f, -fwd.x);
				dir = (fwd * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
				reach = ReachOf(c, dir);
				Vector3 p = new Vector3(c.Centre.x, 0f, c.Centre.z) + dir * (reach * r.Range(fMin, fMax));
				return FloorPoint(c, p);
			}

			/// <summary>
			/// A spot on a chamber's floor at least <paramref name="edge"/> in from its wall (at the floor) and
			/// <paramref name="fromMiddle"/> out from its middle, in a random direction, even over the area between; false where
			/// that direction has no such spot.
			/// </summary>
			private static bool SpotIn(in CaveChamber c, DeterministicRNG r, float edge, float fromMiddle, out Vector3 at)
			{
				float a = r.Range(0f, 2f * Mathf.PI), u = r.NextFloat();
				Vector3 fwd = Dir(c.Yaw), side = new Vector3(fwd.z, 0f, -fwd.x);
				Vector3 dir = (fwd * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
				float most = ReachOf(c, dir) - edge;
				at = default;
				if (most <= fromMiddle)
				{
					return false;
				}
				float d = Mathf.Sqrt(Mathf.Lerp(fromMiddle * fromMiddle, most * most, u));
				at = FloorPoint(c, new Vector3(c.Centre.x, 0f, c.Centre.z) + dir * d);
				return true;
			}

			/// <summary>Whether the floor round a point is open and level with its chamber's plane: found within 0.4 m of it at eight points on a ring, headroom over each.</summary>
			private bool FloorAround(in CaveChamber c, Vector3 at, float ring, float headroom = 2.4f)
			{
				for (int k = 0; k <= 8; k++)
				{
					Vector3 q = k == 8 ? at : at + Dir(k * 45f) * ring;
					float expect = FloorOf(c, q);
					float floor = system.FloorBelow(new Vector3(q.x, expect + 1.2f, q.z), 2.5f);
					if (float.IsNaN(floor) || Mathf.Abs(floor - expect) > 0.4f || system.Void(new Vector3(q.x, floor + headroom, q.z)) >= 0f)
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Whether all is rock under a floor round a point, from <paramref name="top"/> down to <paramref name="bottom"/>, out to <paramref name="radius"/>.</summary>
			private bool RockBelow(Vector3 at, float radius, float bottom, float top)
			{
				for (float y = bottom; y <= top; y += 1.5f)
				{
					for (int k = 0; k <= 16; k++)
					{
						Vector3 q = k == 16 ? at : at + Dir(k * 45f) * (k < 8 ? radius : 0.5f * radius);
						if (system.Smooth(new Vector3(q.x, y, q.z)) < 0.4f)
						{
							return false;
						}
					}
				}
				return true;
			}

			private IEnumerable<int> ChambersBy(DeterministicRNG r, float odds, float smallest)
			{
				int count = system.Chambers.Count;
				for (int i = 0; i < count; i++)
				{
					CaveChamber c = system.Chambers[i];
					bool pick = r.NextFloat() < odds;
					if (pick && Mathf.Min(c.Radii.x, c.Radii.z) >= smallest && c.SiteId == siteId)
					{
						yield return i;
					}
				}
			}

			// ── Side passages ──────────────────────────────────────

			/// <summary>
			/// A maze's side passages: short ways off the passages at about right angles, each to an alcove (now and then another
			/// short way on from it to another: a stack), or narrowing to a dead end.
			/// </summary>
			private void SidePassages(DeterministicRNG r)
			{
				int want = r.Range(2, 4 + 2 * size), passages = system.Passages.Count;
				Lanes();
				for (int k = 0, tries = 0; k < want && tries < want * 4; tries++)
				{
					CavePassage parent = system.Passages[r.Range(0, passages)];
					if (parent.Nodes.Count < 6)
					{
						continue;
					}
					int i = r.Range(2, parent.Nodes.Count - 3);
					CaveNode n = parent.Nodes[i];
					Vector3 along = Flat3(parent.Nodes[i + 1].Centre - parent.Nodes[i - 1].Centre);
					float heading = Yaw(along) + (r.NextFloat() < 0.5f ? -1f : 1f) * r.Range(60f, 120f);
					if (KeptAt(new Vector3(n.Centre.x, n.Floor, n.Centre.z)) || !Dry(n.Centre) || InAnyChamber(n.Centre, 2f))
					{
						continue;
					}
					Mark mark = Save();
					if (SideFrom(n, heading, r.Range(8f, 26f), r, 2) != null)
					{
						k++;
						Lanes();
					}
					else
					{
						Restore(mark);
					}
				}
			}

			/// <summary>Whether a point lies in a kept place (a mouth's approach, a chamber's middle).</summary>
			private bool KeptAt(Vector3 p)
			{
				foreach ((Vector3 at, float radius, float h) in keeps)
				{
					if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < radius * radius && Mathf.Abs(p.y - at.y) < h)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>A side passage from a node of a way, walked and checked; its alcove, and <paramref name="depth"/> more stacked on.</summary>
			private CavePassage SideFrom(CaveNode from, float heading, float length, DeterministicRNG r, int depth)
			{
				float br = Mathf.Max(MinBore, from.Radius * r.Range(0.5f, 0.75f));
				Vector3 d = Dir(heading);
				var start = new CaveNode { Centre = from.Centre + d * (0.6f * from.Radius), Radius = br, Floor = from.Floor, Along = 0f };
				start.Centre.y = start.Floor + FloorDepth * br * pf.Flat;
				if (system.NearChasmOrClimb(start.Centre, br + 1f))
				{
					return null;
				}
				walker.Avoid = true;
				walker.AvoidFrom = br + 4f + from.Radius;
				CavePassage side = walker.Walk(CavePassageKind.Branch, start, heading, length, -from.Floor + r.Range(-2f, 5f), 0f, 0f, false, br);
				walker.Avoid = false;
				walker.AvoidFrom = 0f;
				if (side.Nodes.Count < 3)
				{
					return null;
				}
				system.Passages.Add(side);
				system.PrepareField();
				float roll = r.NextFloat();
				if (roll < 0.65f && walker.TryChamber(side, side.Nodes.Count - 1, r.Range(1.3f, 2f), CaveKind.Alcove, siteId, out CaveChamber alcove))
				{
					system.Chambers.Add(alcove);
					system.PrepareField();
					if (depth > 0 && r.NextFloat() < 0.35f)
					{
						// Another short way on from the alcove's far side, to another: the stack.
						float on = alcove.Yaw + (r.NextFloat() < 0.5f ? -1f : 1f) * r.Range(30f, 90f);
						Vector3 o = Dir(on);
						var edge = new CaveNode
						{
							Centre = new Vector3(alcove.Centre.x, 0f, alcove.Centre.z) + o * (0.6f * ReachOf(alcove, o)), Radius = br, Along = 0f,
						};
						edge.Floor = FloorOf(alcove, edge.Centre);
						edge.Centre.y = edge.Floor + FloorDepth * br * pf.Flat;
						Mark mark = Save();
						if (SideFrom(edge, on, r.Range(6f, 16f), r, depth - 1) == null)
						{
							Restore(mark);
						}
					}
				}
				else if (roll < 0.85f)
				{
					Narrow(side);
				}
				// Walked from the way it leaves to its end, as the mesher will cut it (its alcove's checks are its own).
				var path = new List<Vector3> { new Vector3(from.Centre.x, from.Floor + 0.3f, from.Centre.z) };
				foreach (CaveNode n in side.Nodes)
				{
					path.Add(new Vector3(n.Centre.x, n.Floor + 0.3f, n.Centre.z));
				}
				return system.WalkFits(path, out _) && ClimbsHold(side.Nodes[side.Nodes.Count / 2].Centre, length) ? side : null;
			}

			/// <summary>A dead end: the last few nodes draw in, level with the first of them, and stop (the layout's own).</summary>
			private void Narrow(CavePassage passage)
			{
				int n = passage.Nodes.Count;
				for (int i = Mathf.Max(0, n - 3); i < n; i++)
				{
					CaveNode node = passage.Nodes[i];
					float t = (i - (n - 3)) / 2f;
					node.Radius = Mathf.Lerp(node.Radius, Mathf.Max(MinBore, 0.7f * node.Radius), t);
					node.Floor = passage.Nodes[Mathf.Max(0, n - 3)].Floor;
					node.Centre.y = node.Floor + FloorDepth * node.Radius * pf.Flat * node.Height;
					passage.Nodes[i] = node;
				}
				system.PrepareField();
			}

			// ── Mines ──────────────────────────────────────────────

			/// <summary>
			/// A mine's stopes worked their own ways: pillars left standing where the ore was taken round them (room and pillar),
			/// a raise driven up from the back after the vein, or the stope opened out into an ore chamber.
			/// </summary>
			private void Stopes(DeterministicRNG r)
			{
				int count = system.Chambers.Count;
				for (int i = 0; i < count; i++)
				{
					CaveChamber c = system.Chambers[i];
					float roll = r.NextFloat(), how = r.NextFloat();
					if (c.Kind != CaveChamberKind.Stope || roll > 0.65f)
					{
						continue;
					}
					Lanes();
					if (how < 0.4f)
					{
						PillarsIn(i, r.Range(1, 4), r, 0.7f, 1.1f, 0.05f, 0.25f);
					}
					else if (how < 0.7f)
					{
						Raise(c, r);
					}
					else
					{
						OreChamber(c, r);
					}
				}
			}

			/// <summary>A raise up from a stope's back: a square shaft 4–10 m up into the rock, under its full cover.</summary>
			private bool Raise(in CaveChamber c, DeterministicRNG r)
			{
				for (int attempt = 0; attempt < 4; attempt++)
				{
					Vector3 at = PointIn(c, r, 0.1f, 0.5f, out _, out _);
					float roof = system.RoofAbove(at + Vector3.up * 1.5f, 30f);
					if (float.IsNaN(roof))
					{
						continue;
					}
					float half = r.Range(0.9f, 1.4f), top = roof + r.Range(4f, 10f);
					bool covered = true;
					for (int k = 0; k <= 4 && covered; k++)
					{
						Vector3 q = k == 4 ? at : at + Dir(c.Yaw + 45f + 90f * k) * (1.42f * half);
						covered = walker.Ground(q) - (top + walker.Amp) >= MinCover + 0.25f;
					}
					if (!covered)
					{
						continue;
					}
					// Nothing else in its way up.
					bool clear = true;
					for (float y = roof + 0.8f; y <= top + 1.5f && clear; y += 1.5f)
					{
						for (int k = 0; k <= 8 && clear; k++)
						{
							Vector3 q = k == 8 ? at : at + Dir(k * 45f) * (half * 1.5f);
							clear = system.Smooth(new Vector3(q.x, y, q.z)) >= 0.6f;
						}
					}
					if (!clear)
					{
						continue;
					}
					system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Raise, A = new Vector3(at.x, roof - 0.4f, at.z), Radius = half, Top = top, Yaw = c.Yaw });
					system.PrepareField();
					return true;
				}
				return false;
			}

			/// <summary>A stope opened out into an ore chamber: a taller working beside it on its floor, where the ore body swelled.</summary>
			private bool OreChamber(in CaveChamber stope, DeterministicRNG r)
			{
				CaveChamber s = stope;
				for (int attempt = 0; attempt < 3; attempt++)
				{
					float yaw = s.Yaw + r.Range(-50f, 50f);
					Vector3 d = Dir(yaw);
					var radii = new Vector3(Mathf.Max(4f, s.Radii.x * r.Range(1.2f, 1.8f)), r.Range(4f, 7f), Mathf.Max(3f, s.Radii.z * r.Range(1f, 1.4f)));
					Vector3 centre = new Vector3(s.Centre.x, 0f, s.Centre.z) + d * (0.6f * ReachOf(s, d) + 0.5f * radii.x);
					float floor = FloorOf(s, centre);
					Mark mark = Save();
					if (!walker.TryLobe(centre, floor, yaw, radii, 0f, CaveChamberKind.Stope, siteId, 1f, out CaveChamber ore, q => InChamber(s, q, 3f)))
					{
						continue;
					}
					system.Chambers.Add(ore);
					system.PrepareField();
					var path = new List<Vector3> { FloorPoint(s, s.Centre) + Vector3.up * 0.3f, new Vector3(ore.Centre.x, ore.Floor + 0.3f, ore.Centre.z) };
					if (system.WalkFits(path, out _))
					{
						return true;
					}
					Restore(mark);
				}
				return false;
			}

			/// <summary>Old drifts fallen in: the dead ends choked with rubble, and a spur or two driven off a drift that ends in a fall.</summary>
			private void Collapses(DeterministicRNG r)
			{
				int passages = system.Passages.Count;
				for (int pi = 0; pi < passages; pi++)
				{
					if (r.NextFloat() < 0.5f)
					{
						Choke(system.Passages[pi], r);
					}
				}
				if (system.Form != CaveForm.Mine)
				{
					return;
				}
				int spurs = r.Range(0, 2 + size);
				for (int k = 0, tries = 0; k < spurs && tries < spurs * 4; tries++)
				{
					CavePassage drift = system.Passages[r.Range(0, passages)];
					if (drift.Nodes.Count < 6)
					{
						continue;
					}
					int i = r.Range(2, drift.Nodes.Count - 3);
					CaveNode n = drift.Nodes[i];
					Lanes();
					if (KeptAt(new Vector3(n.Centre.x, n.Floor, n.Centre.z)) || InAnyChamber(n.Centre, 2f))
					{
						continue;
					}
					float heading = Yaw(Flat3(drift.Nodes[i + 1].Centre - drift.Nodes[i - 1].Centre)) + (r.NextFloat() < 0.5f ? 90f : -90f);
					var start = new CaveNode { Centre = n.Centre + Dir(heading) * (0.6f * n.Radius), Radius = system.Radius, Floor = n.Floor };
					start.Centre.y = start.Floor + FloorDepth * start.Radius * pf.Flat;
					Mark mark = Save();
					walker.Avoid = true;
					walker.AvoidFrom = system.Radius + 4f;
					CavePassage spur = walker.Walk(CavePassageKind.Branch, start, heading, r.Range(10f, 24f), -n.Floor, 0f, 0f, false, system.Radius);
					walker.Avoid = false;
					walker.AvoidFrom = 0f;
					if (spur.Nodes.Count < 4)
					{
						continue;
					}
					system.Passages.Add(spur);
					system.PrepareField();
					if (Choke(spur, r, always: true))
					{
						k++;
					}
					else
					{
						Restore(mark);
					}
				}
			}

			/// <summary>
			/// A dead end choked by a fall of the roof: a breakdown mound filling the passage's end past its last node, the way to
			/// it still walked. Only a passage that ends in no chamber.
			/// </summary>
			private bool Choke(CavePassage passage, DeterministicRNG r, bool always = false)
			{
				int n = passage.Nodes.Count;
				if (n < 4 || passage.Kind == CavePassageKind.Trunk && !always)
				{
					return false;
				}
				CaveNode end = passage.Nodes[n - 1];
				if (InAnyChamber(end.Centre, end.Radius + 1f) || system.NearChasmOrClimb(end.Centre, end.Radius + 1f) || system.NearHollow(end.Centre, end.Radius))
				{
					return false;
				}
				foreach (CavePassage other in system.Passages)
				{
					// Not where another way meets or leaves it at its end.
					if (other != passage && (InRun(other, 0, other.Nodes.Count - 1, end.Centre, end.Radius + 2f)))
					{
						return false;
					}
				}
				Vector3 dir = Flat3(end.Centre - passage.Nodes[n - 3].Centre);
				float salt = Mathf.Floor(r.Range(0f, 65535f));
				float width = end.Radius * r.Range(0.95f, 1.25f), height = end.Radius * Mathf.Max(0.6f, pf.Flat) * r.Range(1.2f, 1.7f);
				var path = new List<Vector3>();
				for (int i = Mathf.Max(0, n - 4); i < n; i++)
				{
					path.Add(new Vector3(passage.Nodes[i].Centre.x, passage.Nodes[i].Floor + 0.3f, passage.Nodes[i].Centre.z));
				}
				foreach (float push in new[] { 0.7f, 1f, 1.3f })
				{
					Mark mark = Save();
					Vector3 at = new Vector3(end.Centre.x, end.Floor, end.Centre.z) + dir * (push * end.Radius);
					system.Solids.Add(new CaveSolid { Kind = CaveSolidKind.Rubble, A = at, Width = width, Depth = height, Angle = salt });
					system.PrepareField();
					if (system.WalkFits(path, out _))
					{
						return true;
					}
					Restore(mark);
				}
				return false;
			}

			/// <summary>
			/// Winzes sunk at drifts' ends: a square shaft 5–12 m down beyond the end, a ladder down its near wall. The lowest one
			/// (a sump) is flooded where it lies under the drainage adit's level (the mouth's floor): the old workings under it
			/// would stand full of water without pumping, its water a little under the drift's floor.
			/// </summary>
			private void Winzes(DeterministicRNG r)
			{
				// The drift ending deepest first: its winze is the sump.
				var ends = new List<int>();
				for (int pi = 0; pi < system.Passages.Count; pi++)
				{
					CavePassage p = system.Passages[pi];
					if (p.Nodes.Count >= 4 && !InAnyChamber(p.Nodes[p.Nodes.Count - 1].Centre, 2f))
					{
						ends.Add(pi);
					}
				}
				ends.Sort((a, b) =>
				{
					int c = system.Passages[a].Nodes[system.Passages[a].Nodes.Count - 1].Floor.CompareTo(system.Passages[b].Nodes[system.Passages[b].Nodes.Count - 1].Floor);
					return c != 0 ? c : a.CompareTo(b);
				});
				int made = 0, most = 1 + size;
				for (int k = 0; k < ends.Count && made < most; k++)
				{
					if (k > 0 && r.NextFloat() > 0.35f)
					{
						continue;
					}
					if (Winze(system.Passages[ends[k]], r, sump: made == 0))
					{
						made++;
					}
				}
			}

			private bool Winze(CavePassage way, DeterministicRNG r, bool sump)
			{
				CaveNode end = way.Nodes[way.Nodes.Count - 1];
				Vector3 dir = Flat3(end.Centre - way.Nodes[way.Nodes.Count - 3].Centre);
				float floor = end.Floor;
				float radius = r.Range(1.5f, 1.9f);
				float bottom = floor - r.Range(5f, 12f);
				if (bottom < MinFloor + 0.5f)
				{
					bottom = MinFloor + 0.5f;
				}
				if (floor - bottom < 4f || system.NearChasmOrClimb(end.Centre, end.Radius + 2f) || system.NearHollow(end.Centre, end.Radius + 1f))
				{
					return false;
				}
				Vector3 centre = new Vector3(end.Centre.x, 0f, end.Centre.z) + dir * (radius + 0.3f);
				float top = floor + 2f * end.Radius * Mathf.Max(0.8f, pf.Flat) + 0.6f;
				// All rock where it goes, but where its drift reaches it, and roofed by enough of it.
				for (int k = 0; k <= 8; k++)
				{
					Vector3 p = k == 8 ? centre : centre + Dir(k * 45f) * radius;
					if (walker.Ground(new Vector3(p.x, top, p.z)) - (top + walker.Amp) < MinCover)
					{
						return false;
					}
				}
				for (float y = bottom + 0.8f; y < top; y += 2f)
				{
					for (int k = 0; k <= 8; k++)
					{
						Vector3 p = new Vector3(centre.x, y, centre.z) + (k == 8 ? Vector3.zero : Dir(k * 45f) * radius * 0.9f);
						if (new Vector2(p.x - end.Centre.x, p.z - end.Centre.z).magnitude < end.Radius + 2f && y > floor - 1f)
						{
							continue;
						}
						if (system.Smooth(p) < 0.5f)
						{
							return false;
						}
					}
				}
				Mark mark = Save();
				system.Shafts.Add(new CaveShaft { Bottom = new Vector3(centre.x, bottom, centre.z), Top = top, Radius = radius, Square = true, Yaw = Yaw(dir) });
				system.PrepareField();
				var climb = new CaveClimb { Kind = CaveClimbKind.Ladder, Bottom = centre - dir * (radius - 0.4f), Top = centre - dir * (radius - 0.4f), Out = dir, TopExit = -dir };
				climb.Bottom.y = bottom;
				climb.Top.y = floor;
				if (!AddClimb(climb))
				{
					Restore(mark);
					return false;
				}
				// Under the drainage adit's level, the sump stands full to a little under the drift's floor.
				float water = float.NaN;
				if (sump && floor < system.Mouths[0].Floor.y - 2f)
				{
					water = system.Origin.y + floor - 0.5f;
				}
				system.Pits.Add(new CavePit
				{
					Kind = CavePitKind.Winze, Rim = new Vector3(centre.x, floor, centre.z), Radius = radius, Bottom = bottom,
					Hole = system.Shafts.Count - 1, Way = system.Climbs.Count - 1, Water = water,
				});
				return true;
			}

			// ── Breakdown ──────────────────────────────────────────

			/// <summary>
			/// Breakdown: over part of a chamber the roof has fallen in, leaving a dome raised over it (as high as the rock over
			/// it allows), the fallen blocks heaped in a mound on the floor under it, off the ways; and dead ends choked by falls.
			/// </summary>
			private void Breakdown(DeterministicRNG r)
			{
				int made = 0, most = 1 + 2 * size;
				foreach (int ci in ChambersBy(r, 0.5f, 6f))
				{
					if (made >= most)
					{
						break;
					}
					Lanes();
					if (Dome(ci, r))
					{
						made++;
					}
				}
				int passages = system.Passages.Count;
				for (int pi = 0; pi < passages; pi++)
				{
					if (r.NextFloat() < 0.45f)
					{
						Choke(system.Passages[pi], r);
					}
				}
			}

			private bool Dome(int ci, DeterministicRNG r)
			{
				CaveChamber c = system.Chambers[ci];
				float small = Mathf.Min(c.Radii.x, c.Radii.z);
				float rough = c.Rough > 0f ? c.Rough : 1f;
				float noise = walker.Amp * Mathf.Max(1f, rough);
				for (int attempt = 0; attempt < 6; attempt++)
				{
					float radius = Mathf.Clamp(small * r.Range(0.35f, 0.55f), 4f, 28f);
					if (!SpotIn(c, r, radius + 1.5f, 0f, out Vector3 at))
					{
						continue;
					}
					bool inside = true;
					for (int k = 0; k < 8 && inside; k++)
					{
						Vector3 q = at + Dir(k * 45f) * radius;
						inside = InChamber(c, new Vector3(q.x, FloorOf(c, q) + 2f, q.z), -1f);
					}
					if (!inside || !Dry(at))
					{
						continue;
					}
					float roof = system.RoofAbove(at + Vector3.up * 1.5f);
					if (float.IsNaN(roof))
					{
						continue;
					}
					float room = roof - at.y;
					float half = Mathf.Max(room * r.Range(1.25f, 1.7f), room + 5f) / 1.4f;
					// No taller than the rock over it holds (a lobe's own cover rule).
					float cover = MinCover + 0.5f * noise + 0.25f;
					half = Mathf.Min(half, (walker.Ground(at) - at.y - noise - cover) / 1.4f);
					for (int k = 0; k < 12; k++)
					{
						for (float reach = 0.45f; reach <= 0.91f; reach += 0.45f)
						{
							Vector3 q = at + Dir(k * 30f) * (reach * radius);
							float rise = 0.4f + Mathf.Sqrt(Mathf.Max(0f, 1f - reach * reach));
							half = Mathf.Min(half, (walker.Ground(q) - at.y - noise - cover) / rise);
						}
					}
					if (1.4f * half < room + 3f)
					{
						continue;
					}
					var radii = new Vector3(radius, half, radius);
					if (walker.Crosses(at, at.y, c.Yaw, radii, c.Slope, q => InChamber(c, q, 2f)))
					{
						continue;
					}
					Mark mark = Save();
					system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Dome, A = at, Radius = radius, Depth = half, Top = at.y + 1.5f + Mathf.Abs(c.Slope) * radius });
					system.PrepareField();
					// Its fallen roof heaped under it, off the ways.
					float mound = Mathf.Clamp(radius * r.Range(0.35f, 0.6f), 1.8f, 9f);
					float height = mound * r.Range(0.45f, 0.85f), salt = Mathf.Floor(r.Range(0f, 65535f));
					float block = Mathf.Clamp(0.45f * mound, 0.8f, 2.6f);
					for (int k = 0; k < 6; k++)
					{
						Vector3 spot = k == 0 ? at : at + Dir(r.Range(0f, 360f)) * (radius * r.Range(0.2f, 0.6f));
						spot = FloorPoint(c, spot);
						if (!Clear(spot, mound + block + 0.4f) || !InChamber(c, spot + Vector3.up * 1f + Flat3(spot - c.Centre) * (mound + block), 0f))
						{
							continue;
						}
						system.Solids.Add(new CaveSolid { Kind = CaveSolidKind.Rubble, A = spot, Width = mound, Depth = height, Angle = salt });
						system.PrepareField();
						break;
					}
					if (!ClimbsHold(at, radius + 12f))
					{
						Restore(mark);
						continue;
					}
					return true;
				}
				return false;
			}

			// ── Terraces ───────────────────────────────────────────

			/// <summary>
			/// Terraces: steps and benches along the walls of rooms and halls, each ramped up from the floor (the tall now and
			/// then climbed), and benches along wide passages — a lava tube's along its walls, both sides now and then.
			/// </summary>
			private void Terraces(DeterministicRNG r)
			{
				bool lava = system.Form == CaveForm.LavaTube, terraced = system.Has(CaveTrait.Terraced);
				int made = 0, most = 3 + 4 * size;
				foreach (int ci in ChambersBy(r, terraced ? 0.6f : 0.35f, 5f))
				{
					int want = r.Range(1, 3 + size);
					for (int k = 0; k < want && made < most; k++)
					{
						Lanes();
						CaveTerraceKind kind = lava || r.NextFloat() < 0.4f ? CaveTerraceKind.Bench : CaveTerraceKind.Step;
						if (ChamberTerrace(ci, kind, r))
						{
							made++;
						}
					}
				}
				int passages = system.Passages.Count, benches = 0, mostBenches = lava ? 3 + 4 * size : 1 + 2 * size;
				for (int pi = 0; pi < passages && benches < mostBenches; pi++)
				{
					if (r.NextFloat() > (lava ? 0.8f : 0.3f))
					{
						continue;
					}
					int sides = lava && r.NextFloat() < 0.4f ? 2 : 1;
					float first = r.NextFloat() < 0.5f ? 1f : -1f;
					for (int s = 0; s < sides && benches < mostBenches; s++)
					{
						Lanes();
						if (PassageBench(pi, s == 0 ? first : -first, lava, r))
						{
							benches++;
						}
					}
				}
			}

			/// <summary>Galleries: shelves high on the walls of tall chambers, overhanging the floor, each reached by a ladder or rope ladder.</summary>
			private void Galleries(DeterministicRNG r)
			{
				int made = 0, most = 1 + 2 * size;
				foreach (int ci in ChambersBy(r, 0.6f, 10f))
				{
					CaveChamber c = system.Chambers[ci];
					if (c.Radii.y < 8f)
					{
						continue;
					}
					int want = r.Range(1, 3);
					for (int k = 0; k < want && made < most; k++)
					{
						Lanes();
						if (ChamberTerrace(ci, CaveTerraceKind.Gallery, r))
						{
							made++;
						}
					}
				}
			}

			/// <summary>
			/// A terrace along a chamber's wall: a slab whose walking top runs 1.8–5 m out from the wall's foot (its back runs on
			/// into the rock past the wall's bulge at its height and the noise on it, so it is never left standing free of the
			/// wall), its top a step's or a bench's height (a gallery's high) over the floor at its inner edge, the chamber's slope
			/// carried along it; and a ramp up onto it off one end, along the wall, at a walk — or a climb up its face.
			/// </summary>
			private bool ChamberTerrace(int ci, CaveTerraceKind kind, DeterministicRNG r)
			{
				CaveChamber c = system.Chambers[ci];
				bool gallery = kind == CaveTerraceKind.Gallery;
				Vector3 fwd = Dir(c.Yaw), cross = new Vector3(fwd.z, 0f, -fwd.x);
				var middle = new Vector3(c.Centre.x, c.Floor, c.Centre.z);
				float noise = walker.Amp * Mathf.Max(1f, c.Rough);
				for (int attempt = 0; attempt < 12; attempt++)
				{
					float a = r.Range(0f, 2f * Mathf.PI);
					Vector3 dir = (fwd * Mathf.Cos(a) + cross * Mathf.Sin(a)).normalized;
					float reach = ReachOf(c, dir);
					float walk = gallery ? r.Range(1.8f, 3.2f) : r.Range(2f, Mathf.Clamp(0.35f * reach, 2.6f, 5.5f));
					float h = kind == CaveTerraceKind.Step ? r.Range(0.5f, 1.4f) : kind == CaveTerraceKind.Bench ? r.Range(1.2f, 3f) : r.Range(3.5f, Mathf.Min(8f, 0.45f * c.Radii.y + 1f));
					float length = r.Range(3f, Mathf.Clamp(0.8f * reach, 5f, gallery ? 22f : 18f));
					bool climbed = gallery || kind == CaveTerraceKind.Bench && h >= 2.2f && r.NextFloat() < 0.5f;
					// The ramp no wider than the top it climbs to (a pace of it either side at the least).
					float rampHalf = Mathf.Max(0.75f, Mathf.Min(r.Range(0.8f, 1.3f), 0.5f * (walk - 0.3f)));
					float thick = gallery ? r.Range(1.4f, 2.2f) : h + 3f;
					float inner = reach - walk;
					if (inner < Keep(ci) + 1.5f)
					{
						continue;
					}
					// The wall at its height stands further out than at the floor (a chamber's section swells to its middle):
					// the slab's back runs past the farthest of it, and the noise on it, into the rock.
					float wall = 0f;
					for (float y = Mathf.Max(0f, h - thick); y <= h + 0.01f; y += 0.5f)
					{
						float e = (y - 0.4f * c.Radii.y) / c.Radii.y;
						wall = Mathf.Max(wall, reach / Mathf.Sqrt(1f - 0.16f) * Mathf.Sqrt(Mathf.Max(0f, 1f - e * e)));
					}
					float half = Mathf.Max(0.5f * walk + TerraceOverhang, 0.5f * (wall + noise + 0.6f - inner));
					Vector3 along = new Vector3(dir.z, 0f, -dir.x);
					Vector3 spine = middle + dir * (inner + half);
					Vector3 a0 = spine - along * (0.5f * length), b0 = spine + along * (0.5f * length);
					a0.y = FloorOf(c, a0 - dir * half) + h;
					b0.y = FloorOf(c, b0 - dir * half) + h;
					if (!Dry(a0 - Vector3.up * h) || !Dry(b0 - Vector3.up * h))
					{
						continue;
					}
					if (TryTerrace(a0, b0, half, thick, h, walk, dir, kind, climbed, rampHalf, reach, q => InChamber(c, q, 1.2f), q => FloorOf(c, q), r))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>
			/// A bench along a wide passage's wall over a straight run of its nodes: its inner edge clear of the way's lane, its top
			/// a bench's height over the run's floor, its back run on into the rock past the bore's swell, ramped down to the floor
			/// off one end.
			/// </summary>
			private bool PassageBench(int pi, float sideSign, bool lava, DeterministicRNG r)
			{
				CavePassage p = system.Passages[pi];
				int count = p.Nodes.Count;
				int len = r.Range(3, 7);
				if (count < len + 4)
				{
					return false;
				}
				int i0 = r.Range(2, count - len - 1), i1 = i0 + len;
				CaveNode n0 = p.Nodes[i0], n1 = p.Nodes[i1];
				float rad = float.MaxValue, most = 0f;
				for (int i = i0; i <= i1; i++)
				{
					rad = Mathf.Min(rad, p.Nodes[i].Radius);
					most = Mathf.Max(most, p.Nodes[i].Radius);
				}
				if (rad < 3f)
				{
					return false;
				}
				var a = new Vector3(n0.Centre.x, n0.Floor, n0.Centre.z);
				var b = new Vector3(n1.Centre.x, n1.Floor, n1.Centre.z);
				Vector3 run = Flat3(b - a);
				float runLength = Mathf.Max(1e-3f, new Vector2(b.x - a.x, b.z - a.z).magnitude);
				Vector3 side = new Vector3(run.z, 0f, -run.x) * sideSign;
				float dev = 0f;
				for (int i = i0; i <= i1; i++)
				{
					Vector3 o = p.Nodes[i].Centre - a;
					dev = Mathf.Max(dev, Mathf.Abs(o.x * side.x + o.z * side.z));
				}
				if (dev > 0.8f || KeptAt(a) || KeptAt(b) || !Dry(a) || !Dry(b))
				{
					return false;
				}
				// The bore's half width at its floor (it is cut flat FloorDepth of its section up from its bottom); it swells to
				// its full radius at its middle, and the slab's back runs past that and the noise into the rock.
				float floorHalf = rad * Mathf.Sqrt(1f - FloorDepth * FloorDepth);
				float inner = WayLane + 0.5f + dev;
				float walk = floorHalf - inner;
				if (walk < 1.8f)
				{
					return false;
				}
				float rampHalf = Mathf.Max(0.75f, Mathf.Min(r.Range(0.8f, 1.1f), 0.5f * (walk - 0.3f)));
				float h = lava ? r.Range(0.6f, 1.6f) : r.Range(0.5f, 1.3f);
				float half = Mathf.Max(0.5f * walk + TerraceOverhang, 0.5f * (most + dev + walker.Amp + 0.6f - inner));
				Vector3 a0 = a + side * (inner + half) + Vector3.up * h, b0 = b + side * (inner + half) + Vector3.up * h;
				float LineFloor(Vector3 q) => Mathf.Lerp(a.y, b.y, Mathf.Clamp01(Vector3.Dot(q - a, run) / runLength));
				float FloorAt(Vector3 q)
				{
					float found = system.FloorBelow(new Vector3(q.x, LineFloor(q) + 1.5f, q.z), 3.5f);
					return float.IsNaN(found) ? LineFloor(q) : found;
				}
				bool Own(Vector3 q) => InRun(p, i0 - 3, i1 + 3, q, 1.5f) && !InAnyChamber(q, 1f);
				return TryTerrace(a0, b0, half, h + 2.5f, h, walk, side, CaveTerraceKind.Bench, false, rampHalf, float.PositiveInfinity, Own, FloorAt, r);
			}

			/// <summary>
			/// The common part of a terrace: its slab and its way up chosen and kept off the ways and out of every void but its own,
			/// then laid and walked on the field as it stands (up its ramp and along its top, or off the top of its climb); false,
			/// and nothing kept, where any of it fails.
			/// </summary>
			/// <param name="dir">Out from the room's middle (or the passage's line) to the wall: the terrace's inner edge faces back along it.</param>
			/// <param name="wallRadius">How far the wall behind it stands from the middle it curves round (infinite along a passage's straight run).</param>
			private bool TryTerrace(Vector3 a0, Vector3 b0, float half, float thick, float h, float walk, Vector3 dir, CaveTerraceKind kind, bool climbed, float rampHalf,
				float wallRadius, Func<Vector3, bool> own, Func<Vector3, float> floorAt, DeterministicRNG r)
			{
				Vector3 along = Flat3(b0 - a0);
				float length = new Vector2(b0.x - a0.x, b0.z - a0.z).magnitude;
				Vector3 toRoom = -dir;
				// Clear of the ways over its walking top, from its inner edge back to the wall's foot (its back is in the rock,
				// where no way runs but one coming in through the wall, which the walking top's own points meet), at the floor;
				// and filling no void but its own.
				for (float w = 0f; w <= walk + 0.51f; w += 1f)
				{
					// Its rounded ends reach past its line's ends by as much as the slab's round end does at this far in.
					float v = half - w, cap = Mathf.Sqrt(Mathf.Max(0f, half * half - v * v));
					for (float u = -cap; u <= length + cap + 0.01f; u += 1f)
					{
						if (!Clear(a0 + along * u + toRoom * v - Vector3.up * h, 0.3f, 4f + h))
						{
							return false;
						}
					}
				}
				if (!OnlyOwn(CapsuleSamples(a0, b0, half, thick), own))
				{
					return false;
				}
				CaveSolid ramp = default;
				CaveClimb climb = default;
				if (!climbed)
				{
					// Off one end along the wall, its inner side flush with the terrace's inner edge; its top end where its line
					// leaves the slab's rounded end, less a little, so the two tops meet without a step.
					float v = half - rampHalf - 0.15f;
					float exit = Mathf.Sqrt(Mathf.Max(0f, half * half - v * v));
					bool placed = false;
					float firstEnd = r.NextFloat() < 0.5f ? 1f : -1f;
					for (int e = 0; e < 2 && !placed; e++)
					{
						float sgn = e == 0 ? firstEnd : -firstEnd;
						Vector3 end = sgn > 0f ? b0 : a0;
						Vector3 top = end + toRoom * v + along * (sgn * (exit - 0.6f));
						// Down to the floor away from the terrace at a walk; on a sloping floor it meets the floor sooner going up
						// the slope (the ramp near level) and later going down it (the ramp at its steepest, or not at all). In a
						// chamber it bends in with the wall's curve, so its foot is on the floor and not in the wall.
						float guess = 0.5f * length + exit + 0.5f * h / TerraceRampGrade;
						float bend = float.IsInfinity(wallRadius) ? 0f : Mathf.Clamp(guess / Mathf.Max(3f, wallRadius), 0f, 0.7f);
						Vector3 run = (along * (sgn * Mathf.Cos(bend)) + toRoom * Mathf.Sin(bend)).normalized;
						float under = floorAt(top);
						float slope = (floorAt(top + run * 3f) - under) / 3f;
						float grade = Mathf.Clamp(TerraceRampGrade - slope, 0.04f, TerraceRampGrade);
						if (grade + slope < 0.05f)
						{
							continue;
						}
						float rampRun = (top.y - under) / (grade + slope);
						Vector3 foot = top + run * rampRun;
						foot.y = floorAt(foot);
						float drop = top.y - foot.y;
						if (rampRun > 14f || drop / rampRun > TerraceRampMost || drop / rampRun < -0.1f || Mathf.Abs(foot.y - (top.y - grade * rampRun)) > 0.5f)
						{
							continue;
						}
						// All of it in the open (its sides a pace clear of the wall), not run into the rock.
						bool open = true;
						Vector3 across = new Vector3(run.z, 0f, -run.x) * (rampHalf + 0.3f);
						for (float t = 0f; t <= 1.001f && open; t += 0.2f)
						{
							Vector3 q = Vector3.Lerp(foot, top, t) + Vector3.up * 1.2f;
							open = system.Smooth(q) < -0.3f && system.Smooth(q + across) < 0f && system.Smooth(q - across) < 0f;
						}
						if (!open)
						{
							continue;
						}
						Vector3 off = top - Vector3.up * (top.y - foot.y);
						if (!LineClear(off + run * 0.6f, foot + run * 0.8f, rampHalf + 0.3f) || !OnlyOwn(CapsuleSamples(foot, top, rampHalf, top.y - foot.y + 1f), own))
						{
							continue;
						}
						ramp = new CaveSolid { Kind = CaveSolidKind.Ramp, A = foot, B = top, Width = rampHalf };
						placed = true;
					}
					if (!placed)
					{
						return false;
					}
				}
				else
				{
					// Up its face from the floor at its inner edge: a ladder leant on the lip, its foot out a fifth of its height.
					Vector3 mid = Vector3.Lerp(a0, b0, r.Range(0.3f, 0.7f));
					Vector3 lip = mid + toRoom * half;
					Vector3 foot = lip + toRoom * Mathf.Clamp(0.2f * h, 0.45f, 1.2f);
					foot.y = floorAt(foot);
					climb = new CaveClimb
					{
						Kind = kind == CaveTerraceKind.Gallery && r.NextFloat() < 0.4f ? CaveClimbKind.RopeLadder : CaveClimbKind.Ladder,
						Bottom = foot,
						Top = new Vector3(lip.x, mid.y, lip.z) - toRoom * 0.05f,
						Out = toRoom,
						TopExit = -toRoom,
					};
					if (!Clear(foot, 0.3f))
					{
						return false;
					}
				}
				Mark mark = Save();
				system.Solids.Add(new CaveSolid { Kind = CaveSolidKind.Terrace, A = a0, B = b0, Width = half, Depth = thick });
				int slabIndex = system.Solids.Count - 1, rampIndex = -1, climbIndex = -1;
				if (!climbed)
				{
					system.Solids.Add(ramp);
					rampIndex = system.Solids.Count - 1;
					system.PrepareField();
				}
				else
				{
					system.PrepareField();
					if (!AddClimb(climb))
					{
						Restore(mark);
						return false;
					}
					climbIndex = system.Climbs.Count - 1;
				}
				var terrace = new CaveTerrace { Kind = kind, Solid = slabIndex, Ramp = rampIndex, Climb = climbIndex, Height = h, Walk = walk, Out = toRoom };
				if (!system.WalkFits(system.TerraceWalk(terrace), out _) || !ClimbsHold(0.5f * (a0 + b0), 0.5f * length + half + 12f))
				{
					Restore(mark);
					return false;
				}
				system.Terraces.Add(terrace);
				return true;
			}

			// ── Pits ───────────────────────────────────────────────

			/// <summary>
			/// Pits in the floors: potholes off the ways in rooms and wide passages (now and then in any cave; often in a pitted
			/// one), shafts dropping off a floor with a rope or ladder down them, and in the halls great pits wound round by a ledge.
			/// </summary>
			private void Pits(DeterministicRNG r)
			{
				if (system.Form == CaveForm.Mine || system.Form == CaveForm.SeaCave)
				{
					return;
				}
				bool pitted = system.Has(CaveTrait.Pitted);
				int made = 0, most = pitted ? 3 + 4 * size : 1 + size;
				foreach (int ci in ChambersBy(r, pitted ? 0.6f : 0.15f, 5f))
				{
					int want = pitted ? r.Range(1, 3 + size) : 1;
					for (int k = 0; k < want && made < most; k++)
					{
						Lanes();
						CaveChamber c = system.Chambers[ci];
						float roll = r.NextFloat();
						CavePitKind kind = !pitted || roll < 0.45f ? CavePitKind.Pothole
							: roll < 0.6f ? CavePitKind.Sinkhole
							: roll < 0.85f || Mathf.Min(c.Radii.x, c.Radii.z) < 18f || size == 0 ? CavePitKind.Shaft : CavePitKind.Ledged;
						if (ChamberPit(ci, kind, r))
						{
							made++;
						}
					}
				}
				// Small potholes (1.5–2.6 m across) scoured into wide passages' floors beside their lanes: now and then in any cave,
				// often in a pitted one.
				int passages = system.Passages.Count, scours = 0, mostScours = pitted ? 3 + 3 * size : 1 + 2 * size;
				for (int pi = 0; pi < passages && scours < mostScours; pi++)
				{
					if (r.NextFloat() < (pitted ? 0.45f : 0.2f))
					{
						Lanes();
						for (int attempt = 0; attempt < 3; attempt++)
						{
							if (PassagePothole(system.Passages[pi], r))
							{
								scours++;
								break;
							}
						}
					}
				}
			}

			/// <summary>Rimstone: flights of three to six shallow gour pools stepping across a floor, each against the last.</summary>
			private void Gours(DeterministicRNG r)
			{
				int flights = 0, most = 1 + 2 * size;
				foreach (int ci in ChambersBy(r, 0.5f, 5f))
				{
					if (flights >= most)
					{
						break;
					}
					CaveChamber c = system.Chambers[ci];
					Lanes();
					// Pools to the room's size: a small room's a few hand-wide basins, a hall's broad ones.
					int pools = r.Range(3, 7), laid = 0;
					float largest = Mathf.Clamp(0.18f * Mathf.Min(c.Radii.x, c.Radii.z), 1f, 2.6f);
					if (!SpotIn(c, r, 2f * largest + 0.5f * walker.Amp + 0.8f, Keep(ci), out Vector3 at))
					{
						continue;
					}
					Vector3 heading = Dir(r.Range(0f, 360f));
					float radius = r.Range(0.8f, largest);
					for (int k = 0; k < pools * 2 && laid < pools; k++)
					{
						if (Basin(c, at, radius, r.Range(0.35f, 0.8f), CavePitKind.Gour))
						{
							laid++;
						}
						float next = r.Range(0.8f, largest);
						float turn = r.Range(-50f, 50f) * Mathf.Deg2Rad, cs = Mathf.Cos(turn), sn = Mathf.Sin(turn);
						heading = new Vector3(heading.x * cs + heading.z * sn, 0f, heading.z * cs - heading.x * sn);
						at = FloorPoint(c, at + heading * (radius + next + 0.6f));
						radius = next;
					}
					if (laid > 0)
					{
						flights++;
					}
				}
			}

			/// <summary>An ice cave's cold trap: a broad basin in its deepest chamber, off its middle and its ways.</summary>
			private void ColdTrap(DeterministicRNG r)
			{
				int deepest = -1;
				for (int i = 0; i < system.Chambers.Count; i++)
				{
					CaveChamber c = system.Chambers[i];
					if (Mathf.Min(c.Radii.x, c.Radii.z) >= 6f && (deepest < 0 || c.Floor < system.Chambers[deepest].Floor))
					{
						deepest = i;
					}
				}
				if (deepest < 0)
				{
					return;
				}
				Lanes();
				ChamberPit(deepest, CavePitKind.ColdTrap, r);
			}

			private bool ChamberPit(int ci, CavePitKind kind, DeterministicRNG r)
			{
				CaveChamber c = system.Chambers[ci];
				float small = Mathf.Min(c.Radii.x, c.Radii.z), noise = walker.Amp * Mathf.Max(1f, c.Rough);
				for (int attempt = 0; attempt < 12; attempt++)
				{
					switch (kind)
					{
						case CavePitKind.Pothole:
						case CavePitKind.Sinkhole:
						case CavePitKind.ColdTrap:
						{
							float radius = kind == CavePitKind.Pothole ? r.Range(1.2f, Mathf.Clamp(0.25f * small, 1.6f, 3.5f))
								: kind == CavePitKind.ColdTrap ? Mathf.Clamp(small * r.Range(0.3f, 0.45f), 3f, 12f) : r.Range(3.5f, Mathf.Clamp(0.3f * small, 4f, 7f));
							float want = kind == CavePitKind.Pothole ? r.Range(0.8f, 2.2f) : r.Range(1.5f, 3f);
							float spill = radius + (0.3f + Mathf.Abs(c.Slope) * 2f * radius) / BowlGrade;
							if (SpotIn(c, r, spill + 0.5f * noise + 0.8f, Keep(ci), out Vector3 at) && Basin(c, at, radius, want, kind))
							{
								return true;
							}
							break;
						}
						case CavePitKind.Shaft:
						{
							float radius = Mathf.Max(1.8f, walker.Amp + 1.5f) * r.Range(1f, 1.3f);
							if (SpotIn(c, r, radius + 0.5f * noise + 1.5f, Keep(ci), out Vector3 at) && ShaftPit(c, at, radius, r))
							{
								return true;
							}
							break;
						}
						default:
						{
							float ledge = r.Range(2.2f, 3f), pitch = r.Range(1.1f, 1.8f);
							float radius = Mathf.Clamp(pitch / LedgeGrade + ledge + r.Range(0.5f, 3f), 7f, size >= 2 ? 13f : 10f);
							if (SpotIn(c, r, radius + 0.5f * noise + 2f, Keep(ci), out Vector3 at) && LedgedPit(c, at, radius, ledge, pitch, r))
							{
								return true;
							}
							break;
						}
					}
				}
				return false;
			}

			/// <summary>
			/// A basin sunk in a chamber's floor at a point: its sides at <see cref="BowlGrade"/>, as deep as they allow (no deeper
			/// than <paramref name="want"/>), its rim clear of the ways and the walls, all rock under it, its bottom over the
			/// lowest floor allowed; walked out of on the field once cut.
			/// </summary>
			private bool Basin(in CaveChamber c, Vector3 at, float radius, float want, CavePitKind kind)
			{
				float depth = Mathf.Min(want, BowlGrade * 0.7f * radius);
				float top = at.y + 0.3f + Mathf.Abs(c.Slope) * 2f * radius;
				float spill = radius + (top - at.y) / BowlGrade;
				float bottom = at.y - depth;
				if (depth < 0.3f || bottom < MinFloor + 0.5f || !Dry(at))
				{
					return false;
				}
				// Off the wall by a pace past its reach, and clear of the ways and features round it.
				Vector3 outward = Flat3(at - c.Centre);
				if (!InChamber(c, at + Vector3.up * 1f + outward * (spill + 0.6f), -0.3f) || !Clear(at, spill + 0.2f))
				{
					return false;
				}
				if (!FloorAround(c, at, spill + 1f) || !RockBelow(at, spill + 0.5f, bottom - 1.2f, at.y - 0.8f))
				{
					return false;
				}
				Mark mark = Save();
				system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Bowl, A = at, Radius = radius, Depth = depth, Top = top });
				var pit = new CavePit { Kind = kind, Rim = at, Radius = radius, Bottom = bottom, Hole = system.Hollows.Count - 1, Way = -1, Water = float.NaN };
				system.PrepareField();
				if (system.PitWayOut(pit) != null || !ClimbsHold(at, spill + 8f))
				{
					Restore(mark);
					return false;
				}
				system.Pits.Add(pit);
				return true;
			}

			/// <summary>A pothole in a wide passage's floor beside its lane, where the bore is wide enough to keep the lane and a pace beside it.</summary>
			private bool PassagePothole(CavePassage p, DeterministicRNG r)
			{
				int count = p.Nodes.Count;
				if (count < 6)
				{
					return false;
				}
				int i = r.Range(2, count - 3);
				CaveNode n = p.Nodes[i];
				if (InAnyChamber(n.Centre, 2f))
				{
					return false;
				}
				Vector3 run = Flat3(p.Nodes[i + 1].Centre - p.Nodes[i - 1].Centre);
				Vector3 side = new Vector3(run.z, 0f, -run.x) * (r.NextFloat() < 0.5f ? 1f : -1f);
				float floorHalf = n.Radius * Mathf.Sqrt(1f - FloorDepth * FloorDepth);
				float runLength = new Vector2(p.Nodes[i + 1].Centre.x - p.Nodes[i - 1].Centre.x, p.Nodes[i + 1].Centre.z - p.Nodes[i - 1].Centre.z).magnitude;
				float slope = Mathf.Abs(p.Nodes[i + 1].Floor - p.Nodes[i - 1].Floor) / Mathf.Max(0.5f, runLength);
				float radius = r.Range(0.75f, Mathf.Clamp(0.3f * n.Radius, 0.9f, 2f));
				float top = n.Floor + 0.3f + slope * 2f * radius;
				float spill = radius + (top - n.Floor) / BowlGrade;
				// Its rim at the lane's edge and a little more; it may cut a little under the wall's foot.
				float offset = WayLane + 0.2f + spill;
				if (offset + spill > floorHalf + 0.3f)
				{
					return false;
				}
				var at = new Vector3(n.Centre.x, n.Floor, n.Centre.z) + side * offset;
				float depth = Mathf.Min(r.Range(0.4f, 1.2f), BowlGrade * 0.7f * radius);
				float bottom = at.y - depth;
				if (depth < 0.3f || bottom < MinFloor + 0.5f || !Dry(at) || KeptAt(at) || !Clear(at, spill + 0.15f) || system.NearHollow(at, spill))
				{
					return false;
				}
				// The floor round it is the passage's (where it is not the wall's foot), and all rock under it.
				for (int k = 0; k < 8; k++)
				{
					Vector3 q = at + Dir(k * 45f) * (spill + 0.3f);
					float expect = n.Floor + Vector3.Dot(q - at, Flat3(p.Nodes[i + 1].Centre - p.Nodes[i - 1].Centre)) * (p.Nodes[i + 1].Floor - p.Nodes[i - 1].Floor) / Mathf.Max(0.5f, runLength);
					if (system.Smooth(new Vector3(q.x, expect + 1.2f, q.z)) >= 0f)
					{
						continue;
					}
					float found = system.FloorBelow(new Vector3(q.x, expect + 1.2f, q.z), 2.5f);
					if (float.IsNaN(found) || Mathf.Abs(found - expect) > 0.5f)
					{
						return false;
					}
				}
				if (!RockBelow(at, spill + 0.5f, bottom - 1.2f, at.y - 0.8f))
				{
					return false;
				}
				Mark mark = Save();
				system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Bowl, A = at, Radius = radius, Depth = depth, Top = top });
				var pit = new CavePit { Kind = CavePitKind.Pothole, Rim = at, Radius = radius, Bottom = bottom, Hole = system.Hollows.Count - 1, Way = -1, Water = float.NaN };
				system.PrepareField();
				var lane = new List<Vector3>();
				for (int k = Mathf.Max(0, i - 2); k <= Mathf.Min(count - 1, i + 2); k++)
				{
					lane.Add(new Vector3(p.Nodes[k].Centre.x, p.Nodes[k].Floor + 0.3f, p.Nodes[k].Centre.z));
				}
				if (system.PitWayOut(pit) != null || !system.WalkFits(lane, out _))
				{
					Restore(mark);
					return false;
				}
				system.Pits.Add(pit);
				return true;
			}

			/// <summary>A vadose shaft dropping off a chamber's floor, 4–16 m, a rope (or rope ladder) down its wall to its bottom.</summary>
			private bool ShaftPit(in CaveChamber c, Vector3 at, float radius, DeterministicRNG r)
			{
				float bottom = Mathf.Max(at.y - r.Range(4f, 12f + 4f * size), MinFloor + 0.5f);
				if (at.y - bottom < 3.5f || !Dry(at))
				{
					return false;
				}
				Vector3 outward = Flat3(at - c.Centre);
				if (!InChamber(c, at + Vector3.up * 1f + outward * (radius + 1.5f), -0.3f) || !Clear(at, radius + 0.8f))
				{
					return false;
				}
				if (!FloorAround(c, at, radius + 1.2f, 2.6f) || !RockBelow(at, radius + 1f, bottom - 1f, at.y - 1f))
				{
					return false;
				}
				float rimTop = float.MinValue;
				for (int k = 0; k < 8; k++)
				{
					rimTop = Mathf.Max(rimTop, FloorOf(c, at + Dir(k * 45f) * radius));
				}
				Mark mark = Save();
				system.Shafts.Add(new CaveShaft { Bottom = new Vector3(at.x, bottom, at.z), Top = rimTop + 0.6f, Radius = radius, Yaw = c.Yaw });
				system.PrepareField();
				// Down the wall on the side toward the chamber's middle first: its top steps off toward the ways.
				float toward = Yaw(Flat3(c.Centre - at));
				CaveClimbKind kind = r.NextFloat() < 0.5f ? CaveClimbKind.Rope : CaveClimbKind.RopeLadder;
				for (int k = 0; k < 8; k++)
				{
					Vector3 wallward = Dir(toward + (k % 2 == 0 ? 1f : -1f) * ((k + 1) / 2) * 45f);
					Vector3 line = at + wallward * (radius - 0.4f);
					var climb = new CaveClimb
					{
						Kind = kind,
						Bottom = new Vector3(line.x, bottom, line.z),
						Top = new Vector3(line.x, FloorOf(c, at + wallward * (radius + 0.6f)), line.z),
						Out = -wallward,
						TopExit = wallward,
					};
					if (AddClimb(climb))
					{
						var pit = new CavePit
						{
							Kind = CavePitKind.Shaft, Rim = at, Radius = radius, Bottom = bottom, Hole = system.Shafts.Count - 1, Way = system.Climbs.Count - 1, Water = float.NaN,
						};
						if (system.PitWayOut(pit) == null && ClimbsHold(at, radius + 8f))
						{
							system.Pits.Add(pit);
							return true;
						}
						break;
					}
				}
				Restore(mark);
				return false;
			}

			/// <summary>
			/// A great pit in a hall's floor, 8–24 m deep, a ledge wound down its wall from the floor at its rim (on the side toward
			/// the hall's middle) to its bottom: walked down, and walked back up.
			/// </summary>
			private bool LedgedPit(in CaveChamber c, Vector3 at, float radius, float ledge, float pitch, DeterministicRNG r)
			{
				float bottom = Mathf.Max(at.y - r.Range(8f, 12f + 6f * size), MinFloor + 0.5f);
				if (at.y - bottom < 6f || !Dry(at))
				{
					return false;
				}
				Vector3 outward = Flat3(at - c.Centre);
				if (!InChamber(c, at + Vector3.up * 1f + outward * (radius + 3f), -0.5f) || !Clear(at, radius + 1.5f))
				{
					return false;
				}
				if (!FloorAround(c, at, radius + 1.5f, 2.8f) || !RockBelow(at, radius + 1f, bottom - 1.5f, at.y - 1f))
				{
					return false;
				}
				Vector3 start = Flat3(c.Centre - at);
				float hand = r.NextFloat() < 0.5f ? -1f : 1f;
				float startFloor = FloorOf(c, at + start * (radius - 0.5f * ledge));
				float rimTop = float.MinValue;
				for (int k = 0; k < 8; k++)
				{
					rimTop = Mathf.Max(rimTop, FloorOf(c, at + Dir(k * 45f) * radius));
				}
				Mark mark = Save();
				system.Shafts.Add(new CaveShaft { Bottom = new Vector3(at.x, bottom, at.z), Top = rimTop + 0.6f, Radius = radius });
				system.Solids.Add(new CaveSolid
				{
					Kind = CaveSolidKind.Spiral,
					A = new Vector3(at.x, bottom, at.z),
					B = new Vector3(at.x, startFloor, at.z),
					Width = ledge,
					Depth = radius,
					Arch = hand * pitch,
					Angle = Yaw(start),
				});
				system.PrepareField();
				var pit = new CavePit
				{
					Kind = CavePitKind.Ledged, Rim = at, Radius = radius, Bottom = bottom, Hole = system.Shafts.Count - 1, Way = system.Solids.Count - 1, Water = float.NaN,
				};
				if (!LedgeWalks(system.Solids[pit.Way]) || !ClimbsHold(at, radius + 8f))
				{
					Restore(mark);
					return false;
				}
				// And onto its top from the floor beside the rim.
				var onto = new List<Vector3> { at + start * (radius + 2.5f) + Vector3.up * 0.3f, at + start * (radius - 0.5f * ledge) + Vector3.up * 0.3f };
				if (!system.WalkFits(onto, out _))
				{
					Restore(mark);
					return false;
				}
				system.Pits.Add(pit);
				return true;
			}

			/// <summary>Whether a spiral's ledge can be walked from its top to its foot (the fixture's own pit check, at 40°).</summary>
			private bool LedgeWalks(in CaveSolid pit)
			{
				float pitch = Mathf.Abs(pit.Arch), hand = Mathf.Sign(pit.Arch);
				float last = (pit.B.y - pit.A.y) / pitch;
				float radius = pit.Depth - 0.5f * pit.Width;
				float previous = float.NaN;
				for (float phi = 0.05f; phi < last - 0.05f; phi += 0.5f / radius)
				{
					float yaw = pit.Angle * Mathf.Deg2Rad + hand * phi;
					float ledge = pit.B.y - phi * pitch;
					var p = new Vector3(pit.A.x + Mathf.Sin(yaw) * radius, ledge + 1f, pit.A.z + Mathf.Cos(yaw) * radius);
					float floor = system.FloorBelow(p, 2f);
					if (float.IsNaN(floor) || Mathf.Abs(floor - ledge) > 0.3f || system.Void(new Vector3(p.x, floor + 0.6f, p.z)) >= 0f
						|| system.Void(new Vector3(p.x, floor + 1.4f, p.z)) >= 0f || system.Void(new Vector3(p.x, floor + 2.25f, p.z)) >= 0f)
					{
						return false;
					}
					if (!float.IsNaN(previous) && Mathf.Atan2(Mathf.Abs(floor - previous), 0.5f) * Mathf.Rad2Deg > 40f)
					{
						return false;
					}
					previous = floor;
				}
				return true;
			}

			// ── Columns and arches ─────────────────────────────────

			/// <summary>Dripstone columns, stalagmite and stalactite grown into one, standing in rooms off the ways.</summary>
			private void Pillars(DeterministicRNG r)
			{
				foreach (int ci in ChambersBy(r, 0.5f, 4f))
				{
					Lanes();
					// Columns to the room's size: slender ones in a small room, stout ones in a hall.
					float small = Mathf.Min(system.Chambers[ci].Radii.x, system.Chambers[ci].Radii.z);
					PillarsIn(ci, r.Range(1, 3 + size), r, 0.35f, Mathf.Clamp(0.12f * small, 0.6f, 1.6f), 0.4f, 1.2f);
				}
			}

			private void PillarsIn(int ci, int want, DeterministicRNG r, float radiusMin, float radiusMax, float flareMin, float flareMax)
			{
				CaveChamber c = system.Chambers[ci];
				float noise = walker.Amp * Mathf.Max(1f, c.Rough);
				for (int k = 0, tries = 0; k < want && tries < want * 5; tries++)
				{
					float radius = r.Range(radiusMin, radiusMax), flare = r.Range(flareMin, flareMax);
					float reach = radius * (1f + flare);
					if (!SpotIn(c, r, reach + noise + 0.6f, Keep(ci) + 0.5f * reach, out Vector3 at))
					{
						continue;
					}
					if (!Clear(at, reach + 0.3f) || system.NearHollow(at, reach + 0.5f) || !InChamber(c, at + Vector3.up + Flat3(at - c.Centre) * (reach + 0.3f), -0.2f))
					{
						continue;
					}
					float floor = system.FloorBelow(new Vector3(at.x, at.y + 1.5f, at.z), 3f);
					float roof = float.IsNaN(floor) ? float.NaN : system.RoofAbove(new Vector3(at.x, floor + 1f, at.z), 60f);
					if (float.IsNaN(roof) || roof - floor < 3f || Mathf.Abs(floor - at.y) > 0.4f)
					{
						continue;
					}
					system.Solids.Add(new CaveSolid { Kind = CaveSolidKind.Column, A = new Vector3(at.x, floor, at.z), B = new Vector3(at.x, roof, at.z), Width = radius, Depth = flare });
					system.PrepareField();
					k++;
				}
			}

			/// <summary>
			/// Natural arches: a rib of rock left spanning a hall or a great room over the way across it, its feet off the way, the
			/// way walked under it after.
			/// </summary>
			private void Arches(DeterministicRNG r)
			{
				int made = 0, most = 1 + size;
				foreach (int ci in ChambersBy(r, 0.6f, 9f))
				{
					if (made >= most)
					{
						break;
					}
					Lanes();
					CaveChamber c = system.Chambers[ci];
					var inside = new List<(Vector3 a, Vector3 b)>();
					foreach ((Vector3 a, Vector3 b) in lanes)
					{
						Vector3 m = 0.5f * (a + b);
						if (InChamber(c, m + Vector3.up * 1.5f, -1f) && new Vector2(b.x - a.x, b.z - a.z).magnitude > 6f)
						{
							inside.Add((a, b));
						}
					}
					if (inside.Count == 0)
					{
						continue;
					}
					for (int attempt = 0; attempt < 4; attempt++)
					{
						(Vector3 a, Vector3 b) = inside[r.Range(0, inside.Count)];
						if (Arch(c, a, b, r))
						{
							made++;
							break;
						}
					}
				}
			}

			private bool Arch(in CaveChamber c, Vector3 a, Vector3 b, DeterministicRNG r)
			{
				Vector3 way = Flat3(b - a), across = new Vector3(way.z, 0f, -way.x);
				Vector3 under = Vector3.Lerp(a, b, r.Range(0.3f, 0.7f));
				under = FloorPoint(c, under);
				float rib = r.Range(0.6f, 1.3f);
				float half = WayLane + 1.7f * rib + 1.2f + r.Range(0f, 4f);
				float roof = system.RoofAbove(under + Vector3.up * 1.5f);
				if (float.IsNaN(roof))
				{
					return false;
				}
				float room = roof - under.y;
				float rise = r.Range(3.5f, Mathf.Min(12f, room - 2.6f * rib - 2.5f));
				if (rise < 3.5f)
				{
					return false;
				}
				Vector3 fa = FloorPoint(c, under - across * half), fb = FloorPoint(c, under + across * half);
				foreach (Vector3 foot in new[] { fa, fb })
				{
					float found = system.FloorBelow(foot + Vector3.up * 1.5f, 3f);
					if (float.IsNaN(found) || Mathf.Abs(found - foot.y) > 0.4f || !Clear(foot, 1.7f * rib + 0.6f) || system.NearHollow(foot, 1.7f * rib + 0.5f))
					{
						return false;
					}
				}
				// All of it inside this chamber, its crown under the roof with room to spare.
				for (int k = 0; k <= 8; k++)
				{
					float t = Mathf.PI * k / 8f;
					Vector3 q = Vector3.Lerp(fa, fb, 0.5f + 0.5f * Mathf.Cos(t)) + Vector3.up * (Mathf.Sin(t) * (rise + rib));
					if (!InChamber(c, q, -1.5f) || system.Smooth(q + Vector3.up * (2.6f * rib + 0.8f)) >= 0f)
					{
						return false;
					}
				}
				Mark mark = Save();
				system.Solids.Add(new CaveSolid { Kind = CaveSolidKind.Arch, A = fa, B = fb, Width = rib, Depth = rise });
				system.PrepareField();
				float back = Mathf.Min(5f, new Vector2(under.x - a.x, under.z - a.z).magnitude), on = Mathf.Min(5f, new Vector2(b.x - under.x, b.z - under.z).magnitude);
				var path = new List<Vector3> { FloorPoint(c, under - way * back) + Vector3.up * 0.3f, FloorPoint(c, under + way * on) + Vector3.up * 0.3f };
				if (!system.WalkFits(path, out _) || !ClimbsHold(under, half + 8f))
				{
					Restore(mark);
					return false;
				}
				return true;
			}

			// ── Avens ──────────────────────────────────────────────

			/// <summary>
			/// Avens: round shafts rising 5–22 m from the roofs of rooms and passages into the rock, where water once fell in down
			/// a joint — several in a fissured or pitted cave, one or two in any other limestone or ice cave. Each keeps its
			/// full cover of rock over its top and opens into nothing else.
			/// </summary>
			private void Avens(DeterministicRNG r)
			{
				if (system.Form == CaveForm.Mine || system.Form == CaveForm.LavaTube || system.Form == CaveForm.SeaCave || system.Form == CaveForm.Grotto)
				{
					return;
				}
				bool many = system.Has(CaveTrait.Fissured) || system.Has(CaveTrait.Pitted);
				int want = many ? r.Range(2, 4 + 2 * size) : r.Range(1, 3);
				Lanes();
				for (int k = 0, tries = 0; k < want && tries < want * 6; tries++)
				{
					// A chamber's roof as often as a passage's.
					Vector3 at;
					float bore;
					if (r.NextFloat() < 0.5f && system.Chambers.Count > 0)
					{
						CaveChamber c = system.Chambers[r.Range(0, system.Chambers.Count)];
						if (!SpotIn(c, r, 2.5f, 0f, out at))
						{
							continue;
						}
						bore = Mathf.Min(c.Radii.x, c.Radii.z);
					}
					else
					{
						CavePassage p = system.Passages[r.Range(0, system.Passages.Count)];
						CaveNode n = p.Nodes[r.Range(0, p.Nodes.Count)];
						at = new Vector3(n.Centre.x, n.Floor, n.Centre.z);
						bore = n.Radius;
					}
					if (KeptAt(at) || !Dry(at) || system.Flooded(at))
					{
						continue;
					}
					float roof = system.RoofAbove(at + Vector3.up * 1.5f, 40f);
					if (float.IsNaN(roof))
					{
						continue;
					}
					float radius = r.Range(0.8f, Mathf.Clamp(0.35f * bore, 1f, 2.2f));
					float top = roof + r.Range(5f, 12f + 5f * size);
					// No higher than the rock over it holds, and nothing else in its way up.
					float cover = float.MaxValue;
					for (int q = 0; q <= 4; q++)
					{
						Vector3 c = q == 4 ? at : at + Dir(45f + 90f * q) * (1.42f * radius);
						cover = Mathf.Min(cover, walker.Ground(c) - walker.Amp - MinCover - 0.5f);
					}
					top = Mathf.Min(top, cover);
					if (top - roof < 4f)
					{
						continue;
					}
					bool clear = true;
					for (float y = roof + 1f; y <= top + 1.5f && clear; y += 1.5f)
					{
						for (int q = 0; q <= 8 && clear; q++)
						{
							Vector3 c = q == 8 ? at : at + Dir(q * 45f) * (radius + 1.5f);
							clear = system.Smooth(new Vector3(c.x, y, c.z)) >= 0.6f;
						}
					}
					if (!clear || system.NearChasmOrClimb(new Vector3(at.x, roof, at.z), radius + 1f))
					{
						continue;
					}
					Mark mark = Save();
					system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Aven, A = new Vector3(at.x, roof - 0.5f, at.z), Radius = radius, Top = top });
					system.PrepareField();
					if (!ClimbsHold(at, radius + 6f))
					{
						Restore(mark);
						continue;
					}
					k++;
				}
			}

			// ── Keyholes ───────────────────────────────────────────

			/// <summary>
			/// Keyhole passages: along a straight run of a side passage, a vadose trench cut down through the floor of the tube
			/// (1–2.6 m deep, 1.8–2.8 m wide), the way running in it, ramped in and out at its ends; only where no other way
			/// meets the run.
			/// </summary>
			private void Keyholes(DeterministicRNG r)
			{
				int passages = system.Passages.Count, made = 0, most = 2 + 2 * size;
				for (int pi = 0; pi < passages && made < most; pi++)
				{
					CavePassage p = system.Passages[pi];
					if (p.Kind == CavePassageKind.Trunk || p.Nodes.Count < 9 || r.NextFloat() > 0.6f)
					{
						continue;
					}
					Lanes();
					for (int attempt = 0; attempt < 3; attempt++)
					{
						if (Keyhole(p, r))
						{
							made++;
							break;
						}
					}
				}
			}

			private bool Keyhole(CavePassage p, DeterministicRNG r)
			{
				int count = p.Nodes.Count, len = r.Range(4, 9);
				if (count < len + 5)
				{
					return false;
				}
				int i0 = r.Range(2, count - len - 2), i1 = i0 + len;
				float rad = float.MaxValue;
				for (int i = i0; i <= i1; i++)
				{
					rad = Mathf.Min(rad, p.Nodes[i].Radius);
				}
				if (rad < 2.2f)
				{
					return false;
				}
				float half = r.Range(0.9f, Mathf.Min(1.4f, 0.45f * rad)), depth = r.Range(1f, Mathf.Min(2.6f, 1.1f * rad));
				CaveNode n0 = p.Nodes[i0], n1 = p.Nodes[i1];
				var a = new Vector3(n0.Centre.x, n0.Floor, n0.Centre.z);
				var b = new Vector3(n1.Centre.x, n1.Floor, n1.Centre.z);
				Vector3 run = Flat3(b - a);
				float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
				if (length < 2f * depth / TrenchGrade + 4f)
				{
					return false;
				}
				// Straight enough that its nodes all lie in its bottom, level enough that its floor is a line.
				for (int i = i0; i <= i1; i++)
				{
					CaveNode n = p.Nodes[i];
					Vector3 o = n.Centre - a;
					float t = Mathf.Clamp01(Vector3.Dot(o, run) / length);
					if (Mathf.Abs(o.x * run.z - o.z * run.x) > half - 0.35f || Mathf.Abs(n.Floor - Mathf.Lerp(a.y, b.y, t)) > 0.3f)
					{
						return false;
					}
					if (KeptAt(new Vector3(n.Centre.x, n.Floor, n.Centre.z)) || InAnyChamber(n.Centre, 3f) || system.NearChasmOrClimb(n.Centre, rad + 1f) || system.NearHollow(n.Centre, rad))
					{
						return false;
					}
				}
				if (Mathf.Min(a.y, b.y) - depth < MinFloor + 0.5f || !Dry(a - Vector3.up * depth) || !Dry(b - Vector3.up * depth))
				{
					return false;
				}
				// No other way meets the run.
				foreach (CavePassage other in system.Passages)
				{
					if (other == p)
					{
						continue;
					}
					foreach (CaveNode n in other.Nodes)
					{
						Vector3 o = n.Centre - a;
						float t = Mathf.Clamp01(Vector3.Dot(o, run) / length);
						Vector3 on = Vector3.Lerp(a, b, t);
						if (new Vector2(n.Centre.x - on.x, n.Centre.z - on.z).magnitude < rad + n.Radius + 3f && Mathf.Abs(n.Floor - on.y) < 8f)
						{
							return false;
						}
					}
				}
				// All rock under it.
				for (float s = 0f; s <= length; s += 1.5f)
				{
					Vector3 on = Vector3.Lerp(a, b, s / length);
					foreach (float v in new[] { -half - 0.5f, 0f, half + 0.5f })
					{
						Vector3 q = on + new Vector3(run.z, 0f, -run.x) * v;
						for (float y = on.y - depth - 1f; y < on.y - 0.6f; y += 1f)
						{
							if (system.Smooth(new Vector3(q.x, y, q.z)) < 0.4f)
							{
								return false;
							}
						}
					}
				}
				Mark mark = Save();
				system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Trench, A = a, B = b, Radius = half, Depth = depth });
				system.PrepareField();
				var path = new List<Vector3>();
				for (int i = Mathf.Max(0, i0 - 2); i <= Mathf.Min(count - 1, i1 + 2); i++)
				{
					path.Add(new Vector3(p.Nodes[i].Centre.x, p.Nodes[i].Floor + 0.3f, p.Nodes[i].Centre.z));
				}
				if (!system.WalkFits(path, out _) || !ClimbsHold(0.5f * (a + b), 0.5f * length + 8f))
				{
					Restore(mark);
					return false;
				}
				return true;
			}
		}
	}
}
#endif
