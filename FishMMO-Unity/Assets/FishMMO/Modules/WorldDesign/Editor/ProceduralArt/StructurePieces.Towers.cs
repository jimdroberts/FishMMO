#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The fortifications walked floor over floor (2026-10-19, round 3: "Guard tower interiors are not always enterable from the
	/// top or bottom (inner facing?), nor are they walkable."): the wall tower, the timber watchtower and the gatehouse. Each
	/// had floors with no way between them (a walk-level floor and a roof deck over a guard room with no stair, a platform
	/// reached by a ladder of beams), and the wall pieces' doors faced the field.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The fortification contract.</b> A wall tower and a gatehouse stand on the curtain's centre line with their field side
	/// down −z: the ground door is on +z (the bailey), the curtain's walk comes in through a doorway on each of ±x at
	/// <see cref="WallWalkHeight"/>, centred on the walk's centre line (z = +0.25 in the curtain's frame: the walk runs from
	/// behind the parapet at z −0.55 to the back face at +1.05), across a floor level with the walk. Each marks where the
	/// curtain meets it (<c>curtain</c>, at the outer threshold of each walk door) and its top (<c>lookout</c>, a standing point).
	/// Nothing stands proud of a tower's ±x face beside the curtain, so the module meets it flush.
	/// </para>
	/// <para>
	/// <b>Stairs.</b> Square plans climb by a stacked scale-and-platt stair (<see cref="TowerFlights"/>): a flight up the
	/// outer lane against a wall, a landing across both lanes at the far end, a flight back up the inner lane to the next floor at
	/// the near end, a spine wall between the lanes the whole height; each storey's pair stands over the one below. A round
	/// tower climbs its wall in two quarter-turn ramps of winders (<see cref="TowerHelix"/>). Risers are at most
	/// <see cref="StepRise"/> (the controller steps 0.25 m), <see cref="StairHeadroom"/> is kept over every tread (the floor
	/// over a flight is cut away where a climber's head would strike it), and every well, open flight side, landing edge and
	/// top has a rail or a parapet: the last level is the collider, so a rail there is what stops a fall.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>The curtain's walk centre line, z in a curtain module's frame and so in a tower's (WallSegment v0: walk z −0.55..+1.05).</summary>
		private const float TowerWalkLine = CurtainWalkCentre;
		/// <summary>A walk doorway through a tower's flank: wide enough for the 1.6 m walk's clear width, a door's height.</summary>
		private const float TowerWalkDoorWidth = 1.5f, TowerWalkDoorHeight = 2.3f;
		/// <summary>A tower's timber floor, metres thick.</summary>
		private const float TowerSlab = 0.24f;
		/// <summary>A rail's height over the floor or tread it guards.</summary>
		private const float TowerRail = 0.95f;

		// ── Stairs ────────────────────────────────────────────────

		/// <summary>
		/// A stair band in a tower's frame: flights climb along <see cref="Run"/> (the outer lane, A) and back against it (the
		/// inner lane, B), laid out across <see cref="Across"/> from the wall the outer lane stands against. s = 0 is the near
		/// end (where lane A leaves a floor and lane B arrives at the next), t = 0 the wall.
		/// </summary>
		private struct TowerStair
		{
			public Vector3 Origin, Run, Across;
			public float Lane, Spine, NearLanding, FarLanding, Waist;
			public StructureMaterial Material;

			public float LaneB => Lane + Spine;
			public float Width => 2f * Lane + Spine;

			public Vector3 At(float s, float t, float y) => new Vector3(Origin.x, y, Origin.z) + Run * s + Across * t;

			/// <summary>A box spanning s0..s1 along the run, t0..t1 across, y0..y1.</summary>
			public StructureSolid Box(float s0, float s1, float t0, float t1, float y0, float y1, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
			{
				Vector3 a = At(s0, t0, y0), b = At(s1, t1, y1);
				return StructureShapes.BoxBetween(Vector3.Min(a, b), Vector3.Max(a, b), material, key, role);
			}

			/// <summary>The plan (x, z) rectangle of s0..s1 by t0..t1: min in xy, max in zw.</summary>
			public Vector4 Plan(float s0, float s1, float t0, float t1)
			{
				Vector3 a = At(s0, t0, 0f), b = At(s1, t1, 0f);
				return new Vector4(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
			}
		}

		/// <summary>The risers a flight needs to climb <paramref name="rise"/> at no more than <see cref="StepRise"/> each.</summary>
		private static int TowerRisers(float rise) => Mathf.CeilToInt(rise / StepRise - 1e-4f);

		/// <summary>
		/// One flight of <paramref name="risers"/> risers from <paramref name="from"/> to <paramref name="to"/>: its foot at
		/// s = <paramref name="foot"/>, climbing toward s's sign <paramref name="dir"/>, across lane t0..t1. Each tread is a block
		/// down to <paramref name="waist"/> under it (a sawtooth soffit the next tread's riser closes), or down to
		/// <paramref name="floor"/>, whichever is higher; the last riser steps onto the landing at the head.
		/// </summary>
		private static void TowerFlight(StructureDraft d, in TowerStair b, float foot, float dir, float t0, float t1, float from, float to, float floor, float waist,
			int risers, int key)
		{
			float rise = (to - from) / risers;
			for (int i = 1; i < risers; i++)
			{
				float top = from + rise * i;
				float sa = foot + dir * (i - 1) * StepTread, sb = foot + dir * i * StepTread;
				d.Add(b.Box(Mathf.Min(sa, sb), Mathf.Max(sa, sb), t0, t1, Mathf.Max(floor, top - waist), top, b.Material, key + i));
			}
		}

		/// <summary>
		/// A stacked scale-and-platt stair from <paramref name="levels"/>[0] to the last level: per storey a flight up lane A
		/// to a landing across both lanes at the far end (half the storey up), a flight back up lane B to the next floor at the
		/// near end; a spine wall between the lanes. The floors themselves are the piece's (cut round <see cref="TowerWell"/>).
		/// </summary>
		/// <param name="solidUnder">Lane A's flight built solid down to the floor (masonry against the wall), else a sawtooth like lane B's.</param>
		/// <param name="handrail">A rail up lane B's open side and along the far landing's (the room's side).</param>
		/// <param name="wellRails">At each floor above the first, a rail round the well over lane B: along the room's side and across its far end.</param>
		/// <param name="nearLandings">A landing at the near end at the levels between (a stair with no floor there: the watchtower).</param>
		/// <param name="spineRail">The spine stands a rail's height over the top level; else it stops at the top and a rail runs along it.</param>
		private static void TowerFlights(StructureDraft d, in TowerStair b, float[] levels, int risers, int key, bool solidUnder, bool handrail, bool wellRails,
			bool nearLandings, bool spineRail)
		{
			float run = (risers - 1) * StepTread;
			int last = levels.Length - 1;
			for (int k = 0; k < last; k++)
			{
				float from = levels[k], to = levels[k + 1], mid = (from + to) * 0.5f;
				int sk = key + k * 200;
				// Up the outer lane, the wall on one side and the spine on the other.
				TowerFlight(d, b, 0f, 1f, 0f, b.Lane, from, mid, from, solidUnder ? 99f : b.Waist, risers, sk);
				// The far landing across both lanes, its back to the far wall: a slab, the floor under it free (a corner to stand in).
				d.Add(b.Box(run, run + b.FarLanding, 0f, b.Width, Mathf.Max(from, mid - b.Waist), mid, b.Material, sk + 60));
				// Back up the inner lane to the next floor.
				TowerFlight(d, b, run, -1f, b.LaneB, b.Width, mid, to, from, b.Waist, risers, sk + 100);
				if (nearLandings && k + 1 < last)
				{
					d.Add(b.Box(-b.NearLanding, 0f, 0f, b.Width, Mathf.Max(from, to - b.Waist), to, b.Material, sk + 61));
				}
				float tr = b.Width - 0.04f;
				if (handrail)
				{
					// Along the far landing's open side and up the inner flight's: a rail at a hand's height over the treads.
					Vector3 farEnd = b.At(run + b.FarLanding - 0.05f, tr, mid + TowerRail), farEdge = b.At(run, tr, mid + TowerRail), head = b.At(0.04f, tr, to + TowerRail);
					d.Add(StructureShapes.Beam(farEnd, farEdge, 0.07f, 0.07f, StructureMaterial.Timber, sk + 70));
					d.Add(StructureShapes.Beam(farEdge, head, 0.07f, 0.07f, StructureMaterial.Timber, sk + 71));
					TowerPost(d, b.At(run, tr, mid), TowerRail, sk + 72, StructureRole.Structure);
					TowerPost(d, b.At(0.04f, tr, to - (to - mid) / risers), TowerRail + (to - mid) / risers, sk + 73, StructureRole.Structure);
					if (d.Mid)
					{
						TowerPost(d, b.At(run * 0.5f, tr, (mid + to) * 0.5f - 0.1f), TowerRail + 0.1f, sk + 74, StructureRole.Detail);
					}
				}
				if (wellRails)
				{
					// Round the well at the floor it reaches: the room's side and the far end; the near end is the way down.
					float tw = b.Width + 0.04f, y = to + TowerRail;
					d.Add(StructureShapes.Beam(b.At(0.04f, tw, y), b.At(run + 0.04f, tw, y), 0.07f, 0.07f, StructureMaterial.Timber, sk + 80));
					// Short of the corners by a hand: rails meeting at a corner make a ledge-top the size of a floor.
					d.Add(StructureShapes.Beam(b.At(run + 0.04f, b.LaneB + 0.12f, y), b.At(run + 0.04f, tw - 0.12f, y), 0.07f, 0.07f, StructureMaterial.Timber, sk + 81));
					TowerPost(d, b.At(0.04f, tw, to), TowerRail, sk + 82, StructureRole.Structure);
					TowerPost(d, b.At(run + 0.04f, tw, to), TowerRail, sk + 83, StructureRole.Structure);
					if (d.Mid)
					{
						TowerPost(d, b.At(run * 0.5f, tw, to), TowerRail, sk + 84, StructureRole.Detail);
					}
					if (k + 1 == last && !spineRail)
					{
						d.Add(StructureShapes.Beam(b.At(0.04f, b.Lane + b.Spine * 0.5f, y), b.At(run - 0.12f, b.Lane + b.Spine * 0.5f, y), 0.07f, 0.07f, StructureMaterial.Timber, sk + 85));
						TowerPost(d, b.At(0.04f, b.Lane + b.Spine * 0.5f, to), TowerRail, sk + 86, StructureRole.Structure);
					}
				}
			}
			// The spine between the lanes, floor to top: no flight's open side faces the other's drop.
			d.Add(b.Box(0f, run, b.Lane, b.LaneB, levels[0], levels[last] + (spineRail ? TowerRail + 0.05f : 0f), b.Material, key + 190));
		}

		/// <summary>The well a stair band leaves in the floor its inner flight reaches: lane B over the flight's length (x, z: min in xy, max in zw).</summary>
		private static Vector4 TowerWell(in TowerStair b, int risers) => b.Plan(0f, (risers - 1) * StepTread, b.LaneB, b.Width);

		/// <summary>A floor slab over x0..x1 by z0..z1, its top at <paramref name="top"/>, less a rectangular well (x, z: min in xy, max in zw).</summary>
		private static void TowerSlabAround(StructureDraft d, float x0, float x1, float z0, float z1, float top, Vector4 well, int key)
		{
			void Slab(float ax, float bx, float az, float bz, int n)
			{
				if (bx - ax > 1e-3f && bz - az > 1e-3f)
				{
					d.Add(StructureShapes.BoxBetween(new Vector3(ax, top - TowerSlab, az), new Vector3(bx, top, bz), StructureMaterial.Timber, key + n)).Grain = Vector3.forward;
				}
			}
			float wx0 = Mathf.Max(x0, well.x), wz0 = Mathf.Max(z0, well.y), wx1 = Mathf.Min(x1, well.z), wz1 = Mathf.Min(z1, well.w);
			Slab(x0, x1, z0, wz0, 0);
			Slab(x0, x1, wz1, z1, 1);
			Slab(x0, wx0, wz0, wz1, 2);
			Slab(wx1, x1, wz0, wz1, 3);
		}

		/// <summary>A rail post from <paramref name="foot"/> up <paramref name="height"/>.</summary>
		private static void TowerPost(StructureDraft d, Vector3 foot, float height, int key, StructureRole role)
		{
			d.Add(StructureShapes.Box(foot + Vector3.up * (height * 0.5f), new Vector3(0.09f, height, 0.09f), StructureMaterial.Timber, key, role));
		}

		/// <summary>
		/// A ramp of winders round the inside of a round wall: <paramref name="risers"/> risers from <paramref name="from"/>
		/// to <paramref name="to"/>, the first tread at angle <paramref name="a0"/> (radians, x = cos, z = sin), each
		/// <paramref name="step"/> on, between radii <paramref name="inner"/> and <paramref name="outer"/>; a sawtooth soffit
		/// <paramref name="waist"/> under each tread. A rail up its open inner edge from where a fall would hurt. Returns the
		/// angle the well over it must open from (the first tread a climber's head would strike the floor at
		/// <paramref name="to"/> over) and the angle of its head, where the floor begins.
		/// </summary>
		private static (float wellFrom, float head) TowerHelix(StructureDraft d, float inner, float outer, float a0, float step, float from, float to, int risers,
			float waist, StructureMaterial material, int key)
		{
			float rise = (to - from) / risers;
			for (int i = 1; i < risers; i++)
			{
				float top = from + rise * i;
				d.Add(RingBlock(Vector3.zero, inner, outer, Mathf.Max(from, top - waist), top, a0 + (i - 1) * step, a0 + i * step, material, key + i, StructureRole.Structure));
			}
			float head = a0 + (risers - 1) * step;
			// The rail along the open edge, a hand over the treads, from the third tread to the floor at the head.
			float r = inner + 0.04f;
			Vector3 Point(float a, float y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
			Vector3 prev = Point(a0 + 2.5f * step, from + rise * 3f + TowerRail);
			TowerPost(d, Point(a0 + 2.5f * step, from + rise * 3f), TowerRail, key + 300, StructureRole.Structure);
			int n = 0;
			for (int j = 5; j <= risers; j += 2)
			{
				Vector3 next = j >= risers - 1 ? Point(head, to + TowerRail) : Point(a0 + (j - 0.5f) * step, from + rise * j + TowerRail);
				d.Add(StructureShapes.Beam(prev, next, 0.07f, 0.07f, StructureMaterial.Timber, key + 310 + n++));
				prev = next;
				if (j >= risers - 1)
				{
					break;
				}
			}
			TowerPost(d, Point(head, to - rise), TowerRail + rise, key + 301, StructureRole.Structure);
			int open = Mathf.FloorToInt((to - TowerSlab - StairHeadroom - from) / rise) + 1;
			return (a0 + (open - 1) * step, head);
		}

		/// <summary>
		/// A round floor at <paramref name="top"/>: a disc inside <paramref name="inner"/> and a ring out to
		/// <paramref name="outer"/> (into the wall) everywhere but the well from <paramref name="wellFrom"/> to
		/// <paramref name="wellTo"/>, which gets a rail along its inner edge and across its low end.
		/// </summary>
		private static void TowerRoundFloor(StructureDraft d, float inner, float outer, float top, float wellFrom, float wellTo, int key)
		{
			int seg = d.Seg(20, 12);
			d.Add(StructureShapes.Cylinder(inner + 0.02f, top - TowerSlab, top, seg, StructureMaterial.Timber, key));
			float span = 2f * Mathf.PI - (wellTo - wellFrom);
			int n = Mathf.Max(3, Mathf.CeilToInt(span / (2f * Mathf.PI) * seg));
			for (int i = 0; i < n; i++)
			{
				float a0 = wellTo + span * i / n, a1 = wellTo + span * (i + 1) / n;
				d.Add(RingBlock(Vector3.zero, inner, outer, top - TowerSlab, top, a0, a1, StructureMaterial.Timber, key + 1 + i, StructureRole.Structure));
			}
			// The well's rail: an arc along its inner edge (the head end open: the way down) and a bar across its low end.
			float r = inner - 0.04f, y = top + TowerRail;
			int arcs = Mathf.Max(2, Mathf.CeilToInt((wellTo - wellFrom) / 0.35f));
			for (int i = 0; i < arcs; i++)
			{
				float a = Mathf.Lerp(wellFrom, wellTo, i / (float)arcs), b = Mathf.Lerp(wellFrom, wellTo, (i + 1) / (float)arcs);
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), new Vector3(Mathf.Cos(b) * r, y, Mathf.Sin(b) * r), 0.07f, 0.07f,
					StructureMaterial.Timber, key + 40 + i));
			}
			float low = wellFrom - 0.03f;
			var lowDir = new Vector3(Mathf.Cos(low), 0f, Mathf.Sin(low));
			d.Add(StructureShapes.Beam(lowDir * r + Vector3.up * y, lowDir * (outer - 0.1f) + Vector3.up * y, 0.07f, 0.07f, StructureMaterial.Timber, key + 60));
			TowerPost(d, lowDir * r + Vector3.up * top, TowerRail, key + 61, StructureRole.Structure);
			TowerPost(d, new Vector3(Mathf.Cos(wellTo) * r, top, Mathf.Sin(wellTo) * r), TowerRail, key + 62, StructureRole.Structure);
		}

		// ── Wall tower ────────────────────────────────────────────

		private static void CurtainTower(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				RoundCurtainTower(d);
				return;
			}
			SquareCurtainTower(d);
		}

		/// <summary>
		/// The round tower: 8 m across, walls 1 m. A guard room from the bailey door; a ramp of winders up the wall on the west and
		/// field side to the walk-level chamber the walk passes through; a second up the bailey side to a crenellated fighting top.
		/// </summary>
		private static void RoundCurtainTower(StructureDraft d)
		{
			const float outer = 4f, inner = 3f, lane = 1.2f, floor = 0.4f, walk = WallWalkHeight, top = 9.4f;
			const float step = StepTread / (inner - lane * 0.5f);
			float stairInner = inner - lane;
			int seg = d.Seg(20, 12);
			// A plinth flush with the wall (the curtain meets the flank flush), its top the ground floor.
			d.Add(StructureShapes.Cylinder(outer, -FoundationDepth, floor, seg, StructureMaterial.Stone, 1));
			// The wall: the bailey door, the walk's doors either flank centred on its line, and windows clear of the stairs.
			float east = Mathf.Asin(TowerWalkLine / inner), west = Mathf.PI - east;
			var windows = new List<(float angle, float y)> { (Mathf.PI * 0.25f, 2.2f), (Mathf.PI * 1.5f, 6.6f), (Mathf.PI * 1.75f, 6.4f) };
			var holes = new List<(float, float, float, float)>
			{
				(Mathf.PI * 0.5f, 1.2f, floor, floor + 2.3f),
				(east, TowerWalkDoorWidth, walk, walk + TowerWalkDoorHeight),
				(west, TowerWalkDoorWidth, walk, walk + TowerWalkDoorHeight),
			};
			foreach ((float a, float y) in windows)
			{
				holes.Add((a, WindowWidth, y - WindowHeight * 0.5f, y + WindowHeight * 0.5f));
			}
			RingWallWithOpenings(d, Vector3.zero, inner, outer, floor, top, seg, Mathf.PI * 0.5f, holes, StructureMaterial.Stone, 10);
			for (int i = 0; i < windows.Count; i++)
			{
				var outward = new Vector3(Mathf.Cos(windows[i].angle), 0f, Mathf.Sin(windows[i].angle));
				Window(d, outward * outer + Vector3.up * windows[i].y, outward, outer - inner, 900 + i * 8);
			}
			// The bailey door: a step up to the sill, the door hung inside it.
			Steps(d, new Vector3(0f, 0f, outer - 0.05f), Vector3.forward, 1.4f, floor, StructureMaterial.Stone, 30);
			HangDoor(d, new Vector3(0f, floor, Mathf.Sqrt(inner * inner - 0.36f) - 0.05f), Vector3.right, Vector3.back, 1.2f, 2.3f, StructureDoorKind.Iron);

			// Up from the guard room round the west and field side (its foot clear of the door, low where it passes under the
			// west walk door), to the walk level; then round the bailey side, from beside the east walk door, to the top.
			(float lowWell, float lowHead) = TowerHelix(d, stairInner, inner + 0.05f, 2.1f, step, floor, walk, TowerRisers(walk - floor), 0.5f, StructureMaterial.Stone, 2000);
			(float topWell, float topHead) = TowerHelix(d, stairInner, inner + 0.05f, 0.62f, step, walk, top, TowerRisers(top - walk), 0.5f, StructureMaterial.Stone, 2400);
			TowerRoundFloor(d, stairInner, inner + 0.05f, walk, lowWell, lowHead, 3000);
			TowerRoundFloor(d, stairInner, inner + 0.05f, top, topWell, topHead, 3100);

			RoundFightingTop(d, outer, top, seg);

			// The rooms: the guard room (the ramp and its foot kept clear) and the walk-level chamber (the walk's way through,
			// the ramp up and the well kept clear).
			d.RoundRoom(new Vector3(0f, floor, 0f), inner, walk - TowerSlab - floor, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(-1.3f, 2.7f));
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(0.7f, -1.3f));
			d.RoundRoom(new Vector3(0f, walk, 0f), inner, top - TowerSlab - walk, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, TowerWalkLine - 0.95f), new Vector2(inner, TowerWalkLine + 0.95f));
			d.RoomClear(new Vector2(-inner, 0.8f), new Vector2(inner, inner));
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(0.7f, -0.5f));

			float join = Mathf.Sqrt(outer * outer - TowerWalkLine * TowerWalkLine);
			d.Mark("curtain", new Vector3(join, walk, TowerWalkLine), 90f);
			d.Mark("curtain", new Vector3(-join, walk, TowerWalkLine), -90f);
			d.Mark("lookout", new Vector3(0f, top, -2.2f), 0f);
		}

		/// <summary>
		/// The square tower: 8.2 m, walls 1 m. A stacked scale-and-platt stair up the field wall from the guard room to the
		/// walk-level chamber and on to a lookout under a pyramid roof on corner piers, behind a breast-high parapet.
		/// </summary>
		private static void SquareCurtainTower(StructureDraft d)
		{
			const float half = 4.1f, wall = 1f, inner = half - wall, floor = 0.3f, walk = WallWalkHeight, top = 9.6f, eave = 12f;
			// The plinth: flush with the flanks the curtain meets, a step proud front and back.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(half * 2f, floor + FoundationDepth, half * 2f + 0.6f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
			var back = new List<WallOpening>
			{
				new WallOpening { Along = half, Width = 1.2f, Bottom = floor, Top = floor + 2.3f },
				new WallOpening { Along = half - 2.2f, Width = WindowWidth, Bottom = 2.2f - WindowHeight * 0.5f, Top = 2.2f + WindowHeight * 0.5f },
				new WallOpening { Along = half + 2.2f, Width = WindowWidth, Bottom = 2.2f - WindowHeight * 0.5f, Top = 2.2f + WindowHeight * 0.5f },
				new WallOpening { Along = half - 1f, Width = WindowWidth, Bottom = 6.6f - WindowHeight * 0.5f, Top = 6.6f + WindowHeight * 0.5f },
				new WallOpening { Along = half + 1f, Width = WindowWidth, Bottom = 6.6f - WindowHeight * 0.5f, Top = 6.6f + WindowHeight * 0.5f },
			};
			var flank = new WallOpening { Along = inner + TowerWalkLine, Width = TowerWalkDoorWidth, Bottom = walk, Top = walk + TowerWalkDoorHeight };
			WallRun(d, new Vector3(-half, 0f, -half + wall * 0.5f), new Vector3(half, 0f, -half + wall * 0.5f), wall, floor, top, null, StructureMaterial.Stone, 2);
			WallRun(d, new Vector3(-half, 0f, half - wall * 0.5f), new Vector3(half, 0f, half - wall * 0.5f), wall, floor, top, back, StructureMaterial.Stone, 8);
			WallRun(d, new Vector3(-half + wall * 0.5f, 0f, -inner), new Vector3(-half + wall * 0.5f, 0f, inner), wall, floor, top, new List<WallOpening> { flank }, StructureMaterial.Stone, 14);
			WallRun(d, new Vector3(half - wall * 0.5f, 0f, -inner), new Vector3(half - wall * 0.5f, 0f, inner), wall, floor, top, new List<WallOpening> { flank }, StructureMaterial.Stone, 20);
			for (int i = 0; i < 4; i++)
			{
				// Not in a row: the guard room's either side of the door, the chamber's over it (WallRun cuts holes that do not overlap along the wall).
				float x = i < 2 ? (i == 0 ? -2.2f : 2.2f) : (i == 2 ? -1f : 1f);
				Window(d, new Vector3(x, i < 2 ? 2.2f : 6.6f, half), Vector3.forward, wall, 900 + i * 8);
			}
			Steps(d, new Vector3(0f, 0f, half + 0.3f), Vector3.forward, 1.6f, floor, StructureMaterial.Stone, 26);
			HangDoor(d, new Vector3(0f, floor, inner + 0.05f), Vector3.right, Vector3.back, 1.2f, 2.3f, StructureDoorKind.Iron);

			// The stair against the field wall, its near end at the east (beside the east walk door), climbing west.
			int risers = TowerRisers((walk - floor) * 0.5f);
			var stair = new TowerStair
			{
				Origin = new Vector3((risers - 1) * StepTread * 0.5f, 0f, -inner), Run = Vector3.left, Across = Vector3.forward,
				Lane = 1.2f, Spine = 0.2f, NearLanding = 1.45f, FarLanding = 1.45f, Waist = 0.4f, Material = StructureMaterial.Stone,
			};
			TowerFlights(d, stair, new[] { floor, walk, top }, risers, 2000, true, true, true, false, true);
			Vector4 well = TowerWell(stair, risers);
			TowerSlabAround(d, -inner, inner, -inner, inner, walk, well, 3000);
			TowerSlabAround(d, -inner, inner, -inner, inner, top, well, 3010);

			SquareLookoutTop(d, half, top, eave);
			// The rooms: the guard room and the walk-level chamber, the stair band (and the walk's way through) kept clear.
			d.Room(new Vector3(0f, floor, 0f), new Vector2(inner * 2f, inner * 2f), walk - TowerSlab - floor, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(inner, -0.25f));
			d.Room(new Vector3(0f, walk, 0f), new Vector2(inner * 2f, inner * 2f), top - TowerSlab - walk, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(inner, -0.25f));
			d.RoomClear(new Vector2(-inner, TowerWalkLine - 0.95f), new Vector2(inner, TowerWalkLine + 0.95f));

			d.Mark("curtain", new Vector3(half, walk, TowerWalkLine), 90f);
			d.Mark("curtain", new Vector3(-half, walk, TowerWalkLine), -90f);
			d.Mark("lookout", new Vector3(0f, top, 1.6f), 0f);
		}

		/// <summary>A round tower's fighting top over its deck at <paramref name="top"/>: a parapet on corbels, merlons round it.</summary>
		private static void RoundFightingTop(StructureDraft d, float outer, float top, int seg)
		{
			d.Add(StructureShapes.Lathe(new[] { new Vector2(outer - 0.6f, top), new Vector2(outer + 0.2f, top), new Vector2(outer + 0.2f, top + 1f), new Vector2(outer - 0.6f, top + 1f) },
				seg, StructureMaterial.Stone, 40, true, 0f, true));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(outer - 0.1f, top - 0.6f), new Vector2(outer + 0.25f, top - 0.6f), new Vector2(outer + 0.25f, top), new Vector2(outer - 0.1f, top) },
					seg, StructureMaterial.Stone, 42, true, 0f, true, false, StructureRole.Detail));
		}
		int merlons = d.Seg(14, 8);
		for (int i = 0; i < merlons; i++)
		{
			float a0 = (i + 0.2f) * 2f * Mathf.PI / merlons, a1 = (i + 0.75f) * 2f * Mathf.PI / merlons;
			d.Add(RingBlock(Vector3.zero, outer - 0.6f, outer + 0.2f, top + 1f, top + 1.85f, a0, a1, StructureMaterial.Stone, 60 + i, StructureRole.Detail));
		}
		}

		/// <summary>
		/// A square tower's lookout over its top floor at <paramref name="top"/>: a parapet breast-high round it, a pier at each corner
		/// carrying a pyramid roof (its palette's pitch) from <paramref name="eave"/>.
		/// </summary>
		private static void SquareLookoutTop(StructureDraft d, float half, float top, float eave)
		{
			float parapet = top + 1.1f;
			WallRun(d, new Vector3(-half, 0f, -half + 0.25f), new Vector3(half, 0f, -half + 0.25f), 0.5f, top, parapet, null, StructureMaterial.Stone, 30);
			WallRun(d, new Vector3(-half, 0f, half - 0.25f), new Vector3(half, 0f, half - 0.25f), 0.5f, top, parapet, null, StructureMaterial.Stone, 31);
			WallRun(d, new Vector3(-half + 0.25f, 0f, -half + 0.5f), new Vector3(-half + 0.25f, 0f, half - 0.5f), 0.5f, top, parapet, null, StructureMaterial.Stone, 32);
			WallRun(d, new Vector3(half - 0.25f, 0f, -half + 0.5f), new Vector3(half - 0.25f, 0f, half - 0.5f), 0.5f, top, parapet, null, StructureMaterial.Stone, 33);
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(sx * (half - 0.45f), (parapet + eave) * 0.5f, sz * (half - 0.45f)), new Vector3(0.9f, eave - parapet, 0.9f),
						StructureMaterial.Stone, 40 + (sx + 1) + (sz + 1) / 2));
				}
		}
		d.Add(StructureShapes.Box(new Vector3(0f, eave + 0.15f, 0f), new Vector3(half * 2f + 0.5f, 0.3f, half * 2f + 0.5f), StructureMaterial.Stone, 7, StructureRole.Roof));
		// The pyramid roof at its palette's pitch (StructureRoofs, I6h); flat, a timber deck.
		const float roofRise = 4.85f;
		OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Timber, roofRise, half + 0.5f);
		using (d.Builds(RoofRoles))
		{
			if (roof.Flat)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, eave + 0.36f, 0f), new Vector3(half * 2f + 0.3f, 0.12f, half * 2f + 0.3f), StructureMaterial.Timber, 8, StructureRole.Roof)).Grain = Vector3.forward;
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2((half + 0.5f) * 1.414f, eave + 0.3f), new Vector2(0f, eave + 0.3f + roof.Rise) }, 4, StructureMaterial.Timber, 8, false,
					Mathf.PI * 0.25f, false, false, StructureRole.Roof));
			}
		}

		}

		// ── Corner tower ──────────────────────────────────────────

		/// <summary>
		/// The corner tower (2026-10-19, for a square curtain such as a palace compound's): where two curtains meet at a right angle.
		/// Its frame: the origin on the joint of the two curtains' centre lines, the field the outer quadrant (−x and −z), the
		/// bailey the inner (+x, +z). One curtain leaves its +x face as a module at yaw 0 (field −z), the other its +z face as a
		/// module at yaw 90 (field −x); each walk door is centred on that curtain's walk line (z = +<see cref="CurtainWalkCentre"/>
		/// on +x, x = +<see cref="CurtainWalkCentre"/> on +z), and marked "curtain" at its outer threshold with the yaw of the
		/// module leaving it. The ground door opens on the bailey. A WallTower's walk doors face each other across it, so at a
		/// right angle one opened onto a 5 m drop.
		/// </summary>
		private static void AddTowers(List<StructurePiece> list)
		{
			Piece(list, "CornerTower", StructureStyle.Stone, StructureSize.Building,
				"A tower at a right-angled corner of the curtain: the field to −x/−z, the walk in at 5 m through its +x and +z faces (centred 0.25 m into the bailey), the ground door on the bailey, stairs to every floor: v0 round, 8 m, crenellated top; v1 square, 8.2 m, a lookout under a pyramid roof.",
				// Not tagged "wall" (nor "tower"): the templates' curtain query (wall!gate!tower!fence) and their towers' (tower+wall)
				// would draw it; it is laid by its id where a curtain turns a square corner.
				CornerTower, "military", "building", "cornertower");
		}

		private static void CornerTower(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				RoundCornerTower(d);
				return;
			}
			SquareCornerTower(d);
		}

		/// <summary>
		/// The round corner tower: the round wall tower's plan turned for the corner. The bailey door on the inner diagonal; a ramp of
		/// winders round the field side up to the walk-level chamber both walks enter, a second on round to the fighting top.
		/// </summary>
		private static void RoundCornerTower(StructureDraft d)
		{
			const float outer = 4f, inner = 3f, lane = 1.2f, floor = 0.4f, walk = WallWalkHeight, top = 9.4f;
			const float step = StepTread / (inner - lane * 0.5f);
			float stairInner = inner - lane;
			int seg = d.Seg(20, 12);
			d.Add(StructureShapes.Cylinder(outer, -FoundationDepth, floor, seg, StructureMaterial.Stone, 1));
			// The walk doors: on +x centred on the walk line z = +0.25, on +z on x = +0.25; the bailey door between them.
			float east = Mathf.Asin(TowerWalkLine / inner), north = Mathf.PI * 0.5f - east, bailey = Mathf.PI * 0.25f;
			// Windows clear of the doors and the ramps (no two holes share an angle: a block's cuts may not overlap).
			var windows = new List<(float angle, float y)> { (5.9f, 2.2f), (3.6f, 6.6f), (5.4f, 6.6f) };
			var holes = new List<(float, float, float, float)>
			{
				(bailey, 1.2f, floor, floor + 2.3f),
				(east, TowerWalkDoorWidth, walk, walk + TowerWalkDoorHeight),
				(north, TowerWalkDoorWidth, walk, walk + TowerWalkDoorHeight),
			};
			foreach ((float a, float y) in windows)
			{
				holes.Add((a, WindowWidth, y - WindowHeight * 0.5f, y + WindowHeight * 0.5f));
			}
			RingWallWithOpenings(d, Vector3.zero, inner, outer, floor, top, seg, bailey, holes, StructureMaterial.Stone, 10);
			for (int i = 0; i < windows.Count; i++)
			{
				var outward = new Vector3(Mathf.Cos(windows[i].angle), 0f, Mathf.Sin(windows[i].angle));
				Window(d, outward * outer + Vector3.up * windows[i].y, outward, outer - inner, 900 + i * 8);
			}
			var way = new Vector3(Mathf.Cos(bailey), 0f, Mathf.Sin(bailey));
			// The flight's top tread from inside the plinth's coarsest facet on the diagonal (a vertex at the finest levels).
			Steps(d, way * (outer * Mathf.Cos(Mathf.PI / 12f) - 0.05f), way, 1.4f, floor, StructureMaterial.Stone, 30);
			HangDoor(d, way * (Mathf.Sqrt(inner * inner - 0.36f) - 0.05f) + Vector3.up * floor, Vector3.Cross(Vector3.up, way), -way, 1.2f, 2.3f, StructureDoorKind.Iron);

			// Up round the field side from beside the door to the walk level; then on round under the east walk door's head
			// (its soffit thin there) to the top, its head short of the north walk door.
			(float lowWell, float lowHead) = TowerHelix(d, stairInner, inner + 0.05f, 1.85f, step, floor, walk, TowerRisers(walk - floor), 0.5f, StructureMaterial.Stone, 2000);
			(float topWell, float topHead) = TowerHelix(d, stairInner, inner + 0.05f, 4.85f, step, walk, top, TowerRisers(top - walk), 0.25f, StructureMaterial.Stone, 2400);
			TowerRoundFloor(d, stairInner, inner + 0.05f, walk, lowWell, lowHead, 3000);
			TowerRoundFloor(d, stairInner, inner + 0.05f, top, topWell, topHead, 3100);
			RoundFightingTop(d, outer, top, seg);

			d.RoundRoom(new Vector3(0f, floor, 0f), inner, walk - TowerSlab - floor, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(-1.3f, inner));
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(0.2f, -1.3f));
			d.RoomClear(new Vector2(-inner, 1.3f), new Vector2(-0.4f, inner));
			d.RoundRoom(new Vector3(0f, walk, 0f), inner, top - TowerSlab - walk, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(1f, TowerWalkLine - 0.95f), new Vector2(inner, TowerWalkLine + 0.95f));
			d.RoomClear(new Vector2(TowerWalkLine - 0.95f, 1f), new Vector2(TowerWalkLine + 0.95f, inner));
			d.RoomClear(new Vector2(1.3f, -inner), new Vector2(inner, inner));
			d.RoomClear(new Vector2(0.2f, -inner), new Vector2(inner, -1.3f));
			d.RoomClear(new Vector2(0.2f, 1.3f), new Vector2(inner, inner));
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(0.2f, -0.2f));

			float join = Mathf.Sqrt(outer * outer - TowerWalkLine * TowerWalkLine);
			d.Mark("curtain", new Vector3(join, walk, TowerWalkLine), 0f);
			d.Mark("curtain", new Vector3(TowerWalkLine, walk, join), 90f);
			d.Mark("lookout", new Vector3(-1.6f, top, -1.6f), 225f);
		}

		/// <summary>
		/// The square corner tower: the square wall tower's stair, floors and lookout, its walk doors on +x and +z, its door on the
		/// +z face east of the north curtain, windows to the field on −x.
		/// </summary>
		private static void SquareCornerTower(StructureDraft d)
		{
			const float half = 4.1f, wall = 1f, inner = half - wall, floor = 0.3f, walk = WallWalkHeight, top = 9.6f, eave = 12f, doorX = 2.2f;
			// The plinth flush with every face: both curtains meet it.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(half * 2f, floor + FoundationDepth, half * 2f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
			var north = new List<WallOpening>
			{
				new WallOpening { Along = half - 2f, Width = WindowWidth, Bottom = 2.2f - WindowHeight * 0.5f, Top = 2.2f + WindowHeight * 0.5f },
				new WallOpening { Along = half + TowerWalkLine, Width = TowerWalkDoorWidth, Bottom = walk, Top = walk + TowerWalkDoorHeight },
				new WallOpening { Along = half + doorX, Width = 1.2f, Bottom = floor, Top = floor + 2.3f },
			};
			var west = new List<WallOpening>
			{
				new WallOpening { Along = inner + 0.5f, Width = WindowWidth, Bottom = 6.6f - WindowHeight * 0.5f, Top = 6.6f + WindowHeight * 0.5f },
				new WallOpening { Along = inner + 1.5f, Width = WindowWidth, Bottom = 2.2f - WindowHeight * 0.5f, Top = 2.2f + WindowHeight * 0.5f },
			};
			var eastDoor = new List<WallOpening> { new WallOpening { Along = inner + TowerWalkLine, Width = TowerWalkDoorWidth, Bottom = walk, Top = walk + TowerWalkDoorHeight } };
			WallRun(d, new Vector3(-half, 0f, -half + wall * 0.5f), new Vector3(half, 0f, -half + wall * 0.5f), wall, floor, top, null, StructureMaterial.Stone, 2);
			WallRun(d, new Vector3(-half, 0f, half - wall * 0.5f), new Vector3(half, 0f, half - wall * 0.5f), wall, floor, top, north, StructureMaterial.Stone, 8);
			WallRun(d, new Vector3(-half + wall * 0.5f, 0f, -inner), new Vector3(-half + wall * 0.5f, 0f, inner), wall, floor, top, west, StructureMaterial.Stone, 14);
			WallRun(d, new Vector3(half - wall * 0.5f, 0f, -inner), new Vector3(half - wall * 0.5f, 0f, inner), wall, floor, top, eastDoor, StructureMaterial.Stone, 20);
			Window(d, new Vector3(-2f, 2.2f, half), Vector3.forward, wall, 900);
			Window(d, new Vector3(-half, 6.6f, 0.5f), Vector3.left, wall, 908);
			Window(d, new Vector3(-half, 2.2f, 1.5f), Vector3.left, wall, 916);
			Steps(d, new Vector3(doorX, 0f, half), Vector3.forward, 1.6f, floor, StructureMaterial.Stone, 26);
			HangDoor(d, new Vector3(doorX, floor, inner + 0.05f), Vector3.right, Vector3.back, 1.2f, 2.3f, StructureDoorKind.Iron);

			// The stair against the field wall (−z), its near end at the east beside the east walk door, climbing west.
			int risers = TowerRisers((walk - floor) * 0.5f);
			var stair = new TowerStair
			{
				Origin = new Vector3((risers - 1) * StepTread * 0.5f, 0f, -inner), Run = Vector3.left, Across = Vector3.forward,
				Lane = 1.2f, Spine = 0.2f, NearLanding = 1.45f, FarLanding = 1.45f, Waist = 0.4f, Material = StructureMaterial.Stone,
			};
			TowerFlights(d, stair, new[] { floor, walk, top }, risers, 2000, true, true, true, false, true);
			Vector4 well = TowerWell(stair, risers);
			TowerSlabAround(d, -inner, inner, -inner, inner, walk, well, 3000);
			TowerSlabAround(d, -inner, inner, -inner, inner, top, well, 3010);
			SquareLookoutTop(d, half, top, eave);

			d.Room(new Vector3(0f, floor, 0f), new Vector2(inner * 2f, inner * 2f), walk - TowerSlab - floor, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(inner, -0.25f));
			d.Room(new Vector3(0f, walk, 0f), new Vector2(inner * 2f, inner * 2f), top - TowerSlab - walk, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(inner, -0.25f));
			d.RoomClear(new Vector2(1f, TowerWalkLine - 0.95f), new Vector2(inner, TowerWalkLine + 0.95f));
			d.RoomClear(new Vector2(TowerWalkLine - 0.95f, 1f), new Vector2(TowerWalkLine + 0.95f, inner));

			d.Mark("curtain", new Vector3(half, walk, TowerWalkLine), 0f);
			d.Mark("curtain", new Vector3(TowerWalkLine, walk, half), 90f);
			d.Mark("lookout", new Vector3(-1.6f, top, 1.6f), 225f);
		}

		// ── Watchtower ────────────────────────────────────────────

		/// <summary>
		/// A timber watchtower, its platform at 6 m: four posts, a boarded stair box in the west half (the way in on +z, the
		/// side a palisade ring's watchtower turns to its bailey), a stair of two storeys up it, through a railed trap onto a
		/// railed platform under a pyramid roof. v1 stands on a stone guard room whose roof is a railed stage.
		/// </summary>
		private static void TimberWatchtower(StructureDraft d)
		{
			bool stoneBase = d.Variant == 1;
			const float inner = 2.1f, post = 2.22f, platform = 6f, deck = 2.8f, plate = 8.4f, stage = 3f;
			float floor = stoneBase ? 0.15f : 0.1f;
			float legFrom = stoneBase ? stage : -0.4f;
			if (stoneBase)
			{
				// The guard room: a floor, walls with the door on +z (onto the stair's foot) and windows, its roof the stage.
				const float outer = 2.6f, wall = 0.5f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(outer * 2f, floor + FoundationDepth, outer * 2f), d.Bevel(0.05f), StructureMaterial.Fieldstone, 1));
				var door = new List<WallOpening>
				{
					new WallOpening { Along = outer - 0.5f, Width = 1f, Bottom = floor, Top = floor + 2.25f },
				};
				var east = new List<WallOpening> { new WallOpening { Along = inner, Width = WindowWidth, Bottom = 1.8f - WindowHeight * 0.5f, Top = 1.8f + WindowHeight * 0.5f } };
				var south = new List<WallOpening> { new WallOpening { Along = outer + 1.1f, Width = WindowWidth, Bottom = 1.8f - WindowHeight * 0.5f, Top = 1.8f + WindowHeight * 0.5f } };
				WallRun(d, new Vector3(-outer, 0f, -outer + wall * 0.5f), new Vector3(outer, 0f, -outer + wall * 0.5f), wall, floor, stage, south, StructureMaterial.Fieldstone, 2);
				WallRun(d, new Vector3(-outer, 0f, outer - wall * 0.5f), new Vector3(outer, 0f, outer - wall * 0.5f), wall, floor, stage, door, StructureMaterial.Fieldstone, 6);
				WallRun(d, new Vector3(-outer + wall * 0.5f, 0f, -inner), new Vector3(-outer + wall * 0.5f, 0f, inner), wall, floor, stage, null, StructureMaterial.Fieldstone, 10);
				WallRun(d, new Vector3(outer - wall * 0.5f, 0f, -inner), new Vector3(outer - wall * 0.5f, 0f, inner), wall, floor, stage, east, StructureMaterial.Fieldstone, 14);
				Window(d, new Vector3(outer, 1.8f, 0f), Vector3.right, wall, 900);
				Window(d, new Vector3(1.1f, 1.8f, -outer), Vector3.back, wall, 908);
				HangDoor(d, new Vector3(-0.5f, floor, inner + 0.05f), Vector3.right, Vector3.back, 1f, 2.25f, StructureDoorKind.Planks);
				// The stage over the room east of the stair box, railed round its open edges.
				d.Add(StructureShapes.BoxBetween(new Vector3(0.06f, stage - 0.2f, -inner), new Vector3(inner, stage, inner), StructureMaterial.Timber, 18)).Grain = Vector3.forward;
				float e = outer - 0.1f, y = stage + TowerRail;
				d.Add(StructureShapes.Beam(new Vector3(e, y, -e), new Vector3(e, y, e), 0.08f, 0.08f, StructureMaterial.Timber, 19));
				d.Add(StructureShapes.Beam(new Vector3(0.06f, y, -e), new Vector3(e, y, -e), 0.08f, 0.08f, StructureMaterial.Timber, 20));
				d.Add(StructureShapes.Beam(new Vector3(0.06f, y, e), new Vector3(e, y, e), 0.08f, 0.08f, StructureMaterial.Timber, 21));
				d.Room(new Vector3(0f, floor, 0f), new Vector2(inner * 2f, inner * 2f), stage - 0.2f - floor, StructureRoomKind.Guard);
				d.RoomClear(new Vector2(-inner, -inner), new Vector2(0.45f, inner));
				// The way from the door round the stair's foot into the room.
				d.RoomClear(new Vector2(0f, 0.5f), new Vector2(1.1f, inner));
			}
			else
			{
				// A fieldstone pad under the frame, the stair's foot on it.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(post * 2f + 0.3f, floor + FoundationDepth, post * 2f + 0.3f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
			}

			// The four posts, the plate on their heads.
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(sx * post, (legFrom + plate) * 0.5f, sz * post), new Vector3(0.24f, plate - legFrom, 0.24f), StructureMaterial.Timber, 30 + (sx + 1) + (sz + 1) / 2));
				}
			}
			for (int f = 0; f < 4; f++)
			{
				Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
				d.Add(StructureShapes.Beam(q * new Vector3(-post - 0.12f, plate - 0.1f, -post), q * new Vector3(post + 0.12f, plate - 0.1f, -post), 0.2f, 0.2f, StructureMaterial.Timber, 36 + f));
			}
			if (d.Mid)
			{
				// Cross braces on the east face; half braces on the north and south faces east of the stair box.
				float[] tiers = stoneBase ? new[] { stage + 0.1f, platform - 0.3f } : new[] { 0.3f, stage - 0.1f, platform - 0.3f };
				int k = 50;
				for (int t = 0; t + 1 < tiers.Length; t++)
				{
					float y0 = tiers[t], y1 = tiers[t + 1];
					d.Add(StructureShapes.Beam(new Vector3(post + 0.13f, y0, -post), new Vector3(post + 0.13f, y1, post), 0.12f, 0.1f, StructureMaterial.Timber, k++, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(post + 0.13f, y0, post), new Vector3(post + 0.13f, y1, -post), 0.12f, 0.1f, StructureMaterial.Timber, k++, StructureRole.Detail));
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(post, y0, sz * (post + 0.13f)), new Vector3(0.2f, y1, sz * (post + 0.13f)), 0.12f, 0.1f, StructureMaterial.Timber, k++, StructureRole.Detail));
					}
				}
			}

			// The stair: two storeys of two flights in the west half, the near end at +z, the spine between the lanes.
			int risers = TowerRisers((stage - floor) * 0.5f);
			var stair = new TowerStair
			{
				Origin = new Vector3(-inner, 0f, (risers - 1) * StepTread * 0.5f), Run = Vector3.back, Across = Vector3.right,
				Lane = 1f, Spine = 0.1f, NearLanding = 1.05f, FarLanding = 1.05f, Waist = 0.25f, Material = StructureMaterial.Timber,
			};
			float nearZ = stair.Origin.z;
			TowerFlights(d, stair, new[] { floor, stage, platform }, risers, 2000, false, false, false, true, true);
			// The stair box boarded: the west, south and the room side to a rail's height over the platform round the trap; the
			// north over the landing at the stage. Below 0.9 m a fall is a stumble.
			float boardsFrom = stoneBase ? stage : 0.9f, top = platform + TowerRail + 0.05f;
			const float board = 0.05f;
			void Boards(Vector3 min, Vector3 max, int key) => d.Add(StructureShapes.BoxBetween(min, max, StructureMaterial.Timber, key)).Grain = Vector3.up;
			Boards(new Vector3(-inner - board, boardsFrom, -inner), new Vector3(-inner, platform, inner), 60);
			Boards(new Vector3(-inner - board, platform, -inner), new Vector3(-inner, top, 0f), 61);
			Boards(new Vector3(-inner, boardsFrom, -inner - board), new Vector3(0.06f, top, -inner), 62);
			Boards(new Vector3(0f, 0.9f, -inner), new Vector3(0.06f, top, nearZ), 63);
			Boards(new Vector3(-inner, stoneBase ? stage : 2.3f, inner), new Vector3(0.06f, stage + TowerRail + 0.05f, inner + board), 64);
			if (!stoneBase)
			{
				// The room side of the landing at the stage (in v1 the stage floor runs on from it).
				Boards(new Vector3(0f, 2.3f, nearZ), new Vector3(0.06f, stage + TowerRail + 0.05f, inner), 65);
			}

			// The platform round the trap, overhanging the posts as a gallery, a rail all round; a bar across the outer lane where
			// the trap begins over it.
			float plat = platform - 0.2f;
			void Deck(float x0, float x1, float z0, float z1, int key) => d.Add(StructureShapes.BoxBetween(new Vector3(x0, plat, z0), new Vector3(x1, platform, z1), StructureMaterial.Timber, key)).Grain = Vector3.forward;
			Deck(0f, deck, -deck, deck, 70);
			Deck(-deck, 0f, nearZ, deck, 71);
			Deck(-inner, -inner + stair.Lane, 0f, nearZ, 72);
			Deck(-deck, -inner, -deck, nearZ, 73);
			Deck(-inner, 0f, -deck, -inner, 74);
			d.Add(StructureShapes.Beam(new Vector3(-inner, platform + TowerRail, 0.03f), new Vector3(-inner + stair.Lane, platform + TowerRail, 0.03f), 0.07f, 0.07f, StructureMaterial.Timber, 75));
			float edge = deck - 0.05f;
			for (int face = 0; face < 4; face++)
			{
				Quaternion q = Quaternion.Euler(0f, face * 90f, 0f);
				d.Add(StructureShapes.Beam(q * new Vector3(-edge, platform + 1.0f, -edge), q * new Vector3(edge, platform + 1.0f, -edge), 0.1f, 0.12f, StructureMaterial.Timber, 80 + face));
				d.Add(StructureShapes.Box(q * new Vector3(-edge, platform + 0.5f, -edge), new Vector3(0.12f, 1f, 0.12f), StructureMaterial.Timber, 84 + face));
				if (d.Mid)
				{
					d.Add(StructureShapes.Block(q * new Vector3(0f, platform + 0.45f, -edge), new Vector3(edge * 2f, 0.7f, 0.06f), q, StructureMaterial.Timber, 88 + face, StructureRole.Detail));
				}
			}

			// The roof: a pyramid on the plate (its palette's pitch, or flat, where the palette asks: StructureRoofs, I6h).
			StructureMaterial roofMaterial = stoneBase ? StructureMaterial.Timber : StructureMaterial.Thatch;
			const float roofRise = 1.8f, roofHalf = 2.5f;
			OwnRoofPlan roof = OwnRoof(d, roofMaterial, roofRise, roofHalf);
			using (d.Builds(RoofRoles))
			{
				if (roof.Flat)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, plate + 0.08f, 0f), new Vector3(roofHalf * 2f, 0.16f, roofHalf * 2f), roofMaterial, 100, StructureRole.Roof)).Grain = Vector3.forward;
				}
				else
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(roofHalf * 1.414f, plate), new Vector2(0f, plate + roof.Rise) }, 4,
						roofMaterial, 100, false, Mathf.PI * 0.25f, false, false, StructureRole.Roof));
				}
			}
			d.Mark("lookout", new Vector3(1.4f, platform, -1.4f), 0f);
		}

		// ── Gatehouse ─────────────────────────────────────────────

		/// <summary>
		/// The gatehouse: 12 m along the curtain, 9 m deep, a 3.5×4.5 m passage through it under the portcullis. A guard room
		/// in each tower from the bailey; a stair up the east tower to the hall over the passage (where the portcullis is wound,
		/// the walk coming in at either end); a second up the hall's back wall to the fighting top. v1's towers have
		/// half-round fronts to the field.
		/// </summary>
		private static void TwinTowerGatehouse(StructureDraft d)
		{
			bool round = d.Variant == 1;
			const float half = 6f, depth = 9f, wall = 0.8f, floor = 0.3f, hall = WallWalkHeight, top = 9.6f;
			const float passageW = 3.5f, passageH = 4.5f;
			float hd = depth * 0.5f, inZ = hd - wall, inX = half - wall, jamb = passageW * 0.5f, towerIn = jamb + wall;
			float towerMid = (towerIn + inX) * 0.5f;
			// The floors: each tower's on its plinth, the passage's cobbles.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? jamb : -half, -FoundationDepth, -hd), new Vector3(s > 0 ? half : -jamb, floor, hd), StructureMaterial.Stone, 1 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, -FoundationDepth * 0.5f, 0f), new Vector3(passageW + 0.4f, FoundationDepth, depth), StructureMaterial.Fieldstone, 3));
			// The walls: the front over the passage's mouth with arrow loops, the back with the towers' doors, the flanks with the
			// walk's doors, the passage's sides.
			Vector2 Loop(float y) => new Vector2(y - WindowHeight * 0.5f, y + WindowHeight * 0.5f);
			var front = new List<WallOpening> { new WallOpening { Along = half, Width = passageW, Bottom = 0f, Top = passageH } };
			if (!round)
			{
				// Arrow loops to the field: the west guard room's, the hall's over the east tower (v1's drums stand before both).
				front.Add(new WallOpening { Along = half - towerMid, Width = WindowWidth, Bottom = Loop(2f).x, Top = Loop(2f).y });
				front.Add(new WallOpening { Along = half + towerMid, Width = WindowWidth, Bottom = Loop(6.6f).x, Top = Loop(6.6f).y });
			}
			var back = new List<WallOpening>
			{
				new WallOpening { Along = half, Width = passageW, Bottom = 0f, Top = passageH },
				new WallOpening { Along = half - towerMid, Width = 1.2f, Bottom = floor, Top = floor + 2.3f },
				new WallOpening { Along = half + towerMid, Width = 1.2f, Bottom = floor, Top = floor + 2.3f },
			};
			var flank = new List<WallOpening> { new WallOpening { Along = inZ + TowerWalkLine, Width = TowerWalkDoorWidth, Bottom = hall, Top = hall + TowerWalkDoorHeight } };
			WallRun(d, new Vector3(-half, 0f, -hd + wall * 0.5f), new Vector3(half, 0f, -hd + wall * 0.5f), wall, 0f, top, front, StructureMaterial.Stone, 10);
			WallRun(d, new Vector3(-half, 0f, hd - wall * 0.5f), new Vector3(half, 0f, hd - wall * 0.5f), wall, 0f, top, back, StructureMaterial.Stone, 16);
			WallRun(d, new Vector3(-half + wall * 0.5f, 0f, -inZ), new Vector3(-half + wall * 0.5f, 0f, inZ), wall, 0f, top, flank, StructureMaterial.Stone, 22);
			WallRun(d, new Vector3(half - wall * 0.5f, 0f, -inZ), new Vector3(half - wall * 0.5f, 0f, inZ), wall, 0f, top, new List<WallOpening>(flank), StructureMaterial.Stone, 28);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? jamb : -towerIn, 0f, -inZ), new Vector3(s > 0 ? towerIn : -jamb, hall - TowerSlab, inZ), StructureMaterial.Stone, 34 + (s > 0 ? 1 : 0)));
			}
			if (!round)
			{
				Window(d, new Vector3(-towerMid, 2f, -hd), Vector3.back, wall, 900);
				Window(d, new Vector3(towerMid, 6.6f, -hd), Vector3.back, wall, 908);
			}
			// The passage's ceiling under the hall floor.
			d.Add(StructureShapes.BoxBetween(new Vector3(-jamb, passageH, -inZ), new Vector3(jamb, hall - TowerSlab, inZ), StructureMaterial.Stone, 36));

			// The guard rooms' doors from the bailey, a step up to each.
			for (int s = -1; s <= 1; s += 2)
			{
				Steps(d, new Vector3(s * towerMid, 0f, hd), Vector3.forward, 1.4f, floor, StructureMaterial.Stone, 40 + (s > 0 ? 2 : 0));
				HangDoor(d, new Vector3(s * towerMid, floor, inZ + 0.05f), Vector3.right, Vector3.back, 1.2f, 2.3f, StructureDoorKind.Studded);
			}

			// Up the east tower: the near end by its door, climbing toward the field, back up beside the passage to the hall.
			int risers = TowerRisers((hall - floor) * 0.5f);
			float run = (risers - 1) * StepTread;
			var lower = new TowerStair
			{
				Origin = new Vector3(inX, 0f, run * 0.5f), Run = Vector3.back, Across = Vector3.left,
				Lane = (inX - towerIn - 0.2f) * 0.5f, Spine = 0.2f, NearLanding = inZ - run * 0.5f, FarLanding = inZ - run * 0.5f, Waist = 0.4f, Material = StructureMaterial.Stone,
			};
			TowerFlights(d, lower, new[] { floor, hall }, risers, 2000, true, true, true, false, false);
			// Up the hall's back wall from over the passage, climbing west, back to the fighting top.
			int upperRisers = TowerRisers((top - hall) * 0.5f);
			float upperRun = (upperRisers - 1) * StepTread;
			var upper = new TowerStair
			{
				Origin = new Vector3(-0.6f, 0f, inZ), Run = Vector3.left, Across = Vector3.back,
				Lane = 1.2f, Spine = 0.2f, NearLanding = 1.3f, FarLanding = inX - 0.6f - upperRun, Waist = 0.4f, Material = StructureMaterial.Stone,
			};
			TowerFlights(d, upper, new[] { hall, top }, upperRisers, 2600, true, true, true, false, true);
			TowerSlabAround(d, -inX, inX, -inZ, inZ, hall, TowerWell(lower, risers), 3000);
			TowerSlabAround(d, -inX, inX, -inZ, inZ, top, TowerWell(upper, upperRisers), 3010);

			// The portcullis: a server-run gate (PointOfInterestDoors) in its grooves near the outer face, raised by day; it
			// drops to close the passage and rises into the wall over it. Its windlass stands in the hall behind that wall.
			d.Door(new Vector3(-passageW * 0.5f, 0f, -depth * 0.5f + 0.4f), Vector3.right, Vector3.forward, passageW, passageH, StructureDoorKind.Iron,
				StructureDoorMotion.Lift, true);
			float winchZ = -inZ + 0.55f;
			d.Add(StructureShapes.Beam(new Vector3(-1.3f, hall + 0.75f, winchZ), new Vector3(1.3f, hall + 0.75f, winchZ), 0.34f, 0.34f, StructureMaterial.Timber, 120));
			// The bearer over the drum, tying the cheeks' heads (and leaving nothing to stand on over the drum).
			d.Add(StructureShapes.Box(new Vector3(0f, hall + 1.62f, winchZ), new Vector3(3.08f, 0.16f, 0.12f), StructureMaterial.Timber, 129));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 1.45f, hall + 0.85f, winchZ), new Vector3(0.18f, 1.7f, 0.7f), StructureMaterial.Timber, 121 + (s > 0 ? 1 : 0)));
				if (d.Mid)
				{
					// The chains from the drum into the wall's slot, and the capstan bars.
					d.Add(StructureShapes.Beam(new Vector3(s * 0.8f, hall + 0.75f, winchZ - 0.1f), new Vector3(s * 0.8f, hall + 1.6f, -inZ + 0.02f), 0.05f, 0.05f, StructureMaterial.Iron, 123 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(s * 1.56f, hall + 0.75f, winchZ - 0.6f), new Vector3(s * 1.56f, hall + 0.75f, winchZ + 0.6f), 0.06f, 0.06f, StructureMaterial.Timber, 125 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (passageW * 0.5f + 0.06f), passageH * 0.5f, -depth * 0.5f + 0.4f), new Vector3(0.14f, passageH, 0.2f), StructureMaterial.Iron, 127 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The fighting top: parapets round it, merlons along them; v1's drum fronts carry theirs round.
			const float parapet = top + 1.1f;
			int pk = 140;
			void Parapet(Vector3 a, Vector3 b)
			{
				WallRun(d, a, b, 0.5f, top, parapet, null, StructureMaterial.Stone, pk);
				Crenellate(d, a + Vector3.up * parapet, b + Vector3.up * parapet, 0.5f, 0.85f, 0.8f, 0.55f, StructureMaterial.Stone, 500 + pk * 10);
				pk++;
			}
			if (round)
			{
				Parapet(new Vector3(-jamb, 0f, -hd + 0.25f), new Vector3(jamb, 0f, -hd + 0.25f));
			}
			else
			{
				Parapet(new Vector3(-half, 0f, -hd + 0.25f), new Vector3(half, 0f, -hd + 0.25f));
			}
			Parapet(new Vector3(-half, 0f, hd - 0.25f), new Vector3(half, 0f, hd - 0.25f));
			Parapet(new Vector3(-half + 0.25f, 0f, -hd + 0.5f), new Vector3(-half + 0.25f, 0f, hd - 0.5f));
			Parapet(new Vector3(half - 0.25f, 0f, -hd + 0.5f), new Vector3(half - 0.25f, 0f, hd - 0.5f));
			float drum = (half - jamb) * 0.5f;
			if (round)
			{
				int seg = d.Seg(10, 6);
				for (int s = -1; s <= 1; s += 2)
				{
					var c = new Vector3(s * towerMid, 0f, -hd);
					int key = 200 + (s > 0 ? 100 : 0);
					for (int i = 0; i < seg; i++)
					{
						float a0 = Mathf.PI + i * Mathf.PI / seg, a1 = Mathf.PI + (i + 1) * Mathf.PI / seg;
						d.Add(RingBlock(c, 0f, drum, -FoundationDepth, top, a0, a1, StructureMaterial.Stone, key + i, StructureRole.Structure));
						d.Add(RingBlock(c, drum - 0.5f, drum, top, parapet, a0, a1, StructureMaterial.Stone, key + 20 + i, StructureRole.Structure));
						d.Add(RingBlock(c, drum - 0.5f, drum, parapet, parapet + 0.85f, Mathf.Lerp(a0, a1, 0.2f), Mathf.Lerp(a0, a1, 0.75f), StructureMaterial.Stone, key + 40 + i, StructureRole.Detail));
					}
				}
			}
			// A torch each side of the gate on the towers' faces, lit day and night (2026-10-12: torches).
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * (jamb + 0.85f);
				float z = round ? -hd - Mathf.Sqrt(drum * drum - (x - s * towerMid) * (x - s * towerMid)) : -hd;
				Vector3 outward = round ? new Vector3(x - s * towerMid, 0f, z + hd).normalized : Vector3.back;
				Matrix4x4 place = Matrix4x4.TRS(new Vector3(x, 2.6f, z), Quaternion.LookRotation(-outward, Vector3.up), Vector3.one);
				MountedTorch(d, place, false, 130 + (s > 0 ? 5 : 0));
			}

			// The rooms: a guard room in each tower (the east one's stair kept clear), the hall over the passage (the walk's way
			// through, both stairs and the windlass kept clear).
			d.Room(new Vector3(-towerMid, floor, 0f), new Vector2(inX - towerIn, inZ * 2f), hall - TowerSlab - floor, StructureRoomKind.Guard);
			d.Room(new Vector3(towerMid, floor, 0f), new Vector2(inX - towerIn, inZ * 2f), hall - TowerSlab - floor, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(towerIn, -inZ), new Vector2(inX, run * 0.5f + 0.25f));
			d.Room(new Vector3(0f, hall, 0f), new Vector2(inX * 2f, inZ * 2f), top - TowerSlab - hall, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(towerIn - 0.25f, -inZ), new Vector2(inX, run * 0.5f + 0.25f));
			d.RoomClear(new Vector2(-inX, TowerWalkLine - 0.95f), new Vector2(inX, TowerWalkLine + 0.95f));
			d.RoomClear(new Vector2(-inX, inZ - upper.Width - 0.25f), new Vector2(1f, inZ));
			d.RoomClear(new Vector2(-2f, -inZ), new Vector2(2f, winchZ + 0.7f));

			d.Mark("curtain", new Vector3(half, hall, TowerWalkLine), 90f);
			d.Mark("curtain", new Vector3(-half, hall, TowerWalkLine), -90f);
			d.Mark("lookout", new Vector3(0f, top, -2.5f), 0f);
		}
	}
}
#endif
