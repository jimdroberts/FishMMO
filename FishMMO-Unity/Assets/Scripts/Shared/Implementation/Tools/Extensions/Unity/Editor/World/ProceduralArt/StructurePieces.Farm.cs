#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The town's other buildings (2026-10-19: "more structure types for towns, villages, and cities"): the farm's barns, ricks, stooks, scarecrows, sties and hen houses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> "Barns and farmland (we have fenced areas but they aren't really farm land areas)": the fields round a
	/// settlement (PointOfInterestFields) were a crop painted into the ground inside a fence. What makes a country read as
	/// worked is what stands in it: the farmstead's barn and its ricks, the sties and hen houses round the yard, the corn
	/// stood up in stooks after the reaping, a scarecrow over the growing corn, a trough in the pasture.
	/// </para>
	/// <para>
	/// <b>The record.</b> The great barns are the medieval farm's biggest buildings: aisled timber barns weatherboarded on a
	/// stone footing (Cressing Temple's, the thirteenth century), and the stone tithe barns of the abbeys and manors, buttressed,
	/// slit-lit, their wagons let in through a projecting porch (Great Coxwell, Bradford on Avon). Both kept a threshing floor
	/// between opposed doors, the through-draught winnowing the chaff. Corn was reaped, bound in sheaves and stood in stooks to
	/// dry; what was carted in was stacked in ricks, round on a stone-legged staddle base against rats or long and ridged,
	/// thatched and roped down with stones.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>A barn's threshing floor over the ground, metres: one riser, no step to trip a cart.</summary>
		private const float BarnFloor = 0.2f;

		private static void AddFarm(List<StructurePiece> list)
		{
			Piece(list, "Barn", StructureStyle.Timber, StructureSize.Building,
				"A great barn with a threshing floor between opposed doors: v0 a weatherboarded aisled timber barn 20×11 m on a fieldstone footing, a 3.8 m cart door in its long side and a wicket door behind, a steep thatched roof with owl holes in the gables and a pitching door; v1 a buttressed stone tithe barn 21×10 m, slit-lit, its carts let in through a projecting porch, stone-slated.",
				Barn, "building", "farm", "barn", "farmstead");
			// The burnt-out barn of an abandoned farm (a raided one's charred finish), made from the timber barn as every ruin is from its piece
			// (both its variants: the tithe barn's ruin, its rubble on its buttresses, ran over a building's budget).
			Ruin(list, "RuinedBarn", "Barn", 1, 2, 0.55f, StructureSize.Building, "A barn fallen in, its roof gone and its walls broken; pairs well with the charred finish.",
				"ruin", "barn", "building");
			Piece(list, "HayRick", StructureStyle.Timber, StructureSize.Medium,
				"A rick of hay or corn: v0 round on a staddle-stone base, a thatched cone cap roped down with stones and a ladder against it; v1 long and ridged on a brushwood bed, its hipped thatch roped, a ladder and a pitchfork.",
				HayRick, "haystack", "farm", "clutter");
			StructurePiece stooks = Piece(list, "Stooks", StructureStyle.Timber, StructureSize.Small,
				"Reaped corn stood up to dry in a short row along x: v0 three stooks; v1 a pair, a fallen sheaf and the sickle left by it.",
				Stooks, "stooks", "farm", "clutter");
			stooks.Collides = false;
			Piece(list, "Scarecrow", StructureStyle.Timber, StructureSize.Small,
				"A scarecrow on a cross over the growing corn: v0 a rag coat, straw at its cuffs, a turnip head under a straw hat; v1 a sack head under a felt hat, a crow perched on its arm.",
				Scarecrow, "scarecrow", "farm", "clutter");
			Piece(list, "Pigsty", StructureStyle.Stone, StructureSize.Medium,
				"A pig sty, its run in front (−z): v0 a low rubble-stone sty under a stone-slab roof and a walled run with a stone trough; v1 a plank sty under a lean-to of boards and a run of wattle hurdles with a log trough.",
				Pigsty, "pigsty", "farm", "farmstead");
			Piece(list, "HenHouse", StructureStyle.Timber, StructureSize.Medium,
				"A hen house: v0 a plank coop raised on legs against foxes, a ramp to its pop hole and nest boxes on its side; v1 a wattle hen house on the ground under a thatched roof.",
				HenHouse, "henhouse", "farm", "farmstead");
			Piece(list, "Sacks", StructureStyle.Timber, StructureSize.Small,
				"Grain sacks, about 1.2×0.8 m, back (+z) to a wall: v0 tied sacks stacked two and one with one standing; v1 three on a pallet, one open with a scoop in it.",
				Sacks, "sacks", "furniture");
			Piece(list, "Trough", StructureStyle.Timber, StructureSize.Small,
				"A water trough, 2 m along x: v0 a plank trough on trestle legs; v1 a stone trough on two stones.",
				Trough, "farm", "clutter");
		}

		// ── Barn ──────────────────────────────────────────────────────

		private static void Barn(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				TitheBarn(d);
				return;
			}
			const float width = 20f, depth = 11f, wall = 0.25f, wallHeight = 4.4f;
			const float floor = BarnFloor, top = floor + wallHeight;
			// The cart door a loaded wain goes through (3.8 m: two leaves), the wicket door opposite across the threshing floor.
			const float cartWidth = 3.8f, cartHeight = 3.8f, backWidth = 1.6f, backHeight = 2.4f;
			float hw = width * 0.5f, hd = depth * 0.5f, t2 = wall * 0.5f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(width + 0.3f, floor + FoundationDepth, depth + 0.3f),
					d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
			}
			var front = new List<WallOpening> { new WallOpening { Along = hw, Width = cartWidth, Bottom = floor - 0.01f, Top = floor + cartHeight } };
			var back = new List<WallOpening> { new WallOpening { Along = hw, Width = backWidth, Bottom = floor - 0.01f, Top = floor + backHeight } };
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-hw, 0f, -hd + t2), new Vector3(hw, 0f, -hd + t2), wall, floor, top, front, StructureMaterial.Timber, 10);
				WallRun(d, new Vector3(-hw, 0f, hd - t2), new Vector3(hw, 0f, hd - t2), wall, floor, top, back, StructureMaterial.Timber, 11);
				WallRun(d, new Vector3(-hw + t2, 0f, -hd + wall), new Vector3(-hw + t2, 0f, hd - wall), wall, floor, top, null, StructureMaterial.Timber, 12);
				WallRun(d, new Vector3(hw - t2, 0f, -hd + wall), new Vector3(hw - t2, 0f, hd - wall), wall, floor, top, null, StructureMaterial.Timber, 13);
			}

			// The roof: thatch at 50°, its ridge along the barn, the gables under it at each end; at its palette's pitch, or a flat
			// canopy over the walls (StructureRoofs, I6h).
			Matrix4x4 ridgeAlongX = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			const float rise = 6.5f, overhang = 0.7f;
			OwnRoofPlan roof = OwnGable(d, depth, width, top, rise, overhang, 0.32f, StructureMaterial.Thatch, 20, ridgeAlongX);
			if (!roof.Flat)
			{
				using (d.Builds(WallRoles))
				{
					Gable(d, depth - 0.02f, top, roof.Rise - 0.02f, -hw + t2, wall, StructureMaterial.Timber, 24, ridgeAlongX);
					Gable(d, depth - 0.02f, top, roof.Rise - 0.02f, hw - t2, wall, StructureMaterial.Timber, 25, ridgeAlongX);
				}
				if (d.Fine)
				{
					// The thatch's ridge roll.
					using (d.Builds(RoofRoles))
					{
						d.Add(StructureShapes.Beam(new Vector3(-hw - overhang, top + roof.Rise + 0.1f, 0f), new Vector3(hw + overhang, top + roof.Rise + 0.1f, 0f), 0.24f, 0.24f,
							StructureMaterial.Thatch, 26, StructureRole.Roof));
					}
				}
				if (d.Mid)
				{
					// An owl hole high in each gable, a diamond with a perch under it: a barn owl kept the rats down.
					for (int s = -1; s <= 1; s += 2)
					{
						float y = top + roof.Rise * 0.42f;
						d.Add(StructureShapes.Block(new Vector3(s * (hw + 0.012f), y, 0f), new Vector3(0.04f, 0.32f, 0.32f), Quaternion.Euler(45f, 0f, 0f), StructureMaterial.Ash, 27 + (s > 0 ? 1 : 0), StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(s * (hw + 0.12f), y - 0.27f, 0f), new Vector3(0.24f, 0.05f, 0.42f), StructureMaterial.Timber, 29 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}

			// The doors: two leaves in the cart door, a single one in the wicket behind. The threshing floor runs between them.
			HangDoor(d, new Vector3(0f, floor, -hd + wall - 0.05f), Vector3.right, Vector3.forward, cartWidth, cartHeight, StructureDoorKind.Planks);
			HangDoor(d, new Vector3(0f, floor, hd - wall + 0.05f), Vector3.left, Vector3.back, backWidth, backHeight, StructureDoorKind.Planks);

			// The aisles: a row of posts down each side of the nave under arcade plates, tie beams across, braces at the heads. The
			// middle bay is the threshing floor, wide enough between its posts for the cart.
			float aisle = hd - 2.8f;
			float[] bays = { -6.9f, -2.3f, 2.3f, 6.9f };
			int k = 0;
			foreach (float x in bays)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(x, (floor + top) * 0.5f, s * aisle), new Vector3(0.28f, wallHeight, 0.28f), StructureMaterial.Timber, 40 + k));
					if (d.Fine)
					{
						for (int b = -1; b <= 1; b += 2)
						{
							d.Add(StructureShapes.Beam(new Vector3(x + b * 0.1f, top - 1.3f, s * aisle), new Vector3(x + b * 1.05f, top - 0.24f, s * aisle), 0.12f, 0.14f,
								StructureMaterial.Timber, 60 + k * 2 + (b > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
					k++;
				}
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, top - 0.1f, -hd + wall), new Vector3(x, top - 0.1f, hd - wall), 0.24f, 0.24f, StructureMaterial.Timber, 80 + k, StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(-hw + wall, top - 0.13f, s * aisle), new Vector3(hw - wall, top - 0.13f, s * aisle), 0.24f, 0.26f,
						StructureMaterial.Timber, 90 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The room: hay, sacks and stores in the aisles and end bays; the threshing floor between the doors kept clear, and a
			// walk down each arcade, so nothing stacked against a post closes off the aisle behind it (the furnishing does not know
			// the posts; a body passes them with the walk's width either side).
			d.Room(new Vector3(0f, floor, 0f), new Vector2(width - 2f * wall, depth - 2f * wall), wallHeight, StructureRoomKind.Barn);
			d.RoomClear(new Vector2(-2.1f, -hd), new Vector2(2.1f, hd));
			for (int s = -1; s <= 1; s += 2)
			{
				d.RoomClear(new Vector2(-hw, s * aisle - 1f), new Vector2(hw, s * aisle + 1f));
			}
			BarnCorners(d, hw - wall, hd - wall);

			if (!d.Mid)
			{
				return;
			}
			// The frame that shows outside: corner posts, the wall plate under the eaves, the sole plate on the footing (broken at
			// the doors), the cart door's heavy jambs and lintel and the wicket's frame.
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * (hw + 0.02f), floor, sz * (hd + 0.02f)), new Vector3(sx * (hw + 0.02f), top, sz * (hd + 0.02f)), 0.26f, 0.26f,
						StructureMaterial.Timber, 100 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0), StructureRole.Detail));
				}
				d.Add(StructureShapes.Beam(new Vector3(-hw - 0.05f, top - 0.1f, sx * (hd + 0.05f)), new Vector3(hw + 0.05f, top - 0.1f, sx * (hd + 0.05f)), 0.2f, 0.2f,
					StructureMaterial.Timber, 104 + (sx > 0 ? 1 : 0), StructureRole.Detail));
			}
			SolePlate(d, new Vector3(-hw, 0f, -hd - 0.05f), new Vector3(hw, 0f, -hd - 0.05f), floor + 0.09f, front, 106);
			SolePlate(d, new Vector3(-hw, 0f, hd + 0.05f), new Vector3(hw, 0f, hd + 0.05f), floor + 0.09f, back, 110);
			DoorFrame(d, new Vector3(0f, floor, -hd - 0.06f), Vector3.right, cartWidth, cartHeight, 0.28f, 114);
			DoorFrame(d, new Vector3(0f, floor, hd + 0.06f), Vector3.left, backWidth, backHeight, 0.18f, 118);
			// The pitching door high in the east end, where the hay was forked in from the cart.
			d.Add(StructureShapes.Box(new Vector3(hw + 0.04f, top - 0.9f, 0f), new Vector3(0.06f, 1.2f, 1.3f), StructureMaterial.Timber, 122, StructureRole.Detail)).Grain = Vector3.up;
			if (!d.Fine)
			{
				return;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(hw + 0.08f, top - 0.9f + s * 0.38f, -0.2f), new Vector3(0.025f, 0.06f, 0.9f), StructureMaterial.Iron, 123 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			// Weatherboarding: lapped boards along every wall, broken round the doors.
			const float lap = 0.42f;
			Laps(d, new Vector3(-hw, 0f, -hd - 0.015f), new Vector3(hw, 0f, -hd - 0.015f), floor + 0.35f, top - 0.25f, lap, front, 2000);
			Laps(d, new Vector3(-hw, 0f, hd + 0.015f), new Vector3(hw, 0f, hd + 0.015f), floor + 0.35f, top - 0.25f, lap, back, 2100);
			Laps(d, new Vector3(-hw - 0.015f, 0f, -hd), new Vector3(-hw - 0.015f, 0f, hd), floor + 0.35f, top - 0.25f, lap, null, 2200);
			Laps(d, new Vector3(hw + 0.015f, 0f, -hd), new Vector3(hw + 0.015f, 0f, hd), floor + 0.35f, top - 0.25f, lap, null, 2300);
		}

		/// <summary>
		/// A stone tithe barn (Barn v1): buttressed rubble-stone walls 0.7 m thick, slit windows, a stone-slate roof, and its carts
		/// let in through a porch projecting from the long side, open to the weather, the great doors hung in the main wall at its
		/// back (so they open straight into the barn's floor) and the wicket door opposite.
		/// </summary>
		private static void TitheBarn(StructureDraft d)
		{
			const float width = 21f, depth = 10f, wall = 0.7f, wallHeight = 5f;
			const float floor = BarnFloor, top = floor + wallHeight;
			const float cartWidth = 4f, cartHeight = 4.2f, backWidth = 1.6f, backHeight = 2.5f;
			// The porch: 7 m across and 4 deep before the main wall, its arch wider and higher than the doors behind it.
			const float porchWidth = 7f, porchDepth = 4f, porchWall = 0.6f, archWidth = 4.4f, archHeight = 4.6f;
			float hw = width * 0.5f, hd = depth * 0.5f, t2 = wall * 0.5f;
			float porchFront = -hd - porchDepth, porchHalf = porchWidth * 0.5f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(width + 0.3f, floor + FoundationDepth, depth + 0.3f),
					d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, -hd - porchDepth * 0.5f - 0.05f), new Vector3(porchWidth + 0.3f, floor + FoundationDepth, porchDepth + 0.1f),
					d.Bevel(0.04f), StructureMaterial.Fieldstone, 2));
			}
			var front = new List<WallOpening> { new WallOpening { Along = hw, Width = cartWidth, Bottom = floor - 0.01f, Top = floor + cartHeight } };
			var back = new List<WallOpening> { new WallOpening { Along = hw, Width = backWidth, Bottom = floor - 0.01f, Top = floor + backHeight } };
			var arch = new List<WallOpening> { new WallOpening { Along = porchHalf, Width = archWidth, Bottom = floor - 0.01f, Top = floor + archHeight } };
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-hw, 0f, -hd + t2), new Vector3(hw, 0f, -hd + t2), wall, floor, top, front, StructureMaterial.Stone, 10);
				WallRun(d, new Vector3(-hw, 0f, hd - t2), new Vector3(hw, 0f, hd - t2), wall, floor, top, back, StructureMaterial.Stone, 11);
				WallRun(d, new Vector3(-hw + t2, 0f, -hd + wall), new Vector3(-hw + t2, 0f, hd - wall), wall, floor, top, null, StructureMaterial.Stone, 12);
				WallRun(d, new Vector3(hw - t2, 0f, -hd + wall), new Vector3(hw - t2, 0f, hd - wall), wall, floor, top, null, StructureMaterial.Stone, 13);
				// The porch's front with its arch, and its two sides back to the main wall.
				WallRun(d, new Vector3(-porchHalf, 0f, porchFront + porchWall * 0.5f), new Vector3(porchHalf, 0f, porchFront + porchWall * 0.5f), porchWall, floor, top, arch,
					StructureMaterial.Stone, 14);
				WallRun(d, new Vector3(-porchHalf + porchWall * 0.5f, 0f, porchFront + porchWall), new Vector3(-porchHalf + porchWall * 0.5f, 0f, -hd), porchWall, floor, top, null,
					StructureMaterial.Stone, 15);
				WallRun(d, new Vector3(porchHalf - porchWall * 0.5f, 0f, porchFront + porchWall), new Vector3(porchHalf - porchWall * 0.5f, 0f, -hd), porchWall, floor, top, null,
					StructureMaterial.Stone, 16);
			}

			// The roofs, stone slates at 45° (the stone's own pitch; they do not follow a palette's), the main ridge along the barn
			// and the porch's square to it, run back into the main roof until its ridge is under it.
			Matrix4x4 ridgeAlongX = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			const float rise = 5f, porchRise = 3.5f;
			GableRoof(d, depth, width, top, rise, 0.45f, 0.22f, StructureMaterial.Stone, 20, ridgeAlongX);
			float porchBack = -hd + porchRise + 0.4f;
			float porchLength = porchBack - porchFront, porchMiddle = (porchBack + porchFront) * 0.5f;
			Matrix4x4 porchFrame = Matrix4x4.Translate(new Vector3(0f, 0f, porchMiddle));
			GableRoof(d, porchWidth, porchLength, top, porchRise, 0.35f, 0.2f, StructureMaterial.Stone, 22, porchFrame);
			using (d.Builds(WallRoles))
			{
				Gable(d, depth - 0.02f, top, rise - 0.02f, -hw + t2, wall, StructureMaterial.Stone, 24, ridgeAlongX);
				Gable(d, depth - 0.02f, top, rise - 0.02f, hw - t2, wall, StructureMaterial.Stone, 25, ridgeAlongX);
				// The porch's gable over its arch, and the one on the main wall's head behind it that shuts its roof off from the barn's.
				Gable(d, porchWidth - 0.02f, top, porchRise - 0.02f, porchFront + porchWall * 0.5f - porchMiddle, porchWall, StructureMaterial.Stone, 26, porchFrame);
				Gable(d, porchWidth - 0.02f, top, porchRise - 0.02f, -hd + t2 - porchMiddle, wall, StructureMaterial.Stone, 27, porchFrame);
			}

			HangDoor(d, new Vector3(0f, floor, -hd + wall - 0.05f), Vector3.right, Vector3.forward, cartWidth, cartHeight, StructureDoorKind.Studded);
			HangDoor(d, new Vector3(0f, floor, hd - wall + 0.05f), Vector3.left, Vector3.back, backWidth, backHeight, StructureDoorKind.Planks);
			d.Room(new Vector3(0f, floor, 0f), new Vector2(width - 2f * wall, depth - 2f * wall), wallHeight, StructureRoomKind.Barn);
			d.RoomClear(new Vector2(-2.3f, -hd), new Vector2(2.3f, hd));
			BarnCorners(d, hw - wall, hd - wall);

			// Buttresses: along both long walls between the bays, clear of the porch and the wicket, and two on each end wall.
			int n = 0;
			foreach (float x in new[] { -9.6f, -6.2f, 6.2f, 9.6f })
			{
				Buttress(d, new Vector3(x, 0f, -hd), Vector3.back, top, 200 + n++ * 8);
			}
			foreach (float x in new[] { -9.6f, -6.2f, -2.8f, 2.8f, 6.2f, 9.6f })
			{
				Buttress(d, new Vector3(x, 0f, hd), Vector3.forward, top, 200 + n++ * 8);
			}
			for (int s = -1; s <= 1; s += 2)
			{
				foreach (float z in new[] { -2.6f, 2.6f })
				{
					Buttress(d, new Vector3(s * hw, 0f, z), s > 0 ? Vector3.right : Vector3.left, top, 200 + n++ * 8);
				}
			}

			if (!d.Mid)
			{
				return;
			}
			// Slit windows between the buttresses: dark splayed slots, a stone dressing round each.
			int w = 0;
			void Slit(Vector3 at, Vector3 outward)
			{
				Quaternion turn = Quaternion.LookRotation(outward);
				d.Add(StructureShapes.Block(at + outward * 0.012f, new Vector3(0.16f, 1.2f, 0.04f), turn, StructureMaterial.Ash, 400 + w * 2, StructureRole.Detail));
				if (d.Fine)
				{
					d.Add(StructureShapes.Block(at + outward * 0.02f, new Vector3(0.42f, 1.5f, 0.03f), turn, StructureMaterial.Stone, 401 + w * 2, StructureRole.Detail));
				}
				w++;
			}
			foreach (float x in new[] { -7.9f, -4.6f, 4.6f, 7.9f })
			{
				Slit(new Vector3(x, floor + 3f, -hd), Vector3.back);
			}
			foreach (float x in new[] { -7.9f, -4.5f, 4.5f, 7.9f })
			{
				Slit(new Vector3(x, floor + 3f, hd), Vector3.forward);
			}
			for (int s = -1; s <= 1; s += 2)
			{
				Slit(new Vector3(s * hw, floor + 3.2f, 0f), s > 0 ? Vector3.right : Vector3.left);
				// An owl hole high in each gable.
				d.Add(StructureShapes.Block(new Vector3(s * (hw + 0.012f), top + rise * 0.45f, 0f), new Vector3(0.04f, 0.3f, 0.3f), Quaternion.Euler(45f, 0f, 0f), StructureMaterial.Ash, 440 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			// Tie beams across the nave over head height, the porch's arch voussoirs' hood and the ridge's crest.
			foreach (float x in new[] { -7f, -3.5f, 3.5f, 7f })
			{
				d.Add(StructureShapes.Beam(new Vector3(x, top - 0.15f, -hd + wall), new Vector3(x, top - 0.15f, hd - wall), 0.3f, 0.3f, StructureMaterial.Timber, 450 + (int)(x + 10f), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, floor + archHeight + 0.2f, porchFront - 0.06f), new Vector3(archWidth + 0.8f, 0.3f, 0.14f), StructureMaterial.Stone, 470, StructureRole.Detail));
			if (!d.Fine)
			{
				return;
			}
			// A stone cross on each main gable's apex and on the porch's (the abbey's barn), and a coped crest along the ridge.
			void Cross(Vector3 foot, Vector3 across, int key)
			{
				d.Add(StructureShapes.Beam(foot, foot + Vector3.up * 0.9f, 0.14f, 0.14f, StructureMaterial.Stone, key, StructureRole.Roof));
				d.Add(StructureShapes.Beam(foot + Vector3.up * 0.6f - across * 0.3f, foot + Vector3.up * 0.6f + across * 0.3f, 0.12f, 0.12f, StructureMaterial.Stone, key + 1, StructureRole.Roof));
			}
			Cross(new Vector3(-hw + t2, top + rise + 0.1f, 0f), Vector3.forward, 480);
			Cross(new Vector3(hw - t2, top + rise + 0.1f, 0f), Vector3.forward, 482);
			Cross(new Vector3(0f, top + porchRise + 0.1f, porchFront + porchWall * 0.5f), Vector3.right, 484);
			d.Add(StructureShapes.Beam(new Vector3(-hw - 0.45f, top + rise + 0.12f, 0f), new Vector3(hw + 0.45f, top + rise + 0.12f, 0f), 0.26f, 0.18f, StructureMaterial.Stone, 490, StructureRole.Roof));
		}

		/// <summary>
		/// Keeps a barn's four inside corners clear of its stores (to <paramref name="hx"/>, <paramref name="hz"/>, its walls' inner
		/// faces): what is stacked along two walls then never meets in a corner and shuts a pocket of floor in behind it.
		/// </summary>
		private static void BarnCorners(StructureDraft d, float hx, float hz)
		{
			const float corner = 1.6f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.RoomClear(new Vector2(sx * hx, sz * hz), new Vector2(sx * (hx - corner), sz * (hz - corner)));
				}
			}
		}

		/// <summary>
		/// A stepped stone buttress against a wall: its foot on the wall's outer face at <paramref name="foot"/> (ground level),
		/// standing out along <paramref name="outward"/>, in two stages to <paramref name="top"/>'s three quarters, each with a sloped
		/// weathering, its footing down into the ground.
		/// </summary>
		private static void Buttress(StructureDraft d, Vector3 foot, Vector3 outward, float top, int key)
		{
			Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
			float lower = top * 0.45f, upper = top * 0.78f;
			d.Add(StructureShapes.Block(foot + outward * 0.5f + Vector3.up * ((lower - FoundationDepth) * 0.5f), new Vector3(0.85f, lower + FoundationDepth, 1f), turn,
				StructureMaterial.Stone, key, StructureRole.Structure, d.Bevel(0.03f)));
			d.Add(StructureShapes.Block(foot + outward * 0.3f + Vector3.up * ((lower + upper) * 0.5f), new Vector3(0.7f, upper - lower, 0.6f), turn,
				StructureMaterial.Stone, key + 1, StructureRole.Structure, d.Bevel(0.03f)));
			if (d.Mid)
			{
				// The weatherings: slabs sloped down and out over each stage's head.
				Quaternion slope = turn * Quaternion.Euler(28f, 0f, 0f);
				d.Add(StructureShapes.Block(foot + outward * 0.82f + Vector3.up * (lower + 0.05f), new Vector3(0.9f, 0.14f, 0.5f), slope, StructureMaterial.Stone, key + 2, StructureRole.Detail));
				d.Add(StructureShapes.Block(foot + outward * 0.32f + Vector3.up * (upper + 0.12f), new Vector3(0.75f, 0.14f, 0.66f), slope, StructureMaterial.Stone, key + 3, StructureRole.Detail));
			}
		}

		/// <summary>The sole plate along a timber wall's foot, outside it, from <paramref name="a"/> to <paramref name="b"/> at <paramref name="y"/>, broken where a doorway's foot is.</summary>
		private static void SolePlate(StructureDraft d, Vector3 a, Vector3 b, float y, List<WallOpening> holes, int key)
		{
			Vector3 run = new Vector3(b.x - a.x, 0f, b.z - a.z);
			float length = run.magnitude;
			Vector3 dir = run / Mathf.Max(1e-4f, length);
			float from = 0f;
			int n = 0;
			if (holes != null)
			{
				foreach (WallOpening o in holes)
				{
					if (o.Bottom > y + 0.1f)
					{
						continue;
					}
					float to = o.Along - o.Width * 0.5f;
					if (to - from > 0.05f)
					{
						d.Add(StructureShapes.Beam(a + dir * from + Vector3.up * y, a + dir * to + Vector3.up * y, 0.2f, 0.18f, StructureMaterial.Timber, key + n++, StructureRole.Detail));
					}
					from = Mathf.Max(from, o.Along + o.Width * 0.5f);
				}
			}
			if (length - from > 0.05f)
			{
				d.Add(StructureShapes.Beam(a + dir * from + Vector3.up * y, a + dir * length + Vector3.up * y, 0.2f, 0.18f, StructureMaterial.Timber, key + n, StructureRole.Detail));
			}
		}

		/// <summary>A doorway's frame outside its wall: a jamb post either side (clear of the hole) and a lintel over it.</summary>
		private static void DoorFrame(StructureDraft d, Vector3 foot, Vector3 across, float width, float height, float post, int key)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 at = foot + across * (s * (width * 0.5f + post * 0.5f));
				d.Add(StructureShapes.Beam(at, at + Vector3.up * (height + post), post, post, StructureMaterial.Timber, key + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			Vector3 head = foot + Vector3.up * (height + post * 0.6f);
			d.Add(StructureShapes.Beam(head - across * (width * 0.5f + post), head + across * (width * 0.5f + post), post, post, StructureMaterial.Timber, key + 2, StructureRole.Detail));
		}

		/// <summary>
		/// Weatherboarding on a wall's outer face: lapped boards from <paramref name="a"/> to <paramref name="b"/> (the face's line)
		/// every <paramref name="spacing"/> metres between two heights, each broken where it would cross a hole.
		/// </summary>
		private static void Laps(StructureDraft d, Vector3 a, Vector3 b, float y0, float y1, float spacing, List<WallOpening> holes, int key)
		{
			Vector3 run = new Vector3(b.x - a.x, 0f, b.z - a.z);
			float length = run.magnitude;
			Vector3 dir = run / Mathf.Max(1e-4f, length);
			int n = 0;
			for (float y = y0; y <= y1 + 1e-3f; y += spacing)
			{
				float from = 0f;
				if (holes != null)
				{
					foreach (WallOpening o in holes)
					{
						if (y < o.Bottom - 0.05f || y > o.Top + 0.05f)
						{
							continue;
						}
						float to = o.Along - o.Width * 0.5f - 0.05f;
						if (to - from > 0.1f)
						{
							d.Add(StructureShapes.Beam(a + dir * from + Vector3.up * y, a + dir * to + Vector3.up * y, 0.035f, 0.08f, StructureMaterial.Timber, key + n++, StructureRole.Detail));
						}
						from = Mathf.Max(from, o.Along + o.Width * 0.5f + 0.05f);
					}
				}
				if (length - from > 0.1f)
				{
					d.Add(StructureShapes.Beam(a + dir * from + Vector3.up * y, a + dir * length + Vector3.up * y, 0.035f, 0.08f, StructureMaterial.Timber, key + n++, StructureRole.Detail));
				}
			}
		}

		// ── Ricks and stooks ──────────────────────────────────────────

		private static void HayRick(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				RoundRick(d);
			}
			else
			{
				LongRick(d);
			}
		}

		/// <summary>A round rick on its staddle base: five mushroom stones, a timber floor on them, the stack, its thatched cap roped down with stones, a ladder.</summary>
		private static void RoundRick(StructureDraft d)
		{
			var staddle = new[] { new Vector2(0f, -0.2f), new Vector2(0.22f, -0.2f), new Vector2(0.16f, 0.5f), new Vector2(0.42f, 0.55f), new Vector2(0.4f, 0.68f), new Vector2(0f, 0.68f) };
			for (int i = 0; i < 5; i++)
			{
				float a = (45f + i * 90f) * Mathf.Deg2Rad;
				Vector3 at = i == 4 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f;
				d.Add(StructureShapes.Lathe(staddle, d.Seg(8, 5), StructureMaterial.Stone, 1 + i).Transform(Matrix4x4.Translate(at)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-2.1f, 0.76f, 0f), new Vector3(2.1f, 0.76f, 0f), 0.18f, 0.16f, StructureMaterial.Timber, 10));
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.76f, -2.1f), new Vector3(0f, 0.76f, 2.1f), 0.18f, 0.16f, StructureMaterial.Timber, 11));
			d.Add(StructureShapes.Cylinder(2.3f, 0.84f, 0.94f, d.Seg(14, 8), StructureMaterial.Timber, 12));
			// The stack: bellied out a little, as a rick is built, and its cap overhanging it as eaves to shed the rain.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(2f, 0.94f), new Vector2(2.3f, 1.85f), new Vector2(2.22f, 2.9f) }, d.Seg(14, 8), StructureMaterial.Thatch, 20, true, 0f, false, true));
			var cap = new[] { new Vector2(2.2f, 2.85f), new Vector2(2.66f, 2.74f), new Vector2(2.62f, 2.96f), new Vector2(0.35f, 5f), new Vector2(0f, 5.12f) };
			d.Add(StructureShapes.Lathe(cap, d.Seg(14, 8), StructureMaterial.Thatch, 21, true, 0f, false, true, StructureRole.Roof));
			// The ladder up to the eaves.
			Ladder(d, new Vector3(3.6f, 0f, 0f), new Vector3(2.75f, 2.7f, 0f), 0.56f, 30);
			if (!d.Fine)
			{
				return;
			}
			d.Add(StructureShapes.Sphere(new Vector3(0f, 5.12f, 0f), new Vector3(0.18f, 0.22f, 0.18f), 6, 4, StructureMaterial.Thatch, 22));
			// Ropes over the cap, each weighted with a stone hung below the eaves.
			for (int i = 0; i < 4; i++)
			{
				float a = (i * 90f + 20f) * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 eave = dir * 2.7f + Vector3.up * 2.95f;
				d.Add(StructureShapes.Beam(new Vector3(0f, 5.14f, 0f) + dir * 0.1f, eave, 0.04f, 0.04f, StructureMaterial.Canvas, 50 + i * 3, StructureRole.Detail));
				d.Add(StructureShapes.Beam(eave, eave + dir * 0.06f + Vector3.down * 0.65f, 0.035f, 0.035f, StructureMaterial.Canvas, 51 + i * 3, StructureRole.Detail));
				d.Add(StructureShapes.ChamferBox(eave + dir * 0.1f + Vector3.down * 0.78f, new Vector3(0.2f, 0.22f, 0.2f), 0.03f, StructureMaterial.Fieldstone, 52 + i * 3, StructureRole.Detail));
			}
		}

		/// <summary>A long rick on a brushwood bed, built in a frame with its length along z and turned to lie along x.</summary>
		private static void LongRick(StructureDraft d)
		{
			int first = d.Solids.Count;
			const float half = 3.4f;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.02f, 0f), new Vector3(3.9f, 0.44f, 7.4f), StructureMaterial.Timber, 1)).Grain = Vector3.forward;
			var body = new[] { new Vector2(-1.6f, 0.24f), new Vector2(1.6f, 0.24f), new Vector2(1.84f, 1.4f), new Vector2(1.72f, 2.55f), new Vector2(-1.72f, 2.55f), new Vector2(-1.84f, 1.4f) };
			d.Add(StructureShapes.Prism(body, -half, half, StructureMaterial.Thatch, 2)).Grain = Vector3.up;
			// The thatch: a ridge hipped at both ends.
			StructureSolid thatch = StructureShapes.Prism(new[] { new Vector2(-2.05f, 2.45f), new Vector2(2.05f, 2.45f), new Vector2(0f, 4.4f) }, -half - 0.35f, half + 0.35f,
				StructureMaterial.Thatch, 3, StructureRole.Roof);
			for (int e = -1; e <= 1; e += 2)
			{
				Vector3 n = new Vector3(0f, 1.75f, e * 1.95f).normalized;
				thatch.Clip(n, Vector3.Dot(n, new Vector3(0f, 2.45f, e * (half + 0.35f))));
			}
			d.Add(thatch);
			Ladder(d, new Vector3(3f, 0f, -1.2f), new Vector3(2.12f, 2.4f, -1.2f), 0.56f, 30);
			if (d.Mid)
			{
				// The bearers the bed is laid on, their ends showing.
				for (int i = 0; i < 3; i++)
				{
					float x = -1.4f + i * 1.4f;
					Log(d, new Vector3(x, 0.14f, -half - 0.25f), new Vector3(x, 0.14f, half + 0.25f), 0.12f, 10 + i);
				}
			}
			if (d.Fine)
			{
				// Ropes over the ridge weighted with stones each side, and a pitchfork left leaning on the end.
				for (int i = 0; i < 3; i++)
				{
					float z = -2f + i * 2f;
					for (int s = -1; s <= 1; s += 2)
					{
						var eave = new Vector3(s * 2.1f, 2.48f, z);
						d.Add(StructureShapes.Beam(new Vector3(0f, 4.42f, z), eave, 0.04f, 0.04f, StructureMaterial.Canvas, 50 + i * 6 + (s > 0 ? 3 : 0), StructureRole.Detail));
						d.Add(StructureShapes.Beam(eave, eave + new Vector3(s * 0.06f, -0.6f, 0f), 0.035f, 0.035f, StructureMaterial.Canvas, 51 + i * 6 + (s > 0 ? 3 : 0), StructureRole.Detail));
						d.Add(StructureShapes.ChamferBox(eave + new Vector3(s * 0.08f, -0.74f, 0f), new Vector3(0.2f, 0.22f, 0.2f), 0.03f, StructureMaterial.Fieldstone, 52 + i * 6 + (s > 0 ? 3 : 0), StructureRole.Detail));
					}
				}
				var butt = new Vector3(0.5f, 0f, half + 0.75f);
				var head = new Vector3(0.3f, 2.1f, half + 0.12f);
				d.Add(StructureShapes.Beam(butt, head, 0.05f, 0.05f, StructureMaterial.Timber, 80, StructureRole.Detail));
				Vector3 up = (head - butt).normalized;
				for (int t = -1; t <= 1; t++)
				{
					Vector3 root = head + new Vector3(t * 0.08f, 0f, 0f);
					d.Add(StructureShapes.Beam(root, root + up * 0.38f + new Vector3(t * 0.04f, 0f, 0f), 0.025f, 0.025f, StructureMaterial.Iron, 81 + t + 1, StructureRole.Detail));
				}
			}
			TransformSince(d, first, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one));
		}

		/// <summary>A ladder leant from its foot to its head (both at its middle), its rails <paramref name="width"/> apart, rungs every 0.3 m from the middle level.</summary>
		private static void Ladder(StructureDraft d, Vector3 foot, Vector3 head, float width, int key)
		{
			Vector3 run = head - foot;
			Vector3 across = Vector3.Cross(Vector3.up, run).normalized * (width * 0.5f);
			if (across.sqrMagnitude < 1e-6f)
			{
				across = Vector3.right * (width * 0.5f);
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(foot + across * s, head + across * s + run.normalized * 0.25f, 0.06f, 0.05f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
			}
			if (!d.Mid)
			{
				return;
			}
			int rungs = Mathf.Max(2, Mathf.FloorToInt(run.magnitude / 0.3f));
			for (int i = 1; i < rungs; i++)
			{
				if (!d.Fine && i % 2 == 1)
				{
					continue;
				}
				Vector3 at = foot + run * (i / (float)rungs);
				d.Add(StructureShapes.Beam(at - across, at + across, 0.04f, 0.04f, StructureMaterial.Timber, key + 2 + i, StructureRole.Detail));
			}
		}

		private static void Stooks(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				for (int i = 0; i < 3; i++)
				{
					Stook(d, new Vector3(-2.2f + i * 2.2f + d.Range(10 + i, 1, -0.25f, 0.25f), 0f, d.Range(10 + i, 2, -0.3f, 0.3f)), 10 + i * 4);
				}
				return;
			}
			Stook(d, new Vector3(-1.1f, 0f, 0.1f), 10);
			Stook(d, new Vector3(1.1f, 0f, -0.15f), 14);
			// A sheaf fallen out of the row, and the sickle the reaper left by it.
			Sheaf(d, new Vector3(2.3f, 0.13f, 0.5f), Quaternion.Euler(0f, 0f, -88f) * Quaternion.Euler(0f, 30f, 0f), 30);
			if (d.Fine)
			{
				var handle = new Vector3(-2.4f, 0.03f, -0.4f);
				d.Add(StructureShapes.Beam(handle, handle + new Vector3(0.3f, 0f, 0.05f), 0.04f, 0.04f, StructureMaterial.Timber, 40, StructureRole.Detail));
				Vector3 at = handle + new Vector3(0.3f, 0f, 0.05f);
				for (int i = 0; i < 3; i++)
				{
					float a0 = (i * 50f - 10f) * Mathf.Deg2Rad, a1 = ((i + 1) * 50f - 10f) * Mathf.Deg2Rad;
					var c = at + new Vector3(0.18f, 0f, 0f);
					Vector3 p0 = c + new Vector3(-Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.18f, p1 = c + new Vector3(-Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.18f;
					d.Add(StructureShapes.Beam(p0, p1, 0.035f, 0.01f, StructureMaterial.Iron, 41 + i, StructureRole.Detail));
				}
			}
		}

		/// <summary>One stook: eight or ten sheaves stood leaning together, their butts spread on the stubble, bound at the waist, their ears gathered at the head.</summary>
		private static void Stook(StructureDraft d, Vector3 at, int key)
		{
			var profile = new[]
			{
				new Vector2(0.44f, 0f), new Vector2(0.36f, 0.4f), new Vector2(0.2f, 0.72f), new Vector2(0.25f, 0.8f), new Vector2(0.37f, 1.02f), new Vector2(0.22f, 1.22f), new Vector2(0f, 1.32f),
			};
			Matrix4x4 place = Matrix4x4.TRS(at, Quaternion.Euler(d.Range(key, 1, -5f, 5f), d.Range(key, 2, 0f, 360f), d.Range(key, 3, -5f, 5f)), Vector3.one);
			// Faceted, not smooth: the sheaves read as sheaves.
			d.Add(StructureShapes.Lathe(profile, d.Seg(9, 5), StructureMaterial.Thatch, key, false, d.Range(key, 4, 0f, 1f), false, true).Transform(place));
			if (d.Mid)
			{
				d.Add(StructureShapes.Cylinder(0.24f, 0.72f, 0.8f, d.Seg(9, 5), StructureMaterial.Thatch, key + 1, true, StructureRole.Detail).Transform(place));
			}
		}

		/// <summary>One sheaf, upright in its own frame (butt at the origin), bound at the waist, turned and moved by <paramref name="turn"/>.</summary>
		private static void Sheaf(StructureDraft d, Vector3 at, Quaternion turn, int key)
		{
			var profile = new[] { new Vector2(0.12f, 0f), new Vector2(0.1f, 0.5f), new Vector2(0.08f, 0.58f), new Vector2(0.17f, 0.9f), new Vector2(0f, 1.1f) };
			d.Add(StructureShapes.Lathe(profile, d.Seg(6, 4), StructureMaterial.Thatch, key, false, 0f, false, true).Transform(Matrix4x4.TRS(at, turn, Vector3.one)));
		}

		// ── Scarecrow ─────────────────────────────────────────────────

		private static void Scarecrow(StructureDraft d)
		{
			bool sack = d.Variant == 1;
			const float arms = 1.62f;
			// The cross: a post driven into the ground, a bar across it for the arms.
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.4f, 0f), new Vector3(0f, 2.05f, 0f), 0.09f, 0.09f, StructureMaterial.Timber, 1));
			d.Add(StructureShapes.Beam(new Vector3(-0.9f, arms, 0f), new Vector3(0.9f, arms, 0f), 0.07f, 0.07f, StructureMaterial.Timber, 2));
			// The coat, stuffed: a body, sleeves along the bar drooping a little, and the trousers dangling under it.
			StructureMaterial coat = sack ? StructureMaterial.Canvas : StructureMaterial.Dyed;
			d.Add(StructureShapes.Block(new Vector3(0f, 1.3f, 0f), new Vector3(0.52f, 0.72f, 0.26f), Quaternion.Euler(0f, 0f, d.Range(3, 1, -4f, 4f)), coat, 3));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.2f, arms, 0f), new Vector3(s * 0.78f, arms - 0.07f, 0f), 0.17f, 0.17f, coat, 4 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * 0.13f, 0.98f, 0f), new Vector3(s * 0.16f + d.Range(6, s + 1, -0.04f, 0.04f), 0.45f, 0.02f), 0.13f, 0.13f, StructureMaterial.Canvas, 6 + (s > 0 ? 1 : 0)));
			}
			// The head.
			var neck = new Vector3(0f, 1.82f, 0f);
			if (sack)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.06f, -0.08f), new Vector2(0.16f, 0.04f), new Vector2(0.18f, 0.18f), new Vector2(0.12f, 0.32f), new Vector2(0f, 0.36f) },
					d.Seg(8, 5), StructureMaterial.Canvas, 10).Transform(Matrix4x4.Translate(neck)));
				// A shapeless felt hat.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.27f, 0.3f), new Vector2(0.28f, 0.33f), new Vector2(0.14f, 0.36f), new Vector2(0.12f, 0.52f), new Vector2(0f, 0.55f) },
					d.Seg(8, 5), StructureMaterial.Ash, 11, false).Transform(Matrix4x4.TRS(neck, Quaternion.Euler(8f, 0f, -6f), Vector3.one)));
			}
			else
			{
				// A turnip, ribbed, its face cut into it, under a straw hat.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.04f, -0.05f), new Vector2(0.15f, 0.04f), new Vector2(0.2f, 0.17f), new Vector2(0.15f, 0.31f), new Vector2(0.05f, 0.36f), new Vector2(0f, 0.37f) },
					d.Seg(9, 5), StructureMaterial.Plaster, 10, false).Transform(Matrix4x4.Translate(neck)));
				d.Add(StructureShapes.Cylinder(0.34f, 0.32f, 0.35f, d.Seg(10, 6), StructureMaterial.Thatch, 11).Transform(Matrix4x4.TRS(neck, Quaternion.Euler(-6f, 0f, 5f), Vector3.one)));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, 0.34f), new Vector2(0.14f, 0.5f), new Vector2(0f, 0.53f) }, d.Seg(8, 5), StructureMaterial.Thatch, 12)
					.Transform(Matrix4x4.TRS(neck, Quaternion.Euler(-6f, 0f, 5f), Vector3.one)));
			}
			if (!d.Mid)
			{
				return;
			}
			// The face: two eyes and a mouth (cut into the turnip, stitched on the sack).
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(neck + new Vector3(s * 0.07f, 0.2f, -0.17f), new Vector3(0.06f, 0.05f, 0.03f), StructureMaterial.Ash, 13 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Box(neck + new Vector3(0f, 0.09f, -0.165f), new Vector3(0.14f, 0.03f, 0.03f), StructureMaterial.Ash, 15, StructureRole.Detail));
			if (!d.Fine)
			{
				return;
			}
			// Straw out of the cuffs and the trouser legs, and the coat's ragged tails.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Block(new Vector3(s * 0.88f, arms - 0.09f, 0f), new Vector3(0.14f, 0.14f, 0.12f), Quaternion.Euler(0f, 0f, s * 20f), StructureMaterial.Thatch, 20 + (s > 0 ? 1 : 0), StructureRole.Detail));
				d.Add(StructureShapes.Block(new Vector3(s * 0.16f, 0.42f, 0.02f), new Vector3(0.12f, 0.12f, 0.12f), Quaternion.Euler(0f, 30f, 0f), StructureMaterial.Thatch, 22 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			for (int i = 0; i < 3; i++)
			{
				float x = -0.18f + i * 0.18f;
				d.Add(StructureShapes.Block(new Vector3(x, 0.84f - d.Range(30 + i, 1, 0f, 0.1f), 0.06f), new Vector3(0.15f, 0.22f, 0.02f), Quaternion.Euler(d.Range(30 + i, 2, -8f, 8f), 0f, d.Range(30 + i, 3, -10f, 10f)),
					coat, 30 + i, StructureRole.Detail));
			}
			if (sack)
			{
				// A rope round the sack's neck, and a crow perched on the end of the arm, unafraid.
				d.Add(StructureShapes.Cylinder(0.14f, 1.79f, 1.83f, 6, StructureMaterial.Canvas, 40, false, StructureRole.Detail));
				var perch = new Vector3(0.62f, arms + 0.15f, 0f);
				d.Add(StructureShapes.Sphere(perch, new Vector3(0.07f, 0.075f, 0.13f), 6, 4, StructureMaterial.Ash, 41, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(perch + new Vector3(0f, 0.07f, -0.11f), new Vector3(0.045f, 0.045f, 0.05f), 6, 4, StructureMaterial.Ash, 42, StructureRole.Detail));
				d.Add(StructureShapes.Beam(perch + new Vector3(0f, 0.06f, -0.15f), perch + new Vector3(0f, 0.05f, -0.23f), 0.02f, 0.02f, StructureMaterial.Iron, 43, StructureRole.Detail));
				d.Add(StructureShapes.Block(perch + new Vector3(0f, 0.01f, 0.16f), new Vector3(0.08f, 0.015f, 0.14f), Quaternion.Euler(-20f, 0f, 0f), StructureMaterial.Ash, 44, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(perch + new Vector3(s * 0.03f, -0.05f, 0f), perch + new Vector3(s * 0.03f, -0.13f, 0.01f), 0.015f, 0.015f, StructureMaterial.Ash, 45 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
		}

		// ── Sty, hen house ────────────────────────────────────────────

		private static void Pigsty(StructureDraft d)
		{
			// The sty at the back (+z), its pig door to the run in front.
			const float styWidth = 2.8f, styDepth = 2.2f, styZ = 1.2f, runFront = -2.6f;
			bool stone = d.Variant == 0;
			float hw = styWidth * 0.5f, back = styZ + styDepth * 0.5f, front = styZ - styDepth * 0.5f;
			float wall = stone ? 0.4f : 0.08f, eave = stone ? 1.35f : 1.3f;
			StructureMaterial walls = stone ? StructureMaterial.Fieldstone : StructureMaterial.Timber;
			var door = new List<WallOpening> { new WallOpening { Along = hw - 0.6f, Width = 0.7f, Bottom = -FoundationDepth - 0.01f, Top = 0.85f } };
			using (d.Builds(WallRoles))
			{
				float y0 = stone ? -FoundationDepth : -0.2f;
				WallRun(d, new Vector3(-hw, 0f, front + wall * 0.5f), new Vector3(hw, 0f, front + wall * 0.5f), wall, y0, eave, door, walls, 1);
				WallRun(d, new Vector3(-hw, 0f, back - wall * 0.5f), new Vector3(hw, 0f, back - wall * 0.5f), wall, y0, eave, null, walls, 2);
				WallRun(d, new Vector3(-hw + wall * 0.5f, 0f, front + wall), new Vector3(-hw + wall * 0.5f, 0f, back - wall), wall, y0, eave, null, walls, 3);
				WallRun(d, new Vector3(hw - wall * 0.5f, 0f, front + wall), new Vector3(hw - wall * 0.5f, 0f, back - wall), wall, y0, eave, null, walls, 4);
			}
			// The sty's trodden floor.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.03f, styZ), new Vector3(styWidth - 2f * wall, 0.06f, styDepth - 2f * wall), StructureMaterial.Earth, 5, StructureRole.Detail));
			if (stone)
			{
				// Heavy stone slabs on a low gable over the sty.
				Matrix4x4 roof = Matrix4x4.TRS(new Vector3(0f, 0f, styZ), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
				GableRoof(d, styDepth, styWidth, eave, 0.8f, 0.18f, 0.14f, StructureMaterial.Stone, 10, roof);
				using (d.Builds(WallRoles))
				{
					Gable(d, styDepth - 0.02f, eave, 0.78f, -hw + wall * 0.5f, wall, walls, 12, roof);
					Gable(d, styDepth - 0.02f, eave, 0.78f, hw - wall * 0.5f, wall, walls, 13, roof);
				}
			}
			else
			{
				// Corner posts, and a lean-to of boards falling to the back.
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						float z = sz < 0 ? front : back;
						d.Add(StructureShapes.Beam(new Vector3(sx * hw, -0.3f, z), new Vector3(sx * hw, sz < 0 ? 1.62f : 1.3f, z), 0.12f, 0.12f, StructureMaterial.Timber, 10 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
					}
				}
				d.Add(StructureShapes.Block(new Vector3(0f, 1.52f, styZ), new Vector3(styWidth + 0.5f, 0.06f, styDepth + 0.6f), Quaternion.Euler(8f, 0f, 0f), StructureMaterial.Timber, 14, StructureRole.Roof)).Grain = Vector3.forward;
				using (d.Builds(WallRoles))
				{
					// The triangle of boards between the front wall's head and the lean-to.
					d.Add(StructureShapes.Box(new Vector3(0f, eave + 0.15f, front + wall * 0.5f), new Vector3(styWidth, 0.3f, wall), walls, 15));
				}
			}

			// The run: low walls (or hurdles) on three sides, the gate in the front, a trough for the swill and the mud trodden bare.
			float runDepth = front - runFront, runMiddle = (front + runFront) * 0.5f;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.02f, runMiddle), new Vector3(styWidth - 0.4f, 0.04f, runDepth - 0.3f), StructureMaterial.Earth, 20, StructureRole.Detail));
			var gate = new List<WallOpening> { new WallOpening { Along = hw + 0.8f, Width = 0.85f, Bottom = -FoundationDepth - 0.01f, Top = 1.2f } };
			if (stone)
			{
				const float t = 0.35f, h = 1f;
				using (d.Builds(WallRoles))
				{
					WallRun(d, new Vector3(-hw, 0f, runFront + t * 0.5f), new Vector3(hw, 0f, runFront + t * 0.5f), t, -FoundationDepth, h, gate, walls, 21);
					WallRun(d, new Vector3(-hw + t * 0.5f, 0f, runFront + t), new Vector3(-hw + t * 0.5f, 0f, front), t, -FoundationDepth, h, null, walls, 22);
					WallRun(d, new Vector3(hw - t * 0.5f, 0f, runFront + t), new Vector3(hw - t * 0.5f, 0f, front), t, -FoundationDepth, h, null, walls, 23);
				}
				// The stone trough against the front wall, its swill dark in it.
				d.Add(StructureShapes.ChamferBox(new Vector3(-0.55f, 0.2f, runFront + t + 0.24f), new Vector3(1f, 0.4f, 0.46f), d.Bevel(0.03f), StructureMaterial.Stone, 24));
				d.Add(StructureShapes.Box(new Vector3(-0.55f, 0.401f, runFront + t + 0.24f), new Vector3(0.84f, 0.01f, 0.32f), StructureMaterial.Ash, 25, StructureRole.Detail));
				if (d.Mid)
				{
					// Coping slabs along the run walls.
					d.Add(StructureShapes.Box(new Vector3(-hw + t * 0.5f, h + 0.05f, (runFront + front) * 0.5f), new Vector3(t + 0.1f, 0.1f, runDepth), StructureMaterial.Stone, 26, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(hw - t * 0.5f, h + 0.05f, (runFront + front) * 0.5f), new Vector3(t + 0.1f, 0.1f, runDepth), StructureMaterial.Stone, 27, StructureRole.Detail));
				}
			}
			else
			{
				// Wattle hurdles on stakes.
				void Hurdle(Vector3 a, Vector3 b, int key)
				{
					Vector3 mid = (a + b) * 0.5f;
					float length = (b - a).magnitude;
					Quaternion turn = Quaternion.LookRotation(Vector3.Cross(Vector3.up, (b - a).normalized), Vector3.up);
					using (d.Builds(FenceRoles))
					{
						d.Add(StructureShapes.Block(mid + Vector3.up * 0.5f, new Vector3(length, 0.9f, 0.06f), turn, StructureMaterial.Timber, key)).Grain = (b - a).normalized;
						if (d.Mid)
						{
							int stakes = Mathf.Max(2, Mathf.RoundToInt(length / 0.9f) + 1);
							for (int i = 0; i < stakes; i++)
							{
								Vector3 at = Vector3.Lerp(a, b, i / (stakes - 1f));
								d.Add(StructureShapes.Beam(at + Vector3.down * 0.3f, at + Vector3.up * 1.05f, 0.06f, 0.06f, StructureMaterial.Timber, key + 1 + i, StructureRole.Detail));
							}
						}
					}
				}
				Hurdle(new Vector3(-hw, 0f, runFront), new Vector3(0.35f, 0f, runFront), 30);
				Hurdle(new Vector3(-hw, 0f, runFront), new Vector3(-hw, 0f, front), 40);
				Hurdle(new Vector3(hw, 0f, runFront), new Vector3(hw, 0f, front), 50);
				// The gate hurdle, swung a little open.
				d.Add(StructureShapes.Block(new Vector3(0.82f, 0.5f, runFront - 0.2f), new Vector3(0.9f, 0.9f, 0.05f), Quaternion.Euler(0f, -25f, 0f), StructureMaterial.Timber, 60, StructureRole.Detail));
				// A trough hewn from a log.
				d.Add(StructureShapes.Box(new Vector3(-0.5f, 0.17f, runFront + 0.4f), new Vector3(1.1f, 0.34f, 0.4f), StructureMaterial.Timber, 61)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(-0.5f, 0.341f, runFront + 0.4f), new Vector3(0.95f, 0.01f, 0.28f), StructureMaterial.Ash, 62, StructureRole.Detail));
			}
			if (d.Mid)
			{
				// The gate in the walled run: a plank gate shut across its gap.
				if (stone)
				{
					d.Add(StructureShapes.Box(new Vector3(0.8f, 0.48f, runFront + 0.17f), new Vector3(0.82f, 0.86f, 0.05f), StructureMaterial.Timber, 70, StructureRole.Detail)).Grain = Vector3.up;
				}
				// The dark of the pig door.
				d.Add(StructureShapes.Box(new Vector3(hw - 0.6f - hw, 0.42f, front + wall * 0.5f), new Vector3(0.68f, 0.84f, 0.02f), StructureMaterial.Ash, 71, StructureRole.Detail));
			}
		}

		private static void HenHouse(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// A plank coop up on legs against foxes and the wet, 1.6×1.2 m.
				const float width = 1.6f, depth = 1.2f, floor = 0.75f, eave = 1.75f;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(sx * (width * 0.5f - 0.08f), -0.3f, sz * (depth * 0.5f - 0.08f)), new Vector3(sx * (width * 0.5f - 0.08f), floor, sz * (depth * 0.5f - 0.08f)),
							0.1f, 0.1f, StructureMaterial.Timber, 1 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, floor + 0.04f, 0f), new Vector3(width + 0.06f, 0.08f, depth + 0.06f), StructureMaterial.Timber, 5)).Grain = Vector3.right;
				using (d.Builds(WallRoles))
				{
					Walls(d, Vector3.zero, width, depth, 0.05f, floor + 0.08f, eave, StructureMaterial.Timber, 6);
				}
				Matrix4x4 ridgeAlongX = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
				OwnRoofPlan roof = OwnGable(d, depth, width, eave, 0.55f, 0.15f, 0.06f, StructureMaterial.Timber, 12, ridgeAlongX);
				if (!roof.Flat)
				{
					using (d.Builds(WallRoles))
					{
						Gable(d, depth - 0.02f, eave, roof.Rise - 0.02f, -width * 0.5f + 0.025f, 0.05f, StructureMaterial.Timber, 14, ridgeAlongX);
						Gable(d, depth - 0.02f, eave, roof.Rise - 0.02f, width * 0.5f - 0.025f, 0.05f, StructureMaterial.Timber, 15, ridgeAlongX);
					}
				}
				// The pop hole, the ramp up to it with its cleats, and the nest boxes along the side under a hinged lid.
				const float hole = 0.42f;
				d.Add(StructureShapes.Box(new Vector3(hole, floor + 0.25f, -depth * 0.5f - 0.01f), new Vector3(0.24f, 0.3f, 0.03f), StructureMaterial.Ash, 20, StructureRole.Detail));
				var foot = new Vector3(hole, 0.02f, -depth * 0.5f - 1.15f);
				var sill = new Vector3(hole, floor + 0.1f, -depth * 0.5f - 0.03f);
				d.Add(StructureShapes.Beam(foot, sill, 0.3f, 0.03f, StructureMaterial.Timber, 21));
				d.Add(StructureShapes.Box(new Vector3(width * 0.5f + 0.2f, floor + 0.38f, 0f), new Vector3(0.4f, 0.45f, 0.95f), StructureMaterial.Timber, 22));
				if (d.Mid)
				{
					d.Add(StructureShapes.Block(new Vector3(width * 0.5f + 0.2f, floor + 0.66f, 0f), new Vector3(0.5f, 0.04f, 1.02f), Quaternion.Euler(0f, 0f, -18f), StructureMaterial.Timber, 23, StructureRole.Roof));
					// The keeper's hatch in the far gable.
					d.Add(StructureShapes.Box(new Vector3(-width * 0.5f - 0.02f, floor + 0.5f, 0f), new Vector3(0.03f, 0.6f, 0.55f), StructureMaterial.Timber, 24, StructureRole.Detail)).Grain = Vector3.up;
				}
				if (d.Fine)
				{
					for (int i = 1; i < 5; i++)
					{
						Vector3 at = Vector3.Lerp(foot, sill, i / 5f) + Vector3.up * 0.025f;
						d.Add(StructureShapes.Beam(at + Vector3.left * 0.13f, at + Vector3.right * 0.13f, 0.025f, 0.025f, StructureMaterial.Timber, 25 + i, StructureRole.Detail));
					}
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(-width * 0.5f - 0.04f, floor + 0.5f + s * 0.2f, 0.1f), new Vector3(0.02f, 0.04f, 0.32f), StructureMaterial.Iron, 31 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
				return;
			}
			// A wattle hen house on the ground under its thatch, 1.8×1.4 m: the rods woven between stakes, a low door for the keeper.
			const float w = 1.8f, dp = 1.4f, top = 0.95f;
			using (d.Builds(WallRoles))
			{
				Walls(d, Vector3.zero, w, dp, 0.08f, -0.2f, top, StructureMaterial.Timber, 1);
			}
			Matrix4x4 alongX = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			OwnRoofPlan thatch = OwnGable(d, dp, w, top, 1.05f, 0.3f, 0.22f, StructureMaterial.Thatch, 10, alongX);
			if (!thatch.Flat)
			{
				using (d.Builds(WallRoles))
				{
					Gable(d, dp - 0.02f, top, thatch.Rise - 0.02f, -w * 0.5f + 0.04f, 0.08f, StructureMaterial.Timber, 12, alongX);
					Gable(d, dp - 0.02f, top, thatch.Rise - 0.02f, w * 0.5f - 0.04f, 0.08f, StructureMaterial.Timber, 13, alongX);
				}
			}
			d.Add(StructureShapes.Box(new Vector3(-0.45f, 0.42f, -dp * 0.5f - 0.01f), new Vector3(0.55f, 0.75f, 0.03f), StructureMaterial.Timber, 20, StructureRole.Detail)).Grain = Vector3.up;
			d.Add(StructureShapes.Box(new Vector3(0.45f, 0.18f, -dp * 0.5f - 0.01f), new Vector3(0.24f, 0.28f, 0.03f), StructureMaterial.Ash, 21, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(0.45f, 0.01f, -dp * 0.5f - 0.5f), new Vector3(0.45f, 0.07f, -dp * 0.5f - 0.03f), 0.26f, 0.03f, StructureMaterial.Timber, 22));
			if (!d.Mid)
			{
				return;
			}
			// The stakes standing up at the corners and along the sides, and (finest) the rods' courses.
			int k = 0;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * (w * 0.5f + 0.01f), -0.2f, sz * (dp * 0.5f + 0.01f)), new Vector3(sx * (w * 0.5f + 0.01f), top + 0.08f, sz * (dp * 0.5f + 0.01f)),
						0.07f, 0.07f, StructureMaterial.Timber, 30 + k++, StructureRole.Detail));
				}
			}
			if (!d.Fine)
			{
				return;
			}
			for (int r = 0; r < 4; r++)
			{
				float y = 0.05f + r * 0.22f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(-w * 0.5f, y, s * (dp * 0.5f + 0.02f)), new Vector3(w * 0.5f, y + 0.02f, s * (dp * 0.5f + 0.02f)), 0.03f, 0.05f, StructureMaterial.Timber, 40 + r * 4 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(s * (w * 0.5f + 0.02f), y + 0.08f, -dp * 0.5f), new Vector3(s * (w * 0.5f + 0.02f), y + 0.1f, dp * 0.5f), 0.03f, 0.05f, StructureMaterial.Timber, 42 + r * 4 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
		}

		// ── Sacks, trough ─────────────────────────────────────────────

		private static void Sacks(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// Two lying side by side, one across them, one standing at the end: tied at their necks.
				LyingSack(d, new Vector3(-0.2f, 0.15f, -0.13f), 4f, 1);
				LyingSack(d, new Vector3(-0.17f, 0.15f, 0.22f), -3f, 3);
				LyingSack(d, new Vector3(-0.2f, 0.43f, 0.05f), 12f, 5);
				StandingSack(d, new Vector3(0.45f, 0f, 0.08f), 0.72f, 20f, false, 7);
				return;
			}
			// A pallet of boards on three bearers, three sacks on it, one open with a scoop in its grain.
			for (int i = 0; i < 3; i++)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, -0.4f + i * 0.4f), new Vector3(1.15f, 0.08f, 0.08f), StructureMaterial.Timber, 1 + i)).Grain = Vector3.right;
			}
			for (int i = 0; i < 5; i++)
			{
				d.Add(StructureShapes.Box(new Vector3(-0.46f + i * 0.23f, 0.1f, 0f), new Vector3(0.19f, 0.04f, 1f), StructureMaterial.Timber, 4 + i)).Grain = Vector3.forward;
			}
			StandingSack(d, new Vector3(-0.29f, 0.12f, 0.18f), 0.74f, 10f, false, 10);
			StandingSack(d, new Vector3(0.28f, 0.12f, 0.18f), 0.7f, -25f, false, 12);
			StandingSack(d, new Vector3(0f, 0.12f, -0.27f), 0.6f, 0f, true, 14);
			if (d.Fine)
			{
				// The scoop: a little tray and its handle, resting in the open sack's grain.
				d.Add(StructureShapes.Block(new Vector3(0.02f, 0.7f, -0.29f), new Vector3(0.16f, 0.05f, 0.2f), Quaternion.Euler(-25f, 15f, 0f), StructureMaterial.Timber, 20, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(0.05f, 0.72f, -0.2f), new Vector3(0.11f, 0.86f, -0.05f), 0.03f, 0.03f, StructureMaterial.Timber, 21, StructureRole.Detail));
			}
		}

		/// <summary>A full sack lying on its side, long along x turned by <paramref name="yaw"/>, its tied neck at one end.</summary>
		private static void LyingSack(StructureDraft d, Vector3 at, float yaw, int key)
		{
			Matrix4x4 place = Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
			d.Add(StructureShapes.ChamferBox(Vector3.zero, new Vector3(0.74f, 0.3f, 0.42f), d.Bevel(0.09f), StructureMaterial.Canvas, key).Transform(place));
			if (d.Mid)
			{
				d.Add(StructureShapes.Rod(new Vector3(0.36f, 0f, 0f), new Vector3(0.48f, 0.01f, 0f), 0.07f, d.Seg(6, 4), StructureMaterial.Canvas, key + 1, StructureRole.Detail).Transform(place));
			}
		}

		/// <summary>A sack standing on its end, bellied, gathered and tied at the neck; open (its grain showing) when <paramref name="open"/>.</summary>
		private static void StandingSack(StructureDraft d, Vector3 at, float height, float yaw, bool open, int key)
		{
			float h = height;
			var profile = open
				? new[] { new Vector2(0.2f, 0f), new Vector2(0.26f, 0.06f), new Vector2(0.27f, h * 0.5f), new Vector2(0.25f, h * 0.9f), new Vector2(0.23f, h) }
				: new[] { new Vector2(0.2f, 0f), new Vector2(0.26f, 0.06f), new Vector2(0.27f, h * 0.45f), new Vector2(0.23f, h * 0.8f), new Vector2(0.09f, h * 0.93f), new Vector2(0.06f, h * 0.96f), new Vector2(0.11f, h * 1.03f), new Vector2(0f, h * 1.06f) };
			Matrix4x4 place = Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
			d.Add(StructureShapes.Lathe(profile, d.Seg(8, 5), StructureMaterial.Canvas, key).Transform(place));
			if (open && d.Mid)
			{
				// The grain, a little under the sack's rolled-down mouth.
				d.Add(StructureShapes.Cylinder(0.215f, h - 0.06f, h - 0.03f, d.Seg(8, 5), StructureMaterial.Thatch, key + 1, false, StructureRole.Detail).Transform(place));
			}
		}

		private static void Trough(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				// A plank box on two trestles, the water dark in it.
				const float length = 2f, width = 0.5f, y0 = 0.32f, y1 = 0.75f, plank = 0.05f;
				d.Add(StructureShapes.Box(new Vector3(0f, y0 + plank * 0.5f, 0f), new Vector3(length, plank, width), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, (y0 + y1) * 0.5f, s * (width * 0.5f - plank * 0.5f)), new Vector3(length, y1 - y0, plank), StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
					d.Add(StructureShapes.Box(new Vector3(s * (length * 0.5f - plank * 0.5f), (y0 + y1) * 0.5f, 0f), new Vector3(plank, y1 - y0, width - 2f * plank), StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0)));
					// The trestle: two splayed legs and a cross-bar.
					float x = s * (length * 0.5f - 0.25f);
					for (int t = -1; t <= 1; t += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(x, -0.25f, t * 0.36f), new Vector3(x, y0, t * 0.16f), 0.07f, 0.07f, StructureMaterial.Timber, 6 + (s > 0 ? 2 : 0) + (t > 0 ? 1 : 0)));
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, y1 - 0.08f, 0f), new Vector3(length - 2f * plank, 0.02f, width - 2f * plank), StructureMaterial.Ash, 10, StructureRole.Detail));
				if (d.Fine)
				{
					// The bucket left by it.
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.13f, 0f), new Vector2(0.16f, 0.3f) }, 8, StructureMaterial.Timber, 11, true, 0f, false, true, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(0.6f, 0f, -0.6f))));
				}
				return;
			}
			// A stone trough hollowed from one block, on two field stones.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.48f, 0f), new Vector3(1.8f, 0.5f, 0.6f), d.Bevel(0.04f), StructureMaterial.Stone, 1));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.7f, 0f), new Vector3(1.55f, 0.07f, 0.4f), StructureMaterial.Ash, 2, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(RoughStone(d, new Vector3(s * 0.6f, -0.2f, 0f), new Vector3(0.45f, 0.45f, 0.55f), d.Range(3, s + 1, 0f, 40f), 3 + (s > 0 ? 1 : 0), d.Fine ? 3 : 1));
			}
		}
	}
}
#endif
