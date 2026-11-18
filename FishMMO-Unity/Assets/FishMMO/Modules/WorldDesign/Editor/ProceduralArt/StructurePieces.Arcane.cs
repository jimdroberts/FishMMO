#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The town's other buildings (2026-10-19: "more structure types for towns, villages, and cities"): the wizard's tower and the
	/// library, and their furniture (the bookcase, the writing desk, the alchemist's bench, the scrying focus).
	/// </summary>
	public static partial class StructurePieces
	{
		private static void AddArcane(List<StructurePiece> list)
		{
			Piece(list, "WizardTower", StructureStyle.Stone, StructureSize.Building,
				"A mage's tower 8 m across, four floors high with a stair winding up inside its wall to a corbelled gallery round the top room, lancet windows, runes glowing over its door, a steep cone roof and a light on its spire; v0 round, a crystal on the spire; v1 octagonal, an armillary on the spire and a library wing built on against it (its own door, a doorway through from the tower).",
				WizardTower, "building", "wizard", "arcane", "town");
			Piece(list, "Library", StructureStyle.Stone, StructureSize.Building,
				"A town library, 10×16 m: v0 a tall stone reading hall, gable to the street, traceried lancets between buttresses down both sides, a great window at the far end, a studded double door under a carved arch and a louvred lantern on the ridge; v1 a timber-framed scriptorium jettied over a stone ground floor, a stair up to it.",
				Library, "building", "library", "town");
			Piece(list, "Bookcase", StructureStyle.Timber, StructureSize.Small,
				"A bookcase against the wall, 1.2 m wide: v0 tall, 2.1 m, its five shelves packed with books of every height and binding; v1 a scroll rack of sixteen pigeonholes, rolled scrolls in them.",
				Bookcase, "furniture", "bookcase");
			Piece(list, "Desk", StructureStyle.Timber, StructureSize.Small,
				"A desk against the wall: v0 a writing desk with a pigeonholed back, an open book, an inkwell and quill, a lit candle and a stool tucked under; v1 a scribe's sloped desk at a high stool's height, a book on its slope and a candle on a pricket.",
				Desk, "furniture", "desk");
			Piece(list, "AlchemyBench", StructureStyle.Timber, StructureSize.Small,
				"An alchemist's work: v0 a bench of glowing flasks, an alembic and a retort over a little brazier, jars on the shelf under it and on a shelf behind; v1 an iron cauldron of glowing brew on a tripod over a ring of coals, a low bench of jars behind it.",
				AlchemyBench, "furniture", "alchemy");
			Piece(list, "ArcaneFocus", StructureStyle.Stone, StructureSize.Small,
				"A scrying focus to stand in the middle of a room: v0 a glowing crystal orb in iron claws on a carved stone pedestal; v1 an iron orrery on a tripod stand, its sun alight, two rings and two planets on their arms.",
				ArcaneFocus, "furniture", "arcane");
		}

		// ── Wizard's tower ────────────────────────────────────────────

		/// <summary>The tower's ground floor over the ground (one step up onto its footing), each floor's rise, and a floor's thickness, metres.</summary>
		private const float WizardFloor = 0.4f, WizardStorey = 3.2f, WizardSlab = 0.24f;
		/// <summary>The tower's outer radius (at a corner, for the octagonal one) and its wall's thickness, metres.</summary>
		private const float WizardOuter = 4f, WizardWall = 0.7f;
		/// <summary>The top room's floor (and the gallery's) and the eaves the roof stands on, metres.</summary>
		private const float WizardTop = WizardFloor + 3f * WizardStorey, WizardEave = WizardTop + 3f;
		/// <summary>The treads of floor between a flight's arrival and the next flight's foot, on the walking line: room to step off into the room.</summary>
		private const int WizardLanding = 3;
		/// <summary>The v1 tower's centre: west of the origin, so the tower and its wing stand centred.</summary>
		private const float WizardWingTowerX = -3.85f;

		/// <summary>
		/// A wizard's tower (2026-10-19: "Wizard tower (with optional library?)"): a stone tower four floors high, every floor a
		/// room a player reaches, by a stair that winds up inside the wall, flight over flight, through a well in each floor; the top
		/// room opens onto a corbelled gallery under a steep cone roof, a light on the spire (a glowing crystal, or an armillary
		/// round a glowing orb). Runes glow over the door. v0 round; v1 octagonal, with a library wing built on against its east
		/// side, its own door to the street and a doorway through from the tower.
		/// </summary>
		/// <remarks>
		/// <para>
		/// One wall rises from the footing to the eaves (RingWallWithOpenings), every window, door and arch cut through it at its
		/// own bearing, so no two holes share a bearing at different heights (the wall's blocks are cut once each way).
		/// </para>
		/// <para>
		/// The stair: each flight is 16 risers of 0.2 m round the wall, a winder's tread 0.3 m on its walking line, from the stair's
		/// inner edge to the wall (1.35 m and more); a flight arrives on the next floor, a landing of WizardLanding treads, and the next
		/// flight goes on up round. The floor over a flight is cut away over every step a climber's head would meet it, and a stone parapet runs
		/// round the well; the arrival end is open to step off. The rooms keep the flights, the wells and the landings clear of
		/// furniture (RoomClear): the ground floor and the top room are the wizard's studies, the first floor his chamber, the
		/// second his library.
		/// </para>
		/// </remarks>
		private static void WizardTower(StructureDraft d)
		{
			bool wing = d.Variant == 1;
			bool round = !wing;
			var c = new Vector3(wing ? WizardWingTowerX : 0f, 0f, 0f);
			int sides = round ? d.Seg(16, 12) : 8;
			const float outer = WizardOuter, inner = WizardOuter - WizardWall;
			float doorA = 1.5f * Mathf.PI;
			float facet = 2f * Mathf.PI / sides;
			float lathePhase = doorA - Mathf.PI / sides;
			// The faces' distance in from the corners at the coarsest level: what the rooms and the stair are measured to.
			float k = Mathf.Cos(Mathf.PI / (round ? 12 : 8));
			float roomR = inner * k;
			float rs0 = round ? 1.9f : 1.75f;
			float dA = StepTread / ((rs0 + roomR) * 0.5f);
			int risers = Mathf.CeilToInt(WizardStorey / StepRise - 1e-3f);
			float rise = WizardStorey / risers;
			// A flight, its arrival tread and a landing, then the next flight on round. The landing is WizardLanding treads long
			// (2026-10-19, round 3: "stairs are missing platforms at the top"): with only the arrival tread between the well's
			// parapet and the next flight's first riser, the way off the stair into the room was 0.45 m wide, too narrow for a
			// body, and every floor over the ground floor was reached only on its landing.
			float flight = (risers + 1 + WizardLanding) * dA;
			float a0 = (round ? 300f : 90f) * Mathf.Deg2Rad;
			float Floor(int level) => WizardFloor + level * WizardStorey;
			Vector3 Out(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			// The outer face's distance at a bearing: the circle's, or the octagon's face's.
			float FaceAngle(float a) => round ? a : doorA + Mathf.Round((a - doorA) / facet) * facet;
			float Apothem(float r) => round ? r : r * Mathf.Cos(Mathf.PI / 8f);

			// The footing, its top the ground floor: a drum a little wider than the tower.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -FoundationDepth), new Vector2(outer + 0.25f, -FoundationDepth), new Vector2(outer + 0.25f, WizardFloor),
					new Vector2(0f, WizardFloor) }, sides, StructureMaterial.Fieldstone, 1, round, lathePhase).Transform(Matrix4x4.Translate(c)));
			}

			// Every hole through the wall, by bearing (degrees), width and height: the door, the windows of each floor, the arch onto
			// the gallery, and on v1 the doorway through to the wing. Each floor's windows are clear of its own flight.
			const float lancet = 0.45f, lancetSill = 1.1f, lancetHeight = 1.5f, arch = 1.1f;
			var holes = new List<(float angle, float width, float bottom, float top)>();
			var lights = new List<(float deg, int level)>();
			void Hole(float deg, float width, float bottom, float top) => holes.Add((deg * Mathf.Deg2Rad, width, bottom, top));
			Hole(270f, 1.2f, WizardFloor - 0.01f, WizardFloor + 2.4f);
			float archDeg;
			if (round)
			{
				lights.AddRange(new[] { (130f, 0), (210f, 0), (240f, 1), (340f, 1), (20f, 2), (100f, 2), (0f, 3), (150f, 3), (300f, 3) });
				archDeg = 60f;
			}
			else
			{
				// The octagon's faces are centred every 45° from the door's; a window stands 15° off a face's middle so two on
				// one face (at other heights) never share a bearing. East (the wing's side) has none under the gallery.
				Hole(0f, 1.4f, WizardFloor - 0.01f, WizardFloor + 2.5f);
				lights.AddRange(new[] { (210f, 0), (330f, 0), (75f, 1), (150f, 1), (165f, 2), (240f, 2), (255f, 3), (120f, 3), (300f, 3), (30f, 3) });
				archDeg = 180f;
			}
			foreach ((float deg, int level) in lights)
			{
				Hole(deg, lancet, Floor(level) + lancetSill, Floor(level) + lancetSill + lancetHeight);
			}
			Hole(archDeg, arch, WizardTop - 0.01f, WizardTop + 2.3f);
			int first = d.Solids.Count;
			using (d.Builds(WallRoles))
			{
				RingWallWithOpenings(d, c, inner, outer, WizardFloor, WizardEave, sides, doorA, holes, StructureMaterial.Stone, 10);
			}
			if (!round)
			{
				// Flat faces: the octagon's blocks shade by their planes, not round the axis.
				for (int i = first; i < d.Solids.Count; i++)
				{
					foreach (StructureFace f in d.Solids[i].Faces)
					{
						f.Normals = null;
					}
				}
			}

			// The windows: a pane in each hole, and from the middle level a sill and a pointed hood over it.
			for (int i = 0; i < lights.Count; i++)
			{
				float a = lights[i].deg * Mathf.Deg2Rad;
				TowerLancet(d, c, a, FaceAngle(a), Apothem(inner), Apothem(outer), Floor(lights[i].level) + lancetSill, lancetHeight, lancet, 400 + i * 4, !round);
			}

			// The floors over each storey, each cut over the flight below it, with its parapet round the well; the flights.
			for (int level = 0; level < 3; level++)
			{
				float fa = a0 + level * flight;
				float y = Floor(level);
				// The flight: winders from this floor round the wall, each solid from the floor to its tread.
				using (d.Builds(StepRoles))
				{
					for (int i = 1; i < risers; i++)
					{
						d.Add(RingBlock(c, rs0, inner + 0.1f, y - 0.05f, y + rise * i, fa + (i - 1) * dA, fa + i * dA, StructureMaterial.Stone, 600 + level * 20 + i, StructureRole.Structure));
					}
				}
				// An iron handrail on stanchions along the flight's open inner edge, from its foot to where the floor over closes in
				// over a climber's head (2026-10-19, round 3: "you just fall off"): a climber could step off its inner side 1.2 m
				// over the floor. At every level, for the collider.
				{
					const int railTo = 8;
					float railR = rs0 - 0.05f;
					Vector3 RailAt(float i, float over) => c + Out(fa + (i - 0.5f) * dA) * railR + Vector3.up * (y + rise * i + over);
					for (int j = 0; j < 2; j++)
					{
						float i0 = 1f + (railTo - 1f) * j / 2f, i1 = 1f + (railTo - 1f) * (j + 1) / 2f;
						d.Add(StructureShapes.Beam(RailAt(i0, 0.9f), RailAt(i1, 0.9f), 0.05f, 0.05f, StructureMaterial.Iron, 880 + level * 4 + j, StructureRole.Detail));
					}
					d.Add(StructureShapes.Beam(RailAt(1f, -rise), RailAt(1f, 0.93f), 0.06f, 0.06f, StructureMaterial.Iron, 883 + level * 4, StructureRole.Detail));
				}
				// The well over it: from the step a head would meet the floor over to the flight's top.
				float well0 = fa + 3.5f * dA, well1 = fa + (risers - 1) * dA;
				float top = Floor(level + 1);
				using (d.Builds(FloorRoles))
				{
					TowerFloor(d, c, top, well0, well1, rs0, inner + 0.15f, sides, doorA, 700 + level * 40);
				}
				// The parapet round the well: along its inner edge and across its foot end; the arrival end open.
				using (d.Builds(WallRoles))
				{
					int chunks = Mathf.Max(1, Mathf.CeilToInt((well1 - well0) / (45f * Mathf.Deg2Rad)));
					for (int j = 0; j < chunks; j++)
					{
						float lo = Mathf.Lerp(well0, well1, j / (float)chunks), hi = Mathf.Lerp(well0, well1, (j + 1) / (float)chunks);
						d.Add(RingBlock(c, rs0 - 0.14f, rs0, top - 0.02f, top + 0.95f, lo, hi, StructureMaterial.Stone, 820 + level * 10 + j, StructureRole.Structure));
					}
					float across = well0 - 0.08f / rs0;
					float r0 = rs0 - 0.14f, r1 = inner + 0.1f;
					d.Add(StructureShapes.Block(c + Out(across) * ((r0 + r1) * 0.5f) + Vector3.up * (top + 0.465f), new Vector3(0.14f, 0.97f, r1 - r0),
						Quaternion.LookRotation(Out(across), Vector3.up), StructureMaterial.Stone, 860 + level));
				}
			}

			// The rooms, each keeping its flight, its well and its landing clear.
			StructureRoomKind[] kinds = { StructureRoomKind.Study, StructureRoomKind.Chamber, StructureRoomKind.Library, StructureRoomKind.Study };
			for (int level = 0; level <= 3; level++)
			{
				d.RoundRoom(c + Vector3.up * Floor(level), roomR, WizardStorey - WizardSlab, kinds[level]);
				float from = level == 0 ? a0 - 0.5f * dA : a0 + (level - 1) * flight + 3f * dA;
				float to = level < 3 ? a0 + level * flight + risers * dA : a0 + (level - 1) * flight + (risers + 1) * dA;
				// To a body's width in from the well's parapet and the flight's side, so nothing stands to cut off a strip along them.
				ClearSector(d, c, from, to, rs0 - 0.95f, inner);
			}
			// The top room's way out onto the gallery.
			float archA = archDeg * Mathf.Deg2Rad;
			ClearSector(d, c, archA - 0.75f / inner, archA + 0.75f / inner, inner - 1.4f, inner);
			if (wing)
			{
				// The ground floor's way through to the wing (the ground floor is the first room marked).
				StructureRoom ground = d.Rooms[0];
				int n = ground.Clear != null ? ground.Clear.Length : 0;
				var clear = new Vector4[n + 1];
				if (n > 0)
				{
					System.Array.Copy(ground.Clear, clear, n);
				}
				clear[n] = new Vector4(c.x + roomR - 1.5f, -0.9f, c.x + roomR, 0.9f);
				ground.Clear = clear;
				d.Rooms[0] = ground;
			}

			// The door: a studded (v1 iron) door hung on the inside face, a step up onto the footing, a pointed arch over it with
			// its runes glowing.
			float doorInner = round ? Mathf.Sqrt(inner * inner - 0.36f) : Apothem(inner);
			HangDoor(d, c + new Vector3(0f, WizardFloor, -doorInner + 0.05f), Vector3.right, Vector3.forward, 1.2f, 2.4f, round ? StructureDoorKind.Studded : StructureDoorKind.Iron);
			float face = Apothem(outer);
			Steps(d, c + new Vector3(0f, 0f, -Apothem(outer + 0.25f) + 0.12f), Vector3.back, 1.8f, WizardFloor, StructureMaterial.Stone, 30);
			DoorArch(d, c.x, -face, WizardFloor, 1.2f, 2.4f, 0.35f, 40, 1);

			// The gallery round the top room: three corbelled courses out from the wall, its walk a metre wide at the top room's
			// floor, a parapet round it; the arch out onto it.
			float g = outer + 1.3f;
			using (d.Builds(WallRoles))
			{
				var gallery = new[]
				{
					new Vector2(outer - 0.05f, WizardTop - 1f), new Vector2(outer + 0.35f, WizardTop - 1f), new Vector2(outer + 0.35f, WizardTop - 0.7f),
					new Vector2(outer + 0.8f, WizardTop - 0.7f), new Vector2(outer + 0.8f, WizardTop - 0.4f), new Vector2(g, WizardTop - 0.4f),
					new Vector2(g, WizardTop + 1f), new Vector2(g - 0.3f, WizardTop + 1f), new Vector2(g - 0.3f, WizardTop), new Vector2(outer - 0.05f, WizardTop),
				};
				d.Add(StructureShapes.Lathe(gallery, sides, StructureMaterial.Stone, 50, round, lathePhase, true).Transform(Matrix4x4.Translate(c)));
			}
			if (d.Mid)
			{
				// The arch's hood: a pointed drip mould over it.
				Vector3 n = Out(FaceAngle(archA));
				Vector3 t = new Vector3(-n.z, 0f, n.x);
				Vector3 at = c + n * (face + 0.05f) + t * (face * Mathf.Tan(archA - FaceAngle(archA)));
				float y = WizardTop + 2.3f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(at + t * (s * (arch * 0.5f + 0.14f)) + Vector3.up * (y - 0.35f), at + Vector3.up * (y + 0.45f), 0.14f, 0.14f,
						StructureMaterial.Stone, 56 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The roof: a steep cone, bell-cast at its eaves over the gallery (a spire's slope, not a roof the palette re-pitches:
			// its shingles or slates are the palette's), its bottom the top room's ceiling.
			float apex = WizardEave + 7.75f;
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(outer + 0.95f, WizardEave - 0.15f), new Vector2(outer + 0.15f, WizardEave + 0.55f), new Vector2(0f, apex) },
					sides, StructureMaterial.Timber, 60, round, lathePhase, false, false, StructureRole.Roof).Transform(Matrix4x4.Translate(c)));
			}

			// The spire's light: v0 a crystal clasped in iron claws, v1 an armillary of two rings round a glowing orb.
			Vector3 tip = c + Vector3.up * apex;
			d.Add(StructureShapes.Beam(tip + Vector3.down * 0.5f, tip + Vector3.up * 2.3f, 0.07f, 0.07f, StructureMaterial.Iron, 70));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, -0.3f), new Vector2(0.1f, 0.2f), new Vector2(0f, 0.3f) }, d.Seg(8, 4), StructureMaterial.Iron, 71)
				.Transform(Matrix4x4.Translate(tip)));
			Vector3 glow = tip + Vector3.up * 1.35f;
			if (round)
			{
				d.Add(StructureShapes.Sphere(glow, new Vector3(0.2f, 0.45f, 0.2f), 6, 2, StructureMaterial.Runelight, 72, StructureRole.Detail));
				if (d.Mid)
				{
					for (int i = 0; i < 4; i++)
					{
						float a = (i + 0.5f) * Mathf.PI * 0.5f;
						Vector3 o = Out(a);
						d.Add(StructureShapes.Beam(tip + Vector3.up * 0.25f + o * 0.06f, glow + Vector3.down * 0.15f + o * 0.2f, 0.035f, 0.035f, StructureMaterial.Iron, 73 + i, StructureRole.Detail));
					}
				}
			}
			else
			{
				d.Add(StructureShapes.Sphere(glow, new Vector3(0.17f, 0.17f, 0.17f), d.Seg(8, 5), d.Fine ? 4 : 3, StructureMaterial.Runelight, 72, StructureRole.Detail));
				if (d.Mid)
				{
					int ringSeg = d.Seg(10, 6);
					d.Add(Ring(glow, 0.5f, 0.05f, Quaternion.Euler(90f, 0f, 0f), ringSeg, StructureMaterial.Iron, 73, StructureRole.Detail));
					d.Add(Ring(glow, 0.46f, 0.05f, Quaternion.Euler(0f, 30f, 66.5f), ringSeg, StructureMaterial.Iron, 74, StructureRole.Detail));
				}
			}
			d.Emit(glow, SceneEmitterKind.Glow);

			if (round && d.Mid)
			{
				// String courses round the tower at its upper floors, chamfered under to throw the rain off.
				using (d.Builds(WallRoles))
				{
					for (int level = 1; level <= 2; level++)
					{
						float y = Floor(level);
						d.Add(StructureShapes.Lathe(new[] { new Vector2(outer - 0.05f, y - 0.2f), new Vector2(outer + 0.1f, y), new Vector2(outer - 0.05f, y) },
							sides, StructureMaterial.Stone, 90 + level, true, lathePhase, true, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c)));
					}
				}
				// A lantern on an iron arm beside the door.
				float x = c.x + 1.15f;
				float z = -Mathf.Sqrt(outer * outer - 1.15f * 1.15f);
				d.Add(StructureShapes.Box(new Vector3(x, 2.85f, z - 0.01f), new Vector3(0.1f, 0.3f, 0.06f), StructureMaterial.Iron, 80, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(x, 2.95f, z), new Vector3(x, 2.95f, z - 0.5f), 0.04f, 0.04f, StructureMaterial.Iron, 81, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(x, 2.95f, z - 0.45f), new Vector3(x, 2.6f, z - 0.45f), 0.015f, 0.015f, StructureMaterial.Iron, 82, StructureRole.Detail));
				Lantern(d, new Vector3(x, 2.24f, z - 0.45f), false, 83);
			}

			if (wing)
			{
				// The library wing: a stone hall built on against the tower's east face, its end wall in the tower's own (the doorway
				// through both), its roof starting at the tower's face, so nothing of it stands in the tower's rooms.
				float x0 = c.x + Apothem(inner) + 0.05f;
				BuildHall(d, new Hall
				{
					AlongX = true, U0 = x0, U1 = x0 + 9.5f, Width = 6f, Wall = 0.5f, WallHeight = 4f, Floor = WizardFloor, Rise = 3.6f,
					Lancets = 3, SideDoor = true, SideDoorU = x0 + 7.4f, DoorWidth = 1.3f, DoorHeight = 2.5f, Through = true, ThroughWidth = 1.4f,
					GreatWindow = true, Against = true, Key = 1000,
				});
			}
		}

		/// <summary>
		/// A tower's floor at <paramref name="y"/> (its top): wedges round the axis to <paramref name="rOut"/> (into the wall), cut back to
		/// <paramref name="rWell"/> between <paramref name="well0"/> and <paramref name="well1"/> (radians), where the stair comes up
		/// through it. The wedges follow the wall's blocks, so an octagon's floor meets its faces.
		/// </summary>
		private static void TowerFloor(StructureDraft d, Vector3 c, float y, float well0, float well1, float rWell, float rOut, int sides, float phase, int key)
		{
			float step = 2f * Mathf.PI / sides;
			float start = phase - step * 0.5f;
			int n = 0;
			for (int b = 0; b < sides; b++)
			{
				float a0 = start + b * step, a1 = a0 + step;
				var cuts = new List<(float lo, float hi)>();
				foreach (float shift in new[] { 0f, 2f * Mathf.PI, -2f * Mathf.PI, 4f * Mathf.PI, -4f * Mathf.PI })
				{
					float lo = Mathf.Max(a0, well0 + shift), hi = Mathf.Min(a1, well1 + shift);
					if (hi > lo + 1e-4f)
					{
						cuts.Add((lo, hi));
					}
				}
				cuts.Sort((p, q) => p.lo.CompareTo(q.lo));
				float at = a0;
				foreach ((float lo, float hi) in cuts)
				{
					if (lo - at > 1e-4f)
					{
						d.Add(RingBlock(c, 0f, rOut, y - WizardSlab, y, at, lo, StructureMaterial.Timber, key + n++, StructureRole.Structure));
					}
					d.Add(RingBlock(c, 0f, rWell, y - WizardSlab, y, lo, hi, StructureMaterial.Timber, key + n++, StructureRole.Structure));
					at = Mathf.Max(at, hi);
				}
				if (a1 - at > 1e-4f)
				{
					d.Add(RingBlock(c, 0f, rOut, y - WizardSlab, y, at, a1, StructureMaterial.Timber, key + n++, StructureRole.Structure));
				}
			}
		}

		/// <summary>Keeps a ring's sector of the last room marked clear of furniture: the rectangles round it, a slice of 20° at most each.</summary>
		private static void ClearSector(StructureDraft d, Vector3 c, float a0, float a1, float r0, float r1)
		{
			int n = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / (20f * Mathf.Deg2Rad)));
			for (int i = 0; i < n; i++)
			{
				float lo = Mathf.Lerp(a0, a1, i / (float)n), hi = Mathf.Lerp(a0, a1, (i + 1) / (float)n);
				var min = new Vector2(float.MaxValue, float.MaxValue);
				var max = new Vector2(float.MinValue, float.MinValue);
				foreach (float a in new[] { lo, (lo + hi) * 0.5f, hi })
				{
					foreach (float r in new[] { r0, r1 })
					{
						var p = new Vector2(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r);
						min = Vector2.Min(min, p);
						max = Vector2.Max(max, p);
					}
				}
				d.RoomClear(min, max);
			}
		}

		/// <summary>
		/// A tower's lancet at bearing <paramref name="a"/> (in a face square to <paramref name="face"/>, whose inner and outer faces stand
		/// <paramref name="inner"/> and <paramref name="outer"/> from the axis): the pane in the hole's middle and, from the middle level, a
		/// sill and a pointed hood mould (on the octagon, the sill only, for its budget).
		/// </summary>
		private static void TowerLancet(StructureDraft d, Vector3 c, float a, float face, float inner, float outer, float sill, float height, float width, int key, bool plain)
		{
			var n = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face));
			var t = new Vector3(-n.z, 0f, n.x);
			float off = Mathf.Tan(a - face);
			Quaternion turn = Quaternion.LookRotation(n, Vector3.up);
			float mid = (inner + outer) * 0.5f;
			d.Add(StructureShapes.Block(c + n * mid + t * (mid * off) + Vector3.up * (sill + height * 0.5f), new Vector3(width, height, 0.04f), turn, StructureMaterial.Glass, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			Vector3 at = c + n * outer + t * (outer * off);
			d.Add(StructureShapes.Block(at + n * 0.06f + Vector3.up * (sill - 0.05f), new Vector3(width + 0.25f, 0.1f, 0.16f), turn, StructureMaterial.Stone, key + 1, StructureRole.Detail));
			if (plain)
			{
				return;
			}
			float top = sill + height;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(at + n * 0.04f + t * (s * (width * 0.5f + 0.1f)) + Vector3.up * (top - 0.2f), at + n * 0.04f + Vector3.up * (top + 0.32f), 0.1f, 0.1f,
					StructureMaterial.Stone, key + 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
		}

		/// <summary>
		/// A doorway's carved surround on a face looking down −z (<paramref name="zFace"/>): jambs, and over the doorway's head a
		/// pointed tympanum <paramref name="deep"/> into the wall, carved (<paramref name="carving"/>: 0 plain, 1 runes that glow,
		/// 2 an open book); the jambs and the hood from the middle level, the carving at the finest.
		/// </summary>
		private static void DoorArch(StructureDraft d, float x, float zFace, float floor, float width, float height, float deep, int key, int carving)
		{
			float head = floor + height, half = width * 0.5f + 0.18f;
			float z0 = zFace - 0.1f;
			// The tympanum: a pointed stone over the door's head, at every level (it is the arch's silhouette).
			d.Add(StructureShapes.Prism(new[] { new Vector2(x - half, head), new Vector2(x + half, head), new Vector2(x, head + half * 1.15f) },
				z0, zFace + deep, StructureMaterial.Stone, key, StructureRole.Detail));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(x + s * (width * 0.5f + 0.1f), floor + height * 0.5f, z0 + 0.06f), new Vector3(0.2f, height, 0.14f),
						StructureMaterial.Stone, key + 1 + (s > 0 ? 1 : 0), StructureRole.Detail));
					// The hood: a drip mould round the tympanum's slopes.
					d.Add(StructureShapes.Beam(new Vector3(x + s * (half + 0.12f), head - 0.05f, z0 - 0.04f), new Vector3(x, head + half * 1.15f + 0.16f, z0 - 0.04f), 0.12f, 0.12f,
						StructureMaterial.Stone, key + 3 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			float zc = z0 - 0.004f;
			if (carving == 1)
			{
				if (d.Fine)
				{
					for (int r = 0; r < 3; r++)
					{
						Rune(d, new Vector2(x + (r - 1) * 0.3f, head + 0.32f - Mathf.Abs(r - 1) * 0.06f), 0.1f, r * 2 + 1, zc, key + 10 + r * 4);
					}
				}
				d.Emit(new Vector3(x, head + 0.35f, z0 - 0.4f), SceneEmitterKind.Glow);
			}
			else if (carving == 2 && d.Fine)
			{
				// An open book carved in the tympanum: two leaves leaning out from the spine.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(new Vector3(x + s * 0.2f, head + 0.38f, zc - 0.02f), new Vector3(0.38f, 0.28f, 0.04f), Quaternion.Euler(0f, 0f, s * 10f),
						StructureMaterial.Stone, key + 10 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
		}

		// ── Halls (the library and the tower's wing) ──────────────────

		/// <summary>
		/// A tall stone hall under a gable roof (the library and the wizard's tower's wing): along <c>u</c> (its ridge: world x when
		/// <see cref="AlongX"/>, else z) from <see cref="U0"/> to <see cref="U1"/>, <see cref="Width"/> across about the origin's line.
		/// </summary>
		private struct Hall
		{
			public bool AlongX;
			public float U0, U1, Width, Wall, WallHeight, Floor, Rise;
			/// <summary>Lancets down each long side.</summary>
			public int Lancets;
			/// <summary>The door: in the front long side (−z, AlongX) at <see cref="SideDoorU"/>, or in the start end (U0, facing −z).</summary>
			public bool SideDoor, EndDoor;
			public float SideDoorU, DoorWidth, DoorHeight;
			/// <summary>A doorway through the start end into what the hall is built against (a tower).</summary>
			public bool Through;
			public float ThroughWidth;
			/// <summary>A great window in the far end.</summary>
			public bool GreatWindow;
			/// <summary>A louvred lantern on the ridge.</summary>
			public bool Fleche;
			/// <summary>Built against a tower at its start end: no buttresses there, no bargeboards, its roof flush with the end wall.</summary>
			public bool Against;
			public int Key;
		}

		private static void BuildHall(StructureDraft d, Hall h)
		{
			int key = h.Key;
			bool x = h.AlongX;
			float t = h.Wall, half = h.Width * 0.5f, length = h.U1 - h.U0, uMid = (h.U0 + h.U1) * 0.5f;
			float floor = h.Floor, eave = floor + h.WallHeight;
			Vector3 P(float u, float y, float v) => x ? new Vector3(u, y, v) : new Vector3(v, y, u);
			Vector3 S(float su, float sy, float sv) => x ? new Vector3(su, sy, sv) : new Vector3(sv, sy, su);
			Vector3 Dir(float du, float dv) => x ? new Vector3(du, 0f, dv) : new Vector3(dv, 0f, du);

			// The plinth, its top the floor (a hair under it against a tower, whose own footing is the floor at the doorway).
			using (d.Builds(PlinthRoles))
			{
				float p0 = h.Against ? h.U0 + t + 0.02f : h.U0 - 0.15f;
				float top = h.Against ? floor - 0.002f : floor;
				d.Add(StructureShapes.ChamferBox(P((p0 + h.U1 + 0.15f) * 0.5f, (top - FoundationDepth) * 0.5f, 0f), S(h.U1 + 0.15f - p0, top + FoundationDepth, h.Width + 0.3f),
					d.Bevel(0.04f), StructureMaterial.Fieldstone, key));
			}

			// The lancets down both long sides, a bay each; the side door takes a bay's place.
			float bay = (length - 2f * t) / h.Lancets;
			const float lancetWidth = 0.75f;
			float sill = floor + 1.4f, head = eave - 0.9f;
			var lancets = new List<(float u, int side)>();
			for (int side = -1; side <= 1; side += 2)
			{
				for (int i = 0; i < h.Lancets; i++)
				{
					float u = h.U0 + t + (i + 0.5f) * bay;
					if (side < 0 && h.SideDoor && Mathf.Abs(u - h.SideDoorU) < bay * 0.5f)
					{
						continue;
					}
					lancets.Add((u, side));
				}
			}
			float great = x ? 1.4f : 1.8f;
			using (d.Builds(WallRoles))
			{
				for (int side = -1; side <= 1; side += 2)
				{
					var holes = new List<WallOpening>();
					foreach ((float u, int s) in lancets)
					{
						if (s == side)
						{
							holes.Add(new WallOpening { Along = u - h.U0, Width = lancetWidth, Bottom = sill, Top = head });
						}
					}
					if (side < 0 && h.SideDoor)
					{
						holes.Add(new WallOpening { Along = h.SideDoorU - h.U0, Width = h.DoorWidth, Bottom = floor - 0.01f, Top = floor + h.DoorHeight });
					}
					float v = side * (half - t * 0.5f);
					WallRun(d, P(h.U0, 0f, v), P(h.U1, 0f, v), t, -FoundationDepth, eave, holes, StructureMaterial.Stone, key + 1 + (side > 0 ? 1 : 0));
				}
				// The start end: the door, or the doorway through into the tower; the far end: the great window.
				var start = new List<WallOpening>();
				if (h.EndDoor)
				{
					start.Add(new WallOpening { Along = half - t, Width = h.DoorWidth, Bottom = floor - 0.01f, Top = floor + h.DoorHeight });
				}
				if (h.Through)
				{
					start.Add(new WallOpening { Along = half - t, Width = h.ThroughWidth, Bottom = floor - 0.01f, Top = floor + 2.5f });
				}
				var end = new List<WallOpening>();
				if (h.GreatWindow)
				{
					end.Add(new WallOpening { Along = half - t, Width = great, Bottom = floor + 1.6f, Top = eave - 0.5f });
				}
				WallRun(d, P(h.U0 + t * 0.5f, 0f, -half + t), P(h.U0 + t * 0.5f, 0f, half - t), t, -FoundationDepth, eave, start, StructureMaterial.Stone, key + 3);
				WallRun(d, P(h.U1 - t * 0.5f, 0f, -half + t), P(h.U1 - t * 0.5f, 0f, half - t), t, -FoundationDepth, eave, end, StructureMaterial.Stone, key + 4);
			}

			// The panes, and from the middle level each lancet's sill and pointed hood, and at the finest its mullion.
			for (int i = 0; i < lancets.Count; i++)
			{
				(float u, int side) = lancets[i];
				Vector3 outward = Dir(0f, side);
				Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
				Vector3 along = Dir(1f, 0f);
				int k = key + 100 + i * 6;
				d.Add(StructureShapes.Block(P(u, (sill + head) * 0.5f, side * (half - t * 0.5f)), new Vector3(lancetWidth, head - sill, 0.04f), turn, StructureMaterial.Glass, k, StructureRole.Detail));
				if (!d.Mid)
				{
					continue;
				}
				Vector3 faceAt = P(u, 0f, side * (half + 0.04f));
				d.Add(StructureShapes.Block(faceAt + Vector3.up * (sill - 0.06f) + outward * 0.04f, new Vector3(lancetWidth + 0.3f, 0.12f, 0.18f), turn, StructureMaterial.Stone, k + 1, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(faceAt + along * (s * (lancetWidth * 0.5f + 0.1f)) + Vector3.up * (head - 0.25f), faceAt + Vector3.up * (head + 0.42f), 0.1f, 0.1f,
						StructureMaterial.Stone, k + 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
				if (d.Fine && !h.Against)
				{
					// The mullion parting the two lights, and the transom across them.
					Vector3 mid = P(u, 0f, side * (half - t * 0.5f));
					d.Add(StructureShapes.Block(mid + Vector3.up * ((sill + head) * 0.5f), new Vector3(0.08f, head - sill, 0.12f), turn, StructureMaterial.Stone, k + 4, StructureRole.Detail));
					d.Add(StructureShapes.Block(mid + Vector3.up * (head - 0.6f), new Vector3(lancetWidth, 0.08f, 0.12f), turn, StructureMaterial.Stone, k + 5, StructureRole.Detail));
				}
			}
			if (h.GreatWindow)
			{
				Vector3 outward = Dir(1f, 0f);
				Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
				Vector3 across = Dir(0f, 1f);
				Vector3 mid = P(h.U1 - t * 0.5f, 0f, 0f);
				float wy0 = floor + 1.6f, wy1 = eave - 0.5f;
				d.Add(StructureShapes.Block(mid + Vector3.up * ((wy0 + wy1) * 0.5f), new Vector3(great, wy1 - wy0, 0.04f), turn, StructureMaterial.Glass, key + 80, StructureRole.Detail));
				if (d.Mid)
				{
					// Two mullions into three lights, a hood over them.
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Block(mid + across * (s * great / 6f) + Vector3.up * ((wy0 + wy1) * 0.5f), new Vector3(0.09f, wy1 - wy0, 0.14f), turn, StructureMaterial.Stone,
							key + 81 + (s > 0 ? 1 : 0), StructureRole.Detail));
						Vector3 faceAt = P(h.U1 + 0.04f, 0f, 0f);
						d.Add(StructureShapes.Beam(faceAt + across * (s * (great * 0.5f + 0.12f)) + Vector3.up * (wy1 - 0.3f), faceAt + Vector3.up * (wy1 + great * 0.5f), 0.12f, 0.12f,
							StructureMaterial.Stone, key + 83 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
				if (d.Fine)
				{
					// Y-tracery in the head: the middle light's mullions forking up to the hood.
					d.Add(StructureShapes.Block(mid + Vector3.up * (wy1 - 0.9f), new Vector3(great, 0.08f, 0.14f), turn, StructureMaterial.Stone, key + 85, StructureRole.Detail));
				}
			}

			// Buttresses down the long sides, between the lancets and at the corners (none against the tower), two stages with
			// sloped weatherings.
			var buttresses = new List<float>();
			for (int i = 1; i < h.Lancets; i++)
			{
				buttresses.Add(h.U0 + t + i * bay);
			}
			if (!h.Against)
			{
				buttresses.Add(h.U0 + 0.35f);
			}
			buttresses.Add(h.U1 - 0.35f);
			int b = 0;
			using (d.Builds(WallRoles))
			{
				foreach (float u in buttresses)
				{
					for (int side = -1; side <= 1; side += 2)
					{
						if (side < 0 && h.SideDoor && Mathf.Abs(u - h.SideDoorU) < h.DoorWidth * 0.5f + 0.9f)
						{
							continue;
						}
						Vector3 outward = Dir(0f, side);
						float stage = floor + h.WallHeight * 0.5f, crown = eave - 0.35f;
						StructureSolid lower = StructureShapes.Box(P(u, (stage - FoundationDepth) * 0.5f, side * (half + 0.4f)), S(0.65f, stage + FoundationDepth, 0.8f),
							StructureMaterial.Stone, key + 200 + b++);
						Vector3 nLow = (outward * 0.6f + Vector3.up * 0.8f).normalized;
						lower.Clip(nLow, Vector3.Dot(nLow, P(u, stage, side * half)));
						d.Add(lower);
						StructureSolid upper = StructureShapes.Box(P(u, (stage - 0.4f + crown) * 0.5f, side * (half + 0.22f)), S(0.5f, crown - stage + 0.4f, 0.44f),
							StructureMaterial.Stone, key + 200 + b++);
						Vector3 nUp = (outward * 0.7f + Vector3.up * 0.7f).normalized;
						upper.Clip(nUp, Vector3.Dot(nUp, P(u, crown, side * half)));
						d.Add(upper);
					}
				}
			}

			// The roof as its palette has it (OwnGable), from the tower's face (against one) or overhanging both ends, and its gables.
			const float overhang = 0.4f;
			float uc = h.Against ? (h.U0 + h.U1 + overhang) * 0.5f : uMid;
			float roofLength = h.Against ? length - overhang : length;
			Matrix4x4 Place(float u) => x ? Matrix4x4.TRS(P(u, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one) : Matrix4x4.Translate(P(u, 0f, 0f));
			OwnRoofPlan plan = OwnGable(d, h.Width, roofLength, eave, h.Rise, overhang, 0.16f, StructureMaterial.Timber, key + 300, Place(uc));
			if (!plan.Flat)
			{
				using (d.Builds(WallRoles))
				{
					Gable(d, h.Width - 0.02f, eave, plan.Rise - 0.02f, -length * 0.5f + t * 0.5f, t, StructureMaterial.Stone, key + 310, Place(uMid));
					Gable(d, h.Width - 0.02f, eave, plan.Rise - 0.02f, length * 0.5f - t * 0.5f, t, StructureMaterial.Stone, key + 311, Place(uMid));
				}
				if (!h.Against)
				{
					Bargeboards(d, h.Width, roofLength, eave, plan.Rise, overhang, StructureMaterial.Timber, key + 320, Place(uc));
				}
			}
			float ridge = eave + (plan.Flat ? 0.3f : plan.Rise);
			if (h.Fleche)
			{
				// A louvred timber lantern astride the ridge, its spirelet in the roof's covering.
				Vector3 foot = P(uMid, 0f, 0f);
				d.Add(StructureShapes.Box(foot + Vector3.up * (ridge + 0.3f), new Vector3(1f, 1.4f, 1f), StructureMaterial.Timber, key + 330));
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.85f, ridge + 1f), new Vector2(0f, ridge + 2.7f) }, 4, StructureMaterial.Timber, key + 331, false, Mathf.PI * 0.25f,
						false, false, StructureRole.Roof).Transform(Matrix4x4.Translate(foot)));
				}
				if (d.Mid)
				{
					for (int f = 0; f < 4; f++)
					{
						Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
						d.Add(StructureShapes.Block(foot + Vector3.up * (ridge + 0.55f) + q * new Vector3(0f, 0f, -0.52f), new Vector3(0.6f, 0.6f, 0.05f), q, StructureMaterial.Ash, key + 332 + f,
							StructureRole.Detail));
					}
				}
				if (d.Fine)
				{
					d.Add(StructureShapes.Beam(foot + Vector3.up * (ridge + 2.6f), foot + Vector3.up * (ridge + 3.3f), 0.05f, 0.05f, StructureMaterial.Iron, key + 336, StructureRole.Detail));
				}
			}

			// The door, its step, its carved arch; the room, its way in from the tower kept clear.
			if (h.EndDoor)
			{
				HangDoor(d, P(h.U0 + t - 0.05f, floor, 0f), Dir(0f, 1f), Dir(1f, 0f), h.DoorWidth, h.DoorHeight, StructureDoorKind.Studded);
				Steps(d, P(h.U0 - 0.15f, 0f, 0f), Dir(-1f, 0f), h.DoorWidth + 0.8f, floor, StructureMaterial.Stone, key + 40);
				Vector3 at = P(h.U0, 0f, 0f);
				DoorArch(d, at.x, at.z, floor, h.DoorWidth, h.DoorHeight, t * 0.6f, key + 50, 2);
			}
			if (h.SideDoor)
			{
				HangDoor(d, P(h.SideDoorU, floor, -half + t - 0.05f), Dir(1f, 0f), Dir(0f, 1f), h.DoorWidth, h.DoorHeight, StructureDoorKind.Studded);
				Steps(d, P(h.SideDoorU, 0f, -half - 0.15f), Dir(0f, -1f), h.DoorWidth + 0.6f, floor, StructureMaterial.Stone, key + 40);
				Vector3 at = P(h.SideDoorU, 0f, -half);
				DoorArch(d, at.x, at.z, floor, h.DoorWidth, h.DoorHeight, t * 0.6f, key + 50, 2);
			}
			d.Room(P(uMid + (h.Against ? t * 0.5f : 0f), floor, 0f), new Vector2(x ? length - 2f * t : h.Width - 2f * t, x ? h.Width - 2f * t : length - 2f * t), h.WallHeight, StructureRoomKind.Library);
			if (h.Through)
			{
				Vector3 a = P(h.U0 + t, 0f, -0.9f), z = P(h.U0 + t + 1.5f, 0f, 0.9f);
				d.RoomClear(new Vector2(a.x, a.z), new Vector2(z.x, z.z));
			}
		}

		// ── Library ───────────────────────────────────────────────────

		/// <summary>
		/// A town's library (2026-10-19): v0 a stone reading hall, gable end to the street with its double door, lancets down both
		/// sides between buttresses and a great window at the far end, a louvred lantern on the ridge; v1 a scriptorium, a jettied
		/// timber-framed storey on a stone ground floor (the house kit), its stair up the side wall.
		/// </summary>
		private static void Library(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				BuildHall(d, new Hall
				{
					AlongX = false, U0 = -8f, U1 = 8f, Width = 10f, Wall = 0.6f, WallHeight = 6f, Floor = 0.5f, Rise = 6f,
					Lancets = 5, EndDoor = true, DoorWidth = 1.9f, DoorHeight = 3f, GreatWindow = true, Fleche = true, Key = 1,
				});
				if (d.Mid)
				{
					// A lantern on an iron arm beside the door.
					const float lampX = 1.9f, lz = -8f;
					d.Add(StructureShapes.Box(new Vector3(lampX, 3.1f, lz - 0.02f), new Vector3(0.1f, 0.3f, 0.05f), StructureMaterial.Iron, 900, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(lampX, 3.2f, lz), new Vector3(lampX, 3.2f, lz - 0.5f), 0.04f, 0.04f, StructureMaterial.Iron, 901, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(lampX, 3.2f, lz - 0.45f), new Vector3(lampX, 2.85f, lz - 0.45f), 0.015f, 0.015f, StructureMaterial.Iron, 902, StructureRole.Detail));
					Lantern(d, new Vector3(lampX, 2.49f, lz - 0.45f), true, 903);
				}
				return;
			}
			var h = new House
			{
				Width = 14f, Depth = 9f, WallHeight = 2.8f, Upper = 2.7f, Rise = 0f, Overhang = 0.5f, Wall = 0.4f, Plinth = 0.45f,
				WallMaterial = StructureMaterial.Stone, RoofMaterial = StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, UpperWallMaterial = StructureMaterial.Plaster, UpperFrame = true, RidgeAlongX = true, Chimney = false, Windows = 4,
				DoorWidth = 1.6f, DoorHeight = 2.4f, DoorX = 0.8f, DoorKind = StructureDoorKind.Studded,
				RoomKind = StructureRoomKind.Library, UpperRoomKind = StructureRoomKind.Library, Key = 1,
			};
			HouseRoof roof = BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f;
			if (!d.Mid)
			{
				return;
			}
			// A louvred lantern on the ridge (moved with a roof its palette re-pitched), and a lantern on an arm by the door.
			Vector3 foot = new Vector3(0f, roof.Ridge, 0f);
			d.Add(StructureShapes.Box(foot + Vector3.up * 0.3f, new Vector3(0.9f, 1.2f, 0.9f), StructureMaterial.Timber, 900, StructureRole.Detail));
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.75f, roof.Ridge + 0.9f), new Vector2(0f, roof.Ridge + 2.1f) }, 4, StructureMaterial.Timber, 901, false, Mathf.PI * 0.25f,
					false, false, StructureRole.Roof));
			}
			float lx = h.DoorX + h.DoorWidth * 0.5f + 0.7f;
			d.Add(StructureShapes.Beam(new Vector3(lx, floor + 2.75f, front), new Vector3(lx, floor + 2.75f, front - 0.5f), 0.04f, 0.04f, StructureMaterial.Iron, 905, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(lx, floor + 2.75f, front - 0.45f), new Vector3(lx, floor + 2.4f, front - 0.45f), 0.015f, 0.015f, StructureMaterial.Iron, 906, StructureRole.Detail));
			Lantern(d, new Vector3(lx, floor + 2.04f, front - 0.45f), false, 907);
		}

		// ── Furniture ─────────────────────────────────────────────────

		/// <summary>A binding's material, by a book's own draw: dyed cloth, plain vellum, or leather (timber's brown).</summary>
		private static StructureMaterial Binding(StructureDraft d, int key)
		{
			float r = d.Rand(key, 5);
			return r < 0.45f ? StructureMaterial.Dyed : r < 0.7f ? StructureMaterial.Canvas : StructureMaterial.Timber;
		}

		/// <summary>
		/// A bookcase (v0), 1.2×0.4×2.1 m, five shelves of books in runs of a height and binding each; or a scroll rack (v1) of
		/// sixteen pigeonholes, 1.8 m high, scrolls lying in them ends out. The books thin out with the level: one block a shelf last.
		/// </summary>
		private static void Bookcase(StructureDraft d)
		{
			const float width = 1.2f;
			float depth = d.Variant == 0 ? 0.4f : 0.45f, height = d.Variant == 0 ? 2.1f : 1.8f;
			// The case: sides, back, a plinth and a cornice.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.03f), height * 0.5f, 0f), new Vector3(0.06f, height, depth), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
			}
			d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, depth * 0.5f - 0.015f), new Vector3(width, height, 0.03f), StructureMaterial.Timber, 3)).Grain = Vector3.up;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.05f, -0.01f), new Vector3(width, 0.1f, depth + 0.02f), StructureMaterial.Timber, 4)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, height + 0.04f, -0.02f), new Vector3(width + 0.08f, 0.08f, depth + 0.06f), StructureMaterial.Timber, 5)).Grain = Vector3.right;
			float inside = width - 0.12f;
			if (d.Variant == 0)
			{
				const int shelves = 5;
				const float pitch = 0.4f;
				for (int row = 0; row < shelves; row++)
				{
					float y = 0.1f + row * pitch;
					if (row > 0)
					{
						d.Add(StructureShapes.Box(new Vector3(0f, y - 0.015f, 0f), new Vector3(inside, 0.03f, depth - 0.03f), StructureMaterial.Timber, 10 + row)).Grain = Vector3.right;
					}
					// Runs of books, each of one height and binding, packed from the left with a gap at the end.
					int runs = d.Fine ? 5 : d.Mid ? 3 : 1;
					float slot = inside / runs;
					for (int r = 0; r < runs; r++)
					{
						int k = 100 + row * 10 + r;
						float w = runs == 1 ? inside - 0.12f : slot * d.Range(k, 1, 0.72f, 0.97f);
						float tall = runs == 1 ? 0.27f : d.Range(k, 2, 0.21f, 0.33f);
						float deep = d.Range(k, 3, 0.22f, 0.3f);
						float x0 = -inside * 0.5f + r * slot;
						d.Add(StructureShapes.Box(new Vector3(x0 + w * 0.5f, y + tall * 0.5f, depth * 0.5f - 0.03f - deep * 0.5f), new Vector3(w, tall, deep), Binding(d, k), k)).Grain = Vector3.up;
					}
					if (d.Fine && d.Rand(200 + row, 1) < 0.6f)
					{
						// A book leaning in the gap at the run's end.
						int k = 200 + row;
						float tall = d.Range(k, 2, 0.22f, 0.3f);
						d.Add(StructureShapes.Block(new Vector3(inside * 0.5f - 0.07f, y + tall * 0.5f - 0.01f, depth * 0.5f - 0.17f), new Vector3(0.05f, tall, 0.24f),
							Quaternion.Euler(0f, 0f, 14f), Binding(d, k), k)).Grain = Vector3.up;
					}
				}
				return;
			}
			// The scroll rack: three boards across and three dividers up make sixteen pigeonholes.
			const int cells = 4;
			float cellW = inside / cells, cellH = (height - 0.1f) / cells;
			for (int i = 1; i < cells; i++)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.1f + i * cellH, 0f), new Vector3(inside, 0.025f, depth - 0.03f), StructureMaterial.Timber, 10 + i)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(-inside * 0.5f + i * cellW, 0.05f + height * 0.5f, 0f), new Vector3(0.025f, height - 0.1f, depth - 0.03f), StructureMaterial.Timber, 20 + i)).Grain = Vector3.up;
			}
			if (!d.Mid)
			{
				return;
			}
			int laid = 0;
			for (int row = 0; row < cells; row++)
			{
				for (int col = 0; col < cells; col++)
				{
					int k = 100 + row * 8 + col * 2;
					int count = d.Fine ? Mathf.FloorToInt(d.Range(k, 1, 0f, 2.6f)) : d.Rand(k, 2) < 0.7f ? 1 : 0;
					for (int s = 0; s < count && laid < 22; s++, laid++)
					{
						float r = d.Range(k + s, 3, 0.04f, 0.055f);
						float cx = -inside * 0.5f + (col + 0.5f) * cellW + (count > 1 ? (s - 0.5f) * 0.11f : d.Range(k, 4, -0.05f, 0.05f));
						float y = 0.1f + row * cellH + 0.0125f + r;
						float z0 = depth * 0.5f - 0.05f, z1 = -depth * 0.5f + d.Range(k + s, 5, 0.02f, 0.1f);
						d.Add(StructureShapes.Rod(new Vector3(cx, y, z0), new Vector3(cx, y, z1), r, d.Fine ? 6 : 5, d.Rand(k + s, 6) < 0.8f ? StructureMaterial.Canvas : StructureMaterial.Dyed,
							300 + row * 16 + col * 4 + s, StructureRole.Detail));
					}
				}
			}
		}

		/// <summary>
		/// A desk, its back to the wall: v0 a writing desk, 1.3 m, its drawers on the left, a pigeonholed back, an open book, an
		/// inkwell and quill and a lit candle (a lamp), a stool under it; v1 a scribe's sloped desk at a high stool's height, a book
		/// on the slope, an inkhorn, a candle on a pricket.
		/// </summary>
		private static void Desk(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float top = 0.76f, width = 1.3f, depth = 0.65f;
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.02f, 0f), new Vector3(width, 0.04f, depth), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(-width * 0.5f + 0.2f, (top - 0.04f) * 0.5f, 0.01f), new Vector3(0.38f, top - 0.04f, depth - 0.06f), StructureMaterial.Timber, 2)).Grain = Vector3.up;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(width * 0.5f - 0.06f, (top - 0.04f) * 0.5f, s * (depth * 0.5f - 0.06f)), new Vector3(0.06f, top - 0.04f, 0.06f), StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0)));
				}
				// The pigeonholed back on the desk's back edge: a back board, a top, ends and dividers, papers rolled in its holes.
				float pz = depth * 0.5f - 0.09f;
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.2f, depth * 0.5f - 0.015f), new Vector3(width - 0.1f, 0.4f, 0.03f), StructureMaterial.Timber, 5)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.39f, pz), new Vector3(width - 0.1f, 0.03f, 0.18f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
				for (int i = 0; i < 4; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(-(width - 0.12f) * 0.5f + i * (width - 0.12f) / 3f, top + 0.19f, pz), new Vector3(0.025f, 0.38f, 0.18f), StructureMaterial.Timber, 7 + i)).Grain = Vector3.up;
				}
				if (d.Mid)
				{
					for (int i = 0; i < 3; i++)
					{
						float px = -(width - 0.12f) / 3f + i * (width - 0.12f) / 3f;
						d.Add(StructureShapes.Rod(new Vector3(px - 0.05f, top + 0.04f, pz + 0.07f), new Vector3(px - 0.05f, top + 0.04f, pz - 0.1f), 0.035f, 5, StructureMaterial.Canvas, 30 + i, StructureRole.Detail));
					}
					// The open book: its leaves rising a little from the spine.
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Block(new Vector3(-0.12f + s * 0.12f, top + 0.025f, -0.08f), new Vector3(0.23f, 0.03f, 0.3f), Quaternion.Euler(0f, 0f, -s * 6f), StructureMaterial.Canvas,
							10 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
					d.Add(StructureShapes.Box(new Vector3(-0.12f, top + 0.006f, -0.08f), new Vector3(0.5f, 0.012f, 0.32f), StructureMaterial.Dyed, 12, StructureRole.Detail));
					// The inkwell and its quill.
					d.Add(StructureShapes.Cylinder(0.04f, top, top + 0.06f, 6, StructureMaterial.Earth, 13, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.3f, 0f, 0.05f))));
					d.Add(StructureShapes.Beam(new Vector3(0.31f, top + 0.04f, 0.05f), new Vector3(0.38f, top + 0.3f, 0.13f), 0.014f, 0.03f, StructureMaterial.Canvas, 14, StructureRole.Detail));
				}
				// The candle in its iron dish.
				var candle = new Vector3(0.5f, top, 0.12f);
				d.Add(StructureShapes.Cylinder(0.05f, 0f, 0.02f, 6, StructureMaterial.Iron, 15, false, StructureRole.Detail).Transform(Matrix4x4.Translate(candle)));
				d.Add(StructureShapes.Cylinder(0.022f, 0.02f, 0.18f, 6, StructureMaterial.Canvas, 16, false, StructureRole.Detail).Transform(Matrix4x4.Translate(candle)));
				if (d.Fine)
				{
					d.Add(StructureShapes.Sphere(candle + Vector3.up * 0.21f, new Vector3(0.014f, 0.03f, 0.014f), 5, 3, StructureMaterial.Lamplight, 17, StructureRole.Detail));
				}
				d.Emit(candle + Vector3.up * 0.22f, SceneEmitterKind.Lamp);
				// The stool, half under the desk.
				d.Add(StructureShapes.Cylinder(0.17f, 0.42f, 0.46f, d.Seg(8, 5), StructureMaterial.Timber, 20).Transform(Matrix4x4.Translate(new Vector3(0.15f, 0f, -0.33f))));
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f + 0.3f;
					var footAt = new Vector3(0.15f + Mathf.Cos(a) * 0.19f, 0f, -0.33f + Mathf.Sin(a) * 0.19f);
					d.Add(StructureShapes.Beam(footAt, new Vector3(0.15f + Mathf.Cos(a) * 0.1f, 0.43f, -0.33f + Mathf.Sin(a) * 0.1f), 0.04f, 0.04f, StructureMaterial.Timber, 21 + i));
				}
				return;
			}
			// The scribe's desk: four legs, its board sloping down toward the scribe, a lip to hold the book, a shelf under it.
			const float back = 1.2f, frontY = 1.0f, w = 0.9f, dz = 0.6f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					float hgt = sz > 0 ? back : frontY;
					d.Add(StructureShapes.Box(new Vector3(sx * (w * 0.5f - 0.05f), hgt * 0.5f, sz * (dz * 0.5f - 0.05f)), new Vector3(0.06f, hgt, 0.06f), StructureMaterial.Timber,
						1 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0))).Grain = Vector3.up;
				}
			}
			float slope = Mathf.Atan2(back - frontY, dz) * Mathf.Rad2Deg;
			Quaternion tilt = Quaternion.Euler(-slope, 0f, 0f);
			var boardAt = new Vector3(0f, (back + frontY) * 0.5f + 0.02f, 0f);
			d.Add(StructureShapes.Block(boardAt, new Vector3(w, 0.04f, dz / Mathf.Cos(slope * Mathf.Deg2Rad) + 0.04f), tilt, StructureMaterial.Timber, 5)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.35f, 0f), new Vector3(w - 0.1f, 0.03f, dz - 0.1f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, frontY + 0.05f, -dz * 0.5f + 0.01f), new Vector3(w - 0.1f, 0.05f, 0.03f), StructureMaterial.Timber, 7, StructureRole.Detail));
				d.Add(StructureShapes.Block(boardAt + tilt * new Vector3(-0.05f, 0.04f, 0f), new Vector3(0.42f, 0.035f, 0.3f), tilt, StructureMaterial.Canvas, 8, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(-0.1f, 0.39f, 0.02f), new Vector3(0.3f, 0.05f, 0.24f), Binding(d, 9), 9, StructureRole.Detail));
				d.Add(StructureShapes.Rod(new Vector3(0.36f, back - 0.02f, dz * 0.5f - 0.12f), new Vector3(0.36f, back + 0.12f, dz * 0.5f - 0.08f), 0.025f, 5, StructureMaterial.Bone, 10, StructureRole.Detail));
			}
			// The pricket candle at the high back corner.
			var pricket = new Vector3(-w * 0.5f + 0.08f, back, dz * 0.5f - 0.06f);
			d.Add(StructureShapes.Cylinder(0.02f, 0f, 0.16f, 5, StructureMaterial.Canvas, 11, false, StructureRole.Detail).Transform(Matrix4x4.Translate(pricket)));
			d.Add(StructureShapes.Cylinder(0.045f, -0.02f, 0.01f, 5, StructureMaterial.Iron, 12, false, StructureRole.Detail).Transform(Matrix4x4.Translate(pricket)));
			if (d.Fine)
			{
				d.Add(StructureShapes.Sphere(pricket + Vector3.up * 0.19f, new Vector3(0.014f, 0.03f, 0.014f), 5, 3, StructureMaterial.Lamplight, 13, StructureRole.Detail));
			}
			d.Emit(pricket + Vector3.up * 0.2f, SceneEmitterKind.Lamp);
			// The high stool before it.
			var seat = new Vector3(0f, 0f, -dz * 0.5f - 0.3f);
			d.Add(StructureShapes.Box(seat + Vector3.up * 0.7f, new Vector3(0.36f, 0.04f, 0.34f), StructureMaterial.Timber, 20)).Grain = Vector3.right;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(seat + new Vector3(sx * 0.17f, 0f, sz * 0.16f), seat + new Vector3(sx * 0.13f, 0.69f, sz * 0.12f), 0.035f, 0.035f, StructureMaterial.Timber,
						21 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
				}
			}
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(seat + Vector3.up * 0.25f, new Vector3(0.3f, 0.03f, 0.03f), StructureMaterial.Timber, 25, StructureRole.Detail));
			}
		}

		/// <summary>A flask of glowing liquor: a bellied glass body (its liquid Lamplight) and a neck, its foot at <paramref name="at"/>.</summary>
		private static void Flask(StructureDraft d, Vector3 at, float size, int key)
		{
			float r = 0.07f * size, h = 0.26f * size;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r * 0.8f, 0f), new Vector2(r, h * 0.3f), new Vector2(r * 0.35f, h * 0.62f), new Vector2(0f, h * 0.62f) },
				d.Seg(6, 4), StructureMaterial.Lamplight, key, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
			d.Add(StructureShapes.Cylinder(r * 0.3f, h * 0.6f, h, d.Seg(6, 4), StructureMaterial.Glass, key + 1, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>
		/// An alchemist's work, its back to the wall: v0 a bench (1.6 m) of glowing flasks, an alembic and a retort over a brazier, jars
		/// on its shelf and on a shelf behind (a lamp); v1 an iron cauldron of glowing brew hung from a tripod over a ring of coals (a
		/// fire indoors), a low bench of jars behind it.
		/// </summary>
		private static void AlchemyBench(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float top = 0.88f, width = 1.6f, depth = 0.62f;
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.03f, 0f), new Vector3(width, 0.06f, depth), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(sx * (width * 0.5f - 0.06f), (top - 0.06f) * 0.5f, sz * (depth * 0.5f - 0.06f)), new Vector3(0.08f, top - 0.06f, 0.08f),
							StructureMaterial.Timber, 2 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0))).Grain = Vector3.up;
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.22f, 0f), new Vector3(width - 0.1f, 0.03f, depth - 0.1f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
				// The shelf behind, on the wall, with its jars.
				d.Add(StructureShapes.Box(new Vector3(0f, 1.42f, depth * 0.5f - 0.12f), new Vector3(width - 0.2f, 0.03f, 0.22f), StructureMaterial.Timber, 7)).Grain = Vector3.right;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.2f), 1.32f, depth * 0.5f - 0.05f), new Vector3(0.04f, 0.2f, 0.08f), StructureMaterial.Timber, 8 + (s > 0 ? 1 : 0)));
				}
				int jars = d.Fine ? 3 : d.Mid ? 2 : 0;
				for (int i = 0; i < jars; i++)
				{
					float x = -0.6f + i * 0.5f + d.Range(30 + i, 1, -0.05f, 0.05f);
					float tall = d.Range(30 + i, 2, 0.12f, 0.22f);
					d.Add(StructureShapes.Cylinder(d.Range(30 + i, 3, 0.05f, 0.075f), 1.435f, 1.435f + tall, 6, StructureMaterial.Earth, 30 + i, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(x, 0f, depth * 0.5f - 0.12f))));
					d.Add(StructureShapes.Cylinder(0.06f, 0.235f, 0.235f + tall, 6, StructureMaterial.Earth, 40 + i, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(-x, 0f, 0.05f))));
				}
				// The flasks along the bench.
				Flask(d, new Vector3(-0.6f, top, -0.05f), 1.2f, 50);
				if (d.Mid)
				{
					Flask(d, new Vector3(-0.3f, top, 0.1f), 0.85f, 52);
				}
				// The brazier and the retort over it on its ring stand, its neck down to the receiver.
				var brazier = new Vector3(0.45f, top, 0.02f);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.06f, 0f), new Vector2(0.14f, 0.08f), new Vector2(0.14f, 0.1f), new Vector2(0f, 0.1f) }, d.Seg(7, 5), StructureMaterial.Iron, 60)
					.Transform(Matrix4x4.Translate(brazier)));
				d.Add(StructureShapes.Cylinder(0.12f, 0.1f, 0.115f, d.Seg(7, 5), StructureMaterial.Ash, 61, false, StructureRole.Detail).Transform(Matrix4x4.Translate(brazier)));
				var bulb = brazier + Vector3.up * 0.3f;
				d.Add(StructureShapes.Sphere(bulb, new Vector3(0.1f, 0.09f, 0.1f), d.Seg(7, 5), 3, StructureMaterial.Lamplight, 62, StructureRole.Detail));
				var receiver = new Vector3(0.72f, top, -0.12f);
				d.Add(StructureShapes.Beam(bulb + new Vector3(0.05f, 0.06f, 0f), receiver + Vector3.up * 0.2f, 0.025f, 0.025f, StructureMaterial.Glass, 63, StructureRole.Detail));
				Flask(d, receiver, 0.8f, 64);
				if (d.Mid)
				{
					for (int i = 0; i < 3; i++)
					{
						float a = i * Mathf.PI * 2f / 3f;
						var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
						d.Add(StructureShapes.Beam(brazier + o * 0.15f, brazier + o * 0.12f + Vector3.up * 0.24f, 0.015f, 0.015f, StructureMaterial.Iron, 66 + i, StructureRole.Detail));
					}
				}
				d.Emit(new Vector3(0f, top + 0.25f, 0f), SceneEmitterKind.Lamp);
				return;
			}
			// The cauldron: its fire a ring of stones round the coals, the tripod over it, the pot hung on its chain.
			var hearth = new Vector3(0f, 0f, -0.2f);
			int stones = d.Mid ? 7 : 5;
			for (int i = 0; i < stones; i++)
			{
				float a = i * Mathf.PI * 2f / stones + d.Range(10 + i, 1, -0.15f, 0.15f);
				d.Add(RoughStone(d, hearth + new Vector3(Mathf.Cos(a) * 0.48f, 0f, Mathf.Sin(a) * 0.48f), new Vector3(0.22f, 0.16f, 0.18f), a * Mathf.Rad2Deg, 10 + i, d.Mid ? 2 : 0));
			}
			d.Add(StructureShapes.Cylinder(0.4f, 0f, 0.05f, d.Seg(10, 6), StructureMaterial.Ash, 20, false, StructureRole.Detail).Transform(Matrix4x4.Translate(hearth)));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(hearth + Vector3.up * 0.06f, new Vector3(0.3f, 0.03f, 0.22f), StructureMaterial.Lamplight, 21, StructureRole.Detail));
			}
			var apex = hearth + Vector3.up * 1.45f;
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f + Mathf.PI * 0.5f;
				d.Add(StructureShapes.Beam(hearth + new Vector3(Mathf.Cos(a) * 0.62f, 0f, Mathf.Sin(a) * 0.62f), apex, 0.04f, 0.04f, StructureMaterial.Iron, 22 + i));
			}
			var pot = hearth + Vector3.up * 0.32f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.2f, 0.02f), new Vector2(0.3f, 0.14f), new Vector2(0.31f, 0.3f), new Vector2(0.27f, 0.42f), new Vector2(0f, 0.42f) },
				d.Seg(10, 6), StructureMaterial.Iron, 30).Transform(Matrix4x4.Translate(pot)));
			d.Add(StructureShapes.Cylinder(0.25f, 0.4f, 0.425f, d.Seg(10, 6), StructureMaterial.Lamplight, 31, false, StructureRole.Detail).Transform(Matrix4x4.Translate(pot)));
			d.Add(StructureShapes.Beam(apex, pot + Vector3.up * 0.42f, 0.02f, 0.02f, StructureMaterial.Iron, 32, StructureRole.Detail));
			d.Emit(hearth + Vector3.up * 0.1f, SceneEmitterKind.Hearth);
			// The low bench of ingredients behind it, against the wall.
			var bench = new Vector3(0f, 0f, 0.62f);
			d.Add(StructureShapes.Box(bench + Vector3.up * 0.42f, new Vector3(1.2f, 0.05f, 0.36f), StructureMaterial.Timber, 40)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(bench + new Vector3(s * 0.52f, 0.2f, 0f), new Vector3(0.06f, 0.4f, 0.3f), StructureMaterial.Timber, 41 + (s > 0 ? 1 : 0)));
			}
			int pots = d.Fine ? 4 : d.Mid ? 2 : 0;
			for (int i = 0; i < pots; i++)
			{
				float x = -0.42f + i * 0.28f;
				float tall = d.Range(50 + i, 1, 0.1f, 0.22f);
				d.Add(StructureShapes.Cylinder(d.Range(50 + i, 2, 0.05f, 0.08f), 0.445f, 0.445f + tall, 6, i % 2 == 0 ? StructureMaterial.Earth : StructureMaterial.Glass, 50 + i, false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(bench + new Vector3(x, 0f, d.Range(50 + i, 3, -0.06f, 0.06f)))));
			}
		}

		/// <summary>
		/// A scrying focus for the middle of a room: v0 a crystal orb (a cold glow) held in iron claws on a carved stone pedestal, its
		/// runes alight; v1 an iron orrery on a tripod stand, its sun alight (a lamp), a meridian and an ecliptic ring and two planets
		/// on their arms.
		/// </summary>
		private static void ArcaneFocus(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.08f, 0f), new Vector3(0.62f, 0.16f, 0.62f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.22f, 0.16f), new Vector2(0.16f, 0.3f), new Vector2(0.13f, 0.8f), new Vector2(0.21f, 0.92f), new Vector2(0f, 0.92f) },
					8, StructureMaterial.Monolith, 2, false, Mathf.PI / 8f));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.95f, 0f), new Vector3(0.44f, 0.07f, 0.44f), d.Bevel(0.015f), StructureMaterial.Monolith, 3));
				var orb = new Vector3(0f, 1.2f, 0f);
				d.Add(StructureShapes.Sphere(orb, new Vector3(0.17f, 0.17f, 0.17f), d.Seg(10, 6), d.Fine ? 6 : 4, StructureMaterial.Runelight, 4, StructureRole.Detail));
				for (int i = 0; i < 4; i++)
				{
					float a = (i + 0.5f) * Mathf.PI * 0.5f;
					var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					d.Add(StructureShapes.Beam(o * 0.08f + Vector3.up * 0.98f, orb + o * 0.17f + Vector3.down * 0.02f, 0.025f, 0.025f, StructureMaterial.Iron, 5 + i, StructureRole.Detail));
				}
				if (d.Fine)
				{
					// Runes cut in the base's face, glowing as the orb does.
					for (int r = 0; r < 3; r++)
					{
						Rune(d, new Vector2((r - 1) * 0.16f, 0.08f), 0.045f, r * 2 + 2, -0.314f, 10 + r * 4);
					}
				}
				d.Emit(orb, SceneEmitterKind.Glow);
				return;
			}
			// The orrery: a tripod's feet, its column, the sun on top with the rings and the planets' arms round it.
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f + Mathf.PI * 0.5f;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.32f, 0f, Mathf.Sin(a) * 0.32f), new Vector3(0f, 0.32f, 0f), 0.04f, 0.04f, StructureMaterial.Iron, 1 + i));
			}
			d.Add(StructureShapes.Cylinder(0.035f, 0.3f, 1.12f, d.Seg(8, 5), StructureMaterial.Iron, 4, true));
			var sun = new Vector3(0f, 1.25f, 0f);
			d.Add(StructureShapes.Sphere(sun, new Vector3(0.11f, 0.11f, 0.11f), d.Seg(10, 6), d.Fine ? 5 : 3, StructureMaterial.Lamplight, 5, StructureRole.Detail));
			int seg = d.Seg(14, 8);
			d.Add(Ring(sun, 0.44f, 0.03f, Quaternion.Euler(90f, 0f, 0f), seg, StructureMaterial.Iron, 6, StructureRole.Structure));
			if (d.Mid)
			{
				d.Add(Ring(sun, 0.4f, 0.025f, Quaternion.Euler(0f, 25f, 23.5f), seg, StructureMaterial.Iron, 7, StructureRole.Detail));
				foreach ((float a, float r, float size, StructureMaterial m, int k) in new[] { (0.6f, 0.28f, 0.045f, StructureMaterial.Dyed, 10), (2.9f, 0.36f, 0.06f, StructureMaterial.Stone, 14) })
				{
					var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					d.Add(StructureShapes.Beam(sun + Vector3.down * 0.02f, sun + o * r + Vector3.down * 0.02f, 0.012f, 0.012f, StructureMaterial.Iron, k, StructureRole.Detail));
					d.Add(StructureShapes.Sphere(sun + o * r + Vector3.up * 0.02f, new Vector3(size, size, size), d.Seg(8, 5), d.Fine ? 4 : 3, m, k + 1, StructureRole.Detail));
				}
			}
			d.Emit(sun, SceneEmitterKind.Lamp);
		}
	}
}
#endif
