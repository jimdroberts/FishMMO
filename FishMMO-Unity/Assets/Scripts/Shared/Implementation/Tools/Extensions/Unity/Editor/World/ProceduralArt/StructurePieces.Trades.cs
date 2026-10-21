#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The town's other buildings (2026-10-19: "more structure types for towns, villages, and cities"): the trades' houses and yards: healer, bakery, workshops, dye works, kilns, brewhouse.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Medieval forms only</b> (from the record): the herbalist's cottage with its simples drying under the eaves and its
	/// walled bed of physic herbs, and the stone hospital hall with its pentice (the covered walk a cloister's is); the
	/// bakehouse with its domed oven built out from the gable beside the chimney, its bread sold over a stall-board, the
	/// window's lower shutter let down on brackets as the counter and the upper propped as its awning; the craftsman's shop
	/// open to the street under its roof, its store behind; the dyer's coppers over their own fires and the cloth stretched on
	/// tenters; the potter's kiln; the brewhouse with its louvre for the copper's steam (or a malt kiln) and the ale-stake, a
	/// bush on a pole, hung out when there was ale to sell. Trade signs are things hung up (a mortar and pestle, a bush), as
	/// most medieval signs were, not lettering.
	/// </para>
	/// <para>
	/// <b>Roofs.</b> Each building lays one roof that follows its palette (a house's, or a shop's own gable): the pentices,
	/// the oven's and the yards' lean-tos, the louvre and the malt kiln's cap are shelters and fittings, pitched as their use
	/// has them (StructureRoofs).
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		private static void AddTrades(List<StructurePiece> list)
		{
			Piece(list, "Healer", StructureStyle.Timber, StructureSize.Building,
				"A house of healing: v0 a herbalist's framed cottage 10×8 m under thatch, herbs drying under its eaves, a carved mortar and pestle hung from a post by its walled garden of raised herb beds; v1 a stone hospice hall 16×9 m, a covered walk on posts along its front and a painted herb sprig hung by the door.",
				Healer, "building", "healer", "town");
			Piece(list, "Bakery", StructureStyle.Timber, StructureSize.Building,
				"A bakehouse 10×8 m, its domed bread oven built out from the chimney gable with its own flue, a stall-board let down under the shop window with loaves on it, flour sacks by the door; v0 framed plaster under planks and a clay oven, v1 stone under thatch, the oven stone under its own pentice.",
				Bakery, "building", "bakery", "town");
			Piece(list, "Workshop", StructureStyle.Timber, StructureSize.Building,
				"A craftsman's shop 8×7 m, open to the street under its roof on posts with a walled store and door behind: v0 a joiner's, plank-walled, a sawhorse, a pole lathe and planks; v1 a cooper's, framed and thatched, staves, hoops, barrels and one being fired.",
				Workshop, "building", "workshop", "town");
			Piece(list, "DyeWorks", StructureStyle.Timber, StructureSize.Medium,
				"A dyer's yard: v0 two coppers steaming on stone hearths and a rinsing tub, dyed cloth on tenter frames and hung from a pole, a lean-to of woad and mordant; v1 a fuller's and weaver's, a loom under a lean-to, a copper and a fulling trough, cloth on tenters and bolts of it.",
				DyeWorks, "dyeworks", "town");
			Piece(list, "PotteryKiln", StructureStyle.Stone, StructureSize.Medium,
				"A potter's yard: v0 a bottle kiln 7 m high, smoke from its neck and its fire mouths glowing, pots on a stand, saggars and clay; v1 a clay updraft kiln with its stoke-hole, a kick wheel under a lean-to and racks of pots drying.",
				PotteryKiln, "pottery", "town");
			Piece(list, "Brewhouse", StructureStyle.Timber, StructureSize.Building,
				"A brewhouse 12×8 m, barrels stacked by its wide door and an ale-stake (a bush on a pole) out over the street; v0 framed plaster under planks with a louvre on the ridge for the copper's steam, v1 stone under thatch with a malt kiln's tower and cowl at its gable.",
				Brewhouse, "building", "brewery", "town");
			Piece(list, "HerbShelf", StructureStyle.Timber, StructureSize.Small,
				"A healer's shelves: v0 shelves of jars and crocks with herbs hung to dry from a rod along the top, a mortar on one; v1 a counter with a mortar and pestle, a balance and bottles, a shelf of jars on the wall over it.",
				HerbShelf, "furniture", "herbs");
			Piece(list, "Workbench", StructureStyle.Timber, StructureSize.Small,
				"A craftsman's bench: v0 a joiner's bench 2 m long with a leg vice and its tools racked behind; v1 a cooper's shaving horse with a stave on it and staves and a hoop against the wall.",
				Workbench, "furniture", "workbench");
		}

		// ── Healer ───────────────────────────────────────────────────

		private static void Healer(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				Hospice(d);
			}
			else
			{
				HerbalistCottage(d);
			}
		}

		/// <summary>
		/// A herbalist's cottage: the house kit's framed cottage under thatch, its long side to the street, the ward inside
		/// warmed from the gable chimney; herbs hung in bunches to dry along rails under the eaves (never across the door);
		/// a garden of raised beds walled in fieldstone in front, a gap to the street; the sign, a carved mortar and pestle,
		/// hung from a post at the garden's corner (the eaves come down too low to hang it from the wall).
		/// </summary>
		private static void HerbalistCottage(StructureDraft d)
		{
			var h = new House
			{
				Width = 10f, Depth = 8f, WallHeight = 2.8f, Rise = 2f, Overhang = 0.5f, Wall = 0.25f, Plinth = 0.4f,
				WallMaterial = StructureMaterial.Plaster, RoofMaterial = StructureMaterial.Thatch, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = true, RidgeAlongX = true, Chimney = true, Windows = 2,
				DoorWidth = 1.1f, DoorHeight = 2.3f, DoorX = 1.2f, DoorKind = StructureDoorKind.Planks, RoomKind = StructureRoomKind.Infirmary, Key = 1,
			};
			BuildHouse(d, h);
			float front = -h.Depth * 0.5f;
			float doorRight = h.DoorX + h.DoorWidth * 0.5f;

			// The garden: a low fieldstone wall on three sides, the house's plinth the fourth, a gap in its front to the street.
			const float gx0 = -4.75f, gx1 = -0.8f, gz0 = -4.1f, gz1 = -7.4f, gt = 0.3f, gTop = 0.7f, gateX = -1.9f, gate = 0.95f;
			using (d.Builds(FenceRoles))
			{
				float gy = (gTop - 0.3f) * 0.5f, gh = gTop + 0.3f;
				foreach (float x in new[] { gx0 + gt * 0.5f, gx1 - gt * 0.5f })
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(x, gy, (gz0 + gz1) * 0.5f), new Vector3(gt, gh, gz0 - gz1), d.Bevel(0.04f), StructureMaterial.Fieldstone, 8000 + (x > -2f ? 1 : 0)));
				}
				float fz = gz1 + gt * 0.5f;
				d.Add(StructureShapes.ChamferBox(new Vector3((gx0 + gateX - gate * 0.5f) * 0.5f, gy, fz), new Vector3(gateX - gate * 0.5f - gx0, gh, gt), d.Bevel(0.04f), StructureMaterial.Fieldstone, 8002));
				d.Add(StructureShapes.ChamferBox(new Vector3((gateX + gate * 0.5f + gx1) * 0.5f, gy, fz), new Vector3(gx1 - gateX - gate * 0.5f, gh, gt), d.Bevel(0.04f), StructureMaterial.Fieldstone, 8003));
			}
			// Two raised beds, a path between them from the gap.
			var beds = new[] { (new Vector3(-3.6f, 0f, -5.75f), new Vector2(1.2f, 2.3f)), (new Vector3(-1.45f, 0f, -5f), new Vector2(0.8f, 1.4f)) };
			for (int b = 0; b < beds.Length; b++)
			{
				(Vector3 at, Vector2 size) = beds[b];
				d.Add(StructureShapes.Box(at + new Vector3(0f, 0.025f, 0f), new Vector3(size.x, 0.65f, size.y), StructureMaterial.Timber, 8010 + b * 4)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(at + new Vector3(0f, 0.36f, 0f), new Vector3(size.x - 0.12f, 0.03f, size.y - 0.12f), StructureMaterial.Earth, 8011 + b * 4, StructureRole.Detail));
				if (!d.Mid)
				{
					continue;
				}
				// Herbs in rows, some of them in flower.
				int tufts = d.Fine ? (b == 0 ? 3 : 2) : 1;
				for (int i = 0; i < tufts; i++)
				{
					float z = at.z + (tufts == 1 ? 0f : Mathf.Lerp(-size.y * 0.32f, size.y * 0.32f, i / (tufts - 1f)));
					d.Add(StructureShapes.Sphere(new Vector3(at.x, 0.38f, z), new Vector3(size.x * 0.32f, 0.22f, 0.3f), 6, 3,
						(i + b) % 2 == 0 ? StructureMaterial.Thatch : StructureMaterial.Dyed, 8020 + b * 8 + i, StructureRole.Detail));
				}
			}

			// The sign's post at the garden's front corner by the gap, its arm out over the way in, the mortar on two chains.
			var post = new Vector3(gx1 - gt * 0.5f, 0f, gz1 + gt * 0.5f);
			d.Add(StructureShapes.Beam(post + Vector3.down * 0.3f, post + Vector3.up * 3.05f, 0.15f, 0.15f, StructureMaterial.Timber, 8040));
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(post + new Vector3(-0.08f, 2.88f, 0f), post + new Vector3(1.05f, 2.88f, 0f), 0.1f, 0.12f, StructureMaterial.Timber, 8041, StructureRole.Detail));
				d.Add(StructureShapes.Beam(post + new Vector3(0f, 2.3f, 0f), post + new Vector3(0.55f, 2.84f, 0f), 0.06f, 0.06f, StructureMaterial.Iron, 8042, StructureRole.Detail));
				TradeMortarSign(d, post + new Vector3(0.82f, 2.82f, 0f), 8045);
			}

			// Rails under the eaves either side of the door, hooked to the wall, with the bunches hung head down from them.
			if (d.Mid)
			{
				const float railY = 2.55f;
				float railZ = front - 0.3f;
				var rails = new[] { (-h.Width * 0.5f + 0.4f, -1.2f, d.Fine ? 5 : 3), (doorRight + 0.45f, h.Width * 0.5f - 0.4f, d.Fine ? 4 : 2) };
				for (int r = 0; r < rails.Length; r++)
				{
					(float x0, float x1, int bunches) = rails[r];
					d.Add(StructureShapes.Beam(new Vector3(x0, railY, railZ), new Vector3(x1, railY, railZ), 0.045f, 0.045f, StructureMaterial.Timber, 8060 + r * 4, StructureRole.Detail));
					foreach (float x in new[] { x0 + 0.1f, x1 - 0.1f })
					{
						d.Add(StructureShapes.Beam(new Vector3(x, railY + 0.02f, railZ), new Vector3(x, railY + 0.4f, front - 0.02f), 0.025f, 0.025f, StructureMaterial.Iron,
							8061 + r * 4 + (x > x0 + 0.2f ? 1 : 0), StructureRole.Detail));
					}
					for (int i = 0; i < bunches; i++)
					{
						float x = Mathf.Lerp(x0 + 0.3f, x1 - 0.3f, (i + 0.5f) / bunches) + d.Range(8070 + r * 10 + i, 1, -0.1f, 0.1f);
						TradeHerbBunch(d, new Vector3(x, railY - 0.02f, railZ), d.Range(8070 + r * 10 + i, 2, 0.38f, 0.5f), 8070 + r * 10 + i, null, 5, 1.3f);
					}
				}
			}
		}

		/// <summary>
		/// A hospice: a stone hall under a plank roof, one long ward warmed from the gable chimney, its front shaded by a pentice
		/// on posts the length of it (the covered walk of a cloister, where the sick could sit out of the rain), the walk
		/// flagged, a stone bench along the wall, and a painted board with a sprig of herbs hung from the pentice by the door.
		/// </summary>
		private static void Hospice(StructureDraft d)
		{
			var h = new House
			{
				Width = 16f, Depth = 9f, WallHeight = 3.2f, Rise = 2f, Overhang = 0.35f, Wall = 0.5f, Plinth = 0.3f,
				WallMaterial = StructureMaterial.Stone, RoofMaterial = StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, RidgeAlongX = true, Chimney = true, Windows = 4,
				DoorWidth = 1.5f, DoorHeight = 2.5f, DoorX = 0f, DoorKind = StructureDoorKind.Studded, RoomKind = StructureRoomKind.Infirmary, Key = 1,
			};
			BuildHouse(d, h);
			float front = -h.Depth * 0.5f, halfW = h.Width * 0.5f;
			// The pentice: from under the main eaves down to a plate on posts 2.1 m out, the posts clear of the door.
			const float depth = 2.1f, high = 3.08f, low = 2.5f;
			float postZ = front - depth;
			using (d.Builds(StepRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.125f, front - depth * 0.5f - 0.1f), new Vector3(h.Width + 0.4f, 0.35f, depth + 0.4f), StructureMaterial.Stone, 8000));
			}
			var posts = new[] { -halfW + 0.4f, -5f, -2.3f, 2.3f, 5f, halfW - 0.4f };
			for (int i = 0; i < posts.Length; i++)
			{
				d.Add(StructureShapes.Beam(new Vector3(posts[i], -0.3f, postZ), new Vector3(posts[i], low, postZ), 0.2f, 0.2f, StructureMaterial.Timber, 8010 + i));
				if (d.Mid)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(posts[i], 0.12f, postZ), new Vector3(0.38f, 0.16f, 0.38f), d.Bevel(0.03f), StructureMaterial.Stone, 8020 + i, StructureRole.Detail));
					for (int s = -1; s <= 1; s += 2)
					{
						if ((i == 0 && s < 0) || (i == posts.Length - 1 && s > 0))
						{
							continue;
						}
						d.Add(StructureShapes.Beam(new Vector3(posts[i], 1.95f, postZ), new Vector3(posts[i] + s * 0.55f, low - 0.02f, postZ), 0.11f, 0.11f, StructureMaterial.Timber,
							8030 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
			d.Add(StructureShapes.Beam(new Vector3(-halfW - 0.1f, low + 0.08f, postZ), new Vector3(halfW + 0.1f, low + 0.08f, postZ), 0.2f, 0.18f, StructureMaterial.Timber, 8040));
			// Its planks, a shelter pitched as its posts have it (not a roof its palette re-pitches).
			float run = depth + 0.25f, drop = high - low;
			float tilt = Mathf.Atan2(drop, run) * Mathf.Rad2Deg;
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Block(new Vector3(0f, (high + low) * 0.5f + 0.12f, front - run * 0.5f), new Vector3(h.Width + 0.3f, 0.08f, Mathf.Sqrt(run * run + drop * drop)),
					Quaternion.Euler(-tilt, 0f, 0f), StructureMaterial.Timber, 8041, StructureRole.Roof)).Grain = Vector3.forward;
			}
			if (!d.Mid)
			{
				return;
			}
			// The wall plate the planks rest on at the wall, and a stone bench along the wall left of the door.
			d.Add(StructureShapes.Beam(new Vector3(-halfW, high + 0.02f, front - 0.08f), new Vector3(halfW, high + 0.02f, front - 0.08f), 0.16f, 0.16f, StructureMaterial.Timber, 8042, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(-4.3f, 0.47f, front - 0.3f), new Vector3(3.2f, 0.08f, 0.42f), StructureMaterial.Stone, 8043, StructureRole.Detail));
			foreach (float x in new[] { -5.6f, -3f })
			{
				d.Add(StructureShapes.Box(new Vector3(x, 0.24f, front - 0.3f), new Vector3(0.2f, 0.4f, 0.36f), StructureMaterial.Stone, 8044 + (x > -4f ? 1 : 0), StructureRole.Detail));
			}
			// The sign: a board with a sprig of three leaves, hung on two rods from the plate beside the door.
			var board = new Vector3(1.35f, 2.06f, postZ - 0.02f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(board + new Vector3(s * 0.3f, 0.26f, 0f), new Vector3(board.x + s * 0.3f, low - 0.01f, board.z), 0.02f, 0.02f, StructureMaterial.Iron,
					8046 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			TradeSprigBoard(d, board, 8050);
		}

		// ── Bakery ───────────────────────────────────────────────────

		/// <summary>
		/// A bakehouse: the house kit's (its long side to the street, the chimney on its right gable, the hearth inside against
		/// its breast), its domed oven built out beyond the breast with a flue of its own; the one front window a shop window,
		/// its lower shutter let down on brackets as the stall-board with loaves on it and its upper propped up as the awning;
		/// flour sacks by the door.
		/// </summary>
		private static void Bakery(StructureDraft d)
		{
			bool stone = d.Variant == 1;
			var h = new House
			{
				Width = 10f, Depth = 8f, WallHeight = 2.8f, Rise = 2f, Overhang = 0.5f, Wall = stone ? 0.45f : 0.25f, Plinth = 0.4f,
				WallMaterial = stone ? StructureMaterial.Stone : StructureMaterial.Plaster, RoofMaterial = stone ? StructureMaterial.Thatch : StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = !stone, RidgeAlongX = true, Chimney = true, Windows = 1,
				DoorWidth = 1.2f, DoorHeight = 2.3f, DoorX = -2.2f, DoorKind = StructureDoorKind.Planks, RoomKind = StructureRoomKind.Bakery, Key = 1,
			};
			BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f;

			// The oven, beyond the chimney breast on the right gable (ChimneyStack's breast stands 0.87 m out of the wall's line).
			float breast = h.Width * 0.5f - h.Wall * 0.5f + 0.87f;
			float ox = breast + 1.15f;
			d.Add(StructureShapes.ChamferBox(new Vector3(breast + 1.1f, 0.2f, 0f), new Vector3(2.4f, 1.4f, 2.5f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 8000));
			var dome = new[] { new Vector2(0f, 0.88f), new Vector2(1.05f, 0.88f), new Vector2(1.02f, 1.2f), new Vector2(0.85f, 1.55f), new Vector2(0.5f, 1.82f), new Vector2(0f, 1.9f) };
			d.Add(StructureShapes.Lathe(dome, d.Seg(12, 6), stone ? StructureMaterial.Fieldstone : StructureMaterial.Earth, 8001).Transform(Matrix4x4.Translate(new Vector3(ox, 0f, 0f))));
			float flueTop = stone ? 3.45f : 2.7f;
			var flue = new Vector3(ox - 0.45f, 0f, 0.3f);
			d.Add(StructureShapes.Cylinder(0.17f, 1.5f, flueTop, d.Seg(8, 5), StructureMaterial.Fieldstone, 8002).Transform(Matrix4x4.Translate(flue)));
			d.Emit(flue + Vector3.up * (flueTop + 0.1f), SceneEmitterKind.Smoke);
			if (stone)
			{
				// Its pentice: planks from the stack's face over the breast's weathering down to a plate on two posts beyond the oven.
				float x0 = breast - 0.35f, x1 = breast + 2.6f, high = 3.1f, low = 2.35f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(x1 - 0.2f, -0.3f, s * 1.25f), new Vector3(x1 - 0.2f, low, s * 1.25f), 0.14f, 0.14f, StructureMaterial.Timber, 8003 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Beam(new Vector3(x1 - 0.2f, low + 0.06f, -1.45f), new Vector3(x1 - 0.2f, low + 0.06f, 1.45f), 0.14f, 0.14f, StructureMaterial.Timber, 8005));
				float run = x1 - x0, drop = high - low;
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Block(new Vector3((x0 + x1) * 0.5f, (high + low) * 0.5f + 0.15f, 0f), new Vector3(Mathf.Sqrt(run * run + drop * drop), 0.08f, 3.1f),
						Quaternion.Euler(0f, 0f, -Mathf.Atan2(drop, run) * Mathf.Rad2Deg), StructureMaterial.Timber, 8006, StructureRole.Roof)).Grain = Vector3.right;
				}
			}
			if (!d.Mid)
			{
				return;
			}
			// The ash pit's door under the oven, and faggots of brushwood stacked behind it.
			d.Add(StructureShapes.Box(new Vector3(ox, 0.42f, -1.27f), new Vector3(0.5f, 0.38f, 0.05f), StructureMaterial.Ash, 8010, StructureRole.Detail));
			if (d.Fine)
			{
				for (int i = 0; i < 3; i++)
				{
					float y = 0.14f + (i == 2 ? 0.26f : 0f), z = 1.55f + (i == 2 ? 0.15f : i * 0.3f);
					Log(d, new Vector3(ox - 0.75f, y, z), new Vector3(ox + 0.75f, y, z), 0.14f, 8011 + i, 0f, StructureMaterial.Timber, 6, StructureRole.Detail);
				}
			}

			// The stall-board, under the one front window (BuildHouse sets one window in the middle of the front, jittered by its own keys).
			float wx = (d.Rand(h.Key + 600, 0) - 0.5f) * 0.5f;
			float sill = floor + h.DoorHeight * 0.62f + (d.Rand(h.Key + 601, 0) - 0.5f) * 0.16f;
			float wb = sill - WindowHeight * 0.5f, wt = sill + WindowHeight * 0.5f;
			const float stall = 1.5f, reach = 0.58f, prop = 20f;
			d.Add(StructureShapes.Box(new Vector3(wx, wb - 0.12f, front - 0.04f - reach * 0.5f), new Vector3(stall, 0.06f, reach), StructureMaterial.Timber, 8020, StructureRole.Detail)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(wx + s * 0.6f, wb - 0.62f, front - 0.02f), new Vector3(wx + s * 0.6f, wb - 0.16f, front - reach + 0.05f), 0.06f, 0.06f,
					StructureMaterial.Timber, 8021 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			// The upper shutter, hinged over the window and propped out on iron stays.
			var hinge = new Vector3(wx, wt + 0.22f, front - 0.03f);
			float c = Mathf.Cos(prop * Mathf.Deg2Rad), sn = Mathf.Sin(prop * Mathf.Deg2Rad);
			d.Add(StructureShapes.Block(hinge + new Vector3(0f, sn * 0.31f, -c * 0.31f), new Vector3(stall, 0.05f, 0.62f), Quaternion.Euler(prop, 0f, 0f), StructureMaterial.Timber, 8023,
				StructureRole.Detail)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(wx + s * 0.65f, wt - 0.1f, front - 0.02f), new Vector3(wx + s * 0.65f, hinge.y + sn * 0.58f - 0.03f, hinge.z - c * 0.58f), 0.025f, 0.025f,
					StructureMaterial.Iron, 8024 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			// The loaves on the board: long and round.
			int loaves = d.Fine ? 5 : 3;
			for (int i = 0; i < loaves; i++)
			{
				float x = wx + Mathf.Lerp(-0.55f, 0.55f, i / (loaves - 1f));
				bool round = i % 2 == 1;
				d.Add(StructureShapes.Sphere(new Vector3(x, wb - 0.09f + 0.06f, front - 0.04f - reach * 0.5f + d.Range(8030 + i, 1, -0.08f, 0.08f)),
					round ? new Vector3(0.1f, 0.07f, 0.1f) : new Vector3(0.14f, 0.07f, 0.08f), 6, 3, StructureMaterial.Thatch, 8030 + i, StructureRole.Detail));
			}
			// Flour sacks against the wall left of the door, clear of its step.
			float doorLeft = h.DoorX - h.DoorWidth * 0.5f;
			int sacks = d.Fine ? 3 : 2;
			for (int i = 0; i < sacks; i++)
			{
				var at = new Vector3(doorLeft - 0.75f - (i == 1 ? 0.55f : 0f) - (i == 2 ? 0.28f : 0f), 0.34f + (i == 2 ? 0.52f : 0f), front - 0.42f);
				d.Add(StructureShapes.Sphere(at, new Vector3(0.28f, 0.36f, 0.25f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 8040 + i, StructureRole.Detail));
			}
		}

		// ── Workshop ─────────────────────────────────────────────────

		/// <summary>The workshop's frame: its width along the street, its depth, the eaves' height, and where its store's front wall stands.</summary>
		private const float TradeShopWidth = 8f, TradeShopDepth = 7f, TradeShopEave = 3f, TradeShopStoreFront = -0.4f, TradeShopFloor = 0.15f, TradeShopDoorX = 1.8f;

		/// <summary>
		/// A craftsman's shop as the smithy is, open to the street under its roof on posts, with a walled store behind and its
		/// door from the shop floor (the room the interior furnishes): v0 a joiner's, plank-walled and plank-roofed, a sawhorse,
		/// a pole lathe whose spring pole reaches up under the roof, a stack of planks; v1 a cooper's, its store framed and its
		/// roof thatched, staves stooked to season, hoops, finished barrels and one being fired over a cresset to bend its staves.
		/// The way from the street to the store's door is kept clear.
		/// </summary>
		private static void Workshop(StructureDraft d)
		{
			bool cooper = d.Variant == 1;
			float halfW = TradeShopWidth * 0.5f, halfD = TradeShopDepth * 0.5f;
			const float wall = 0.2f, doorWidth = 1.1f, doorHeight = 2.3f;
			StructureMaterial walls = cooper ? StructureMaterial.Plaster : StructureMaterial.Timber;
			// The floor: a fieldstone platform one low step up from the street.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (TradeShopFloor - FoundationDepth) * 0.5f, 0f), new Vector3(TradeShopWidth + 0.2f, TradeShopFloor + FoundationDepth, TradeShopDepth + 0.2f),
					d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
			}
			// The store behind: four walls, the door in the one facing the shop floor.
			float storeDepth = halfD - TradeShopStoreFront, storeZ = (halfD + TradeShopStoreFront) * 0.5f;
			using (d.Builds(WallRoles))
			{
				int first = d.Solids.Count;
				WallsWithDoor(d, new Vector3(0f, 0f, storeZ), TradeShopWidth, storeDepth, wall, TradeShopFloor, TradeShopEave, TradeShopDoorX, doorWidth, TradeShopFloor - 0.01f, TradeShopFloor + doorHeight, walls, 10);
				if (!cooper)
				{
					for (int i = first; i < d.Solids.Count; i++)
					{
						d.Solids[i].Grain = Vector3.up;
					}
				}
			}
			HangDoor(d, new Vector3(TradeShopDoorX, TradeShopFloor, TradeShopStoreFront + wall - 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight, StructureDoorKind.Planks);
			d.Room(new Vector3(0f, TradeShopFloor, storeZ), new Vector2(TradeShopWidth - 2f * wall, storeDepth - 2f * wall), TradeShopEave - TradeShopFloor, StructureRoomKind.Workshop);
			if (cooper && d.Mid)
			{
				int first = d.Solids.Count;
				Framing(d, TradeShopWidth, storeDepth, TradeShopFloor, TradeShopEave, 20,
					new List<WallOpening> { new WallOpening { Along = TradeShopDoorX + halfW, Width = doorWidth, Bottom = TradeShopFloor - 0.01f, Top = TradeShopFloor + doorHeight } });
				TransformSince(d, first, Matrix4x4.Translate(new Vector3(0f, 0f, storeZ)));
			}
			// The open front: posts at the corners and the middle, a plate over them, ties back to the store.
			float postZ = -halfD + 0.15f, plate = TradeShopEave - 0.12f;
			foreach (float x in new[] { -halfW + 0.15f, 0f, halfW - 0.15f })
			{
				d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, postZ), new Vector3(x, TradeShopEave, postZ), 0.22f, 0.22f, StructureMaterial.Timber, 30 + (int)(x + 5f)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-halfW - 0.05f, plate, postZ), new Vector3(halfW + 0.05f, plate, postZ), 0.22f, 0.25f, StructureMaterial.Timber, 40));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * (halfW - 0.15f), plate, postZ), new Vector3(s * (halfW - 0.15f), plate, TradeShopStoreFront), 0.2f, 0.22f, StructureMaterial.Timber, 41 + (s > 0 ? 1 : 0)));
			}
			if (d.Mid)
			{
				foreach ((float x, float s) in new[] { (-halfW + 0.15f, 1f), (0f, -1f), (0f, 1f), (halfW - 0.15f, -1f) })
				{
					d.Add(StructureShapes.Beam(new Vector3(x, plate - 0.75f, postZ), new Vector3(x + s * 0.65f, plate - 0.08f, postZ), 0.12f, 0.12f, StructureMaterial.Timber,
						45 + (int)(x + 5f) * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			// The roof at its palette's pitch (or a flat canopy), ridge along the street, a gable at each end over the ties.
			Matrix4x4 place = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			StructureMaterial roofMaterial = cooper ? StructureMaterial.Thatch : StructureMaterial.Timber;
			OwnRoofPlan roof = OwnGable(d, TradeShopDepth, TradeShopWidth, TradeShopEave, cooper ? 4f : 3.3f, 0.45f, cooper ? 0.3f : 0.12f, roofMaterial, 50, place);
			if (!roof.Flat)
			{
				using (d.Builds(WallRoles))
				{
					for (int s = -1; s <= 1; s += 2)
					{
						Gable(d, TradeShopDepth - 0.02f, TradeShopEave, roof.Rise - 0.04f, s * (halfW - 0.1f), wall, walls, 55 + (s > 0 ? 1 : 0), place);
					}
				}
			}
			if (cooper)
			{
				CooperYard(d);
			}
			else
			{
				JoinerYard(d);
			}
		}

		/// <summary>The joiner's shop floor: a sawhorse with a plank on it, a pole lathe along the store's wall, planks stacked by the right post.</summary>
		private static void JoinerYard(StructureDraft d)
		{
			const float f = TradeShopFloor;
			// The sawhorse: a beam on splayed legs, a plank across it.
			var horse = new Vector3(-2.3f, f, -2.35f);
			d.Add(StructureShapes.Beam(horse + new Vector3(-0.7f, 0.72f, 0f), horse + new Vector3(0.7f, 0.72f, 0f), 0.12f, 0.12f, StructureMaterial.Timber, 100));
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(horse + new Vector3(sx * 0.62f, 0.7f, 0f), horse + new Vector3(sx * 0.68f, 0f, sz * 0.32f), 0.07f, 0.07f, StructureMaterial.Timber,
						101 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			d.Add(StructureShapes.Block(horse + new Vector3(0.1f, 0.81f, 0.05f), new Vector3(2.4f, 0.05f, 0.3f), Quaternion.Euler(0f, 8f, 0f), StructureMaterial.Timber, 105, StructureRole.Detail)).Grain = Vector3.right;
			// The pole lathe: two puppets on a bed, the work between them, the spring pole from the floor by the wall up under
			// the roof and its cord down to the treadle.
			float lz = TradeShopStoreFront - 0.55f;
			d.Add(StructureShapes.Beam(new Vector3(-3.6f, f + 0.85f, lz), new Vector3(-1.5f, f + 0.85f, lz), 0.14f, 0.16f, StructureMaterial.Timber, 110));
			foreach (float x in new[] { -3.45f, -1.65f })
			{
				d.Add(StructureShapes.Beam(new Vector3(x, f, lz), new Vector3(x, f + 1.15f, lz), 0.12f, 0.12f, StructureMaterial.Timber, 111 + (x > -2f ? 1 : 0)));
			}
			if (d.Mid)
			{
				foreach (float x in new[] { -3.05f, -2.25f })
				{
					d.Add(StructureShapes.Box(new Vector3(x, f + 1.08f, lz), new Vector3(0.1f, 0.3f, 0.14f), StructureMaterial.Timber, 113 + (x > -2.5f ? 1 : 0), StructureRole.Detail));
				}
				Log(d, new Vector3(-3f, f + 1.12f, lz), new Vector3(-2.3f, f + 1.12f, lz), 0.05f, 115, 0f, StructureMaterial.Timber, 6, StructureRole.Detail);
				Log(d, new Vector3(-3.85f, f + 0.1f, TradeShopStoreFront - 0.15f), new Vector3(-1.2f, TradeShopEave - 0.35f, lz - 0.35f), 0.045f, 116, 0f, StructureMaterial.Timber, 5, StructureRole.Detail);
				d.Add(StructureShapes.Box(new Vector3(-2.65f, f + 0.03f, lz - 0.75f), new Vector3(0.22f, 0.05f, 0.9f), StructureMaterial.Timber, 117, StructureRole.Detail));
				if (d.Fine)
				{
					d.Add(StructureShapes.Beam(new Vector3(-1.25f, TradeShopEave - 0.4f, lz - 0.33f), new Vector3(-2.65f, f + 0.06f, lz - 1.05f), 0.015f, 0.015f, StructureMaterial.Canvas, 118, StructureRole.Detail));
					// Shavings under the work.
					d.Add(StructureShapes.Sphere(new Vector3(-2.65f, f, lz - 0.1f), new Vector3(0.45f, 0.06f, 0.3f), 6, 2, StructureMaterial.Thatch, 119, StructureRole.Detail));
				}
			}
			// Planks stacked on bearers by the right post, clear of the way to the door.
			const float px = 3.3f;
			for (int i = 0; i < 2; i++)
			{
				d.Add(StructureShapes.Box(new Vector3(px, f + 0.06f, -2.9f + i * 1.9f), new Vector3(0.75f, 0.12f, 0.12f), StructureMaterial.Timber, 120 + i));
			}
			int layers = d.Mid ? 5 : 3;
			for (int l = 0; l < layers; l++)
			{
				d.Add(StructureShapes.Box(new Vector3(px + d.Range(122 + l, 1, -0.04f, 0.04f), f + 0.16f + l * 0.06f, -1.95f), new Vector3(0.62f, 0.05f, 2.3f),
					StructureMaterial.Timber, 122 + l)).Grain = Vector3.forward;
			}
			if (!d.Mid)
			{
				return;
			}
			// The sign, a board with a saw on it hung under the front plate; the tools racked on the store's wall left of its door.
			var sign = new Vector3(-1.9f, TradeShopEave - 0.62f, -TradeShopDepth * 0.5f + 0.15f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(sign + new Vector3(s * 0.28f, 0.2f, 0f), new Vector3(sign.x + s * 0.28f, TradeShopEave - 0.24f, sign.z), 0.02f, 0.02f, StructureMaterial.Iron,
					130 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(sign, new Vector3(0.75f, 0.4f, 0.05f), StructureMaterial.Timber, 132, StructureRole.Detail)).Grain = Vector3.right;
			d.Add(StructureShapes.Block(sign + new Vector3(0.06f, 0f, -0.035f), new Vector3(0.48f, 0.12f, 0.012f), Quaternion.Euler(0f, 0f, -8f), StructureMaterial.Iron, 133, StructureRole.Detail));
			d.Add(StructureShapes.Box(sign + new Vector3(-0.24f, 0.03f, -0.04f), new Vector3(0.1f, 0.13f, 0.03f), StructureMaterial.Timber, 134, StructureRole.Detail));
			float rackZ = TradeShopStoreFront - 0.04f;
			d.Add(StructureShapes.Box(new Vector3(-0.6f, f + 1.75f, rackZ), new Vector3(1.4f, 0.08f, 0.05f), StructureMaterial.Timber, 135, StructureRole.Detail)).Grain = Vector3.right;
			if (d.Fine)
			{
				// Two saws, an axe and a mallet hung from its pegs.
				for (int i = 0; i < 2; i++)
				{
					float x = -1.1f + i * 0.3f;
					d.Add(StructureShapes.Box(new Vector3(x, f + 1.38f, rackZ - 0.03f), new Vector3(0.12f, 0.6f, 0.012f), StructureMaterial.Iron, 136 + i * 2, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(x, f + 1.72f, rackZ - 0.04f), new Vector3(0.12f, 0.1f, 0.03f), StructureMaterial.Timber, 137 + i * 2, StructureRole.Detail));
				}
				d.Add(StructureShapes.Beam(new Vector3(-0.35f, f + 1.72f, rackZ - 0.04f), new Vector3(-0.35f, f + 1.15f, rackZ - 0.04f), 0.035f, 0.035f, StructureMaterial.Timber, 140, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(-0.3f, f + 1.2f, rackZ - 0.04f), new Vector3(0.16f, 0.12f, 0.02f), StructureMaterial.Iron, 141, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-0.05f, f + 1.72f, rackZ - 0.05f), new Vector3(-0.05f, f + 1.4f, rackZ - 0.05f), 0.03f, 0.03f, StructureMaterial.Timber, 142, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(-0.05f, f + 1.36f, rackZ - 0.05f), new Vector3(0.18f, 0.1f, 0.1f), StructureMaterial.Timber, 143, StructureRole.Detail));
			}
		}

		/// <summary>The cooper's shop floor: a barrel raised in its staves fired over a cresset, staves stooked to season, hoops on a post, finished barrels.</summary>
		private static void CooperYard(StructureDraft d)
		{
			const float f = TradeShopFloor;
			// The barrel being raised: its staves splayed out from the trussing hoop, the cresset's fire inside to bend them.
			var raise = new Vector3(-2.2f, f, -2.2f);
			int seg = d.Seg(12, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.42f, 0f), new Vector2(0.3f, 0.88f), new Vector2(0.26f, 0.88f), new Vector2(0.38f, 0f) }, seg, StructureMaterial.Timber, 200,
				true, 0f, true, true).Transform(Matrix4x4.Translate(raise)));
			if (d.Mid)
			{
				d.Add(TradeRing(0.29f, 0.06f, 0.025f, seg, StructureMaterial.Iron, 201).Transform(Matrix4x4.Translate(raise + Vector3.up * 0.76f)));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.06f, 0f), new Vector2(0.18f, 0.32f), new Vector2(0.2f, 0.36f), new Vector2(0f, 0.36f) }, d.Seg(8, 5), StructureMaterial.Iron, 202,
					true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(raise)));
				d.Add(StructureShapes.Cylinder(0.17f, 0.35f, 0.38f, d.Seg(8, 5), StructureMaterial.Ash, 203, false, StructureRole.Detail).Transform(Matrix4x4.Translate(raise)));
			}
			d.Emit(raise + Vector3.up * 0.42f, SceneEmitterKind.Fire);
			// Staves stooked to season by the left post.
			var stook = new Vector3(-3.25f, f, -1.25f);
			int staves = d.Fine ? 8 : d.Mid ? 5 : 3;
			for (int i = 0; i < staves; i++)
			{
				float a = (i + 0.5f) * 360f / staves;
				Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
				Vector3 foot = stook + dir * 0.32f;
				Vector3 head = stook + dir * 0.05f + Vector3.up * 0.95f;
				d.Add(StructureShapes.Beam(foot, head, 0.1f, 0.025f, StructureMaterial.Timber, 210 + i, i < 3 ? StructureRole.Structure : StructureRole.Detail));
			}
			// Hoops hung on the left front post.
			if (d.Mid)
			{
				for (int i = 0; i < 2; i++)
				{
					d.Add(TradeRing(0.3f + i * 0.04f, 0.04f, 0.018f, d.Seg(12, 8), StructureMaterial.Iron, 220 + i)
						.Transform(Matrix4x4.TRS(new Vector3(-TradeShopWidth * 0.5f + 0.15f, f + 1.55f - i * 0.08f, -TradeShopDepth * 0.5f + 0.15f - 0.13f - i * 0.03f), Quaternion.Euler(82f - i * 6f, 0f, 0f), Vector3.one)));
				}
			}
			// Finished barrels by the right post, one lying across two.
			const float bx = 3.25f;
			for (int i = 0; i < 3; i++)
			{
				d.Add(TradeBarrel(d, 230 + i).Transform(Matrix4x4.Translate(new Vector3(bx, f, -2.85f + i * 0.68f))));
			}
			if (d.Mid)
			{
				d.Add(TradeBarrel(d, 233).Transform(Matrix4x4.TRS(new Vector3(bx, f + 0.9f + 0.3f, -2.17f - 0.45f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
			// The cooper's block, a stump to work the heads on.
			d.Add(StructureShapes.Cylinder(0.3f, f, f + 0.55f, d.Seg(10, 6), StructureMaterial.Timber, 240, true).Transform(Matrix4x4.Translate(new Vector3(-0.9f, 0f, -1.4f))));
			if (d.Mid)
			{
				// The sign, a little barrel hung on its side from the front plate.
				var sign = new Vector3(-1.9f, TradeShopEave - 0.6f, -TradeShopDepth * 0.5f + 0.15f);
				const float scale = 0.45f;
				d.Add(TradeBarrel(d, 241).Transform(Matrix4x4.TRS(sign + new Vector3(0.45f * scale, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one * scale)));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(sign + new Vector3(s * 0.15f, 0.12f, 0f), new Vector3(sign.x + s * 0.15f, TradeShopEave - 0.24f, sign.z), 0.02f, 0.02f, StructureMaterial.Iron,
						242 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
		}

		// ── Dye works ────────────────────────────────────────────────

		/// <summary>
		/// A dyer's yard (v0) or a fuller's and weaver's (v1): coppers on fieldstone hearths over their own fires, steaming;
		/// cloth stretched on tenter frames and hung from a pole; a plank lean-to against the yard's back, its stores (v0) or
		/// the loom (v1) under it.
		/// </summary>
		private static void DyeWorks(StructureDraft d)
		{
			bool fuller = d.Variant == 1;
			if (!fuller)
			{
				TradeDyeVat(d, new Vector3(-3.3f, 0f, -2f), true, 100);
				TradeDyeVat(d, new Vector3(-1f, 0f, -2.6f), true, 120);
				// The rinsing tub, cold.
				var tub = new Vector3(-3.55f, 0f, 0.2f);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.55f, -0.1f), new Vector2(0.64f, 0.68f) }, d.Seg(10, 6), StructureMaterial.Timber, 140, true, 0f, false, true).Transform(Matrix4x4.Translate(tub)));
				d.Add(StructureShapes.Cylinder(0.6f, 0.66f, 0.7f, d.Seg(10, 6), StructureMaterial.Dyed, 141, false, StructureRole.Detail).Transform(Matrix4x4.Translate(tub)));
				// Cloth hung from a pole to dry.
				TradeClothPole(d, 1.2f, 4.6f, -2.6f, 3, 150);
				TradeTenter(d, 0.4f, 4.6f, 3.2f, StructureMaterial.Dyed, 170);
				TradeTenter(d, 0.8f, 4.6f, 1.4f, StructureMaterial.Dyed, 180);
				TradeLeanTo(d, -4.9f, -1.4f, 1.6f, 3.7f, 2.2f, 2.75f, 190);
				if (d.Mid)
				{
					// Barrels of mordant and the woad, balled, under the lean-to.
					d.Add(TradeBarrel(d, 200).Transform(Matrix4x4.Translate(new Vector3(-4.25f, 0f, 3.05f))));
					d.Add(TradeBarrel(d, 201).Transform(Matrix4x4.Translate(new Vector3(-3.55f, 0f, 3.1f))));
					for (int i = 0; i < 3; i++)
					{
						var at = new Vector3(-2.4f + (i == 2 ? 0.2f : i * 0.42f), 0.18f + (i == 2 ? 0.3f : 0f), 3f);
						d.Add(StructureShapes.Sphere(at, new Vector3(0.2f, 0.19f, 0.2f), 5, 3, StructureMaterial.Earth, 202 + i, StructureRole.Detail));
					}
					// Wood for the fires.
					for (int i = 0; i < 3; i++)
					{
						float x = -4.75f + (i == 2 ? 0.15f : i * 0.3f), y = 0.13f + (i == 2 ? 0.24f : 0f);
						Log(d, new Vector3(x, y, -2.9f), new Vector3(x, y, -1.4f), 0.13f, 210 + i, 0f, StructureMaterial.Timber, 6, StructureRole.Detail);
					}
				}
				return;
			}
			// The fuller's and weaver's: one copper, the fulling trough, the loom under a longer lean-to.
			TradeDyeVat(d, new Vector3(-3.3f, 0f, -2.2f), true, 100);
			d.Add(StructureShapes.Box(new Vector3(-0.9f, 0.175f, -2.4f), new Vector3(2.2f, 0.95f, 0.85f), StructureMaterial.Timber, 130)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(-0.9f, 0.655f, -2.4f), new Vector3(2f, 0.02f, 0.65f), StructureMaterial.Ash, 131, StructureRole.Detail));
			TradeLeanTo(d, -4.9f, 0.4f, 1.3f, 3.7f, 2.3f, 2.85f, 190);
			TradeLoom(d, new Vector3(-2.4f, 0f, 2.55f), 240);
			TradeTenter(d, 1.1f, 4.6f, 3.2f, StructureMaterial.Canvas, 170);
			TradeTenter(d, 1.1f, 4.6f, 1.5f, StructureMaterial.Canvas, 180);
			TradeClothPole(d, 1.2f, 4.4f, -3f, 2, 150);
			// Bolts of the finished cloth on a pallet.
			d.Add(StructureShapes.Box(new Vector3(2.7f, 0.06f, -1.35f), new Vector3(1.5f, 0.12f, 0.95f), StructureMaterial.Timber, 260)).Grain = Vector3.right;
			int bolts = d.Mid ? 5 : 3;
			for (int i = 0; i < bolts; i++)
			{
				bool top = i >= 3;
				float z = top ? -1.5f + (i - 3) * 0.3f : -1.65f + i * 0.3f, y = top ? 0.53f : 0.27f;
				Log(d, new Vector3(2.05f, y, z), new Vector3(3.35f, y, z), 0.15f, 261 + i, 0f, i % 2 == 0 ? StructureMaterial.Dyed : StructureMaterial.Canvas, 8,
					top ? StructureRole.Detail : StructureRole.Structure);
			}
		}

		/// <summary>
		/// A treadle loom under the lean-to (its front toward −z): four posts and their rails, the warp from its beam at the back
		/// to the breast beam, the woven cloth rolled under it, the beater hung between the posts, the weaver's bench in front.
		/// </summary>
		private static void TradeLoom(StructureDraft d, Vector3 at, int key)
		{
			const float hx = 0.8f, hz = 0.65f, height = 1.6f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(at + new Vector3(sx * hx, 0f, sz * hz), at + new Vector3(sx * hx, height, sz * hz), 0.09f, 0.09f, StructureMaterial.Timber,
						key + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Beam(at + new Vector3(sx * hx, height - 0.05f, -hz), at + new Vector3(sx * hx, height - 0.05f, hz), 0.08f, 0.08f, StructureMaterial.Timber, key + 4 + (sx > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(at + new Vector3(sx * hx, 0.3f, -hz), at + new Vector3(sx * hx, 0.3f, hz), 0.07f, 0.07f, StructureMaterial.Timber, key + 6 + (sx > 0 ? 1 : 0)));
			}
			foreach (float z in new[] { -hz, hz })
			{
				Log(d, at + new Vector3(-hx, 0.85f, z), at + new Vector3(hx, 0.85f, z), 0.07f, key + 8 + (z > 0f ? 1 : 0), 0f, StructureMaterial.Timber, 6);
			}
			if (!d.Mid)
			{
				return;
			}
			d.Add(StructureShapes.Box(at + new Vector3(0f, 0.86f, 0f), new Vector3(2f * hx - 0.2f, 0.01f, 2f * hz - 0.05f), StructureMaterial.Canvas, key + 10, StructureRole.Detail));
			Log(d, at + new Vector3(-hx + 0.1f, 0.55f, -hz + 0.1f), at + new Vector3(hx - 0.1f, 0.55f, -hz + 0.1f), 0.13f, key + 11, 0f, StructureMaterial.Dyed, 8, StructureRole.Detail);
			// The beater: its swords hung from the top rails, the reed across at the warp.
			foreach (float x in new[] { -hx + 0.08f, hx - 0.08f })
			{
				d.Add(StructureShapes.Beam(at + new Vector3(x, height - 0.05f, -0.1f), at + new Vector3(x, 0.8f, -0.2f), 0.05f, 0.05f, StructureMaterial.Timber, key + 12 + (x > 0f ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Beam(at + new Vector3(-hx + 0.08f, 0.92f, -0.19f), at + new Vector3(hx - 0.08f, 0.92f, -0.19f), 0.06f, 0.08f, StructureMaterial.Timber, key + 14, StructureRole.Detail));
			// The bench.
			d.Add(StructureShapes.Box(at + new Vector3(0f, 0.48f, -hz - 0.5f), new Vector3(1.1f, 0.06f, 0.32f), StructureMaterial.Timber, key + 15, StructureRole.Detail));
			foreach (float x in new[] { -0.45f, 0.45f })
			{
				d.Add(StructureShapes.Box(at + new Vector3(x, 0.225f, -hz - 0.5f), new Vector3(0.07f, 0.45f, 0.26f), StructureMaterial.Timber, key + 16 + (x > 0f ? 1 : 0), StructureRole.Detail));
			}
			if (d.Fine)
			{
				// The treadles.
				foreach (float x in new[] { -0.15f, 0.15f })
				{
					d.Add(StructureShapes.Block(at + new Vector3(x, 0.1f, -0.2f), new Vector3(0.08f, 0.04f, 0.9f), Quaternion.Euler(-6f, 0f, 0f), StructureMaterial.Timber, key + 18 + (x > 0f ? 1 : 0), StructureRole.Detail));
				}
			}
		}

		// ── Pottery ──────────────────────────────────────────────────

		/// <summary>
		/// A potter's yard: v0 a bottle kiln, the brick hovel round its oven narrowing to the neck its smoke leaves by, banded
		/// with iron, its fire mouths round its foot glowing and its wicket bricked up dark for the firing; a stand of pots,
		/// saggars stacked, a heap of clay and the wood for the fires. v1 a clay updraft kiln (the medieval potter's) with its
		/// stoke-hole out in front, a kick wheel under a lean-to, and racks of pots drying.
		/// </summary>
		private static void PotteryKiln(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				UpdraftPottery(d);
				return;
			}
			var c = new Vector3(-1f, 0f, 0.6f);
			var bottle = new[]
			{
				new Vector2(0f, -0.3f), new Vector2(2.4f, -0.3f), new Vector2(2.4f, 0.5f), new Vector2(2.5f, 1.5f), new Vector2(2.35f, 2.7f), new Vector2(1.85f, 3.8f),
				new Vector2(1.15f, 4.8f), new Vector2(0.78f, 5.6f), new Vector2(0.72f, 6.8f), new Vector2(0.86f, 6.9f), new Vector2(0.86f, 7.1f),
			};
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Lathe(bottle, d.Seg(16, 8), StructureMaterial.Stone, 1).Transform(Matrix4x4.Translate(c)));
			}
			d.Add(StructureShapes.Cylinder(0.62f, 7.09f, 7.13f, d.Seg(10, 6), StructureMaterial.Ash, 2, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c)));
			d.Emit(c + Vector3.up * 7.5f, SceneEmitterKind.Smoke);
			// The wicket, bricked up dark for the firing, under its lintel.
			d.Add(StructureShapes.Box(c + new Vector3(0f, 0.85f, -2.45f), new Vector3(0.9f, 1.7f, 0.12f), StructureMaterial.Ash, 3, StructureRole.Detail));
			// The fire mouths round its foot (the two in front at every level but the last, all four at the first).
			var mouths = new[] { 220f, 320f, 140f, 40f };
			for (int i = 0; i < mouths.Length; i++)
			{
				if (!d.Mid || (!d.Fine && i >= 2))
				{
					break;
				}
				float a = mouths[i] * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				d.Add(StructureShapes.Block(c + dir * 2.42f + Vector3.up * 0.3f, new Vector3(0.5f, 0.45f, 0.12f), Quaternion.LookRotation(dir), StructureMaterial.Ash, 4 + i, StructureRole.Detail));
			}
			float front = 220f * Mathf.Deg2Rad;
			d.Emit(c + new Vector3(Mathf.Cos(front), 0f, Mathf.Sin(front)) * 2.25f + Vector3.up * 0.3f, SceneEmitterKind.Forge);
			if (d.Mid)
			{
				d.Add(StructureShapes.ChamferBox(c + new Vector3(0f, 1.82f, -2.42f), new Vector3(1.2f, 0.22f, 0.22f), d.Bevel(0.02f), StructureMaterial.Fieldstone, 9, StructureRole.Detail));
			}
			if (d.Fine)
			{
				// The iron bands round its belly.
				foreach ((float y, float r) in new[] { (1.25f, 2.48f), (2.4f, 2.4f) })
				{
					d.Add(TradeRing(r, 0.08f, 0.05f, 12, StructureMaterial.Iron, 10 + (y > 2f ? 1 : 0)).Transform(Matrix4x4.Translate(c + Vector3.up * y)));
				}
			}
			// A stand of pots: two boards on four posts.
			TradePotStand(d, new Vector3(3.2f, 0f, -1.2f), 2f, 2, 20);
			// Saggars stacked, the clay heap, wood for the fires behind the kiln.
			int saggars = d.Mid ? 4 : 2;
			for (int i = 0; i < saggars; i++)
			{
				d.Add(StructureShapes.Cylinder(0.3f, i * 0.25f, i * 0.25f + 0.24f, d.Seg(8, 6), StructureMaterial.Fieldstone, 60 + i).Transform(Matrix4x4.Translate(new Vector3(3.4f, 0f, 1.6f))));
			}
			d.Add(StructureShapes.Sphere(new Vector3(3.1f, 0f, 3f), new Vector3(0.9f, 0.5f, 0.7f), d.Seg(8, 6), 4, StructureMaterial.Earth, 70));
			if (d.Mid)
			{
				for (int i = 0; i < 5; i++)
				{
					float z = 3.55f + (i >= 3 ? 0.13f : (i - 1) * 0.27f), y = 0.13f + (i >= 3 ? 0.24f : 0f), x = i == 4 ? 0.3f : 0f;
					Log(d, new Vector3(-2.6f + x, y, z), new Vector3(-0.6f + x, y, z), 0.13f, 80 + i, 0f, StructureMaterial.Timber, 6, StructureRole.Detail);
				}
			}
		}

		/// <summary>The updraft potter's yard (PotteryKiln v1).</summary>
		private static void UpdraftPottery(StructureDraft d)
		{
			var c = new Vector3(-1.6f, 0f, 0.8f);
			var kiln = new[] { new Vector2(0f, -0.3f), new Vector2(1.7f, -0.3f), new Vector2(1.75f, 0.5f), new Vector2(1.55f, 1.3f), new Vector2(1f, 1.95f), new Vector2(0.42f, 2.2f), new Vector2(0.42f, 2.45f) };
			d.Add(StructureShapes.Lathe(kiln, d.Seg(14, 8), StructureMaterial.Earth, 1).Transform(Matrix4x4.Translate(c)));
			d.Add(StructureShapes.Cylinder(0.3f, 2.44f, 2.47f, d.Seg(8, 5), StructureMaterial.Ash, 2, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c)));
			d.Emit(c + Vector3.up * 2.8f, SceneEmitterKind.Smoke);
			// The stoke-hole: a fieldstone arch out in front, its fire glowing.
			d.Add(StructureShapes.ChamferBox(c + new Vector3(0f, 0.175f, -1.85f), new Vector3(1.1f, 0.95f, 1f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 3));
			d.Add(StructureShapes.Box(c + new Vector3(0f, 0.3f, -2.36f), new Vector3(0.5f, 0.42f, 0.04f), StructureMaterial.Ash, 4, StructureRole.Detail));
			d.Emit(c + new Vector3(0f, 0.3f, -2.2f), SceneEmitterKind.Forge);
			// The wheel's lean-to, and the kick wheel under it with a pot on its head and the potter's seat behind.
			TradeLeanTo(d, 1f, 3.8f, 0.6f, 2.9f, 2.1f, 2.6f, 10);
			var wheel = new Vector3(2.4f, 0f, 1.5f);
			d.Add(StructureShapes.Cylinder(0.42f, 0.06f, 0.16f, d.Seg(12, 6), StructureMaterial.Timber, 30).Transform(Matrix4x4.Translate(wheel)));
			d.Add(StructureShapes.Cylinder(0.04f, 0.16f, 0.78f, d.Seg(6, 4), StructureMaterial.Iron, 31).Transform(Matrix4x4.Translate(wheel)));
			d.Add(StructureShapes.Cylinder(0.22f, 0.78f, 0.83f, d.Seg(10, 6), StructureMaterial.Timber, 32).Transform(Matrix4x4.Translate(wheel)));
			d.Add(StructureShapes.Box(wheel + new Vector3(0f, 0.55f, 0.62f), new Vector3(0.9f, 0.05f, 0.36f), StructureMaterial.Timber, 33)).Grain = Vector3.right;
			foreach (float x in new[] { -0.38f, 0.38f })
			{
				d.Add(StructureShapes.Box(wheel + new Vector3(x, 0.265f, 0.62f), new Vector3(0.07f, 0.53f, 0.3f), StructureMaterial.Timber, 34 + (x > 0f ? 1 : 0)));
			}
			if (d.Fine)
			{
				TradePot(d, wheel + Vector3.up * 0.83f, 0.7f, 36, 8);
			}
			// Racks of pots drying in front of the lean-to, and the clay heap.
			TradePotStand(d, new Vector3(1.9f, 0f, -1.35f), 2f, 3, 40);
			d.Add(StructureShapes.Sphere(new Vector3(3.9f, 0f, -0.9f), new Vector3(0.7f, 0.45f, 0.6f), d.Seg(8, 6), 4, StructureMaterial.Earth, 70));
			if (d.Mid)
			{
				// Wood for the kiln by its stoke-hole.
				for (int i = 0; i < 4; i++)
				{
					float x = -3.45f + (i == 3 ? 0.13f : i * 0.27f), y = 0.13f + (i == 3 ? 0.24f : 0f);
					Log(d, new Vector3(x, y, -2.2f), new Vector3(x, y, -0.6f), 0.13f, 80 + i, 0f, StructureMaterial.Timber, 6, StructureRole.Detail);
				}
			}
		}

		// ── Brewhouse ────────────────────────────────────────────────

		/// <summary>
		/// A brewhouse: the house kit's (its long side to the street, a wide door to roll the barrels through), barrels stacked
		/// on their sides against the wall by the door, and the ale-stake, a pole out over the street with a bush tied to its end,
		/// the sign there was ale to be had. v0 framed plaster under planks, a louvre on the ridge letting out the copper's
		/// steam; v1 stone under thatch, the malt kiln built against the right gable behind its window: a stone tower under a
		/// steep cap and a timber cowl, its fire door glowing.
		/// </summary>
		private static void Brewhouse(StructureDraft d)
		{
			bool kiln = d.Variant == 1;
			var h = new House
			{
				Width = 12f, Depth = 8f, WallHeight = 3.2f, Rise = 2f, Overhang = 0.5f, Wall = kiln ? 0.45f : 0.25f, Plinth = 0.4f,
				WallMaterial = kiln ? StructureMaterial.Stone : StructureMaterial.Plaster, RoofMaterial = kiln ? StructureMaterial.Thatch : StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = !kiln, RidgeAlongX = true, Chimney = false, Windows = 3,
				DoorWidth = 1.6f, DoorHeight = 2.4f, DoorX = -2.6f, DoorKind = StructureDoorKind.Planks, RoomKind = StructureRoomKind.Storehouse, Key = 1,
			};
			HouseRoof roof = BuildHouse(d, h);
			float front = -h.Depth * 0.5f, halfW = h.Width * 0.5f;
			if (kiln)
			{
				// The malt kiln: a stone tower against the gable, back of its window, its cap a steep pyramid and the cowl on it.
				const float kx0 = 0f, kw = 3.2f, kz0 = 0.7f, kz1 = 4f, kTop = 5f;
				float kx = halfW + kx0 + kw * 0.5f, kz = (kz0 + kz1) * 0.5f;
				using (d.Builds(WallRoles))
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(kx, (kTop - FoundationDepth) * 0.5f, kz), new Vector3(kw, kTop + FoundationDepth, kz1 - kz0), d.Bevel(0.04f), StructureMaterial.Stone, 8000));
				}
				float r = Mathf.Max(kw, kz1 - kz0) * 0.5f * 1.414f + 0.3f;
				const float capTop = 8.1f;
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r, kTop), new Vector2(0f, capTop) }, 4, StructureMaterial.Timber, 8001, false, Mathf.PI * 0.25f, false, false, StructureRole.Roof)
						.Transform(Matrix4x4.Translate(new Vector3(kx, 0f, kz))));
				}
				// The cowl: a little boarded hood on the cap's peak, turned from the weather.
				d.Add(StructureShapes.Box(new Vector3(kx, capTop - 0.1f, kz), new Vector3(0.7f, 0.75f, 0.7f), StructureMaterial.Timber, 8002, StructureRole.Roof)).Grain = Vector3.up;
				d.Add(StructureShapes.Block(new Vector3(kx, capTop + 0.33f, kz + 0.05f), new Vector3(0.85f, 0.06f, 0.95f), Quaternion.Euler(-12f, 0f, 0f), StructureMaterial.Timber, 8003, StructureRole.Roof));
				d.Emit(new Vector3(kx, capTop + 0.55f, kz - 0.3f), SceneEmitterKind.Smoke);
				// The fire door in its front face, under a stone lintel.
				d.Add(StructureShapes.Box(new Vector3(kx, 0.5f, kz0 - 0.04f), new Vector3(0.8f, 1f, 0.08f), StructureMaterial.Ash, 8004, StructureRole.Detail));
				d.Emit(new Vector3(kx, 0.35f, kz0 - 0.2f), SceneEmitterKind.Forge);
				if (d.Mid)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(kx, 1.12f, kz0 - 0.08f), new Vector3(1.2f, 0.22f, 0.2f), d.Bevel(0.02f), StructureMaterial.Stone, 8005, StructureRole.Detail));
				}
			}
			else
			{
				// The louvre: a boarded turret astride the ridge, its sides slatted, under its own little gable; on a flat roof it
				// stands on the deck.
				const float lx = 1.6f, lw = 1.3f, ld = 1f, lh = 0.95f;
				float foot = roof.Flat ? roof.WallTop : roof.Ridge - 0.3f;
				d.Add(StructureShapes.Box(new Vector3(lx, foot + 0.12f, 0f), new Vector3(lw, 0.24f, ld), StructureMaterial.Timber, 8010, StructureRole.Roof));
				foreach ((float x, float z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
				{
					d.Add(StructureShapes.Box(new Vector3(lx + x * (lw * 0.5f - 0.05f), foot + 0.24f + lh * 0.5f, z * (ld * 0.5f - 0.05f)), new Vector3(0.1f, lh, 0.1f), StructureMaterial.Timber,
						8011 + (x > 0f ? 1 : 0) + (z > 0f ? 2 : 0), StructureRole.Roof));
				}
				d.Add(StructureShapes.Box(new Vector3(lx, foot + 0.24f + lh * 0.5f, 0f), new Vector3(lw - 0.16f, lh, ld - 0.16f), StructureMaterial.Ash, 8015, StructureRole.Roof));
				if (d.Mid)
				{
					// The slats across its long sides, angled to shed rain.
					int slats = d.Fine ? 4 : 2;
					for (int s = -1; s <= 1; s += 2)
					{
						for (int i = 0; i < slats; i++)
						{
							float y = foot + 0.24f + lh * (i + 0.5f) / slats;
							d.Add(StructureShapes.Block(new Vector3(lx, y, s * (ld * 0.5f - 0.06f)), new Vector3(lw - 0.2f, 0.03f, 0.18f), Quaternion.Euler(s * 35f, 0f, 0f),
								StructureMaterial.Timber, 8020 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
				}
				Matrix4x4 place = Matrix4x4.TRS(new Vector3(lx, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
				GableRoof(d, ld + 0.1f, lw + 0.1f, foot + 0.24f + lh, 0.45f, 0.15f, 0.06f, StructureMaterial.Timber, 8030, place);
				d.Emit(new Vector3(lx, foot + 0.24f + lh + 0.65f, 0f), SceneEmitterKind.Smoke);
			}
			if (!d.Mid)
			{
				return;
			}
			// The ale-stake: a pole out over the street from beside the door, braced, a bush tied to its end.
			float sx = h.DoorX + h.DoorWidth * 0.5f + 0.8f;
			var root = new Vector3(sx, h.Plinth + 2.6f, front - 0.02f);
			var tip = new Vector3(sx, h.Plinth + 2.85f, front - 1.85f);
			Log(d, root, tip, 0.055f, 8040, 0f, StructureMaterial.Timber, 6, StructureRole.Detail);
			d.Add(StructureShapes.Beam(new Vector3(sx, h.Plinth + 1.9f, front - 0.02f), Vector3.Lerp(root, tip, 0.5f), 0.04f, 0.04f, StructureMaterial.Iron, 8041, StructureRole.Detail));
			d.Add(StructureShapes.Sphere(tip + new Vector3(0f, -0.12f, -0.1f), new Vector3(0.36f, 0.33f, 0.38f), 7, 4, StructureMaterial.Thatch, 8042, StructureRole.Detail));
			if (d.Fine)
			{
				for (int i = 0; i < 5; i++)
				{
					Vector3 dir = Quaternion.Euler(d.Range(8043 + i, 1, -50f, 50f), d.Range(8043 + i, 2, 0f, 360f), 0f) * Vector3.forward;
					Vector3 from = tip + new Vector3(0f, -0.12f, -0.1f);
					d.Add(StructureShapes.Beam(from, from + dir * 0.5f, 0.02f, 0.02f, StructureMaterial.Timber, 8043 + i, StructureRole.Detail));
				}
			}
			// Barrels on their sides against the wall left of the door, three and two, ends to the street.
			float doorLeft = h.DoorX - h.DoorWidth * 0.5f;
			for (int i = 0; i < (d.Fine ? 5 : 3); i++)
			{
				bool upper = i >= 3;
				float x = doorLeft - 0.6f - (upper ? 0.32f + (i - 3) * 0.64f : i * 0.64f);
				float y = 0.32f + (upper ? 0.52f : 0f);
				d.Add(TradeBarrel(d, 8050 + i).Transform(Matrix4x4.TRS(new Vector3(x, y, front - 0.05f), Quaternion.Euler(-90f, 0f, 0f), Vector3.one)));
			}
		}

		// ── Furniture ────────────────────────────────────────────────

		/// <summary>
		/// A healer's shelves (v0): sides, a back and four boards of jars and crocks, a rod along the top hung with bunches of
		/// drying herbs, a mortar on the middle board; or (v1) a counter with a mortar and pestle, a balance and bottles on it,
		/// a shelf of jars on the wall over it. Back to the wall (+z), facing the room.
		/// </summary>
		private static void HerbShelf(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float width = 1.2f, depth = 0.35f, height = 1.9f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f - 0.03f), height * 0.5f, 0f), new Vector3(0.05f, height, depth), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
				}
				d.Add(StructureShapes.Box(new Vector3(0f, height * 0.5f, depth * 0.5f - 0.015f), new Vector3(width, height, 0.03f), StructureMaterial.Timber, 3)).Grain = Vector3.up;
				d.Add(StructureShapes.Box(new Vector3(0f, height - 0.02f, 0f), new Vector3(width, 0.04f, depth), StructureMaterial.Timber, 4)).Grain = Vector3.right;
				for (int i = 0; i < 4; i++)
				{
					float y = 0.12f + i * 0.44f;
					d.Add(StructureShapes.Box(new Vector3(0f, y, 0f), new Vector3(width - 0.06f, 0.03f, depth), StructureMaterial.Timber, 10 + i)).Grain = Vector3.right;
					if (!d.Mid || (i == 2 && d.Fine))
					{
						continue;
					}
					int perBoard = 2;
					for (int p = 0; p < perBoard; p++)
					{
						float x = Mathf.Lerp(-0.42f, 0.42f, p / (perBoard - 1f)) + d.Range(20 + i * 4 + p, 1, -0.04f, 0.04f);
						TradeJar(d, new Vector3(x, y + 0.015f, d.Range(20 + i * 4 + p, 2, -0.03f, 0.03f)), d.Range(20 + i * 4 + p, 3, 0.75f, 1.15f), (i + p) % 3 == 0, 20 + i * 4 + p, d.Fine ? 5 : 4);
					}
				}
				if (d.Fine)
				{
					// The middle board: a mortar with its pestle, and two jars.
					float y = 0.12f + 2 * 0.44f + 0.015f;
					TradeMortar(d, new Vector3(-0.2f, y, 0f), 0.55f, 40);
					TradeJar(d, new Vector3(0.25f, y, 0f), 1f, false, 44, 5);
					TradeJar(d, new Vector3(0.45f, y, 0.02f), 0.8f, true, 45, 5);
				}
				if (d.Mid)
				{
					// A rod across the top's front on two hooks, the herbs hung head down from it.
					d.Add(StructureShapes.Beam(new Vector3(-width * 0.5f, height - 0.1f, -depth * 0.5f - 0.06f), new Vector3(width * 0.5f, height - 0.1f, -depth * 0.5f - 0.06f), 0.03f, 0.03f,
						StructureMaterial.Timber, 50, StructureRole.Detail));
					int bunches = d.Fine ? 3 : 2;
					for (int i = 0; i < bunches; i++)
					{
						float x = Mathf.Lerp(-0.42f, 0.42f, i / (bunches - 1f));
						TradeHerbBunch(d, new Vector3(x, height - 0.12f, -depth * 0.5f - 0.06f), d.Range(51 + i, 1, 0.24f, 0.32f), 51 + i, null, 4);
					}
				}
				return;
			}
			// The counter.
			const float cw = 1.4f, cd = 0.6f, ch = 0.88f;
			d.Add(StructureShapes.Box(new Vector3(0f, (ch - 0.05f) * 0.5f, 0.02f), new Vector3(cw, ch - 0.05f, cd - 0.06f), StructureMaterial.Timber, 1)).Grain = Vector3.up;
			d.Add(StructureShapes.Box(new Vector3(0f, ch - 0.025f, 0f), new Vector3(cw + 0.08f, 0.05f, cd + 0.04f), StructureMaterial.Timber, 2)).Grain = Vector3.right;
			// The shelf on the wall over it, on two brackets.
			const float sy = 1.48f;
			d.Add(StructureShapes.Box(new Vector3(0f, sy, cd * 0.5f - 0.13f), new Vector3(cw - 0.1f, 0.04f, 0.24f), StructureMaterial.Timber, 3)).Grain = Vector3.right;
			if (!d.Mid)
			{
				return;
			}
			foreach (float x in new[] { -0.5f, 0.5f })
			{
				d.Add(StructureShapes.Beam(new Vector3(x, sy - 0.3f, cd * 0.5f - 0.02f), new Vector3(x, sy - 0.03f, cd * 0.5f - 0.2f), 0.04f, 0.04f, StructureMaterial.Timber, 4 + (x > 0f ? 1 : 0), StructureRole.Detail));
			}
			// The counter's front in panels.
			d.Add(StructureShapes.Box(new Vector3(0f, (ch - 0.05f) * 0.5f, -cd * 0.5f + 0.02f), new Vector3(cw - 0.12f, ch - 0.25f, 0.02f), StructureMaterial.Timber, 6, StructureRole.Detail)).Grain = Vector3.up;
			TradeMortar(d, new Vector3(-0.45f, ch, -0.05f), 0.6f, 10);
			// The balance: a post, its beam, two pans hung on cords.
			var post = new Vector3(0.1f, ch, 0.08f);
			d.Add(StructureShapes.Box(post + new Vector3(0f, 0.015f, 0f), new Vector3(0.16f, 0.03f, 0.12f), StructureMaterial.Timber, 14, StructureRole.Detail));
			d.Add(StructureShapes.Beam(post, post + Vector3.up * 0.48f, 0.025f, 0.025f, StructureMaterial.Iron, 15, StructureRole.Detail));
			d.Add(StructureShapes.Beam(post + new Vector3(-0.22f, 0.46f, 0f), post + new Vector3(0.22f, 0.46f, 0f), 0.02f, 0.02f, StructureMaterial.Iron, 16, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				var pan = post + new Vector3(s * 0.21f, 0.18f, 0f);
				d.Add(StructureShapes.Cylinder(0.08f, pan.y, pan.y + 0.025f, d.Seg(8, 5), StructureMaterial.Iron, 17 + (s > 0 ? 2 : 0), false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(pan.x, 0f, pan.z))));
				if (d.Fine)
				{
					d.Add(StructureShapes.Beam(pan + Vector3.up * 0.02f, post + new Vector3(s * 0.21f, 0.45f, 0f), 0.008f, 0.008f, StructureMaterial.Canvas, 18 + (s > 0 ? 2 : 0), StructureRole.Detail));
				}
			}
			// Bottles and a crock on the counter, jars on the shelf; a bundle of herbs laid by.
			int bottles = d.Fine ? 3 : 1;
			for (int i = 0; i < bottles; i++)
			{
				TradeBottle(d, new Vector3(0.42f + i * 0.13f, ch, -0.08f + (i % 2) * 0.12f), d.Range(30 + i, 1, 0.85f, 1.15f), 30 + i, d.Fine ? 6 : 5);
			}
			int jars = d.Fine ? 3 : 2;
			for (int i = 0; i < jars; i++)
			{
				TradeJar(d, new Vector3(Mathf.Lerp(-0.5f, 0.5f, i / (jars - 1f)), sy + 0.02f, cd * 0.5f - 0.13f), d.Range(36 + i, 1, 0.7f, 1f), i % 2 == 1, 36 + i, d.Fine ? 6 : 5);
			}
			if (d.Fine)
			{
				TradeHerbBunch(d, new Vector3(-0.1f, ch + 0.06f, -0.18f), 0.3f, 42, Quaternion.Euler(0f, 0f, 90f), 4);
			}
		}

		/// <summary>
		/// A craftsman's bench (v0): a joiner's, its heavy top on four legs and a shelf, a leg vice at its left end, its tools
		/// racked on a board behind; or (v1) a cooper's shaving horse, a stave clamped under its head, staves leant against the
		/// wall behind and a hoop. Back to the wall (+z), facing the room.
		/// </summary>
		private static void Workbench(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float top = 0.86f, length = 2f, depth = 0.62f;
				d.Add(StructureShapes.Box(new Vector3(0f, top - 0.05f, 0f), new Vector3(length, 0.1f, depth), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(sx * 0.82f, (top - 0.1f) * 0.5f, sz * 0.22f), new Vector3(0.1f, top - 0.1f, 0.1f), StructureMaterial.Timber,
							2 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
					}
				}
				// The tool board behind, rising off the top's back edge.
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.27f, depth * 0.5f - 0.02f), new Vector3(length - 0.1f, 0.54f, 0.03f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
				if (!d.Mid)
				{
					return;
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.2f, 0f), new Vector3(1.6f, 0.03f, 0.5f), StructureMaterial.Timber, 7, StructureRole.Detail)).Grain = Vector3.right;
				// The leg vice: its chop up the front of the left leg, the screw through it, the bar in the screw.
				d.Add(StructureShapes.Box(new Vector3(-0.82f, 0.48f, -depth * 0.5f - 0.04f), new Vector3(0.16f, 0.76f, 0.06f), StructureMaterial.Timber, 8, StructureRole.Detail)).Grain = Vector3.up;
				d.Add(StructureShapes.Beam(new Vector3(-0.82f, 0.7f, -depth * 0.5f - 0.2f), new Vector3(-0.82f, 0.7f, -0.15f), 0.06f, 0.06f, StructureMaterial.Timber, 9, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-1.02f, 0.7f, -depth * 0.5f - 0.18f), new Vector3(-0.62f, 0.7f, -depth * 0.5f - 0.18f), 0.03f, 0.03f, StructureMaterial.Timber, 10, StructureRole.Detail));
				// A plane on the top.
				d.Add(StructureShapes.Box(new Vector3(0.35f, top + 0.035f, -0.08f), new Vector3(0.3f, 0.07f, 0.08f), StructureMaterial.Timber, 11, StructureRole.Detail)).Grain = Vector3.right;
				if (!d.Fine)
				{
					return;
				}
				// The tools on the board: a saw, a mallet, chisels; shavings by the plane.
				float face = depth * 0.5f - 0.045f;
				d.Add(StructureShapes.Box(new Vector3(-0.45f, top + 0.3f, face), new Vector3(0.6f, 0.16f, 0.01f), StructureMaterial.Iron, 12, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(-0.1f, top + 0.33f, face - 0.01f), new Vector3(0.13f, 0.12f, 0.03f), StructureMaterial.Timber, 13, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0.25f, top + 0.42f, face - 0.03f), new Vector3(0.16f, 0.09f, 0.09f), StructureMaterial.Timber, 14, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(0.25f, top + 0.12f, face - 0.03f), new Vector3(0.25f, top + 0.38f, face - 0.03f), 0.03f, 0.03f, StructureMaterial.Timber, 15, StructureRole.Detail));
				for (int i = 0; i < 3; i++)
				{
					float x = 0.5f + i * 0.1f;
					d.Add(StructureShapes.Beam(new Vector3(x, top + 0.14f, face), new Vector3(x, top + 0.3f, face), 0.015f, 0.008f, StructureMaterial.Iron, 16 + i * 2, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(x, top + 0.3f, face), new Vector3(x, top + 0.42f, face), 0.03f, 0.03f, StructureMaterial.Timber, 17 + i * 2, StructureRole.Detail));
				}
				d.Add(StructureShapes.Sphere(new Vector3(0.62f, top + 0.01f, -0.1f), new Vector3(0.14f, 0.025f, 0.1f), 6, 2, StructureMaterial.Thatch, 24, StructureRole.Detail));
				return;
			}
			// The shaving horse: a plank on splayed legs, its bridge rising to the head that clamps the stave.
			const float seat = 0.5f;
			d.Add(StructureShapes.Box(new Vector3(0f, seat, -0.05f), new Vector3(1.6f, 0.07f, 0.3f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			foreach ((float x, float z) in new[] { (-0.7f, -0.18f), (-0.7f, 0.08f), (0.7f, -0.18f), (0.7f, 0.08f) })
			{
				d.Add(StructureShapes.Beam(new Vector3(x * 0.95f, seat, z * 0.5f - 0.05f), new Vector3(x * 1.05f, 0.03f, z - 0.05f), 0.06f, 0.06f, StructureMaterial.Timber,
					2 + (x > 0f ? 2 : 0) + (z > 0f ? 1 : 0)));
			}
			d.Add(StructureShapes.Block(new Vector3(0.35f, seat + 0.13f, -0.05f), new Vector3(0.75f, 0.06f, 0.22f), Quaternion.Euler(0f, 0f, 16f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0.65f, seat + 0.1f, -0.05f), new Vector3(0.12f, 0.16f, 0.2f), StructureMaterial.Timber, 7));
			// The head on its arm through the plank, the foot bar under it.
			d.Add(StructureShapes.Beam(new Vector3(0.15f, 0.18f, -0.05f), new Vector3(0.15f, 0.98f, -0.05f), 0.08f, 0.06f, StructureMaterial.Timber, 8));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0.15f, 0.98f, -0.05f), new Vector3(0.12f, 0.14f, 0.34f), StructureMaterial.Timber, 9, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(0.15f, 0.18f, -0.3f), new Vector3(0.15f, 0.18f, 0.2f), 0.05f, 0.05f, StructureMaterial.Timber, 10, StructureRole.Detail));
			}
			// Staves leant against the wall behind.
			int staves = d.Fine ? 6 : d.Mid ? 4 : 2;
			for (int i = 0; i < staves; i++)
			{
				float x = -0.7f + i * 0.22f;
				d.Add(StructureShapes.Block(new Vector3(x, 0.5f, 0.3f), new Vector3(0.1f, 1f, 0.025f), Quaternion.Euler(-10f, d.Range(30 + i, 1, -6f, 6f), 0f), StructureMaterial.Timber, 30 + i,
					i < 2 ? StructureRole.Structure : StructureRole.Detail)).Grain = Vector3.up;
			}
			if (!d.Fine)
			{
				return;
			}
			// A stave on the bridge under the head, the drawknife on it, a hoop against the wall.
			d.Add(StructureShapes.Block(new Vector3(0.38f, seat + 0.19f, -0.05f), new Vector3(0.9f, 0.025f, 0.1f), Quaternion.Euler(0f, 0f, 16f), StructureMaterial.Timber, 40, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(0.45f, seat + 0.26f, -0.22f), new Vector3(0.45f, seat + 0.26f, 0.12f), 0.02f, 0.05f, StructureMaterial.Iron, 41, StructureRole.Detail));
			d.Add(TradeRing(0.3f, 0.04f, 0.018f, 12, StructureMaterial.Iron, 42).Transform(Matrix4x4.TRS(new Vector3(0.7f, 0.32f, 0.28f), Quaternion.Euler(80f, 0f, 0f), Vector3.one)));
		}

		// ── Shared parts ─────────────────────────────────────────────

		/// <summary>A bunch of herbs hung head down to dry, its stems tied at <paramref name="top"/>, <paramref name="length"/> long.</summary>
		private static void TradeHerbBunch(StructureDraft d, Vector3 top, float length, int key, Quaternion? turn = null, int segments = 5, float girth = 1f)
		{
			float y0 = -length;
			var profile = new[]
			{
				new Vector2(0f, y0), new Vector2(0.1f * girth, y0 + 0.09f), new Vector2(0.075f * girth, y0 + length * 0.55f), new Vector2(0.025f, -0.03f), new Vector2(0.025f, 0f),
			};
			d.Add(StructureShapes.Lathe(profile, segments, StructureMaterial.Thatch, key, false, d.Rand(key, 3) * 1.2f, false, true, StructureRole.Detail)
				.Transform(Matrix4x4.TRS(top, turn ?? Quaternion.identity, Vector3.one)));
		}

		/// <summary>
		/// The apothecary's sign: a carved wooden mortar with its pestle standing in it, hung on two chains from
		/// <paramref name="hook"/> (the end of a bracket's arm).
		/// </summary>
		private static void TradeMortarSign(StructureDraft d, Vector3 hook, int key)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(hook + new Vector3(0f, -0.04f, 0f), hook + new Vector3(s * 0.2f, -0.3f, 0f), 0.02f, 0.02f, StructureMaterial.Iron, key + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			float rim = hook.y - 0.3f;
			var mortar = new[] { new Vector2(0f, rim - 0.4f), new Vector2(0.16f, rim - 0.4f), new Vector2(0.25f, rim - 0.27f), new Vector2(0.28f, rim) };
			d.Add(StructureShapes.Lathe(mortar, d.Seg(10, 6), StructureMaterial.Timber, key + 2, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(hook.x, 0f, hook.z))));
			d.Add(StructureShapes.Cylinder(0.23f, rim, rim + 0.01f, d.Seg(10, 6), StructureMaterial.Ash, key + 3, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(hook.x, 0f, hook.z))));
			d.Add(StructureShapes.Beam(hook + new Vector3(-0.05f, -0.42f, 0f), hook + new Vector3(0.14f, 0.06f, 0.05f), 0.08f, 0.08f, StructureMaterial.Timber, key + 4, StructureRole.Detail));
		}

		/// <summary>
		/// A painted board facing −z with a sprig of three herb leaves on it, centred at <paramref name="centre"/>: a healer's
		/// sign (leaves, not a cross).
		/// </summary>
		private static void TradeSprigBoard(StructureDraft d, Vector3 centre, int key)
		{
			d.Add(StructureShapes.Box(centre, new Vector3(0.8f, 0.52f, 0.05f), StructureMaterial.Timber, key, StructureRole.Detail)).Grain = Vector3.right;
			var stem = centre + new Vector3(0f, -0.2f, 0f);
			d.Add(StructureShapes.Beam(stem + new Vector3(0f, 0f, -0.035f), stem + new Vector3(0f, 0.18f, -0.035f), 0.022f, 0.015f, StructureMaterial.Dyed, key + 1, StructureRole.Detail));
			for (int i = -1; i <= 1; i++)
			{
				float angle = -i * 50f * Mathf.Deg2Rad, length = i == 0 ? 0.24f : 0.19f, width = i == 0 ? 0.07f : 0.06f;
				var leaf = new[]
				{
					new Vector2(0f, 0f), new Vector2(width * 0.6f, length * 0.15f), new Vector2(width, length * 0.45f), new Vector2(0f, length),
					new Vector2(-width, length * 0.45f), new Vector2(-width * 0.6f, length * 0.15f),
				};
				Vector2 foot = new Vector2(0f, i == 0 ? 0.17f : 0.09f);
				for (int p = 0; p < leaf.Length; p++)
				{
					Vector2 q = leaf[p];
					leaf[p] = foot + new Vector2(q.x * Mathf.Cos(angle) - q.y * Mathf.Sin(angle), q.x * Mathf.Sin(angle) + q.y * Mathf.Cos(angle));
				}
				d.Add(StructureShapes.Prism(leaf, -0.045f, -0.02f, StructureMaterial.Dyed, key + 2 + (i + 1), StructureRole.Detail).Transform(Matrix4x4.Translate(stem)));
			}
		}

		/// <summary>A barrel standing on its foot at the origin (0.9 m tall), staves along its height.</summary>
		private static StructureSolid TradeBarrel(StructureDraft d, int key)
		{
			var profile = new[] { new Vector2(0.27f, 0f), new Vector2(0.32f, 0.45f), new Vector2(0.27f, 0.9f) };
			return StructureShapes.Lathe(profile, d.Seg(10, 6), StructureMaterial.Timber, key, true, 0f, false, true);
		}

		/// <summary>A flat ring about the y axis at the origin (a hoop, a band): <paramref name="radius"/> inside, <paramref name="width"/> tall, <paramref name="thick"/> thick.</summary>
		private static StructureSolid TradeRing(float radius, float width, float thick, int segments, StructureMaterial material, int key)
		{
			var profile = new[] { new Vector2(radius + thick, 0f), new Vector2(radius + thick, width), new Vector2(radius, width), new Vector2(radius, 0f) };
			return StructureShapes.Lathe(profile, segments, material, key, true, 0f, true, false, StructureRole.Detail);
		}

		/// <summary>A thrown pot on its foot at <paramref name="at"/>, round-bellied, <paramref name="scale"/> × 0.35 m tall.</summary>
		private static void TradePot(StructureDraft d, Vector3 at, float scale, int key, int segments)
		{
			var profile = new[] { new Vector2(0.11f * scale, 0f), new Vector2(0.17f * scale, 0.14f * scale), new Vector2(0.1f * scale, 0.3f * scale), new Vector2(0.11f * scale, 0.35f * scale) };
			d.Add(StructureShapes.Lathe(profile, segments, StructureMaterial.Earth, key, true, d.Rand(key, 1), false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>A jar or crock on a shelf: tall and narrow-necked, or squat.</summary>
		private static void TradeJar(StructureDraft d, Vector3 at, float scale, bool squat, int key, int segments)
		{
			Vector2[] profile = squat
				? new[] { new Vector2(0.07f * scale, 0f), new Vector2(0.085f * scale, 0.07f * scale), new Vector2(0.06f * scale, 0.13f * scale) }
				: new[] { new Vector2(0.05f * scale, 0f), new Vector2(0.065f * scale, 0.12f * scale), new Vector2(0.035f * scale, 0.22f * scale), new Vector2(0.04f * scale, 0.25f * scale) };
			d.Add(StructureShapes.Lathe(profile, segments, StructureMaterial.Earth, key, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>A stoneware bottle: a round body and a long neck.</summary>
		private static void TradeBottle(StructureDraft d, Vector3 at, float scale, int key, int segments)
		{
			var profile = new[] { new Vector2(0.05f * scale, 0f), new Vector2(0.06f * scale, 0.09f * scale), new Vector2(0.02f * scale, 0.16f * scale), new Vector2(0.02f * scale, 0.22f * scale) };
			d.Add(StructureShapes.Lathe(profile, segments, StructureMaterial.Earth, key, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>A stone mortar on its foot at <paramref name="at"/> with its pestle in it, <paramref name="scale"/> × 0.3 m across.</summary>
		private static void TradeMortar(StructureDraft d, Vector3 at, float scale, int key)
		{
			var profile = new[] { new Vector2(0f, 0f), new Vector2(0.11f * scale, 0f), new Vector2(0.15f * scale, 0.12f * scale), new Vector2(0.16f * scale, 0.2f * scale) };
			d.Add(StructureShapes.Lathe(profile, d.Seg(8, 5), StructureMaterial.Stone, key, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
			d.Add(StructureShapes.Beam(at + new Vector3(0f, 0.08f * scale, 0f), at + new Vector3(0.08f * scale, 0.42f * scale, 0.03f * scale), 0.04f * scale, 0.04f * scale,
				StructureMaterial.Stone, key + 1, StructureRole.Detail));
		}

		/// <summary>
		/// A dyer's copper on its fieldstone hearth: the iron vessel set into the hearth's top, its liquor dyed, the stoke-hole
		/// in the hearth's front; a hot one steams and its fire glows, a paddle left in it.
		/// </summary>
		private static void TradeDyeVat(StructureDraft d, Vector3 at, bool hot, int key)
		{
			int seg = d.Seg(10, 6);
			Matrix4x4 to = Matrix4x4.Translate(at);
			d.Add(StructureShapes.Cylinder(0.9f, -0.3f, 0.7f, seg, StructureMaterial.Fieldstone, key).Transform(to));
			d.Add(StructureShapes.Cylinder(0.72f, 0.7f, 1f, seg, StructureMaterial.Iron, key + 1).Transform(to));
			d.Add(StructureShapes.Cylinder(0.66f, 1f, 1.025f, seg, StructureMaterial.Dyed, key + 2, false, StructureRole.Detail).Transform(to));
			if (d.Mid)
			{
				d.Add(TradeRing(0.68f, 0.07f, 0.07f, seg, StructureMaterial.Iron, key + 3).Transform(Matrix4x4.Translate(at + Vector3.up * 0.99f)));
				d.Add(StructureShapes.Box(at + new Vector3(0f, 0.22f, -0.87f), new Vector3(0.42f, 0.38f, 0.1f), StructureMaterial.Ash, key + 4, StructureRole.Detail));
			}
			if (d.Fine)
			{
				d.Add(StructureShapes.Beam(at + new Vector3(0.2f, 0.85f, 0.1f), at + new Vector3(0.5f, 2.05f, 0.45f), 0.05f, 0.05f, StructureMaterial.Timber, key + 5, StructureRole.Detail));
			}
			if (hot)
			{
				d.Emit(at + Vector3.up * 1.35f, SceneEmitterKind.Smoke);
				d.Emit(at + new Vector3(0f, 0.22f, -0.75f), SceneEmitterKind.Forge);
			}
		}

		/// <summary>
		/// A tenter frame along x at <paramref name="z"/>: two posts, a rail along the top and one along the foot, the cloth
		/// stretched between them on its hooks.
		/// </summary>
		private static void TradeTenter(StructureDraft d, float x0, float x1, float z, StructureMaterial cloth, int key)
		{
			foreach (float x in new[] { x0, x1 })
			{
				d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, z), new Vector3(x, 2.05f, z), 0.1f, 0.1f, StructureMaterial.Timber, key + (x > x0 ? 1 : 0)));
			}
			foreach (float y in new[] { 1.95f, 0.4f })
			{
				d.Add(StructureShapes.Beam(new Vector3(x0 - 0.08f, y, z), new Vector3(x1 + 0.08f, y, z), 0.07f, 0.08f, StructureMaterial.Timber, key + 2 + (y > 1f ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3((x0 + x1) * 0.5f, 1.175f, z - 0.05f), new Vector3(x1 - x0 - 0.2f, 1.45f, 0.02f), cloth, key + 4, StructureRole.Detail)).Grain = Vector3.right;
		}

		/// <summary>A pole on two posts along x at <paramref name="z"/> with lengths of dyed cloth hung over it to dry.</summary>
		private static void TradeClothPole(StructureDraft d, float x0, float x1, float z, int cloths, int key)
		{
			foreach (float x in new[] { x0, x1 })
			{
				Log(d, new Vector3(x, -0.3f, z), new Vector3(x, 3f, z), 0.07f, key + (x > x0 ? 1 : 0), 0f, StructureMaterial.Timber, 6);
			}
			Log(d, new Vector3(x0 - 0.15f, 2.9f, z), new Vector3(x1 + 0.15f, 2.9f, z), 0.05f, key + 2, 0f, StructureMaterial.Timber, 6);
			if (!d.Mid)
			{
				return;
			}
			for (int i = 0; i < cloths; i++)
			{
				float x = Mathf.Lerp(x0, x1, (i + 0.5f) / cloths);
				float drop = d.Range(key + 3 + i, 1, 1.6f, 2f);
				d.Add(StructureShapes.Block(new Vector3(x, 2.88f - drop * 0.5f, z), new Vector3(0.75f, drop, 0.02f), Quaternion.Euler(0f, d.Range(key + 3 + i, 2, -8f, 8f), 0f),
					StructureMaterial.Dyed, key + 3 + i, StructureRole.Detail)).Grain = Vector3.up;
			}
		}

		/// <summary>
		/// A plank lean-to from <paramref name="x0"/> to <paramref name="x1"/>, open to the front (<paramref name="z0"/>), its
		/// back (<paramref name="z1"/>) boarded: posts, plates, and planks falling from <paramref name="high"/> at the back to
		/// <paramref name="low"/> at the front (a shelter pitched by its posts, not a roof its palette re-pitches).
		/// </summary>
		private static void TradeLeanTo(StructureDraft d, float x0, float x1, float z0, float z1, float low, float high, int key)
		{
			int n = 0;
			foreach ((float z, float h) in new[] { (z0 + 0.1f, low), (z1 - 0.15f, high) })
			{
				foreach (float x in new[] { x0 + 0.1f, x1 - 0.1f })
				{
					d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, z), new Vector3(x, h, z), 0.14f, 0.14f, StructureMaterial.Timber, key + n++));
				}
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(x0, h - 0.06f, z), new Vector3(x1, h - 0.06f, z), 0.12f, 0.12f, StructureMaterial.Timber, key + n++, StructureRole.Detail));
				}
			}
			float run = z1 - z0 + 0.35f, drop = high - low;
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Block(new Vector3((x0 + x1) * 0.5f, (high + low) * 0.5f + 0.04f, (z0 + z1) * 0.5f - 0.05f), new Vector3(x1 - x0 + 0.3f, 0.07f, Mathf.Sqrt(run * run + drop * drop)),
					Quaternion.Euler(-Mathf.Atan2(drop, run) * Mathf.Rad2Deg, 0f, 0f), StructureMaterial.Timber, key + 10, StructureRole.Roof)).Grain = Vector3.forward;
			}
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Box(new Vector3((x0 + x1) * 0.5f, (high - 0.3f) * 0.5f, z1 - 0.06f), new Vector3(x1 - x0, high + 0.3f, 0.08f), StructureMaterial.Timber, key + 11)).Grain = Vector3.up;
			}
		}

		/// <summary>
		/// A stand of pots along x centred on <paramref name="at"/>, <paramref name="length"/> long: posts and
		/// <paramref name="boards"/> boards, pots along each (fewer at the coarser levels).
		/// </summary>
		private static void TradePotStand(StructureDraft d, Vector3 at, float length, int boards, int key)
		{
			float hx = length * 0.5f - 0.08f;
			float top = 0.4f + (boards - 1) * 0.45f + 0.05f;
			foreach ((float x, float z) in new[] { (-hx, -0.2f), (hx, -0.2f), (-hx, 0.2f), (hx, 0.2f) })
			{
				d.Add(StructureShapes.Box(at + new Vector3(x, top * 0.5f, z), new Vector3(0.07f, top, 0.07f), StructureMaterial.Timber, key + (x > 0f ? 2 : 0) + (z > 0f ? 1 : 0)));
			}
			for (int b = 0; b < boards; b++)
			{
				float y = 0.4f + b * 0.45f;
				d.Add(StructureShapes.Box(at + new Vector3(0f, y, 0f), new Vector3(length, 0.04f, 0.5f), StructureMaterial.Timber, key + 4 + b)).Grain = Vector3.right;
				if (!d.Mid)
				{
					continue;
				}
				int pots = d.Fine ? (b == boards - 1 ? 3 : 4) : 2;
				for (int p = 0; p < pots; p++)
				{
					float x = Mathf.Lerp(-hx + 0.22f, hx - 0.22f, pots == 1 ? 0.5f : p / (pots - 1f)) + d.Range(key + 10 + b * 6 + p, 1, -0.05f, 0.05f);
					TradePot(d, at + new Vector3(x, y + 0.02f, d.Range(key + 10 + b * 6 + p, 2, -0.06f, 0.06f)), d.Range(key + 10 + b * 6 + p, 3, 0.8f, 1.15f), key + 10 + b * 6 + p, d.Fine ? 6 : 5);
				}
			}
		}
	}
}
#endif
