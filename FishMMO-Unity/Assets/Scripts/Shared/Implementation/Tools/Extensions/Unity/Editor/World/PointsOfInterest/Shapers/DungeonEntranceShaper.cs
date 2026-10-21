#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Sets a dungeon entrance's door into its rock face (2026-10-18: "Dungeon entrances look boring and misaligned"): finds
	/// the face's foot along the site's heading, turns the heading square to the real face, moves the site to the door's
	/// threshold there, and cuts the ground so the door stands recessed in the rock with a level forecourt before it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The planner stands a face site on its 16 m cell's middle, facing one of sixteen headings (FaceScore), at
	/// the ground's height there: the door stood metres out on the slope before the face, up to 11° off it, sunk into the
	/// hillside or floating over its fall. A face's foot is where the ground turns steep; marched along the heading from the
	/// cell's middle it is found to a quarter metre (<see cref="FindFace"/>), and the face's own fall line (the mean height
	/// gradient over the face above the foot) gives the heading to a degree, not to 22.5°.
	/// </para>
	/// <para>
	/// <b>The cut</b> (<see cref="CutHeight"/>), in the door's frame: the forecourt levelled at the door's floor in front of
	/// the threshold, the facade's notch at the same floor behind it, and banks from both back to the ground as it was — cut
	/// rock-steep behind the threshold (the notch's cheeks), at 50° in front, filled at 35° where the forecourt stands out
	/// over falling ground. The frame is the kit's (StructurePieces.DungeonHalfWidth, DungeonFrontReach): every dungeon
	/// piece keeps inside it, so the cut is right for whichever variant the site draws. Idempotent: the ground is only held
	/// between bounds that depend on the plan alone, so cutting again changes nothing.
	/// </para>
	/// <para>
	/// The architecture itself is laid by the template's props (the DungeonPortal or DungeonBarrow centrepiece at the
	/// record, which is now the threshold) and DungeonEntranceFeature (the doorway's interaction box, the passage's
	/// darkness, the forecourt's braziers and guardians). A large cave's dungeon gate is placed against its great cavern's
	/// back wall instead (<see cref="TryCavernGate"/>), with no cut.
	/// </para>
	/// </remarks>
	public sealed class DungeonEntranceShaper : IPointOfInterestShaper
	{
		public static readonly DungeonEntranceShaper Instance = new DungeonEntranceShaper();

		public const string ShaperName = "DungeonEntrance";
		private const int Format = 1;
		private const int ValueCount = 7;

		public bool Handles(POIType kind) => kind == POIType.DungeonEntrance;

		// ── The frame ─────────────────────────────────────────────

		/// <summary>The forecourt's levelled half-width and depth before the threshold, metres: the kit's frame and a margin.</summary>
		public static float PadHalf => StructurePieces.DungeonHalfWidth + 1f;
		public static float PadDepth => StructurePieces.DungeonFrontReach + 1.5f;
		/// <summary>The facade's notch: its half-width and how far it is cut back behind the threshold, metres.</summary>
		public const float NotchHalf = 5f, NotchDepth = 2f;
		/// <summary>The banks' grades (rise over run): behind the threshold (rock-cut cheeks), cut in front, filled in front.</summary>
		public const float CheekGrade = 2.5f, CutGrade = 1.2f, FillGrade = 0.7f;
		/// <summary>How far the threshold stands back into the face from its foot, metres: the facade's front about at the foot.</summary>
		public const float Recess = 0.8f;
		/// <summary>The steepest the ground may be at a face's foot to count as its foot (rise over run over a metre).</summary>
		public const float ToeGrade = 0.75f;
		/// <summary>How far along the heading the foot is looked for, and how much the face must rise over the 8 m behind it.</summary>
		public const float MaxMarch = 24f, MinFaceRise = 4f;
		/// <summary>The most the heading is turned to the face's fall line, degrees (beyond it the planner's face is not this face).</summary>
		public const float MaxTurn = 40f;
		/// <summary>The most the forecourt may stand over the ground at its far corners, metres: beyond it the door is on a ledge.</summary>
		public const float MaxFill = 6f;

		/// <summary>What a planned entrance stores (its shape's values).</summary>
		public struct Plan
		{
			/// <summary>The middle of the doorway's outer face line on the floor (world; y the floor).</summary>
			public Vector3 Threshold;
			/// <summary>The heading out of the face, degrees.</summary>
			public float Yaw;
			/// <summary>The floor the forecourt and the notch are levelled to.</summary>
			public float Floor;
			/// <summary>How much the face rises over the 8 m behind its foot.</summary>
			public float Rise;

			public Vector3 Outward => new Vector3(Mathf.Sin(Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(Yaw * Mathf.Deg2Rad));

			public float[] ToValues() => new[] { Format, Threshold.x, Threshold.y, Threshold.z, Yaw, Floor, Rise };

			public static bool TryFrom(float[] v, out Plan plan)
			{
				plan = default;
				if (v == null || v.Length != ValueCount || Mathf.RoundToInt(v[0]) != Format)
				{
					return false;
				}
				plan = new Plan { Threshold = new Vector3(v[1], v[2], v[3]), Yaw = v[4], Floor = v[5], Rise = v[6] };
				return true;
			}
		}

		private static Vector2 Dir(float yaw) => new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));

		private static float YawOf(Vector2 d) => Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 360f);

		// ── Finding the face (pure) ───────────────────────────────

		/// <summary>
		/// The foot of the face behind <paramref name="start"/> along the way back from <paramref name="yaw"/> (the face lies at
		/// yaw + 180°): the first point, a quarter metre at a time, where the ground climbs <see cref="ToeGrade"/> over the next
		/// metre and keeps climbing (3.5 m over the next 6), so a bump on the forecourt is not the face. NaN distance for none.
		/// </summary>
		public static float MarchToFoot(Func<float, float, float> ground, Vector2 start, float yaw)
		{
			Vector2 back = -Dir(yaw);
			for (float t = 0f; t <= MaxMarch; t += 0.25f)
			{
				Vector2 p = start + back * t;
				float h = ground(p.x, p.y);
				Vector2 one = p + back, six = p + back * 6f;
				if (ground(one.x, one.y) - h >= ToeGrade && ground(six.x, six.y) - h >= 3.5f)
				{
					return t;
				}
			}
			return float.NaN;
		}

		/// <summary>
		/// The face's fall line over its lower part behind <paramref name="foot"/>: the heading straight out of it (down its
		/// mean height gradient over a 9 × 5 m patch), or NaN where the patch is near flat.
		/// </summary>
		public static float FallLine(Func<float, float, float> ground, Vector2 foot, float yaw)
		{
			Vector2 back = -Dir(yaw), side = new Vector2(back.y, -back.x);
			Vector2 sum = Vector2.zero;
			for (float u = 1.5f; u <= 5.5f; u += 1f)
			{
				for (float v = -4f; v <= 4f; v += 2f)
				{
					Vector2 q = foot + back * u + side * v;
					sum += new Vector2(ground(q.x + 1f, q.y) - ground(q.x - 1f, q.y), ground(q.x, q.y + 1f) - ground(q.x, q.y - 1f)) * 0.5f;
				}
			}
			// Out of the face is down it.
			return sum.sqrMagnitude < 1e-4f ? float.NaN : YawOf(-sum);
		}

		/// <summary>
		/// Where a site's door stands on its face (pure): the heading turned square to the face (twice, the foot found again
		/// along each turn), the foot, the threshold <see cref="Recess"/> back into the face from it, and the floor the cut
		/// levels to (between the foot's height and the forecourt's: as much cut as fill). False with
		/// <paramref name="problem"/> where there is no face to set a door in.
		/// </summary>
		public static bool FindFace(Func<float, float, float> ground, Vector3 start, float yaw, out Plan plan, out string problem)
		{
			plan = default;
			problem = null;
			var s = new Vector2(start.x, start.z);
			float heading = Mathf.Repeat(yaw, 360f);
			float t = float.NaN;
			for (int pass = 0; pass < 3; pass++)
			{
				t = MarchToFoot(ground, s, heading);
				if (float.IsNaN(t))
				{
					problem = $"no face within {MaxMarch:0} m behind it";
					return false;
				}
				if (pass == 2)
				{
					break;
				}
				float fall = FallLine(ground, s - Dir(heading) * t, heading);
				if (float.IsNaN(fall))
				{
					break;
				}
				// Square to the face, within reason of the heading the planner scored it on.
				float turn = Mathf.Clamp(Mathf.DeltaAngle(yaw, fall), -MaxTurn, MaxTurn);
				heading = Mathf.Repeat(yaw + turn, 360f);
			}
			Vector2 o = Dir(heading);
			Vector2 foot = s - o * t;
			float footHeight = ground(foot.x, foot.y);
			Vector2 eight = foot - o * 8f;
			float rise = ground(eight.x, eight.y) - footHeight;
			if (rise < MinFaceRise)
			{
				problem = $"its face rises only {rise:0.0} m";
				return false;
			}
			// The forecourt's own ground along its middle: the floor halves the difference, cutting into the foot as much as it fills.
			float front = 0f;
			int n = 0;
			for (float u = 1f; u <= PadDepth; u += 2f)
			{
				Vector2 q = foot + o * u;
				front += ground(q.x, q.y);
				n++;
			}
			front /= Mathf.Max(1, n);
			float floor = Mathf.Clamp(0.5f * (footHeight + front), footHeight - 2f, footHeight);
			Vector2 threshold = foot - o * Recess;
			plan = new Plan { Threshold = new Vector3(threshold.x, floor, threshold.y), Yaw = heading, Floor = floor, Rise = rise };
			return true;
		}

		// ── The cut (pure) ────────────────────────────────────────

		/// <summary>
		/// The ground at (x, z) once a door's forecourt and notch are cut, from <paramref name="height"/>: level at the floor
		/// over the forecourt and the notch, held within banks back to the ground outside them (steep behind the threshold,
		/// <see cref="CutGrade"/> and <see cref="FillGrade"/> in front; no fill behind it, where the face is). Idempotent.
		/// </summary>
		public static float CutHeight(in Plan plan, float x, float z, float height)
		{
			Vector2 o = Dir(plan.Yaw), side = new Vector2(o.y, -o.x);
			var p = new Vector2(x - plan.Threshold.x, z - plan.Threshold.z);
			float u = Vector2.Dot(p, o), v = Mathf.Abs(Vector2.Dot(p, side));
			// Out of the forecourt and the notch: the nearer of the two rectangles.
			float padU = Mathf.Max(0f, Mathf.Max(-u, u - PadDepth)), padV = Mathf.Max(0f, v - PadHalf);
			float notchU = Mathf.Max(0f, Mathf.Max(-u - NotchDepth, u)), notchV = Mathf.Max(0f, v - NotchHalf);
			float outPad = Mathf.Sqrt(padU * padU + padV * padV), outNotch = Mathf.Sqrt(notchU * notchU + notchV * notchV);
			float away = Mathf.Min(outPad, outNotch);
			float s = Mathf.Clamp01((u + 2f) / 4f);
			float grade = Mathf.Lerp(CheekGrade, CutGrade, s * s * (3f - 2f * s));
			float high = plan.Floor + away * grade;
			if (u < 0f)
			{
				return Mathf.Min(height, high);
			}
			float low = plan.Floor - outPad * FillGrade;
			return Mathf.Clamp(height, low, high);
		}

		/// <summary>The area a plan's cut can change: the forecourt, the notch and the farthest bank either reaches.</summary>
		public static Rect CutArea(in Plan plan)
		{
			float reach = PadDepth + PadHalf + MaxFill / FillGrade + 2f;
			return new Rect(plan.Threshold.x - reach, plan.Threshold.z - reach, 2f * reach, 2f * reach);
		}

		/// <summary>The forecourt's corners and middle, world (x, z): what is checked for water and for a ledge.</summary>
		private static IEnumerable<Vector2> PadSamples(Plan plan)
		{
			Vector2 o = Dir(plan.Yaw), side = new Vector2(o.y, -o.x), t = new Vector2(plan.Threshold.x, plan.Threshold.z);
			for (int i = 0; i <= 2; i++)
			{
				for (int j = -1; j <= 1; j++)
				{
					yield return t + o * (PadDepth * i * 0.5f) + side * (PadHalf * j);
				}
			}
		}

		// ── The seam ──────────────────────────────────────────────

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			if (!FindFace(context.GroundAt, site.Position, site.Yaw, out Plan plan, out string problem))
			{
				context.Reject(site, problem);
				return;
			}
			if (plan.Floor < CaveShaper.SeaLevel + 1f)
			{
				context.Reject(site, "its door would stand at the sea's edge");
				return;
			}
			foreach (Vector2 q in PadSamples(plan))
			{
				SceneWater water = context.Water;
				if (water != null && (!float.IsNegativeInfinity(water.RiverSurfaceNear(q.x, q.y, 0f)) || !float.IsNegativeInfinity(water.LakeSurfaceNear(q.x, q.y))))
				{
					context.Reject(site, "its forecourt would stand in water");
					return;
				}
				if (plan.Floor - context.GroundAt(q.x, q.y) > MaxFill)
				{
					context.Reject(site, "the ground falls away before its face: its door would stand on a ledge");
					return;
				}
			}
			Plan cut = plan;
			PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, CutArea(plan), (x, z, h) => CutHeight(cut, x, z, h));
			// The site is its door: the props lay the doorway's piece here, the way comes to it, the name toast is its.
			site.Position = plan.Threshold;
			site.Yaw = plan.Yaw;
			// Nothing grows or tumbles onto the forecourt, the facade or the rock just over it.
			Vector3 o = plan.Outward;
			Vector3 court = plan.Threshold + o * (0.5f * PadDepth);
			context.AddKeepOut(site, court.x, court.z, new Vector2(PadHalf, 0.5f * PadDepth).magnitude + 2f);
			Vector3 face = plan.Threshold - o * (0.5f * StructurePieces.DungeonRearReach);
			context.AddKeepOut(site, face.x, face.z, StructurePieces.DungeonHalfWidth + 4f);
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = plan.Threshold,
				Yaw = plan.Yaw,
				Size = new Vector3(2f * PadHalf, plan.Rise, PadDepth),
				Seed = site.SiteSeed,
				Values = plan.ToValues(),
			});
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			// The ground is cut at plan time; the door is the template's (its props and DungeonEntranceFeature).
		}

		/// <summary>The plan stored for a site, false for none (a site cut before 2026-10-18, or not a dungeon entrance).</summary>
		public static bool TryPlan(IEnumerable<PointOfInterestShape> shapes, int siteId, out Plan plan)
		{
			plan = default;
			if (shapes == null)
			{
				return false;
			}
			foreach (PointOfInterestShape shape in shapes)
			{
				if (shape != null && shape.SiteId == siteId && shape.Shaper == ShaperName && Plan.TryFrom(shape.Values, out plan))
				{
					return true;
				}
			}
			return false;
		}

		// ── A large cave's dungeon gate ───────────────────────────

		/// <summary>Where a dungeon gate stands against a great cavern's wall: its threshold (world, y its floor), the piece's turn and its scale.</summary>
		public struct CavernGate
		{
			public Vector3 Threshold;
			/// <summary>The gate's own turn about +y, degrees (its +z into the rock, its front facing the cavern).</summary>
			public float PieceYaw;
			public float Scale;
			/// <summary>The heading out of the gate into the cavern.</summary>
			public float Facing => Mathf.Repeat(PieceYaw + 180f, 360f);
		}

		/// <summary>How far in front of the gate's face the cavern must be open, across its width and up its doorway, metres.</summary>
		private const float GateClearAhead = 1.8f, GateClearHalf = 3.6f, GateClearHeight = 5.4f;
		/// <summary>How far the gate's face stands proud of the wall at its middle, metres (the rock round it is rough).</summary>
		private const float GateInset = 0.5f;
		/// <summary>The gate's height at scale 1 the cavern's roof is measured against, metres.</summary>
		private const float GateHeight = 9.5f;

		/// <summary>
		/// The dungeon gate of a large cave's great cavern (pure, from the cave's own field): built against the cavern's wall
		/// along the chamber's heading (deeper in, away from the passage the way comes in by) or, where a passage opens there or
		/// the wall leans in over it, the nearest heading round from it that takes one; its face a little proud of the rock, its
		/// floor the field's, scaled to the roof over it. False where no wall of the chamber takes a gate.
		/// </summary>
		public static bool TryCavernGate(CaveSystem system, int siteId, out CavernGate gate) => TryCavernGate(system, siteId, null, out gate);

		/// <summary>
		/// <see cref="TryCavernGate(CaveSystem, int, out CavernGate)"/> on a field of the caller's (<paramref name="voidAt"/>, the
		/// system's frame; null for the system's own): the lava's plan finds the gate on the field without its lava.
		/// </summary>
		internal static bool TryCavernGate(CaveSystem system, int siteId, Func<Vector3, float> voidAt, out CavernGate gate)
		{
			gate = default;
			if (system == null || !system.TryChamberOf(siteId, out CaveChamber chamber))
			{
				return false;
			}
			voidAt ??= system.Void;
			float reach = Mathf.Max(chamber.Radii.x, chamber.Radii.z) + 20f;
			foreach (float off in new[] { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 105f, -105f })
			{
				float heading = Mathf.Repeat(chamber.Yaw + off, 360f);
				Vector2 d = Dir(heading);
				var dir = new Vector3(d.x, 0f, d.y);
				var centre = new Vector3(chamber.Centre.x, chamber.Floor, chamber.Centre.z);
				// The wall a metre over the floor's plane, a half metre at a time and then to a tenth.
				float wall = float.NaN;
				for (float r = 0.5f; r <= reach; r += 0.5f)
				{
					Vector3 p = centre + dir * r;
					p.y = CaveSystem.FloorOf(chamber, p) + 1f;
					if (voidAt(p) >= 0f)
					{
						for (float back = 0f; back <= 0.5f; back += 0.1f)
						{
							Vector3 q = centre + dir * (r - back);
							q.y = CaveSystem.FloorOf(chamber, q) + 1f;
							if (voidAt(q) < 0f)
							{
								wall = r - back;
								break;
							}
						}
						break;
					}
				}
				if (float.IsNaN(wall) || wall < 4f)
				{
					continue;
				}
				// Stepped in from the wall until the doorway and its apron are open air on a floor.
				for (float inset = GateInset; inset <= GateInset + 6f; inset += 0.5f)
				{
					Vector3 t = centre + dir * (wall - inset);
					if (!FloorUnder(voidAt, t, out float floor) || Mathf.Abs(floor - CaveSystem.FloorOf(chamber, t)) > 0.8f)
					{
						continue;
					}
					t.y = floor;
					if (!Clear(voidAt, t, -dir, floor))
					{
						continue;
					}
					float roof = RoofOver(voidAt, t - dir * 1.5f + Vector3.up * 1f, floor);
					gate = new CavernGate
					{
						Threshold = system.Origin + t,
						PieceYaw = heading,
						// As tall as the cavern lets it stand under its roof, within reason of its drawn size.
						Scale = Mathf.Clamp((roof - floor) / GateHeight, 0.8f, 1.3f),
					};
					return true;
				}
			}
			return false;
		}

		/// <summary>The floor under a point in the void (system frame), marched down; false where the point is rock or no floor is within 6 m.</summary>
		private static bool FloorUnder(Func<Vector3, float> voidAt, Vector3 p, out float floor)
		{
			floor = p.y;
			Vector3 q = p + Vector3.up * 1.5f;
			if (voidAt(q) >= 0f)
			{
				return false;
			}
			for (float y = q.y; y > p.y - 6f; y -= 0.05f)
			{
				if (voidAt(new Vector3(p.x, y, p.z)) >= 0f)
				{
					floor = y + 0.05f;
					return true;
				}
			}
			return false;
		}

		/// <summary>The cave's floor under a point (world), for what stands on a cavern's floor; false where there is none near it.</summary>
		public static bool CavernFloor(CaveSystem system, Vector3 world, out float y)
		{
			y = world.y;
			if (system == null || !FloorUnder(system.Void, world - system.Origin + Vector3.up * 0.5f, out float floor))
			{
				return false;
			}
			y = floor + system.Origin.y;
			return true;
		}

		/// <summary>The roof over a point in the void (system frame), marched up to 60 m over the floor.</summary>
		private static float RoofOver(Func<Vector3, float> voidAt, Vector3 p, float floor)
		{
			for (float y = p.y; y < floor + 60f; y += 0.1f)
			{
				if (voidAt(new Vector3(p.x, y, p.z)) >= 0f)
				{
					return y;
				}
			}
			return floor + 60f;
		}

		/// <summary>Whether the cavern is open before a gate whose threshold is <paramref name="t"/>: across its width, up its doorway, out over its apron.</summary>
		private static bool Clear(Func<Vector3, float> voidAt, Vector3 t, Vector3 outward, float floor)
		{
			var side = new Vector3(outward.z, 0f, -outward.x);
			for (float u = GateClearAhead; u <= GateClearAhead + 3f; u += 1.5f)
			{
				for (float v = -GateClearHalf; v <= GateClearHalf; v += 1.2f)
				{
					for (float h = 0.6f; h <= GateClearHeight; h += 1.2f)
					{
						Vector3 q = t + outward * u + side * v;
						q.y = floor + h;
						if (voidAt(q) >= 0f)
						{
							return false;
						}
					}
				}
			}
			return true;
		}

		/// <summary>The keep-clear disc of a site's cavern gate (system frame: centre on the floor, radius), for the cave's dressing; false for none.</summary>
		public static bool TryCavernGateClearing(CaveSystem system, int siteId, out Vector3 centre, out float radius) => TryCavernGateClearing(system, siteId, null, out centre, out radius);

		/// <summary><see cref="TryCavernGateClearing(CaveSystem, int, out Vector3, out float)"/> on a field of the caller's (null for the system's own).</summary>
		internal static bool TryCavernGateClearing(CaveSystem system, int siteId, Func<Vector3, float> voidAt, out Vector3 centre, out float radius)
		{
			centre = default;
			radius = 0f;
			if (!TryCavernGate(system, siteId, voidAt, out CavernGate gate))
			{
				return false;
			}
			Vector2 o = Dir(gate.Facing);
			// From the rock behind it to the apron's end, and its width.
			Vector3 local = gate.Threshold - system.Origin;
			centre = local + new Vector3(o.x, 0f, o.y) * gate.Scale;
			radius = StructurePieces.DungeonHalfWidth * gate.Scale + 2f;
			return true;
		}

		/// <summary>Whether a site of a kind and size holds a dungeon gate in its great cavern (PointOfInterestFeatureDefaults: large caves, ice caves, lava tubes).</summary>
		public static bool HasCavernGate(POIType kind, int sizeClass)
			=> sizeClass >= 2 && (kind == POIType.Cave || kind == POIType.IceCave || kind == POIType.LavaTube);
	}
}
#endif
