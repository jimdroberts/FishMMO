#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A city's great church and what stands round a settlement's edges (2026-10-19): the cathedral, the churchyard's lychgate,
	/// the gallows on its hill, a wayside cross by the road, the village green's maypole, the shepherd's hut and the sheepfold on
	/// the pasture, and the outlier's bothy at the wood's or the orchard's edge.
	/// </summary>
	/// <remarks>
	/// Each is laid by its id (the templates' "=Id" slots), and carries only tags of its own, so no older query lays one where a
	/// church, a shrine, a fence or a house stood before. What a player walks through (the cathedral's nave, the lychgate's
	/// passage, the fold's gap, the huts' doors) stays open at every level, since the last level is the collider.
	/// </remarks>
	public static partial class StructurePieces
	{
		private static void AddOutskirts(List<StructurePiece> list)
		{
			Piece(list, "Cathedral", StructureStyle.Stone, StructureSize.Stronghold,
				"A city's cathedral, 18 m wide (22 m across its transepts) and 33 m long: a tall nave under a steep roof between arcades of pointed bays, lean-to aisles, clerestory lights, short transepts at the crossing, a great east window over the altar and a studded west door up three steps; v0 twin west towers under stone spires with a rose window between them and flying buttresses down both aisles, v1 a great lancet in a turreted west front and a square lantern tower over the crossing under a pyramid cap.",
				Cathedral, "cathedral");
			Piece(list, "Lychgate", StructureStyle.Timber, StructureSize.Medium,
				"A churchyard's roofed gateway, its passage 2.6 m wide running front (−z) to back: v0 four oak posts on low stone sill walls whose tops are the coffin rests either side, under a steep shingled gable with braced trusses and bargeboards; v1 a pointed stone arch through a short wall under a little gabled stone cap, a carved cross over it and stone benches inside.",
				Lychgate, "lychgate");
			Piece(list, "Gallows", StructureStyle.Timber, StructureSize.Medium,
				"Justice on a hill outside the town: v0 a two-post gallows and its crossbeam on a plank scaffold up a flight of steps, a noose hanging over the trapdoor and a ladder against the beam; v1 a gibbet, a tall post on a stony mound with its braced arm reaching out and an empty iron cage hanging from its chain.",
				Gallows, "gallows");
			Piece(list, "WaysideCross", StructureStyle.Stone, StructureSize.Medium,
				"A marker where roads meet: v0 a carved stone shaft on two square steps and its socket stone, a sun-disc wheel head with rays and runes cut in it; v1 a timber calvary, a tall cross with a carved robed figure under a little pent gable of boards, an offering ledge with a lantern and a bowl, and a kneeling step before it.",
				WaysideCross, "waysidecross");
			Piece(list, "Maypole", StructureStyle.Timber, StructureSize.Medium,
				"A village green's maypole, 9 m, painted in bands, on a stone-kerbed turf mound: v0 a garland hoop near the top, dyed ribbons hanging from it; v1 a crown of woven boughs tied with dyed bows and a swallow-tailed pennant above it, fewer ribbons.",
				Maypole, "maypole");
			Piece(list, "ShepherdHut", StructureStyle.Timber, StructureSize.Building,
				"A shepherd's lambing hut, one room with a stove and a bunk: v0 a plank hut 2.4×4.4 m on four iron wheels under a curved board roof, timber steps up to its end door, a stove pipe through the roof and its drawbar on the ground behind; v1 a fieldstone bothy 4.6×3.8 m under a turf roof, a chimney at its gable, a crook and a hurdle by the door.",
				ShepherdHut, "shepherd");
			Piece(list, "Sheepfold", StructureStyle.Stone, StructureSize.Medium,
				"A pen the flock is gathered into, open ground inside, its gap at the front (−z): v0 a round drystone fold 9.3 m across, a coping course, tall stones either side of its gap, a creep hole for lambs and a trough inside; v1 a square fold 8 m across of wattle hurdles lashed to stakes, the gate hurdle swung open, a hay rack and a log trough inside.",
				Sheepfold, "sheepfold");
			Piece(list, "Bothy", StructureStyle.Timber, StructureSize.Building,
				"An outlier's one-room hut for a woodcutter, a beekeeper or an orchard keeper: v0 a plank shed 4×3.2 m under a boarded lean-to roof, a door and a shuttered window, an axe in its block, a saw and a rake leaning by it and logs stacked at its side; v1 a round wattle-and-daub hut 4.4 m across under a conical thatch, bee skeps on a bench by its door.",
				Bothy, "bothy");
		}

		// ── Shared parts ──────────────────────────────────────────────

		/// <summary>
		/// A window's glazing in a hole a wall run has cut (<see cref="WallRun"/>), the wall's outer face at <paramref name="at"/>
		/// facing <paramref name="outward"/>: the glass halfway through the wall at every level (a far window shows no hole), and
		/// from the middle level a sill and a hood moulding to throw the rain off; from the first, the mullion between its lights.
		/// </summary>
		private static void OutskirtsLight(StructureDraft d, Vector3 at, Vector3 outward, float wall, float width, float height, bool trim, int key)
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

		// ── Cathedral ─────────────────────────────────────────────────

		/// <summary>The cathedral's floor over the ground (its plinth's top, three steps up).</summary>
		private const float CathedralFloor = 0.6f;

		/// <summary>
		/// A city's cathedral (2026-10-19: "a city's or the capital's great church"): a Gothic church on the cross, its west front
		/// to the square (−z) and its altar at the east end (+z). The nave is a tall middle vessel between arcades of pointed bays,
		/// lit from high up by the clerestory over the lean-to roofs of the aisles either side; the transepts cross it two thirds of
		/// the way down, the choir beyond them; the thrust of the high vault is taken out over the aisle roofs by buttresses (flying
		/// buttresses, v0). The nave is the one room (StructureRoomKind.Church: pews toward the altar); the aisles and the transepts
		/// are open floor round it. v0 is the great church of a capital, twin towers over its west door and a rose window between
		/// them; v1 a cathedral city's older minster, its one tower over the crossing.
		/// </summary>
		/// <remarks>
		/// Its roofs: the nave and choir under one steep roof that follows its palette (<see cref="OwnRoof"/>), the transepts' roof
		/// crossing it at the same pitch (lower, since narrower), the aisles' lean-tos fixed at their own low pitch under the
		/// clerestory windows (lean-tos do not follow, StructureRoofs), the spires and the tower's cap stone and shingle of their own.
		/// </remarks>
		private static void Cathedral(StructureDraft d)
		{
			bool twin = d.Variant == 0;
			const float F = CathedralFloor;
			// The plan, x across and z from the west front to the east end: the outer faces of the end walls, and inside them the
			// nave's arcade (its inner face, its centre line, the clerestory's outer face), the aisles' outer walls, the transepts.
			const float west = -15.5f, east = 17.9f, endWall = 0.9f;
			const float naveIn = 4.3f, arcade = 4.7f, arcadeT = 0.8f, clerestory = 5.1f;
			const float aisle = 8.65f, aisleT = 0.7f, aisleOut = 9f, aisleIn = 8.3f;
			const float tz0 = 1.5f, tz1 = 10.5f, tWall = 0.7f, tEnd = 10.6f, tMid = 6f;
			float aisleTop = F + 6.5f, arcadeTop = F + 7.2f, eave = F + 12.2f;
			float wIn = west + endWall, eIn = east - endWall;
			float crossW = tz1 - tz0 - 2f * tWall;
			// The arcade's bays (centre z, width): four down the nave west of the crossing, two in the choir east of it.
			var bays = new[] { (-12.4f, 2.9f), (-8.5f, 2.9f), (-4.6f, 2.9f), (-0.7f, 2.9f), (12.1f, 2.6f), (15.7f, 2.6f) };
			// The aisles' lean-tos: from the aisle wall's head up to the clerestory, under its sills.
			const float leanPitch = 0.53f;
			float leanTop = aisleTop + (aisleOut - clerestory) * leanPitch;

			// The plinth the church stands on, three steps up, and the transepts' arms of it.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (F - FoundationDepth) * 0.5f, (west + east) * 0.5f),
					new Vector3(2f * aisleOut + 0.24f, F + FoundationDepth, east - west + 0.24f), d.Bevel(0.05f), StructureMaterial.Stone, 1));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * (aisleOut + tEnd) * 0.5f, (F - FoundationDepth) * 0.5f, tMid),
						new Vector3(tEnd - aisleOut + 0.24f, F + FoundationDepth, tz1 - tz0 + 0.24f), d.Bevel(0.05f), StructureMaterial.Stone, 2 + (s > 0 ? 1 : 0)));
				}
			}

			using (d.Builds(WallRoles))
			{
				for (int s = -1; s <= 1; s += 2)
				{
					int k = s > 0 ? 1 : 0;
					// The arcade: piers between pointed bays, the crossing open full height into the transept.
					var arches = new List<WallOpening>();
					var lights = new List<WallOpening>();
					var aisleHoles = new List<WallOpening>();
					foreach ((float z, float w) in bays)
					{
						arches.Add(new WallOpening { Along = z - wIn, Width = w, Bottom = F - 0.01f, Top = F + 6f });
						lights.Add(new WallOpening { Along = z - wIn, Width = 1.3f, Bottom = F + 8.9f, Top = F + 11.4f });
						aisleHoles.Add(new WallOpening { Along = z - wIn, Width = 1.2f, Bottom = F + 2.2f, Top = F + 5.2f });
					}
					var crossing = new WallOpening { Along = tMid - wIn, Width = crossW, Bottom = F - 0.01f, Top = eave + 0.01f };
					arches.Add(crossing);
					lights.Add(crossing);
					aisleHoles.Add(new WallOpening { Along = tMid - wIn, Width = crossW, Bottom = F - 0.01f, Top = F + 6f });
					WallRun(d, new Vector3(s * arcade, 0f, wIn), new Vector3(s * arcade, 0f, eIn), arcadeT, F, arcadeTop, arches, StructureMaterial.Stone, 10 + k);
					// The clerestory over it, a light to a bay.
					WallRun(d, new Vector3(s * arcade, 0f, wIn), new Vector3(s * arcade, 0f, eIn), arcadeT, arcadeTop, eave, lights, StructureMaterial.Stone, 12 + k);
					// The aisle's outer wall, a window to a bay, open into the transept.
					WallRun(d, new Vector3(s * aisle, 0f, wIn), new Vector3(s * aisle, 0f, eIn), aisleT, F, aisleTop, aisleHoles, StructureMaterial.Stone, 14 + k);
					// The transept's side walls, carried over the aisle on an arch (the aisle walks on through), and its end wall with
					// a pair of tall lancets.
					foreach (float z in new[] { tz0 + tWall * 0.5f, tz1 - tWall * 0.5f })
					{
						var under = new List<WallOpening> { new WallOpening { Along = (aisleIn - clerestory) * 0.5f, Width = aisleIn - clerestory, Bottom = F - 0.01f, Top = F + 6f } };
						WallRun(d, new Vector3(s * clerestory, 0f, z), new Vector3(s * tEnd, 0f, z), tWall, F, eave, under, StructureMaterial.Stone, 16 + k * 2 + (z > tMid ? 1 : 0));
					}
					var pair = new List<WallOpening>
					{
						new WallOpening { Along = tMid - 1f - tz0, Width = 1.1f, Bottom = F + 2.4f, Top = F + 9.2f },
						new WallOpening { Along = tMid + 1f - tz0, Width = 1.1f, Bottom = F + 2.4f, Top = F + 9.2f },
					};
					WallRun(d, new Vector3(s * (tEnd - tWall * 0.5f), 0f, tz0), new Vector3(s * (tEnd - tWall * 0.5f), 0f, tz1), tWall, F, eave, pair, StructureMaterial.Stone, 20 + k);
					// The ends of each aisle under its lean-to, west and east.
					foreach (float z in new[] { west + endWall * 0.5f, east - endWall * 0.5f })
					{
						var end = new[] { new Vector2(s * aisleOut, aisleTop - 0.01f), new Vector2(s * clerestory, aisleTop - 0.01f), new Vector2(s * clerestory, leanTop) };
						d.Add(StructureShapes.Prism(end, z - endWall * 0.5f, z + endWall * 0.5f, StructureMaterial.Stone, 22 + k * 2 + (z > 0f ? 1 : 0)));
					}
				}
				// The west front: the door below, in v0 the rose over it between the towers, in v1 a great lancet and the aisles' windows.
				float wz = west + endWall * 0.5f;
				var westHoles = new List<WallOpening> { new WallOpening { Along = aisleOut, Width = 2.6f, Bottom = F - 0.01f, Top = F + 4.6f } };
				if (!twin)
				{
					foreach (int s in new[] { -1, 1 })
					{
						westHoles.Add(new WallOpening { Along = aisleOut + s * 6.7f, Width = 1.2f, Bottom = F + 2.2f, Top = F + 5.2f });
					}
				}
				WallRun(d, new Vector3(-aisleOut, 0f, wz), new Vector3(aisleOut, 0f, wz), endWall, F, aisleTop, westHoles, StructureMaterial.Stone, 30);
				var westLight = twin
					? new WallOpening { Along = clerestory, Width = 3.4f, Bottom = F + 7.6f, Top = F + 11f }
					: new WallOpening { Along = clerestory, Width = 2.2f, Bottom = F + 7f, Top = F + 11.4f };
				WallRun(d, new Vector3(-clerestory, 0f, wz), new Vector3(clerestory, 0f, wz), endWall, aisleTop, eave, new List<WallOpening> { westLight }, StructureMaterial.Stone, 31);
				// The east end: the aisles' windows, and the great east window over the altar running up through both courses.
				float ez = east - endWall * 0.5f;
				var eastHoles = new List<WallOpening>
				{
					new WallOpening { Along = aisleOut - 6.7f, Width = 1.2f, Bottom = F + 2.2f, Top = F + 5.2f },
					new WallOpening { Along = aisleOut, Width = 3f, Bottom = F + 3f, Top = aisleTop + 0.01f },
					new WallOpening { Along = aisleOut + 6.7f, Width = 1.2f, Bottom = F + 2.2f, Top = F + 5.2f },
				};
				WallRun(d, new Vector3(-aisleOut, 0f, ez), new Vector3(aisleOut, 0f, ez), endWall, F, aisleTop, eastHoles, StructureMaterial.Stone, 32);
				WallRun(d, new Vector3(-clerestory, 0f, ez), new Vector3(clerestory, 0f, ez), endWall, aisleTop, eave,
					new List<WallOpening> { new WallOpening { Along = clerestory, Width = 3f, Bottom = aisleTop - 0.01f, Top = F + 11f } }, StructureMaterial.Stone, 33);
			}

			// The glass: the aisles', the clerestory's (none over the crossing), the transepts' lancets, the west and east windows.
			int g = 0;
			for (int s = -1; s <= 1; s += 2)
			{
				foreach ((float z, float w) in bays)
				{
					OutskirtsLight(d, new Vector3(s * aisleOut, F + 3.7f, z), new Vector3(s, 0f, 0f), aisleT, 1.2f, 3f, true, 200 + g * 4);
					OutskirtsLight(d, new Vector3(s * clerestory, F + 10.15f, z), new Vector3(s, 0f, 0f), arcadeT, 1.3f, 2.5f, false, 260 + g * 4);
					g++;
				}
				for (int i = -1; i <= 1; i += 2)
				{
					OutskirtsLight(d, new Vector3(s * tEnd, F + 5.8f, tMid + i), new Vector3(s, 0f, 0f), tWall, 1.1f, 6.8f, true, 320 + (s > 0 ? 8 : 0) + (i > 0 ? 4 : 0));
				}
				OutskirtsLight(d, new Vector3(s * 6.7f, F + 3.7f, east), Vector3.forward, endWall, 1.2f, 3f, true, 340 + (s > 0 ? 4 : 0));
				if (!twin)
				{
					OutskirtsLight(d, new Vector3(s * 6.7f, F + 3.7f, west), Vector3.back, endWall, 1.2f, 3f, true, 350 + (s > 0 ? 4 : 0));
				}
			}
			CathedralEastWindow(d, new Vector3(0f, F + 7f, east), endWall, 360);
			if (twin)
			{
				CathedralRose(d, new Vector3(0f, F + 9.3f, west), endWall, 380);
			}
			else
			{
				OutskirtsLight(d, new Vector3(0f, F + 9.2f, west), Vector3.back, endWall, 2.2f, 4.4f, true, 380);
				CathedralLancetHead(d, new Vector3(0f, F + 11.4f, west), Vector3.back, 2.2f, 1.2f, 384);
			}

			// The aisles' lean-tos, broken by the transepts.
			for (int s = -1; s <= 1; s += 2)
			{
				var lean = new[]
				{
					new Vector2(s * (aisleOut + 0.45f), aisleTop - 0.45f * leanPitch), new Vector2(s * clerestory, leanTop),
					new Vector2(s * clerestory, leanTop + 0.16f), new Vector2(s * (aisleOut + 0.45f), aisleTop - 0.45f * leanPitch + 0.16f),
				};
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Prism(lean, west - 0.3f, tz0, StructureMaterial.Timber, 50 + (s > 0 ? 1 : 0), StructureRole.Roof)).Grain = Vector3.forward;
					d.Add(StructureShapes.Prism(lean, tz1, east + 0.3f, StructureMaterial.Timber, 52 + (s > 0 ? 1 : 0), StructureRole.Roof)).Grain = Vector3.forward;
				}
			}

			// The high roof over the nave and the choir, and the transepts' roof crossing it at the same pitch.
			const float kitRise = 5.4f, tHalf = (tz1 - tz0) * 0.5f;
			float tKitRise = kitRise * tHalf / clerestory;
			Matrix4x4 navePlace = Matrix4x4.Translate(new Vector3(0f, 0f, (west + east) * 0.5f));
			Matrix4x4 transeptPlace = Matrix4x4.TRS(new Vector3(0f, 0f, tMid), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Timber, kitRise, clerestory);
			float rise = 0f;
			if (roof.Flat)
			{
				FlatCanopy(d, 2f * clerestory, east - west, eave, 0.4f, 0.14f, StructureMaterial.Timber, 60, navePlace);
				FlatCanopy(d, tz1 - tz0, 2f * tEnd, eave, 0.4f, 0.14f, StructureMaterial.Timber, 70, transeptPlace);
			}
			else
			{
				rise = roof.Rise;
				CivicPitched(d, roof, 2f * clerestory, east - west, eave, kitRise, 0.4f, StructureMaterial.Timber, 60, navePlace);
				var crossRoof = new OwnRoofPlan { Repitched = roof.Repitched, Rise = roof.Rise * tHalf / clerestory, Soft = roof.Soft };
				CivicPitched(d, crossRoof, tz1 - tz0, 2f * tEnd, eave, tKitRise, 0.4f, StructureMaterial.Timber, 120, transeptPlace);
				using (d.Builds(WallRoles))
				{
					Gable(d, 2f * clerestory - 0.02f, eave, rise - 0.02f, west + endWall * 0.5f, endWall, StructureMaterial.Stone, 140, Matrix4x4.identity);
					Gable(d, 2f * clerestory - 0.02f, eave, rise - 0.02f, east - endWall * 0.5f, endWall, StructureMaterial.Stone, 141, Matrix4x4.identity);
					for (int s = -1; s <= 1; s += 2)
					{
						Gable(d, tz1 - tz0 - 0.02f, eave, crossRoof.Rise - 0.02f, s * (tEnd - tWall * 0.5f), tWall, StructureMaterial.Stone, 142 + (s > 0 ? 1 : 0), transeptPlace);
					}
				}
			}
			float ridge = eave + rise;

			// The door, up three steps, under a gabled portal on shafts.
			HangDoor(d, new Vector3(0f, F, wIn - 0.05f), Vector3.right, Vector3.forward, 2.6f, 4.6f, StructureDoorKind.Studded);
			Steps(d, new Vector3(0f, 0f, west - 0.12f), Vector3.back, 3.8f, F, StructureMaterial.Stone, 90);
			d.Room(new Vector3(0f, F, (wIn + eIn) * 0.5f), new Vector2(2f * naveIn, eIn - wIn), eave - F, StructureRoomKind.Church);
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Cylinder(0.14f, 0f, F + 4.6f, d.Seg(8, 6), StructureMaterial.Monolith, 95 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(s * 1.65f, 0f, west - 0.2f))));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, F + 4.75f, west - 0.2f), new Vector3(3.6f, 0.3f, 0.4f), StructureMaterial.Stone, 97, StructureRole.Detail));
				var gablet = new[] { new Vector2(-1.95f, F + 4.9f), new Vector2(1.95f, F + 4.9f), new Vector2(0f, F + 6.4f) };
				d.Add(StructureShapes.Prism(gablet, west - 0.4f, west, StructureMaterial.Stone, 98, StructureRole.Detail));
			}

			// The buttresses down the aisles between the bays: in v0 each rises over the aisle roof to a pinnacle and throws a flying
			// buttress over to the clerestory; in v1 each stops under the eaves with a sloped weathering.
			float[] buttresses = { -10.45f, -6.55f, -2.65f, 13.9f };
			for (int s = -1; s <= 1; s += 2)
			{
				for (int i = 0; i < buttresses.Length; i++)
				{
					int k = 400 + i * 8 + (s > 0 ? 4 : 0);
					float z = buttresses[i], top = twin ? F + 8.2f : F + 5.4f;
					using (d.Builds(WallRoles))
					{
						d.Add(StructureShapes.Box(new Vector3(s * (aisleOut + 0.45f), (top - FoundationDepth) * 0.5f, z), new Vector3(0.9f, top + FoundationDepth, 0.9f), StructureMaterial.Stone, k));
						if (twin)
						{
							d.Add(StructureShapes.Beam(new Vector3(s * (aisleOut + 0.2f), F + 7.6f, z), new Vector3(s * clerestory, F + 10.6f, z), 0.5f, 0.45f, StructureMaterial.Stone, k + 1));
						}
						else
						{
							var weathering = new[] { new Vector2(s * aisleOut, top), new Vector2(s * (aisleOut + 0.9f), top), new Vector2(s * aisleOut, top + 0.75f) };
							d.Add(StructureShapes.Prism(weathering, z - 0.45f, z + 0.45f, StructureMaterial.Stone, k + 1));
						}
					}
					if (twin && d.Mid)
					{
						d.Add(StructureShapes.Lathe(new[] { new Vector2(0.62f, top), new Vector2(0f, top + 1.7f) }, 4, StructureMaterial.Stone, k + 2, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
							.Transform(Matrix4x4.Translate(new Vector3(s * (aisleOut + 0.45f), 0f, z))));
					}
				}
				// The east end's buttresses either side of the great window, up to pinnacles; the corners' lower.
				using (d.Builds(WallRoles))
				{
					float high = F + 11.4f;
					d.Add(StructureShapes.Box(new Vector3(s * clerestory, (high - FoundationDepth) * 0.5f, east + 0.4f), new Vector3(0.9f, high + FoundationDepth, 0.8f), StructureMaterial.Stone, 440 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Box(new Vector3(s * (aisleOut - 0.35f), (F + 5.4f - FoundationDepth) * 0.5f, east + 0.4f), new Vector3(0.9f, F + 5.4f + FoundationDepth, 0.8f),
						StructureMaterial.Stone, 442 + (s > 0 ? 1 : 0)));
				}
				if (d.Mid)
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.6f, F + 11.4f), new Vector2(0f, F + 13.4f) }, 4, StructureMaterial.Stone, 444 + (s > 0 ? 1 : 0), false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(s * clerestory, 0f, east + 0.4f))));
				}
			}

			if (twin)
			{
				// Twin west towers over the aisles' ends, flanking the door: belfries louvred under a string course, pinnacles at their
				// corners, and stone spires.
				const float side = 4.8f, tz = west - side * 0.5f;
				float towerTop = Mathf.Max(19.6f, ridge + 1.2f);
				for (int s = -1; s <= 1; s += 2)
				{
					int k = 500 + (s > 0 ? 40 : 0);
					var c = new Vector3(s * 6.75f, 0f, tz);
					using (d.Builds(WallRoles))
					{
						d.Add(StructureShapes.ChamferBox(c + Vector3.up * ((towerTop - FoundationDepth) * 0.5f), new Vector3(side, towerTop + FoundationDepth, side), d.Bevel(0.05f), StructureMaterial.Stone, k));
					}
					// The louvres of the belfry, on the faces that show (west, outward, and east over the aisle roof), and a lancet lower down.
					var faces = new[] { Vector3.back, new Vector3(s, 0f, 0f), Vector3.forward };
					for (int f = 0; f < faces.Length; f++)
					{
						Quaternion turn = Quaternion.LookRotation(-faces[f], Vector3.up);
						d.Add(StructureShapes.Block(c + faces[f] * (side * 0.5f + 0.03f) + Vector3.up * (towerTop - 3f), new Vector3(1.1f, 2.4f, 0.06f), turn, StructureMaterial.Iron, k + 1 + f, StructureRole.Detail));
						if (f < 2)
						{
							d.Add(StructureShapes.Block(c + faces[f] * (side * 0.5f + 0.03f) + Vector3.up * (F + 9.5f), new Vector3(0.8f, 2.6f, 0.06f), turn, StructureMaterial.Glass, k + 4 + f, StructureRole.Detail));
						}
					}
					if (d.Mid)
					{
						// String courses: one at the aisles' eaves, one under the belfry.
						foreach ((float y, int n) in new[] { (aisleTop, 0), (towerTop - 4.6f, 1) })
						{
							d.Add(StructureShapes.Box(c + Vector3.up * y, new Vector3(side + 0.24f, 0.22f, side + 0.24f), StructureMaterial.Stone, k + 6 + n, StructureRole.Detail));
						}
						for (int p = 0; p < 4; p++)
						{
							float a = (p * 90f + 45f) * Mathf.Deg2Rad;
							var corner = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (side * 0.5f * 1.414f - 0.3f);
							d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f, towerTop), new Vector2(0f, towerTop + 1.8f) }, 4, StructureMaterial.Stone, k + 10 + p, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
								.Transform(Matrix4x4.Translate(c + corner)));
						}
					}
					// The spire, octagonal, and its finial.
					d.Add(StructureShapes.Lathe(new[] { new Vector2(side * 0.5f - 0.1f, towerTop), new Vector2(0f, towerTop + 4.8f) }, 8, StructureMaterial.Stone, k + 20, false, Mathf.PI / 8f, false, false, StructureRole.Roof)
						.Transform(Matrix4x4.Translate(c)));
					if (d.Mid)
					{
						d.Add(StructureShapes.Sphere(c + Vector3.up * (towerTop + 4.85f), new Vector3(0.14f, 0.14f, 0.14f), d.Seg(8, 5), 4, StructureMaterial.Iron, k + 21, StructureRole.Detail));
					}
				}
				return;
			}

			// v1: the west front's corner turrets flanking its lancet, up to spirelets.
			for (int s = -1; s <= 1; s += 2)
			{
				int k = 500 + (s > 0 ? 8 : 0);
				var c = new Vector3(s * clerestory, 0f, west - 0.1f);
				float top = F + 14.4f;
				using (d.Builds(WallRoles))
				{
					d.Add(StructureShapes.Box(c + Vector3.up * ((top - FoundationDepth) * 0.5f), new Vector3(1.4f, top + FoundationDepth, 1.4f), StructureMaterial.Stone, k));
				}
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.78f, top), new Vector2(0f, top + 2.6f) }, 8, StructureMaterial.Stone, k + 1, false, Mathf.PI / 8f, false, false, StructureRole.Roof)
					.Transform(Matrix4x4.Translate(c)));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(c + Vector3.up * (top - 0.15f), new Vector3(1.6f, 0.3f, 1.6f), StructureMaterial.Stone, k + 2, StructureRole.Detail));
				}
			}
			// The lantern tower over the crossing, astride both roofs: louvred belfry openings, pinnacles and a pyramid cap.
			const float half = 4.2f;
			float crossTop = Mathf.Max(21f, ridge + 2.2f);
			var tc = new Vector3(0f, 0f, tMid);
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.ChamferBox(tc + Vector3.up * ((eave - 0.3f + crossTop) * 0.5f), new Vector3(2f * half, crossTop - eave + 0.3f, 2f * half), d.Bevel(0.05f), StructureMaterial.Stone, 600));
			}
			for (int f = 0; f < 4; f++)
			{
				Quaternion turn = Quaternion.Euler(0f, f * 90f, 0f);
				for (int i = -1; i <= 1; i += 2)
				{
					d.Add(StructureShapes.Block(tc + turn * new Vector3(i * 1.6f, crossTop - 2.3f, -half - 0.03f), new Vector3(1f, 2.6f, 0.06f), turn, StructureMaterial.Iron, 601 + f * 2 + (i > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(tc + Vector3.up * (crossTop - 4f), new Vector3(2f * half + 0.24f, 0.22f, 2f * half + 0.24f), StructureMaterial.Stone, 610, StructureRole.Detail));
				for (int p = 0; p < 4; p++)
				{
					float a = (p * 90f + 45f) * Mathf.Deg2Rad;
					var corner = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (half * 1.414f - 0.35f);
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.45f, crossTop), new Vector2(0f, crossTop + 1.9f) }, 4, StructureMaterial.Stone, 611 + p, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(tc + corner)));
				}
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2((half - 0.3f) * 1.414f, crossTop), new Vector2(0f, crossTop + 3.6f) }, 4, StructureMaterial.Timber, 620, false, Mathf.PI * 0.25f, false, false, StructureRole.Roof)
				.Transform(Matrix4x4.Translate(tc)));
		}

		/// <summary>
		/// The great east window over the altar (in the hole the east walls cut, 3 × 8 m from <paramref name="centre"/>'s height
		/// −4 to +4): glass at every level; from the middle level two mullions dividing three lights, its pointed head and a hood;
		/// from the first, transoms across.
		/// </summary>
		private static void CathedralEastWindow(StructureDraft d, Vector3 centre, float wall, int key)
		{
			Vector3 glass = centre + Vector3.back * (wall * 0.5f);
			d.Add(StructureShapes.Box(glass, new Vector3(3f, 8f, 0.06f), StructureMaterial.Glass, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			for (int i = -1; i <= 1; i += 2)
			{
				d.Add(StructureShapes.Box(glass + new Vector3(i * 0.5f, 0f, 0f), new Vector3(0.12f, 8f, 0.16f), StructureMaterial.Stone, key + 1 + (i > 0 ? 1 : 0), StructureRole.Detail));
			}
			CathedralLancetHead(d, centre + Vector3.up * 4f, Vector3.forward, 3f, 1.5f, key + 3);
			d.Add(StructureShapes.Box(centre + new Vector3(0f, -4.07f, 0.07f), new Vector3(3.2f, 0.14f, 0.16f), StructureMaterial.Stone, key + 5, StructureRole.Detail));
			if (d.Fine)
			{
				foreach ((float y, int n) in new[] { (-1.4f, 0), (1.4f, 1) })
				{
					d.Add(StructureShapes.Box(glass + new Vector3(0f, y, 0f), new Vector3(3f, 0.1f, 0.14f), StructureMaterial.Stone, key + 6 + n, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// A pointed head in a square-headed hole: the two spandrels of stone that fill its top corners down to the springing,
		/// <paramref name="height"/> below the hole's head (<paramref name="top"/>, the middle of the wall's outer face, which faces
		/// <paramref name="outward"/> along z), set into the wall's outer part.
		/// </summary>
		private static void CathedralLancetHead(StructureDraft d, Vector3 top, Vector3 outward, float width, float height, int key)
		{
			if (!d.Mid)
			{
				return;
			}
			float half = width * 0.5f;
			for (int s = -1; s <= 1; s += 2)
			{
				// The arch's side: its springing at the jamb, curving to the apex in two straight runs.
				var spandrel = new[]
				{
					new Vector2(s * half, top.y - height), new Vector2(s * half, top.y), new Vector2(0f, top.y),
					new Vector2(s * half * 0.45f, top.y - height * 0.3f),
				};
				float z0 = top.z + outward.z * 0.02f, z1 = top.z - outward.z * 0.22f;
				d.Add(StructureShapes.Prism(spandrel, Mathf.Min(z0, z1), Mathf.Max(z0, z1), StructureMaterial.Stone, key + (s > 0 ? 1 : 0), StructureRole.Detail)
					.Transform(Matrix4x4.Translate(new Vector3(top.x, 0f, 0f))));
			}
		}

		/// <summary>
		/// The rose window over the west door (in a 3.4 m square hole centred on <paramref name="centre"/>, on the wall's outer face):
		/// the glass, the stone ring and the spandrels that square it at every level; from the middle level its spokes and boss.
		/// </summary>
		private static void CathedralRose(StructureDraft d, Vector3 centre, float wall, int key)
		{
			d.Add(StructureShapes.Box(centre + Vector3.forward * (wall * 0.5f), new Vector3(3.4f, 3.4f, 0.06f), StructureMaterial.Glass, key, StructureRole.Detail));
			// The ring: a lathe about y, stood up to face the square.
			Matrix4x4 face = Matrix4x4.TRS(centre + Vector3.forward * 0.05f, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.5f, -0.12f), new Vector2(1.72f, -0.12f), new Vector2(1.72f, 0.12f), new Vector2(1.5f, 0.12f) },
				d.Seg(16, 10), StructureMaterial.Stone, key + 1, true, 0f, true, false, StructureRole.Detail).Transform(face));
			for (int i = 0; i < 4; i++)
			{
				float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
				var corner = new[] { new Vector2(sx * 1.7f, sy * 1.7f), new Vector2(sx * 1.7f, sy * 0.55f), new Vector2(sx * 0.55f, sy * 1.7f) };
				d.Add(StructureShapes.Prism(corner, -0.1f, 0.15f, StructureMaterial.Stone, key + 2 + i, StructureRole.Detail).Transform(Matrix4x4.Translate(centre)));
			}
			if (!d.Mid)
			{
				return;
			}
			int spokes = d.Fine ? 8 : 6;
			for (int i = 0; i < spokes; i++)
			{
				float a = i * Mathf.PI * 2f / spokes;
				var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
				d.Add(StructureShapes.Beam(centre + dir * 0.3f + Vector3.forward * 0.1f, centre + dir * 1.55f + Vector3.forward * 0.1f, 0.1f, 0.12f, StructureMaterial.Stone, key + 10 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Cylinder(0.32f, -0.08f, 0.08f, d.Seg(10, 6), StructureMaterial.Stone, key + 20, false, StructureRole.Detail)
				.Transform(Matrix4x4.TRS(centre + Vector3.forward * 0.1f, Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
		}

		// ── Lychgate ──────────────────────────────────────────────────

		/// <summary>
		/// The churchyard's roofed gate (2026-10-19), where a coffin rested out of the rain while the priest came down to meet it:
		/// its passage runs front (−z) to back, 2.6 m wide and open at every level. v0 the timber lychgate, its posts on low stone
		/// sill walls whose capped tops are the coffin rests, the roof steep and gabled to the path (it follows its palette); v1 a
		/// pointed stone arch through a short length of wall under a little gabled stone cap.
		/// </summary>
		private static void Lychgate(StructureDraft d)
		{
			const float passage = 2.6f, depth = 3f;
			if (d.Variant == 1)
			{
				LychgateArch(d, passage);
				return;
			}
			const float sill = 0.45f, sillW = 0.55f, postX = 1.62f, postZ = 1.3f, eave = 2.66f;
			float inner = passage * 0.5f;
			// The sill walls, each capped with a board: the coffin's rest.
			for (int s = -1; s <= 1; s += 2)
			{
				using (d.Builds(WallRoles))
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * (inner + sillW * 0.5f), (sill - FoundationDepth) * 0.5f, 0f), new Vector3(sillW, sill + FoundationDepth, depth),
						d.Bevel(0.03f), StructureMaterial.Fieldstone, 1 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(s * (inner + sillW * 0.5f), sill + 0.03f, 0f), new Vector3(sillW + 0.06f, 0.06f, depth - 0.3f), StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0))).Grain = Vector3.forward;
				// The posts on the sills, the wall plates on the posts.
				for (int e = -1; e <= 1; e += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * postX, sill, e * postZ), new Vector3(s * postX, eave, e * postZ), 0.2f, 0.2f, StructureMaterial.Timber, 5 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Beam(new Vector3(s * postX, eave - 0.08f, -depth * 0.5f - 0.1f), new Vector3(s * postX, eave - 0.08f, depth * 0.5f + 0.1f), 0.2f, 0.16f, StructureMaterial.Timber, 10 + (s > 0 ? 1 : 0)));
			}
			// The tie beams across the ends, over the walker's head.
			for (int e = -1; e <= 1; e += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(-postX - 0.12f, eave - 0.08f, e * postZ), new Vector3(postX + 0.12f, eave - 0.08f, e * postZ), 0.18f, 0.16f, StructureMaterial.Timber, 12 + (e > 0 ? 1 : 0)));
			}
			// Flagstones down the passage, barely proud of the ground.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.02f, 0f), new Vector3(passage - 0.05f, 0.04f, depth), StructureMaterial.Fieldstone, 14, StructureRole.Detail));
			// The roof, steep and gabled to the path, at its palette's pitch (StructureRoofs).
			const float span = 2f * postX + 0.2f, length = depth, kitRise = 1.75f, overhang = 0.42f;
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Timber, kitRise, span * 0.5f);
			if (roof.Flat)
			{
				FlatCanopy(d, span, length, eave, overhang, 0.12f, StructureMaterial.Timber, 20, Matrix4x4.identity);
				return;
			}
			CivicPitched(d, roof, span, length, eave, kitRise, overhang, StructureMaterial.Timber, 20, Matrix4x4.identity);
			if (!d.Mid)
			{
				return;
			}
			// The trusses in the open gables: a king post from the tie beam to the ridge and two raking struts; knee braces under
			// the ties and the plates.
			for (int e = -1; e <= 1; e += 2)
			{
				int k = 70 + (e > 0 ? 10 : 0);
				float z = e * postZ, y = eave;
				d.Add(StructureShapes.Beam(new Vector3(0f, y, z), new Vector3(0f, y + roof.Rise - 0.1f, z), 0.14f, 0.14f, StructureMaterial.Timber, k, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0f, y + 0.1f, z), new Vector3(s * span * 0.3f, y + roof.Rise * 0.4f, z), 0.1f, 0.1f, StructureMaterial.Timber, k + 1 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(s * postX, y - 0.62f, z), new Vector3(s * (postX - 0.55f), y - 0.1f, z), 0.1f, 0.1f, StructureMaterial.Timber, k + 3 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Beam(new Vector3(s * postX, y - 0.62f, z), new Vector3(s * postX, y - 0.1f, z - e * 0.5f), 0.1f, 0.1f, StructureMaterial.Timber, k + 5 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// The stone lychgate: a pointed arch through a short wall (the passage along z through it), a gabled stone cap over the
		/// wall's head with a cross on its front, stone benches against the wall inside.
		/// </summary>
		private static void LychgateArch(StructureDraft d, float passage)
		{
			const float wall = 0.9f, half = 2.3f, head = 2.65f, top = 3.3f;
			using (d.Builds(WallRoles))
			{
				var arch = new List<WallOpening> { new WallOpening { Along = half, Width = passage, Bottom = -FoundationDepth - 0.01f, Top = head } };
				WallRun(d, new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), wall, -FoundationDepth, top, arch, StructureMaterial.Stone, 1);
			}
			// The arch's pointed head, at every level (its springing well over a walker's head).
			for (int s = -1; s <= 1; s += 2)
			{
				var spandrel = new[]
				{
					new Vector2(s * passage * 0.5f, head - 0.6f), new Vector2(s * passage * 0.5f, head + 0.01f), new Vector2(0f, head + 0.01f), new Vector2(s * passage * 0.22f, head - 0.16f),
				};
				d.Add(StructureShapes.Prism(spandrel, -wall * 0.5f, wall * 0.5f, StructureMaterial.Stone, 5 + (s > 0 ? 1 : 0)));
			}
			// The cap: stone slabs on a low gable along the wall, its ends closed.
			Matrix4x4 place = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			const float span = wall + 0.3f, rise = 0.6f;
			GableRoof(d, span, 2f * half, top, rise, 0.14f, 0.14f, StructureMaterial.Stone, 10, place);
			Gable(d, span - 0.02f, top, rise - 0.02f, -half + 0.1f, 0.2f, StructureMaterial.Stone, 12, place);
			Gable(d, span - 0.02f, top, rise - 0.02f, half - 0.1f, 0.2f, StructureMaterial.Stone, 13, place);
			// Benches inside, either side of the arch.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * (passage * 0.5f + 0.5f), -0.05f, wall * 0.5f + 0.25f), new Vector3(0.9f, 0.9f, 0.5f), d.Bevel(0.03f), StructureMaterial.Stone, 14 + (s > 0 ? 1 : 0)));
			}
			if (!d.Mid)
			{
				return;
			}
			// A chamfered hood over the arch on the front, and a stone cross on the cap's front end.
			d.Add(StructureShapes.Box(new Vector3(0f, head + 0.18f, -wall * 0.5f - 0.06f), new Vector3(passage + 0.5f, 0.16f, 0.14f), StructureMaterial.Stone, 20, StructureRole.Detail));
			var foot = new Vector3(0f, top + rise - 0.05f, 0f);
			d.Add(StructureShapes.Box(foot + Vector3.up * 0.45f, new Vector3(0.14f, 0.9f, 0.14f), StructureMaterial.Monolith, 21, StructureRole.Detail));
			d.Add(StructureShapes.Box(foot + Vector3.up * 0.62f, new Vector3(0.55f, 0.13f, 0.13f), StructureMaterial.Monolith, 22, StructureRole.Detail));
			if (d.Fine)
			{
				// Jamb shafts in the arch's corners, and the carved niche over it.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Cylinder(0.08f, 0f, head - 0.6f, 6, StructureMaterial.Monolith, 23 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(s * (passage * 0.5f + 0.02f), 0f, -wall * 0.5f + 0.02f))));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, head + 0.48f, -wall * 0.5f - 0.005f), new Vector3(0.36f, 0.38f, 0.02f), StructureMaterial.Ash, 25, StructureRole.Detail));
			}
		}

		// ── Gallows ───────────────────────────────────────────────────

		/// <summary>
		/// Justice outside the town (2026-10-19), on a hill where the road could see it: v0 a gallows on its scaffold, the trap in the
		/// planks under the noose, a ladder for the hangman against the beam, a flight of steps up the front; v1 a gibbet, where the
		/// hanged were left in an iron cage as a warning (empty here: no bodies).
		/// </summary>
		private static void Gallows(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				Gibbet(d);
				return;
			}
			const float deck = 1.4f, halfX = 1.8f, front = -1.2f, back = 1.8f, beamZ = 0.6f, beamY = 4.8f;
			// The scaffold: planks on posts, braced across the back and the sides.
			d.Add(StructureShapes.Box(new Vector3(0f, deck - 0.08f, (front + back) * 0.5f), new Vector3(2f * halfX, 0.16f, back - front), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					float z = sz < 0 ? front + 0.15f : back - 0.15f;
					d.Add(StructureShapes.Beam(new Vector3(sx * (halfX - 0.15f), -0.3f, z), new Vector3(sx * (halfX - 0.15f), deck - 0.16f, z), 0.18f, 0.18f, StructureMaterial.Timber, 2 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			// The two uprights through the deck, the crossbeam over them.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 1.2f, -0.3f, beamZ), new Vector3(s * 1.2f, beamY + 0.12f, beamZ), 0.24f, 0.24f, StructureMaterial.Timber, 6 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-1.55f, beamY, beamZ), new Vector3(1.55f, beamY, beamZ), 0.24f, 0.26f, StructureMaterial.Timber, 8));
			Steps(d, new Vector3(0f, 0f, front), Vector3.back, 1.2f, deck, StructureMaterial.Timber, 10);
			// The rope from the beam, and its noose over the trap.
			d.Add(StructureShapes.Rod(new Vector3(0f, beamY - 0.12f, beamZ), new Vector3(0f, 3.42f, beamZ), 0.022f, d.Seg(6, 4), StructureMaterial.Canvas, 12, StructureRole.Detail));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.11f, -0.025f), new Vector2(0.15f, -0.025f), new Vector2(0.15f, 0.025f), new Vector2(0.11f, 0.025f) }, d.Seg(10, 6),
				StructureMaterial.Canvas, 13, true, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.TRS(new Vector3(0f, 3.27f, beamZ), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			// The trap: its leaf in the planks, the dark of its edge round it.
			d.Add(StructureShapes.Box(new Vector3(0f, deck + 0.005f, beamZ), new Vector3(1.1f, 0.012f, 1.1f), StructureMaterial.Ash, 14, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, deck + 0.012f, beamZ), new Vector3(1f, 0.016f, 1f), StructureMaterial.Timber, 15, StructureRole.Detail)).Grain = Vector3.forward;
			// Rails round the back and the sides and across the front either side of the steps, at every level (the last is the
			// collider: no one walks off the scaffold's edge).
			float edgeX = halfX - 0.15f, rail = deck + 0.9f, backZ = back - 0.15f, frontZ = front + 0.15f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * edgeX, deck - 0.16f, backZ), new Vector3(s * edgeX, rail + 0.05f, backZ), 0.1f, 0.1f, StructureMaterial.Timber, 22 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * edgeX, rail, backZ), new Vector3(s * edgeX, rail, frontZ), 0.08f, 0.1f, StructureMaterial.Timber, 24 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * edgeX, deck - 0.16f, frontZ), new Vector3(s * edgeX, rail + 0.05f, frontZ), 0.1f, 0.1f, StructureMaterial.Timber, 26 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * edgeX, rail, frontZ), new Vector3(s * 0.66f, rail, frontZ), 0.08f, 0.1f, StructureMaterial.Timber, 36 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * 0.66f, deck - 0.16f, frontZ), new Vector3(s * 0.66f, rail + 0.05f, frontZ), 0.1f, 0.1f, StructureMaterial.Timber, 38 + (s > 0 ? 1 : 0)));
				// The handrails down the steps, on a post at their foot.
				float footZ = front - 1.65f;
				d.Add(StructureShapes.Beam(new Vector3(s * 0.66f, -0.3f, footZ), new Vector3(s * 0.66f, 1.2f, footZ), 0.09f, 0.09f, StructureMaterial.Timber, 50 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * 0.66f, rail, frontZ), new Vector3(s * 0.66f, 1.15f, footZ), 0.07f, 0.08f, StructureMaterial.Timber, 52 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-edgeX, rail, backZ), new Vector3(edgeX, rail, backZ), 0.08f, 0.1f, StructureMaterial.Timber, 30));
			if (!d.Mid)
			{
				return;
			}
			// Knee braces under the crossbeam, cross braces under the scaffold's sides, the noose's knot, the trap's bolt lever.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 1.2f, beamY - 0.75f, beamZ), new Vector3(s * 0.55f, beamY - 0.1f, beamZ), 0.12f, 0.12f, StructureMaterial.Timber, 20 + (s > 0 ? 1 : 0), StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(s * edgeX, 0.05f, frontZ), new Vector3(s * edgeX, deck - 0.25f, backZ), 0.08f, 0.12f, StructureMaterial.Timber, 28 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Cylinder(0.05f, 3.42f, 3.62f, 6, StructureMaterial.Canvas, 31, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, beamZ))));
			d.Add(StructureShapes.Beam(new Vector3(-1f, deck, beamZ + 0.55f), new Vector3(-0.9f, deck + 0.9f, beamZ + 0.55f), 0.06f, 0.06f, StructureMaterial.Iron, 32, StructureRole.Detail));
			// The hangman's ladder against the beam.
			var foot = new Vector3(0.8f, deck, -0.25f);
			var head = new Vector3(0.8f, beamY - 0.1f, beamZ - 0.15f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(foot + new Vector3(s * 0.2f, 0f, 0f), head + new Vector3(s * 0.2f, 0f, 0f), 0.05f, 0.07f, StructureMaterial.Timber, 34 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			if (d.Fine)
			{
				for (int r = 1; r <= 9; r++)
				{
					Vector3 at = Vector3.Lerp(foot, head, r / 10f);
					d.Add(StructureShapes.Beam(at + Vector3.left * 0.2f, at + Vector3.right * 0.2f, 0.035f, 0.035f, StructureMaterial.Timber, 40 + r, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// The gibbet: a tall post on a low stony mound, its arm reaching out over the front (−z) on a raking brace, the iron cage
		/// on its chain from the arm's end — hoops and straps round an empty frame, a ring at its crown.
		/// </summary>
		private static void Gibbet(StructureDraft d)
		{
			int seg = d.Seg(12, 6);
			using (d.Builds(BiomeTurfRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(2f, -0.4f), new Vector2(1.6f, 0.15f), new Vector2(0.6f, 0.4f), new Vector2(0f, 0.42f) }, seg, StructureMaterial.Earth, 1));
			}
			const float top = 6f, armY = 5.7f, reach = 1.9f;
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.5f, 0f), new Vector3(0f, top, 0f), 0.28f, 0.28f, StructureMaterial.Timber, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, armY, 0.14f), new Vector3(0f, armY, -reach), 0.22f, 0.24f, StructureMaterial.Timber, 3));
			d.Add(StructureShapes.Beam(new Vector3(0f, armY - 1.2f, -0.12f), new Vector3(0f, armY - 0.1f, -1.1f), 0.14f, 0.14f, StructureMaterial.Timber, 4));
			// The chain from the arm's end, and the cage on it.
			float hang = armY - 0.12f, cageTop = 4.65f, cageFoot = 3.45f;
			var c = new Vector3(0f, 0f, -reach + 0.15f);
			d.Add(StructureShapes.Rod(c + Vector3.up * hang, c + Vector3.up * (cageTop + 0.2f), 0.025f, 4, StructureMaterial.Iron, 5, StructureRole.Detail));
			// Its crown: a shallow iron dome and the ring the chain runs to.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.36f, cageTop), new Vector2(0.18f, cageTop + 0.14f), new Vector2(0f, cageTop + 0.18f) }, seg, StructureMaterial.Iron, 6, true, 0f, false, false, StructureRole.Detail)
				.Transform(Matrix4x4.Translate(c)));
			// The straps down its sides, shaped to the body they were made for, and the plate it stands on.
			int bars = d.Fine ? 8 : d.Mid ? 6 : 4;
			for (int i = 0; i < bars; i++)
			{
				float a = (i + 0.5f) * Mathf.PI * 2f / bars;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 p0 = c + dir * 0.3f + Vector3.up * cageFoot, p1 = c + dir * 0.4f + Vector3.up * (cageFoot + 0.7f), p2 = c + dir * 0.36f + Vector3.up * cageTop;
				d.Add(StructureShapes.Beam(p0, p1, 0.035f, 0.02f, StructureMaterial.Iron, 10 + i * 2, StructureRole.Detail));
				d.Add(StructureShapes.Beam(p1, p2, 0.035f, 0.02f, StructureMaterial.Iron, 11 + i * 2, StructureRole.Detail));
			}
			d.Add(StructureShapes.Cylinder(0.31f, cageFoot - 0.03f, cageFoot + 0.02f, seg, StructureMaterial.Iron, 30, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c)));
			if (d.Mid)
			{
				// Hoops round it: at the hip and the chest.
				foreach ((float y, float r, int k) in new[] { (cageFoot + 0.7f, 0.41f, 31), (cageTop - 0.3f, 0.39f, 32) })
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r - 0.02f, y - 0.025f), new Vector2(r + 0.01f, y - 0.025f), new Vector2(r + 0.01f, y + 0.025f), new Vector2(r - 0.02f, y + 0.025f) },
						seg, StructureMaterial.Iron, k, true, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.Translate(c)));
				}
				// Stones heaped round the post's foot to wedge it.
				for (int i = 0; i < 4; i++)
				{
					float a = i * 90f + 20f;
					Vector3 at = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.25f, 0.45f);
					d.Add(RoughStone(d, at, new Vector3(0.42f, 0.32f, 0.36f), a, 40 + i, 2));
				}
			}
		}

		/// <summary>A mound's turf: the palette's Turf where it has one (StructureDraft.Builds), else the kit's packed earth.</summary>
		private static readonly BiomeStructureRole[] BiomeTurfRoles = { BiomeStructureRole.Turf };

		// ── Wayside cross ─────────────────────────────────────────────

		/// <summary>
		/// A marker where roads meet (2026-10-19), as different from the kit's wayside shrine (a niche on a post) as a cross is from a
		/// box: v0 a carved stone cross of the old kind, its shaft on two square steps, its head a ringed sun-disc with rays and runes
		/// cut in it; v1 a timber calvary, a tall cross carrying a carved robed figure under a pent gable of boards (it follows its
		/// palette), an offering ledge and a kneeling step before it.
		/// </summary>
		private static void WaysideCross(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				Calvary(d);
				return;
			}
			// Two square steps and the socket stone.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.1f, 0f), new Vector3(1.9f, 0.6f, 1.9f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.33f, 0f), new Vector3(1.3f, 0.26f, 1.3f), d.Bevel(0.03f), StructureMaterial.Stone, 2));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.65f, 0f), new Vector3(0.72f, 0.4f, 0.72f), d.Bevel(0.04f), StructureMaterial.Stone, 3));
			// The shaft, octagonal and tapering.
			const float shaft0 = 0.84f, shaft1 = 3.05f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.18f, shaft0), new Vector2(0.14f, shaft1 + 0.05f) }, 8, StructureMaterial.Monolith, 4, false, Mathf.PI / 8f));
			// The head: a ringed cross, a sun-disc at its heart, faced to the road (−z).
			var heart = new Vector3(0f, shaft1 + 0.42f, 0f);
			Matrix4x4 face = Matrix4x4.TRS(heart, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.34f, -0.07f), new Vector2(0.46f, -0.07f), new Vector2(0.46f, 0.07f), new Vector2(0.34f, 0.07f) }, d.Seg(16, 8),
				StructureMaterial.Monolith, 5, true, 0f, true).Transform(face));
			d.Add(StructureShapes.Box(heart, new Vector3(0.22f, 1.1f, 0.16f), StructureMaterial.Monolith, 6));
			d.Add(StructureShapes.Box(heart, new Vector3(1.1f, 0.22f, 0.16f), StructureMaterial.Monolith, 7));
			d.Add(StructureShapes.Cylinder(0.2f, -0.11f, 0.11f, d.Seg(12, 6), StructureMaterial.Monolith, 8).Transform(face));
			if (!d.Mid)
			{
				return;
			}
			// The sun's rays cut into the ring between the arms, and a band where the shaft meets the socket.
			for (int i = 0; i < 4; i++)
			{
				float a = (i * 90f + 45f) * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
				d.Add(StructureShapes.Beam(heart + dir * 0.22f + Vector3.back * 0.075f, heart + dir * 0.34f + Vector3.back * 0.075f, 0.05f, 0.012f, StructureMaterial.Ash, 10 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.21f, shaft0 + 0.02f), new Vector2(0.21f, shaft0 + 0.16f) }, 8, StructureMaterial.Monolith, 14, false, Mathf.PI / 8f, false, false, StructureRole.Detail));
			if (d.Fine)
			{
				// Runes down the shaft's front and back faces, and a ring of them on the disc.
				for (int i = 0; i < 6; i++)
				{
					float y = shaft0 + 0.35f + i * 0.3f, r = Mathf.Lerp(0.18f, 0.14f, (y - shaft0) / (shaft1 - shaft0)) * Mathf.Cos(Mathf.PI / 8f);
					float w = d.Range(20 + i, 1, 0.05f, 0.1f), h = d.Range(20 + i, 2, 0.1f, 0.18f);
					d.Add(StructureShapes.Box(new Vector3(0f, y, -r - 0.004f), new Vector3(w, h, 0.012f), StructureMaterial.Ash, 20 + i, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0f, y + 0.1f, r + 0.004f), new Vector3(h * 0.6f, w, 0.012f), StructureMaterial.Ash, 30 + i, StructureRole.Detail));
				}
			}
		}

		/// <summary>The timber calvary: its cross, the carved figure on it, the pent gable over it, the ledge and the kneeler.</summary>
		private static void Calvary(StructureDraft d)
		{
			d.Add(RoughStone(d, new Vector3(0f, -0.3f, 0f), new Vector3(0.75f, 0.5f, 0.75f), 20f, 1, 2));
			const float top = 3.05f, armY = 2.4f;
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.4f, 0f), new Vector3(0f, top, 0f), 0.2f, 0.2f, StructureMaterial.Timber, 2));
			d.Add(StructureShapes.Beam(new Vector3(-0.78f, armY, 0f), new Vector3(0.78f, armY, 0f), 0.16f, 0.16f, StructureMaterial.Timber, 3));
			// The figure on its front: a robe, a head, arms out along the bar. Painted, as the wayside saints were.
			const float fz = -0.16f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.17f, 1.3f), new Vector2(0.13f, 1.9f), new Vector2(0.12f, 2.25f), new Vector2(0f, 2.32f) }, d.Seg(8, 5), StructureMaterial.Dyed, 4)
				.Transform(Matrix4x4.Translate(new Vector3(0f, 0f, fz))));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 2.48f, fz), new Vector3(0.1f, 0.12f, 0.1f), d.Seg(8, 5), 4, StructureMaterial.Timber, 5));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.1f, 2.22f, fz), new Vector3(s * 0.62f, armY + 0.05f, fz + 0.02f), 0.08f, 0.08f, StructureMaterial.Dyed, 6 + (s > 0 ? 1 : 0)));
			}
			// The pent gable over the head of the cross, its ridge along the bar (at its palette's pitch, StructureRoofs).
			const float span = 0.7f, length = 1.9f, eave = 2.86f, kitRise = 0.36f;
			Matrix4x4 place = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
			OwnRoofPlan roof = OwnGable(d, span, length, eave, kitRise, 0.12f, 0.05f, StructureMaterial.Timber, 10, place);
			if (!roof.Flat)
			{
				Gable(d, span - 0.02f, eave, roof.Rise - 0.04f, -length * 0.5f + 0.02f, 0.03f, StructureMaterial.Timber, 12, place);
				Gable(d, span - 0.02f, eave, roof.Rise - 0.04f, length * 0.5f - 0.02f, 0.03f, StructureMaterial.Timber, 13, place);
			}
			// The ledge for offerings on its bracket, a lantern lit on it at night.
			d.Add(StructureShapes.Box(new Vector3(0f, 1.05f, -0.3f), new Vector3(0.7f, 0.05f, 0.36f), StructureMaterial.Timber, 14)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(-0.18f, 1.17f, -0.32f), new Vector3(0.14f, 0.2f, 0.14f), StructureMaterial.Glass, 15, StructureRole.Detail));
			d.Emit(new Vector3(-0.18f, 1.17f, -0.32f), SceneEmitterKind.Lamp);
			// The kneeling step.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.08f, -0.8f), new Vector3(0.8f, 0.2f, 0.34f), StructureMaterial.Timber, 16)).Grain = Vector3.right;
			if (!d.Mid)
			{
				return;
			}
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.75f, -0.1f), new Vector3(0f, 1.02f, -0.4f), 0.06f, 0.06f, StructureMaterial.Timber, 17, StructureRole.Detail));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.04f, 1.075f), new Vector2(0.09f, 1.12f), new Vector2(0.1f, 1.16f) }, d.Seg(8, 5), StructureMaterial.Fieldstone, 18, true, 0f, false, false, StructureRole.Detail)
				.Transform(Matrix4x4.Translate(new Vector3(0.16f, 0f, -0.3f))));
			if (d.Fine)
			{
				// The lantern's iron cap, and the halo over the figure's head.
				d.Add(StructureShapes.Box(new Vector3(-0.18f, 1.29f, -0.32f), new Vector3(0.17f, 0.04f, 0.17f), StructureMaterial.Iron, 19, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.17f, -0.01f, 0.01f, 10, StructureMaterial.Iron, 20, false, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(0f, 2.5f, fz + 0.06f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
		}

		// ── Maypole ───────────────────────────────────────────────────

		/// <summary>
		/// The village green's maypole (2026-10-19), raised each spring and left standing: a tall pole painted in bands on a mound
		/// of turf kept by a ring of stones, the garland at its head and the ribbons hanging from it for the dance.
		/// </summary>
		private static void Maypole(StructureDraft d)
		{
			bool crowned = d.Variant == 1;
			int seg = d.Seg(16, 8);
			const float top = 9f, hoopY = 7.6f;
			// The mound and its kerb.
			using (d.Builds(BiomeTurfRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(1.4f, -0.3f), new Vector2(1.3f, 0.18f), new Vector2(0.5f, 0.3f), new Vector2(0f, 0.32f) }, seg, StructureMaterial.Earth, 1));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(1.38f, -0.25f), new Vector2(1.62f, -0.25f), new Vector2(1.62f, 0.24f), new Vector2(1.38f, 0.24f) }, d.Seg(16, 8),
				StructureMaterial.Fieldstone, 2, false, 0f, true));
			// The pole, tapering, and its painted bands.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, -0.5f), new Vector2(0.09f, top), new Vector2(0f, top + 0.08f) }, d.Seg(10, 6), StructureMaterial.Timber, 3, true, 0f, false, true));
			if (d.Mid)
			{
				int bands = d.Fine ? 7 : 4;
				for (int i = 0; i < bands; i++)
				{
					float y0 = 0.8f + i * (6.4f / bands), y1 = y0 + 0.35f;
					float r0 = Mathf.Lerp(0.14f, 0.09f, (y0 + 0.5f) / (top + 0.5f)) + 0.012f, r1 = Mathf.Lerp(0.14f, 0.09f, (y1 + 0.5f) / (top + 0.5f)) + 0.012f;
					d.Add(StructureShapes.Lathe(new[] { new Vector2(r0, y0), new Vector2(r1, y1) }, d.Seg(10, 6), StructureMaterial.Dyed, 10 + i, true, 0f, false, false, StructureRole.Detail));
				}
			}
			// The garland hoop near the head, on four stays, and the ribbons hanging from it.
			const float hoopR = 0.8f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(hoopR - 0.05f, hoopY - 0.05f), new Vector2(hoopR + 0.05f, hoopY - 0.05f), new Vector2(hoopR + 0.05f, hoopY + 0.05f), new Vector2(hoopR - 0.05f, hoopY + 0.05f) },
				seg, crowned ? StructureMaterial.Timber : StructureMaterial.Thatch, 20, true, 0f, true));
			for (int i = 0; i < 4; i++)
			{
				float a = i * Mathf.PI * 0.5f;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				d.Add(StructureShapes.Beam(dir * 0.1f + Vector3.up * (hoopY + 0.55f), dir * (hoopR - 0.02f) + Vector3.up * hoopY, 0.03f, 0.03f, StructureMaterial.Timber, 21 + i, StructureRole.Detail));
			}
			int ribbons = crowned ? (d.Mid ? 6 : 4) : (d.Fine ? 10 : d.Mid ? 8 : 4);
			for (int i = 0; i < ribbons; i++)
			{
				float a = (i + 0.5f) * Mathf.PI * 2f / ribbons;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				float sway = d.Range(30 + i, 1, -0.12f, 0.12f), drop = d.Range(30 + i, 2, 1.1f, 1.8f);
				Vector3 from = dir * hoopR + Vector3.up * (hoopY - 0.04f);
				Vector3 to = dir * (hoopR + 0.28f) + Vector3.Cross(Vector3.up, dir) * sway + Vector3.up * drop;
				d.Add(StructureShapes.Beam(from, to, 0.07f, 0.012f, StructureMaterial.Dyed, 30 + i, StructureRole.Detail));
			}
			if (!crowned)
			{
				if (d.Fine)
				{
					// Bows tied round the hoop, and a gilt knob at the head.
					for (int i = 0; i < 6; i++)
					{
						float a = i * Mathf.PI / 3f;
						d.Add(StructureShapes.Box(new Vector3(Mathf.Cos(a) * hoopR, hoopY + 0.06f, Mathf.Sin(a) * hoopR), new Vector3(0.16f, 0.1f, 0.05f), StructureMaterial.Dyed, 50 + i, StructureRole.Detail));
					}
				}
				d.Add(StructureShapes.Sphere(Vector3.up * (top + 0.12f), new Vector3(0.13f, 0.13f, 0.13f), d.Seg(8, 5), 4, StructureMaterial.Iron, 56, StructureRole.Detail));
				return;
			}
			// v1: the crown — two hoops crossed over the head on the garland — bows tied to it, and the pennant's staff and its swallowtail.
			for (int i = 0; i < 2; i++)
			{
				Matrix4x4 m = Matrix4x4.TRS(Vector3.up * hoopY, Quaternion.Euler(0f, i * 90f, 90f), Vector3.one);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(hoopR - 0.04f, -0.035f), new Vector2(hoopR + 0.04f, -0.035f), new Vector2(hoopR + 0.04f, 0.035f), new Vector2(hoopR - 0.04f, 0.035f) },
					d.Seg(12, 6), StructureMaterial.Timber, 60 + i, true, 0f, true, false, StructureRole.Detail).Transform(m));
			}
			const float staff = top + 1.4f;
			d.Add(StructureShapes.Beam(Vector3.up * top, Vector3.up * staff, 0.04f, 0.04f, StructureMaterial.Timber, 62));
			var pennant = new[] { new Vector2(0.02f, staff - 0.05f), new Vector2(1.1f, staff - 0.2f), new Vector2(0.8f, staff - 0.3f), new Vector2(1.1f, staff - 0.42f), new Vector2(0.02f, staff - 0.55f) };
			d.Add(StructureShapes.Prism(pennant, -0.01f, 0.01f, StructureMaterial.Dyed, 63, StructureRole.Detail));
			if (d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					float a = (i * 90f + 45f) * Mathf.Deg2Rad;
					d.Add(StructureShapes.Box(new Vector3(Mathf.Cos(a) * hoopR, hoopY + 0.05f, Mathf.Sin(a) * hoopR), new Vector3(0.18f, 0.12f, 0.06f), StructureMaterial.Dyed, 64 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Sphere(Vector3.up * (staff + 0.06f), new Vector3(0.07f, 0.07f, 0.07f), d.Seg(8, 5), 4, StructureMaterial.Iron, 68, StructureRole.Detail));
			}
		}

		// ── Shepherd's hut ────────────────────────────────────────────

		/// <summary>
		/// Where the shepherd slept out on the hill at lambing (2026-10-19), with a stove for the lambs that came weak: v0 the hut
		/// on wheels, drawn from pasture to pasture, its curved board roof on bent hoops (a barrel, not a pitch: it does not follow a
		/// palette's roof) and its end door up timber steps; v1 the stone bothy of the high grazing, under turf. One room
		/// (StructureRoomKind.Dwelling: a bunk, the stove, a chest).
		/// </summary>
		private static void ShepherdHut(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				ShepherdBothy(d);
				return;
			}
			const float width = 2.4f, depth = 4.4f, floor = 0.83f, wall = 0.1f, wallH = 1.95f, door = 0.85f, doorH = 1.8f;
			float halfW = width * 0.5f, halfD = depth * 0.5f, top = floor + wallH;
			// The chassis: two runners, two axles, four iron wheels, the drawbar laid down behind.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.75f, floor - 0.2f, -halfD - 0.05f), new Vector3(s * 0.75f, floor - 0.2f, halfD + 0.05f), 0.16f, 0.2f, StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
			}
			int seg = d.Seg(14, 8);
			for (int e = -1; e <= 1; e += 2)
			{
				float z = e * 1.45f;
				d.Add(StructureShapes.Beam(new Vector3(-1.38f, 0.42f, z), new Vector3(1.38f, 0.42f, z), 0.1f, 0.1f, StructureMaterial.Iron, 3 + (e > 0 ? 1 : 0)));
				for (int s = -1; s <= 1; s += 2)
				{
					// A wheel: a cylinder about y laid on its side, its axis along x.
					Matrix4x4 wheel = Matrix4x4.TRS(new Vector3(s * 1.32f, 0.42f, z), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
					d.Add(StructureShapes.Cylinder(0.42f, -0.05f, 0.05f, seg, StructureMaterial.Iron, 5 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0)).Transform(wheel));
					if (d.Fine)
					{
						d.Add(StructureShapes.Cylinder(0.1f, -0.09f, 0.09f, 8, StructureMaterial.Iron, 9 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0), false, StructureRole.Detail).Transform(wheel));
					}
				}
				// Struts from the axle up to the runners.
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(s * 0.75f, 0.42f, z), new Vector3(s * 0.75f, floor - 0.3f, z), 0.1f, 0.1f, StructureMaterial.Iron, 13 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.5f, floor - 0.25f, halfD), new Vector3(s * 0.08f, 0.06f, halfD + 1.3f), 0.08f, 0.1f, StructureMaterial.Timber, 17 + (s > 0 ? 1 : 0)));
			}
			// The floor, and the walls of planks: the door in the front end, a window in each side.
			// (Its front edge a sill proud of the door, the steps' top tread meeting it.)
			d.Add(StructureShapes.Box(new Vector3(0f, floor - 0.05f, -0.04f), new Vector3(width, 0.1f, depth + 0.08f), StructureMaterial.Timber, 20)).Grain = Vector3.forward;
			var sideHoles = new List<WallOpening> { new WallOpening { Along = depth * 0.5f - wall + 0.4f, Width = WindowWidth, Bottom = floor + 1.2f - WindowHeight * 0.5f, Top = floor + 1.2f + WindowHeight * 0.5f } };
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-halfW, 0f, -halfD + wall * 0.5f), new Vector3(halfW, 0f, -halfD + wall * 0.5f), wall, floor, top,
					new List<WallOpening> { new WallOpening { Along = halfW, Width = door, Bottom = floor - 0.01f, Top = floor + doorH } }, StructureMaterial.Timber, 21);
				WallRun(d, new Vector3(-halfW, 0f, halfD - wall * 0.5f), new Vector3(halfW, 0f, halfD - wall * 0.5f), wall, floor, top, null, StructureMaterial.Timber, 22);
				for (int s = -1; s <= 1; s += 2)
				{
					WallRun(d, new Vector3(s * (halfW - wall * 0.5f), 0f, -halfD + wall), new Vector3(s * (halfW - wall * 0.5f), 0f, halfD - wall),
						wall, floor, top, new List<WallOpening>(sideHoles), StructureMaterial.Timber, 23 + (s > 0 ? 1 : 0));
				}
			}
			for (int s = -1; s <= 1; s += 2)
			{
				Window(d, new Vector3(s * halfW, floor + 1.2f, 0.4f), new Vector3(s, 0f, 0f), wall, 30 + (s > 0 ? 8 : 0));
			}
			// The curved roof: boards on hoops, a shallow barrel over the walls, its ends closed by the curved heads of the end walls.
			const float rise = 0.45f, overhang = 0.12f;
			float chord = halfW + overhang, radius = (chord * chord + rise * rise) / (2f * rise), cy = top + rise - radius;
			float sweep = Mathf.Asin(chord / radius);
			int strips = d.Seg(8, 4);
			for (int i = 0; i < strips; i++)
			{
				float a0 = -sweep + 2f * sweep * i / strips, a1 = -sweep + 2f * sweep * (i + 1) / strips, a = (a0 + a1) * 0.5f;
				float boardW = 2f * radius * Mathf.Sin((a1 - a0) * 0.5f) + 0.02f;
				var at = new Vector3(Mathf.Sin(a) * (radius + 0.03f), cy + Mathf.Cos(a) * (radius + 0.03f), 0f);
				using (d.Builds(RoofRoles))
				{
					d.Add(StructureShapes.Block(at, new Vector3(boardW, 0.06f, depth + 0.3f), Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg), StructureMaterial.Timber, 40 + i, StructureRole.Roof)).Grain = Vector3.forward;
				}
			}
			var head = new List<Vector2>();
			int n = Mathf.Max(3, strips);
			for (int i = 0; i <= n; i++)
			{
				float a = -Mathf.Asin((halfW - 0.01f) / radius) + 2f * Mathf.Asin((halfW - 0.01f) / radius) * i / n;
				head.Add(new Vector2(Mathf.Sin(a) * radius, cy + Mathf.Cos(a) * radius - 0.01f));
			}
			using (d.Builds(WallRoles))
			{
				for (int e = -1; e <= 1; e += 2)
				{
					float z = e * (halfD - wall * 0.5f);
					d.Add(StructureShapes.Prism(head, z - wall * 0.5f, z + wall * 0.5f, StructureMaterial.Timber, 50 + (e > 0 ? 1 : 0)));
				}
			}
			// The door, the steps up to it, the room inside with the stove against the right wall under its pipe.
			HangDoor(d, new Vector3(0f, floor, -halfD + wall - 0.03f), Vector3.right, Vector3.forward, door, doorH, StructureDoorKind.Planks);
			Steps(d, new Vector3(0f, 0f, -halfD - 0.08f), Vector3.back, 1f, floor, StructureMaterial.Timber, 60);
			d.Room(new Vector3(0f, floor, 0f), new Vector2(width - 2f * wall, depth - 2f * wall), wallH, StructureRoomKind.Dwelling);
			var pipe = new Vector3(0.7f, 0f, 1.3f);
			d.RoomHearth(new Vector2(halfW - wall - 0.47f, pipe.z), 90f);
			float pipeTop = top + rise + 0.75f;
			d.Add(StructureShapes.Cylinder(0.07f, top, pipeTop, d.Seg(8, 5), StructureMaterial.Iron, 70).Transform(Matrix4x4.Translate(pipe)));
			d.Emit(pipe + Vector3.up * (pipeTop + 0.15f), SceneEmitterKind.Smoke);
			if (!d.Mid)
			{
				return;
			}
			// The pipe's hat, a handrail by the steps, a lantern hook by the door.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, pipeTop + 0.05f), new Vector2(0f, pipeTop + 0.16f) }, d.Seg(8, 5), StructureMaterial.Iron, 71, true, 0f, false, false, StructureRole.Detail)
				.Transform(Matrix4x4.Translate(pipe)));
			d.Add(StructureShapes.Beam(new Vector3(0.62f, floor + 0.9f, -halfD - 0.05f), new Vector3(0.62f, 0.85f, -halfD - 0.95f), 0.05f, 0.05f, StructureMaterial.Timber, 72, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(0.62f, -0.2f, -halfD - 0.95f), new Vector3(0.62f, 0.9f, -halfD - 0.95f), 0.06f, 0.06f, StructureMaterial.Timber, 73, StructureRole.Detail));
		}

		/// <summary>The stone bothy of the high grazing: rubble walls under a turf roof (it follows its palette), a chimney at its gable, the crook and a hurdle by the door.</summary>
		private static void ShepherdBothy(StructureDraft d)
		{
			var h = new House
			{
				Width = 4.6f, Depth = 4.2f, WallHeight = 2f, Rise = 0f, Overhang = 0.4f, Wall = 0.55f, Plinth = 0.2f,
				WallMaterial = StructureMaterial.Fieldstone, RoofMaterial = StructureMaterial.Earth, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, RidgeAlongX = true, Chimney = true, Windows = 1,
				DoorWidth = 0.9f, DoorHeight = 1.8f, DoorX = -0.75f, DoorKind = StructureDoorKind.Planks, RoomKind = StructureRoomKind.Dwelling, Key = 1,
			};
			BuildHouse(d, h);
			if (!d.Mid)
			{
				return;
			}
			float front = -h.Depth * 0.5f;
			// The crook leaning by the door, its hook up; a hurdle against the wall beyond it.
			float doorRight = h.DoorX + h.DoorWidth * 0.5f;
			d.Add(StructureShapes.Beam(new Vector3(doorRight + 0.3f, 0.05f, front - 0.25f), new Vector3(doorRight + 0.36f, 1.55f, front - 0.04f), 0.035f, 0.035f, StructureMaterial.Timber, 900, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(doorRight + 0.36f, 1.55f, front - 0.04f), new Vector3(doorRight + 0.5f, 1.66f, front - 0.06f), 0.03f, 0.03f, StructureMaterial.Timber, 901, StructureRole.Detail));
			d.Add(StructureShapes.Block(new Vector3(1.45f, 0.5f, front - 0.22f), new Vector3(1.5f, 0.95f, 0.06f), Quaternion.Euler(-12f, 0f, 0f), StructureMaterial.Timber, 902, StructureRole.Detail)).Grain = Vector3.right;
		}

		// ── Sheepfold ─────────────────────────────────────────────────

		/// <summary>
		/// Where the flock is gathered for counting, shearing and dipping (2026-10-19): open ground inside a wall a sheep cannot
		/// clear, one gap at the front (−z) to drive them through. v0 the round drystone fold of the uplands, coped, a creep hole
		/// in its back for the lambs; v1 the lowland fold of wattle hurdles lashed to stakes, put up and taken down as the flock
		/// moves.
		/// </summary>
		private static void Sheepfold(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				HurdleFold(d);
				return;
			}
			const float inner = 4.1f, outer = 4.65f, height = 1.25f, gap = 1.6f;
			int seg = d.Seg(28, 14);
			const float door = 1.5f * Mathf.PI;
			var holes = new List<(float angle, float width, float bottom, float top)>
			{
				(door, gap, -0.31f, height + 0.5f),
				// The creep hole in the back, a lamb's height.
				(0.5f * Mathf.PI, 0.45f, -0.31f, 0.4f),
			};
			using (d.Builds(WallRoles))
			{
				RingWallWithOpenings(d, Vector3.zero, inner, outer, -0.3f, height, seg, door, holes, StructureMaterial.Fieldstone, 1);
				// The coping, a course of flat stones a little proud both sides, broken at the gap.
				RingWallWithOpenings(d, Vector3.zero, inner - 0.06f, outer + 0.06f, height, height + 0.18f, seg, door,
					new List<(float, float, float, float)> { (door, gap - 0.12f, height - 0.01f, height + 0.5f) },
					StructureMaterial.Fieldstone, 100);
			}
			if (!d.Mid)
			{
				return;
			}
			// Tall stones either side of the gap, a hurdle leant beside it to close it, a stone trough inside against the wall.
			float gapA = Mathf.Asin(gap * 0.5f / inner) + 0.06f;
			for (int s = -1; s <= 1; s += 2)
			{
				float a = door + s * (gapA + 0.05f);
				var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ((inner + outer) * 0.5f);
				d.Add(RoughStone(d, at + Vector3.down * 0.3f, new Vector3(0.5f, height + 0.75f, 0.6f), a * Mathf.Rad2Deg, 200 + (s > 0 ? 1 : 0), 2));
			}
			d.Add(StructureShapes.Block(new Vector3(-2.2f, 0.5f, -outer - 0.25f), new Vector3(1.8f, 0.95f, 0.06f), Quaternion.Euler(-14f, 8f, 0f), StructureMaterial.Timber, 210, StructureRole.Detail)).Grain = Vector3.right;
			// (Along the wall a third of the way round from the creep hole, its long side to the wall.)
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 0.5f), d.Bevel(0.03f), StructureMaterial.Stone, 211).Transform(
				Matrix4x4.TRS(new Vector3(Mathf.Cos(Mathf.PI / 6f), 0f, Mathf.Sin(Mathf.PI / 6f)) * (inner - 0.35f), Quaternion.Euler(0f, -120f, 0f), Vector3.one)));
		}

		/// <summary>The hurdle fold: a square of 2 m hurdles on stakes, the gate hurdle in the front swung open, a hay rack and a trough inside.</summary>
		private static void HurdleFold(StructureDraft d)
		{
			const float half = 4f, gate = 1.6f;
			int k = 1;
			void Hurdle(Vector3 a, Vector3 b)
			{
				Vector3 run = b - a;
				int n = Mathf.Max(1, Mathf.RoundToInt(run.magnitude / 2f));
				Quaternion turn = Quaternion.LookRotation(Vector3.Cross(Vector3.up, run.normalized), Vector3.up);
				using (d.Builds(FenceRoles))
				{
					for (int i = 0; i < n; i++)
					{
						Vector3 p0 = Vector3.Lerp(a, b, (float)i / n), p1 = Vector3.Lerp(a, b, (i + 1f) / n);
						d.Add(StructureShapes.Block((p0 + p1) * 0.5f + Vector3.up * 0.55f, new Vector3((p1 - p0).magnitude - 0.04f, 0.95f, 0.07f), turn, StructureMaterial.Timber, k++)).Grain = run.normalized;
						d.Add(StructureShapes.Beam(p0 + Vector3.down * 0.3f, p0 + Vector3.up * 1.15f, 0.07f, 0.07f, StructureMaterial.Timber, k++));
						if (d.Fine)
						{
							// The hurdle's sails standing proud of the weave, and the withy tie at its top.
							for (int j = 1; j < 4; j++)
							{
								Vector3 sail = Vector3.Lerp(p0, p1, j / 4f) + turn * new Vector3(0f, 0f, 0.045f);
								d.Add(StructureShapes.Beam(sail + Vector3.up * 0.05f, sail + Vector3.up * 1.08f, 0.035f, 0.025f, StructureMaterial.Timber, k++, StructureRole.Detail));
							}
						}
					}
					d.Add(StructureShapes.Beam(b + Vector3.down * 0.3f, b + Vector3.up * 1.15f, 0.07f, 0.07f, StructureMaterial.Timber, k++));
				}
			}
			Hurdle(new Vector3(-half, 0f, half), new Vector3(half, 0f, half));
			Hurdle(new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half));
			Hurdle(new Vector3(half, 0f, -half), new Vector3(half, 0f, half));
			Hurdle(new Vector3(-half, 0f, -half), new Vector3(-gate * 0.5f, 0f, -half));
			Hurdle(new Vector3(gate * 0.5f, 0f, -half), new Vector3(half, 0f, -half));
			// The gate hurdle, hung on its stake and swung out.
			using (d.Builds(FenceRoles))
			{
				var hinge = new Vector3(gate * 0.5f, 0f, -half);
				Quaternion swing = Quaternion.Euler(0f, 65f, 0f);
				d.Add(StructureShapes.Block(hinge + swing * new Vector3(-gate * 0.5f, 0.55f, 0f), new Vector3(gate - 0.08f, 0.95f, 0.06f), swing, StructureMaterial.Timber, 80, StructureRole.Detail));
			}
			if (!d.Mid)
			{
				return;
			}
			// The hay rack: a slatted crib on legs against the back, hay in it; a trough hewn from a log beside it.
			var rack = new Vector3(-1.5f, 0f, half - 0.7f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(rack + new Vector3(s * 0.8f, 0f, 0f), rack + new Vector3(s * 0.8f, 1.2f, 0f), 0.08f, 0.08f, StructureMaterial.Timber, 90 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Block(rack + Vector3.up * 0.95f, new Vector3(1.7f, 0.5f, 0.5f), Quaternion.identity, StructureMaterial.Timber, 92, StructureRole.Detail)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(rack + Vector3.up * 1.24f, new Vector3(1.5f, 0.1f, 0.4f), StructureMaterial.Thatch, 93, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(1.2f, 0.17f, half - 0.6f), new Vector3(1.3f, 0.34f, 0.4f), StructureMaterial.Timber, 94)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(1.2f, 0.341f, half - 0.6f), new Vector3(1.15f, 0.01f, 0.28f), StructureMaterial.Ash, 95, StructureRole.Detail));
		}

		// ── Bothy ─────────────────────────────────────────────────────

		/// <summary>
		/// An outlier's hut (2026-10-19), for the man who lives with his work: the woodcutter in the wood, the beekeeper by his skeps,
		/// the keeper of a far orchard. One room (StructureRoomKind.Keeper: a bed, a table and stool, a chest, a shelf). v0 a plank
		/// shed under a lean-to of boards falling to the back (a lean-to keeps its own pitch, StructureRoofs); v1 the round hut of
		/// wattle daubed with clay under a cone of thatch (it follows its palette).
		/// </summary>
		private static void Bothy(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				RoundBothy(d);
				return;
			}
			const float width = 4f, depth = 3.2f, floor = 0.2f, wall = 0.12f, frontH = 2.5f, backH = 1.95f, door = 0.9f, doorH = 1.9f, doorX = -0.7f;
			float halfW = width * 0.5f, halfD = depth * 0.5f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (floor - FoundationDepth) * 0.5f, 0f), new Vector3(width + 0.2f, floor + FoundationDepth, depth + 0.2f), d.Bevel(0.03f), StructureMaterial.Fieldstone, 1));
			}
			float slope = (frontH - backH) / (depth - wall);
			using (d.Builds(WallRoles))
			{
				var holes = new List<WallOpening>
				{
					new WallOpening { Along = doorX + halfW, Width = door, Bottom = floor - 0.01f, Top = floor + doorH },
					new WallOpening { Along = 1.1f + halfW, Width = WindowWidth, Bottom = floor + 1.25f - WindowHeight * 0.5f, Top = floor + 1.25f + WindowHeight * 0.5f },
				};
				WallRun(d, new Vector3(-halfW, 0f, -halfD + wall * 0.5f), new Vector3(halfW, 0f, -halfD + wall * 0.5f), wall, floor, floor + frontH, holes, StructureMaterial.Timber, 2);
				WallRun(d, new Vector3(-halfW, 0f, halfD - wall * 0.5f), new Vector3(halfW, 0f, halfD - wall * 0.5f), wall, floor, floor + backH, null, StructureMaterial.Timber, 3);
				for (int s = -1; s <= 1; s += 2)
				{
					// The side walls, their heads cut to the roof's fall.
					StructureSolid side = StructureShapes.Box(new Vector3(s * (halfW - wall * 0.5f), floor + frontH * 0.5f, 0f), new Vector3(wall, frontH, depth - 2f * wall), StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0));
					var n = new Vector3(0f, 1f, slope).normalized;
					side.Clip(n, Vector3.Dot(n, new Vector3(0f, floor + frontH, -halfD + wall * 0.5f)));
					d.Add(side).Grain = Vector3.up;
				}
			}
			// The lean-to: boards on the wall heads, falling to the back, overhanging all round.
			float angle = Mathf.Atan(slope) * Mathf.Rad2Deg;
			float midY = floor + (frontH + backH) * 0.5f + 0.06f;
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Block(new Vector3(0f, midY, 0f), new Vector3(width + 0.6f, 0.08f, (depth + 0.7f) / Mathf.Cos(angle * Mathf.Deg2Rad)), Quaternion.Euler(angle, 0f, 0f),
					StructureMaterial.Timber, 6, StructureRole.Roof)).Grain = Vector3.forward;
			}
			HangDoor(d, new Vector3(doorX, floor, -halfD + wall - 0.03f), Vector3.right, Vector3.forward, door, doorH, StructureDoorKind.Planks);
			d.Room(new Vector3(0f, floor, 0f), new Vector2(width - 2f * wall, depth - 2f * wall), backH, StructureRoomKind.Keeper);
			// The window's pane, its shutters open against the wall.
			var pane = new Vector3(1.1f, floor + 1.25f, -halfD);
			d.Add(StructureShapes.Box(pane + Vector3.forward * (wall * 0.5f), new Vector3(WindowWidth, WindowHeight, 0.04f), StructureMaterial.Glass, 10, StructureRole.Detail));
			// A chopping block with its axe, and logs stacked against the side.
			var block = new Vector3(-2.6f, 0f, -1.2f);
			d.Add(StructureShapes.Cylinder(0.3f, -0.1f, 0.5f, d.Seg(10, 6), StructureMaterial.Timber, 20, true).Transform(Matrix4x4.Translate(block)));
			for (int i = 0; i < (d.Mid ? 3 : 2); i++)
			{
				float y = 0.16f + (i == 2 ? 0.3f : 0f), z = -0.6f + (i == 2 ? 0.55f : i * 1.1f) + 0.55f;
				Log(d, new Vector3(-halfW - 0.3f, y, z - 0.55f), new Vector3(-halfW - 0.3f, y, z + 0.55f), 0.15f, 21 + i);
			}
			if (!d.Mid)
			{
				return;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(pane + new Vector3(s * (WindowWidth * 0.5f + 0.17f), 0f, -0.04f), new Vector3(0.32f, WindowHeight + 0.04f, 0.04f), StructureMaterial.Timber, 11 + (s > 0 ? 1 : 0), StructureRole.Detail)).Grain = Vector3.up;
			}
			d.Add(StructureShapes.Box(pane + new Vector3(0f, -WindowHeight * 0.5f - 0.05f, -0.06f), new Vector3(WindowWidth + 0.2f, 0.08f, 0.14f), StructureMaterial.Timber, 13, StructureRole.Detail));
			// The axe in the block, a bow saw and a rake leaning on the front by the door.
			d.Add(StructureShapes.Beam(block + new Vector3(0.05f, 0.45f, 0f), block + new Vector3(0.2f, 1.05f, -0.05f), 0.04f, 0.04f, StructureMaterial.Timber, 30, StructureRole.Detail));
			d.Add(StructureShapes.Box(block + new Vector3(0.03f, 0.5f, 0f), new Vector3(0.05f, 0.12f, 0.18f), StructureMaterial.Iron, 31, StructureRole.Detail));
			float lean = -halfD - 0.12f;
			d.Add(StructureShapes.Beam(new Vector3(-1.75f, 0.02f, lean - 0.3f), new Vector3(-1.65f, 1.55f, lean), 0.035f, 0.035f, StructureMaterial.Timber, 32, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(-1.72f, 0.12f, lean - 0.28f), new Vector3(0.42f, 0.06f, 0.06f), StructureMaterial.Iron, 33, StructureRole.Detail));
			d.Add(StructureShapes.Block(new Vector3(0.35f, 0.62f, lean - 0.08f), new Vector3(0.75f, 0.32f, 0.02f), Quaternion.Euler(-8f, 0f, 80f), StructureMaterial.Timber, 34, StructureRole.Detail));
			d.Add(StructureShapes.Block(new Vector3(0.35f, 0.62f, lean - 0.1f), new Vector3(0.7f, 0.015f, 0.025f), Quaternion.Euler(-8f, 0f, 80f), StructureMaterial.Iron, 35, StructureRole.Detail));
		}

		/// <summary>The round bothy: a daubed wattle wall on a stone footing, its door at the front, a cone of thatch, skeps on a bench by the door.</summary>
		private static void RoundBothy(StructureDraft d)
		{
			const float inner = 2f, outer = 2.2f, floor = 0.15f, wallTop = 2f, door = 0.95f, doorH = 1.8f;
			int seg = d.Seg(16, 10);
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(outer + 0.15f, -FoundationDepth), new Vector2(outer + 0.15f, floor), new Vector2(0f, floor) }, seg, StructureMaterial.Fieldstone, 1));
			}
			using (d.Builds(WallRoles))
			{
				RingWallWithDoor(d, Vector3.zero, inner, outer, floor, wallTop, seg, 1.5f * Mathf.PI, door, floor + doorH, StructureMaterial.Plaster, 10);
			}
			HangDoor(d, new Vector3(0f, floor, -Mathf.Sqrt(inner * inner - door * door * 0.25f) + 0.05f), Vector3.right, Vector3.forward, door, doorH, StructureDoorKind.Planks);
			d.RoundRoom(new Vector3(0f, floor, 0f), inner, wallTop - floor, StructureRoomKind.Keeper);
			// The cone at its palette's pitch (StructureRoofs); flat, a round deck over the wall.
			const float kitRise = 3.3f, eaves = outer + 0.55f;
			OwnRoofPlan roof = OwnRoof(d, StructureMaterial.Thatch, kitRise, eaves);
			using (d.Builds(RoofRoles))
			{
				if (roof.Flat)
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, wallTop - 0.05f), new Vector2(eaves, wallTop - 0.05f), new Vector2(eaves, wallTop + 0.2f), new Vector2(0f, wallTop + 0.2f) },
						seg, StructureMaterial.Thatch, 30, true, 0f, false, false, StructureRole.Roof));
				}
				else
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, wallTop - 0.1f), new Vector2(eaves, wallTop - 0.25f), new Vector2(eaves, wallTop + 0.05f), new Vector2(0.3f, wallTop + roof.Rise - 0.3f), new Vector2(0f, wallTop + roof.Rise - 0.25f) },
						seg, StructureMaterial.Thatch, 30, true, 0f, false, false, StructureRole.Roof));
				}
			}
			if (!d.Mid)
			{
				return;
			}
			// The bench by the door with its skeps, and the door's timber frame in the daub.
			var bench = new Vector3(1.55f, 0f, -outer - 0.55f);
			d.Add(StructureShapes.Box(bench + Vector3.up * 0.42f, new Vector3(1.3f, 0.06f, 0.36f), StructureMaterial.Timber, 40, StructureRole.Detail)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(bench + new Vector3(s * 0.5f, 0.2f, 0f), new Vector3(0.08f, 0.4f, 0.3f), StructureMaterial.Timber, 41 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			for (int i = 0; i < 2; i++)
			{
				var skep = bench + new Vector3(-0.3f + i * 0.6f, 0.45f, 0f);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.22f, 0f), new Vector2(0.22f, 0.16f), new Vector2(0.14f, 0.36f), new Vector2(0f, 0.42f) }, d.Seg(10, 6), StructureMaterial.Thatch, 43 + i, true, 0f, false, false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(skep)));
			}
			float jamb = Mathf.Sqrt(outer * outer - door * door * 0.25f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * (door * 0.5f + 0.06f), floor, -jamb - 0.02f), new Vector3(s * (door * 0.5f + 0.06f), floor + doorH + 0.08f, -jamb - 0.02f), 0.12f, 0.08f,
					StructureMaterial.Timber, 46 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			d.Add(StructureShapes.Beam(new Vector3(-door * 0.5f - 0.15f, floor + doorH + 0.08f, -jamb - 0.02f), new Vector3(door * 0.5f + 0.15f, floor + doorH + 0.08f, -jamb - 0.02f), 0.14f, 0.12f,
				StructureMaterial.Timber, 48, StructureRole.Detail));
		}
	}
}
#endif
