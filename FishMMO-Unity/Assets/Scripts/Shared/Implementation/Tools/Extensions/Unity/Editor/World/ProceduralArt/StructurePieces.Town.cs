#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The buildings a settlement is more than houses for (2026-10-11): church, temple, tavern, smithy, stables, notice board.
	/// Each is built from the house kit and the same parts as the rest, so it wears the same surfaces and weathers alike.
	/// </summary>
	public static partial class StructurePieces
	{
		private static void AddTown(List<StructurePiece> list)
		{
			Piece(list, "Church", StructureStyle.Stone, StructureSize.Building,
				"A stone church, 7×14 m nave with lancet windows and buttresses; v0 a bell tower and spire at the west end, v1 a chapel with a bell-cote.",
				Church, "building", "church", "sacred");
			Piece(list, "TownTemple", StructureStyle.Stone, StructureSize.Building,
				"A columned stone temple on a stepped podium, 10×14 m: a cella under a pedimented roof, braziers burning at the steps; v0 a portico of four columns, v1 two columns between the side walls.",
				TownTemple, "building", "temple", "sacred");
			Piece(list, "Tavern", StructureStyle.Timber, StructureSize.Building,
				"A two-storey jettied tavern, 8×10 m, a gable chimney over its taproom hearth, a hanging sign, barrels and a bench by the door; v1 thatched.",
				Tavern, "building", "tavern");
			Piece(list, "Smithy", StructureStyle.Timber, StructureSize.Building,
				"An open-fronted smithy, 6×5 m: a stone forge under a hood and chimney, anvil on its stump, quench trough and bellows; v1 a stone back wall.",
				Smithy, "building", "smithy");
			Piece(list, "Stables", StructureStyle.Timber, StructureSize.Building,
				"A plank stable, 12×6.5 m, stall half-doors along the front, a hay-loft door, trough, hitching rail and bales; v1 thatched.",
				Stables, "building", "stable");
			Piece(list, "NoticeBoard", StructureStyle.Timber, StructureSize.Small,
				"A notice board under a little roof with notices pinned to it; v1 a single post with a board.",
				NoticeBoard, "notice");
			// A door's leaf, a metre wide and two high (the door scales it to its doorway), hinge side at x = -0.5, the
			// outside face toward -z. Not baked into a building: each door is a server-run scene object (PointOfInterestDoors).
			StructurePiece leaf = Piece(list, "DoorLeaf", StructureStyle.Timber, StructureSize.Small,
				"A door's leaf, 1×2 m: v0 planks on ledges and a brace with strap hinges and a ring; v1 boarded oak studded with iron; v2 iron-plated, riveted.",
				Under(DoorRoles, DoorLeaf), "door");
			leaf.Variants = 3;
			leaf.Collides = false;
			Piece(list, "FieldFence", StructureStyle.Timber, StructureSize.Small,
				"Three metres of field fence along x: v0 post and two rails, v1 post and three rails, weathered and a little uneven.",
				Under(FenceRoles, FieldFence), "farm").ModuleLength = 3f;
		}

		// ── Door leaf ─────────────────────────────────────────────────

		private static void DoorLeaf(StructureDraft d)
		{
			// 10 cm: a ledged plank door's boards and the iron plate's frame (a thinner leaf read as a sheet of card).
			const float width = 1f, height = 2f, thick = 0.1f;
			var kind = (StructureDoorKind)d.Variant;
			if (kind == StructureDoorKind.Iron)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, thick), StructureMaterial.Iron, 1));
				if (d.Mid)
				{
					// A raised frame round the plate and a cross rail, riveted.
					d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, -thick * 0.5f - 0.01f), new Vector3(width - 0.12f, 0.08f, 0.02f), StructureMaterial.Iron, 2, StructureRole.Detail));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.06f), height * 0.5f, -thick * 0.5f - 0.01f), new Vector3(0.08f, height - 0.1f, 0.02f), StructureMaterial.Iron, 3 + (s > 0 ? 1 : 0), StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(0f, s > 0 ? height - 0.06f : 0.06f, -thick * 0.5f - 0.01f), new Vector3(width - 0.1f, 0.08f, 0.02f), StructureMaterial.Iron, 5 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
			else
			{
				// Vertical boards, edge to edge.
				int boards = kind == StructureDoorKind.Studded ? 6 : 5;
				float board = width / boards;
				for (int i = 0; i < boards; i++)
				{
					float x = -width * 0.5f + board * (i + 0.5f);
					d.Add(StructureShapes.Box(new Vector3(x, height * 0.5f, d.Range(10 + i, 1, -0.006f, 0.006f)), new Vector3(board - 0.004f, height, thick), StructureMaterial.Timber, 10 + i)).Grain = Vector3.up;
				}
				if (d.Mid)
				{
					// Ledges and a brace on the inside face (+z).
					foreach (float y in new[] { 0.35f, height - 0.35f })
					{
						d.Add(StructureShapes.Box(new Vector3(0f, y, thick * 0.5f + 0.02f), new Vector3(width - 0.08f, 0.14f, 0.04f), StructureMaterial.Timber, 30 + (y > 1f ? 1 : 0), StructureRole.Detail));
					}
					d.Add(StructureShapes.Beam(new Vector3(-width * 0.5f + 0.1f, 0.42f, thick * 0.5f + 0.02f), new Vector3(width * 0.5f - 0.1f, height - 0.42f, thick * 0.5f + 0.02f),
						0.12f, 0.04f, StructureMaterial.Timber, 32, StructureRole.Detail));
					// Strap hinges from the hinge side across the outside face (−z), and the ring by the latch.
					foreach (float y in new[] { 0.4f, height - 0.4f })
					{
						d.Add(StructureShapes.Box(new Vector3(-width * 0.5f + (kind == StructureDoorKind.Studded ? 0.45f : 0.3f), y, -thick * 0.5f - 0.008f),
							new Vector3(kind == StructureDoorKind.Studded ? 0.9f : 0.6f, 0.05f, 0.016f), StructureMaterial.Iron, 40 + (y > 1f ? 1 : 0), StructureRole.Detail));
					}
					d.Add(StructureShapes.Box(new Vector3(width * 0.5f - 0.16f, 1.0f, -thick * 0.5f - 0.03f), new Vector3(0.09f, 0.09f, 0.03f), StructureMaterial.Iron, 42, StructureRole.Detail));
				}
				if (kind == StructureDoorKind.Studded && d.Fine)
				{
					// Rows of iron studs.
					for (int row = 0; row < 5; row++)
					{
						for (int col = 0; col < 4; col++)
						{
							float x = -width * 0.5f + 0.17f + col * ((width - 0.34f) / 3f), y = 0.25f + row * ((height - 0.5f) / 4f);
							d.Add(StructureShapes.Box(new Vector3(x, y, -thick * 0.5f - 0.008f), new Vector3(0.035f, 0.035f, 0.016f), StructureMaterial.Iron, 50 + row * 4 + col, StructureRole.Detail));
						}
					}
				}
			}
		}

		// ── Field fence ───────────────────────────────────────────────

		private static void FieldFence(StructureDraft d)
		{
			int rails = d.Variant == 1 ? 3 : 2;
			// One post at the module's start (the next module's start closes it), rails to the next.
			float lean = d.Range(1, 1, -4f, 4f);
			Vector3 top = Quaternion.Euler(0f, 0f, lean) * new Vector3(0f, 1.25f, 0f);
			d.Add(StructureShapes.Beam(new Vector3(-1.5f, -0.35f, 0f), new Vector3(-1.5f, 0f, 0f) + top, 0.12f, 0.12f, StructureMaterial.Timber, 1));
			for (int r = 0; r < rails; r++)
			{
				float y = 0.4f + r * (0.75f / Mathf.Max(1, rails - 1));
				float sag = d.Range(2 + r, 3, -0.04f, 0.04f);
				d.Add(StructureShapes.Beam(new Vector3(-1.55f, y, -0.07f), new Vector3(1.55f, y + sag, -0.07f), 0.07f, 0.09f, StructureMaterial.Timber, 2 + r, StructureRole.Detail));
			}
		}

		// ── Church ────────────────────────────────────────────────────

		private static void Church(StructureDraft d)
		{
			bool tower = d.Variant == 0;
			var h = new House
			{
				Width = 7f, Depth = 14f, WallHeight = 5.5f, Rise = 4.2f, Overhang = 0.4f, Wall = 0.5f, Plinth = 0.5f,
				WallMaterial = StructureMaterial.Stone, RoofMaterial = StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, RidgeAlongX = false, Chimney = false, Windows = 0,
				DoorWidth = 1.6f, DoorHeight = 3f, DoorX = 0f, DoorKind = StructureDoorKind.Studded, RoomKind = StructureRoomKind.Church, Key = 1,
			};
			HouseRoof roof = BuildHouse(d, h);
			float floor = h.Plinth;
			// Lancet windows down both sides, and buttresses between them.
			for (int i = 0; i < 4; i++)
			{
				float z = -4.5f + i * 3f;
				for (int side = -1; side <= 1; side += 2)
				{
					float x = side * h.Width * 0.5f;
					if (d.Mid)
					{
						d.Add(StructureShapes.Box(new Vector3(x + side * 0.03f, floor + 3f, z), new Vector3(0.12f, 2.8f, 1.05f), StructureMaterial.Stone, 100 + i * 8 + (side > 0 ? 4 : 0), StructureRole.Detail));
					}
					d.Add(StructureShapes.Box(new Vector3(x + side * 0.08f, floor + 3f, z), new Vector3(0.06f, 2.4f, 0.7f), StructureMaterial.Glass, 101 + i * 8 + (side > 0 ? 4 : 0), StructureRole.Detail));
					if (d.Mid && i < 3)
					{
						d.Add(StructureShapes.ChamferBox(new Vector3(x + side * 0.35f, floor + 1.6f, z + 1.5f), new Vector3(0.7f, 3.6f, 0.7f), d.Bevel(0.03f),
							StructureMaterial.Stone, 102 + i * 8 + (side > 0 ? 4 : 0)));
					}
				}
			}
			// The east window over the altar.
			d.Add(StructureShapes.Box(new Vector3(0f, floor + 3.6f, h.Depth * 0.5f + 0.06f), new Vector3(1.2f, 3f, 0.06f), StructureMaterial.Glass, 140, StructureRole.Detail));

			float front = -h.Depth * 0.5f;
			if (tower)
			{
				// A square west tower over the door, a belfry under a spire, a cross on top.
				const float side = 3.6f, top = 13f, tw = 0.6f;
				float tz = front - side * 0.5f + 0.3f;
				// The tower's foot is a porch: an open arch in front, the church's door behind, solid from the arch's head up.
				float half = side * 0.5f, porchTop = floor + 3.6f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, tz), new Vector3(side, floor + FoundationDepth, side), d.Bevel(0.04f),
					StructureMaterial.Stone, 150));
				WallRun(d, new Vector3(-half, 0f, tz - half + tw * 0.5f), new Vector3(half, 0f, tz - half + tw * 0.5f), tw, floor, porchTop,
					new List<WallOpening> { new WallOpening { Along = half, Width = 1.5f, Bottom = floor - 0.01f, Top = floor + 3f } }, StructureMaterial.Stone, 151);
				WallRun(d, new Vector3(-half, 0f, tz + half - tw * 0.5f), new Vector3(half, 0f, tz + half - tw * 0.5f), tw, floor, porchTop,
					new List<WallOpening> { new WallOpening { Along = half, Width = h.DoorWidth, Bottom = floor - 0.01f, Top = floor + h.DoorHeight } }, StructureMaterial.Stone, 152);
				WallRun(d, new Vector3(-half + tw * 0.5f, 0f, tz - half + tw), new Vector3(-half + tw * 0.5f, 0f, tz + half - tw), tw, floor, porchTop, null, StructureMaterial.Stone, 153);
				WallRun(d, new Vector3(half - tw * 0.5f, 0f, tz - half + tw), new Vector3(half - tw * 0.5f, 0f, tz + half - tw), tw, floor, porchTop, null, StructureMaterial.Stone, 154);
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (porchTop + top) * 0.5f, tz), new Vector3(side, top - porchTop, side), d.Bevel(0.04f), StructureMaterial.Stone, 155));
				Steps(d, new Vector3(0f, 0f, tz - half), Vector3.back, 1.9f, floor, StructureMaterial.Stone, 156);
				if (d.Mid)
				{
					// A string course under the belfry, and the belfry's openings on every face.
					d.Add(StructureShapes.Box(new Vector3(0f, top - 3.2f, tz), new Vector3(side + 0.2f, 0.25f, side + 0.2f), StructureMaterial.Stone, 152, StructureRole.Detail));
					for (int f = 0; f < 4; f++)
					{
						Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
						Vector3 at = new Vector3(0f, top - 1.6f, tz) + q * new Vector3(0f, 0f, -side * 0.5f - 0.03f);
						d.Add(StructureShapes.Block(at, new Vector3(0.9f, 1.7f, 0.06f), q, StructureMaterial.Iron, 160 + f, StructureRole.Detail));
					}
				}
				float r = side * 0.5f * 1.414f + 0.15f;
				// A lathe turns about the origin: the spire is moved onto the tower, under the cross.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(r, top), new Vector2(0f, top + 6.5f) }, 4, StructureMaterial.Timber, 170, false, Mathf.PI * 0.25f, false, false, StructureRole.Roof)
					.Transform(Matrix4x4.Translate(new Vector3(0f, 0f, tz))));
				if (d.Mid)
				{
					Vector3 tip = new Vector3(0f, top + 6.5f, tz);
					d.Add(StructureShapes.Beam(tip, tip + Vector3.up * 1.2f, 0.08f, 0.08f, StructureMaterial.Iron, 171, StructureRole.Detail));
					d.Add(StructureShapes.Beam(tip + new Vector3(-0.35f, 0.8f, 0f), tip + new Vector3(0.35f, 0.8f, 0f), 0.07f, 0.07f, StructureMaterial.Iron, 172, StructureRole.Detail));
				}
			}
			else if (d.Mid)
			{
				// A bell-cote on the west gable: two piers and a little gable over the bell (on a flat roof, over the west parapet:
				// the bell-cote moves with the roof its palette re-pitched, StructureRoofs).
				float y0 = floor + h.WallHeight + h.Rise - 0.3f + roof.Shift;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * 0.45f, y0 + 0.7f, front + 0.1f), new Vector3(0.25f, 1.4f, 0.4f), StructureMaterial.Stone, 180 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, y0 + 1.5f, front + 0.1f), new Vector3(1.2f, 0.25f, 0.45f), StructureMaterial.Stone, 182, StructureRole.Roof));
				// The bell between the piers: a lathe turns about the origin, so it is moved onto the gable.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.22f, y0 + 0.75f), new Vector2(0.28f, y0 + 0.95f), new Vector2(0.12f, y0 + 1.3f), new Vector2(0f, y0 + 1.32f) },
					d.Seg(10, 6), StructureMaterial.Iron, 183, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, front + 0.1f))));
			}
		}

		// ── Temple ────────────────────────────────────────────────────

		private static void TownTemple(StructureDraft d)
		{
			const float cellaWidth = 7f, cellaDepth = 9f, wall = 0.6f, columnHeight = 5.5f, radius = 0.38f;
			// Four steps of 0.24 m: a character's controller climbs a quarter metre at most.
			const float podiumWidth = 10.4f, podiumDepth = 13.6f, step = 0.24f;
			const int stepCount = 4;
			bool portico = d.Variant == 0;
			// Three steps up, the first reaching down into the ground.
			for (int i = 0; i < stepCount; i++)
			{
				float top = (i + 1) * step, bottom = i == 0 ? -FoundationDepth : i * step;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (top + bottom) * 0.5f, 0f), new Vector3(podiumWidth - i * 0.6f, top - bottom, podiumDepth - i * 0.6f), d.Bevel(0.03f),
					StructureMaterial.Stone, 10 + i));
			}
			float floor = stepCount * step;
			float front = -podiumDepth * 0.5f + 1.2f;
			// The cella: the god's room at the back, its door to the portico.
			const float cellaZ = 1.5f;
			WallsWithDoor(d, new Vector3(0f, 0f, cellaZ), cellaWidth, cellaDepth, wall, floor, floor + columnHeight, 0f, 1.8f, floor - 0.01f, floor + 3.6f, StructureMaterial.Stone, 20);
			HangDoor(d, new Vector3(0f, floor, cellaZ - cellaDepth * 0.5f + wall - 0.05f), Vector3.right, Vector3.forward, 1.8f, 3.6f, StructureDoorKind.Studded);
			d.Room(new Vector3(0f, floor, cellaZ), new Vector2(cellaWidth - 2f * wall, cellaDepth - 2f * wall), columnHeight, StructureRoomKind.Temple);
			float columnZ = front + 0.6f;
			if (portico)
			{
				for (int c = 0; c < 4; c++)
				{
					Column(d, new Vector3(-3.6f + c * 2.4f, floor, columnZ), columnHeight, radius, StructureMaterial.Monolith, 30 + c * 3);
				}
			}
			else
			{
				// In antis: the side walls run forward to the front, two columns between their ends.
				float length = (cellaZ - cellaDepth * 0.5f) - columnZ + radius;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (cellaWidth - wall) * 0.5f, floor + columnHeight * 0.5f, columnZ - radius + length * 0.5f),
						new Vector3(wall, columnHeight, length), StructureMaterial.Stone, 30 + (s > 0 ? 1 : 0)));
				}
				for (int c = 0; c < 2; c++)
				{
					Column(d, new Vector3(c == 0 ? -1.2f : 1.2f, floor, columnZ), columnHeight, radius, StructureMaterial.Monolith, 34 + c * 3);
				}
			}
			// The entablature on the columns and the walls, and the pedimented roof over it all.
			float span = portico ? 8.6f : cellaWidth + 0.4f;
			float back = cellaZ + cellaDepth * 0.5f + 0.2f;
			float start = columnZ - radius - 0.3f;
			float length2 = back - start, middle = (back + start) * 0.5f;
			float eave = floor + columnHeight + 0.9f;
			d.Add(StructureShapes.Box(new Vector3(0f, eave - 0.45f, middle), new Vector3(span, 0.9f, length2), StructureMaterial.Stone, 50));
			Matrix4x4 place = Matrix4x4.Translate(new Vector3(0f, 0f, middle));
			const float rise = 1.6f;
			GableRoof(d, span, length2, eave, rise, 0.35f, 0.25f, StructureMaterial.Stone, 52, place);
			Gable(d, span, eave, rise, -length2 * 0.5f + 0.15f, 0.3f, StructureMaterial.Stone, 54, place);
			Gable(d, span, eave, rise, length2 * 0.5f - 0.15f, 0.3f, StructureMaterial.Stone, 55, place);
			if (!d.Mid)
			{
				return;
			}
			// Braziers burning at the top of the steps, either side of the way in.
			for (int s = -1; s <= 1; s += 2)
			{
				var at = new Vector3(s * (podiumWidth * 0.5f - 1.4f), floor, front - 0.4f);
				var bowl = new List<Vector2>
				{
					new Vector2(0.18f, 0f), new Vector2(0.08f, 0.1f), new Vector2(0.07f, 0.85f), new Vector2(0.32f, 0.95f), new Vector2(0.38f, 1.15f),
				};
				d.Add(StructureShapes.Lathe(bowl, d.Seg(10, 6), StructureMaterial.Iron, 60 + (s > 0 ? 2 : 0), true, 0f, false, true, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(at)));
				d.Add(StructureShapes.Box(at + new Vector3(0f, 1.08f, 0f), new Vector3(0.45f, 0.06f, 0.45f), StructureMaterial.Ash, 61 + (s > 0 ? 2 : 0), StructureRole.Detail));
				d.Emit(at + new Vector3(0f, 1.15f, 0f), SceneEmitterKind.Fire);
			}
		}

		// ── Tavern ────────────────────────────────────────────────────

		private static void Tavern(StructureDraft d)
		{
			var h = new House
			{
				Width = 11f, Depth = 14f, WallHeight = 2.8f, Upper = 2.7f, Rise = 3f, Overhang = 0.6f, Wall = 0.25f, Plinth = 0.45f,
				WallMaterial = StructureMaterial.Plaster, RoofMaterial = d.Variant == 1 ? StructureMaterial.Thatch : StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = true, RidgeAlongX = true, Chimney = true, Windows = 3,
				DoorWidth = 1.4f, DoorHeight = 2.4f, DoorX = -2.2f, DoorKind = StructureDoorKind.Studded, RoomKind = StructureRoomKind.Tavern,
				UpperRoomKind = StructureRoomKind.Lodging, Key = 1,
			};
			BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f;
			float ridgeTop = floor + h.WallHeight + 0.24f + h.Upper + h.Rise;
			if (!d.Mid)
			{
				return;
			}
			// The sign: an iron-braced bracket from the wall beside the door, the board hanging on two rods.
			var wall = new Vector3(h.DoorX + h.DoorWidth * 0.5f + 1.5f, floor + 3.1f, front - 0.25f);
			d.Add(StructureShapes.Beam(wall, wall + new Vector3(0f, 0f, -1.3f), 0.12f, 0.12f, StructureMaterial.Timber, 410, StructureRole.Detail));
			d.Add(StructureShapes.Beam(wall + new Vector3(0f, -0.7f, 0f), wall + new Vector3(0f, -0.05f, -0.8f), 0.05f, 0.05f, StructureMaterial.Iron, 411, StructureRole.Detail));
			Vector3 board = wall + new Vector3(0f, -0.65f, -0.85f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(wall + new Vector3(0f, -0.06f, -0.85f + s * 0.4f), board + new Vector3(0f, 0.35f, s * 0.4f), 0.02f, 0.02f, StructureMaterial.Iron, 412 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(board, new Vector3(0.07f, 0.7f, 1f), StructureMaterial.Timber, 414, StructureRole.Detail));
			d.Add(StructureShapes.Box(board, new Vector3(0.1f, 0.5f, 0.8f), StructureMaterial.Dyed, 415, StructureRole.Detail));
			// Barrels and a bench by the door.
			float doorLeft = h.DoorX - h.DoorWidth * 0.5f;
			foreach ((float x, float z, int key) in new[] { (doorLeft - 1f, front - 0.55f, 420), (doorLeft - 0.45f, front - 0.6f, 424) })
			{
				var profile = new List<Vector2> { new Vector2(0.27f, 0f), new Vector2(0.32f, 0.45f), new Vector2(0.27f, 0.9f) };
				d.Add(StructureShapes.Lathe(profile, d.Seg(12, 6), StructureMaterial.Timber, key, true, 0f, false, true, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(x, 0f, z))));
			}
			d.Add(StructureShapes.Beam(new Vector3(1.6f, 0.45f, front - 0.45f), new Vector3(3.4f, 0.45f, front - 0.45f), 0.3f, 0.07f, StructureMaterial.Timber, 430, StructureRole.Detail));
			for (int s = 0; s < 2; s++)
			{
				float x = s == 0 ? 1.8f : 3.2f;
				d.Add(StructureShapes.Box(new Vector3(x, 0.2f, front - 0.45f), new Vector3(0.08f, 0.45f, 0.26f), StructureMaterial.Timber, 431 + s, StructureRole.Detail));
			}
		}

		// ── Smithy ────────────────────────────────────────────────────

		private static void Smithy(StructureDraft d)
		{
			const float width = 6f, depth = 5f, eave = 2.8f;
			// Four posts and the roof over them: open at the front.
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * (width * 0.5f - 0.15f), -0.3f, sz * (depth * 0.5f - 0.15f)), new Vector3(sx * (width * 0.5f - 0.15f), eave, sz * (depth * 0.5f - 0.15f)),
						0.22f, 0.22f, StructureMaterial.Timber, 10 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			// At its palette's pitch, or a flat canopy (StructureRoofs, I6h): the forge's stack stands clear of either.
			OwnGable(d, depth, width, eave, 1.5f, 0.45f, 0.12f, StructureMaterial.Timber, 20, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one));
			// The back wall: planks (the smithy's log wall in a palette), or stone.
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, eave * 0.5f - 0.15f, depth * 0.5f - 0.15f), new Vector3(width - 0.2f, eave + 0.3f, 0.18f),
					d.Variant == 1 ? StructureMaterial.Fieldstone : StructureMaterial.Timber, 30)).Grain = Vector3.up;
			}
			// The forge: a stone hearth, its fire bed, a hood and a chimney through the roof.
			var forge = new Vector3(1.4f, 0f, 1.3f);
			d.Add(StructureShapes.ChamferBox(forge + new Vector3(0f, 0.35f, 0f), new Vector3(1.8f, 1.1f, 1.5f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 40));
			d.Add(StructureShapes.Box(forge + new Vector3(0f, 0.92f, -0.1f), new Vector3(1.2f, 0.05f, 0.9f), StructureMaterial.Ash, 41, StructureRole.Detail));
			d.Add(StructureShapes.ChamferBox(forge + new Vector3(0f, 2.05f, 0.1f), new Vector3(1.5f, 0.9f, 1.3f), d.Bevel(0.03f), StructureMaterial.Fieldstone, 42));
			d.Add(StructureShapes.ChamferBox(forge + new Vector3(0f, 3.9f, 0.25f), new Vector3(0.7f, 2.9f, 0.7f), d.Bevel(0.03f), StructureMaterial.Fieldstone, 43));
			d.Emit(forge + new Vector3(0f, 5.4f, 0.25f), SceneEmitterKind.Smoke);
			d.Emit(forge + new Vector3(0f, 1.05f, -0.4f), SceneEmitterKind.Forge);
			if (!d.Mid)
			{
				return;
			}
			// The anvil on its stump.
			var anvil = new Vector3(-0.6f, 0f, 0.1f);
			d.Add(StructureShapes.Cylinder(0.32f, -0.2f, 0.55f, d.Seg(10, 6), StructureMaterial.Timber, 50, true).Transform(Matrix4x4.Translate(anvil)));
			d.Add(StructureShapes.Box(anvil + new Vector3(0f, 0.68f, 0f), new Vector3(0.6f, 0.24f, 0.22f), StructureMaterial.Iron, 51));
			d.Add(StructureShapes.Box(anvil + new Vector3(0f, 0.6f, 0f), new Vector3(0.3f, 0.12f, 0.18f), StructureMaterial.Iron, 52));
			d.Add(StructureShapes.Beam(anvil + new Vector3(0.3f, 0.74f, 0f), anvil + new Vector3(0.58f, 0.7f, 0f), 0.1f, 0.08f, StructureMaterial.Iron, 53, StructureRole.Detail));
			// The quench trough and the bellows.
			var trough = new Vector3(-2f, 0f, 1.5f);
			d.Add(StructureShapes.Box(trough + new Vector3(0f, 0.28f, 0f), new Vector3(1.2f, 0.56f, 0.55f), StructureMaterial.Timber, 60));
			d.Add(StructureShapes.Box(trough + new Vector3(0f, 0.57f, 0f), new Vector3(1.05f, 0.03f, 0.42f), StructureMaterial.Ash, 61, StructureRole.Detail));
			d.Add(StructureShapes.Block(forge + new Vector3(-1.15f, 0.75f, 0.2f), new Vector3(0.45f, 0.18f, 0.7f), Quaternion.Euler(0f, 0f, 12f), StructureMaterial.Canvas, 62, StructureRole.Detail));
			// Tongs and a hammer hung on the back wall.
			for (int i = 0; i < 3; i++)
			{
				float x = -1.8f + i * 0.35f;
				d.Add(StructureShapes.Beam(new Vector3(x, 1.9f, depth * 0.5f - 0.28f), new Vector3(x + 0.05f, 1.2f, depth * 0.5f - 0.28f), 0.04f, 0.03f, StructureMaterial.Iron, 70 + i, StructureRole.Detail));
			}
		}

		// ── Stables ───────────────────────────────────────────────────

		private static void Stables(StructureDraft d)
		{
			var h = new House
			{
				Width = 16f, Depth = 9f, WallHeight = 3f, Rise = 2.6f, Overhang = 0.6f, Wall = 0.2f, Plinth = 0.3f,
				WallMaterial = StructureMaterial.Timber, RoofMaterial = d.Variant == 1 ? StructureMaterial.Thatch : StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = false, RidgeAlongX = true, Chimney = false, Windows = 0,
				DoorWidth = 2.6f, DoorHeight = 2.6f, DoorX = 0f, RoomKind = StructureRoomKind.Stable, Key = 1,
			};
			BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f;
			// Stall half-doors either side of the big doors, each with the dark of the stall above it: as many as the front holds.
			int perSide = Mathf.Max(1, Mathf.FloorToInt((h.Width * 0.5f - 1f - 2.4f) / 1.6f) + 1);
			for (int i = 0; i < perSide * 2; i++)
			{
				float x = (i < perSide ? -1f : 1f) * (2.4f + (i % perSide) * 1.6f);
				d.Add(StructureShapes.Box(new Vector3(x, floor + 0.7f, front - 0.05f), new Vector3(1.3f, 1.3f, 0.08f), StructureMaterial.Timber, 200 + i * 2, StructureRole.Detail)).Grain = Vector3.up;
				d.Add(StructureShapes.Box(new Vector3(x, floor + 1.85f, front - 0.02f), new Vector3(1.2f, 0.95f, 0.04f), StructureMaterial.Ash, 201 + i * 2, StructureRole.Detail));
			}
			// The hay-loft door in each gable.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (h.Width * 0.5f + 0.04f), floor + h.WallHeight + 0.75f, 0f), new Vector3(0.08f, 1.1f, 1.2f), StructureMaterial.Timber, 220 + (s > 0 ? 1 : 0), StructureRole.Detail)).Grain = Vector3.up;
			}
			if (!d.Mid)
			{
				return;
			}
			// A trough and a hitching rail before the doors; bales against the side.
			d.Add(StructureShapes.Box(new Vector3(-3.8f, 0.3f, front - 1.4f), new Vector3(1.6f, 0.55f, 0.5f), StructureMaterial.Timber, 230));
			d.Add(StructureShapes.Box(new Vector3(-3.8f, 0.58f, front - 1.4f), new Vector3(1.45f, 0.03f, 0.38f), StructureMaterial.Ash, 231, StructureRole.Detail));
			for (int i = 0; i < 3; i++)
			{
				float x = 2.2f + i * 1.5f;
				d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, front - 1.6f), new Vector3(x, 1.1f, front - 1.6f), 0.12f, 0.12f, StructureMaterial.Timber, 240 + i));
			}
			d.Add(StructureShapes.Beam(new Vector3(2.1f, 1.0f, front - 1.6f), new Vector3(5.3f, 1.0f, front - 1.6f), 0.1f, 0.1f, StructureMaterial.Timber, 245, StructureRole.Detail));
			for (int i = 0; i < 3; i++)
			{
				var at = new Vector3(h.Width * 0.5f + 0.75f, 0.28f + (i == 2 ? 0.55f : 0f), -1.2f + (i == 2 ? 0.55f : i * 1.1f));
				d.Add(StructureShapes.ChamferBox(at, new Vector3(0.6f, 0.55f, 1.05f), d.Bevel(0.05f), StructureMaterial.Thatch, 250 + i));
			}
		}

		// ── Notice board ──────────────────────────────────────────────

		private static void NoticeBoard(StructureDraft d)
		{
			bool roofed = d.Variant == 0;
			if (roofed)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * 0.85f, -0.3f, 0f), new Vector3(s * 0.85f, 2.25f, 0f), 0.1f, 0.1f, StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 1.45f, 0f), new Vector3(1.6f, 1.05f, 0.06f), StructureMaterial.Timber, 3)).Grain = Vector3.right;
				d.Add(StructureShapes.Block(new Vector3(0f, 2.28f, -0.06f), new Vector3(2.05f, 0.05f, 0.55f), Quaternion.Euler(-14f, 0f, 0f), StructureMaterial.Timber, 4, StructureRole.Roof));
			}
			else
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.3f, 0.06f), new Vector3(0f, 2f, 0.06f), 0.12f, 0.12f, StructureMaterial.Timber, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 1.4f, 0f), new Vector3(0.9f, 0.7f, 0.05f), StructureMaterial.Timber, 3)).Grain = Vector3.right;
			}
			if (!d.Mid)
			{
				return;
			}
			// Notices pinned up, a little askew.
			int notes = roofed ? 5 : 2;
			float spread = roofed ? 0.6f : 0.3f;
			for (int i = 0; i < notes; i++)
			{
				float x = d.Range(10 + i, 1, -spread, spread), y = d.Range(10 + i, 2, roofed ? 1.15f : 1.2f, roofed ? 1.75f : 1.6f);
				float tilt = d.Range(10 + i, 3, -8f, 8f);
				d.Add(StructureShapes.Block(new Vector3(x, y, -0.04f), new Vector3(0.22f, 0.3f, 0.01f), Quaternion.Euler(0f, 0f, tilt), StructureMaterial.Canvas, 20 + i, StructureRole.Detail));
			}
		}
	}
}
#endif
