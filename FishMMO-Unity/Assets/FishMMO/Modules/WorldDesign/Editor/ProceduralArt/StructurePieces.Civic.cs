#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The town's other buildings (2026-10-19: "more structure types for towns, villages, and cities"): the town's own: market hall, shop-houses, warehouse, stocks and pillory, market cross.
	/// </summary>
	public static partial class StructurePieces
	{
		private static void AddCivic(List<StructurePiece> list)
		{
			Piece(list, "MarketHall", StructureStyle.Stone, StructureSize.Building,
				"The town's market and guild hall, 16×10 m: v0 an open arcade of stone piers (stalls and people pass under it) carrying a jettied, timber-framed council hall reached by a stone forestair, a bell turret and weather-vane on the ridge, the town's banner on its front; v1 a stone guildhall, a tall-windowed hall behind a studded door, a lead-capped clock cupola on the ridge.",
				MarketHall, "building", "town", "guildhall");
			Piece(list, "ShopHouse", StructureStyle.Timber, StructureSize.Building,
				"A shop-house for a burgage plot, 7×10 m, its gable to the street: the shop's wide front with its stall-board let down as a counter and the shutter over it propped up as a canopy, an iron sign bracket, the family's jettied storey over it up a stair; v0 timber-framed under planks, v1 a stone shop under a framed storey and thatch.",
				ShopHouse, "building", "town", "shop");
			Piece(list, "Warehouse", StructureStyle.Stone, StructureSize.Building,
				"A merchant's warehouse, 10×16 m, gable-fronted: a cart door, a loft door high in the gable under a hooded hoist beam with its pulley, rope and a sack on the hook, small barred windows; v0 stone, v1 timber-boarded on posts.",
				Warehouse, "building", "town", "warehouse");
			Piece(list, "Pillory", StructureStyle.Timber, StructureSize.Small,
				"Justice on the square: v0 the stocks (a bench and its leg board) beside a whipping post with its shackles; v1 a pillory's head-and-hands board on a post, on a railed timber platform up a flight of steps.",
				Pillory, "town", "pillory");
			Piece(list, "MarketCross", StructureStyle.Stone, StructureSize.Medium,
				"The square's centrepiece, 5.6 m across: v0 a stone market cross, a tall octagonal shaft on four broad octagonal steps to sit on, its carved lantern head gabled and pinnacled; v1 a fountain, an octagonal stone basin of dark water round a pillar with spouts and a bowl.",
				MarketCross, "town", "marketcross", "fountain");
		}

		// ── Shared parts ──────────────────────────────────────────────

		/// <summary>
		/// A roof a civic building builds for itself, pitched, at the rise its palette asked (<see cref="OwnRoof"/>'s plan): the
		/// gable roof, its overhang drawn in by as much as a steeper roof's bargeboards lean further out (as BuildHouse does, so a
		/// re-pitched building stands on the kit's footprint), the bargeboards and a ridge beam. Ridge along z in the roof's frame.
		/// </summary>
		private static void CivicPitched(StructureDraft d, OwnRoofPlan plan, float span, float length, float eaveY, float kitRise, float overhang,
			StructureMaterial material, int key, Matrix4x4 place)
		{
			float half = span * 0.5f;
			bool soft = plan.Repitched ? plan.Soft : material == StructureMaterial.Thatch;
			if (plan.Repitched)
			{
				overhang -= Mathf.Max(0f, BargeboardDepth * 0.5f * (Mathf.Sin(Mathf.Atan2(plan.Rise, half)) - Mathf.Sin(Mathf.Atan2(kitRise, half))));
			}
			GableRoof(d, span, length, eaveY, plan.Rise, overhang, soft ? 0.32f : 0.14f, material, key, place, plan.Grain);
			Bargeboards(d, span, length, eaveY, plan.Rise, overhang, material, key + 20, place);
			if (d.Fine)
			{
				using (d.Builds(RoofRoles))
				{
					var r0 = new Vector3(0f, eaveY + plan.Rise + 0.1f, -length * 0.5f - overhang);
					var r1 = new Vector3(0f, eaveY + plan.Rise + 0.1f, length * 0.5f + overhang);
					d.Add(StructureShapes.Beam(place.MultiplyPoint3x4(r0), place.MultiplyPoint3x4(r1), 0.22f, 0.22f,
						material == StructureMaterial.Thatch ? StructureMaterial.Thatch : StructureMaterial.Timber, key + 40, StructureRole.Roof));
				}
			}
		}

		/// <summary>
		/// A civic building's own gable roof as its palette has it: <see cref="CivicPitched"/> at the kit's rise or the palette's,
		/// or a flat canopy over the walls. Returns the plan, so what stands on the roof (a turret, a cupola, the gables) moves with it.
		/// </summary>
		private static OwnRoofPlan CivicRoof(StructureDraft d, float span, float length, float eaveY, float kitRise, float overhang,
			StructureMaterial material, int key, Matrix4x4 place)
		{
			OwnRoofPlan plan = OwnRoof(d, material, kitRise, span * 0.5f);
			if (plan.Flat)
			{
				FlatCanopy(d, span, length, eaveY, overhang, 0.14f, material, key, place);
			}
			else
			{
				CivicPitched(d, plan, span, length, eaveY, kitRise, overhang, material, key, place);
			}
			return plan;
		}

		/// <summary>
		/// A window larger than a cottage's (<see cref="Window(StructureDraft, Vector3, Vector3, float, int)"/>), in a wall whose
		/// outer face is at <paramref name="at"/> facing <paramref name="outward"/>: the pane halfway through the wall (the hole is
		/// the wall's, cut by WallRun), and from the middle level a sill, a hood over the head to throw the rain off, the mullion
		/// splitting it into two lights and, in a tall one, a transom.
		/// </summary>
		private static void CivicWindow(StructureDraft d, Vector3 at, Vector3 outward, float wall, float width, float height, StructureMaterial trim, bool transom, int key)
		{
			Quaternion turn = Quaternion.LookRotation(-outward, Vector3.up);
			d.Add(StructureShapes.Block(at - outward * (wall * 0.5f), new Vector3(width, height, 0.04f), turn, StructureMaterial.Glass, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			d.Add(StructureShapes.Block(at + outward * 0.06f + Vector3.down * (height * 0.5f + 0.05f), new Vector3(width + 0.24f, 0.1f, 0.18f), turn, trim, key + 1, StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.05f + Vector3.up * (height * 0.5f + 0.08f), new Vector3(width + 0.32f, 0.14f, 0.14f), turn, trim, key + 2, StructureRole.Detail));
			d.Add(StructureShapes.Block(at - outward * (wall * 0.5f - 0.04f), new Vector3(0.08f, height, 0.1f), turn, trim, key + 3, StructureRole.Detail));
			if (transom)
			{
				d.Add(StructureShapes.Block(at - outward * (wall * 0.5f - 0.04f) + Vector3.up * (height * 0.22f), new Vector3(width, 0.08f, 0.1f), turn, trim, key + 4, StructureRole.Detail));
			}
		}

		/// <summary>A barrel standing at <paramref name="at"/>, bellied, 0.9 m.</summary>
		private static void CivicBarrel(StructureDraft d, Vector3 at, int key)
		{
			var profile = new List<Vector2> { new Vector2(0.27f, 0f), new Vector2(0.32f, 0.45f), new Vector2(0.27f, 0.9f) };
			d.Add(StructureShapes.Lathe(profile, d.Seg(10, 6), StructureMaterial.Timber, key, true, 0f, false, true, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>A weather-vane on an iron rod from <paramref name="foot"/>: a ball, the vane's pennant, and at level 0 the four arms of the compass.</summary>
		private static void CivicVane(StructureDraft d, Vector3 foot, int key)
		{
			d.Add(StructureShapes.Beam(foot, foot + Vector3.up * 1.25f, 0.05f, 0.05f, StructureMaterial.Iron, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			d.Add(StructureShapes.Sphere(foot + Vector3.up * 0.55f, new Vector3(0.09f, 0.09f, 0.09f), 6, 4, StructureMaterial.Iron, key + 1, StructureRole.Detail));
			// The pennant swung a little off the front, as the wind left it.
			d.Add(StructureShapes.Block(foot + new Vector3(0.24f, 1.1f, 0.06f), new Vector3(0.5f, 0.24f, 0.02f), Quaternion.Euler(0f, -14f, 0f), StructureMaterial.Iron, key + 2, StructureRole.Detail));
			if (d.Fine)
			{
				d.Add(StructureShapes.Beam(foot + new Vector3(-0.32f, 0.85f, 0f), foot + new Vector3(0.32f, 0.85f, 0f), 0.025f, 0.025f, StructureMaterial.Iron, key + 3, StructureRole.Detail));
				d.Add(StructureShapes.Beam(foot + new Vector3(0f, 0.85f, -0.32f), foot + new Vector3(0f, 0.85f, 0.32f), 0.025f, 0.025f, StructureMaterial.Iron, key + 4, StructureRole.Detail));
			}
		}

		/// <summary>
		/// A chimney stack standing against a wall from the ground (a gable's, outside it): <paramref name="centre"/> its foot's
		/// middle, <paramref name="size"/> its breadth (x) and depth (z), up to <paramref name="top"/>, with a cap, its pots, and
		/// its smoke given off over them.
		/// </summary>
		private static void CivicStack(StructureDraft d, Vector3 centre, Vector2 size, float top, int key)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(centre.x, (top - FoundationDepth) * 0.5f, centre.z), new Vector3(size.x, top + FoundationDepth, size.y), d.Bevel(0.03f),
				StructureMaterial.Fieldstone, key));
			d.Add(StructureShapes.Box(new Vector3(centre.x, top + 0.06f, centre.z), new Vector3(size.x + 0.14f, 0.12f, size.y + 0.14f), StructureMaterial.Stone, key + 1, StructureRole.Detail));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(centre.x, top - 0.55f, centre.z), new Vector3(size.x + 0.1f, 0.09f, size.y + 0.1f), StructureMaterial.Stone, key + 2, StructureRole.Detail));
				Vector3 along = size.x >= size.y ? Vector3.right : Vector3.forward;
				float breadth = Mathf.Max(size.x, size.y);
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 pot = centre + along * (s * breadth * 0.22f);
					d.Add(StructureShapes.Cylinder(0.11f, top + 0.12f, top + 0.42f, d.Seg(10, 6), StructureMaterial.Fieldstone, key + 3 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(pot.x, 0f, pot.z))));
				}
			}
			d.Emit(new Vector3(centre.x, top + 0.45f, centre.z), SceneEmitterKind.Smoke);
		}

		// ── Market hall ───────────────────────────────────────────────

		private static void MarketHall(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				ArcadedHall(d);
			}
			else
			{
				StoneGuildhall(d);
			}
		}

		/// <summary>
		/// The market hall on its arcade (2026-10-19): the ground left open between stone piers for the market's stalls and the
		/// passers-by, as the market halls of the wool towns stood, its paving one low step (0.15 m) up from the square on every
		/// side; over it the council's hall, jettied out front and back, reached by a stone forestair straight up to a door in its
		/// right gable. A hall over a market must be climbed to from outside: a stair inside would take a bay of the market; and
		/// the flight meets the door head-on (a flight along the wall leaves its landing's side a drop to anyone walking out of the
		/// door), so the arcade's right side stays open but for the flight's 1.5 m.
		/// </summary>
		private static void ArcadedHall(StructureDraft d)
		{
			const float pave = 0.15f, pier = 0.7f, arcade = 3.3f, upper = 3.8f, hallHeight = 3.2f, wall = 0.25f;
			// The hall's walls stand jettied out over the piers' faces (pierZ + pier / 2 + a 0.45 m jetty), 5.4 m: the roof's run.
			const float pierX = 7.6f, pierZ = 4.6f, halfW = 8f, halfD = 5.4f;
			const float top = upper + hallHeight;
			const float doorZ = 1.5f, doorWidth = 1.2f, doorHeight = 2.3f;

			// The paving, one low step up from the square.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, (pave - FoundationDepth) * 0.5f, 0f), new Vector3(halfW * 2f + 0.3f, pave + FoundationDepth, pierZ * 2f + pier + 0.3f),
					StructureMaterial.Stone, 1));
			}
			// The piers: six down each long side, one in the middle of each end; a dressed base and a capital from the middle level.
			var piers = new List<Vector2>();
			for (int i = 0; i < 6; i++)
			{
				float x = -pierX + i * (pierX * 2f / 5f);
				piers.Add(new Vector2(x, -pierZ));
				piers.Add(new Vector2(x, pierZ));
			}
			piers.Add(new Vector2(-pierX, 0f));
			piers.Add(new Vector2(pierX, 0f));
			using (d.Builds(WallRoles))
			{
				for (int i = 0; i < piers.Count; i++)
				{
					Vector2 p = piers[i];
					d.Add(StructureShapes.Box(new Vector3(p.x, (arcade - FoundationDepth) * 0.5f, p.y), new Vector3(pier, arcade + FoundationDepth, pier), StructureMaterial.Stone, 10 + i * 3));
					if (d.Mid)
					{
						d.Add(StructureShapes.Box(new Vector3(p.x, pave + 0.15f, p.y), new Vector3(pier + 0.18f, 0.4f, pier + 0.18f), StructureMaterial.Stone, 11 + i * 3, StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(p.x, arcade - 0.11f, p.y), new Vector3(pier + 0.24f, 0.22f, pier + 0.24f), StructureMaterial.Stone, 12 + i * 3, StructureRole.Detail));
					}
				}
			}
			// Oak posts down the middle under the spine beam, the spans being too long for the floor's joists.
			float bay = pierX * 2f / 5f;
			for (int i = 0; i < 4; i++)
			{
				float x = (i < 2 ? -1f : 1f) * (i % 2 == 0 ? bay * 0.5f : bay * 1.5f);
				d.Add(StructureShapes.Beam(new Vector3(x, pave - 0.05f, 0f), new Vector3(x, arcade + 0.02f, 0f), 0.32f, 0.32f, StructureMaterial.Timber, 60 + i));
				if (d.Fine)
				{
					// Braces from the post up to the spine, each way along it.
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(x, arcade - 0.7f, 0f), new Vector3(x + s * 0.7f, arcade + 0.02f, 0f), 0.14f, 0.14f, StructureMaterial.Timber,
							64 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
			// The girts on the piers, the spine on the posts, and the floor over them.
			float girtY = arcade + 0.13f;
			d.Add(StructureShapes.Beam(new Vector3(-halfW, girtY, -pierZ), new Vector3(halfW, girtY, -pierZ), 0.4f, 0.26f, StructureMaterial.Timber, 80));
			d.Add(StructureShapes.Beam(new Vector3(-halfW, girtY, pierZ), new Vector3(halfW, girtY, pierZ), 0.4f, 0.26f, StructureMaterial.Timber, 81));
			d.Add(StructureShapes.Beam(new Vector3(-halfW, girtY, 0f), new Vector3(halfW, girtY, 0f), 0.36f, 0.26f, StructureMaterial.Timber, 82));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * pierX, girtY, -pierZ - pier * 0.5f), new Vector3(s * pierX, girtY, pierZ + pier * 0.5f), 0.4f, 0.26f, StructureMaterial.Timber, 83 + (s > 0 ? 1 : 0)));
			}
			using (d.Builds(FloorRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, upper - 0.12f, 0f), new Vector3(halfW * 2f, 0.24f, halfD * 2f), StructureMaterial.Timber, 90)).Grain = Vector3.right;
			}
			if (d.Mid)
			{
				// The jetty's bressummers along the floor's overhanging edges, and at level 0 a brace up to each from its pier.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(-halfW, upper - 0.33f, s * (halfD - 0.14f)), new Vector3(halfW, upper - 0.33f, s * (halfD - 0.14f)), 0.28f, 0.22f,
						StructureMaterial.Timber, 92 + (s > 0 ? 1 : 0), StructureRole.Detail)).Grain = Vector3.right;
					if (d.Fine)
					{
						for (int i = 0; i < 6; i++)
						{
							float x = -pierX + i * bay;
							d.Add(StructureShapes.Beam(new Vector3(x, arcade - 0.55f, s * (pierZ + pier * 0.5f)), new Vector3(x, upper - 0.42f, s * (halfD - 0.2f)), 0.12f, 0.12f,
								StructureMaterial.Timber, 94 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
				}
			}

			// The hall's walls: windows along the front and back, the door in the right gable at the head of the forestair, a window
			// in each gable beside it.
			var front = new List<WallOpening>();
			var back = new List<WallOpening>();
			var left = new List<WallOpening>();
			var right = new List<WallOpening>();
			var windows = new List<(Vector3 at, Vector3 outward)>();
			const float sill = upper + 1.7f, winW = 0.9f, winH = 1.4f;
			foreach (float x in new[] { -5.8f, -2.6f, 2.6f, 5.8f })
			{
				front.Add(new WallOpening { Along = x + halfW, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
				windows.Add((new Vector3(x, sill, -halfD), Vector3.back));
			}
			foreach (float x in new[] { -4.4f, 0f, 4.4f })
			{
				back.Add(new WallOpening { Along = x + halfW, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
				windows.Add((new Vector3(x, sill, halfD), Vector3.forward));
			}
			float sideFrom = -halfD + wall;
			left.Add(new WallOpening { Along = -3f - sideFrom, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
			windows.Add((new Vector3(-halfW, sill, -3f), Vector3.left));
			right.Add(new WallOpening { Along = -3f - sideFrom, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
			windows.Add((new Vector3(halfW, sill, -3f), Vector3.right));
			right.Add(new WallOpening { Along = doorZ - sideFrom, Width = doorWidth, Bottom = upper - 0.01f, Top = upper + doorHeight });
			float t2 = wall * 0.5f;
			using (d.Builds(UpperWallRoles))
			{
				WallRun(d, new Vector3(-halfW, 0f, -halfD + t2), new Vector3(halfW, 0f, -halfD + t2), wall, upper, top, front, StructureMaterial.Plaster, 100);
				WallRun(d, new Vector3(-halfW, 0f, halfD - t2), new Vector3(halfW, 0f, halfD - t2), wall, upper, top, back, StructureMaterial.Plaster, 101);
				WallRun(d, new Vector3(-halfW + t2, 0f, -halfD + wall), new Vector3(-halfW + t2, 0f, halfD - wall), wall, upper, top, left, StructureMaterial.Plaster, 102);
				WallRun(d, new Vector3(halfW - t2, 0f, -halfD + wall), new Vector3(halfW - t2, 0f, halfD - wall), wall, upper, top, right, StructureMaterial.Plaster, 103);
			}
			for (int i = 0; i < windows.Count; i++)
			{
				CivicWindow(d, windows[i].at, windows[i].outward, wall, winW, winH, StructureMaterial.Timber, false, 120 + i * 6);
			}
			if (d.Mid)
			{
				Framing(d, halfW * 2f, halfD * 2f, upper, top, 200, front);
			}

			// The roof, ridge along the front, at its palette's pitch; the gables under it; the bell turret on the ridge.
			Matrix4x4 place = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			OwnRoofPlan roof = CivicRoof(d, halfD * 2f, halfW * 2f, top, halfD, 0.5f, StructureMaterial.Timber, 300, place);
			if (!roof.Flat)
			{
				using (d.Builds(UpperWallRoles))
				{
					Gable(d, halfD * 2f - 0.02f, top, roof.Rise - 0.02f, -halfW + t2, wall, StructureMaterial.Plaster, 350, place);
					Gable(d, halfD * 2f - 0.02f, top, roof.Rise - 0.02f, halfW - t2, wall, StructureMaterial.Plaster, 351, place);
				}
			}
			float ridge = roof.Flat ? top + 0.12f : top + roof.Rise;
			BellTurret(d, ridge, roof.Flat ? top + 0.1f : ridge - 1f, 400);

			// The door at the forestair's head, the hall behind it, its hearth against the left gable under the stack outside it.
			HangDoor(d, new Vector3(halfW - wall + 0.05f, upper, doorZ), Vector3.forward, Vector3.left, doorWidth, doorHeight, StructureDoorKind.Studded);
			d.Room(new Vector3(0f, upper, 0f), new Vector2(halfW * 2f - 2f * wall, halfD * 2f - 2f * wall), hallHeight, StructureRoomKind.Guildhall);
			d.RoomHearth(new Vector2(-halfW + wall + 0.47f, 0f), -90f, upper);
			CivicStack(d, new Vector3(-halfW - 0.4f, 0f, 0f), new Vector2(0.8f, 1.2f), ridge + 0.9f, 450);

			// The forestair: a solid stone flight straight out from the door down the right gable, its top step at the threshold.
			Steps(d, new Vector3(halfW, 0f, doorZ), Vector3.right, StairWidth, upper, StructureMaterial.Stone, 500);
			// A rail down each side of the flight on a post at its foot: at every level (2026-10-19, round 3: LOD0-1 only, the
			// collider let a climber walk off the flight's sides 3 m over the ground).
			{
				float flight = (Mathf.CeilToInt(upper / StepRise) - 1) * StepTread;
				for (int s = -1; s <= 1; s += 2)
				{
					float rz = doorZ + s * (StairWidth * 0.5f - 0.06f);
					d.Add(StructureShapes.Beam(new Vector3(halfW + 0.05f, upper + 0.95f, rz), new Vector3(halfW + flight - 0.15f, 0.2f + 0.95f, rz), 0.07f, 0.07f, StructureMaterial.Timber,
						530 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(halfW + flight - 0.15f, 0f, rz), new Vector3(halfW + flight - 0.15f, 1.2f, rz), 0.09f, 0.09f, StructureMaterial.Timber,
						532 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				// The door's lintel.
				d.Add(StructureShapes.Beam(new Vector3(halfW + 0.06f, upper + doorHeight + 0.12f, doorZ - doorWidth * 0.5f - 0.15f),
					new Vector3(halfW + 0.06f, upper + doorHeight + 0.12f, doorZ + doorWidth * 0.5f + 0.15f), 0.2f, 0.18f, StructureMaterial.Timber, 536, StructureRole.Detail));
				// The town's arms on the front: a banner hung flat on its pole between the middle windows, a cross on its field.
				float by = top - 0.3f, bz = -halfD - 0.06f;
				d.Add(StructureShapes.Beam(new Vector3(-0.7f, by, bz - 0.04f), new Vector3(0.7f, by, bz - 0.04f), 0.05f, 0.05f, StructureMaterial.Iron, 540, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, by - 1.05f, bz), new Vector3(1.1f, 2f, 0.03f), StructureMaterial.Dyed, 541, StructureRole.Detail)).Grain = Vector3.up;
				d.Add(StructureShapes.Box(new Vector3(0f, by - 1f, bz - 0.02f), new Vector3(0.18f, 1.6f, 0.02f), StructureMaterial.Canvas, 542, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, by - 0.75f, bz - 0.02f), new Vector3(0.9f, 0.18f, 0.02f), StructureMaterial.Canvas, 543, StructureRole.Detail));
				// A torch by the hall's door, lit day and night.
				MountedTorch(d, Matrix4x4.TRS(new Vector3(halfW, upper + 1.9f, doorZ + doorWidth * 0.5f + 0.45f), Quaternion.LookRotation(Vector3.left, Vector3.up), Vector3.one), false, 550);
			}
		}

		/// <summary>
		/// A bell turret astride the ridge (<paramref name="ridge"/>, its boarded foot from <paramref name="foot"/>): four corner
		/// posts open between them, the bell hung in the middle, a pyramid cap and a weather-vane. A fixed form, not a roof that
		/// follows the palette: it rides the ridge wherever the palette pitched it.
		/// </summary>
		private static void BellTurret(StructureDraft d, float ridge, float foot, int key)
		{
			const float half = 0.8f, open = 1.4f;
			float sill = ridge + 0.6f, head = sill + open;
			d.Add(StructureShapes.Box(new Vector3(0f, (foot + sill) * 0.5f, 0f), new Vector3(half * 2f, sill - foot, half * 2f), StructureMaterial.Timber, key)).Grain = Vector3.up;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * (half - 0.08f), sill - 0.02f, sz * (half - 0.08f)), new Vector3(sx * (half - 0.08f), head + 0.05f, sz * (half - 0.08f)),
						0.16f, 0.16f, StructureMaterial.Timber, key + 1 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			// The cap: a pyramid on the posts' heads, its eaves out over the openings.
			d.Add(StructureShapes.Lathe(new[] { new Vector2((half + 0.3f) * 1.414f, head), new Vector2(0f, head + 1.6f) }, 4, StructureMaterial.Timber, key + 5, false,
				Mathf.PI * 0.25f, false, false, StructureRole.Roof));
			CivicVane(d, new Vector3(0f, head + 1.55f, 0f), key + 10);
			if (d.Mid)
			{
				// The headstock across under the cap, and the bell under it.
				d.Add(StructureShapes.Beam(new Vector3(-half + 0.1f, head - 0.12f, 0f), new Vector3(half - 0.1f, head - 0.12f, 0f), 0.14f, 0.14f, StructureMaterial.Timber, key + 6, StructureRole.Detail));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.36f, sill + 0.35f), new Vector2(0.4f, sill + 0.45f), new Vector2(0.22f, sill + 0.95f), new Vector2(0.12f, head - 0.2f), new Vector2(0f, head - 0.18f) },
					d.Seg(10, 6), StructureMaterial.Iron, key + 7, true, 0f, false, false, StructureRole.Detail));
			}
		}

		/// <summary>
		/// The stone guildhall (2026-10-19): one tall hall on a plinth, its studded double door up three steps, tall two-light
		/// windows between buttresses, the guild's arms carved over the door and its banners either side, the hall's hearth under
		/// a stack on the right gable, and a lead-capped clock cupola on the ridge.
		/// </summary>
		private static void StoneGuildhall(StructureDraft d)
		{
			const float halfW = 8f, halfD = 5f, wall = 0.6f, plinth = 0.45f, height = 5.2f;
			const float top = plinth + height;
			const float doorWidth = 2f, doorHeight = 3.2f, winW = 0.9f, winH = 2.6f, sill = plinth + 2.8f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (plinth - FoundationDepth) * 0.5f, 0f), new Vector3(halfW * 2f + 0.3f, plinth + FoundationDepth, halfD * 2f + 0.3f),
					d.Bevel(0.04f), StructureMaterial.Stone, 1));
			}
			var front = new List<WallOpening> { new WallOpening { Along = halfW, Width = doorWidth, Bottom = plinth - 0.01f, Top = plinth + doorHeight } };
			var back = new List<WallOpening>();
			var left = new List<WallOpening>();
			var windows = new List<(Vector3 at, Vector3 outward)>();
			foreach (float x in new[] { -6.4f, -3.4f, 3.4f, 6.4f })
			{
				front.Add(new WallOpening { Along = x + halfW, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
				windows.Add((new Vector3(x, sill, -halfD), Vector3.back));
			}
			foreach (float x in new[] { -5.5f, -2f, 2f, 5.5f })
			{
				back.Add(new WallOpening { Along = x + halfW, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
				windows.Add((new Vector3(x, sill, halfD), Vector3.forward));
			}
			left.Add(new WallOpening { Along = halfD - wall, Width = winW, Bottom = sill - winH * 0.5f, Top = sill + winH * 0.5f });
			windows.Add((new Vector3(-halfW, sill, 0f), Vector3.left));
			float t2 = wall * 0.5f;
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-halfW, 0f, -halfD + t2), new Vector3(halfW, 0f, -halfD + t2), wall, plinth, top, front, StructureMaterial.Stone, 10);
				WallRun(d, new Vector3(-halfW, 0f, halfD - t2), new Vector3(halfW, 0f, halfD - t2), wall, plinth, top, back, StructureMaterial.Stone, 11);
				WallRun(d, new Vector3(-halfW + t2, 0f, -halfD + wall), new Vector3(-halfW + t2, 0f, halfD - wall), wall, plinth, top, left, StructureMaterial.Stone, 12);
				WallRun(d, new Vector3(halfW - t2, 0f, -halfD + wall), new Vector3(halfW - t2, 0f, halfD - wall), wall, plinth, top, null, StructureMaterial.Stone, 13);
				// Buttresses between the windows and at the corners, front and back, their heads weathered back to the wall.
				int n = 0;
				foreach ((float x, float side) in new[] { (-7.7f, -1f), (-4.9f, -1f), (4.9f, -1f), (7.7f, -1f), (-7.7f, 1f), (-3.75f, 1f), (3.75f, 1f), (7.7f, 1f) })
				{
					float z = side * (halfD + 0.3f);
					d.Add(StructureShapes.ChamferBox(new Vector3(x, (3.6f - FoundationDepth) * 0.5f, z), new Vector3(0.6f, 3.6f + FoundationDepth, 0.6f), d.Bevel(0.03f), StructureMaterial.Stone, 20 + n));
					if (d.Mid)
					{
						d.Add(StructureShapes.Block(new Vector3(x, 3.72f, z - side * 0.06f), new Vector3(0.62f, 0.3f, 0.5f), Quaternion.Euler(side * -35f, 0f, 0f), StructureMaterial.Stone, 30 + n, StructureRole.Detail));
					}
					n++;
				}
			}
			for (int i = 0; i < windows.Count; i++)
			{
				CivicWindow(d, windows[i].at, windows[i].outward, wall, winW, winH, StructureMaterial.Stone, true, 40 + i * 6);
			}

			// The roof along the front, its stone gables, the clock cupola on its ridge, the hall's stack on the right gable.
			Matrix4x4 place = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			OwnRoofPlan roof = CivicRoof(d, halfD * 2f, halfW * 2f, top, halfD, 0.45f, StructureMaterial.Timber, 100, place);
			if (!roof.Flat)
			{
				using (d.Builds(WallRoles))
				{
					Gable(d, halfD * 2f - 0.02f, top, roof.Rise - 0.02f, -halfW + t2, wall, StructureMaterial.Stone, 150, place);
					Gable(d, halfD * 2f - 0.02f, top, roof.Rise - 0.02f, halfW - t2, wall, StructureMaterial.Stone, 151, place);
				}
			}
			float ridge = roof.Flat ? top + 0.12f : top + roof.Rise;
			ClockCupola(d, ridge, roof.Flat ? top + 0.1f : ridge - 0.9f, 200);
			CivicStack(d, new Vector3(halfW + 0.4f, 0f, 0f), new Vector2(0.8f, 1.3f), ridge + 0.9f, 250);

			// The door up its steps, the hall behind it with its hearth under the stack.
			Steps(d, new Vector3(0f, 0f, -halfD - 0.15f), Vector3.back, doorWidth + 0.6f, plinth, StructureMaterial.Stone, 300);
			HangDoor(d, new Vector3(0f, plinth, -halfD + wall - 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight, StructureDoorKind.Studded);
			d.Room(new Vector3(0f, plinth, 0f), new Vector2(halfW * 2f - 2f * wall, halfD * 2f - 2f * wall), height, StructureRoomKind.Guildhall);
			d.RoomHearth(new Vector2(halfW - wall - 0.47f, 0f), 90f, plinth);
			if (!d.Mid)
			{
				return;
			}
			// The doorway's dressed jambs and hood; the guild's arms carved over it; its banners either side; torches by the door.
			float fz = -halfD - 0.06f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (doorWidth * 0.5f + 0.12f), plinth + doorHeight * 0.5f, fz), new Vector3(0.24f, doorHeight, 0.14f), StructureMaterial.Stone, 310 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, plinth + doorHeight + 0.18f, fz - 0.02f), new Vector3(doorWidth + 0.7f, 0.22f, 0.2f), StructureMaterial.Stone, 312, StructureRole.Detail));
			var shield = new[] { new Vector2(-0.38f, 0.5f), new Vector2(0.38f, 0.5f), new Vector2(0.38f, 0f), new Vector2(0f, -0.42f), new Vector2(-0.38f, 0f) };
			d.Add(StructureShapes.Prism(shield, -0.08f, 0f, StructureMaterial.Monolith, 313, StructureRole.Detail)
				.Transform(Matrix4x4.Translate(new Vector3(0f, plinth + doorHeight + 0.85f, -halfD))));
			for (int s = -1; s <= 1; s += 2)
			{
				float x = s * 1.98f;
				d.Add(StructureShapes.Beam(new Vector3(x - 0.42f, plinth + 4.3f, fz - 0.04f), new Vector3(x + 0.42f, plinth + 4.3f, fz - 0.04f), 0.05f, 0.05f, StructureMaterial.Iron, 320 + s, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(x, plinth + 3.3f, fz), new Vector3(0.7f, 1.95f, 0.03f), StructureMaterial.Dyed, 323 + s, StructureRole.Detail)).Grain = Vector3.up;
				if (d.Fine)
				{
					d.Add(StructureShapes.Box(new Vector3(x, plinth + 3.45f, fz - 0.02f), new Vector3(0.16f, 1.4f, 0.02f), StructureMaterial.Canvas, 326 + s, StructureRole.Detail));
				}
				MountedTorch(d, Matrix4x4.TRS(new Vector3(s * 1.45f, plinth + 2.1f, -halfD), Quaternion.identity, Vector3.one), false, 330 + (s > 0 ? 10 : 0));
			}
		}

		/// <summary>
		/// A clock cupola astride the ridge: an octagonal timber drum with the clock's dial on its front and open lights on its
		/// sides, an ogee cap of lead (dark metal), a weather-vane. A fixed form that rides the ridge, as the bell turret does.
		/// </summary>
		private static void ClockCupola(StructureDraft d, float ridge, float foot, int key)
		{
			const float r = 0.9f, drum = 1.4f;
			float headY = ridge + drum;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r, foot), new Vector2(r, headY) }, 8, StructureMaterial.Timber, key, false, Mathf.PI / 8f, false, true));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r + 0.15f, headY), new Vector2(r + 0.05f, headY + 0.15f), new Vector2(0.55f, headY + 0.6f), new Vector2(0.16f, headY + 1.2f), new Vector2(0f, headY + 1.45f) },
				8, StructureMaterial.Iron, key + 1, false, Mathf.PI / 8f, false, false, StructureRole.Roof));
			CivicVane(d, new Vector3(0f, headY + 1.4f, 0f), key + 10);
			if (!d.Mid)
			{
				return;
			}
			float apothem = r * Mathf.Cos(Mathf.PI / 8f);
			float dialY = ridge + 0.8f;
			// The dial facing the square, its hour and minute hands; dark lights on the drum's other faces.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.42f, 0f), new Vector2(0.42f, 0.05f) }, d.Seg(12, 8), StructureMaterial.Plaster, key + 2, true, 0f, false, false, StructureRole.Detail)
				.Transform(Matrix4x4.TRS(new Vector3(0f, dialY, -apothem - 0.04f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			d.Add(StructureShapes.Beam(new Vector3(0f, dialY, -apothem - 0.06f), new Vector3(0.12f, dialY + 0.26f, -apothem - 0.06f), 0.04f, 0.015f, StructureMaterial.Iron, key + 3, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(0f, dialY, -apothem - 0.07f), new Vector3(-0.22f, dialY - 0.18f, -apothem - 0.07f), 0.03f, 0.015f, StructureMaterial.Iron, key + 4, StructureRole.Detail));
			for (int f = 1; f < 4; f++)
			{
				Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
				d.Add(StructureShapes.Block(q * new Vector3(0f, dialY, -apothem - 0.01f), new Vector3(0.4f, 0.8f, 0.04f), q, StructureMaterial.Ash, key + 5 + f, StructureRole.Detail));
			}
		}

		// ── Shop-house ────────────────────────────────────────────────

		/// <summary>The width of a shop-house's stall-board, along the front (x), metres; how far it reaches out, and its top over the ground.</summary>
		private const float ShopBoardWidth = 2.9f, ShopBoardDepth = 0.7f;

		/// <summary>
		/// A medieval shop-house (2026-10-19), its gable to the street as a burgage plot's narrow front was: the shop on the ground
		/// floor behind a wide unglazed front whose lower shutter folds down on its legs as the stall-board the wares are laid out
		/// on and whose upper shutter is propped up over it as a canopy; the door beside it; an iron sign bracket out from the
		/// jettied storey where the family lives, up a real stair (UpperStorey). The roof follows its palette, its gables with it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// What the trade dresses (2026-10-19: one shop-house for every trade, each laid one given its trade's sign, wares and
		/// room): the stall-board is built EMPTY and the bracket with no board on it. Two marks say where they go
		/// (<see cref="StructurePieces.MarksOf"/>):
		/// <c>"sign"</c>, the top-centre of the sign board, the point under the bracket it hangs from; its yaw is the way the
		/// board's face looks, 0 down −z as a piece's front does — the bracket projects out of the front wall, so the board hangs
		/// along it and is seen side-on from the street: yaw 90 (the board's face toward −x). Room under it for a board up to
		/// 0.9 m along the bracket and 0.8 m tall.
		/// <c>"stall"</c>, the middle of the stall-board's top face (yaw 0, its front edge toward −z), and <c>"stall-size"</c>, its
		/// usable top (x along the front, z out from the wall, y unused).
		/// The ground floor's room is <see cref="StructureRoomKind.Shop"/>; the trade's builder may furnish it as its own.
		/// </para>
		/// </remarks>
		private static void ShopHouse(StructureDraft d)
		{
			bool stone = d.Variant == 1;
			var h = new House
			{
				Width = 7f, Depth = 10f, WallHeight = 2.8f, Upper = 2.7f, Overhang = 0.5f, Wall = 0.3f, Plinth = 0.4f,
				WallMaterial = stone ? StructureMaterial.Stone : StructureMaterial.Plaster, RoofMaterial = stone ? StructureMaterial.Thatch : StructureMaterial.Timber,
				PlinthMaterial = StructureMaterial.Fieldstone, Frame = !stone, RidgeAlongX = false, Chimney = false, Windows = 2,
				DoorWidth = 1.1f, DoorHeight = 2.3f, DoorX = 1.85f, DoorKind = StructureDoorKind.Planks, RoomKind = StructureRoomKind.Shop,
				UpperWallMaterial = stone ? StructureMaterial.Plaster : (StructureMaterial?)null, UpperFrame = true, Key = 1,
			};
			int key = h.Key;
			float floor = h.Plinth, groundTop = floor + h.WallHeight;
			float halfW = h.Width * 0.5f, halfD = h.Depth * 0.5f, t2 = h.Wall * 0.5f, front = -halfD;
			const float shopX = -1.15f, shopBottom = 0.55f, shopTop = 2.15f;
			float shopWidth = ShopBoardWidth + 0.1f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (h.Plinth - FoundationDepth) * 0.5f, 0f), new Vector3(h.Width + 0.24f, h.Plinth + FoundationDepth, h.Depth + 0.24f),
					d.Bevel(0.04f), h.PlinthMaterial, key));
			}
			// The front: the door, and the shop's wide opening beside it.
			var frontHoles = new List<WallOpening>
			{
				new WallOpening { Along = h.DoorX + halfW, Width = h.DoorWidth, Bottom = floor - 0.01f, Top = floor + h.DoorHeight },
				new WallOpening { Along = shopX + halfW, Width = shopWidth, Bottom = floor + shopBottom, Top = floor + shopTop },
			};
			// A window in the side wall the stair does not climb (UpperStorey drops the one behind its flight).
			var leftHoles = new List<WallOpening>();
			var rightHoles = new List<WallOpening>();
			var windows = new List<(Vector3 at, Vector3 outward)>();
			float sill = floor + h.DoorHeight * 0.62f;
			var sideHole = new WallOpening { Along = (h.Depth - 2f * h.Wall) * 0.5f, Width = WindowWidth, Bottom = sill - WindowHeight * 0.5f, Top = sill + WindowHeight * 0.5f };
			int stair = StairSide(h, floor, groundTop);
			if (stair >= 0)
			{
				windows.Add((new Vector3(-halfW, sill, 0f), Vector3.left));
				leftHoles.Add(sideHole);
			}
			if (stair <= 0)
			{
				windows.Add((new Vector3(halfW, sill, 0f), Vector3.right));
				rightHoles.Add(sideHole);
			}
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-halfW, 0f, -halfD + t2), new Vector3(halfW, 0f, -halfD + t2), h.Wall, floor, groundTop, frontHoles, h.WallMaterial, key + 1);
				WallRun(d, new Vector3(-halfW, 0f, halfD - t2), new Vector3(halfW, 0f, halfD - t2), h.Wall, floor, groundTop, null, h.WallMaterial, key + 2);
				WallRun(d, new Vector3(-halfW + t2, 0f, -halfD + h.Wall), new Vector3(-halfW + t2, 0f, halfD - h.Wall), h.Wall, floor, groundTop, leftHoles, h.WallMaterial, key + 3);
				WallRun(d, new Vector3(halfW - t2, 0f, -halfD + h.Wall), new Vector3(halfW - t2, 0f, halfD - h.Wall), h.Wall, floor, groundTop, rightHoles, h.WallMaterial, key + 4);
			}
			// The family's storey over the shop: its floor with the stairwell, the stair, its walls and windows, both rooms.
			float depth = h.Depth;
			float top = UpperStorey(d, h, floor, groundTop, true, ref depth);

			// The roof, ridge running back from the street, at its palette's pitch (or flat behind a parapet, as a house's is).
			bool thatch = h.RoofMaterial == StructureMaterial.Thatch;
			float kitRise = thatch ? 4.2f : 3.5f;
			OwnRoofPlan roof = OwnRoof(d, h.RoofMaterial, kitRise, halfW);
			if (roof.Flat)
			{
				FlatRoof(d, h, h.Width, depth, top, true, h.DoorX, key);
			}
			else
			{
				CivicPitched(d, roof, h.Width, depth, top, kitRise, h.Overhang, h.RoofMaterial, key + 10, Matrix4x4.identity);
				StructureMaterial upperWall = h.UpperWallMaterial ?? h.WallMaterial;
				using (d.Builds(UpperWallRoles))
				{
					Gable(d, h.Width - 0.02f, top, roof.Rise - 0.02f, -depth * 0.5f + t2, h.Wall, upperWall, key + 12, Matrix4x4.identity);
					Gable(d, h.Width - 0.02f, top, roof.Rise - 0.02f, depth * 0.5f - t2, h.Wall, upperWall, key + 13, Matrix4x4.identity);
				}
			}

			// The door and its step; the shop's floor (UpperStorey marked it, its stair kept clear).
			HangDoor(d, new Vector3(h.DoorX, floor, front + h.Wall - 0.05f), Vector3.right, Vector3.forward, h.DoorWidth, h.DoorHeight, h.DoorKind);
			Steps(d, new Vector3(h.DoorX, 0f, front - 0.12f), Vector3.back, Mathf.Max(h.DoorWidth + 0.4f, StairWidth), floor, h.PlinthMaterial, key + 22);
			for (int i = 0; i < windows.Count; i++)
			{
				Window(d, windows[i].at, windows[i].outward, h.Wall, key + 30 + i * 8);
			}
			if (h.Frame && d.Mid)
			{
				Framing(d, h.Width, h.Depth, floor, groundTop, key + 200, frontHoles);
			}

			// The stall-board: the lower shutter let down on its hinges at the opening's sill, flat on two legs, its top level
			// with the sill; EMPTY (the trade lays its wares on it, the "stall" mark).
			float boardTop = floor + shopBottom;
			float boardZ = front - ShopBoardDepth * 0.5f;
			d.Add(StructureShapes.Box(new Vector3(shopX, boardTop - 0.03f, boardZ), new Vector3(ShopBoardWidth, 0.06f, ShopBoardDepth), StructureMaterial.Timber, key + 400)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = shopX + s * (ShopBoardWidth * 0.5f - 0.12f);
				d.Add(StructureShapes.Beam(new Vector3(x, -0.1f, front - ShopBoardDepth + 0.08f), new Vector3(x, boardTop - 0.05f, front - ShopBoardDepth + 0.08f), 0.07f, 0.07f,
					StructureMaterial.Timber, key + 401 + (s > 0 ? 1 : 0)));
			}
			d.Mark("stall", new Vector3(shopX, boardTop, boardZ), 0f);
			d.Mark("stall-size", new Vector3(ShopBoardWidth, 0f, ShopBoardDepth));
			// The upper shutter, hinged at the opening's head and propped up over the board as a canopy (clear of the jetty).
			float headY = floor + shopTop;
			const float flap = 0.85f, lift = 14f;
			Vector3 flapMid = new Vector3(shopX, headY + Mathf.Sin(lift * Mathf.Deg2Rad) * flap * 0.5f + 0.03f, front - Mathf.Cos(lift * Mathf.Deg2Rad) * flap * 0.5f);
			d.Add(StructureShapes.Block(flapMid, new Vector3(shopWidth + 0.06f, 0.05f, flap), Quaternion.Euler(lift, 0f, 0f), StructureMaterial.Timber, key + 405, StructureRole.Roof)).Grain = Vector3.right;
			if (d.Mid)
			{
				// The opening's lintel (a stone one in a stone front), and the shutter's iron props.
				d.Add(StructureShapes.Beam(new Vector3(shopX - shopWidth * 0.5f - 0.15f, headY + 0.1f, front - 0.06f), new Vector3(shopX + shopWidth * 0.5f + 0.15f, headY + 0.1f, front - 0.06f),
					0.2f, 0.18f, stone ? StructureMaterial.Stone : StructureMaterial.Timber, key + 406, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(h.DoorX - h.DoorWidth * 0.5f - 0.15f, floor + h.DoorHeight + 0.1f, front - 0.06f),
					new Vector3(h.DoorX + h.DoorWidth * 0.5f + 0.15f, floor + h.DoorHeight + 0.1f, front - 0.06f), 0.2f, 0.18f,
					stone ? StructureMaterial.Stone : StructureMaterial.Timber, key + 407, StructureRole.Detail));
				Vector3 tip = flapMid + new Vector3(0f, Mathf.Sin(lift * Mathf.Deg2Rad) * flap * 0.5f - 0.04f, -Mathf.Cos(lift * Mathf.Deg2Rad) * flap * 0.5f + 0.05f);
				for (int s = -1; s <= 1; s += 2)
				{
					float x = shopX + s * (shopWidth * 0.5f - 0.2f);
					d.Add(StructureShapes.Beam(new Vector3(x, headY - 0.55f, front - 0.04f), new Vector3(x, tip.y, tip.z), 0.025f, 0.025f, StructureMaterial.Iron, key + 408 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The sign bracket: iron, out of the jettied storey's front over the door's side, with a stay under it and a scroll at
			// its end; NO board on it (the trade hangs its own, the "sign" mark). High enough that a tall rider passes under the board.
			float wallZ = -depth * 0.5f;
			const float bracketX = 2.65f, reach = 1.25f;
			float upperFloor = groundTop + 0.24f;
			float bracketY = upperFloor + 0.35f;
			d.Add(StructureShapes.Beam(new Vector3(bracketX, bracketY, wallZ + 0.02f), new Vector3(bracketX, bracketY, wallZ - reach), 0.06f, 0.06f, StructureMaterial.Iron, key + 410, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(bracketX, bracketY, wallZ - 0.02f), new Vector3(0.12f, 0.3f, 0.04f), StructureMaterial.Iron, key + 411, StructureRole.Detail));
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(new Vector3(bracketX, bracketY - 0.6f, wallZ + 0.02f), new Vector3(bracketX, bracketY - 0.02f, wallZ - reach * 0.65f), 0.04f, 0.04f,
					StructureMaterial.Iron, key + 412, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(new Vector3(bracketX, bracketY, wallZ - reach - 0.04f), new Vector3(0.06f, 0.06f, 0.06f), 6, 4, StructureMaterial.Iron, key + 413, StructureRole.Detail));
			}
			if (d.Fine)
			{
				// The two hooks a board's rings hang on.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(bracketX, bracketY - 0.07f, wallZ - 0.7f + s * 0.38f), new Vector3(0.02f, 0.08f, 0.02f), StructureMaterial.Iron, key + 414 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			// "sign": the board's top-centre under the bracket, between its hooks; yaw 90, seen side-on from the street.
			d.Mark("sign", new Vector3(bracketX, bracketY - 0.11f, wallZ - 0.7f), 90f);
		}

		// ── Warehouse ─────────────────────────────────────────────────

		/// <summary>
		/// A merchant's warehouse (2026-10-19), its gable to the quay or the street: the cart door, the loft door shut high over it
		/// under the hoist beam (its hood, pulley, rope and a sack on the hook), small barred windows high in the long walls; one
		/// tall store open to the roof's tie beams inside (its loft door is shut from outside: nothing behind glass is out of reach).
		/// </summary>
		private static void Warehouse(StructureDraft d)
		{
			bool timber = d.Variant == 1;
			const float halfW = 5f, halfD = 8f, plinth = 0.2f, height = 5.4f;
			float wall = timber ? 0.2f : 0.6f;
			const float top = plinth + height;
			const float doorWidth = 3f, doorHeight = 3.3f;
			StructureMaterial walls = timber ? StructureMaterial.Timber : StructureMaterial.Stone;
			StructureMaterial trim = timber ? StructureMaterial.Timber : StructureMaterial.Stone;
			float front = -halfD, t2 = wall * 0.5f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (plinth - FoundationDepth) * 0.5f, 0f), new Vector3(halfW * 2f + 0.3f, plinth + FoundationDepth, halfD * 2f + 0.3f),
					d.Bevel(0.04f), timber ? StructureMaterial.Fieldstone : StructureMaterial.Stone, 1));
			}
			const float winW = 0.6f, winH = 0.7f, winY = plinth + 3.25f;
			var fronts = new List<WallOpening> { new WallOpening { Along = halfW, Width = doorWidth, Bottom = plinth - 0.01f, Top = plinth + doorHeight } };
			var backs = new List<WallOpening> { new WallOpening { Along = halfW, Width = winW, Bottom = winY - winH * 0.5f, Top = winY + winH * 0.5f } };
			var sides = new List<WallOpening>();
			var windows = new List<(Vector3 at, Vector3 outward)> { (new Vector3(0f, winY, halfD), Vector3.forward) };
			float sideFrom = -halfD + wall;
			foreach (float z in new[] { -4.5f, 0f, 4.5f })
			{
				sides.Add(new WallOpening { Along = z - sideFrom, Width = winW, Bottom = winY - winH * 0.5f, Top = winY + winH * 0.5f });
				windows.Add((new Vector3(-halfW, winY, z), Vector3.left));
				windows.Add((new Vector3(halfW, winY, z), Vector3.right));
			}
			int firstWall = d.Solids.Count;
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-halfW, 0f, -halfD + t2), new Vector3(halfW, 0f, -halfD + t2), wall, plinth, top, fronts, walls, 10);
				WallRun(d, new Vector3(-halfW, 0f, halfD - t2), new Vector3(halfW, 0f, halfD - t2), wall, plinth, top, backs, walls, 11);
				WallRun(d, new Vector3(-halfW + t2, 0f, sideFrom), new Vector3(-halfW + t2, 0f, halfD - wall), wall, plinth, top, sides, walls, 12);
				WallRun(d, new Vector3(halfW - t2, 0f, sideFrom), new Vector3(halfW - t2, 0f, halfD - wall), wall, plinth, top, new List<WallOpening>(sides), walls, 13);
			}
			if (timber)
			{
				// Boarded upright, as weatherboarding on a frame was.
				for (int i = firstWall; i < d.Solids.Count; i++)
				{
					d.Solids[i].Grain = Vector3.up;
				}
			}

			// The roof, ridge running back from the gable front, at its palette's pitch; the gables with it.
			OwnRoofPlan roof = CivicRoof(d, halfW * 2f, halfD * 2f, top, halfW, 0.5f, StructureMaterial.Timber, 100, Matrix4x4.identity);
			if (!roof.Flat)
			{
				int firstGable = d.Solids.Count;
				using (d.Builds(WallRoles))
				{
					Gable(d, halfW * 2f - 0.02f, top, roof.Rise - 0.02f, -halfD + t2, wall, walls, 150, Matrix4x4.identity);
					Gable(d, halfW * 2f - 0.02f, top, roof.Rise - 0.02f, halfD - t2, wall, walls, 151, Matrix4x4.identity);
				}
				if (timber)
				{
					for (int i = firstGable; i < d.Solids.Count; i++)
					{
						d.Solids[i].Grain = Vector3.up;
					}
				}
			}

			// The cart door, its double leaves; the store behind it.
			HangDoor(d, new Vector3(0f, plinth, front + wall - 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight, StructureDoorKind.Planks);
			d.Room(new Vector3(0f, plinth, 0f), new Vector2(halfW * 2f - 2f * wall, halfD * 2f - 2f * wall), height, StructureRoomKind.Storehouse);

			// The loft door, shut, high over the cart door; the hoist beam out over it from the wall's head.
			const float loftBottom = 3.75f, loftTop = 5.25f, loftWidth = 1.6f;
			d.Add(StructureShapes.Box(new Vector3(0f, (loftBottom + loftTop) * 0.5f, front - 0.04f), new Vector3(loftWidth, loftTop - loftBottom, 0.08f), StructureMaterial.Timber, 200)).Grain = Vector3.up;
			const float beamY = 5.45f, beamReach = 1.8f;
			d.Add(StructureShapes.Beam(new Vector3(0f, beamY, front + wall), new Vector3(0f, beamY, front - beamReach), 0.24f, 0.26f, StructureMaterial.Timber, 210));
			// Barred windows: the hole, three iron bars in it, a sill and a lintel.
			for (int i = 0; i < windows.Count; i++)
			{
				(Vector3 at, Vector3 outward) = windows[i];
				Quaternion turn = Quaternion.LookRotation(-outward, Vector3.up);
				Vector3 across = turn * Vector3.right;
				if (!d.Mid)
				{
					continue;
				}
				for (int b = -1; b <= 1; b++)
				{
					d.Add(StructureShapes.Block(at - outward * (wall * 0.5f) + across * (b * winW * 0.27f), new Vector3(0.035f, winH, 0.035f), turn, StructureMaterial.Iron, 220 + i * 6 + b + 1, StructureRole.Detail));
				}
				d.Add(StructureShapes.Block(at + outward * 0.05f + Vector3.down * (winH * 0.5f + 0.05f), new Vector3(winW + 0.2f, 0.1f, 0.16f), turn, trim, 220 + i * 6 + 3, StructureRole.Detail));
				d.Add(StructureShapes.Block(at + outward * 0.05f + Vector3.up * (winH * 0.5f + 0.07f), new Vector3(winW + 0.26f, 0.14f, 0.14f), turn, trim, 220 + i * 6 + 4, StructureRole.Detail));
			}
			if (timber)
			{
				// The frame the boards are nailed to: corner posts, and from the middle level the posts between and a rail.
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(sx * (halfW + 0.06f), -0.3f, sz * (halfD + 0.06f)), new Vector3(sx * (halfW + 0.06f), top, sz * (halfD + 0.06f)),
							0.24f, 0.24f, StructureMaterial.Timber, 270 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
					}
				}
				if (d.Mid)
				{
					for (int sx = -1; sx <= 1; sx += 2)
					{
						foreach (float z in new[] { -6.75f, -2.25f, 2.25f, 6.75f })
						{
							d.Add(StructureShapes.Beam(new Vector3(sx * (halfW + 0.04f), plinth, z), new Vector3(sx * (halfW + 0.04f), top, z), 0.18f, 0.16f, StructureMaterial.Timber,
								280 + (sx > 0 ? 4 : 0) + Mathf.RoundToInt((z + 6.75f) / 4.5f), StructureRole.Detail));
						}
						d.Add(StructureShapes.Beam(new Vector3(sx * (halfW + 0.04f), plinth + 2.2f, -halfD), new Vector3(sx * (halfW + 0.04f), plinth + 2.2f, halfD), 0.14f, 0.16f,
							StructureMaterial.Timber, 290 + (sx > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
			else if (d.Fine)
			{
				// Quoins up the stone corners, long and short by turns.
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						for (int q = 0; q < 4; q++)
						{
							bool longFace = q % 2 == 0;
							float y = plinth + 0.5f + q * 1.3f;
							var size = new Vector3(longFace ? 0.7f : 0.4f, 0.32f, longFace ? 0.4f : 0.7f);
							d.Add(StructureShapes.Box(new Vector3(sx * (halfW - size.x * 0.5f + 0.03f), y, sz * (halfD - size.z * 0.5f + 0.03f)), size, StructureMaterial.Stone,
								300 + (sx > 0 ? 8 : 0) + (sz > 0 ? 4 : 0) + q, StructureRole.Detail));
						}
					}
				}
			}
			if (!d.Mid)
			{
				return;
			}
			// The cart door's lintel and the loft door's frame.
			d.Add(StructureShapes.Beam(new Vector3(-doorWidth * 0.5f - 0.2f, plinth + doorHeight + 0.12f, front - 0.08f), new Vector3(doorWidth * 0.5f + 0.2f, plinth + doorHeight + 0.12f, front - 0.08f),
				0.22f, 0.2f, timber ? StructureMaterial.Timber : StructureMaterial.Stone, 240, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (loftWidth * 0.5f + 0.06f), (loftBottom + loftTop) * 0.5f, front - 0.06f), new Vector3(0.12f, loftTop - loftBottom + 0.12f, 0.1f), StructureMaterial.Timber,
					241 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, loftBottom - 0.04f, front - 0.1f), new Vector3(loftWidth + 0.3f, 0.1f, 0.2f), StructureMaterial.Timber, 243, StructureRole.Detail));
			// The hood over the hoist beam: two boards pitched over it from the wall out past the pulley.
			float hoodRidge = beamY + 0.62f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Block(new Vector3(s * 0.29f, hoodRidge - 0.2f, front - 0.95f), new Vector3(0.72f, 0.05f, 2f), Quaternion.Euler(0f, 0f, -s * 35f), StructureMaterial.Timber,
					244 + (s > 0 ? 1 : 0), StructureRole.Roof)).Grain = Vector3.forward;
			}
			// The pulley in its iron strap under the beam's end, the rope over it, a sack on the hook (high over a cart's load).
			float pulleyZ = front - beamReach + 0.15f, pulleyY = beamY - 0.3f;
			d.Add(StructureShapes.Rod(new Vector3(-0.05f, pulleyY, pulleyZ), new Vector3(0.05f, pulleyY, pulleyZ), 0.16f, d.Seg(10, 6), StructureMaterial.Timber, 246, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, beamY - 0.18f, pulleyZ), new Vector3(0.14f, 0.36f, 0.04f), StructureMaterial.Iron, 247, StructureRole.Detail));
			float ropeZ = pulleyZ - 0.16f;
			d.Add(StructureShapes.Rod(new Vector3(0f, pulleyY, ropeZ), new Vector3(0f, 4.35f, ropeZ), 0.02f, 4, StructureMaterial.Timber, 248, StructureRole.Detail));
			Sack(d, new Vector3(0f, 3.75f, ropeZ), 0.6f, 0.9f, Quaternion.identity, 249);
			// Goods waiting by the door, clear of its way in: crates stacked one side, a barrel and a sack the other.
			d.Add(StructureShapes.Box(new Vector3(-2.75f, 0.35f, front - 0.55f), new Vector3(0.7f, 0.7f, 0.7f), StructureMaterial.Timber, 250, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(-2.7f, 0.97f, front - 0.5f), new Vector3(0.55f, 0.55f, 0.55f), StructureMaterial.Timber, 251, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(-3.55f, 0.3f, front - 0.5f), new Vector3(0.6f, 0.6f, 0.6f), StructureMaterial.Timber, 252, StructureRole.Detail));
			CivicBarrel(d, new Vector3(2.75f, 0f, front - 0.5f), 253);
			Sack(d, new Vector3(2.1f, 0f, front - 0.45f), 0.62f, 0.8f, Quaternion.Euler(0f, 25f, 0f), 254);
			if (d.Fine)
			{
				// Iron straps across the loft door's leaves, and the tie beams across the store under the roof.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, (loftBottom + loftTop) * 0.5f + s * 0.45f, front - 0.09f), new Vector3(loftWidth - 0.1f, 0.05f, 0.02f), StructureMaterial.Iron, 255 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
				foreach (float z in new[] { -4f, 0f, 4f })
				{
					d.Add(StructureShapes.Beam(new Vector3(-halfW + wall, top - 0.15f, z), new Vector3(halfW - wall, top - 0.15f, z), 0.24f, 0.24f, StructureMaterial.Timber,
						260 + Mathf.RoundToInt(z / 4f + 1f), StructureRole.Detail));
				}
			}
		}

		// ── Stocks and pillory ────────────────────────────────────────

		private static void Pillory(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				Stocks(d);
				return;
			}
			// The pillory on its scaffold: a railed timber platform a metre up on posts, steps up its front, the post in the middle
			// with its head-and-hands board at a standing man's neck, a little hood of boards over it.
			const float half = 1.3f, deck = 1f;
			d.Add(StructureShapes.Box(new Vector3(0f, deck - 0.1f, 0f), new Vector3(half * 2f, 0.2f, half * 2f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * (half - 0.12f), -0.3f, sz * (half - 0.12f)), new Vector3(sx * (half - 0.12f), deck + 1f, sz * (half - 0.12f)), 0.18f, 0.18f,
						StructureMaterial.Timber, 2 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			Steps(d, new Vector3(0f, 0f, -half), Vector3.back, 1.2f, deck, StructureMaterial.Timber, 10);
			// Rails round the sides and back at hand height, and either side of the steps at the front.
			float railY = deck + 0.95f, edge = half - 0.12f;
			d.Add(StructureShapes.Beam(new Vector3(-edge, railY, edge), new Vector3(edge, railY, edge), 0.08f, 0.1f, StructureMaterial.Timber, 20));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * edge, railY, -edge), new Vector3(s * edge, railY, edge), 0.08f, 0.1f, StructureMaterial.Timber, 21 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * edge, railY, -edge), new Vector3(s * 0.66f, railY, -edge), 0.08f, 0.1f, StructureMaterial.Timber, 23 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * 0.66f, deck - 0.02f, -edge), new Vector3(s * 0.66f, railY + 0.05f, -edge), 0.1f, 0.1f, StructureMaterial.Timber, 25 + (s > 0 ? 1 : 0)));
			}
			// The post and its board, split along the holes, the upper half hinged and padlocked down.
			const float postZ = 0.45f;
			float boardY = deck + 1.5f;
			d.Add(StructureShapes.Beam(new Vector3(0f, deck - 0.05f, postZ), new Vector3(0f, deck + 2.25f, postZ), 0.2f, 0.2f, StructureMaterial.Timber, 30));
			d.Add(StructureShapes.Box(new Vector3(0f, boardY - 0.09f, postZ - 0.15f), new Vector3(1.3f, 0.18f, 0.09f), StructureMaterial.Timber, 31)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, boardY + 0.09f, postZ - 0.15f), new Vector3(1.3f, 0.18f, 0.09f), StructureMaterial.Timber, 32)).Grain = Vector3.right;
			if (d.Mid)
			{
				// The hood: two boards pitched over the post's head.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(new Vector3(s * 0.33f, deck + 2.33f, postZ - 0.1f), new Vector3(0.75f, 0.04f, 0.6f), Quaternion.Euler(0f, 0f, -s * 30f), StructureMaterial.Timber,
						33 + (s > 0 ? 1 : 0), StructureRole.Roof)).Grain = Vector3.forward;
				}
			}
			if (d.Fine)
			{
				// The neck hole and the wrists' either side (dark), the hinge and hasp.
				foreach ((float x, float r, int k) in new[] { (0f, 0.09f, 40), (-0.38f, 0.05f, 41), (0.38f, 0.05f, 42) })
				{
					d.Add(StructureShapes.Box(new Vector3(x, boardY, postZ - 0.2f), new Vector3(r * 2f, r * 2f, 0.01f), StructureMaterial.Ash, k, StructureRole.Detail));
				}
				d.Add(StructureShapes.Box(new Vector3(-0.6f, boardY, postZ - 0.2f), new Vector3(0.06f, 0.3f, 0.02f), StructureMaterial.Iron, 43, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0.6f, boardY + 0.02f, postZ - 0.21f), new Vector3(0.08f, 0.16f, 0.03f), StructureMaterial.Iron, 44, StructureRole.Detail));
			}
		}

		/// <summary>
		/// The stocks and the whipping post (2026-10-19): a low bench to sit the offender on, the leg board before it between two
		/// posts (split along its ankle holes, the upper half hinged and locked down), and beside them a tall post on its stone
		/// with iron shackles at wrist height and a cross-bar.
		/// </summary>
		private static void Stocks(StructureDraft d)
		{
			const float sx = -0.75f;
			// The bench.
			d.Add(StructureShapes.Box(new Vector3(sx, 0.42f, 0.2f), new Vector3(1.5f, 0.07f, 0.32f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(sx + s * 0.6f, 0.17f, 0.2f), new Vector3(0.08f, 0.5f, 0.28f), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0)));
			}
			// The leg board between its posts.
			const float boardZ = -0.45f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(sx + s * 0.78f, -0.3f, boardZ), new Vector3(sx + s * 0.78f, 0.8f, boardZ), 0.13f, 0.13f, StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3(sx, 0.3f, boardZ), new Vector3(1.45f, 0.18f, 0.09f), StructureMaterial.Timber, 6)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(sx, 0.48f, boardZ), new Vector3(1.45f, 0.18f, 0.09f), StructureMaterial.Timber, 7)).Grain = Vector3.right;
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(new Vector3(sx - 0.86f, 0.8f, boardZ), new Vector3(sx + 0.86f, 0.8f, boardZ), 0.1f, 0.12f, StructureMaterial.Timber, 8, StructureRole.Detail));
			}
			if (d.Fine)
			{
				for (int i = 0; i < 4; i++)
				{
					float x = sx - 0.45f + i * 0.3f;
					d.Add(StructureShapes.Box(new Vector3(x, 0.39f, boardZ - 0.05f), new Vector3(0.1f, 0.1f, 0.01f), StructureMaterial.Ash, 10 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Box(new Vector3(sx + 0.62f, 0.4f, boardZ - 0.06f), new Vector3(0.08f, 0.16f, 0.03f), StructureMaterial.Iron, 14, StructureRole.Detail));
			}
			// The whipping post on its stone, its cross-bar, the shackles.
			const float px = 1.05f;
			d.Add(RoughStone(d, new Vector3(px, -0.3f, 0f), new Vector3(0.6f, 0.45f, 0.6f), 20f, 20, 2));
			d.Add(StructureShapes.Beam(new Vector3(px, -0.3f, 0f), new Vector3(px, 2.6f, 0f), 0.22f, 0.22f, StructureMaterial.Timber, 21));
			d.Add(StructureShapes.Beam(new Vector3(px - 0.45f, 2.3f, 0f), new Vector3(px + 0.45f, 2.3f, 0f), 0.14f, 0.16f, StructureMaterial.Timber, 22));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(px + s * 0.08f, 1.95f, -0.13f), new Vector3(0.06f, 0.12f, 0.04f), StructureMaterial.Iron, 23 + (s > 0 ? 1 : 0), StructureRole.Detail));
					if (d.Fine)
					{
						d.Add(StructureShapes.Beam(new Vector3(px + s * 0.08f, 1.9f, -0.15f), new Vector3(px + s * 0.12f, 1.65f, -0.17f), 0.03f, 0.03f, StructureMaterial.Iron, 25 + (s > 0 ? 1 : 0), StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(px + s * 0.12f, 1.6f, -0.17f), new Vector3(0.1f, 0.06f, 0.06f), StructureMaterial.Iron, 27 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
		}

		// ── Market cross and fountain ─────────────────────────────────

		private static void MarketCross(StructureDraft d)
		{
			const float phase = Mathf.PI / 8f;
			if (d.Variant == 0)
			{
				// Four broad octagonal steps, each a seat's tread deep and a step's rise high (the controller climbs them).
				float[] radii = { 2.8f, 2.3f, 1.8f, 1.3f };
				for (int i = 0; i < radii.Length; i++)
				{
					float y0 = i == 0 ? -FoundationDepth : i * StepRise, y1 = (i + 1) * StepRise;
					d.Add(StructureShapes.Lathe(new[] { new Vector2(radii[i], y0), new Vector2(radii[i], y1) }, 8, StructureMaterial.Stone, 1 + i, false, phase));
				}
				float socket = 4 * StepRise;
				// The socket stone, its corners chamfered; the shaft, octagonal and tapering; the lantern head.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, socket + 0.3f, 0f), new Vector3(0.95f, 0.6f, 0.95f), d.Bevel(0.06f), StructureMaterial.Stone, 10));
				float shaft0 = socket + 0.6f, shaft1 = shaft0 + 4.2f;
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.27f, shaft0 - 0.05f), new Vector2(0.19f, shaft1 + 0.05f) }, 8, StructureMaterial.Monolith, 11, false, phase));
				const float head = 0.6f, headH = 0.7f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, shaft1 - 0.05f, 0f), new Vector3(0.5f, 0.12f, 0.5f), d.Bevel(0.02f), StructureMaterial.Monolith, 12));
				d.Add(StructureShapes.Box(new Vector3(0f, shaft1 + headH * 0.5f, 0f), new Vector3(head, headH, head), StructureMaterial.Monolith, 13));
				// Its four gablets, two ridges crossing over the head, and the pinnacle and its ball over them.
				float eave = shaft1 + headH;
				var gablet = new[] { new Vector2(-head * 0.5f - 0.04f, eave), new Vector2(head * 0.5f + 0.04f, eave), new Vector2(0f, eave + 0.38f) };
				d.Add(StructureShapes.Prism(gablet, -head * 0.5f - 0.04f, head * 0.5f + 0.04f, StructureMaterial.Monolith, 14));
				d.Add(StructureShapes.Prism(gablet, -head * 0.5f - 0.04f, head * 0.5f + 0.04f, StructureMaterial.Monolith, 15).Transform(Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f))));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, eave + 0.2f), new Vector2(0f, eave + 0.95f) }, 4, StructureMaterial.Monolith, 16, false, Mathf.PI * 0.25f));
				if (d.Mid)
				{
					d.Add(StructureShapes.Sphere(new Vector3(0f, eave + 1f, 0f), new Vector3(0.09f, 0.09f, 0.09f), d.Seg(8, 5), 4, StructureMaterial.Monolith, 17, StructureRole.Detail));
					// A carved band round the shaft's foot, and the head's four niches, each a saint's or the town's carving in shadow.
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.31f, shaft0), new Vector2(0.31f, shaft0 + 0.14f) }, 8, StructureMaterial.Monolith, 18, false, phase, false, false, StructureRole.Detail));
					for (int f = 0; f < 4; f++)
					{
						Quaternion q = Quaternion.Euler(0f, f * 90f, 0f);
						d.Add(StructureShapes.Block(q * new Vector3(0f, shaft1 + headH * 0.48f, -head * 0.5f - 0.005f), new Vector3(0.3f, 0.44f, 0.02f), q, StructureMaterial.Ash, 20 + f, StructureRole.Detail));
					}
				}
				if (d.Fine)
				{
					// Crockets up the pinnacle's corners, and pinnacles on the socket stone's corners.
					for (int c = 0; c < 4; c++)
					{
						float a = (c * 90f + 45f) * Mathf.Deg2Rad;
						var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
						d.Add(StructureShapes.Block(dir * 0.12f + Vector3.up * (eave + 0.5f), new Vector3(0.06f, 0.08f, 0.06f), Quaternion.identity, StructureMaterial.Monolith, 30 + c, StructureRole.Detail));
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.08f, socket + 0.6f), new Vector2(0f, socket + 0.95f) }, 4, StructureMaterial.Stone, 34 + c, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
							.Transform(Matrix4x4.Translate(dir * 0.55f)));
					}
				}
				return;
			}
			// The fountain: an octagonal basin, its rim a seat's height, dark water in it (Iron, as the well's water is: the glass
			// a window is glazed with glows warm at night); the pillar in the middle with its spouts, a bowl and a finial.
			const float outer = 2.6f, inner = 2.25f, rim = 0.55f, water = 0.42f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(outer, -0.4f), new Vector2(outer, rim), new Vector2(inner, rim), new Vector2(inner, -0.4f) }, 8, StructureMaterial.Stone, 1, false, phase, true));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(inner + 0.02f, -0.4f), new Vector2(inner + 0.02f, water) }, 8, StructureMaterial.Iron, 2, false, phase));
			if (d.Mid)
			{
				// The rim's coping, a little proud both sides.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(outer + 0.08f, rim - 0.04f), new Vector2(outer + 0.08f, rim + 0.08f), new Vector2(inner - 0.06f, rim + 0.08f), new Vector2(inner - 0.06f, rim - 0.04f) },
					8, StructureMaterial.Stone, 3, false, phase, true, false, StructureRole.Detail));
			}
			int seg = d.Seg(12, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.55f, water - 0.1f), new Vector2(0.55f, 0.95f) }, 8, StructureMaterial.Stone, 4, false, phase));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.24f, 0.9f), new Vector2(0.2f, 1.95f) }, seg, StructureMaterial.Monolith, 5));
			// The bowl, its own water in it, and the finial over it.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.18f, 1.85f), new Vector2(0.7f, 2.12f), new Vector2(0.82f, 2.3f), new Vector2(0f, 2.3f) }, seg, StructureMaterial.Stone, 6));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, 2.25f), new Vector2(0.1f, 2.75f) }, seg, StructureMaterial.Monolith, 7));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 2.86f, 0f), new Vector3(0.15f, 0.17f, 0.15f), seg, d.Fine ? 6 : 4, StructureMaterial.Monolith, 8));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.72f, 2.26f), new Vector2(0.72f, 2.31f) }, seg, StructureMaterial.Iron, 9, true, 0f, false, false, StructureRole.Detail));
				// Four spouts out of the pedestal, iron pipes from carved heads, turned a little down to the water.
				for (int f = 0; f < 4; f++)
				{
					float a = f * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
					var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					d.Add(StructureShapes.Block(dir * 0.55f + Vector3.up * 0.78f, new Vector3(0.22f, 0.22f, 0.16f), Quaternion.LookRotation(dir, Vector3.up), StructureMaterial.Monolith, 10 + f, StructureRole.Detail));
					d.Add(StructureShapes.Beam(dir * 0.6f + Vector3.up * 0.76f, dir * 0.92f + Vector3.up * 0.7f, 0.05f, 0.05f, StructureMaterial.Iron, 14 + f, StructureRole.Detail));
				}
			}
		}
	}
}
#endif
