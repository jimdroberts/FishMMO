#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A capital's palace (2026-10-19: "massive palace style castles with interiors (kings chair?) for capitals"): a compound of
	/// kit pieces the capital's inner ward lays together — the throne hall at the back of a court, a residential wing down each
	/// side of it, the court's formal garden between them, and the gatehouse and towers of the walls (StructurePieces.Towers.cs)
	/// round it — and the throne itself, the one piece of furniture the throne room is built round.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The throne room's contract.</b> A room of kind <see cref="StructureRoomKind.ThroneRoom"/> is built with a dais across
	/// its whole back (+z): <see cref="ThroneDaisDepth"/> deep and <see cref="ThroneDaisRise"/> over the room's floor, climbed by a
	/// flight across its front <see cref="ThroneDaisSteps"/> deep. The interior planner (StructureInteriors) knows the dais by these
	/// alone, and lays the throne, its braziers and its guard ON it (an item's height is its own, so the throne stands at the dais'
	/// top): the room's one floor height cannot say a part of it is raised, and a throne built into the hall's mesh could be neither
	/// varied by the site nor replaced by a hand-made one. The builder keeps the dais and its flight clear of other furniture
	/// (StructureDraft.RoomClear), and the runner from the door to the flight.
	/// </para>
	/// <para>
	/// <b>Walking it.</b> Every floor a person reaches is guarded where it drops more than a metre: the hall stands 0.9 m up on its
	/// plinth (its broad front steps no ledge), the dais 0.75 m over the hall, the minstrels' gallery of v1 behind a solid front and
	/// its stair between the wall and a side wall; the wing's stair is the keep's (a straight flight against a wall, a wall along its
	/// open side, a parapet round the well over it). The last level is the collider, so each of these stands at every level.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>A throne room's dais: its height over the room's floor and its depth from the back wall (+z), metres (see the remarks).</summary>
		public const float ThroneDaisRise = 0.75f, ThroneDaisDepth = 5f;
		/// <summary>
		/// The flight up the front of a throne room's dais (Steps: one riser fewer blocks than risers, each a tread deep), metres:
		/// (⌈0.75 / StepRise⌉ − 1) treads of StepTread.
		/// </summary>
		public const float ThroneDaisSteps = 0.9f;

		/// <summary>A palace's turf: the palette's Turf where it has one (StructureDraft.Builds), else the kit's packed earth.</summary>
		private static readonly BiomeStructureRole[] PalaceTurfRoles = { BiomeStructureRole.Turf };

		/// <summary>Maps (u, v, w) to (w, v, u): a prism drawn in the (z, y) plane and extruded along x (a reflection; Transform turns its faces back).</summary>
		private static readonly Matrix4x4 PalaceSwapXZ = new Matrix4x4(new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 1f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 0f, 0f, 1f));

		private static void AddPalace(List<StructurePiece> list)
		{
			Piece(list, "ThroneHall", StructureStyle.Stone, StructureSize.Stronghold,
				"A capital's throne hall, 18×34 m on a plinth 0.9 m up, its grand double door at the front (−z) up broad steps: inside, the throne room (StructureRoomKind.ThroneRoom), two rows of piers down it, banners between them, a runner to a dais across the far end (0.75 m up) under a cloth of estate and its canopy, iron chandeliers; v0 a Gothic basilica, a tall clerestoried nave over lean-to aisles, buttresses and a square turret under a spirelet at each corner; v1 a hall of round columns under one great roof crowned by a lead-domed cupola, a round tower under a cone at each corner, a round-arched portal and a minstrels' gallery over the door up a stair.",
				ThroneHall, "palace");
			Piece(list, "PalaceWing", StructureStyle.Stone, StructureSize.Stronghold,
				"A palace's residential range, 28×12 m, two storeys walked by a stone stair, its door in the middle of its long front (−z) up three steps: a council chamber, a porter's hall and a great chamber below; a bedchamber, a long gallery and a solar above, each with its own hearth at the ends; v0 battlemented, a low hipped roof behind the parapet and a round bartizan at each corner; v1 under a steep roof between stone gables, an oriel over the door.",
				PalaceWing, "palace");
			Piece(list, "Throne", StructureStyle.Timber, StructureSize.Small,
				"A king's chair, its back to the wall (+z), facing −z: v0 a high-backed carved oak throne with armrests, a gabled crest and metal-capped finials, a dyed cushion and back panel; v1 a stone throne on a step, its back slab between pillars with pyramid finials, glowing runes carved down it.",
				Throne, "furniture", "throne");
			Piece(list, "PalaceGarden", StructureStyle.Stone, StructureSize.Building,
				"A palace court's formal garden, 20×20 m: flagged walks in a cross meeting at a square plaza, four parterre beds edged in clipped box, benches round the middle; v0 a tiered fountain in an octagonal basin, knots of diagonal hedges with cone topiary, a low hedge round it and urns at its corners; v1 a statue in a round basin, diamond knots with clipped balls, a stone balustrade round it with gate piers and timber arbours over the walks.",
				PalaceGarden, "palace", "garden");
		}

		// ── Shared parts ──────────────────────────────────────────────

		/// <summary>
		/// A window's glazing in a hole a wall run has cut (<see cref="WallRun"/>), the wall's outer face at <paramref name="at"/>
		/// facing <paramref name="outward"/>: the glass halfway through the wall at every level (a far window shows no hole), and
		/// from the middle level a sill and a hood mould to throw the rain off; from the first, a mullion between its lights.
		/// </summary>
		private static void PalaceLight(StructureDraft d, Vector3 at, Vector3 outward, float wall, float width, float height, bool trim, int key)
		{
			Quaternion turn = Quaternion.LookRotation(-outward, Vector3.up);
			d.Add(StructureShapes.Block(at - outward * (wall * 0.5f), new Vector3(width, height, 0.05f), turn, StructureMaterial.Glass, key, StructureRole.Detail));
			if (!trim || !d.Mid)
			{
				return;
			}
			d.Add(StructureShapes.Block(at + outward * 0.07f + Vector3.down * (height * 0.5f + 0.06f), new Vector3(width + 0.2f, 0.12f, 0.16f), turn, StructureMaterial.Stone, key + 1, StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.06f + Vector3.up * (height * 0.5f + 0.08f), new Vector3(width + 0.34f, 0.16f, 0.14f), turn, StructureMaterial.Stone, key + 2, StructureRole.Detail));
			if (d.Fine && width > 0.9f)
			{
				d.Add(StructureShapes.Block(at - outward * (wall * 0.5f - 0.05f), new Vector3(0.1f, height, 0.12f), turn, StructureMaterial.Stone, key + 3, StructureRole.Detail));
			}
		}

		// ── Throne hall ───────────────────────────────────────────────

		/// <summary>The hall's outer half width and depth, its walls' thickness and the room inside them (half), metres.</summary>
		private const float HallHalfX = 9f, HallHalfZ = 17f, HallWall = 1f, HallInX = HallHalfX - HallWall, HallInZ = HallHalfZ - HallWall;
		/// <summary>The hall's floor: its plinth's top, five risers up its front steps.</summary>
		private const float HallFloor = 0.9f;
		/// <summary>The rows of piers (v0) or columns (v1): their centre lines' x either side, and a pier's breadth, metres.</summary>
		private const float HallPierX = 4.6f, HallPier = 1f;
		/// <summary>Where the piers stand down each row (z), the last at the dais' front.</summary>
		private static readonly float[] HallPiers = { -11f, -6.6f, -2.2f, 2.2f, 6.6f, 11f };
		/// <summary>v0's nave roof: its rise over half the clerestory's span (5.1 m), as the kit builds it (StructureRoofs.Followers).</summary>
		private const float GothicHallRise = 5f;
		/// <summary>v1's great roof: its rise over half the hall's span (9 m), as the kit builds it (StructureRoofs.Followers).</summary>
		private const float CupolaHallRise = 6.5f;
		/// <summary>v1's minstrels' gallery: the line of its front (z) and its floor's height over the hall's floor, metres.</summary>
		private const float GalleryFront = -12.8f, GalleryRise = 3.6f;

		/// <summary>The bays down a row: the open spans between the piers and the end walls (z from, z to).</summary>
		private static List<Vector2> HallBays()
		{
			var bays = new List<Vector2>();
			float at = -HallInZ;
			foreach (float p in HallPiers)
			{
				bays.Add(new Vector2(at, p - HallPier * 0.5f));
				at = p + HallPier * 0.5f;
			}
			bays.Add(new Vector2(at, HallInZ));
			return bays;
		}

		/// <summary>
		/// A capital's throne hall (2026-10-19): the palace's great hall, its front (−z) to the court up broad steps, its throne at
		/// the far end on a dais across the hall. One room, the throne room, down its whole length: the court stands in the nave
		/// between two rows of piers (the aisles behind them, with benches along the walls), a runner leads from the door to the
		/// dais, banners hang in the bays and iron chandeliers over the nave, and the cloth of estate hangs on the back wall over the
		/// throne under its canopy. v0 is a Gothic basilica (<see cref="GothicHall"/>), v1 a hall of columns under one roof crowned
		/// by a cupola, with a minstrels' gallery over its door (<see cref="CupolaHall"/>).
		/// </summary>
		private static void ThroneHall(StructureDraft d)
		{
			bool gothic = d.Variant == 0;
			const float F = HallFloor;
			float daisTop = F + ThroneDaisRise, daisFront = HallInZ - ThroneDaisDepth;

			// The plinth: the hall's floor, a little proud of its walls all round.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (F - FoundationDepth) * 0.5f, 0f), new Vector3(2f * HallHalfX + 0.6f, F + FoundationDepth, 2f * HallHalfZ + 0.6f),
					d.Bevel(0.06f), StructureMaterial.Stone, 1));
			}
			// The broad steps up to the door, from the plinth's edge (five risers: no fall from them is a metre).
			Steps(d, new Vector3(0f, 0f, -HallHalfZ - 0.3f), Vector3.back, 7f, F, StructureMaterial.Stone, 2);
			// The dais across the far end, and its flight across its front (the planner's contract: ThroneDaisSteps).
			using (d.Builds(StepRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(-HallInX - 0.05f, F - 0.05f, daisFront), new Vector3(HallInX + 0.05f, daisTop, HallInZ + 0.05f), StructureMaterial.Stone, 3));
			}
			Steps(d, new Vector3(0f, 0f, daisFront), Vector3.back, 2f * HallInX + 0.1f, daisTop, StructureMaterial.Stone, 4, F);

			float roomTop, chainTop, bannerTop, doorWidth, doorHeight;
			if (gothic)
			{
				doorWidth = 2.6f;
				doorHeight = 4.6f;
				GothicHall(d, doorWidth, doorHeight, out roomTop, out chainTop, out bannerTop);
			}
			else
			{
				// Under the gallery: the door no higher than the gallery's beams.
				doorWidth = 2.4f;
				doorHeight = 3.3f;
				CupolaHall(d, doorWidth, doorHeight, out roomTop, out chainTop, out bannerTop);
			}
			HangDoor(d, new Vector3(0f, F, -HallInZ + 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight, StructureDoorKind.Studded);

			// The runner from inside the door's swing to the dais' flight, and a rug on the dais before the throne.
			d.Add(StructureShapes.BoxBetween(new Vector3(-1.2f, F - 0.02f, -HallInZ + 1.7f), new Vector3(1.2f, F + 0.02f, daisFront - ThroneDaisSteps - 0.05f), StructureMaterial.Dyed, 700));
			d.Add(StructureShapes.BoxBetween(new Vector3(-2.2f, daisTop - 0.02f, daisFront + 0.4f), new Vector3(2.2f, daisTop + 0.02f, HallInZ - 0.15f), StructureMaterial.Dyed, 701));

			// The cloth of estate on the back wall over the throne, and its canopy (the tester) on two posts.
			d.Add(StructureShapes.Box(new Vector3(0f, daisTop + 2.3f, HallInZ - 0.03f), new Vector3(2.6f, 4.4f, 0.04f), StructureMaterial.Dyed, 710, StructureRole.Detail));
			const float testerZ = HallInZ - 1.7f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 1.5f, daisTop, testerZ + 0.08f), new Vector3(s * 1.5f, daisTop + 4.6f, testerZ + 0.08f), 0.16f, 0.16f, StructureMaterial.Timber, 711 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.BoxBetween(new Vector3(-1.65f, daisTop + 4.4f, testerZ), new Vector3(1.65f, daisTop + 4.6f, HallInZ - 0.02f), StructureMaterial.Dyed, 713));
			if (d.Mid)
			{
				// The valance hanging round the tester's three open sides.
				d.Add(StructureShapes.BoxBetween(new Vector3(-1.66f, daisTop + 4.0f, testerZ - 0.03f), new Vector3(1.66f, daisTop + 4.4f, testerZ), StructureMaterial.Dyed, 714, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.BoxBetween(new Vector3(s * 1.66f - 0.015f, daisTop + 4.0f, testerZ), new Vector3(s * 1.66f + 0.015f, daisTop + 4.4f, HallInZ - 0.05f),
						StructureMaterial.Dyed, 715 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Sphere(new Vector3(s * 1.5f, daisTop + 4.78f, testerZ + 0.08f), new Vector3(0.13f, 0.16f, 0.13f), 6, 4, StructureMaterial.Iron, 717 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// Banners hanging in the bays down both rows (not the front bay nor the dais').
			List<Vector2> bays = HallBays();
			for (int b = 1; b < bays.Count - 1; b++)
			{
				float z = (bays[b].x + bays[b].y) * 0.5f;
				for (int s = -1; s <= 1; s += 2)
				{
					int k = 720 + b * 4 + (s > 0 ? 2 : 0);
					d.Add(StructureShapes.BoxBetween(new Vector3(s * HallPierX - 0.02f, bannerTop - 2.3f, z - 0.55f), new Vector3(s * HallPierX + 0.02f, bannerTop - 0.05f, z + 0.55f), StructureMaterial.Dyed, k, StructureRole.Detail));
					if (d.Mid)
					{
						d.Add(StructureShapes.Beam(new Vector3(s * HallPierX, bannerTop - 0.05f, z - 0.7f), new Vector3(s * HallPierX, bannerTop - 0.05f, z + 0.7f), 0.06f, 0.06f, StructureMaterial.Iron, k + 1, StructureRole.Detail));
					}
				}
			}

			// Two iron chandeliers over the nave, each a hoop of candles on a chain from a tie beam.
			foreach ((float z, int n) in new[] { (HallPiers[1], 0), (HallPiers[3], 1) })
			{
				int k = 760 + n * 20;
				float hoop = F + 4.6f;
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.95f, hoop - 0.05f), new Vector2(1.05f, hoop - 0.05f), new Vector2(1.05f, hoop + 0.05f), new Vector2(0.95f, hoop + 0.05f) },
					d.Seg(12, 6), StructureMaterial.Iron, k, true, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, z))));
				d.Add(StructureShapes.Beam(new Vector3(0f, hoop, z), new Vector3(0f, chainTop, z), 0.05f, 0.05f, StructureMaterial.Iron, k + 1, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(-1f, hoop, z), new Vector3(1f, hoop, z), 0.05f, 0.05f, StructureMaterial.Iron, k + 2, StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(0f, hoop, z - 1f), new Vector3(0f, hoop, z + 1f), 0.05f, 0.05f, StructureMaterial.Iron, k + 3, StructureRole.Detail));
				}
				if (d.Fine)
				{
					for (int c = 0; c < 6; c++)
					{
						float a = c * Mathf.PI / 3f;
						d.Add(StructureShapes.Box(new Vector3(Mathf.Cos(a), hoop + 0.14f, z + Mathf.Sin(a)), new Vector3(0.05f, 0.18f, 0.05f), StructureMaterial.Lamplight, k + 4 + c, StructureRole.Detail));
					}
				}
				d.Emit(new Vector3(0f, hoop + 0.2f, z), SceneEmitterKind.Lamp);
			}

			// The throne room: the whole floor inside the walls (the dais is at its back: see the remarks). Kept clear of other
			// furniture: the dais and its flight, the runner, and each pier with room round it.
			d.Room(new Vector3(0f, F, 0f), new Vector2(2f * HallInX, 2f * HallInZ), roomTop - F, StructureRoomKind.ThroneRoom);
			d.RoomClear(new Vector2(-HallInX, daisFront - ThroneDaisSteps), new Vector2(HallInX, HallInZ));
			d.RoomClear(new Vector2(-1.6f, -HallInZ), new Vector2(1.6f, daisFront - ThroneDaisSteps));
			foreach (float z in HallPiers)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.RoomClear(new Vector2(s * HallPierX - 0.7f, z - 0.7f), new Vector2(s * HallPierX + 0.7f, z + 0.7f));
				}
			}
			if (!gothic)
			{
				// The gallery's stair along the left wall, and the floor before its foot.
				float foot = GalleryFront + (Mathf.CeilToInt(GalleryRise / StepRise - 1e-3f) - 1) * StepTread;
				d.RoomClear(new Vector2(-HallInX, GalleryFront), new Vector2(-HallInX + 1.95f, foot + 1.4f));
			}
		}

		/// <summary>
		/// v0, the Gothic basilica: a tall nave over the arcades of piers, lit by a clerestory over the lean-to roofs of the aisles;
		/// buttresses down the aisles between the bays, a square turret at each corner under a stone spirelet, a gabled portal on
		/// shafts round the door and a great window over it, and the nave's steep roof (it follows its palette).
		/// </summary>
		private static void GothicHall(StructureDraft d, float doorWidth, float doorHeight, out float roomTop, out float chainTop, out float bannerTop)
		{
			const float F = HallFloor;
			float aisleTop = F + 5.6f, arcadeTop = F + 6.4f, archTop = F + 5f, eave = F + 11.5f;
			const float clere = HallPierX + HallPier * 0.5f, aisleWall = HallHalfX - HallWall * 0.5f, leanPitch = 0.5f;
			float leanTop = aisleTop + (HallHalfX - clere) * leanPitch;
			roomTop = eave;
			chainTop = eave - 0.3f;
			bannerTop = archTop;
			List<Vector2> bays = HallBays();

			using (d.Builds(WallRoles))
			{
				for (int s = -1; s <= 1; s += 2)
				{
					int k = s > 0 ? 1 : 0;
					var arches = new List<WallOpening>();
					var lights = new List<WallOpening>();
					var aisleHoles = new List<WallOpening>();
					foreach (Vector2 bay in bays)
					{
						float along = (bay.x + bay.y) * 0.5f + HallInZ;
						arches.Add(new WallOpening { Along = along, Width = bay.y - bay.x, Bottom = F - 0.01f, Top = archTop });
						lights.Add(new WallOpening { Along = along, Width = 1.2f, Bottom = F + 8f, Top = F + 10.6f });
						aisleHoles.Add(new WallOpening { Along = along, Width = 1.2f, Bottom = F + 1.8f, Top = F + 4.6f });
					}
					// The arcade, the clerestory over it, and the aisle's outer wall.
					WallRun(d, new Vector3(s * HallPierX, 0f, -HallInZ), new Vector3(s * HallPierX, 0f, HallInZ), HallPier, F, arcadeTop, arches, StructureMaterial.Stone, 10 + k);
					WallRun(d, new Vector3(s * HallPierX, 0f, -HallInZ), new Vector3(s * HallPierX, 0f, HallInZ), HallPier, arcadeTop, eave, lights, StructureMaterial.Stone, 12 + k);
					WallRun(d, new Vector3(s * aisleWall, 0f, -HallInZ), new Vector3(s * aisleWall, 0f, HallInZ), HallWall, F, aisleTop, aisleHoles, StructureMaterial.Stone, 14 + k);
					// The wall's top raked up to the lean-to's underside: no slot between them for a body to be stood in.
					// (The lean-to's underside meets the wall's outer face at the wall's top, and rises a wall's thickness at the lean-to's pitch.)
					float under0 = aisleTop, under1 = under0 + HallWall * leanPitch;
					var rake = new[] { new Vector2(s * HallHalfX, aisleTop - 0.01f), new Vector2(s * HallInX, aisleTop - 0.01f), new Vector2(s * HallInX, under1 + 0.02f), new Vector2(s * HallHalfX, under0 + 0.02f) };
					d.Add(StructureShapes.Prism(rake, -HallInZ, HallInZ, StructureMaterial.Stone, 24 + k));
					// The ends of each aisle under its lean-to, front and back.
					foreach (float z in new[] { -HallHalfZ + HallWall * 0.5f, HallHalfZ - HallWall * 0.5f })
					{
						var end = new[] { new Vector2(s * HallHalfX, aisleTop - 0.01f), new Vector2(s * clere, aisleTop - 0.01f), new Vector2(s * clere, leanTop) };
						d.Add(StructureShapes.Prism(end, z - HallWall * 0.5f, z + HallWall * 0.5f, StructureMaterial.Stone, 16 + k * 2 + (z > 0f ? 1 : 0)));
					}
				}
				// The front: the doorway and the aisles' windows below, the great window over the door; the back likewise, its high
				// window over the canopy.
				float fz = -HallHalfZ + HallWall * 0.5f, bz = HallHalfZ - HallWall * 0.5f;
				var front = new List<WallOpening>
				{
					new WallOpening { Along = HallHalfX, Width = doorWidth, Bottom = F - 0.01f, Top = F + doorHeight },
					new WallOpening { Along = HallHalfX - 6.55f, Width = 1.2f, Bottom = F + 1.8f, Top = F + 4.6f },
					new WallOpening { Along = HallHalfX + 6.55f, Width = 1.2f, Bottom = F + 1.8f, Top = F + 4.6f },
				};
				WallRun(d, new Vector3(-HallHalfX, 0f, fz), new Vector3(HallHalfX, 0f, fz), HallWall, F, aisleTop, front, StructureMaterial.Stone, 20);
				WallRun(d, new Vector3(-clere, 0f, fz), new Vector3(clere, 0f, fz), HallWall, aisleTop, eave,
					new List<WallOpening> { new WallOpening { Along = clere, Width = 2.4f, Bottom = F + 6.6f, Top = F + 10.6f } }, StructureMaterial.Stone, 21);
				var back = new List<WallOpening>
				{
					new WallOpening { Along = HallHalfX - 6.55f, Width = 1.2f, Bottom = F + 1.8f, Top = F + 4.6f },
					new WallOpening { Along = HallHalfX + 6.55f, Width = 1.2f, Bottom = F + 1.8f, Top = F + 4.6f },
				};
				WallRun(d, new Vector3(-HallHalfX, 0f, bz), new Vector3(HallHalfX, 0f, bz), HallWall, F, aisleTop, back, StructureMaterial.Stone, 22);
				WallRun(d, new Vector3(-clere, 0f, bz), new Vector3(clere, 0f, bz), HallWall, aisleTop, eave,
					new List<WallOpening> { new WallOpening { Along = clere, Width = 2f, Bottom = F + 7.4f, Top = F + 10.6f } }, StructureMaterial.Stone, 23);
			}

			// The glass: the aisles' (trimmed), the clerestory's (high and plain), the front's and the back's.
			int g = 0;
			for (int s = -1; s <= 1; s += 2)
			{
				foreach (Vector2 bay in bays)
				{
					float z = (bay.x + bay.y) * 0.5f;
					PalaceLight(d, new Vector3(s * HallHalfX, F + 3.2f, z), new Vector3(s, 0f, 0f), HallWall, 1.2f, 2.8f, true, 200 + g * 4);
					PalaceLight(d, new Vector3(s * clere, F + 9.3f, z), new Vector3(s, 0f, 0f), HallPier, 1.2f, 2.6f, false, 260 + g * 4);
					g++;
				}
				PalaceLight(d, new Vector3(s * 6.55f, F + 3.2f, -HallHalfZ), Vector3.back, HallWall, 1.2f, 2.8f, true, 320 + (s > 0 ? 4 : 0));
				PalaceLight(d, new Vector3(s * 6.55f, F + 3.2f, HallHalfZ), Vector3.forward, HallWall, 1.2f, 2.8f, true, 330 + (s > 0 ? 4 : 0));
			}
			PalaceLight(d, new Vector3(0f, F + 8.6f, -HallHalfZ), Vector3.back, HallWall, 2.4f, 4f, true, 340);
			PalaceLight(d, new Vector3(0f, F + 9f, HallHalfZ), Vector3.forward, HallWall, 2f, 3.2f, true, 344);

			// The piers' bases and capitals, from the middle level.
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = 0; i < HallPiers.Length; i++)
					{
						int k = 400 + i * 4 + (s > 0 ? 2 : 0);
						d.Add(StructureShapes.Box(new Vector3(s * HallPierX, F + 0.2f, HallPiers[i]), new Vector3(HallPier + 0.24f, 0.4f, HallPier + 0.24f), StructureMaterial.Stone, k, StructureRole.Detail));
						d.Add(StructureShapes.Box(new Vector3(s * HallPierX, archTop - 0.15f, HallPiers[i]), new Vector3(HallPier + 0.2f, 0.3f, HallPier + 0.2f), StructureMaterial.Stone, k + 1, StructureRole.Detail));
					}
				}
				// Tie beams across the nave at the eaves, over the piers.
				for (int i = 1; i < HallPiers.Length - 1; i++)
				{
					d.Add(StructureShapes.Beam(new Vector3(-clere + HallPier, eave - 0.3f, HallPiers[i]), new Vector3(clere - HallPier, eave - 0.3f, HallPiers[i]), 0.3f, 0.35f, StructureMaterial.Timber, 440 + i, StructureRole.Detail));
				}
			}

			// The aisles' lean-tos (fixed at their own low pitch: lean-tos do not follow, StructureRoofs).
			for (int s = -1; s <= 1; s += 2)
			{
				var lean = new[]
				{
					new Vector2(s * (HallHalfX + 0.45f), aisleTop - 0.45f * leanPitch), new Vector2(s * clere, leanTop),
					new Vector2(s * clere, leanTop + 0.16f), new Vector2(s * (HallHalfX + 0.45f), aisleTop - 0.45f * leanPitch + 0.16f),
				};
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Prism(lean, -HallHalfZ - 0.3f, HallHalfZ + 0.3f, StructureMaterial.Timber, 50 + (s > 0 ? 1 : 0), StructureRole.Roof)).Grain = Vector3.forward;
				}
			}

			// The nave's roof, as its palette has it, and the stone gables at its ends.
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Timber, GothicHallRise, clere);
			if (roof.Flat)
			{
				FlatCanopy(d, 2f * clere, 2f * HallHalfZ, eave, 0.4f, 0.14f, StructureMaterial.Timber, 60, Matrix4x4.identity);
			}
			else
			{
				CivicPitched(d, roof, 2f * clere, 2f * HallHalfZ, eave, GothicHallRise, 0.4f, StructureMaterial.Timber, 60, Matrix4x4.identity);
				using (d.Builds(WallRoles))
				{
					Gable(d, 2f * clere - 0.02f, eave, roof.Rise - 0.02f, -HallHalfZ + HallWall * 0.5f, HallWall, StructureMaterial.Stone, 140, Matrix4x4.identity);
					Gable(d, 2f * clere - 0.02f, eave, roof.Rise - 0.02f, HallHalfZ - HallWall * 0.5f, HallWall, StructureMaterial.Stone, 141, Matrix4x4.identity);
				}
			}

			// The buttresses down the aisles at the piers, each under a sloped weathering.
			for (int s = -1; s <= 1; s += 2)
			{
				for (int i = 0; i < HallPiers.Length; i++)
				{
					int k = 460 + i * 4 + (s > 0 ? 2 : 0);
					float z = HallPiers[i], top = aisleTop - 0.6f;
					using (d.Builds(WallRoles))
					{
						d.Add(StructureShapes.Box(new Vector3(s * (HallHalfX + 0.35f), (top - FoundationDepth) * 0.5f, z), new Vector3(0.7f, top + FoundationDepth, 0.9f), StructureMaterial.Stone, k));
						var weathering = new[] { new Vector2(s * HallHalfX, top), new Vector2(s * (HallHalfX + 0.7f), top), new Vector2(s * HallHalfX, top + 0.6f) };
						d.Add(StructureShapes.Prism(weathering, z - 0.45f, z + 0.45f, StructureMaterial.Stone, k + 1));
					}
				}
			}

			// A square turret at each corner, up past the nave's eaves, a corbelled string course and a stone spirelet over it.
			const float side = 2.6f;
			float turretTop = F + 14.5f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					int k = 500 + (sx > 0 ? 10 : 0) + (sz > 0 ? 5 : 0);
					var c = new Vector3(sx * 9.3f, 0f, sz * 17.3f);
					using (d.Builds(WallRoles))
					{
						d.Add(StructureShapes.ChamferBox(c + Vector3.up * ((turretTop - FoundationDepth) * 0.5f), new Vector3(side, turretTop + FoundationDepth, side), d.Bevel(0.05f), StructureMaterial.Stone, k));
					}
					d.Add(StructureShapes.Lathe(new[] { new Vector2((side * 0.5f + 0.1f) * 1.414f, turretTop), new Vector2(0f, turretTop + 3.4f) }, 4, StructureMaterial.Stone, k + 1, false,
						Mathf.PI * 0.25f, false, false, StructureRole.Roof).Transform(Matrix4x4.Translate(c)));
					if (d.Mid)
					{
						d.Add(StructureShapes.Box(c + Vector3.up * (turretTop - 0.2f), new Vector3(side + 0.3f, 0.4f, side + 0.3f), StructureMaterial.Stone, k + 2, StructureRole.Detail));
						// A slit lighting the turret's stair, on its outer face.
						d.Add(StructureShapes.Box(c + new Vector3(sx * (side * 0.5f + 0.02f), F + 8.5f, 0f), new Vector3(0.04f, 1.2f, 0.24f), StructureMaterial.Glass, k + 3, StructureRole.Detail));
					}
					if (d.Fine)
					{
						d.Add(StructureShapes.Sphere(c + Vector3.up * (turretTop + 3.5f), new Vector3(0.16f, 0.16f, 0.16f), 6, 4, StructureMaterial.Iron, k + 4, StructureRole.Detail));
					}
				}
			}
			// The king's banners hanging down the front turrets' faces.
			for (int s = -1; s <= 1; s += 2)
			{
				var at = new Vector3(s * 9.3f, 0f, -17.3f - side * 0.5f - 0.04f);
				d.Add(StructureShapes.BoxBetween(at + new Vector3(-0.6f, F + 7f, -0.02f), at + new Vector3(0.6f, F + 11f, 0.02f), StructureMaterial.Dyed, 530 + (s > 0 ? 1 : 0), StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(at + new Vector3(-0.75f, F + 11f, -0.05f), at + new Vector3(0.75f, F + 11f, -0.05f), 0.07f, 0.07f, StructureMaterial.Iron, 532 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The portal: shafts either side of the door, a lintel and a gablet over it, and the king's arms carved in it.
			if (d.Mid)
			{
				float pz = -HallHalfZ - 0.2f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Cylinder(0.14f, 0f, F + doorHeight, d.Seg(8, 6), StructureMaterial.Monolith, 540 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(s * (doorWidth * 0.5f + 0.35f), 0f, pz))));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, F + doorHeight + 0.15f, pz), new Vector3(doorWidth + 1.1f, 0.3f, 0.4f), StructureMaterial.Stone, 542, StructureRole.Detail));
				var gablet = new[] { new Vector2(-(doorWidth * 0.5f + 0.65f), F + doorHeight + 0.3f), new Vector2(doorWidth * 0.5f + 0.65f, F + doorHeight + 0.3f), new Vector2(0f, F + doorHeight + 1.9f) };
				d.Add(StructureShapes.Prism(gablet, -HallHalfZ - 0.4f, -HallHalfZ, StructureMaterial.Stone, 543, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, F + doorHeight + 0.85f, -HallHalfZ - 0.43f), new Vector3(0.7f, 0.8f, 0.06f), StructureMaterial.Dyed, 544, StructureRole.Detail));
			}
		}

		/// <summary>
		/// v1, the hall of columns: one great roof over the whole hall on walls 9 m high, two rows of round columns carrying timber
		/// plates down the nave and tie beams across it, a lead-domed cupola on the ridge, a round tower under a cone at each corner, a
		/// round-arched portal; inside, a minstrels' gallery over the door, reached by a stair up the left wall.
		/// </summary>
		private static void CupolaHall(StructureDraft d, float doorWidth, float doorHeight, out float roomTop, out float chainTop, out float bannerTop)
		{
			const float F = HallFloor;
			float eave = F + 9f, plate = F + 5.6f, tie = eave - 0.2f;
			const float sideWall = HallHalfX - HallWall * 0.5f;
			roomTop = eave;
			chainTop = tie - 0.2f;
			bannerTop = plate;
			List<Vector2> bays = HallBays();

			using (d.Builds(WallRoles))
			{
				for (int s = -1; s <= 1; s += 2)
				{
					// A tall window to a bay; the front bay's over the gallery's floor.
					var holes = new List<WallOpening>();
					for (int b = 0; b < bays.Count; b++)
					{
						float along = (bays[b].x + bays[b].y) * 0.5f + HallInZ;
						holes.Add(b == 0
							? new WallOpening { Along = along, Width = 1.4f, Bottom = F + 4.8f, Top = F + 7.6f }
							: new WallOpening { Along = along, Width = 1.4f, Bottom = F + 3.2f, Top = F + 7.2f });
					}
					WallRun(d, new Vector3(s * sideWall, 0f, -HallInZ), new Vector3(s * sideWall, 0f, HallInZ), HallWall, F, eave, holes, StructureMaterial.Stone, 10 + (s > 0 ? 1 : 0));
				}
				// The front in two courses (WallRun cuts one hole a span along a wall): the doorway below, and over it the gallery's
				// three windows, the middle one over the portal's arch.
				float fz = -HallHalfZ + HallWall * 0.5f, bz = HallHalfZ - HallWall * 0.5f, course = F + 4.6f;
				var door = new List<WallOpening> { new WallOpening { Along = HallHalfX, Width = doorWidth, Bottom = F - 0.01f, Top = F + doorHeight } };
				WallRun(d, new Vector3(-HallHalfX, 0f, fz), new Vector3(HallHalfX, 0f, fz), HallWall, F, course, door, StructureMaterial.Stone, 20);
				var front = new List<WallOpening>
				{
					new WallOpening { Along = HallHalfX - 6f, Width = 1.2f, Bottom = F + 4.8f, Top = F + 7.6f },
					new WallOpening { Along = HallHalfX, Width = 2.2f, Bottom = F + 5.4f, Top = F + 8.2f },
					new WallOpening { Along = HallHalfX + 6f, Width = 1.2f, Bottom = F + 4.8f, Top = F + 7.6f },
				};
				WallRun(d, new Vector3(-HallHalfX, 0f, fz), new Vector3(HallHalfX, 0f, fz), HallWall, course, eave, front, StructureMaterial.Stone, 21);
				var back = new List<WallOpening>
				{
					new WallOpening { Along = HallHalfX - 6f, Width = 1.2f, Bottom = F + 2f, Top = F + 5f },
					new WallOpening { Along = HallHalfX, Width = 2f, Bottom = F + 5.8f, Top = F + 8.4f },
					new WallOpening { Along = HallHalfX + 6f, Width = 1.2f, Bottom = F + 2f, Top = F + 5f },
				};
				WallRun(d, new Vector3(-HallHalfX, 0f, bz), new Vector3(HallHalfX, 0f, bz), HallWall, F, eave, back, StructureMaterial.Stone, 22);
			}
			int g = 0;
			for (int s = -1; s <= 1; s += 2)
			{
				for (int b = 0; b < bays.Count; b++)
				{
					float z = (bays[b].x + bays[b].y) * 0.5f;
					float mid = b == 0 ? F + 6.2f : F + 5.2f, height = b == 0 ? 2.8f : 4f;
					PalaceLight(d, new Vector3(s * HallHalfX, mid, z), new Vector3(s, 0f, 0f), HallWall, 1.4f, height, true, 200 + g * 4);
					g++;
				}
				PalaceLight(d, new Vector3(s * 6f, F + 6.2f, -HallHalfZ), Vector3.back, HallWall, 1.2f, 2.8f, true, 320 + (s > 0 ? 4 : 0));
				PalaceLight(d, new Vector3(s * 6f, F + 3.5f, HallHalfZ), Vector3.forward, HallWall, 1.2f, 3f, true, 330 + (s > 0 ? 4 : 0));
			}
			PalaceLight(d, new Vector3(0f, F + 6.8f, -HallHalfZ), Vector3.back, HallWall, 2.2f, 2.8f, true, 340);
			PalaceLight(d, new Vector3(0f, F + 7.1f, HallHalfZ), Vector3.forward, HallWall, 2f, 2.6f, true, 344);

			// The columns down the nave, the plates they carry, the tie beams across at the eaves and the struts between.
			int seg = d.Seg(10, 6);
			for (int s = -1; s <= 1; s += 2)
			{
				for (int i = 0; i < HallPiers.Length; i++)
				{
					int k = 400 + i * 4 + (s > 0 ? 2 : 0);
					var at = new Vector3(s * HallPierX, 0f, HallPiers[i]);
					d.Add(StructureShapes.Box(at + Vector3.up * (F + 0.2f), new Vector3(0.95f, 0.4f, 0.95f), StructureMaterial.Stone, k));
					d.Add(StructureShapes.Cylinder(0.36f, F + 0.4f, plate - 0.5f, seg, StructureMaterial.Monolith, k + 1).Transform(Matrix4x4.Translate(at)));
					d.Add(StructureShapes.Box(at + Vector3.up * (plate - 0.25f), new Vector3(0.95f, 0.5f, 0.95f), StructureMaterial.Stone, k + 2));
				}
				d.Add(StructureShapes.Beam(new Vector3(s * HallPierX, plate + 0.2f, -HallInZ), new Vector3(s * HallPierX, plate + 0.2f, HallInZ), 0.45f, 0.4f, StructureMaterial.Timber, 430 + (s > 0 ? 1 : 0)));
			}
			for (int i = 0; i < HallPiers.Length; i++)
			{
				d.Add(StructureShapes.Beam(new Vector3(-HallInX - 0.3f, tie, HallPiers[i]), new Vector3(HallInX + 0.3f, tie, HallPiers[i]), 0.35f, 0.4f, StructureMaterial.Timber, 440 + i));
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(s * HallPierX, plate + 0.4f, HallPiers[i]), new Vector3(s * HallPierX, tie - 0.2f, HallPiers[i]), 0.25f, 0.25f, StructureMaterial.Timber,
							450 + i * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}

			HallGallery(d);

			// The great roof as its palette has it, the stone gables at its ends, and the cupola on its ridge.
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Timber, CupolaHallRise, HallHalfX);
			float rise = 0f;
			if (roof.Flat)
			{
				FlatCanopy(d, 2f * HallHalfX, 2f * HallHalfZ, eave, 0.5f, 0.14f, StructureMaterial.Timber, 60, Matrix4x4.identity);
			}
			else
			{
				rise = roof.Rise;
				CivicPitched(d, roof, 2f * HallHalfX, 2f * HallHalfZ, eave, CupolaHallRise, 0.5f, StructureMaterial.Timber, 60, Matrix4x4.identity);
				using (d.Builds(WallRoles))
				{
					Gable(d, 2f * HallHalfX - 0.02f, eave, rise - 0.02f, -HallHalfZ + HallWall * 0.5f, HallWall, StructureMaterial.Stone, 140, Matrix4x4.identity);
					Gable(d, 2f * HallHalfX - 0.02f, eave, rise - 0.02f, HallHalfZ - HallWall * 0.5f, HallWall, StructureMaterial.Stone, 141, Matrix4x4.identity);
				}
			}
			HallCupola(d, eave + rise, eave);

			// A round tower at each corner: a battered foot, a corbelled crown and a cone; the king's banner on the front right.
			int tseg = d.Seg(12, 8);
			const float r = 1.8f;
			float towerTop = F + 12.5f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					int k = 500 + (sx > 0 ? 10 : 0) + (sz > 0 ? 5 : 0);
					Matrix4x4 at = Matrix4x4.Translate(new Vector3(sx * 9.4f, 0f, sz * 17.4f));
					using (d.Builds(WallRoles))
					{
						d.Add(StructureShapes.Lathe(new[] { new Vector2(r + 0.2f, -FoundationDepth), new Vector2(r, 1.2f), new Vector2(r, towerTop - 1.1f), new Vector2(r + 0.3f, towerTop - 0.7f),
							new Vector2(r + 0.3f, towerTop), new Vector2(0f, towerTop) }, tseg, StructureMaterial.Stone, k).Transform(at));
					}
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r + 0.45f, towerTop - 0.1f), new Vector2(0f, towerTop + 4.2f) }, tseg, StructureMaterial.Timber, k + 1, true, 0f, false, false,
						StructureRole.Roof).Transform(at));
					if (d.Mid)
					{
						// Slits up the tower's outward faces.
						Vector3 outward = new Vector3(sx, 0f, sz).normalized;
						foreach ((float y, int n) in new[] { (F + 4f, 0), (F + 8.5f, 1) })
						{
							d.Add(StructureShapes.Block(at.MultiplyPoint3x4(outward * (r + 0.01f)) + Vector3.up * y, new Vector3(0.24f, 1.1f, 0.05f), Quaternion.LookRotation(-outward, Vector3.up),
								StructureMaterial.Glass, k + 2 + n, StructureRole.Detail));
						}
					}
				}
			}
			var pole = new Vector3(9.4f, towerTop + 4.1f, -17.4f);
			d.Add(StructureShapes.Beam(pole, pole + Vector3.up * 2.6f, 0.09f, 0.09f, StructureMaterial.Timber, 530, StructureRole.Detail));
			d.Add(StructureShapes.Box(pole + new Vector3(-0.66f, 2.15f, 0f), new Vector3(1.2f, 0.8f, 0.04f), StructureMaterial.Dyed, 531, StructureRole.Detail));

			// The portal: a round arch of voussoirs over the door, a tympanum with the king's arms in it.
			if (d.Mid)
			{
				float spring = F + doorHeight, inner = doorWidth * 0.5f + 0.2f, outer = inner + 0.4f;
				const int stones = 7;
				for (int i = 0; i < stones; i++)
				{
					float a = Mathf.PI * (i + 0.5f) / stones;
					var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
					float length = Mathf.PI * outer / stones * 0.92f;
					d.Add(StructureShapes.Block(new Vector3(0f, spring, -HallHalfZ - 0.12f) + dir * ((inner + outer) * 0.5f), new Vector3(length, outer - inner, 0.26f),
						Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f), StructureMaterial.Stone, 540 + i, StructureRole.Detail));
				}
				var tympanum = new List<Vector2>();
				for (int i = 0; i <= 6; i++)
				{
					float a = Mathf.PI * i / 6f;
					tympanum.Add(new Vector2(Mathf.Cos(a) * inner, spring + Mathf.Sin(a) * inner));
				}
				d.Add(StructureShapes.Prism(tympanum, -HallHalfZ - 0.06f, -HallHalfZ + 0.01f, StructureMaterial.Stone, 548, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, spring + inner * 0.45f, -HallHalfZ - 0.09f), new Vector3(0.6f, 0.7f, 0.05f), StructureMaterial.Dyed, 549, StructureRole.Detail));
			}
		}

		/// <summary>
		/// v1's minstrels' gallery over the door: a timber floor across the hall's front on two posts, a solid front to a rail's
		/// height, and a straight flight up the left wall between the wall and a side wall (no open side anywhere: a metre's fall is
		/// guarded). Its "lookout" mark is where the reach check confirms it is walked to.
		/// </summary>
		private static void HallGallery(StructureDraft d)
		{
			const float F = HallFloor, deck = F + GalleryRise, x0 = -HallInX, x1 = -HallInX + 1.4f, spine = 0.25f;
			int n = Mathf.CeilToInt(GalleryRise / StepRise - 1e-3f);
			float head = GalleryFront, foot = head + (n - 1) * StepTread;
			using (d.Builds(FloorRoles))
			{
				StructureSolid floor = StructureShapes.BoxBetween(new Vector3(-HallInX - 0.05f, deck - 0.25f, -HallInZ - 0.05f), new Vector3(HallInX + 0.05f, deck, head), StructureMaterial.Timber, 600);
				floor.Grain = Vector3.right;
				d.Add(floor);
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * HallPierX, F, head - 0.2f), new Vector3(s * HallPierX, deck - 0.25f, head - 0.2f), 0.3f, 0.3f, StructureMaterial.Timber, 601 + (s > 0 ? 1 : 0)));
			}
			// Its front: a solid panelled screen a rail's height over the floor, from the stair's side wall to the right wall.
			d.Add(StructureShapes.BoxBetween(new Vector3(x1, deck - 0.02f, head - 0.12f), new Vector3(HallInX + 0.05f, deck + 1.05f, head), StructureMaterial.Timber, 603));
			if (d.Mid)
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(x1, deck + 1.05f, head - 0.16f), new Vector3(HallInX + 0.05f, deck + 1.12f, head + 0.04f), StructureMaterial.Timber, 604, StructureRole.Detail));
			}
			// The flight up the left wall toward the front, each step solid to the floor.
			using (d.Builds(StepRoles))
			{
				for (int i = 1; i < n; i++)
				{
					float top = F + GalleryRise * i / n, reach = (n - i) * StepTread;
					d.Add(StructureShapes.BoxBetween(new Vector3(x0 - 0.05f, F - 0.05f, head), new Vector3(x1, top, head + reach), StructureMaterial.Stone, 610 + i));
				}
			}
			// Its side wall, from the floor to a rail's height over the treads, meeting the gallery's front at its head.
			var side = new[]
			{
				new Vector2(foot + 0.3f, F - 0.05f), new Vector2(foot + 0.3f, F + 1.0f), new Vector2(head, deck + 1.05f), new Vector2(head - 0.12f, deck + 1.05f), new Vector2(head - 0.12f, F - 0.05f),
			};
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Prism(side, x1, x1 + spine, StructureMaterial.Stone, 640).Transform(PalaceSwapXZ));
			}
			d.Mark("lookout-gallery", new Vector3(1.5f, deck, -HallInZ + 1.4f), 0f);
		}

		/// <summary>
		/// v1's cupola on the ridge at the hall's middle: a timber base straddling the ridge, an octagonal drum louvred on four faces,
		/// a lead dome swelling to a point and a finial (a cupola is no roof that follows a palette: it stands on whatever roof is laid).
		/// </summary>
		private static void HallCupola(StructureDraft d, float ridge, float eave)
		{
			float baseFoot = eave + Mathf.Max(0f, ridge - eave - 1.4f), baseTop = ridge + 0.6f, drumTop = baseTop + 2f;
			d.Add(StructureShapes.BoxBetween(new Vector3(-1.3f, baseFoot, -1.3f), new Vector3(1.3f, baseTop, 1.3f), StructureMaterial.Timber, 650));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.1f, baseTop), new Vector2(1.1f, drumTop), new Vector2(0f, drumTop) }, 8, StructureMaterial.Timber, 651, false, Mathf.PI / 8f));
			int seg = d.Seg(12, 8);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.3f, drumTop - 0.05f), new Vector2(1.35f, drumTop + 0.3f), new Vector2(0.95f, drumTop + 1.2f), new Vector2(0.28f, drumTop + 1.9f),
				new Vector2(0.08f, drumTop + 2.4f), new Vector2(0f, drumTop + 2.6f) }, seg, StructureMaterial.Iron, 652, true, 0f, false, false, StructureRole.Roof));
			if (d.Mid)
			{
				for (int f = 0; f < 4; f++)
				{
					Quaternion turn = Quaternion.Euler(0f, f * 90f, 0f);
					d.Add(StructureShapes.Block(turn * new Vector3(0f, (baseTop + drumTop) * 0.5f, -1.03f), new Vector3(0.7f, 1.4f, 0.06f), turn, StructureMaterial.Iron, 653 + f, StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				d.Add(StructureShapes.Sphere(new Vector3(0f, drumTop + 2.75f, 0f), new Vector3(0.18f, 0.18f, 0.18f), 6, 4, StructureMaterial.Iron, 657, StructureRole.Detail));
			}
		}

		// ── Palace wing ───────────────────────────────────────────────

		/// <summary>The wing's outer half width (x) and depth (z), its walls' thickness and the rooms inside them (half), metres.</summary>
		private const float WingHalfX = 14f, WingHalfZ = 6f, WingWall = 0.9f, WingInX = WingHalfX - WingWall, WingInZ = WingHalfZ - WingWall;
		/// <summary>The floors: the ground floor's (the plinth's top), the upper floor's, and the wall's top (v0's roof deck, v1's eaves), metres.</summary>
		private const float WingGround = 0.6f, WingUpper = 5f, WingTop = 9.2f;
		/// <summary>A floor's thickness under its top, metres.</summary>
		private const float WingSlab = 0.3f;
		/// <summary>The cross walls dividing each storey in three: their centre lines' x either side, and their thickness, metres.</summary>
		private const float WingCross = 3f, WingCrossWall = 0.5f;
		/// <summary>The doorways through the cross walls, both storeys: their middle (z), width and height, metres (on the stair's landing upstairs).</summary>
		private const float WingPassZ = 4f, WingPassWidth = 1.3f, WingPassHeight = 2.5f;
		/// <summary>The stair's breadth (from the left cross wall), the wall along its open side, its foot (z), and the parapet round its well, metres.</summary>
		private const float WingFlight = 1.4f, WingSpine = 0.25f, WingFoot = -3.4f, WingRail = 1f, WingCoping = 0.22f;
		/// <summary>v0's hipped roof: its rise over its half span (4.3 m) inside the parapet walk, as the kit builds it (StructureRoofs.Followers).</summary>
		private const float WingHipRise = 3f, WingHipHalf = 4.3f, WingHipLength = 12.3f;
		/// <summary>v1's steep roof over the walls: its rise over half the wing's depth (6 m), as the kit builds it (StructureRoofs.Followers).</summary>
		private const float WingGableRise = 6f;

		/// <summary>The wing's stair as <see cref="WingStair"/> laid it: its breadth across x, its head along z, and the well over it.</summary>
		private struct PalaceFlight
		{
			public float X0, X1, Head, WellFrom;

			public float WellMin => Mathf.Min(WellFrom, Head);
			public float WellMax => Mathf.Max(WellFrom, Head);
		}

		/// <summary>
		/// A palace's residential range (2026-10-19), two storeys of three rooms each, walked from its door to every room: the door
		/// in the middle of its long front (−z) into a porter's hall, the stair up its left cross wall to a landing at the back, a
		/// doorway through each cross wall on both floors. Below, a council chamber (left) and the great chamber (right); above, the
		/// bedchamber (left), the long gallery over the hall and the solar (right); a hearth against each end wall under its chimney.
		/// v0 battlemented with a low hipped roof behind its parapet and a round bartizan at each corner; v1 under a steep roof
		/// between stone gables, an oriel over its door.
		/// </summary>
		private static void PalaceWing(StructureDraft d)
		{
			bool battlemented = d.Variant == 0;
			const float G = WingGround, U = WingUpper, T = WingTop, half = WingHalfX, depth = WingHalfZ, W = WingWall;
			const float crossIn = WingCross - WingCrossWall * 0.5f, crossOut = WingCross + WingCrossWall * 0.5f;
			float upperTop = battlemented ? T - WingSlab : T;

			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (G - FoundationDepth) * 0.5f, 0f), new Vector3(2f * half + 0.6f, G + FoundationDepth, 2f * depth + 0.6f), d.Bevel(0.05f), StructureMaterial.Stone, 1));
			}
			Steps(d, new Vector3(0f, 0f, -depth - 0.3f), Vector3.back, 3f, G, StructureMaterial.Stone, 2);
			HangDoor(d, new Vector3(0f, G, -WingInZ + 0.05f), Vector3.right, Vector3.forward, 1.8f, 2.8f, StructureDoorKind.Studded);

			// The stair up the left cross wall, and the upper floor over everything but its well.
			PalaceFlight stair = WingStair(d, -crossIn, WingFoot, G, U, 2000);
			using (d.Builds(FloorRoles))
			{
				float lo = -WingInX - 0.05f, hi = WingInX + 0.05f, y = U - WingSlab * 0.5f, zlo = -WingInZ - 0.05f, zhi = WingInZ + 0.05f;
				float wx0 = stair.X0 - 0.05f, wx1 = stair.X1 + 0.05f;
				void Slab(float x0, float x1, float z0, float z1, int n)
				{
					if (x1 - x0 > 1e-3f && z1 - z0 > 1e-3f)
					{
						StructureSolid slab = StructureShapes.Box(new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(x1 - x0, WingSlab, z1 - z0), StructureMaterial.Timber, 60 + n);
						slab.Grain = Vector3.right;
						d.Add(slab);
					}
				}
				Slab(lo, hi, zlo, stair.WellMin, 0);
				Slab(lo, hi, stair.WellMax, zhi, 1);
				Slab(lo, wx0, stair.WellMin, stair.WellMax, 2);
				Slab(wx1, hi, stair.WellMin, stair.WellMax, 3);
				if (battlemented)
				{
					// The roof deck: the upper rooms' ceiling and the lead flat round the hipped roof.
					d.Add(StructureShapes.BoxBetween(new Vector3(lo, T - WingSlab, zlo), new Vector3(hi, T, zhi), StructureMaterial.Stone, 64));
				}
			}

			// Every hole through the outer walls, by face (0 front −z, 1 back +z, 2 left −x, 3 right +x) and storey.
			var holes = new List<WallOpening>[4, 2];
			for (int f = 0; f < 4; f++)
			{
				for (int s = 0; s < 2; s++)
				{
					holes[f, s] = new List<WallOpening>();
				}
			}
			var lights = new List<(int face, float at, float bottom)>();
			const float sill = 1f, tall = 1.8f, wide = 1f;
			void Light(int face, int storey, float at)
			{
				float bottom = (storey == 0 ? G : U) + sill;
				holes[face, storey].Add(new WallOpening { Along = at + (face < 2 ? half : WingInZ), Width = wide, Bottom = bottom, Top = bottom + tall });
				lights.Add((face, at, bottom));
			}
			holes[0, 0].Add(new WallOpening { Along = half, Width = 1.8f, Bottom = G - 0.01f, Top = G + 2.8f });
			for (int storey = 0; storey < 2; storey++)
			{
				foreach (float x in new[] { -11.2f, -8f, -4.8f, 4.8f, 8f, 11.2f })
				{
					Light(0, storey, x);
				}
				foreach (float x in new[] { -10f, -6f, 1.4f, 6f, 10f })
				{
					Light(1, storey, x);
				}
				foreach (float z in new[] { -3f, 3f })
				{
					Light(2, storey, z);
					Light(3, storey, z);
				}
			}
			if (battlemented)
			{
				// Over the door (v1's oriel stands there instead).
				Light(0, 1, 0f);
			}
			using (d.Builds(WallRoles))
			{
				for (int s = 0; s < 2; s++)
				{
					float y0 = s == 0 ? G : U, y1 = s == 0 ? U : T;
					int key = 10 + s * 8;
					WallRun(d, new Vector3(-half, 0f, -depth + W * 0.5f), new Vector3(half, 0f, -depth + W * 0.5f), W, y0, y1, holes[0, s], StructureMaterial.Stone, key);
					WallRun(d, new Vector3(-half, 0f, depth - W * 0.5f), new Vector3(half, 0f, depth - W * 0.5f), W, y0, y1, holes[1, s], StructureMaterial.Stone, key + 1);
					WallRun(d, new Vector3(-half + W * 0.5f, 0f, -WingInZ), new Vector3(-half + W * 0.5f, 0f, WingInZ), W, y0, y1, holes[2, s], StructureMaterial.Stone, key + 2);
					WallRun(d, new Vector3(half - W * 0.5f, 0f, -WingInZ), new Vector3(half - W * 0.5f, 0f, WingInZ), W, y0, y1, holes[3, s], StructureMaterial.Stone, key + 3);
					// The cross walls, a doorway through each at the back (upstairs, off the stair's landing).
					float top = s == 0 ? U - WingSlab : upperTop;
					for (int c = -1; c <= 1; c += 2)
					{
						var pass = new List<WallOpening> { new WallOpening { Along = WingPassZ + WingInZ, Width = WingPassWidth, Bottom = y0 - 0.01f, Top = y0 + WingPassHeight } };
						WallRun(d, new Vector3(c * WingCross, 0f, -WingInZ), new Vector3(c * WingCross, 0f, WingInZ), WingCrossWall, y0, top + 0.02f, pass, StructureMaterial.Stone, key + 4 + (c > 0 ? 1 : 0));
					}
				}
			}
			for (int i = 0; i < lights.Count; i++)
			{
				(int face, float at, float bottom) = lights[i];
				Vector3 outward = face == 0 ? Vector3.back : face == 1 ? Vector3.forward : face == 2 ? Vector3.left : Vector3.right;
				Vector3 c = face < 2 ? new Vector3(at, bottom + tall * 0.5f, outward.z * depth) : new Vector3(outward.x * half, bottom + tall * 0.5f, at);
				PalaceLight(d, c, outward, W, wide, tall, true, 200 + i * 4);
			}

			// The hearths' breasts against the end walls on both floors, and the chimney stack up each end's outer face.
			for (int s = -1; s <= 1; s += 2)
			{
				int k = 340 + (s > 0 ? 4 : 0);
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? WingInX - 0.45f : -WingInX - 0.02f, G, -0.9f), new Vector3(s > 0 ? WingInX + 0.02f : -WingInX + 0.45f, U - WingSlab, 0.9f),
					StructureMaterial.Fieldstone, k));
				d.Add(StructureShapes.BoxBetween(new Vector3(s > 0 ? WingInX - 0.45f : -WingInX - 0.02f, U, -0.9f), new Vector3(s > 0 ? WingInX + 0.02f : -WingInX + 0.45f, upperTop, 0.9f),
					StructureMaterial.Fieldstone, k + 1));
			}

			if (battlemented)
			{
				BattlementedWing(d);
			}
			else
			{
				GabledWing(d);
			}

			// The door's hood mould and the arms over it.
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, G + 2.95f, -depth - 0.08f), new Vector3(2.4f, 0.18f, 0.16f), StructureMaterial.Stone, 30, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, G + 3.6f, -depth - 0.04f), new Vector3(0.6f, 0.7f, 0.08f), StructureMaterial.Dyed, 31, StructureRole.Detail));
				// A string course round the walls at the upper floor.
				for (int f = 0; f < 4; f++)
				{
					bool across = f < 2;
					float sign = f % 2 == 0 ? -1f : 1f;
					Vector3 c = across ? new Vector3(0f, U - 0.08f, sign * (depth + 0.06f)) : new Vector3(sign * (half + 0.06f), U - 0.08f, 0f);
					d.Add(StructureShapes.Box(c, across ? new Vector3(2f * half + 0.24f, 0.16f, 0.12f) : new Vector3(0.12f, 0.16f, 2f * depth), StructureMaterial.Stone, 32 + f, StructureRole.Detail));
				}
			}

			// The rooms: each floor's three, the ways through the cross walls kept clear, the stair band and the well over it kept
			// clear in the middle, each end room's hearth against its breast.
			float sideW = WingInX - crossOut, sideC = (WingInX + crossOut) * 0.5f;
			float lowH = U - WingSlab - G, highH = upperTop - U;
			var passMin = WingPassZ - WingPassWidth * 0.5f - 0.35f;
			void SideRoom(float floor, float height, int s, StructureRoomKind kind)
			{
				d.Room(new Vector3(s * sideC, floor, 0f), new Vector2(sideW, 2f * WingInZ), height, kind);
				d.RoomClear(new Vector2(s * crossOut, passMin), new Vector2(s * (crossOut + 1.5f), WingInZ));
				d.RoomHearth(new Vector2(s * (WingInX - 0.45f - 0.47f), 0f), s < 0 ? -90f : 90f, floor);
			}
			SideRoom(G, lowH, -1, StructureRoomKind.Guildhall);
			d.Room(new Vector3(0f, G, 0f), new Vector2(2f * crossIn, 2f * WingInZ), lowH, StructureRoomKind.Guard);
			d.RoomClear(new Vector2(-crossIn, -WingInZ), new Vector2(stair.X1 + WingSpine + 0.75f, WingInZ));
			d.RoomClear(new Vector2(crossIn - 1.5f, passMin), new Vector2(crossIn, WingInZ));
			SideRoom(G, lowH, 1, StructureRoomKind.Hall);
			SideRoom(U, highH, -1, StructureRoomKind.Chamber);
			d.Room(new Vector3(0f, U, 0f), new Vector2(2f * crossIn, 2f * WingInZ), highH, StructureRoomKind.Library);
			d.RoomClear(new Vector2(-crossIn, -WingInZ), new Vector2(stair.X1 + WingSpine + 0.75f, WingInZ));
			d.RoomClear(new Vector2(crossIn - 1.5f, passMin), new Vector2(crossIn, WingInZ));
			SideRoom(U, highH, 1, StructureRoomKind.Dwelling);
		}

		/// <summary>
		/// The wing's stair: a straight flight against the left cross wall's face at <paramref name="wallX"/>, climbing +z from
		/// <paramref name="foot"/> on the floor at <paramref name="from"/> to the floor at <paramref name="to"/>, as the keep's are
		/// (StructurePieces.Keep.cs: KeepStairs): each step solid from the floor to its tread, a wall along its open side up to the
		/// floor over, and from there a parapet along the well and across its far end, under a saddleback coping.
		/// </summary>
		private static PalaceFlight WingStair(StructureDraft d, float wallX, float foot, float from, float to, int key)
		{
			var s = new PalaceFlight { X0 = wallX, X1 = wallX + WingFlight };
			float rise = to - from;
			int n = Mathf.CeilToInt(rise / StepRise - 1e-3f);
			s.Head = foot + (n - 1) * StepTread;
			// The first step whose tread a climber's head would meet the floor over: the well opens from there.
			int firstOpen = Mathf.Clamp(Mathf.CeilToInt((to - WingSlab - StairHeadroom - from) / rise * n), 1, n);
			s.WellFrom = s.Head - ((n - firstOpen + 1) * StepTread + 0.1f);
			float bottom = from - 0.1f;
			using (d.Builds(StepRoles))
			{
				for (int i = 1; i < n; i++)
				{
					float top = from + rise * i / n, reach = (n - i) * StepTread;
					d.Add(StructureShapes.BoxBetween(new Vector3(s.X0 - 0.05f, bottom, s.Head - reach), new Vector3(s.X1, top, s.Head), StructureMaterial.Stone, key + i));
				}
			}
			float sx0 = s.X1, sx1 = sx0 + WingSpine;
			float under = to - WingSlab, rail = to + WingRail, coping = rail - WingCoping;
			float e0 = s.WellFrom - WingSpine, e1 = s.WellFrom;
			float ex0 = s.X0 - 0.05f, ex1 = sx1;
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(sx0, bottom, foot), new Vector3(sx1, under + 0.02f, s.Head), StructureMaterial.Stone, key + 90));
				d.Add(StructureShapes.BoxBetween(new Vector3(sx0, under, s.WellMin), new Vector3(sx1, coping, s.WellMax), StructureMaterial.Stone, key + 91));
				d.Add(StructureShapes.Prism(new[] { new Vector2(sx0, coping), new Vector2(sx1, coping), new Vector2((sx0 + sx1) * 0.5f, rail) }, s.WellMin, s.WellMax,
					StructureMaterial.Stone, key + 93));
				d.Add(StructureShapes.BoxBetween(new Vector3(ex0, under, e0), new Vector3(ex1, coping, e1), StructureMaterial.Stone, key + 92));
				d.Add(StructureShapes.Prism(new[] { new Vector2(e0, coping), new Vector2(e1, coping), new Vector2((e0 + e1) * 0.5f, rail) }, ex0, ex1, StructureMaterial.Stone, key + 94)
					.Transform(PalaceSwapXZ));
			}
			return s;
		}

		/// <summary>
		/// v0's top: a parapet round the roof deck under plain merlons, a round bartizan corbelled out at each corner under a cone, a
		/// low hipped roof on a curb inside the walk (the palette's pitch, or none where it roofs flat: the deck is the roof), and the
		/// chimney stacks standing clear of the parapet.
		/// </summary>
		private static void BattlementedWing(StructureDraft d)
		{
			const float T = WingTop, half = WingHalfX, depth = WingHalfZ, p = 0.6f, top = T + 1.1f;
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-half, 0f, -depth + p * 0.5f), new Vector3(half, 0f, -depth + p * 0.5f), p, T, top, null, StructureMaterial.Stone, 40);
				WallRun(d, new Vector3(-half, 0f, depth - p * 0.5f), new Vector3(half, 0f, depth - p * 0.5f), p, T, top, null, StructureMaterial.Stone, 41);
				WallRun(d, new Vector3(-half + p * 0.5f, 0f, -depth + p), new Vector3(-half + p * 0.5f, 0f, depth - p), p, T, top, null, StructureMaterial.Stone, 42);
				WallRun(d, new Vector3(half - p * 0.5f, 0f, -depth + p), new Vector3(half - p * 0.5f, 0f, depth - p), p, T, top, null, StructureMaterial.Stone, 43);
			}
			// Plain merlons (no bevel: there are many), short of the bartizans at the corners.
			int key = 1000;
			void Merlons(Vector3 a, Vector3 b)
			{
				Vector3 run = b - a;
				float length = run.magnitude;
				Vector3 dir = run / length;
				int count = Mathf.Max(1, Mathf.FloorToInt((length + 0.7f) / 1.5f));
				float start = (length - (count * 0.8f + (count - 1) * 0.7f)) * 0.5f + 0.4f;
				Quaternion turn = Quaternion.FromToRotation(Vector3.right, dir);
				for (int i = 0; i < count; i++)
				{
					d.Add(StructureShapes.Block(a + dir * (start + i * 1.5f) + Vector3.up * (top + 0.35f), new Vector3(0.8f, 0.7f, p), turn, StructureMaterial.Stone, key++, StructureRole.Detail));
				}
			}
			Merlons(new Vector3(-half + 1.6f, 0f, -depth + p * 0.5f), new Vector3(half - 1.6f, 0f, -depth + p * 0.5f));
			Merlons(new Vector3(-half + 1.6f, 0f, depth - p * 0.5f), new Vector3(half - 1.6f, 0f, depth - p * 0.5f));
			Merlons(new Vector3(-half + p * 0.5f, 0f, -depth + 1.6f), new Vector3(-half + p * 0.5f, 0f, depth - 1.6f));
			Merlons(new Vector3(half - p * 0.5f, 0f, -depth + 1.6f), new Vector3(half - p * 0.5f, 0f, depth - 1.6f));

			// The bartizans.
			int seg = d.Seg(10, 6);
			for (int i = 0; i < 4; i++)
			{
				float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
				Matrix4x4 at = Matrix4x4.Translate(new Vector3(sx * (half - 0.3f), 0f, sz * (depth - 0.3f)));
				using (d.Builds(WallRoles))
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.3f, T - 1.4f), new Vector2(1f, T - 0.6f), new Vector2(1f, top + 0.5f), new Vector2(0f, top + 0.5f) }, seg, StructureMaterial.Stone, 1100 + i).Transform(at));
				}
				d.Add(StructureShapes.Lathe(new[] { new Vector2(1.15f, top + 0.4f), new Vector2(0f, top + 2.7f) }, seg, StructureMaterial.Timber, 1110 + i, true, 0f, false, false, StructureRole.Roof).Transform(at));
			}

			// The hipped roof inside the walk, on a low curb, its ridge along x.
			const float curb = T + 0.5f, eave = 0.2f;
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(-WingHipLength, T - 0.05f, -WingHipHalf), new Vector3(WingHipLength, curb, WingHipHalf), StructureMaterial.Stone, 1120));
			}
			OwnRoofPlan plan = OwnRoof(d, StructureMaterial.Timber, WingHipRise, WingHipHalf);
			if (!plan.Flat)
			{
				// Built with its ridge along z (a gable prism across, clipped back to the same slope at both ends: the hips), then
				// turned a quarter so the ridge runs along the wing.
				float a = WingHipHalf + eave, b = WingHipLength + eave, r = plan.Rise * (a / WingHipHalf), y0 = curb - plan.Rise * (eave / WingHipHalf);
				using (d.Builds(RoofRoles))
				{
					StructureSolid roof = StructureShapes.Prism(new[] { new Vector2(-a, y0), new Vector2(a, y0), new Vector2(0f, y0 + r) }, -b, b, StructureMaterial.Timber, 1121, StructureRole.Roof);
					float k = r / a;
					var n0 = new Vector3(0f, 1f, k).normalized;
					var n1 = new Vector3(0f, 1f, -k).normalized;
					roof.Clip(n0, Vector3.Dot(n0, new Vector3(0f, y0 + r, b - a)));
					roof.Clip(n1, Vector3.Dot(n1, new Vector3(0f, y0 + r, -(b - a))));
					roof.Transform(Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f)));
					roof.Grain = Vector3.right;
					d.Add(roof);
				}
			}
			WingChimneys(d, top + 1.6f);
		}

		/// <summary>
		/// v1's top: a steep roof over the walls (the palette's pitch, or a flat one), stone gables at its ends with a finial at
		/// each apex, the chimney stacks up past the ridge, and an oriel over the door: a bay of glass corbelled out from the upper
		/// storey under a stone pent.
		/// </summary>
		private static void GabledWing(StructureDraft d)
		{
			const float T = WingTop, half = WingHalfX, depth = WingHalfZ, U = WingUpper;
			Matrix4x4 place = Matrix4x4.Rotate(Quaternion.Euler(0f, 90f, 0f));
			OwnRoofPlan plan = OwnRoof(d, StructureMaterial.Timber, WingGableRise, depth);
			float rise = 0f;
			if (plan.Flat)
			{
				FlatCanopy(d, 2f * depth, 2f * half, T, 0.4f, 0.14f, StructureMaterial.Timber, 40, place);
			}
			else
			{
				rise = plan.Rise;
				CivicPitched(d, plan, 2f * depth, 2f * half, T, WingGableRise, 0.4f, StructureMaterial.Timber, 40, place);
				using (d.Builds(WallRoles))
				{
					Gable(d, 2f * depth - 0.02f, T, rise - 0.02f, -half + WingWall * 0.5f, WingWall, StructureMaterial.Stone, 90, place);
					Gable(d, 2f * depth - 0.02f, T, rise - 0.02f, half - WingWall * 0.5f, WingWall, StructureMaterial.Stone, 91, place);
				}
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.32f, T + rise - 0.1f), new Vector2(0f, T + rise + 1.1f) }, 4, StructureMaterial.Stone, 92 + (s > 0 ? 1 : 0), false,
							Mathf.PI * 0.25f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(s * (half - WingWall * 0.5f), 0f, 0f))));
					}
				}
			}
			WingChimneys(d, T + rise + 1f);

			// The oriel over the door.
			const float ox = 1.3f, oz = depth + 0.75f, o0 = U, o1 = U + 3.2f;
			d.Add(StructureShapes.BoxBetween(new Vector3(-ox, o0, -oz), new Vector3(ox, o1, -depth + 0.1f), StructureMaterial.Stone, 100));
			d.Add(StructureShapes.Prism(new[] { new Vector2(-depth + 0.1f, o0 - 1.1f), new Vector2(-depth + 0.1f, o0 + 0.01f), new Vector2(-oz, o0 + 0.01f) }, -ox, ox,
				StructureMaterial.Stone, 101).Transform(PalaceSwapXZ));
			d.Add(StructureShapes.Prism(new[] { new Vector2(-depth + 0.1f, o1 - 0.01f), new Vector2(-oz - 0.15f, o1 - 0.01f), new Vector2(-depth + 0.1f, o1 + 0.7f) }, -ox - 0.12f, ox + 0.12f,
				StructureMaterial.Stone, 102, StructureRole.Roof).Transform(PalaceSwapXZ));
			d.Add(StructureShapes.Box(new Vector3(0f, (o0 + o1) * 0.5f + 0.1f, -oz - 0.02f), new Vector3(2f * ox - 0.4f, 2.2f, 0.05f), StructureMaterial.Glass, 103, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (ox + 0.02f), (o0 + o1) * 0.5f + 0.1f, -(depth + oz) * 0.5f), new Vector3(0.05f, 2.2f, 0.45f), StructureMaterial.Glass, 104 + (s > 0 ? 1 : 0),
					StructureRole.Detail));
			}
			if (d.Mid)
			{
				for (int i = -1; i <= 1; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(i * 0.75f, (o0 + o1) * 0.5f + 0.1f, -oz - 0.05f), new Vector3(0.1f, 2.2f, 0.08f), StructureMaterial.Stone, 107 + i, StructureRole.Detail));
				}
			}
		}

		/// <summary>The chimney stacks up the wing's end walls' outer faces to <paramref name="top"/>, each capped, smoking.</summary>
		private static void WingChimneys(StructureDraft d, float top)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				int k = 1200 + (s > 0 ? 4 : 0);
				float x = s * (WingHalfX + 0.3f);
				d.Add(StructureShapes.ChamferBox(new Vector3(x, (top - FoundationDepth) * 0.5f, 0f), new Vector3(0.6f, top + FoundationDepth, 1.6f), 0f, StructureMaterial.Stone, k));
				d.Add(StructureShapes.Box(new Vector3(x, top + 0.07f, 0f), new Vector3(0.8f, 0.14f, 1.8f), StructureMaterial.Stone, k + 1, StructureRole.Detail));
				d.Emit(new Vector3(x, top + 0.4f, 0f), SceneEmitterKind.Smoke);
			}
		}

		// ── Throne ────────────────────────────────────────────────────

		/// <summary>
		/// The king's chair (2026-10-19), its back to the wall (+z) and facing −z as all furniture does, its seat a step up: v0 a
		/// high-backed carved oak throne, armrests on panelled sides, a dyed cushion and back panel, a gabled crest and metal-capped
		/// finials on its back posts and arms; v1 a stone throne on a broad step, its tall back slab between two pillars under pyramid
		/// finials, runes cut down it that glow (and pulse with the throne's own light). Its canopy is the hall's, not the chair's.
		/// </summary>
		private static void Throne(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				StoneThrone(d);
				return;
			}
			// The footboard, the solid carved base and the seat over it.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.06f, -0.02f), new Vector3(1.1f, 0.12f, 0.94f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.3f, 0.02f), new Vector3(0.9f, 0.36f, 0.7f), StructureMaterial.Timber, 2)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.53f, 0f), new Vector3(0.96f, 0.1f, 0.78f), StructureMaterial.Timber, 3)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.63f, -0.04f), new Vector3(0.78f, 0.1f, 0.62f), StructureMaterial.Dyed, 4));
			// The tall back and its posts, the crest over it.
			d.Add(StructureShapes.Box(new Vector3(0f, 1.42f, 0.36f), new Vector3(0.9f, 1.7f, 0.1f), StructureMaterial.Timber, 5)).Grain = Vector3.up;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.48f, 1.2f, 0.36f), new Vector3(0.12f, 2.28f, 0.14f), StructureMaterial.Timber, 6 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
				// The arm: a panelled side and a rest over it, a post at its front.
				d.Add(StructureShapes.Box(new Vector3(s * 0.45f, 0.78f, 0f), new Vector3(0.08f, 0.4f, 0.66f), StructureMaterial.Timber, 8 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Box(new Vector3(s * 0.46f, 1.01f, -0.04f), new Vector3(0.14f, 0.06f, 0.8f), StructureMaterial.Timber, 10 + (s > 0 ? 1 : 0))).Grain = Vector3.forward;
				d.Add(StructureShapes.Box(new Vector3(s * 0.46f, 0.78f, -0.38f), new Vector3(0.1f, 0.5f, 0.1f), StructureMaterial.Timber, 12 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Prism(new[] { new Vector2(-0.54f, 2.27f), new Vector2(0.54f, 2.27f), new Vector2(0f, 2.72f) }, 0.3f, 0.42f, StructureMaterial.Timber, 14));
			if (d.Mid)
			{
				// The dyed panel let into the back, and the finials.
				d.Add(StructureShapes.Box(new Vector3(0f, 1.45f, 0.3f), new Vector3(0.66f, 1.1f, 0.03f), StructureMaterial.Dyed, 15, StructureRole.Detail));
				int seg = d.Seg(6, 4);
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Sphere(new Vector3(s * 0.48f, 2.43f, 0.36f), new Vector3(0.09f, 0.11f, 0.09f), seg, 4, StructureMaterial.Iron, 16 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Sphere(new Vector3(s * 0.46f, 1.1f, -0.4f), new Vector3(0.07f, 0.07f, 0.07f), seg, 4, StructureMaterial.Iron, 18 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				// A roundel carved in the crest.
				d.Add(StructureShapes.Cylinder(0.1f, 0.27f, 0.31f, 8, StructureMaterial.Iron, 20, false, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(0f, 2.44f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
		}

		/// <summary>v1: the stone throne with its rune-cut back (<see cref="Throne"/>).</summary>
		private static void StoneThrone(StructureDraft d)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.11f, -0.02f), new Vector3(1.4f, 0.22f, 1.08f), d.Bevel(0.03f), StructureMaterial.Monolith, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.4f, 0.04f), new Vector3(0.98f, 0.36f, 0.78f), d.Bevel(0.025f), StructureMaterial.Monolith, 2));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.63f, -0.02f), new Vector3(0.8f, 0.1f, 0.62f), StructureMaterial.Canvas, 3));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.5f, 0.38f), new Vector3(1f, 2.56f, 0.24f), d.Bevel(0.025f), StructureMaterial.Monolith, 4));
			d.Add(StructureShapes.Prism(new[] { new Vector2(-0.5f, 2.77f), new Vector2(0.5f, 2.77f), new Vector2(0f, 3.15f) }, 0.27f, 0.49f, StructureMaterial.Monolith, 5));
			for (int s = -1; s <= 1; s += 2)
			{
				// The arm blocks and the pillars at the back's sides, under their finials.
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 0.56f, 0.55f, -0.02f), new Vector3(0.22f, 0.66f, 0.82f), d.Bevel(0.02f), StructureMaterial.Monolith, 6 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Box(new Vector3(s * 0.62f, 1.36f, 0.38f), new Vector3(0.24f, 2.28f, 0.3f), StructureMaterial.Monolith, 8 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, 2.5f), new Vector2(0f, 2.95f) }, 4, StructureMaterial.Monolith, 10 + (s > 0 ? 1 : 0), false, Mathf.PI * 0.25f)
					.Transform(Matrix4x4.Translate(new Vector3(s * 0.62f, 0f, 0.38f))));
			}
			// The runes cut down the back's face, glowing: a column of marks and a ring over them.
			for (int i = 0; i < 5; i++)
			{
				float y = 1.05f + i * 0.3f, w = i % 2 == 0 ? 0.22f : 0.14f;
				d.Add(StructureShapes.Box(new Vector3(0f, y, 0.255f), new Vector3(w, 0.06f, 0.02f), StructureMaterial.Runelight, 20 + i, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3((i % 2 == 0 ? 1f : -1f) * 0.08f, y + 0.1f, 0.255f), new Vector3(0.04f, 0.16f, 0.02f), StructureMaterial.Runelight, 25 + i, StructureRole.Detail));
				}
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, -0.01f), new Vector2(0.21f, -0.01f), new Vector2(0.21f, 0.01f), new Vector2(0.16f, 0.01f) }, d.Seg(10, 6), StructureMaterial.Runelight, 30,
				false, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.TRS(new Vector3(0f, 2.55f, 0.258f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			d.Emit(new Vector3(0f, 1.8f, 0.1f), SceneEmitterKind.Glow);
		}

		// ── Garden ────────────────────────────────────────────────────

		/// <summary>The garden's half size, the walks' half width, the plaza's half size, the hedges' height and thickness, metres.</summary>
		private const float GardenHalf = 10f, GardenWalk = 1.2f, GardenPlaza = 3.6f, GardenHedge = 0.55f, GardenHedgeThick = 0.45f;

		/// <summary>
		/// A palace court's formal garden (2026-10-19): a square of four parterre beds edged in clipped box between flagged walks in
		/// a cross, the walks meeting at a square plaza round a basin, stone benches at the plaza's corners facing it. Everything a
		/// person walks on is the ground or a flag 4 cm proud of it, and no solid stands on a walk: the beds' hedges, the basin, the
		/// benches and the topiary all stand off them. The hedges are built in the palette's turf (the kit has no foliage of its
		/// own), so a site may grow its biome's bushes and flowers in the beds: each bed's middle is marked ("bed").
		/// </summary>
		private static void PalaceGarden(StructureDraft d)
		{
			bool fountain = d.Variant == 0;
			const float half = GardenHalf, walk = GardenWalk, plaza = GardenPlaza, inner = walk + 0.4f, outer = half - 0.8f, notch = plaza + 0.2f;

			// The walks and the plaza: flags, the plaza square and four arms out to the garden's edges.
			using (d.Builds(StepRoles))
			{
				d.Add(StructureShapes.BoxBetween(new Vector3(-plaza, -0.1f, -plaza), new Vector3(plaza, 0.04f, plaza), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.BoxBetween(new Vector3(-walk, -0.1f, -half), new Vector3(walk, 0.04f, -plaza), StructureMaterial.Stone, 2));
				d.Add(StructureShapes.BoxBetween(new Vector3(-walk, -0.1f, plaza), new Vector3(walk, 0.04f, half), StructureMaterial.Stone, 3));
				d.Add(StructureShapes.BoxBetween(new Vector3(-half, -0.1f, -walk), new Vector3(-plaza, 0.04f, walk), StructureMaterial.Stone, 4));
				d.Add(StructureShapes.BoxBetween(new Vector3(plaza, -0.1f, -walk), new Vector3(half, 0.04f, walk), StructureMaterial.Stone, 5));
			}

			// The beds: each an L round the plaza's corner, edged in box, a knot inside, topiary on it.
			int key = 10;
			void Hedge(Vector3 a, Vector3 b, float height)
			{
				Vector3 run = b - a;
				Vector3 dir = run.normalized;
				// Run on past each end by half the hedge's thickness, so the corners close.
				d.Add(StructureShapes.Beam(a - dir * (GardenHedgeThick * 0.5f) + Vector3.up * (height * 0.5f - 0.05f), b + dir * (GardenHedgeThick * 0.5f) + Vector3.up * (height * 0.5f - 0.05f),
					GardenHedgeThick, height + 0.1f, StructureMaterial.Earth, key++));
			}
			int seg = d.Seg(8, 6);
			for (int q = 0; q < 4; q++)
			{
				float sx = (q & 1) == 0 ? -1f : 1f, sz = (q & 2) == 0 ? -1f : 1f;
				Vector3 P(float x, float z) => new Vector3(sx * x, 0f, sz * z);
				float mid = (notch + outer) * 0.5f;
				using (d.Builds(PalaceTurfRoles))
				{
					// The bed's ground, a little over the walks.
					d.Add(StructureShapes.BoxBetween(Vector3.Min(P(inner, notch), P(outer, outer)) - Vector3.up * 0.1f, Vector3.Max(P(inner, notch), P(outer, outer)) + Vector3.up * 0.18f,
						StructureMaterial.Earth, 100 + q * 2));
					d.Add(StructureShapes.BoxBetween(Vector3.Min(P(notch, inner), P(outer, notch)) - Vector3.up * 0.1f, Vector3.Max(P(notch, inner), P(outer, notch)) + Vector3.up * 0.18f,
						StructureMaterial.Earth, 101 + q * 2));
					// Its box edging round the L.
					Hedge(P(inner, notch), P(inner, outer), GardenHedge);
					Hedge(P(inner, outer), P(outer, outer), GardenHedge);
					Hedge(P(outer, outer), P(outer, inner), GardenHedge);
					Hedge(P(outer, inner), P(notch, inner), GardenHedge);
					Hedge(P(notch, inner), P(notch, notch), GardenHedge);
					Hedge(P(notch, notch), P(inner, notch), GardenHedge);
					if (fountain)
					{
						// The knot: a diagonal hedge from the plaza's corner out, a cone of clipped box either side of it.
						Hedge(P(notch + 0.4f, notch + 0.4f), P(outer - 0.4f, outer - 0.4f), GardenHedge * 0.8f);
						foreach ((float a, float b) in new[] { (notch + 1.4f, outer - 1.6f), (outer - 1.6f, notch + 1.4f) })
						{
							d.Add(StructureShapes.Lathe(new[] { new Vector2(0.5f, 0f), new Vector2(0.55f, 0.3f), new Vector2(0f, 2.1f) }, seg, StructureMaterial.Earth, 120 + q * 2 + (a > b ? 1 : 0))
								.Transform(Matrix4x4.Translate(P(a, b))));
						}
					}
					else
					{
						// The knot: a diamond of low box, a clipped ball on a stem in the middle.
						Vector3 c = P(mid, mid);
						const float r = 1.6f;
						Hedge(c + new Vector3(-r, 0f, 0f), c + new Vector3(0f, 0f, r), GardenHedge * 0.7f);
						Hedge(c + new Vector3(0f, 0f, r), c + new Vector3(r, 0f, 0f), GardenHedge * 0.7f);
						Hedge(c + new Vector3(r, 0f, 0f), c + new Vector3(0f, 0f, -r), GardenHedge * 0.7f);
						Hedge(c + new Vector3(0f, 0f, -r), c + new Vector3(-r, 0f, 0f), GardenHedge * 0.7f);
						d.Add(StructureShapes.Sphere(c + Vector3.up * 1.1f, new Vector3(0.6f, 0.6f, 0.6f), seg, d.Fine ? 5 : 4, StructureMaterial.Earth, 120 + q));
					}
				}
				if (!fountain)
				{
					d.Add(StructureShapes.Beam(P(mid, mid), P(mid, mid) + Vector3.up * 0.6f, 0.12f, 0.12f, StructureMaterial.Timber, 130 + q));
				}
				d.Mark("bed", P(mid, mid) + Vector3.up * 0.18f, 0f);
			}

			// Round the garden: v0 a low hedge, v1 a stone balustrade; both broken where the walks leave it.
			const float edge = half - 0.25f, gap = walk + 0.25f;
			for (int f = 0; f < 4; f++)
			{
				Quaternion turn = Quaternion.Euler(0f, f * 90f, 0f);
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 a = turn * new Vector3(s * gap, 0f, -edge), b = turn * new Vector3(s * (half - 0.25f), 0f, -edge);
					if (fountain)
					{
						using (d.Builds(PalaceTurfRoles))
						{
							Hedge(a, b, 0.75f);
						}
					}
					else
					{
						Vector3 dir = (b - a).normalized;
						Quaternion along = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
						d.Add(StructureShapes.Block((a + b) * 0.5f + Vector3.up * 0.4f, new Vector3((b - a).magnitude, 0.9f, 0.35f), along, StructureMaterial.Stone, 200 + f * 4 + (s > 0 ? 1 : 0)));
						if (d.Mid)
						{
							d.Add(StructureShapes.Block((a + b) * 0.5f + Vector3.up * 0.9f, new Vector3((b - a).magnitude + 0.1f, 0.1f, 0.45f), along, StructureMaterial.Stone, 202 + f * 4 + (s > 0 ? 1 : 0),
								StructureRole.Detail));
						}
						// A gate pier at the walk under a ball.
						Vector3 pier = a + dir * 0.05f;
						d.Add(StructureShapes.Box(pier + Vector3.up * 0.6f, new Vector3(0.5f, 1.3f, 0.5f), StructureMaterial.Stone, 220 + f * 2 + (s > 0 ? 1 : 0)));
						if (d.Mid)
						{
							d.Add(StructureShapes.Sphere(pier + Vector3.up * 1.42f, new Vector3(0.2f, 0.2f, 0.2f), d.Seg(6, 4), 4, StructureMaterial.Stone, 230 + f * 2 + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
				}
				if (!fountain)
				{
					GardenArbour(d, turn, 240 + f * 10);
				}
			}
			if (fountain)
			{
				// Urns on plinths at the garden's outer corners.
				for (int i = 0; i < 4; i++)
				{
					var c = new Vector3((i & 1) == 0 ? -(half - 0.4f) : half - 0.4f, 0f, (i & 2) == 0 ? -(half - 0.4f) : half - 0.4f);
					d.Add(StructureShapes.Box(c + Vector3.up * 0.35f, new Vector3(0.7f, 0.9f, 0.7f), StructureMaterial.Stone, 200 + i));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.18f, 0.8f), new Vector2(0.32f, 1.05f), new Vector2(0.3f, 1.35f), new Vector2(0.38f, 1.45f), new Vector2(0f, 1.45f) }, seg,
						StructureMaterial.Stone, 210 + i).Transform(Matrix4x4.Translate(c)));
				}
			}

			// The benches at the plaza's corners, each facing the basin.
			for (int i = 0; i < 4; i++)
			{
				var dir = new Vector3((i & 1) == 0 ? -1f : 1f, 0f, (i & 2) == 0 ? -1f : 1f).normalized;
				Vector3 at = dir * 4.15f;
				Quaternion face = Quaternion.LookRotation(dir, Vector3.up);
				int k = 300 + i * 4;
				d.Add(StructureShapes.Block(at + Vector3.up * 0.45f, new Vector3(1.6f, 0.1f, 0.45f), face, StructureMaterial.Stone, k));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(at + face * new Vector3(s * 0.6f, 0.2f, 0f), new Vector3(0.16f, 0.4f, 0.38f), face, StructureMaterial.Stone, k + 1 + (s > 0 ? 1 : 0)));
				}
			}

			if (fountain)
			{
				GardenFountain(d);
			}
			else
			{
				GardenStatueBasin(d);
			}
		}

		/// <summary>
		/// v1's arbour over a walk where it leaves the garden (turned by <paramref name="turn"/> from the −z side): two posts either
		/// side of the walk, a pair of beams along it and slats across them, all over head height.
		/// </summary>
		private static void GardenArbour(StructureDraft d, Quaternion turn, int key)
		{
			const float x = GardenWalk + 0.1f, z0 = -GardenHalf + 0.6f, z1 = -GardenHalf + 2.4f, top = 2.6f;
			for (int s = -1; s <= 1; s += 2)
			{
				foreach ((float z, int n) in new[] { (z0, 0), (z1, 1) })
				{
					d.Add(StructureShapes.Beam(turn * new Vector3(s * x, 0f, z), turn * new Vector3(s * x, top, z), 0.14f, 0.14f, StructureMaterial.Timber, key + n + (s > 0 ? 2 : 0)));
				}
				d.Add(StructureShapes.Beam(turn * new Vector3(s * x, top + 0.08f, z0 - 0.2f), turn * new Vector3(s * x, top + 0.08f, z1 + 0.2f), 0.12f, 0.16f, StructureMaterial.Timber, key + 4 + (s > 0 ? 1 : 0)));
			}
			int slats = d.Mid ? 4 : 2;
			for (int i = 0; i < slats; i++)
			{
				float z = Mathf.Lerp(z0, z1, (i + 0.5f) / slats);
				d.Add(StructureShapes.Beam(turn * new Vector3(-x - 0.2f, top + 0.2f, z), turn * new Vector3(x + 0.2f, top + 0.2f, z), 0.08f, 0.08f, StructureMaterial.Timber, key + 6 + i, StructureRole.Detail));
			}
		}

		/// <summary>v0's fountain: an octagonal basin of dark water (Iron, as the town's fountain), a pillar, a bowl and a finial.</summary>
		private static void GardenFountain(StructureDraft d)
		{
			const float outer = 2.3f, inner = 2f, rim = 0.5f, water = 0.38f, phase = Mathf.PI / 8f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(outer, -0.3f), new Vector2(outer, rim), new Vector2(inner, rim), new Vector2(inner, -0.3f) }, 8, StructureMaterial.Stone, 400, false, phase, true));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(inner + 0.02f, -0.3f), new Vector2(inner + 0.02f, water) }, 8, StructureMaterial.Iron, 401, false, phase));
			int seg = d.Seg(12, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.5f, water - 0.1f), new Vector2(0.5f, 0.85f) }, 8, StructureMaterial.Stone, 402, false, phase));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.22f, 0.8f), new Vector2(0.18f, 1.8f) }, seg, StructureMaterial.Monolith, 403));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, 1.7f), new Vector2(0.65f, 1.95f), new Vector2(0.76f, 2.12f), new Vector2(0f, 2.12f) }, seg, StructureMaterial.Stone, 404));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.12f, 2.08f), new Vector2(0.09f, 2.55f) }, seg, StructureMaterial.Monolith, 405));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 2.65f, 0f), new Vector3(0.14f, 0.16f, 0.14f), seg, d.Fine ? 6 : 4, StructureMaterial.Monolith, 406));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(outer + 0.08f, rim - 0.04f), new Vector2(outer + 0.08f, rim + 0.08f), new Vector2(inner - 0.06f, rim + 0.08f), new Vector2(inner - 0.06f, rim - 0.04f) },
					8, StructureMaterial.Stone, 407, false, phase, true, false, StructureRole.Detail));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.66f, 2.08f), new Vector2(0.66f, 2.13f) }, seg, StructureMaterial.Iron, 408, true, 0f, false, false, StructureRole.Detail));
			}
		}

		/// <summary>v1's basin: round, a robed figure on a pedestal in the middle holding up a ewer, water at its feet.</summary>
		private static void GardenStatueBasin(StructureDraft d)
		{
			const float outer = 2.3f, inner = 2f, rim = 0.5f, water = 0.38f;
			int seg = d.Seg(16, 8);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(outer, -0.3f), new Vector2(outer, rim), new Vector2(inner, rim), new Vector2(inner, -0.3f) }, seg, StructureMaterial.Stone, 400, true, 0f, true));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(inner + 0.02f, -0.3f), new Vector2(inner + 0.02f, water) }, seg, StructureMaterial.Iron, 401));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.6f, 0f), new Vector3(0.9f, 1.2f, 0.9f), d.Bevel(0.04f), StructureMaterial.Stone, 402));
			int fs = d.Seg(10, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.38f, 1.2f), new Vector2(0.34f, 1.8f), new Vector2(0.24f, 2.55f), new Vector2(0.12f, 2.68f), new Vector2(0f, 2.7f) }, fs, StructureMaterial.Monolith, 403));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 2.86f, 0f), new Vector3(0.14f, 0.17f, 0.15f), fs, d.Fine ? 6 : 4, StructureMaterial.Monolith, 404));
			// The arm raised before it, the ewer in its hand.
			d.Add(StructureShapes.Beam(new Vector3(0.18f, 2.45f, -0.08f), new Vector3(0.28f, 2.95f, -0.32f), 0.09f, 0.09f, StructureMaterial.Monolith, 405));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.06f, 2.92f), new Vector2(0.11f, 3.02f), new Vector2(0.05f, 3.14f), new Vector2(0f, 3.14f) }, fs, StructureMaterial.Monolith, 406)
				.Transform(Matrix4x4.Translate(new Vector3(0.28f, 0f, -0.34f))));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(outer + 0.08f, rim - 0.04f), new Vector2(outer + 0.08f, rim + 0.08f), new Vector2(inner - 0.06f, rim + 0.08f), new Vector2(inner - 0.06f, rim - 0.04f) },
					seg, StructureMaterial.Stone, 407, true, 0f, true, false, StructureRole.Detail));
			}
		}
	}
}
#endif
