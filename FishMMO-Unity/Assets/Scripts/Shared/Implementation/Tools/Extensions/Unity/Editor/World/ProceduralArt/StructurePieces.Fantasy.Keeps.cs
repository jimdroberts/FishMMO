#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class StructurePieces
	{
		/// <summary>
		/// The fantasy wave's Keeps pack: its new pieces (2026-10-27, poi-fantasy-wave/WAVE.md). A sorcerer's black spire for the dark
		/// towers and dark keeps (Orthanc's horned crown, Barad-dûr's stepped tiers) and the elves' slender white tower, each with its
		/// keeper's room at the foot behind a real door. Laid by id only (=DarkSpire, =ElvenSpire): "building" and "tower" draw neither
		/// into an older query (a wall tower is "tower+wall", a watchtower "tower+military").
		/// </summary>
		/// <remarks>
		/// Their walls are built in materials no palette re-lays (slate and marble are no kit material of the stone style, so the solids
		/// play no palette part): the dark spire stays black and the elven tower white in every biome. The upper floors are sealed, as
		/// the lighthouse's are.
		/// </remarks>
		private static void AddFantasyKeeps(List<StructurePiece> list)
		{
			Piece(list, "DarkSpire", StructureStyle.Stone, StructureSize.Building,
				"A sorcerer's black tower, 8 m across at its foot: a guard room behind an iron door at the top of three steps, its walls of black slate, the shaft sealed above it with lit slit windows; v0 tapering and faceted to 17 m between four sloping buttresses that rise past its crown as four black horns round a glowing orb (Orthanc); v1 octagonal in three stepped tiers to 18.6 m, its crown battlemented with iron spikes and a great fire bowl burning at its middle (Barad-dûr).",
				DarkSpire, "building", "tower", "wizard");
			Piece(list, "ElvenSpire", StructureStyle.Stone, StructureSize.Building,
				"The elves' slender white tower, 5.4 m across: a study behind a studded door, its marble shaft ribbed and tapering to a balcony at 15 m, tall lancets lit at night; v0 a lantern drum of six lancets under a needle spire to 24 m; v1 a drum ringed by six slender columns under a leaf-shaped dome, a faerie light caged on its tip.",
				ElvenSpire, "building", "tower");
		}

		// ── The dark spire ────────────────────────────────────────────

		/// <summary>The dark spire's plinth top (its room's floor), the room's ceiling top, its foot's outer radius and wall, metres.</summary>
		private const float DarkFloor = 0.6f, DarkRoomTop = 4.6f, DarkOuter = 3.4f, DarkWall = 0.8f, DarkPlinthR = 4.1f;

		/// <summary>
		/// A sorcerer's black tower (2026-10-27, the fantasy wave: "a tall black sorcerer's tower"): a guard room at the foot with its
		/// iron door, the shaft over it sealed, slit windows lit at night. v0 (Orthanc) tapers, faceted, between four buttresses that
		/// run up into four horns round an orb of rune-light; v1 (Barad-dûr) rises in three octagonal tiers to a spiked battlement round
		/// a fire bowl.
		/// </summary>
		private static void DarkSpire(StructureDraft d)
		{
			bool horned = d.Variant == 0;
			int seg = d.Seg(16, 8);
			const float inner = DarkOuter - DarkWall;
			float doorA = 1.5f * Mathf.PI;
			const float doorW = 1.3f, doorH = 2.5f;
			Vector3 Out(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			float octPhase = doorA - Mathf.PI / 8f;
			float plinthFace = DarkPlinthR * Mathf.Cos(Mathf.PI / 8f);

			// The plinth: a black octagon, a face square to the door, its top the room's floor.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(DarkPlinthR, -FoundationDepth), new Vector2(DarkPlinthR, DarkFloor) }, 8, StructureMaterial.Slate, 1, false, octPhase));

			// The guard room: a round wall with the door, its ceiling, a string course over it where the shaft begins.
			RingWallWithDoor(d, Vector3.zero, inner, DarkOuter, DarkFloor, DarkRoomTop, seg, doorA, doorW, DarkFloor + doorH, StructureMaterial.Slate, 100);
			d.Add(StructureShapes.Cylinder(inner + 0.05f, DarkRoomTop - 0.25f, DarkRoomTop, seg, StructureMaterial.Slate, 30));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(DarkOuter + 0.2f, DarkRoomTop - 0.2f), new Vector2(DarkOuter + 0.2f, DarkRoomTop + 0.2f) }, horned ? seg : 8,
				StructureMaterial.Slate, 31, !horned, horned ? 0f : octPhase));
			HangDoor(d, new Vector3(0f, DarkFloor, -Mathf.Sqrt(inner * inner - doorW * doorW * 0.25f) + 0.05f), Vector3.right, Vector3.forward, doorW, doorH, StructureDoorKind.Iron);
			d.RoundRoom(new Vector3(0f, DarkFloor, 0f), inner, DarkRoomTop - 0.25f - DarkFloor, StructureRoomKind.Guard);
			Steps(d, new Vector3(0f, 0f, -plinthFace), Vector3.back, 1.8f, DarkFloor, StructureMaterial.Stone, 60);
			KeepsDoorArch(d, -DarkOuter * Mathf.Cos(Mathf.PI / seg), DarkFloor, doorW, doorH, 0.4f, StructureMaterial.Slate, 40);

			float top, topR;
			if (horned)
			{
				// The shaft, faceted, tapering from the string course to the crown.
				top = 17.2f;
				topR = 2.5f;
				int facets = d.Seg(12, 8);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(DarkOuter, DarkRoomTop), new Vector2(topR, top) }, facets, StructureMaterial.Slate, 32, false, doorA - Mathf.PI / facets));
				float ShaftR(float y) => Mathf.Lerp(DarkOuter, topR, (y - DarkRoomTop) / (top - DarkRoomTop));
				// Four buttresses on the diagonals, sloping in from the ground to the crown (the door's face between two of them).
				for (int i = 0; i < 4; i++)
				{
					float a = (45f + 90f * i) * Mathf.Deg2Rad;
					var fin = new[] { new Vector2(2.9f, -FoundationDepth), new Vector2(4.9f, -FoundationDepth), new Vector2(4.9f, 1.2f), new Vector2(topR + 0.35f, top), new Vector2(topR - 0.4f, top) };
					d.Add(StructureShapes.Prism(fin, -0.35f, 0.35f, StructureMaterial.Slate, 200 + i).Transform(Matrix4x4.Rotate(Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f))));
				}
				// The slit windows between them, up the shaft.
				float[] heights = { 7.2f, 10.6f, 14f };
				for (int h = 0; h < heights.Length; h++)
				{
					for (int i = 0; i < 4; i++)
					{
						if (h == 0 && i == 3)
						{
							continue; // over the door, the arch
						}
						float a = 90f * i * Mathf.Deg2Rad;
						KeepsSlit(d, a, ShaftR(heights[h] + 0.6f) * Mathf.Cos(Mathf.PI / facets), heights[h], 0.3f, 1.3f, StructureMaterial.Slate, 300 + h * 10 + i * 2);
					}
				}

				// The crown: a corbelled roof over the shaft, the buttresses rising past it as four horns, an orb of rune-light in iron
				// claws on a pedestal between them.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(topR - 0.05f, top - 0.9f), new Vector2(topR + 0.55f, top - 0.3f), new Vector2(topR + 0.55f, top + 0.15f), new Vector2(0f, top + 0.15f) },
					facets, StructureMaterial.Slate, 33, false, doorA - Mathf.PI / facets));
				int hornSeg = d.Seg(8, 4);
				for (int i = 0; i < 4; i++)
				{
					float a = (45f + 90f * i) * Mathf.Deg2Rad;
					Vector3 foot = Out(a) * (topR + 0.05f) + Vector3.up * (top - 0.4f);
					Vector3 tip = Out(a) * (topR + 0.95f) + Vector3.up * 23.6f;
					d.Add(StructureShapes.Rod(foot, tip, 0.42f, hornSeg, StructureMaterial.Slate, 210 + i, StructureRole.Structure, 3f));
				}
				d.Add(StructureShapes.Cylinder(0.45f, top + 0.1f, top + 1.15f, d.Seg(10, 6), StructureMaterial.Slate, 34));
				d.Add(StructureShapes.Sphere(new Vector3(0f, top + 1.75f, 0f), new Vector3(0.55f, 0.55f, 0.55f), d.Seg(12, 6), d.Fine ? 6 : 4, StructureMaterial.Runelight, 35, StructureRole.Detail));
				if (d.Mid)
				{
					for (int i = 0; i < 4; i++)
					{
						float a = (90f * i) * Mathf.Deg2Rad;
						d.Add(StructureShapes.Beam(Out(a) * 0.35f + Vector3.up * (top + 1.1f), Out(a) * 0.5f + Vector3.up * (top + 2.15f), 0.07f, 0.07f, StructureMaterial.Iron, 220 + i, StructureRole.Detail));
					}
				}
				d.Emit(new Vector3(0f, top + 1.75f, 0f), SceneEmitterKind.Glow);
				return;
			}

			// v1: three octagonal tiers, each stepped in over a ledge.
			top = 18.6f;
			topR = 2.1f;
			d.Add(StructureShapes.Lathe(new[]
			{
				new Vector2(DarkOuter, DarkRoomTop), new Vector2(3.1f, 10.4f), new Vector2(3.35f, 10.4f), new Vector2(3.35f, 10.8f), new Vector2(2.8f, 10.8f),
				new Vector2(2.55f, 15.6f), new Vector2(2.8f, 15.6f), new Vector2(2.8f, 15.9f), new Vector2(2.25f, 15.9f), new Vector2(topR, top),
			}, 8, StructureMaterial.Slate, 32, false, octPhase));
			float cos8 = Mathf.Cos(Mathf.PI / 8f);
			// The slits, on the faces' middles, two tiers' worth and the top tier's.
			(float y, float r)[] rows = { (6.6f, 3.25f), (8.8f, 3.18f), (12.2f, 2.72f), (14.2f, 2.62f), (16.6f, 2.2f) };
			for (int h = 0; h < rows.Length; h++)
			{
				for (int i = 0; i < 4; i++)
				{
					if (h == 0 && i == 3)
					{
						continue;
					}
					float a = doorA + (90f * i + (h % 2 == 0 ? 0f : 45f)) * Mathf.Deg2Rad;
					KeepsSlit(d, a, rows[h].r * cos8, rows[h].y, 0.28f, 1.1f, StructureMaterial.Slate, 300 + h * 10 + i * 2);
				}
			}
			// The crown: a corbelled walk round the top, merlons on its eight faces, iron spikes on them and four great ones at the
			// corners, and the fire bowl burning in the middle on its drum.
			float walkR = topR + 0.5f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(topR - 0.05f, top - 0.7f), new Vector2(walkR, top - 0.2f), new Vector2(walkR, top), new Vector2(0f, top) },
				8, StructureMaterial.Slate, 33, false, octPhase));
			for (int f = 0; f < 8; f++)
			{
				float a0 = octPhase + f * Mathf.PI / 4f, a1 = a0 + Mathf.PI / 4f;
				Vector3 p0 = Out(a0) * (walkR - 0.15f) + Vector3.up * top, p1 = Out(a1) * (walkR - 0.15f) + Vector3.up * top;
				if (d.Mid)
				{
					Crenellate(d, p0, p1, 0.3f, 0.9f, 0.5f, 0.4f, StructureMaterial.Slate, 400 + f * 4, StructureRole.Structure);
				}
				else
				{
					d.Add(StructureShapes.Block((p0 + p1) * 0.5f + Vector3.up * 0.45f, new Vector3((p1 - p0).magnitude, 0.9f, 0.3f),
						Quaternion.FromToRotation(Vector3.right, (p1 - p0).normalized), StructureMaterial.Slate, 400 + f * 4));
				}
			}
			int spikeSeg = d.Seg(6, 4);
			for (int i = 0; i < 4; i++)
			{
				float a = octPhase + (i * 2 + 0.5f) * Mathf.PI / 4f;
				Vector3 foot = Out(a) * (walkR - 0.2f) * cos8 + Vector3.up * (top - 0.3f);
				d.Add(StructureShapes.Rod(foot, foot + Out(a) * 0.5f + Vector3.up * 3.6f, 0.16f, spikeSeg, StructureMaterial.Iron, 230 + i, StructureRole.Structure, 1.1f));
			}
			d.Add(StructureShapes.Cylinder(0.7f, top - 0.05f, top + 0.7f, d.Seg(10, 6), StructureMaterial.Slate, 34));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f, top + 0.65f), new Vector2(1.05f, top + 1.05f), new Vector2(1.15f, top + 1.3f), new Vector2(1.0f, top + 1.3f), new Vector2(0.4f, top + 0.95f) },
				d.Seg(12, 6), StructureMaterial.Iron, 36, true, 0f, true));
			d.Add(StructureShapes.Cylinder(0.95f, top + 1.1f, top + 1.2f, d.Seg(12, 6), StructureMaterial.Ash, 37, false, StructureRole.Detail));
			d.Emit(new Vector3(0f, top + 1.2f, 0f), SceneEmitterKind.Fire);
		}

		// ── The elven tower ───────────────────────────────────────────

		/// <summary>The elven tower's plinth top, its study's ceiling top, its foot's outer radius and wall, and the balcony's floor, metres.</summary>
		private const float ElfFloor = 0.4f, ElfRoomTop = 4.2f, ElfOuter = 2.7f, ElfWall = 0.6f, ElfBalcony = 15.15f;

		/// <summary>
		/// The elves' slender white tower (2026-10-27, the fantasy wave: "a slender white/pale tower"): a study at its foot behind a
		/// studded door, its marble shaft tapering with four ribs up it to a balcony at 15 m; v0 a lantern drum of lancets under a
		/// needle spire, v1 a drum ringed by slender columns under a leaf-shaped dome with a faerie light caged on its tip.
		/// </summary>
		private static void ElvenSpire(StructureDraft d)
		{
			bool needle = d.Variant == 0;
			int seg = d.Seg(20, 10);
			const float inner = ElfOuter - ElfWall;
			float doorA = 1.5f * Mathf.PI;
			const float doorW = 1.2f, doorH = 2.4f;
			Vector3 Out(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			const float shaftTop = 14.95f, shaftR = 2.05f;
			float ShaftR(float y) => Mathf.Lerp(ElfOuter, shaftR, (y - ElfRoomTop) / (shaftTop - ElfRoomTop));

			// The plinth, its top the study's floor (square-edged: a chamfer there was too steep to step up onto from the stair).
			d.Add(StructureShapes.Lathe(new[] { new Vector2(3.2f, -FoundationDepth), new Vector2(3.2f, ElfFloor) }, seg, StructureMaterial.Marble, 1));

			// The study: a round wall with the door, its ceiling, a string course over it.
			RingWallWithDoor(d, Vector3.zero, inner, ElfOuter, ElfFloor, ElfRoomTop, seg, doorA, doorW, ElfFloor + doorH, StructureMaterial.Marble, 100);
			d.Add(StructureShapes.Cylinder(inner + 0.05f, ElfRoomTop - 0.25f, ElfRoomTop, seg, StructureMaterial.Marble, 30));
			HangDoor(d, new Vector3(0f, ElfFloor, -Mathf.Sqrt(inner * inner - doorW * doorW * 0.25f) + 0.05f), Vector3.right, Vector3.forward, doorW, doorH, StructureDoorKind.Studded);
			d.RoundRoom(new Vector3(0f, ElfFloor, 0f), inner, ElfRoomTop - 0.25f - ElfFloor, StructureRoomKind.Study);
			Steps(d, new Vector3(0f, 0f, -3.2f * Mathf.Cos(Mathf.PI / seg)), Vector3.back, 1.6f, ElfFloor, StructureMaterial.Stone, 60);
			KeepsDoorArch(d, -ElfOuter * Mathf.Cos(Mathf.PI / seg), ElfFloor, doorW, doorH, 0.35f, StructureMaterial.Marble, 40);

			// The shaft, tapering to the balcony; string courses round it; four ribs up it on the diagonals.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(ElfOuter, ElfRoomTop), new Vector2(shaftR, shaftTop) }, seg, StructureMaterial.Marble, 31));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(ElfOuter + 0.15f, ElfRoomTop - 0.15f), new Vector2(ElfOuter + 0.15f, ElfRoomTop + 0.15f) }, seg, StructureMaterial.Marble, 32));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(ShaftR(9.6f) + 0.12f, 9.45f), new Vector2(ShaftR(9.6f) + 0.12f, 9.75f) }, seg, StructureMaterial.Marble, 33, true, 0f, false, false, StructureRole.Detail));
				for (int i = 0; i < 4; i++)
				{
					float a = (45f + 90f * i) * Mathf.Deg2Rad;
					d.Add(StructureShapes.Beam(Out(a) * (ElfOuter + 0.08f) + Vector3.up * ElfFloor, Out(a) * (shaftR + 0.06f) + Vector3.up * (shaftTop - 0.6f), 0.18f, 0.24f,
						StructureMaterial.Marble, 200 + i, StructureRole.Detail));
				}
			}
			// The lancets up the shaft, a pointed hood over each.
			float[] heights = { 6.4f, 9.9f, 12.7f };
			for (int h = 0; h < heights.Length; h++)
			{
				for (int i = 0; i < 3; i++)
				{
					float a = doorA + (60f + 120f * i + (h % 2 == 0 ? 0f : 60f)) * Mathf.Deg2Rad;
					KeepsSlit(d, a, ShaftR(heights[h] + 0.65f), heights[h], 0.34f, 1.3f, StructureMaterial.Marble, 300 + h * 10 + i * 2);
				}
			}

			// The balcony: corbelled out round the shaft's top, its floor, a slender rail on posts.
			const float balconyR = 2.9f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(shaftR - 0.05f, shaftTop - 0.7f), new Vector2(balconyR, ElfBalcony - 0.2f), new Vector2(balconyR, ElfBalcony), new Vector2(0f, ElfBalcony) },
				seg, StructureMaterial.Marble, 34));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(balconyR - 0.1f, ElfBalcony + 0.88f), new Vector2(balconyR, ElfBalcony + 0.88f), new Vector2(balconyR, ElfBalcony + 1f), new Vector2(balconyR - 0.1f, ElfBalcony + 1f) },
				seg, StructureMaterial.Marble, 35, true, 0f, true));
			if (d.Mid)
			{
				int posts = d.Fine ? 12 : 8;
				for (int i = 0; i < posts; i++)
				{
					float a = (i + 0.5f) * 2f * Mathf.PI / posts;
					d.Add(StructureShapes.Box(Out(a) * (balconyR - 0.05f) + Vector3.up * (ElfBalcony + 0.45f), new Vector3(0.07f, 0.9f, 0.07f), StructureMaterial.Marble, 240 + i, StructureRole.Detail));
				}
			}

			if (needle)
			{
				// The lantern drum of six lancets, and the needle spire over it with its finial.
				const float drumR = 1.55f, drumTop = 18.2f;
				d.Add(StructureShapes.Cylinder(drumR, ElfBalcony - 0.05f, drumTop, seg, StructureMaterial.Marble, 36));
				for (int i = 0; i < 6; i++)
				{
					float a = doorA + (30f + 60f * i) * Mathf.Deg2Rad;
					KeepsSlit(d, a, drumR * Mathf.Cos(Mathf.PI / seg), 15.9f, 0.32f, 1.5f, StructureMaterial.Marble, 340 + i * 2);
				}
				d.Add(StructureShapes.Lathe(new[]
				{
					new Vector2(1.85f, drumTop), new Vector2(1.75f, drumTop + 0.4f), new Vector2(1.3f, 19.8f), new Vector2(0.55f, 21.8f), new Vector2(0.18f, 23.4f), new Vector2(0f, 24.3f),
				}, seg, StructureMaterial.Marble, 37, true, 0f, false, false, StructureRole.Roof));
				if (d.Mid)
				{
					d.Add(StructureShapes.Rod(new Vector3(0f, 24.1f, 0f), new Vector3(0f, 24.85f, 0f), 0.04f, 4, StructureMaterial.Iron, 38, StructureRole.Detail));
				}
				return;
			}

			// v1: a drum ringed by six slender engaged columns, a lintel ring on them under a leaf-shaped dome, and the faerie light in an
			// iron cage on its tip. (The drum is solid, as v0's: an open belvedere under the dome was a floor nobody could climb to.)
			const float drumR1 = 1.55f, colR = 1.62f, lintel = 18.0f, domeR = 1.85f;
			int colSeg = d.Seg(8, 4);
			d.Add(StructureShapes.Cylinder(drumR1, ElfBalcony - 0.05f, lintel + 0.02f, seg, StructureMaterial.Marble, 36));
			for (int i = 0; i < 6; i++)
			{
				float a = doorA + (60f * i) * Mathf.Deg2Rad;
				d.Add(StructureShapes.Cylinder(0.14f, ElfBalcony - 0.05f, lintel + 0.05f, colSeg, StructureMaterial.Marble, 250 + i).Transform(Matrix4x4.Translate(Out(a) * colR)));
				KeepsSlit(d, a + 30f * Mathf.Deg2Rad, drumR1 * Mathf.Cos(Mathf.PI / seg), 15.9f, 0.3f, 1.4f, StructureMaterial.Marble, 340 + i * 2);
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(domeR, lintel), new Vector2(domeR, lintel + 0.35f), new Vector2(0f, lintel + 0.35f) }, seg, StructureMaterial.Marble, 135));
			d.Add(StructureShapes.Lathe(new[]
			{
				new Vector2(domeR, lintel + 0.35f), new Vector2(domeR - 0.1f, lintel + 0.9f), new Vector2(1.3f, 19.9f), new Vector2(0.62f, 20.9f), new Vector2(0.22f, 21.8f), new Vector2(0f, 22.6f),
			}, seg, StructureMaterial.Marble, 37, true, 0f, false, false, StructureRole.Roof));
			const float lightY = 23.05f;
			d.Add(StructureShapes.Sphere(new Vector3(0f, lightY, 0f), new Vector3(0.32f, 0.32f, 0.32f), d.Seg(10, 6), d.Fine ? 5 : 3, StructureMaterial.Faelight, 39, StructureRole.Detail));
			if (d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					float a = 90f * i * Mathf.Deg2Rad;
					d.Add(StructureShapes.Beam(Out(a) * 0.08f + Vector3.up * 22.45f, Out(a) * 0.4f + Vector3.up * lightY, 0.04f, 0.04f, StructureMaterial.Iron, 260 + i * 2, StructureRole.Detail));
					d.Add(StructureShapes.Beam(Out(a) * 0.4f + Vector3.up * lightY, Vector3.up * (lightY + 0.45f), 0.04f, 0.04f, StructureMaterial.Iron, 261 + i * 2, StructureRole.Detail));
				}
			}
			d.Emit(new Vector3(0f, lightY, 0f), SceneEmitterKind.Glow);
		}

		// ── Shared by both ───────────────────────────────────────────

		/// <summary>
		/// A tall slit window on a tower's face at bearing <paramref name="a"/>, the face <paramref name="face"/> from the axis: its pane
		/// proud of the unbroken wall (lit at night: the sealed floors' lights) and, from the middle level, a pointed hood over it.
		/// </summary>
		private static void KeepsSlit(StructureDraft d, float a, float face, float sill, float width, float height, StructureMaterial hood, int key)
		{
			var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			var t = new Vector3(-n.z, 0f, n.x);
			Quaternion turn = Quaternion.LookRotation(n, Vector3.up);
			Vector3 at = n * (face + 0.02f);
			d.Add(StructureShapes.Block(at + Vector3.up * (sill + height * 0.5f), new Vector3(width, height, 0.05f), turn, StructureMaterial.Glass, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			float top = sill + height;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(at + n * 0.04f + t * (s * (width * 0.5f + 0.1f)) + Vector3.up * (top - 0.15f), at + n * 0.04f + Vector3.up * (top + 0.3f), 0.1f, 0.1f,
					hood, key + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
		}

		/// <summary>
		/// A doorway's pointed surround on a face looking down −z (<paramref name="zFace"/>), in the tower's own stone: the tympanum over
		/// the door's head at every level, its jambs from the middle level (as the wizard's tower's DoorArch, in another material).
		/// </summary>
		private static void KeepsDoorArch(StructureDraft d, float zFace, float floor, float width, float height, float deep, StructureMaterial material, int key)
		{
			float head = floor + height, half = width * 0.5f + 0.18f;
			float z0 = zFace - 0.1f;
			d.Add(StructureShapes.Prism(new[] { new Vector2(-half, head), new Vector2(half, head), new Vector2(0f, head + half * 1.15f) }, z0, zFace + deep, material, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (width * 0.5f + 0.1f), floor + height * 0.5f, z0 + 0.06f), new Vector3(0.2f, height, 0.14f), material, key + 1 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
		}
	}
}
#endif
