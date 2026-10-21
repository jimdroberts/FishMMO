#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The keep (2026-10-19: "Keeps are single story instead of multi floor interiors, you can't get to the top of a keep to look
	/// out?"): a great stone tower walked floor over floor from its door to its battlements.
	/// </summary>
	public static partial class StructurePieces
	{
		/// <summary>The keep's outer half width and depth, its walls' thickness and the room inside them (half), metres.</summary>
		private const float KeepHalf = 5.5f, KeepWall = 1.2f, KeepInner = KeepHalf - KeepWall;
		/// <summary>The floors: the undercroft's (the footing's top), the great hall's, the solar's and the roof walk's, metres.</summary>
		private const float KeepGround = 0.3f, KeepHall = 3.7f, KeepSolar = 7.6f, KeepDeck = 11f;
		/// <summary>A floor's thickness under its top, metres.</summary>
		private const float KeepSlab = 0.3f;
		/// <summary>A flight's breadth (from the outer wall), the wall along its open side, and how far in from the end wall its foot stands, metres.</summary>
		private const float KeepFlight = 1.4f, KeepSpine = 0.25f, KeepApproach = 1.2f;
		/// <summary>The parapet round the roof walk: its thickness and its top (the embrasures' sills); a well's parapet's height over its floor, metres.</summary>
		private const float KeepParapet = 0.6f, KeepParapetTop = KeepDeck + 1.1f, KeepRail = 1f;
		/// <summary>How high the saddleback coping on a well's parapet rises from its eaves to its ridge, metres.</summary>
		private const float KeepCoping = 0.22f;

		/// <summary>One flight between two floors, as <see cref="KeepStairs"/> laid it: its breadth across x, its foot and head along z, and the well over it.</summary>
		private struct KeepStair
		{
			public float X0, X1;
			public float Foot, Head;
			/// <summary>Where the well in the floor over the flight opens (z), from here to the head.</summary>
			public float WellFrom;

			public float WellMin => Mathf.Min(WellFrom, Head);
			public float WellMax => Mathf.Max(WellFrom, Head);
		}

		/// <summary>
		/// A great keep (2026-10-19), after the Norman keeps and the later tower houses: three floors over a battered footing, each a
		/// room a player walks into, and the roof a walk behind the battlements. The undercroft is the storeroom (no window but arrow
		/// slits); the first floor the great hall, its hearth on the back wall under the chimney and two-light windows; the second the
		/// lord's solar; then the roof walk, the lookout. v0 is entered as the Norman keeps were, at the hall: a forebuilding against
		/// the front wall encloses the stair up to the hall's doorway (its own door at the foot), the roof a flat lead deck inside the
		/// parapet with a turret at each corner and the lord's banner on one. v1, a tower house, is entered at the ground into the
		/// undercroft: a steep hipped roof stands inside a broad parapet walk, a round bartizan corbelled out at each corner.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The stairs.</b> A straight stone flight between every two floors, built against the outer wall in the wall's own
		/// stone, its risers at most <see cref="StepRise"/>, 1.4 m broad, each flight on another wall as the stairs of a tower house
		/// turn: the undercroft's up the left wall toward the back, the hall's up the right wall toward the back, the solar's up the
		/// left wall toward the front to the roof. A wall runs along each flight's open side from the floor it starts on (a stair
		/// between walls: nobody steps off it), and rises a parapet's height through the floor over it along the well, which is cut
		/// over every step a climber's head would meet the floor; a parapet closes the well's other end, its head is open onto the
		/// floor it serves. Every flight's foot stands 1.2 m in from the end wall, its head on a landing of that floor.
		/// </para>
		/// <para>
		/// <b>The walls</b> rise storey by storey (WallRun), each storey's windows and doorways cut through at every level: the last
		/// level is the collider. The roof walk runs over the walls' tops and the deck between them, behind a parapet 1.1 m high.
		/// </para>
		/// </remarks>
		private static void GreatKeep(StructureDraft d)
		{
			bool norman = d.Variant == 0;
			const float half = KeepHalf, inner = KeepInner;

			// The footing: a battered plinth a little wider than the keep, its top the undercroft's floor.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (KeepGround - FoundationDepth) * 0.5f, 0f), new Vector3(2f * half + 0.6f, KeepGround + FoundationDepth, 2f * half + 0.6f),
					d.Bevel(0.06f), StructureMaterial.Stone, 1));
			}

			// The stairs, floor over floor (see the remarks), and the floors cut over them.
			float front = -inner + KeepApproach;
			KeepStair toHall = KeepStairs(d, -1, 1, front, KeepGround, KeepHall, 2000);
			KeepStair toSolar = KeepStairs(d, 1, 1, front, KeepHall, KeepSolar, 2100);
			KeepStair toRoof = KeepStairs(d, -1, -1, -front, KeepSolar, KeepDeck, 2200);
			// On the roof the wall's top runs beside the hatch (below, the wall itself stands there): a parapet fills it from the
			// battlements to the hatch's edge, so nobody walks along the wall top off into the stair.
			using (d.Builds(WallRoles))
			{
				float x0 = -half + KeepParapet, x1 = -inner, coping = KeepDeck + KeepRail - KeepCoping;
				d.Add(StructureShapes.BoxBetween(new Vector3(x0, KeepDeck - 0.02f, toRoof.WellMin), new Vector3(x1, coping, toRoof.WellMax), StructureMaterial.Stone, 2300));
				d.Add(StructureShapes.Prism(new[] { new Vector2(x0, coping), new Vector2(x1, coping), new Vector2(x1, KeepDeck + KeepRail) }, toRoof.WellMin, toRoof.WellMax,
					StructureMaterial.Stone, 2301));
			}
			using (d.Builds(FloorRoles))
			{
				KeepFloor(d, KeepHall, toHall, StructureMaterial.Timber, 60);
				KeepFloor(d, KeepSolar, toSolar, StructureMaterial.Timber, 70);
				KeepFloor(d, KeepDeck, toRoof, StructureMaterial.Stone, 80);
			}

			// Every hole through the walls, by face (0 front −z, 1 back +z, 2 left −x, 3 right +x) and storey: the doorway, arrow
			// slits low, two-light windows in the hall and the solar, each clear of the flights and the chimney.
			var holes = new List<WallOpening>[4, 3];
			for (int f = 0; f < 4; f++)
			{
				for (int s = 0; s < 3; s++)
				{
					holes[f, s] = new List<WallOpening>();
				}
			}
			var lights = new List<(int face, float at, float bottom, float top)>();
			void Hole(int face, int storey, float at, float width, float bottom, float top) =>
				holes[face, storey].Add(new WallOpening { Along = at + (face < 2 ? half : inner), Width = width, Bottom = bottom, Top = top });
			void Slit(int face, int storey, float at, float bottom) => Hole(face, storey, at, 0.22f, bottom, bottom + 1f);
			void Light(int face, int storey, float at, float bottom, float height)
			{
				Hole(face, storey, at, KeepLightWidth, bottom, bottom + height);
				lights.Add((face, at, bottom, bottom + height));
			}
			const float hallSill = KeepHall + 1.2f, solarSill = KeepSolar + 1f;
			if (norman)
			{
				// The hall's doorway, at the head of the forebuilding's stair.
				Hole(0, 1, KeepHallDoorX, 1.3f, KeepHall, KeepHall + 2.4f);
			}
			else
			{
				Hole(0, 0, KeepGroundDoorX, 1.3f, KeepGround, KeepGround + 2.4f);
				Slit(0, 0, -2.6f, 1.4f);
				Slit(0, 0, 3f, 1.4f);
				Light(0, 1, -1.8f, hallSill, 1.6f);
				Light(0, 1, 1.4f, hallSill, 1.6f);
			}
			Slit(1, 0, -2.4f, 1.4f);
			Slit(1, 0, 2.4f, 1.4f);
			Slit(2, 0, 3f, 1.4f);
			Slit(3, 0, -2f, 1.4f);
			Slit(3, 0, 2f, 1.4f);
			Light(1, 1, -2.4f, hallSill, 1.6f);
			Light(1, 1, 1.8f, hallSill, 1.6f);
			Light(2, 1, -3.4f, hallSill, 1.6f);
			Light(2, 1, 3f, hallSill, 1.6f);
			// The hall's stair lit by a slit through the wall behind it.
			Slit(3, 1, 1.4f, KeepHall + 1.7f);
			Light(0, 2, -1.6f, solarSill, 1.4f);
			Light(0, 2, 1.8f, solarSill, 1.4f);
			Light(1, 2, -2.2f, solarSill, 1.4f);
			Light(1, 2, 2.1f, solarSill, 1.4f);
			Light(2, 2, -3f, solarSill, 1.4f);
			Light(3, 2, -2.6f, solarSill, 1.4f);

			float[] floors = { KeepGround, KeepHall, KeepSolar, KeepDeck };
			const float t = KeepWall * 0.5f;
			using (d.Builds(WallRoles))
			{
				for (int s = 0; s < 3; s++)
				{
					float y0 = floors[s], y1 = floors[s + 1];
					int key = 10 + s * 4;
					WallRun(d, new Vector3(-half, 0f, -half + t), new Vector3(half, 0f, -half + t), KeepWall, y0, y1, holes[0, s], StructureMaterial.Stone, key);
					WallRun(d, new Vector3(-half, 0f, half - t), new Vector3(half, 0f, half - t), KeepWall, y0, y1, holes[1, s], StructureMaterial.Stone, key + 1);
					WallRun(d, new Vector3(-half + t, 0f, -inner), new Vector3(-half + t, 0f, inner), KeepWall, y0, y1, holes[2, s], StructureMaterial.Stone, key + 2);
					WallRun(d, new Vector3(half - t, 0f, -inner), new Vector3(half - t, 0f, inner), KeepWall, y0, y1, holes[3, s], StructureMaterial.Stone, key + 3);
				}
				// The parapet round the roof walk, on the walls' outer edge.
				const float p = KeepParapet * 0.5f;
				WallRun(d, new Vector3(-half, 0f, -half + p), new Vector3(half, 0f, -half + p), KeepParapet, KeepDeck, KeepParapetTop, null, StructureMaterial.Stone, 30);
				WallRun(d, new Vector3(-half, 0f, half - p), new Vector3(half, 0f, half - p), KeepParapet, KeepDeck, KeepParapetTop, null, StructureMaterial.Stone, 31);
				WallRun(d, new Vector3(-half + p, 0f, -half + KeepParapet), new Vector3(-half + p, 0f, half - KeepParapet), KeepParapet, KeepDeck, KeepParapetTop, null, StructureMaterial.Stone, 32);
				WallRun(d, new Vector3(half - p, 0f, -half + KeepParapet), new Vector3(half - p, 0f, half - KeepParapet), KeepParapet, KeepDeck, KeepParapetTop, null, StructureMaterial.Stone, 33);
			}
			for (int i = 0; i < lights.Count; i++)
			{
				(int face, float at, float bottom, float top) = lights[i];
				Vector3 outward = face == 0 ? Vector3.back : face == 1 ? Vector3.forward : face == 2 ? Vector3.left : Vector3.right;
				Vector3 c = face < 2 ? new Vector3(at, (bottom + top) * 0.5f, outward.z * half) : new Vector3(outward.x * half, (bottom + top) * 0.5f, at);
				KeepWindow(d, c, outward, top - bottom, 400 + i * 8);
			}

			// The merlons on the parapet, between the corner turrets (or the bartizans).
			float run = norman ? half - 2.1f : half - 1.6f;
			for (int f = 0; f < 4; f++)
			{
				Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
				Crenellate(d, q * new Vector3(-run, KeepParapetTop, -half + KeepParapet * 0.5f), q * new Vector3(run, KeepParapetTop, -half + KeepParapet * 0.5f), KeepParapet, 0.8f, 0.9f, 0.6f,
					StructureMaterial.Stone, 100 + f * 10);
			}

			// The hall's hearth: a breast against the back wall under the chimney, the fire laid before it.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (KeepHall + KeepSolar - KeepSlab) * 0.5f, inner - 0.2f), new Vector3(1.8f, KeepSolar - KeepSlab - KeepHall, 0.45f), 0f,
				StructureMaterial.Fieldstone, 90));
			// The chimney up the back wall's outer face, clear of the parapet, smoking over the battlements.
			float stackTop = KeepParapetTop + 1.3f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, (stackTop - FoundationDepth) * 0.5f, half + 0.3f), new Vector3(1.6f, stackTop + FoundationDepth, 0.6f), 0f,
				StructureMaterial.Stone, 91));
			// Its cap at every level, so the keep's bounds (what a ruin of it breaks by) are the same at every level.
			d.Add(StructureShapes.Box(new Vector3(0f, stackTop + 0.07f, half + 0.3f), new Vector3(1.8f, 0.14f, 0.8f), StructureMaterial.Stone, 92, StructureRole.Detail));
			d.Emit(new Vector3(0f, stackTop + 0.4f, half + 0.3f), SceneEmitterKind.Smoke);

			// The rooms, each keeping the band along a wall its flight climbs or its well opens in clear from end to end, and a
			// body's width and more beside its wall: the stair's foot, its head's landing and the floor round the ends of its
			// walls are the ways between them (the planner sees the room, not the walls in it). The undercroft is the stores.
			const float aside = KeepFlight + KeepSpine + 0.75f;
			d.Room(new Vector3(0f, KeepGround, 0f), new Vector2(2f * inner, 2f * inner), KeepHall - KeepSlab - KeepGround, StructureRoomKind.Storehouse);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(-inner + aside, inner));
			d.Room(new Vector3(0f, KeepHall, 0f), new Vector2(2f * inner, 2f * inner), KeepSolar - KeepSlab - KeepHall, StructureRoomKind.Hall);
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(-inner + aside, inner));
			d.RoomClear(new Vector2(inner - aside, -inner), new Vector2(inner, inner));
			if (norman)
			{
				// The way in from the forebuilding.
				d.RoomClear(new Vector2(KeepHallDoorX - 1f, -inner), new Vector2(KeepHallDoorX + 1f, -inner + 1.6f));
			}
			// The hearth's foot: before the breast, its back to it.
			d.RoomHearth(new Vector2(0f, inner - 0.45f - 0.47f), 0f, KeepHall);
			d.Room(new Vector3(0f, KeepSolar, 0f), new Vector2(2f * inner, 2f * inner), KeepDeck - KeepSlab - KeepSolar, StructureRoomKind.Chamber);
			d.RoomClear(new Vector2(inner - aside, -inner), new Vector2(inner, inner));
			d.RoomClear(new Vector2(-inner, -inner), new Vector2(-inner + aside, inner));

			if (norman)
			{
				NormanKeep(d);
			}
			else
			{
				TowerHouse(d);
			}
		}

		/// <summary>Where v0's hall doorway (the forebuilding's head) and v1's ground door stand along the front, metres.</summary>
		private const float KeepHallDoorX = -2.8f, KeepGroundDoorX = 1f;
		/// <summary>A two-light window's breadth, metres.</summary>
		private const float KeepLightWidth = 0.9f;

		/// <summary>
		/// A straight flight against the outer wall on <paramref name="side"/> (−1 left, +1 right), climbing toward
		/// <paramref name="climb"/> along z from <paramref name="foot"/> on the floor at <paramref name="from"/> to the floor at
		/// <paramref name="to"/>: each step solid from the floor under it to its tread, the wall along its open side to the floor over
		/// it, and from there a parapet along the well and across its far end.
		/// </summary>
		private static KeepStair KeepStairs(StructureDraft d, int side, int climb, float foot, float from, float to, int key)
		{
			var s = new KeepStair { Foot = foot };
			float rise = to - from;
			int n = Mathf.CeilToInt(rise / StepRise - 1e-3f);
			float length = (n - 1) * StepTread;
			s.Head = foot + climb * length;
			s.X0 = side < 0 ? -KeepInner : KeepInner - KeepFlight;
			s.X1 = s.X0 + KeepFlight;
			// The first step whose tread a climber's head would meet the floor over: the well opens from there.
			int firstOpen = Mathf.Clamp(Mathf.CeilToInt((to - KeepSlab - StairHeadroom - from) / rise * n), 1, n);
			s.WellFrom = s.Head - climb * ((n - firstOpen + 1) * StepTread + 0.1f);

			// The steps, each into the wall a little and down into the floor it stands on.
			float wx0 = side < 0 ? s.X0 - 0.05f : s.X0, wx1 = side < 0 ? s.X1 : s.X1 + 0.05f;
			float bottom = from - 0.1f;
			using (d.Builds(StepRoles))
			{
				for (int i = 1; i < n; i++)
				{
					float top = from + rise * i / n, reach = (n - i) * StepTread;
					d.Add(StructureShapes.Box(new Vector3((wx0 + wx1) * 0.5f, (top + bottom) * 0.5f, s.Head - climb * reach * 0.5f), new Vector3(wx1 - wx0, top - bottom, reach),
						StructureMaterial.Stone, key + i));
				}
			}

			// The wall along the open side, from the floor to the floor over; the parapet on up along the well, and across its end,
			// each under a saddleback coping (its ridge shedding the rain, and no walk along its top).
			float sx0 = side < 0 ? s.X1 : s.X0 - KeepSpine, sx1 = sx0 + KeepSpine;
			float under = to - KeepSlab, rail = to + KeepRail, coping = rail - KeepCoping;
			float z0 = Mathf.Min(foot, s.Head), z1 = Mathf.Max(foot, s.Head);
			float e0 = climb > 0 ? s.WellFrom - KeepSpine : s.WellFrom, e1 = e0 + KeepSpine;
			float ex0 = side < 0 ? -KeepInner - 0.05f : sx0, ex1 = side < 0 ? sx1 : KeepInner + 0.05f;
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(sx0, bottom, z0), new Vector3(sx1, under + 0.02f, z1), StructureMaterial.Stone, key + 90));
				d.Add(StructureShapes.BoxBetween(new Vector3(sx0, under, s.WellMin), new Vector3(sx1, coping, s.WellMax), StructureMaterial.Stone, key + 91));
				d.Add(StructureShapes.Prism(new[] { new Vector2(sx0, coping), new Vector2(sx1, coping), new Vector2((sx0 + sx1) * 0.5f, rail) }, s.WellMin, s.WellMax,
					StructureMaterial.Stone, key + 93));
				d.Add(StructureShapes.BoxBetween(new Vector3(ex0, under, e0), new Vector3(ex1, coping, e1), StructureMaterial.Stone, key + 92));
				// Across x: a prism along z turned a quarter about y (its z becomes x, its x −z).
				d.Add(StructureShapes.Prism(new[] { new Vector2(-e1, coping), new Vector2(-e0, coping), new Vector2(-(e0 + e1) * 0.5f, rail) }, ex0, ex1, StructureMaterial.Stone, key + 94)
					.Transform(Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f))));
			}
			return s;
		}

		/// <summary>A floor over the keep's room, its top at <paramref name="top"/>, less the well over <paramref name="stair"/>.</summary>
		private static void KeepFloor(StructureDraft d, float top, KeepStair stair, StructureMaterial material, int key)
		{
			float lo = -KeepInner - 0.05f, hi = KeepInner + 0.05f, y = top - KeepSlab * 0.5f;
			float wx0 = Mathf.Max(lo, stair.X0 - 0.05f), wx1 = Mathf.Min(hi, stair.X1 + 0.05f);
			void Slab(float x0, float x1, float z0, float z1, int n)
			{
				if (x1 - x0 > 1e-3f && z1 - z0 > 1e-3f)
				{
					StructureSolid slab = StructureShapes.Box(new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(x1 - x0, KeepSlab, z1 - z0), material, key + n);
					slab.Grain = Vector3.right;
					d.Add(slab);
				}
			}
			Slab(lo, hi, lo, stair.WellMin, 0);
			Slab(lo, hi, stair.WellMax, hi, 1);
			Slab(lo, wx0, stair.WellMin, stair.WellMax, 2);
			Slab(wx1, hi, stair.WellMin, stair.WellMax, 3);
		}

		/// <summary>
		/// A keep's two-light window: a hole <see cref="KeepLightWidth"/> wide through the wall (WallRun cuts it), the glass in the
		/// wall's middle, and from the middle level a stone mullion between the lights, a sill and a hood mould over it.
		/// </summary>
		private static void KeepWindow(StructureDraft d, Vector3 at, Vector3 outward, float height, int key)
		{
			Quaternion turn = Quaternion.LookRotation(-outward, Vector3.up);
			d.Add(StructureShapes.Block(at - outward * (KeepWall * 0.5f), new Vector3(KeepLightWidth, height, 0.04f), turn, StructureMaterial.Glass, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			d.Add(StructureShapes.Block(at - outward * (KeepWall * 0.5f - 0.02f), new Vector3(0.14f, height, 0.22f), turn, StructureMaterial.Stone, key + 1, StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.08f + Vector3.down * (height * 0.5f + 0.06f), new Vector3(KeepLightWidth + 0.3f, 0.12f, 0.2f), turn, StructureMaterial.Stone, key + 2,
				StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.08f + Vector3.up * (height * 0.5f + 0.1f), new Vector3(KeepLightWidth + 0.4f, 0.16f, 0.18f), turn, StructureMaterial.Stone, key + 3,
				StructureRole.Detail));
			if (d.Fine)
			{
				// A transom across the lights two thirds up, as a hall's tall windows had.
				d.Add(StructureShapes.Block(at - outward * (KeepWall * 0.5f - 0.02f) + Vector3.up * (height * 0.17f), new Vector3(KeepLightWidth, 0.1f, 0.2f), turn, StructureMaterial.Stone,
					key + 4, StructureRole.Detail));
			}
		}

		/// <summary>
		/// v0: the forebuilding (a walled stair up the front to the hall's doorway, its door at the foot), pilaster buttresses, a
		/// square turret at each corner of the roof walk corbelled out over the walls, and a banner on the front right one.
		/// </summary>
		private static void NormanKeep(StructureDraft d)
		{
			const float half = KeepHalf;
			// The forebuilding: 0.5 m walls round a stair 1.6 m broad along the front, from its floor (a riser over the ground) to
			// the hall's; its door in its end wall, as at the keeps whose forebuildings were entered from the side.
			const float fw = 0.5f, fx0 = KeepHallDoorX - 0.8f - fw, fx1 = half, fz1 = -half, fz0 = -half - 1.6f - fw, top = 6.6f, floor = 0.2f;
			const float ix0 = fx0 + fw, ix1 = fx1 - fw, iz0 = fz0 + fw, doorZ = (iz0 + fz1) * 0.5f;
			const float landing = KeepHallDoorX + 0.9f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3((fx0 + fx1) * 0.5f + 0.1f, (floor - FoundationDepth) * 0.5f, (fz0 - 0.2f + fz1 + 0.3f) * 0.5f),
					new Vector3(fx1 - fx0 + 0.6f, floor + FoundationDepth, fz1 + 0.3f - fz0 + 0.2f), d.Bevel(0.05f), StructureMaterial.Stone, 3000));
			}
			using (d.Builds(WallRoles))
			{
				var holes = new List<WallOpening>
				{
					new WallOpening { Along = 0.6f - fx0, Width = 0.22f, Bottom = 1.6f, Top = 2.6f },
					new WallOpening { Along = KeepHallDoorX - fx0, Width = 0.22f, Bottom = KeepHall + 0.9f, Top = KeepHall + 1.9f },
				};
				var door = new List<WallOpening> { new WallOpening { Along = doorZ - iz0, Width = 1.2f, Bottom = floor, Top = floor + 2.4f } };
				WallRun(d, new Vector3(fx0, 0f, fz0 + fw * 0.5f), new Vector3(fx1, 0f, fz0 + fw * 0.5f), fw, floor, top, holes, StructureMaterial.Stone, 3010);
				WallRun(d, new Vector3(fx0 + fw * 0.5f, 0f, iz0), new Vector3(fx0 + fw * 0.5f, 0f, fz1 + 0.05f), fw, floor, top, null, StructureMaterial.Stone, 3011);
				WallRun(d, new Vector3(fx1 - fw * 0.5f, 0f, iz0), new Vector3(fx1 - fw * 0.5f, 0f, fz1 + 0.05f), fw, floor, top, door, StructureMaterial.Stone, 3012);
				// Its roof, a stone slab, the merlons along its front.
				d.Add(StructureShapes.BoxBetween(new Vector3(fx0, top - 0.3f, fz0), new Vector3(fx1, top, fz1 + 0.05f), StructureMaterial.Stone, 3013));
			}
			Crenellate(d, new Vector3(fx0 + 0.3f, top, fz0 + 0.3f), new Vector3(fx1 - 0.3f, top, fz0 + 0.3f), 0.6f, 0.8f, 0.9f, 0.6f, StructureMaterial.Stone, 3020);
			// The landing before the hall's doorway, and the flight down from it along the front to the door.
			using (d.Builds(StepRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(ix0 - 0.05f, floor - 0.05f, iz0 - 0.05f), new Vector3(landing, KeepHall, fz1 + 0.05f), StructureMaterial.Stone, 3030));
				float rise = KeepHall - floor;
				int n = Mathf.CeilToInt(rise / StepRise - 1e-3f);
				for (int i = 1; i < n; i++)
				{
					float y = floor + rise * i / n, reach = (n - i) * StepTread;
					d.Add(StructureShapes.BoxBetween(new Vector3(landing, floor - 0.05f, iz0 - 0.05f), new Vector3(landing + reach, y, fz1 + 0.05f), StructureMaterial.Stone, 3040 + i));
				}
			}
			// The door in the end wall, opening in (−x) toward the stair's foot; its sill one riser over the ground.
			HangDoor(d, new Vector3(ix1 - 0.05f, floor, doorZ), Vector3.forward, Vector3.left, 1.2f, 2.4f, StructureDoorKind.Studded);

			// Pilaster buttresses up the middle of the sides.
			using (d.Builds(WallRoles))
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * (half + 0.17f), (KeepDeck - FoundationDepth) * 0.5f, 0f), new Vector3(0.36f, KeepDeck + FoundationDepth, 1.6f), 0f,
						StructureMaterial.Stone, 3100 + (s > 0 ? 1 : 0)));
				}
			}

			// A square turret over each corner of the roof walk, corbelled out over the walls, merlons round its top.
			const float side = 2.4f, turretTop = KeepParapetTop + 1.7f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					int k = 3200 + (sx > 0 ? 20 : 0) + (sz > 0 ? 10 : 0);
					var c = new Vector3(sx * (half - 0.9f), 0f, sz * (half - 0.9f));
					using (d.Builds(WallRoles))
					{
						d.Add(StructureShapes.ChamferBox(c + Vector3.up * ((KeepDeck - 0.6f + turretTop) * 0.5f), new Vector3(side, turretTop - KeepDeck + 0.6f, side), d.Bevel(0.04f),
							StructureMaterial.Stone, k));
					}
					if (d.Mid)
					{
						// The corbels under its overhang on the two outer faces, and a coping round its top.
						for (int f = 0; f < 2; f++)
						{
							Vector3 o = f == 0 ? new Vector3(sx, 0f, 0f) : new Vector3(0f, 0f, sz);
							Vector3 along = f == 0 ? Vector3.forward : Vector3.right;
							for (int j = -1; j <= 1; j++)
							{
								Vector3 at = c + o * (side * 0.5f - 0.15f) + along * (j * 0.75f) + Vector3.up * (KeepDeck - 0.9f);
								d.Add(StructureShapes.Box(at, new Vector3(Mathf.Abs(o.x) * 0.3f + Mathf.Abs(along.x) * 0.3f, 0.6f, Mathf.Abs(o.z) * 0.3f + Mathf.Abs(along.z) * 0.3f),
									StructureMaterial.Stone, k + 1 + f * 3 + j + 1, StructureRole.Detail));
							}
						}
						d.Add(StructureShapes.Box(c + Vector3.up * (turretTop + 0.06f), new Vector3(side + 0.16f, 0.12f, side + 0.16f), StructureMaterial.Stone, k + 8, StructureRole.Detail));
					}
					if (d.Fine)
					{
						for (int m = 0; m < 4; m++)
						{
							float mx = (m & 1) == 0 ? -1f : 1f, mz = (m & 2) == 0 ? -1f : 1f;
							d.Add(StructureShapes.Box(c + new Vector3(mx * (side * 0.5f - 0.3f), turretTop + 0.12f + 0.35f, mz * (side * 0.5f - 0.3f)), new Vector3(0.6f, 0.7f, 0.6f),
								StructureMaterial.Stone, k + 9 + m, StructureRole.Detail));
						}
					}
				}
			}
			// The lord's banner on the front right turret, at every level (the keep's top: what a ruin of it measures its fall from).
			var pole = new Vector3(half - 0.9f, turretTop, -(half - 0.9f));
			d.Add(StructureShapes.Beam(pole, pole + Vector3.up * 3.2f, 0.09f, 0.09f, StructureMaterial.Timber, 3300, StructureRole.Detail));
			d.Add(StructureShapes.Box(pole + new Vector3(-0.62f, 2.7f, 0f), new Vector3(1.15f, 0.8f, 0.04f), StructureMaterial.Dyed, 3301, StructureRole.Detail));

			// The lookout: the roof walk over the front, between the turrets.
			d.Mark("lookout", new Vector3(0f, KeepDeck, -(half - KeepParapet - 0.6f)), 0f);
		}

		/// <summary>
		/// v1: the tower house's ground door (a step up to it), string courses at the floors, a round bartizan corbelled out at each
		/// corner of the parapet under a cone, and a steep hipped roof inside the parapet walk (the palette's roof, or none where it
		/// roofs flat: the walk is the roof).
		/// </summary>
		private static void TowerHouse(StructureDraft d)
		{
			const float half = KeepHalf;
			HangDoor(d, new Vector3(KeepGroundDoorX, KeepGround, -KeepInner + 0.05f), Vector3.right, Vector3.forward, 1.3f, 2.4f, StructureDoorKind.Iron);
			Steps(d, new Vector3(KeepGroundDoorX, 0f, -half - 0.3f), Vector3.back, 2f, KeepGround, StructureMaterial.Stone, 4000);
			if (d.Mid)
			{
				// A carved panel over the door, the lord's arms, and string courses round the walls at the floors.
				d.Add(StructureShapes.Box(new Vector3(KeepGroundDoorX, KeepGround + 3f, -half - 0.05f), new Vector3(0.9f, 0.7f, 0.12f), StructureMaterial.Stone, 4010, StructureRole.Detail));
				foreach (float y in new[] { KeepHall, KeepSolar })
				{
					for (int f = 0; f < 4; f++)
					{
						Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
						d.Add(StructureShapes.Block(q * new Vector3(0f, y - 0.08f, -half - 0.06f), new Vector3(2f * half + 0.24f, 0.16f, 0.12f), q, StructureMaterial.Stone,
							4020 + f + (y > KeepHall ? 4 : 0), StructureRole.Detail));
					}
				}
			}

			// The bartizans: a drum corbelled out from each corner under the parapet's top, a cone over it.
			int seg = d.Seg(12, 6);
			for (int i = 0; i < 4; i++)
			{
				float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
				Matrix4x4 at = Matrix4x4.Translate(new Vector3(sx * (half - 0.35f), 0f, sz * (half - 0.35f)));
				using (d.Builds(WallRoles))
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.3f, KeepDeck - 1.4f), new Vector2(1.05f, KeepDeck - 0.5f), new Vector2(1.05f, KeepParapetTop + 0.6f), new Vector2(0f, KeepParapetTop + 0.6f) },
						seg, StructureMaterial.Stone, 4100 + i).Transform(at));
				}
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(1.25f, KeepParapetTop + 0.5f), new Vector2(0f, KeepParapetTop + 3.1f) }, seg, StructureMaterial.Timber, 4110 + i,
						true, 0f, false, false, StructureRole.Roof).Transform(at));
				}
			}

			// The hipped roof inside the walk, on a low curb, its ridge along z; its pitch the palette's.
			const float rx = 2.5f, rz = 3f, curb = KeepDeck + 0.5f, eave = 0.2f;
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(-rx, KeepDeck - 0.05f, -rz), new Vector3(rx, curb, rz), StructureMaterial.Stone, 4200));
			}
			OwnRoofPlan plan = OwnRoof(d, StructureMaterial.Timber, KeepRoofRise, rx);
			if (!plan.Flat)
			{
				float a = rx + eave, b = rz + eave, r = plan.Rise * (a / rx), y0 = curb - plan.Rise * (eave / rx);
				using (d.Builds(RoofRoles))
				{
					// A gable prism across x, clipped back at both ends to the same slope: the hips.
					StructureSolid roof = StructureShapes.Prism(new[] { new Vector2(-a, y0), new Vector2(a, y0), new Vector2(0f, y0 + r) }, -b, b, StructureMaterial.Timber, 4210, StructureRole.Roof);
					float k = r / a;
					var n0 = new Vector3(0f, 1f, k).normalized;
					var n1 = new Vector3(0f, 1f, -k).normalized;
					roof.Clip(n0, Vector3.Dot(n0, new Vector3(0f, y0 + r, b - a)));
					roof.Clip(n1, Vector3.Dot(n1, new Vector3(0f, y0 + r, -(b - a))));
					roof.Grain = Vector3.forward;
					d.Add(roof);
				}
			}

			// The lookout: the walk over the front, before the roof.
			d.Mark("lookout", new Vector3(0f, KeepDeck, -(half - KeepParapet - 0.7f)), 0f);
		}

		/// <summary>v1's hipped roof's rise over its half span (2.5 m), as the kit builds it (StructureRoofs.Followers), metres.</summary>
		private const float KeepRoofRise = 2.9f;
	}
}
#endif
