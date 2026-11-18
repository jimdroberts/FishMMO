#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The town's other buildings (2026-10-19): the professions' own houses — the counting house, the astronomer's observatory and its great telescope, the excavators' dig.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The counting house</b> is the lombard's and the money-changer's: the "bank" is the banco, the bench the changer
	/// counted coin on, and the three gilt balls hung over the door are the Lombard bankers' sign. Its stone is thick, its
	/// windows barred, its door plated in iron, and its coin is kept behind a second wall at the back of the hall.
	/// </para>
	/// <para>
	/// <b>The observatory</b> is the cusp of the Renaissance at a fantasy scale: Tycho's Uraniborg had its great instruments on
	/// open platforms and in towers; the long timber-and-brass refractor on a fork mount came later, but it reads at once as
	/// "the astronomer's", so it stands here as the tower's crown, with an armillary sphere by it. The platform is the
	/// astronomer's alone: no stair is built up the tower, so no floor stands unreached behind glass. The telescope stands still
	/// (a turning part would be its own piece laid by the mills' rotor table, which the observatory leaves alone).
	/// </para>
	/// <para>
	/// <b>The dig</b> cuts into a raised mound (a barrow, a tell): the kit carves no terrain, so the trench is the gap left
	/// between banks of earth piled on the ground, its floor at the ground line, shored with boards and entered by steps cut at
	/// one end. The open-area dig lays bare an ancient floor between low spoil banks.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		private static void AddLandmarks(List<StructurePiece> list)
		{
			Piece(list, "CountingHouse", StructureStyle.Stone, StructureSize.Building,
				"A counting house (the lombard's bank), its three gilt balls hung on a bracket by the iron-plated door, a carved coin tablet over it, its windows barred: v0 a stone hall 9×12 m gabled to the street with a strongroom walled off at the back behind a vault door; v1 a money-changer's house, a stone ground floor 9×10 m under a jettied timber-framed storey reached by a stair, the changer's fold-down bench under a window.",
				CountingHouse, "building", "bank");
			Piece(list, "Observatory", StructureStyle.Stone, StructureSize.Building,
				"An astronomer's observatory with a great telescope, its banded timber tube on a fork mount tilted at the sky, an armillary sphere beside it, rune-lit stars on its door: v0 a round stone tower 8 m across, the astronomer's room on the ground floor and a parapeted platform 12 m up with a stair turret; v1 a stone hall 10×9 m whose roof is a terrace, the enormous telescope on a turntable with a gantry and a ladder to its eyepiece.",
				Observatory, "building", "observatory", "telescope");
			Piece(list, "DigSite", StructureStyle.Earth, StructureSize.Medium,
				"An excavation: v0 a trench cut into a raised mound, shored with boards, steps cut down at one end and a ladder at the other, a windlass and bucket over it, a sieve on trestles, finds under a tarp, ranging poles and a string grid; v1 an open-area dig between low spoil banks, an ancient flagged floor laid bare with a broken column, fallen drums and a statue half out of the earth, a finds tent over a table of trays.",
				DigSite, "excavation");
			Piece(list, "DigCamp", StructureStyle.Timber, StructureSize.Small,
				"The diggers' finds: v0 a trestle table of trays, pots, bones and brushes beside a crate; v1 a wheelbarrow of spoil, a spade and a pick leaning on stacked finds crates.",
				DigCamp, "excavation");
			// Its furniture (faces −z into the room, its back to the wall).
			Piece(list, "CountingTable", StructureStyle.Timber, StructureSize.Small,
				"A counting table 1.8 m with coin scales, stacks of coin and ledgers, the changer's bench behind it: v0 behind a wooden grille with a wicket; v1 open, under a green cloth with a low rail.",
				CountingTable, "furniture", "bank");
			Piece(list, "Strongbox", StructureStyle.Iron, StructureSize.Small,
				"A strongbox: v0 an oak chest bound both ways in iron with a hasp and padlock; v1 a squat iron safe-chest, riveted, on feet, chained shut.",
				Strongbox, "furniture", "bank");
			Piece(list, "StarChartTable", StructureStyle.Timber, StructureSize.Small,
				"An astronomer's table: v0 a broad table of star charts weighted with an astrolabe, rolled charts and candles; v1 a sloped chart desk with a celestial globe on a stand and a quadrant.",
				StarChartTable, "furniture", "observatory");
		}

		// ── Counting house ────────────────────────────────────────────

		/// <summary>
		/// A counting house. v0: a stone hall gabled to the street, the coin kept in a strongroom walled off across the back
		/// behind a vault door (the leaf an iron plate on the cross wall: the strongroom is the house's, not the player's). v1: a
		/// money-changer's house, his counting room on a stone ground floor and his chamber in the jettied storey over it.
		/// </summary>
		private static void CountingHouse(StructureDraft d)
		{
			bool storeyed = d.Variant == 1;
			int first = d.Solids.Count;
			var h = new House
			{
				Width = 9f, Depth = storeyed ? 10f : 12f, WallHeight = storeyed ? 3f : 3.4f, Rise = 2.6f, Overhang = 0.45f,
				Wall = storeyed ? 0.35f : 0.6f, Plinth = storeyed ? 0.4f : 0.45f,
				WallMaterial = StructureMaterial.Stone, RoofMaterial = StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, RidgeAlongX = storeyed, Chimney = false, Windows = 2,
				DoorWidth = 1.3f, DoorHeight = storeyed ? 2.4f : 2.5f, DoorX = storeyed ? -1.6f : 0f, DoorKind = StructureDoorKind.Iron,
				RoomKind = StructureRoomKind.CountingHouse, Key = 1,
			};
			if (storeyed)
			{
				// The changer lives over the shop: a framed plaster storey on the stone, as the large timber house's stone variant.
				h.Upper = 2.7f;
				h.UpperRoomKind = StructureRoomKind.Chamber;
				h.UpperWallMaterial = StructureMaterial.Plaster;
				h.UpperFrame = true;
			}
			BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f, top = floor + h.WallHeight;
			if (storeyed)
			{
				// The gables stand on the framed storey: plaster, as it is.
				foreach (StructureSolid s in d.Solids)
				{
					if ((s.Key == 13 || s.Key == 14) && s.Material == StructureMaterial.Stone)
					{
						s.Material = StructureMaterial.Plaster;
					}
				}
			}
			// Iron bars in every ground-floor window's reveal.
			BarWindows(d, first, top, h.Wall, 800);

			if (!storeyed)
			{
				Strongroom(d, h, floor, 60);
				// A carved tablet over the door in the gable: the three bezants of the lombards, and a balance either side.
				float ty = floor + h.DoorHeight + 0.72f;
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, ty, front - 0.06f), new Vector3(1.7f, 0.8f, 0.12f), d.Bevel(0.03f), StructureMaterial.Stone, 40, StructureRole.Detail));
				if (d.Mid)
				{
					var discs = new[] { new Vector3(-0.22f, ty + 0.14f, 0f), new Vector3(0.22f, ty + 0.14f, 0f), new Vector3(0f, ty - 0.16f, 0f) };
					for (int i = 0; i < discs.Length; i++)
					{
						Vector3 at = discs[i] + Vector3.forward * (front - 0.12f);
						d.Add(StructureShapes.Cylinder(0.13f, -0.025f, 0.025f, d.Seg(10, 6), StructureMaterial.Bone, 41 + i, false, StructureRole.Detail)
							.Transform(Matrix4x4.TRS(at, Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
					}
				}
				if (d.Fine)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						Scales(d, new Vector3(s * 0.62f, ty - 0.3f, front - 0.14f), 0.4f, 0.02f, 45 + (s > 0 ? 10 : 0));
					}
				}
				LombardSign(d, new Vector3(2.9f, floor + 3.0f, front), 70);
				return;
			}

			// The changer's bench under the ground floor's rightmost front window: a board let down on brackets, its cloth and coin.
			Vector3 bench = new Vector3(2.4f, 0f, front);
			float best = float.MinValue;
			for (int i = first; i < d.Solids.Count; i++)
			{
				StructureSolid s = d.Solids[i];
				if (s.Material != StructureMaterial.Glass)
				{
					continue;
				}
				Bounds b = s.Bounds;
				if (b.center.y < top && b.center.z < front + h.Wall && b.size.z < b.size.x && b.center.x > best)
				{
					best = b.center.x;
					bench.x = b.center.x;
				}
			}
			using (d.Builds(TrimRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(bench.x, 0.97f, front - 0.3f), new Vector3(1.3f, 0.06f, 0.56f), StructureMaterial.Timber, 90, StructureRole.Detail)).Grain = Vector3.right;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(bench.x + s * 0.5f, 0.45f, front - 0.02f), new Vector3(bench.x + s * 0.5f, 0.93f, front - 0.5f), 0.06f, 0.06f,
						StructureMaterial.Timber, 91 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(bench.x, 1.005f, front - 0.3f), new Vector3(1.0f, 0.012f, 0.46f), StructureMaterial.Dyed, 93, StructureRole.Detail));
			}
			if (d.Fine)
			{
				for (int i = 0; i < 3; i++)
				{
					float tall = 0.03f + 0.025f * i;
					d.Add(StructureShapes.Cylinder(0.035f, 1.011f, 1.011f + tall, 6, StructureMaterial.Iron, 94 + i, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(bench.x - 0.35f + 0.1f * i, 0f, front - 0.22f))));
				}
				Scales(d, new Vector3(bench.x + 0.3f, 1.011f, front - 0.32f), 0.32f, 0.015f, 98);
			}
			LombardSign(d, new Vector3(-h.Width * 0.5f + 0.7f, floor + 2.75f, front), 70);
		}

		/// <summary>
		/// The strongroom across the back of a counting hall: a stone cross wall to the eaves, a vault door on its face (an iron
		/// leaf in a stone surround, strapped and locked), and the hall's room drawn in to stop at its leaf.
		/// </summary>
		private static void Strongroom(StructureDraft d, House h, float floor, int key)
		{
			const float vault = 2.2f, cross = 0.5f, leafW = 1.1f, leafH = 2.1f, leaf = 0.08f;
			float innerBack = h.Depth * 0.5f - h.Wall;
			float z = innerBack - vault - cross * 0.5f;
			float face = z - cross * 0.5f;
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, floor + h.WallHeight * 0.5f, z), new Vector3(h.Width - 2f * h.Wall + 0.02f, h.WallHeight, cross), StructureMaterial.Stone, key));
			}
			const float x = 1.6f;
			d.Add(StructureShapes.Box(new Vector3(x, floor + leafH * 0.5f, face - leaf * 0.5f), new Vector3(leafW, leafH, leaf), StructureMaterial.Iron, key + 1, StructureRole.Detail));
			if (d.Mid)
			{
				// The surround, a dressed lintel and jambs proud of the wall.
				d.Add(StructureShapes.Box(new Vector3(x, floor + leafH + 0.13f, face - 0.06f), new Vector3(leafW + 0.5f, 0.26f, 0.12f), StructureMaterial.Stone, key + 2, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(x + s * (leafW * 0.5f + 0.12f), floor + leafH * 0.5f, face - 0.05f), new Vector3(0.24f, leafH, 0.1f), StructureMaterial.Stone,
						key + 3 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			if (d.Fine)
			{
				// Strap hinges across the leaf, a lock plate and its ring.
				for (int i = 0; i < 3; i++)
				{
					float y = floor + 0.35f + i * 0.7f;
					d.Add(StructureShapes.Box(new Vector3(x - 0.1f, y, face - leaf - 0.015f), new Vector3(leafW - 0.3f, 0.09f, 0.03f), StructureMaterial.Iron, key + 5 + i, StructureRole.Detail));
				}
				d.Add(StructureShapes.Box(new Vector3(x + leafW * 0.5f - 0.18f, floor + 1.05f, face - leaf - 0.02f), new Vector3(0.18f, 0.26f, 0.04f), StructureMaterial.Iron, key + 8, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.07f, -0.015f, 0.015f, 8, StructureMaterial.Iron, key + 9, false, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(x + leafW * 0.5f - 0.18f, floor + 0.9f, face - leaf - 0.05f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
			// The hall stops at the leaf.
			for (int i = d.Rooms.Count - 1; i >= 0; i--)
			{
				StructureRoom room = d.Rooms[i];
				if (room.Kind != StructureRoomKind.CountingHouse)
				{
					continue;
				}
				float near = room.Centre.z - room.Size.y * 0.5f, far = face - leaf - 0.02f;
				room.Centre.z = (near + far) * 0.5f;
				room.Size.y = far - near;
				d.Rooms[i] = room;
				// Nothing stood against the vault door.
				if (i == d.Rooms.Count - 1)
				{
					d.RoomClear(new Vector2(x - leafW * 0.5f - 0.3f, far - 1f), new Vector2(x + leafW * 0.5f + 0.3f, far));
				}
				break;
			}
		}

		/// <summary>
		/// Iron bars set in the reveal of every window pane laid since <paramref name="first"/> whose middle is under
		/// <paramref name="below"/> (the ground floor's): three uprights and two rails between the pane and the wall's face.
		/// </summary>
		private static void BarWindows(StructureDraft d, int first, float below, float wall, int key)
		{
			int end = d.Solids.Count, n = 0;
			for (int i = first; i < end; i++)
			{
				StructureSolid pane = d.Solids[i];
				if (pane.Material != StructureMaterial.Glass)
				{
					continue;
				}
				Bounds b = pane.Bounds;
				if (b.center.y > below)
				{
					continue;
				}
				bool facesZ = b.size.z < b.size.x;
				Vector3 outward = facesZ ? new Vector3(0f, 0f, Mathf.Sign(b.center.z)) : new Vector3(Mathf.Sign(b.center.x), 0f, 0f);
				Vector3 along = facesZ ? Vector3.right : Vector3.forward;
				Vector3 at = b.center + outward * (wall * 0.25f);
				const float bar = 0.035f;
				Vector3 Size(Vector3 x, float y) => new Vector3(Mathf.Abs(x.x) + Mathf.Abs(outward.x) * bar, y, Mathf.Abs(x.z) + Mathf.Abs(outward.z) * bar);
				for (int j = -1; j <= 1; j++)
				{
					if (j != 0 && !d.Mid)
					{
						continue;
					}
					d.Add(StructureShapes.Box(at + along * (j * WindowWidth * 0.28f), Size(along * bar, WindowHeight + 0.06f), StructureMaterial.Iron, key + n++, StructureRole.Detail));
				}
				if (d.Mid)
				{
					for (int j = -1; j <= 1; j += 2)
					{
						d.Add(StructureShapes.Box(at + Vector3.up * (j * WindowHeight * 0.22f), Size(along * (WindowWidth + 0.06f), bar), StructureMaterial.Iron, key + n++, StructureRole.Detail));
					}
				}
			}
		}

		/// <summary>
		/// The lombard's sign: an iron arm out from the wall at <paramref name="at"/> (on the wall's face, the street at −z) and
		/// the three gilt balls hung from a bar on its end, two over one.
		/// </summary>
		private static void LombardSign(StructureDraft d, Vector3 at, int key)
		{
			const float reach = 0.95f;
			Vector3 end = at + Vector3.back * reach;
			d.Add(StructureShapes.Box(at + new Vector3(0f, -0.1f, -0.01f), new Vector3(0.14f, 0.5f, 0.02f), StructureMaterial.Iron, key));
			d.Add(StructureShapes.Beam(at, end, 0.04f, 0.04f, StructureMaterial.Iron, key + 1));
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(at + Vector3.down * 0.32f, Vector3.Lerp(at, end, 0.65f), 0.03f, 0.03f, StructureMaterial.Iron, key + 2, StructureRole.Detail));
			}
			// The bar, hung on a short rod from the arm's end.
			Vector3 bar = end + Vector3.down * 0.25f;
			d.Add(StructureShapes.Beam(end, bar, 0.025f, 0.025f, StructureMaterial.Iron, key + 3));
			d.Add(StructureShapes.Beam(bar + Vector3.left * 0.24f, bar + Vector3.right * 0.24f, 0.035f, 0.035f, StructureMaterial.Iron, key + 4));
			int seg = d.Seg(9, 5), rings = d.Fine ? 5 : 3;
			var balls = new[] { new Vector3(-0.2f, -0.24f, 0f), new Vector3(0.2f, -0.24f, 0f), new Vector3(0f, -0.56f, 0f) };
			for (int i = 0; i < balls.Length; i++)
			{
				Vector3 ball = bar + balls[i];
				Vector3 hook = i < 2 ? bar + new Vector3(balls[i].x, 0f, 0f) : bar;
				d.Add(StructureShapes.Beam(hook, ball + Vector3.up * 0.1f, 0.014f, 0.014f, StructureMaterial.Iron, key + 5 + i, StructureRole.Detail));
				d.Add(StructureShapes.Sphere(ball, new Vector3(0.13f, 0.13f, 0.13f), seg, rings, StructureMaterial.Bone, key + 10 + i));
			}
		}

		/// <summary>A small balance: a post, its beam and two pans, standing at <paramref name="foot"/>, <paramref name="tall"/> high.</summary>
		private static void Scales(StructureDraft d, Vector3 foot, float tall, float depth, int key)
		{
			d.Add(StructureShapes.Box(foot + new Vector3(0f, 0.015f, 0f), new Vector3(0.1f, 0.03f, Mathf.Max(0.06f, depth * 3f)), StructureMaterial.Iron, key, StructureRole.Detail));
			d.Add(StructureShapes.Box(foot + new Vector3(0f, tall * 0.5f, 0f), new Vector3(0.02f, tall, 0.02f), StructureMaterial.Iron, key + 1, StructureRole.Detail));
			float half = tall * 0.45f;
			d.Add(StructureShapes.Box(foot + new Vector3(0f, tall - 0.01f, 0f), new Vector3(half * 2f, 0.014f, 0.014f), StructureMaterial.Iron, key + 2, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 pan = foot + new Vector3(s * half, tall * 0.42f, 0f);
				d.Add(StructureShapes.Beam(foot + new Vector3(s * half, tall - 0.01f, 0f), pan, 0.006f, 0.006f, StructureMaterial.Iron, key + 3 + (s > 0 ? 1 : 0), StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(tall * 0.17f, -0.008f, 0.008f, 8, StructureMaterial.Iron, key + 5 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(pan)));
			}
		}

		// ── Observatory ───────────────────────────────────────────────

		/// <summary>The tower's outer radius at its foot and at its platform, its ground storey's wall, floor and ceiling, and the platform's height.</summary>
		private const float ObservatoryFoot = 4f, ObservatoryCrown = 3.6f, ObservatoryWall = 0.8f, ObservatoryFloor = 0.2f, ObservatoryStorey = 3.8f, ObservatoryDeck = 12f;

		private static void Observatory(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				ObservatoryHall(d);
				return;
			}
			float r0 = ObservatoryFoot, r1 = ObservatoryCrown, inner = r0 - ObservatoryWall, deck = ObservatoryDeck;
			int seg = d.Seg(24, 12);
			// The footing, its top the room's floor: a facet's face, not an edge between two, square to the door.
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -FoundationDepth), new Vector2(r0 + 0.25f, -FoundationDepth), new Vector2(r0 + 0.25f, ObservatoryFloor - 0.04f),
					new Vector2(r0 + 0.15f, ObservatoryFloor), new Vector2(0f, ObservatoryFloor) }, seg, StructureMaterial.Fieldstone, 1, true, Mathf.PI / seg));
			}

			// The astronomer's room: the door at −z, a window either side toward the back, the ceiling, the room.
			const float doorWidth = 1.3f, doorHeight = 2.4f;
			float sill = ObservatoryFloor + 1.2f;
			var holes = new List<(float, float, float, float)>
			{
				(1.5f * Mathf.PI, doorWidth, ObservatoryFloor, ObservatoryFloor + doorHeight),
				(0.15f * Mathf.PI, WindowWidth, sill, sill + WindowHeight),
				(0.85f * Mathf.PI, WindowWidth, sill, sill + WindowHeight),
			};
			using (d.Builds(WallRoles))
			{
				RingWallWithOpenings(d, Vector3.zero, inner, r0, ObservatoryFloor, ObservatoryStorey, seg, 1.5f * Mathf.PI, holes, StructureMaterial.Stone, 10);
			}
			for (int i = 1; i < holes.Count; i++)
			{
				float a = holes[i].Item1;
				var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Window(d, outward * r0 + Vector3.up * (sill + WindowHeight * 0.5f), outward, ObservatoryWall, 300 + i * 20);
			}
			d.Add(StructureShapes.Cylinder(inner + 0.05f, ObservatoryStorey - 0.25f, ObservatoryStorey, seg, StructureMaterial.Timber, 2));
			HangDoor(d, new Vector3(0f, ObservatoryFloor, -Mathf.Sqrt(inner * inner - 0.25f * doorWidth * doorWidth) + 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight,
				StructureDoorKind.Studded);
			d.RoundRoom(new Vector3(0f, ObservatoryFloor, 0f), inner, ObservatoryStorey - ObservatoryFloor - 0.25f, StructureRoomKind.Observatory);
			DoorSurround(d, new Vector3(0f, ObservatoryFloor, -r0 - 0.03f), doorWidth, doorHeight, 20);

			// The tower, battered in to its platform, solid over the room (the platform is the astronomer's: no stair is built up
			// to it, so nothing a player can't reach stands behind glass); string courses where the storey meets it and halfway.
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(r0, ObservatoryStorey), new Vector2(r1, deck - 0.35f) }, seg, StructureMaterial.Stone, 3));
			}
			float mid = (ObservatoryStorey + deck) * 0.5f;
			foreach ((float y, int k) in new[] { (ObservatoryStorey, 4), (mid, 5) })
			{
				float r = Mathf.Lerp(r0, r1, (y - ObservatoryStorey) / (deck - 0.35f - ObservatoryStorey)) + 0.1f;
				d.Add(StructureShapes.Lathe(new[] { new Vector2(r, y - 0.12f), new Vector2(r, y + 0.12f) }, seg, StructureMaterial.Fieldstone, k));
			}
			// The platform, jettied out on corbels, and its parapet with merlons.
			float lip = r1 + 0.4f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(lip, deck - 0.35f), new Vector2(lip, deck), new Vector2(0f, deck) }, seg, StructureMaterial.Stone, 6));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(lip, deck), new Vector2(lip, deck + 1f), new Vector2(r1 - 0.05f, deck + 1f), new Vector2(r1 - 0.05f, deck) }, seg,
				StructureMaterial.Stone, 7, true, 0f, true));
			const int merlons = 12;
			for (int i = 0; i < merlons; i++)
			{
				float a = (i + 0.5f) * 2f * Mathf.PI / merlons;
				var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				d.Add(StructureShapes.Block(outward * ((lip + r1 - 0.05f) * 0.5f) + Vector3.up * (deck + 1.3f), new Vector3(0.75f, 0.6f, lip - r1 + 0.05f),
					Quaternion.LookRotation(outward), StructureMaterial.Stone, 30 + i, StructureRole.Detail));
			}
			if (d.Fine)
			{
				const int corbels = 12;
				for (int i = 0; i < corbels; i++)
				{
					float a = i * 2f * Mathf.PI / corbels;
					var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					d.Add(StructureShapes.Block(outward * (r1 + 0.18f) + Vector3.up * (deck - 0.6f), new Vector3(0.26f, 0.5f, 0.42f), Quaternion.LookRotation(outward),
						StructureMaterial.Stone, 50 + i, StructureRole.Detail));
				}
			}

			// The stair turret up the back (the astronomer's way up, solid), lit by slits, under a stone cone.
			var turret = new Vector3(0f, 0f, r0 + 0.35f);
			const float turretR = 1.25f;
			float turretTop = deck + 1.8f;
			using (d.Builds(WallRoles))
			{
				d.Add(StructureShapes.Cylinder(turretR, -FoundationDepth, turretTop, d.Seg(14, 8), StructureMaterial.Stone, 70).Transform(Matrix4x4.Translate(turret)));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(turretR + 0.2f, turretTop), new Vector2(0f, turretTop + 1.6f) }, d.Seg(14, 8), StructureMaterial.Stone, 71, false, 0f, false, false,
				StructureRole.Roof).Transform(Matrix4x4.Translate(turret)));
			if (d.Mid)
			{
				foreach ((float y, float a, int k) in new[] { (4.6f, 0.35f, 72), (7.6f, 0.65f, 73), (10.6f, 0.45f, 74) })
				{
					float t = a * Mathf.PI;
					var outward = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
					d.Add(StructureShapes.Block(turret + outward * (turretR - 0.02f) + Vector3.up * y, new Vector3(0.14f, 0.9f, 0.08f), Quaternion.LookRotation(outward),
						StructureMaterial.Ash, k, StructureRole.Detail));
				}
			}

			// The great telescope on its fork, turned toward the evening sky over the street, and the armillary by the parapet.
			Telescope(d, new Vector3(0f, deck, 0f), 215f, 40f, 2.4f, 4.6f, 0.42f, 1.1f, 2.5f, 100);
			float aa = -0.2f * Mathf.PI;
			Armillary(d, new Vector3(Mathf.Cos(aa) * 2.5f, deck, Mathf.Sin(aa) * 2.5f), 0.5f, 200);
		}

		/// <summary>
		/// The observatory's hall (v1): a stone hall whose flat roof is the terrace the great telescope stands on, turned on its
		/// turntable, a gantry by its eyepiece with a short ladder up; the astronomer's room is the hall. The terrace is the
		/// astronomer's alone (no stair up to it is built).
		/// </summary>
		private static void ObservatoryHall(StructureDraft d)
		{
			const float width = 10f, depth = 9f, wall = 0.6f, plinth = 0.3f, storey = 3.9f, slab = 0.3f, parapet = 1f;
			const float doorWidth = 1.4f, doorHeight = 2.4f;
			float top = plinth + storey, deck = top + slab;
			float hw = width * 0.5f, hd = depth * 0.5f, t = wall * 0.5f;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (plinth - FoundationDepth) * 0.5f, 0f), new Vector3(width + 0.3f, plinth + FoundationDepth, depth + 0.3f), d.Bevel(0.04f),
					StructureMaterial.Fieldstone, 1));
			}
			float wy = plinth + 1.65f;
			WallOpening Hole(float along) => new WallOpening { Along = along, Width = WindowWidth, Bottom = wy - WindowHeight * 0.5f, Top = wy + WindowHeight * 0.5f };
			var frontHoles = new List<WallOpening>
			{
				new WallOpening { Along = hw, Width = doorWidth, Bottom = plinth - 0.01f, Top = plinth + doorHeight },
				Hole(hw - 3f), Hole(hw + 3f),
			};
			var backHoles = new List<WallOpening> { Hole(hw - 2.5f), Hole(hw + 2.5f) };
			float side = depth - 2f * wall;
			var leftHoles = new List<WallOpening> { Hole(side * 0.5f) };
			var rightHoles = new List<WallOpening> { Hole(side * 0.5f) };
			using (d.Builds(WallRoles))
			{
				WallRun(d, new Vector3(-hw, 0f, -hd + t), new Vector3(hw, 0f, -hd + t), wall, plinth, top, frontHoles, StructureMaterial.Stone, 2);
				WallRun(d, new Vector3(-hw, 0f, hd - t), new Vector3(hw, 0f, hd - t), wall, plinth, top, backHoles, StructureMaterial.Stone, 3);
				WallRun(d, new Vector3(-hw + t, 0f, -hd + wall), new Vector3(-hw + t, 0f, hd - wall), wall, plinth, top, leftHoles, StructureMaterial.Stone, 4);
				WallRun(d, new Vector3(hw - t, 0f, -hd + wall), new Vector3(hw - t, 0f, hd - wall), wall, plinth, top, rightHoles, StructureMaterial.Stone, 5);
				// The terrace's parapet, the walls run up past the roof.
				WallRun(d, new Vector3(-hw, 0f, -hd + 0.225f), new Vector3(hw, 0f, -hd + 0.225f), 0.45f, deck, deck + parapet, null, StructureMaterial.Stone, 6);
				WallRun(d, new Vector3(-hw, 0f, hd - 0.225f), new Vector3(hw, 0f, hd - 0.225f), 0.45f, deck, deck + parapet, null, StructureMaterial.Stone, 7);
				WallRun(d, new Vector3(-hw + 0.225f, 0f, -hd + 0.45f), new Vector3(-hw + 0.225f, 0f, hd - 0.45f), 0.45f, deck, deck + parapet, null, StructureMaterial.Stone, 8);
				WallRun(d, new Vector3(hw - 0.225f, 0f, -hd + 0.45f), new Vector3(hw - 0.225f, 0f, hd - 0.45f), 0.45f, deck, deck + parapet, null, StructureMaterial.Stone, 9);
			}
			// The roof the terrace is: a stone deck over the whole hall.
			d.Add(StructureShapes.Box(new Vector3(0f, top + slab * 0.5f, 0f), new Vector3(width, slab, depth), StructureMaterial.Stone, 10));
			if (d.Mid)
			{
				// A cornice where the walls end and the parapet's coping.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, top + 0.15f, s * (hd + 0.06f)), new Vector3(width + 0.24f, 0.24f, 0.12f), StructureMaterial.Stone, 11 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(s * (hw + 0.06f), top + 0.15f, 0f), new Vector3(0.12f, 0.24f, depth), StructureMaterial.Stone, 13 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0f, deck + parapet + 0.05f, s * (hd - 0.225f)), new Vector3(width + 0.1f, 0.1f, 0.6f), StructureMaterial.Stone, 15 + (s > 0 ? 1 : 0), StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(s * (hw - 0.225f), deck + parapet + 0.05f, 0f), new Vector3(0.6f, 0.1f, depth - 1f), StructureMaterial.Stone, 17 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			// The windows, the door and its steps, the room.
			int w = 0;
			foreach ((Vector3 at, Vector3 outward) in new[]
			{
				(new Vector3(-3f, wy, -hd), Vector3.back), (new Vector3(3f, wy, -hd), Vector3.back),
				(new Vector3(-2.5f, wy, hd), Vector3.forward), (new Vector3(2.5f, wy, hd), Vector3.forward),
				(new Vector3(-hw, wy, 0f), Vector3.left), (new Vector3(hw, wy, 0f), Vector3.right),
			})
			{
				Window(d, at, outward, wall, 300 + 10 * w++);
			}
			HangDoor(d, new Vector3(0f, plinth, -hd + wall - 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight, StructureDoorKind.Studded);
			Steps(d, new Vector3(0f, 0f, -hd - 0.12f), Vector3.back, Mathf.Max(doorWidth + 0.4f, StairWidth), plinth, StructureMaterial.Fieldstone, 20);
			d.Room(new Vector3(0f, plinth, 0f), new Vector2(width - 2f * wall, depth - 2f * wall), storey, StructureRoomKind.Observatory);
			DoorSurround(d, new Vector3(0f, plinth, -hd - 0.03f), doorWidth, doorHeight, 25);

			// The enormous telescope on the terrace, its turntable, gantry and ladder; an armillary at the front corner.
			Telescope(d, new Vector3(0.6f, deck, 0.4f), -50f, 36f, 1.6f, 8.2f, 0.56f, 1.9f, 3.3f, 100, true);
			Armillary(d, new Vector3(hw - 1.3f, deck, -hd + 1.3f), 0.55f, 200);
		}

		/// <summary>
		/// A door's dressed surround on a wall's face (<paramref name="foot"/>: the threshold's middle on the face, the street at −z):
		/// jambs and a lintel, and over it a carved star of rune-light (2026-10-19, the astronomer's mark) that glows at night.
		/// </summary>
		private static void DoorSurround(StructureDraft d, Vector3 foot, float width, float height, int key)
		{
			if (!d.Mid)
			{
				return;
			}
			// Each stone runs back into the wall, so it meets a round tower's face as well as a flat one.
			d.Add(StructureShapes.Box(foot + new Vector3(0f, height + 0.14f, 0.1f), new Vector3(width + 0.6f, 0.28f, 0.32f), StructureMaterial.Stone, key, StructureRole.Detail));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(foot + new Vector3(s * (width * 0.5f + 0.14f), height * 0.5f, 0.1f), new Vector3(0.26f, height, 0.3f), StructureMaterial.Stone,
					key + 1 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
			Vector3 star = foot + new Vector3(0f, height + 0.62f, -0.04f);
			d.Add(StructureShapes.Cylinder(0.3f, -0.06f, 0.25f, d.Seg(12, 8), StructureMaterial.Stone, key + 3, false, StructureRole.Detail)
				.Transform(Matrix4x4.TRS(star, Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			for (int i = 0; i < 4; i++)
			{
				float len = i % 2 == 0 ? 0.44f : 0.3f;
				d.Add(StructureShapes.Block(star + Vector3.back * 0.07f, new Vector3(len, 0.035f, 0.03f), Quaternion.Euler(0f, 0f, i * 45f), StructureMaterial.Runelight, key + 4 + i,
					StructureRole.Detail));
			}
			d.Emit(star + Vector3.back * 0.15f, SceneEmitterKind.Glow);
		}

		/// <summary>
		/// A great refractor on a fork mount standing on <paramref name="foot"/>: a turntable drum, two timber arms up to the
		/// trunnions at <paramref name="pivotHeight"/>, and the tube on them — octagonal timber staves bound in iron bands, its
		/// object glass in a brass cell at the high end and its eyepiece at the low — aimed <paramref name="yaw"/> degrees about +y
		/// from +z and <paramref name="tilt"/> up from level, reaching <paramref name="back"/> behind the trunnions and
		/// <paramref name="front"/> before them. A rune band round the turntable glows. With <paramref name="gantry"/>, a timber
		/// stage behind the eyepiece and a ladder up to it.
		/// </summary>
		private static void Telescope(StructureDraft d, Vector3 foot, float yaw, float tilt, float back, float front, float radius, float table, float pivotHeight, int key,
			bool gantry = false)
		{
			Quaternion aim = Quaternion.Euler(-tilt, yaw, 0f);
			Vector3 dir = aim * Vector3.forward, across = aim * Vector3.right;
			Vector3 level = new Vector3(dir.x, 0f, dir.z).normalized;
			Vector3 pivot = foot + Vector3.up * pivotHeight;
			Matrix4x4 tube = Matrix4x4.TRS(pivot, aim * Quaternion.Euler(90f, 0f, 0f), Vector3.one);
			int round = d.Seg(20, 10);

			// The turntable: a stone drum, the timber deck on it.
			d.Add(StructureShapes.Cylinder(table + 0.15f, -0.05f, 0.25f, round, StructureMaterial.Stone, key).Transform(Matrix4x4.Translate(foot)));
			d.Add(StructureShapes.Cylinder(table, 0.25f, 0.4f, round, StructureMaterial.Timber, key + 1).Transform(Matrix4x4.Translate(foot)));
			if (d.Mid)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(table + 0.17f, 0.1f), new Vector2(table + 0.17f, 0.18f), new Vector2(table + 0.13f, 0.18f), new Vector2(table + 0.13f, 0.1f) },
					round, StructureMaterial.Runelight, key + 2, true, 0f, true, false, StructureRole.Detail).Transform(Matrix4x4.Translate(foot)));
				d.Emit(foot + Vector3.up * 0.6f + across * (table + 0.3f), SceneEmitterKind.Glow);
			}

			// The fork: an arm up each side from the deck to a trunnion, braced.
			float spread = radius + 0.3f;
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 trunnion = pivot + across * (s * spread);
				Vector3 baseA = foot + Vector3.up * 0.4f + across * (s * spread) + level * 0.7f;
				Vector3 baseB = foot + Vector3.up * 0.4f + across * (s * spread) - level * 0.7f;
				d.Add(StructureShapes.Beam(baseA, trunnion, 0.2f, 0.24f, StructureMaterial.Timber, key + 3 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(baseB, trunnion, 0.2f, 0.24f, StructureMaterial.Timber, key + 5 + (s > 0 ? 1 : 0)));
				if (d.Mid)
				{
					Vector3 a = Vector3.Lerp(baseA, trunnion, 0.4f), b = Vector3.Lerp(baseB, trunnion, 0.4f);
					d.Add(StructureShapes.Beam(a, b, 0.12f, 0.12f, StructureMaterial.Timber, key + 7 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			d.Add(StructureShapes.Rod(pivot - across * (spread + 0.12f), pivot + across * (spread + 0.12f), 0.08f, d.Seg(8, 5), StructureMaterial.Iron, key + 9));

			// The tube: octagonal staves narrowing to the eyepiece, its bands, the object glass in its cell.
			float rb = radius * 0.72f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(rb, -back), new Vector2(radius * 0.96f, 0f), new Vector2(radius, front) }, d.Seg(8, 6), StructureMaterial.Timber, key + 10,
				false, Mathf.PI / 8f, false, true).Transform(tube));
			float RadiusAt(float y) => y < 0f ? Mathf.Lerp(radius * 0.96f, rb, -y / back) : Mathf.Lerp(radius * 0.96f, radius, y / front);
			var bands = new List<float> { -back + 0.12f, front * 0.45f, front - 0.7f };
			if (d.Mid)
			{
				bands.Add(-back * 0.45f);
				bands.Add(front * 0.18f);
			}
			if (d.Fine)
			{
				bands.Add(front * 0.72f);
			}
			for (int i = 0; i < bands.Count; i++)
			{
				float y = bands[i];
				d.Add(StructureShapes.Cylinder(RadiusAt(y) * 1.03f + 0.025f, y - 0.07f, y + 0.07f, d.Seg(8, 6), StructureMaterial.Iron, key + 11 + i, false, StructureRole.Detail)
					.Transform(tube));
			}
			d.Add(StructureShapes.Cylinder(radius * 1.12f, front - 0.45f, front + 0.06f, d.Seg(12, 6), StructureMaterial.Iron, key + 20).Transform(tube));
			d.Add(StructureShapes.Cylinder(radius * 0.9f, front + 0.06f, front + 0.09f, d.Seg(12, 6), StructureMaterial.Glass, key + 21, false, StructureRole.Detail).Transform(tube));
			// The eyepiece out of the low end, and a finder along the tube's flank.
			d.Add(StructureShapes.Cylinder(rb * 0.45f, -back - 0.35f, -back, d.Seg(8, 5), StructureMaterial.Iron, key + 22).Transform(tube));
			if (d.Fine)
			{
				Vector3 a = tube.MultiplyPoint3x4(new Vector3(radius + 0.12f, front * 0.1f, 0f)), b = tube.MultiplyPoint3x4(new Vector3(radius + 0.12f, front * 0.55f, 0f));
				d.Add(StructureShapes.Rod(a, b, 0.07f, 6, StructureMaterial.Iron, key + 23, StructureRole.Detail));
				for (int i = 0; i < 2; i++)
				{
					Vector3 at = Vector3.Lerp(a, b, 0.2f + 0.6f * i);
					d.Add(StructureShapes.Beam(at, tube.MultiplyPoint3x4(new Vector3(radius * 0.9f, Mathf.Lerp(front * 0.1f, front * 0.55f, 0.2f + 0.6f * i), 0f)), 0.03f, 0.03f,
						StructureMaterial.Iron, key + 24 + i, StructureRole.Detail));
				}
			}
			if (!gantry)
			{
				return;
			}

			// A counterweight hung under the short end, and the gantry: a timber stage behind the eyepiece on four posts, railed,
			// with a ladder up from the turntable.
			Vector3 low = tube.MultiplyPoint3x4(new Vector3(0f, -back * 0.6f, 0f));
			d.Add(StructureShapes.Beam(low, low + Vector3.down * 0.5f, 0.05f, 0.05f, StructureMaterial.Iron, key + 30));
			d.Add(StructureShapes.ChamferBox(low + Vector3.down * 0.75f, new Vector3(0.5f, 0.45f, 0.5f), d.Bevel(0.03f), StructureMaterial.Iron, key + 31));
			Vector3 eye = tube.MultiplyPoint3x4(new Vector3(0f, -back - 0.35f, 0f));
			float stage = Mathf.Max(foot.y + 0.9f, eye.y - 1.55f);
			Vector3 c = new Vector3(eye.x, 0f, eye.z) - level * 0.85f;
			Quaternion turn = Quaternion.LookRotation(level);
			Vector3 right = turn * Vector3.right;
			const float sw = 1.5f, sd = 1.1f;
			d.Add(StructureShapes.Block(new Vector3(c.x, stage - 0.05f, c.z), new Vector3(sw, 0.1f, sd), turn, StructureMaterial.Timber, key + 32));
			int n = 0;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					Vector3 p = c + right * (sx * (sw * 0.5f - 0.06f)) + level * (sz * (sd * 0.5f - 0.06f));
					float postTop = sz < 0 ? stage + 1f : stage;
					d.Add(StructureShapes.Beam(new Vector3(p.x, foot.y, p.z), new Vector3(p.x, postTop, p.z), 0.1f, 0.1f, StructureMaterial.Timber, key + 33 + n++));
				}
			}
			if (d.Mid)
			{
				// The rail along the back and the sides of the stage (its front is the eyepiece's).
				Vector3 b0 = c - level * (sd * 0.5f - 0.06f);
				d.Add(StructureShapes.Beam(new Vector3(b0.x, stage + 0.95f, b0.z) - right * (sw * 0.5f), new Vector3(b0.x, stage + 0.95f, b0.z) + right * (sw * 0.5f), 0.06f, 0.06f,
					StructureMaterial.Timber, key + 38, StructureRole.Detail));
			}
			// The ladder up the stage's side.
			Vector3 lf = c + right * (sw * 0.5f + 0.3f) + Vector3.up * (foot.y + 0.4f);
			Vector3 lt = c + right * (sw * 0.5f + 0.02f) + Vector3.up * (stage + 0.9f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(lf + level * (s * 0.24f), lt + level * (s * 0.24f), 0.05f, 0.07f, StructureMaterial.Timber, key + 39 + (s > 0 ? 1 : 0)));
			}
			if (d.Mid)
			{
				int rungs = Mathf.Max(2, Mathf.RoundToInt((stage + 0.9f - foot.y - 0.4f) / 0.33f) - 1);
				for (int i = 1; i <= rungs; i++)
				{
					Vector3 r = Vector3.Lerp(lf, lt, i / (rungs + 1f));
					d.Add(StructureShapes.Beam(r - level * 0.24f, r + level * 0.24f, 0.035f, 0.035f, StructureMaterial.Timber, key + 41 + i, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// An armillary sphere standing on <paramref name="foot"/>: a stone pedestal and its iron stem, the meridian ring upright,
		/// the equator round the pole (tilted to a middling latitude) and the ecliptic's band 23.5° from it, a small globe at the
		/// heart.
		/// </summary>
		private static void Armillary(StructureDraft d, Vector3 foot, float radius, int key)
		{
			const float pedestal = 1.05f;
			int seg = d.Seg(10, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.32f, -0.05f), new Vector2(0.32f, 0.12f), new Vector2(0.17f, 0.25f), new Vector2(0.14f, pedestal - 0.12f),
				new Vector2(0.26f, pedestal - 0.04f), new Vector2(0.26f, pedestal), new Vector2(0f, pedestal) }, seg, StructureMaterial.Stone, key).Transform(Matrix4x4.Translate(foot)));
			Vector3 centre = foot + Vector3.up * (pedestal + radius + 0.12f);
			d.Add(StructureShapes.Beam(foot + Vector3.up * pedestal, centre - Vector3.up * radius * 0.98f, 0.05f, 0.05f, StructureMaterial.Iron, key + 1));
			Vector3 pole = Quaternion.AngleAxis(40f, Vector3.right) * Vector3.up;
			Ring(d, centre, radius, Vector3.forward, 0.035f, key + 2);
			if (d.Mid)
			{
				Ring(d, centre, radius * 0.94f, pole, 0.03f, key + 3);
				Ring(d, centre, radius * 0.9f, Quaternion.AngleAxis(23.5f, Vector3.forward) * pole, 0.045f, key + 4);
			}
			d.Add(StructureShapes.Beam(centre - pole * radius * 1.1f, centre + pole * radius * 1.1f, 0.025f, 0.025f, StructureMaterial.Iron, key + 5));
			d.Add(StructureShapes.Sphere(centre, Vector3.one * radius * 0.18f, d.Seg(8, 5), d.Fine ? 4 : 3, StructureMaterial.Plaster, key + 6));
		}

		/// <summary>An iron ring about <paramref name="axis"/>: a turned annulus, square in section, <paramref name="half"/> either way of its line.</summary>
		private static void Ring(StructureDraft d, Vector3 centre, float radius, Vector3 axis, float half, int key)
		{
			d.Add(StructureShapes.Lathe(new[] { new Vector2(radius + half, -half), new Vector2(radius + half, half), new Vector2(radius - half, half), new Vector2(radius - half, -half) },
				d.Seg(16, 8), StructureMaterial.Iron, key, true, 0f, true).Transform(Matrix4x4.TRS(centre, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one)));
		}

		// ── Dig ───────────────────────────────────────────────────────

		/// <summary>
		/// A bank of earth on the ground: the box from <paramref name="min"/> (its foot under the ground line) to
		/// <paramref name="max"/> (its top), each side sloping out from the top by its run (−x, +x, −z, +z; 0 stands sheer, as a
		/// trench's side cut back to its shoring).
		/// </summary>
		private static StructureSolid Bank(Vector3 min, Vector3 max, Vector4 runs, StructureMaterial material, int key)
		{
			StructureSolid s = StructureShapes.Box((min + max) * 0.5f, max - min, material, key);
			float h = max.y;
			void Slope(Vector3 outward, float run, float face)
			{
				if (run <= 1e-3f)
				{
					return;
				}
				Vector3 n = outward + Vector3.up * (run / h);
				float m = n.magnitude;
				s.Clip(n / m, face / m);
			}
			Slope(Vector3.left, runs.x, -min.x);
			Slope(Vector3.right, runs.y, max.x);
			Slope(Vector3.back, runs.z, -min.z);
			Slope(Vector3.forward, runs.w, max.z);
			return s;
		}

		private static void DigSite(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				OpenDig(d);
				return;
			}
			// The trench (x ±3.2, z ±1.4) cut through a mound 1.4 m high: banks round it, a working floor on the front one.
			const float tx = 3.2f, tz = 1.4f, mound = 1.4f, run = 2.6f, sideTop = 1f, frontTop = 2.2f, backTop = 1.2f;
			float ox = tx + sideTop + run, front = -(tz + frontTop + run), back = tz + backTop + run;
			d.Add(Bank(new Vector3(-ox, -0.3f, front), new Vector3(ox, mound, -tz), new Vector4(run, run, run, 0f), StructureMaterial.Earth, 1));
			d.Add(Bank(new Vector3(-ox, -0.3f, tz), new Vector3(ox, mound, back), new Vector4(run, run, 0f, run), StructureMaterial.Earth, 2));
			d.Add(Bank(new Vector3(-ox, -0.3f, -tz), new Vector3(-tx, mound, tz), new Vector4(run, 0f, 0f, 0f), StructureMaterial.Earth, 3));
			d.Add(Bank(new Vector3(tx, -0.3f, -tz), new Vector3(ox, mound, tz), new Vector4(0f, run, 0f, 0f), StructureMaterial.Earth, 4));
			// The trench's floor, a hand over the ground line, and the steps cut down into it at the +x end.
			d.Add(StructureShapes.Box(new Vector3(0f, -0.125f, 0f), new Vector3(2f * tx, 0.35f, 2f * tz), StructureMaterial.Earth, 5));
			const float floor = 0.05f;
			Steps(d, new Vector3(tx, 0f, 0f), Vector3.left, 2f * tz - 0.2f, mound, StructureMaterial.Earth, 6, floor);

			// The shoring: boards along both long sides and the far end, on posts.
			int k = 20;
			for (int s = -1; s <= 1; s += 2)
			{
				float z = s * (tz - 0.03f);
				for (int b = 0; b < (d.Mid ? 3 : 1); b++)
				{
					float y = floor + 0.25f + b * 0.42f;
					d.Add(StructureShapes.Box(new Vector3(-0.6f, y, z), new Vector3(2f * tx - 1.2f, 0.3f, 0.05f), StructureMaterial.Timber, k++, StructureRole.Detail)).Grain = Vector3.right;
				}
				foreach (float x in new[] { -tx + 0.1f, -1.2f, 0.9f })
				{
					d.Add(StructureShapes.Box(new Vector3(x, (floor + mound + 0.15f) * 0.5f, z - s * 0.06f), new Vector3(0.12f, mound + 0.15f - floor, 0.1f), StructureMaterial.Timber, k++));
				}
			}
			for (int b = 0; b < (d.Mid ? 3 : 1); b++)
			{
				d.Add(StructureShapes.Box(new Vector3(-tx + 0.03f, floor + 0.25f + b * 0.42f, 0f), new Vector3(0.05f, 0.3f, 2f * tz - 0.1f), StructureMaterial.Timber, k++, StructureRole.Detail))
					.Grain = Vector3.forward;
			}
			if (d.Fine)
			{
				// Props across the trench between the posts' heads.
				foreach (float x in new[] { -1.2f, 0.9f })
				{
					d.Add(StructureShapes.Rod(new Vector3(x, mound - 0.12f, -tz + 0.1f), new Vector3(x, mound - 0.12f, tz - 0.1f), 0.06f, 6, StructureMaterial.Timber, k++, StructureRole.Detail));
				}
			}

			// What the trench has found: an old wall's footing across its floor and a pot coming out of the side.
			if (d.Mid)
			{
				for (int i = 0; i < 3; i++)
				{
					d.Add(RoughStone(d, new Vector3(-2.2f + i * 0.55f, floor, -0.5f + i * 0.12f), new Vector3(0.5f, 0.32f, 0.42f), i * 23f, 40 + i, 2, StructureMaterial.Fieldstone, StructureRole.Detail));
				}
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.1f, 0f), new Vector2(0.17f, 0.12f), new Vector2(0.12f, 0.26f), new Vector2(0.07f, 0.3f), new Vector2(0f, 0.3f) },
					d.Seg(8, 5), StructureMaterial.Earth, 43, true, 0f, false, false, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(new Vector3(-0.4f, floor + 0.55f, tz - 0.2f), Quaternion.Euler(-70f, 0f, 0f), Vector3.one)));
			}

			// The ladder at the far end, leant up over the lip.
			Vector3 l0 = new Vector3(-tx + 0.55f, floor, 0.3f), l1 = new Vector3(-tx - 0.05f, mound + 0.7f, 0.3f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(l0 + Vector3.forward * (s * 0.24f), l1 + Vector3.forward * (s * 0.24f), 0.05f, 0.07f, StructureMaterial.Timber, 50 + (s > 0 ? 1 : 0)));
			}
			if (d.Mid)
			{
				for (int i = 1; i <= 5; i++)
				{
					Vector3 r = Vector3.Lerp(l0, l1, i / 6f);
					d.Add(StructureShapes.Beam(r - Vector3.forward * 0.24f, r + Vector3.forward * 0.24f, 0.035f, 0.035f, StructureMaterial.Timber, 52 + i, StructureRole.Detail));
				}
			}

			// The windlass across the trench, its rope down to a bucket on the floor.
			const float wx = -1.9f, wy = mound + 0.95f;
			for (int s = -1; s <= 1; s += 2)
			{
				float z = s * (tz + 0.35f);
				d.Add(StructureShapes.Beam(new Vector3(wx - 0.35f, mound, z), new Vector3(wx, wy + 0.05f, z), 0.1f, 0.1f, StructureMaterial.Timber, 60 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(wx + 0.35f, mound, z), new Vector3(wx, wy + 0.05f, z), 0.1f, 0.1f, StructureMaterial.Timber, 62 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Rod(new Vector3(wx, wy, -tz - 0.5f), new Vector3(wx, wy, tz + 0.5f), 0.08f, d.Seg(8, 5), StructureMaterial.Timber, 64));
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(new Vector3(wx, wy, tz + 0.5f), new Vector3(wx, wy - 0.3f, tz + 0.55f), 0.04f, 0.04f, StructureMaterial.Iron, 65, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(wx, wy - 0.3f, tz + 0.55f), new Vector3(wx, wy - 0.3f, tz + 0.75f), 0.03f, 0.03f, StructureMaterial.Iron, 66, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(wx + 0.08f, wy - 0.05f, 0.75f), new Vector3(wx + 0.08f, floor + 0.32f, 0.75f), 0.02f, 0.02f, StructureMaterial.Canvas, 67, StructureRole.Detail));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.15f, 0f), new Vector2(0.19f, 0.3f), new Vector2(0f, 0.3f) }, d.Seg(8, 5), StructureMaterial.Timber, 68)
				.Transform(Matrix4x4.Translate(new Vector3(wx + 0.08f, floor, 0.75f))));

			// The sieve on its trestles on the front bank's working floor, its spoil under it.
			Sieve(d, new Vector3(1.6f, mound, -tz - 1.2f), 70);
			d.Add(StructureShapes.Sphere(new Vector3(1.6f, mound - 0.05f, -tz - 1.2f), new Vector3(0.6f, 0.22f, 0.4f), d.Seg(8, 5), 3, StructureMaterial.Earth, 79));

			// The tarp on its poles over the finds: trays and crates under it.
			float tarpX0 = -3.6f, tarpX1 = -1.0f, tarpZ0 = -tz - 2.0f, tarpZ1 = -tz - 0.35f;
			for (int sx = 0; sx < 2; sx++)
			{
				for (int sz = 0; sz < 2; sz++)
				{
					float x = sx == 0 ? tarpX0 : tarpX1, z = sz == 0 ? tarpZ0 : tarpZ1, h = sz == 0 ? 1.9f : 2.3f;
					d.Add(StructureShapes.Rod(new Vector3(x, mound - 0.1f, z), new Vector3(x, mound + h, z), 0.04f, d.Seg(6, 4), StructureMaterial.Timber, 80 + sx * 2 + sz));
				}
			}
			Vector3 lowEdge = new Vector3((tarpX0 + tarpX1) * 0.5f, mound + 1.9f, tarpZ0 - 0.15f), highEdge = new Vector3(lowEdge.x, mound + 2.3f, tarpZ1 + 0.15f);
			float slope = Mathf.Atan2(highEdge.y - lowEdge.y, highEdge.z - lowEdge.z) * Mathf.Rad2Deg;
			d.Add(StructureShapes.Block((lowEdge + highEdge) * 0.5f + Vector3.up * 0.03f, new Vector3(tarpX1 - tarpX0 + 0.3f, 0.03f, (highEdge - lowEdge).magnitude),
				Quaternion.Euler(-slope, 0f, 0f), StructureMaterial.Canvas, 84, StructureRole.Roof));
			d.Add(StructureShapes.Box(new Vector3(-3.1f, mound + 0.3f, tarpZ0 + 0.45f), new Vector3(0.6f, 0.6f, 0.6f), StructureMaterial.Timber, 85)).Grain = Vector3.right;
			FindsTray(d, new Vector3(-2.2f, mound, tarpZ0 + 0.5f), 0f, 86);
			FindsTray(d, new Vector3(-1.6f, mound, tarpZ0 + 0.85f), 20f, 92);

			// Ranging poles at the trench's corners, their string grid over it.
			RangingPoles(d, new[] { new Vector3(-tx - 0.25f, mound, -tz - 0.25f), new Vector3(tx + 0.25f, mound, -tz - 0.25f), new Vector3(tx + 0.25f, mound, tz + 0.25f),
				new Vector3(-tx - 0.25f, mound, tz + 0.25f) }, 1.8f, 100);

			// Spoil heaps at the mound's back corners.
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Sphere(new Vector3(s * 4.6f, -0.15f, back - 0.6f), new Vector3(1.5f, 0.85f, 1.0f), d.Seg(9, 6), 4, StructureMaterial.Earth, 120 + (s > 0 ? 1 : 0)));
				}
			}
		}

		/// <summary>
		/// The open-area dig (v1): a rectangle of ancient floor laid bare between low spoil banks (an opening at the front to
		/// barrow the spoil out), a broken column on its base and its fallen drums, a statue half out of a heap of earth, a finds
		/// tent over a table of trays, ranging poles and their strings.
		/// </summary>
		private static void OpenDig(StructureDraft d)
		{
			const float ax = 3.6f, az = 2.6f, bank = 0.6f, top = 0.6f, run = 1.1f, gap = 0.9f;
			float ox = ax + top + run, oz = az + top + run;
			d.Add(Bank(new Vector3(-ox, -0.3f, -oz), new Vector3(-gap, bank, -az), new Vector4(run, 0.35f, run, 0.3f), StructureMaterial.Earth, 1));
			d.Add(Bank(new Vector3(gap, -0.3f, -oz), new Vector3(ox, bank, -az), new Vector4(0.35f, run, run, 0.3f), StructureMaterial.Earth, 2));
			d.Add(Bank(new Vector3(-ox, -0.3f, az), new Vector3(ox, bank, oz), new Vector4(run, run, 0.3f, run), StructureMaterial.Earth, 3));
			d.Add(Bank(new Vector3(-ox, -0.3f, -az), new Vector3(-ax, bank, az), new Vector4(run, 0.3f, 0f, 0f), StructureMaterial.Earth, 4));
			d.Add(Bank(new Vector3(ax, -0.3f, -az), new Vector3(ox, bank, az), new Vector4(0.3f, run, 0f, 0f), StructureMaterial.Earth, 5));

			// The ancient floor: flagstones a metre square, some long gone, a little proud of the ground.
			int k = 10;
			if (d.Lod == LevelCount - 1)
			{
				d.Add(StructureShapes.Box(new Vector3(-0.9f, 0.02f, 0f), new Vector3(4f, 0.12f, 4f), StructureMaterial.Stone, k));
			}
			else
			{
				for (int ix = 0; ix < 4; ix++)
				{
					for (int iz = 0; iz < 4; iz++)
					{
						int key = k + ix * 4 + iz;
						if (d.Rand(key, 5) < 0.22f)
						{
							continue;
						}
						float lift = d.Range(key, 6, -0.03f, 0.03f);
						d.Add(StructureShapes.Box(new Vector3(-2.4f + ix, 0.02f + lift, -1.5f + iz), new Vector3(0.96f, 0.12f, 0.96f), StructureMaterial.Stone, key));
					}
				}
			}

			// The column, broken off on its base, and two of its drums fallen beside it.
			var col = new Vector3(2.3f, 0f, 1.2f);
			d.Add(StructureShapes.Box(col + new Vector3(0f, 0.1f, 0f), new Vector3(1f, 0.4f, 1f), StructureMaterial.Stone, 30));
			StructureSolid shaft = StructureShapes.Lathe(new[] { new Vector2(0.38f, 0.3f), new Vector2(0.38f, 0.42f), new Vector2(0.32f, 0.5f), new Vector2(0.3f, 1.9f) },
				d.Seg(12, 6), StructureMaterial.Stone, 31);
			shaft.Clip(new Vector3(0.45f, 1f, 0.2f).normalized, Vector3.Dot(new Vector3(0.45f, 1f, 0.2f).normalized, new Vector3(0f, 1.45f, 0f)));
			d.Add(shaft.Transform(Matrix4x4.Translate(col)));
			for (int i = 0; i < 2; i++)
			{
				var at = new Vector3(2.1f + i * 0.35f, 0.3f, -0.6f - i * 0.75f);
				d.Add(StructureShapes.Cylinder(0.3f, -0.32f, 0.32f, d.Seg(12, 6), StructureMaterial.Stone, 32 + i)
					.Transform(Matrix4x4.TRS(at, Quaternion.Euler(0f, 20f + i * 35f, 90f), Vector3.one)));
			}

			// The statue, lying back in the heap it is being dug out of: its torso and its head.
			var heap = new Vector3(-1.3f, 0f, 1.0f);
			d.Add(StructureShapes.Sphere(heap + Vector3.down * 0.15f, new Vector3(1.0f, 0.55f, 0.8f), d.Seg(9, 6), 4, StructureMaterial.Earth, 40));
			Quaternion lie = Quaternion.Euler(-62f, 25f, 0f);
			d.Add(StructureShapes.Block(heap + new Vector3(0.1f, 0.42f, -0.35f), new Vector3(0.62f, 0.85f, 0.34f), lie, StructureMaterial.Monolith, 41));
			d.Add(StructureShapes.Sphere(heap + new Vector3(0.24f, 0.62f, -0.85f), new Vector3(0.17f, 0.2f, 0.17f), d.Seg(8, 5), d.Fine ? 4 : 3, StructureMaterial.Monolith, 42));
			if (d.Mid)
			{
				d.Add(StructureShapes.Block(heap + new Vector3(-0.32f, 0.35f, -0.45f), new Vector3(0.16f, 0.6f, 0.16f), lie * Quaternion.Euler(0f, 0f, 14f), StructureMaterial.Monolith, 43, StructureRole.Detail));
			}

			// The finds tent: a ridge on two poles, its canvas sloping down either side, a trestle table of trays under it.
			var tent = new Vector3(-2.1f, 0f, -1.3f);
			const float half = 1.35f, ridge = 2.3f, eave = 1.45f, reach = 1.1f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Rod(tent + new Vector3(s * half, -0.1f, 0f), tent + new Vector3(s * half, ridge, 0f), 0.04f, d.Seg(6, 4), StructureMaterial.Timber, 50 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Rod(tent + new Vector3(-half - 0.1f, ridge, 0f), tent + new Vector3(half + 0.1f, ridge, 0f), 0.035f, d.Seg(6, 4), StructureMaterial.Timber, 52));
			float slopeLen = Mathf.Sqrt(reach * reach + (ridge - eave) * (ridge - eave));
			float pitch = Mathf.Atan2(ridge - eave, reach) * Mathf.Rad2Deg;
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 c = tent + new Vector3(0f, (ridge + eave) * 0.5f + 0.03f, s * reach * 0.5f);
				d.Add(StructureShapes.Block(c, new Vector3(2f * half + 0.3f, 0.03f, slopeLen), Quaternion.Euler(s * pitch, 0f, 0f), StructureMaterial.Canvas, 53 + (s > 0 ? 1 : 0),
					StructureRole.Roof));
				if (d.Fine)
				{
					for (int e = -1; e <= 1; e += 2)
					{
						Vector3 corner = tent + new Vector3(e * (half + 0.1f), eave, s * reach);
						d.Add(StructureShapes.Beam(corner, corner + new Vector3(e * 0.3f, -eave + 0.02f, s * 0.6f), 0.012f, 0.012f, StructureMaterial.Canvas, 55 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0),
							StructureRole.Detail));
					}
				}
			}
			TrestleTable(d, tent, 1.6f, 0.7f, 60);
			FindsTray(d, tent + new Vector3(-0.4f, 0.78f, 0f), 5f, 70);
			FindsTray(d, tent + new Vector3(0.35f, 0.78f, 0.05f), -8f, 76);

			// Ranging poles at two corners of the floor, and its strings.
			RangingPoles(d, new[] { new Vector3(-3.1f, 0.08f, -2.2f), new Vector3(1.3f, 0.08f, -2.2f), new Vector3(1.3f, 0.08f, 2.2f) }, 1.6f, 90);

			// A wheelbarrow of spoil at the gap in the front bank.
			if (d.Mid)
			{
				Wheelbarrow(d, new Vector3(0f, 0f, -az - 0.6f), 160f, 110);
			}
		}

		/// <summary>A sieve on two A-trestles (its frame tilted to shake the spoil down its mesh), standing on <paramref name="foot"/>.</summary>
		private static void Sieve(StructureDraft d, Vector3 foot, int key)
		{
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 c = foot + Vector3.right * (s * 0.6f);
				float h = s < 0 ? 0.85f : 0.7f;
				d.Add(StructureShapes.Beam(c + new Vector3(0f, -0.05f, -0.35f), c + new Vector3(0f, h, 0f), 0.06f, 0.06f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(c + new Vector3(0f, -0.05f, 0.35f), c + new Vector3(0f, h, 0f), 0.06f, 0.06f, StructureMaterial.Timber, key + 2 + (s > 0 ? 1 : 0)));
			}
			Vector3 frame = foot + new Vector3(0f, 0.86f, 0f);
			Quaternion tilt = Quaternion.Euler(0f, 0f, -7f);
			d.Add(StructureShapes.Block(frame, new Vector3(1.6f, 0.03f, 0.8f), tilt, StructureMaterial.Iron, key + 4, StructureRole.Detail));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Block(frame + tilt * new Vector3(0f, 0.05f, s * 0.42f), new Vector3(1.7f, 0.12f, 0.05f), tilt, StructureMaterial.Timber, key + 5 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Block(frame + tilt * new Vector3(s * 0.82f, 0.05f, 0f), new Vector3(0.05f, 0.12f, 0.8f), tilt, StructureMaterial.Timber, key + 7 + (s > 0 ? 1 : 0)));
				}
			}
		}

		/// <summary>A shallow finds tray on <paramref name="foot"/>, turned <paramref name="yaw"/>, and the sherds and a bone in it.</summary>
		private static void FindsTray(StructureDraft d, Vector3 foot, float yaw, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(foot, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
			d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.55f, 0.08f, 0.38f), StructureMaterial.Timber, key).Transform(m));
			if (!d.Fine)
			{
				return;
			}
			for (int i = 0; i < 3; i++)
			{
				d.Add(StructureShapes.Block(new Vector3(-0.16f + i * 0.15f, 0.09f, d.Range(key + i, 1, -0.08f, 0.08f)), new Vector3(0.09f, 0.02f, 0.07f), Quaternion.Euler(0f, d.Range(key + i, 2, 0f, 90f), 0f),
					StructureMaterial.Earth, key + 1 + i, StructureRole.Detail).Transform(m));
			}
			d.Add(StructureShapes.Rod(new Vector3(-0.15f, 0.095f, 0.11f), new Vector3(0.17f, 0.095f, 0.08f), 0.018f, 5, StructureMaterial.Bone, key + 4, StructureRole.Detail).Transform(m));
		}

		/// <summary>Ranging poles banded red, standing <paramref name="tall"/> on each foot given, and a string from each to the next at the ground.</summary>
		private static void RangingPoles(StructureDraft d, Vector3[] feet, float tall, int key)
		{
			for (int i = 0; i < feet.Length; i++)
			{
				Vector3 f = feet[i];
				d.Add(StructureShapes.Rod(f + Vector3.down * 0.2f, f + Vector3.up * tall, 0.025f, d.Seg(6, 4), StructureMaterial.Timber, key + i * 4));
				if (d.Mid)
				{
					for (int b = 0; b < 2; b++)
					{
						float y = tall * (0.25f + 0.5f * b);
						d.Add(StructureShapes.Cylinder(0.03f, y, y + tall * 0.2f, d.Seg(6, 4), StructureMaterial.Dyed, key + i * 4 + 1 + b, false, StructureRole.Detail)
							.Transform(Matrix4x4.Translate(f)));
					}
				}
				if (d.Fine && (i + 1 < feet.Length || feet.Length > 3))
				{
					Vector3 g = feet[(i + 1) % feet.Length];
					d.Add(StructureShapes.Beam(f + Vector3.up * 0.12f, g + Vector3.up * 0.12f, 0.01f, 0.01f, StructureMaterial.Canvas, key + i * 4 + 3, StructureRole.Detail));
				}
			}
		}

		/// <summary>A plank trestle table on <paramref name="foot"/>, its top at 0.78 m.</summary>
		private static void TrestleTable(StructureDraft d, Vector3 foot, float length, float width, int key)
		{
			const float top = 0.78f;
			d.Add(StructureShapes.Box(foot + new Vector3(0f, top - 0.03f, 0f), new Vector3(length, 0.06f, width), StructureMaterial.Timber, key)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 c = foot + Vector3.right * (s * (length * 0.5f - 0.2f));
				d.Add(StructureShapes.Beam(c + new Vector3(0f, 0f, -width * 0.42f), c + new Vector3(0f, top - 0.06f, 0f), 0.06f, 0.06f, StructureMaterial.Timber, key + 1 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(c + new Vector3(0f, 0f, width * 0.42f), c + new Vector3(0f, top - 0.06f, 0f), 0.06f, 0.06f, StructureMaterial.Timber, key + 3 + (s > 0 ? 1 : 0)));
			}
		}

		/// <summary>A wheelbarrow on <paramref name="foot"/>, turned <paramref name="yaw"/> (its wheel toward +z at 0), heaped with spoil.</summary>
		private static void Wheelbarrow(StructureDraft d, Vector3 foot, float yaw, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(foot, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
			d.Add(StructureShapes.Cylinder(0.24f, -0.04f, 0.04f, d.Seg(10, 6), StructureMaterial.Timber, key).Transform(m * Matrix4x4.TRS(new Vector3(0f, 0.24f, 0.55f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
			d.Add(StructureShapes.Block(new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.3f, 0.75f), Quaternion.Euler(-6f, 0f, 0f), StructureMaterial.Timber, key + 1).Transform(m));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.25f, 0.26f, 0.55f), new Vector3(s * 0.3f, 0.55f, -0.95f), 0.05f, 0.05f, StructureMaterial.Timber, key + 2 + (s > 0 ? 1 : 0)).Transform(m));
				d.Add(StructureShapes.Beam(new Vector3(s * 0.22f, 0f, -0.25f), new Vector3(s * 0.22f, 0.32f, -0.25f), 0.05f, 0.05f, StructureMaterial.Timber, key + 4 + (s > 0 ? 1 : 0)).Transform(m));
			}
			d.Add(StructureShapes.Sphere(new Vector3(0f, 0.6f, 0f), new Vector3(0.28f, 0.14f, 0.34f), d.Seg(7, 5), 3, StructureMaterial.Earth, key + 6).Transform(m));
		}

		private static void DigCamp(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				// The barrow of spoil, the finds crates and the tools leant on them.
				Wheelbarrow(d, new Vector3(-0.55f, 0f, -0.1f), 200f, 1);
				d.Add(StructureShapes.Box(new Vector3(0.55f, 0.25f, 0.1f), new Vector3(0.6f, 0.5f, 0.5f), StructureMaterial.Timber, 10)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0.6f, 0.71f, 0.12f), new Vector3(0.5f, 0.42f, 0.42f), StructureMaterial.Timber, 11)).Grain = Vector3.right;
				// A spade and a pick against the crates' front.
				d.Add(StructureShapes.Rod(new Vector3(0.35f, 0.1f, -0.45f), new Vector3(0.42f, 1.1f, -0.17f), 0.02f, d.Seg(6, 4), StructureMaterial.Timber, 12));
				d.Add(StructureShapes.Block(new Vector3(0.345f, 0.12f, -0.47f), new Vector3(0.2f, 0.26f, 0.02f), Quaternion.Euler(-16f, 0f, 0f), StructureMaterial.Iron, 13));
				d.Add(StructureShapes.Rod(new Vector3(0.85f, 0.05f, -0.4f), new Vector3(0.8f, 0.9f, -0.17f), 0.022f, d.Seg(6, 4), StructureMaterial.Timber, 14));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(new Vector3(0.62f, 0.86f, -0.21f), new Vector3(0.98f, 0.94f, -0.2f), 0.04f, 0.03f, StructureMaterial.Iron, 15, StructureRole.Detail));
				}
				return;
			}
			// The finds table: trays, pots, a skull and long bones, brushes; a crate beside it.
			TrestleTable(d, new Vector3(0f, 0f, 0f), 1.4f, 0.7f, 1);
			FindsTray(d, new Vector3(-0.35f, 0.78f, 0.05f), 4f, 10);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.07f, 0f), new Vector2(0.12f, 0.1f), new Vector2(0.08f, 0.2f), new Vector2(0.06f, 0.24f), new Vector2(0f, 0.24f) },
				d.Seg(8, 5), StructureMaterial.Earth, 20).Transform(Matrix4x4.Translate(new Vector3(0.25f, 0.78f, 0.15f))));
			d.Add(StructureShapes.Sphere(new Vector3(0.5f, 0.86f, -0.05f), new Vector3(0.08f, 0.08f, 0.1f), d.Seg(7, 5), 3, StructureMaterial.Bone, 21));
			if (d.Mid)
			{
				d.Add(StructureShapes.Rod(new Vector3(0.15f, 0.8f, -0.2f), new Vector3(0.55f, 0.8f, -0.24f), 0.022f, 5, StructureMaterial.Bone, 22, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.08f, 0.78f, 0.86f, d.Seg(8, 5), StructureMaterial.Earth, 23, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.05f, 0f, 0.2f))));
			}
			if (d.Fine)
			{
				for (int i = 0; i < 2; i++)
				{
					var a = new Vector3(-0.05f + i * 0.12f, 0.795f, -0.25f);
					d.Add(StructureShapes.Rod(a, a + new Vector3(0.02f, 0f, 0.16f), 0.01f, 5, StructureMaterial.Timber, 24 + 2 * i, StructureRole.Detail));
					d.Add(StructureShapes.Box(a + new Vector3(0.022f, 0f, 0.19f), new Vector3(0.04f, 0.025f, 0.06f), StructureMaterial.Canvas, 25 + 2 * i, StructureRole.Detail));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(1.1f, 0.25f, 0.05f), new Vector3(0.5f, 0.5f, 0.5f), StructureMaterial.Timber, 30)).Grain = Vector3.right;
		}

		// ── Furniture ─────────────────────────────────────────────────

		/// <summary>
		/// A counting table, its back to the wall (+z): the changer's bench against the wall, the table before it, and across its
		/// front (v0) a wooden grille with a wicket in the middle to pass coin through; v1 open, a green cloth and a low rail.
		/// </summary>
		private static void CountingTable(StructureDraft d)
		{
			const float top = 0.8f, length = 1.8f, z = -0.08f, depth = 0.72f;
			bool grille = d.Variant == 0;
			float fz = z - depth * 0.5f;
			d.Add(StructureShapes.Box(new Vector3(0f, top - 0.03f, z), new Vector3(length, 0.06f, depth), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, (top - 0.06f) * 0.5f, fz + 0.03f), new Vector3(length - 0.04f, top - 0.06f, 0.05f), StructureMaterial.Timber, 2)).Grain = Vector3.up;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (length * 0.5f - 0.04f), (top - 0.06f) * 0.5f, z), new Vector3(0.06f, top - 0.06f, depth - 0.04f), StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0)));
			}
			// The bench behind it.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.44f, 0.46f), new Vector3(length - 0.3f, 0.05f, 0.28f), StructureMaterial.Timber, 5)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * (length * 0.5f - 0.3f), 0.21f, 0.46f), new Vector3(0.05f, 0.42f, 0.24f), StructureMaterial.Timber, 6 + (s > 0 ? 1 : 0)));
			}
			if (grille)
			{
				const float head = 1.95f, wicket = 0.5f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (length * 0.5f - 0.04f), (head + top) * 0.5f, fz + 0.03f), new Vector3(0.08f, head - top, 0.08f), StructureMaterial.Timber, 10 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, head - 0.04f, fz + 0.03f), new Vector3(length, 0.08f, 0.08f), StructureMaterial.Timber, 12)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.34f, fz + 0.03f), new Vector3(length - 0.1f, 0.05f, 0.06f), StructureMaterial.Timber, 13)).Grain = Vector3.right;
				// The bars, the wicket left open in the middle under the rail.
				int bars = d.Fine ? 11 : d.Mid ? 7 : 3;
				for (int i = 0; i < bars; i++)
				{
					float x = Mathf.Lerp(-length * 0.5f + 0.18f, length * 0.5f - 0.18f, i / (bars - 1f));
					float from = Mathf.Abs(x) < wicket * 0.5f ? top + 0.36f : top;
					d.Add(StructureShapes.Box(new Vector3(x, (from + head - 0.08f) * 0.5f, fz + 0.03f), new Vector3(0.03f, head - 0.08f - from, 0.03f), StructureMaterial.Timber, 14 + i,
						StructureRole.Detail));
				}
			}
			else
			{
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.003f, z), new Vector3(length - 0.1f, 0.012f, depth - 0.06f), StructureMaterial.Dyed, 10, StructureRole.Detail));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, top + 0.12f, fz + 0.04f), new Vector3(length - 0.1f, 0.03f, 0.03f), StructureMaterial.Timber, 11, StructureRole.Detail));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * (length * 0.5f - 0.1f), top + 0.06f, fz + 0.04f), new Vector3(0.03f, 0.12f, 0.03f), StructureMaterial.Timber, 12 + (s > 0 ? 1 : 0),
							StructureRole.Detail));
					}
				}
			}
			// On the table: the scales, the coin in its stacks, the ledgers.
			if (d.Mid)
			{
				Scales(d, new Vector3(-0.55f, top, z + 0.05f), 0.34f, 0.015f, 40);
				d.Add(StructureShapes.Box(new Vector3(0.5f, top + 0.03f, z + 0.1f), new Vector3(0.32f, 0.06f, 0.24f), StructureMaterial.Dyed, 50, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0.52f, top + 0.085f, z + 0.1f), new Vector3(0.3f, 0.05f, 0.22f), StructureMaterial.Timber, 51, StructureRole.Detail));
			}
			if (d.Fine)
			{
				for (int i = 0; i < 5; i++)
				{
					float tall = 0.02f + 0.02f * (i % 3);
					d.Add(StructureShapes.Cylinder(0.025f, top, top + tall, 6, StructureMaterial.Iron, 60 + i, false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(-0.1f + i * 0.07f, 0f, z - 0.05f + (i % 2) * 0.06f))));
				}
				// An open ledger and its quill's candle.
				d.Add(StructureShapes.Block(new Vector3(0.1f, top + 0.012f, z + 0.18f), new Vector3(0.4f, 0.02f, 0.28f), Quaternion.Euler(0f, 8f, 0f), StructureMaterial.Plaster, 65, StructureRole.Detail));
				d.Add(StructureShapes.Cylinder(0.022f, top, top + 0.14f, 6, StructureMaterial.Canvas, 66, false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(0.78f, 0f, z + 0.22f))));
			}
		}

		/// <summary>A strongbox, its back to the wall: v0 an oak chest bound in iron both ways, hasped and padlocked; v1 an iron safe-chest on feet, riveted and chained.</summary>
		private static void Strongbox(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				const float w = 1f, h = 0.58f, dz = 0.6f;
				d.Add(StructureShapes.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, dz), StructureMaterial.Timber, 1)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, h + 0.04f, 0f), new Vector3(w + 0.04f, 0.08f, dz + 0.04f), StructureMaterial.Timber, 2)).Grain = Vector3.right;
				// Straps over the lid and down the front and back, and a band round its foot.
				int straps = d.Mid ? 3 : 2;
				for (int i = 0; i < straps; i++)
				{
					float x = straps == 3 ? (i - 1) * 0.36f : (i == 0 ? -0.3f : 0.3f);
					d.Add(StructureShapes.Box(new Vector3(x, (h + 0.09f) * 0.5f, 0f), new Vector3(0.06f, h + 0.1f, dz + 0.08f), StructureMaterial.Iron, 3 + i));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.06f, 0f), new Vector3(w + 0.03f, 0.08f, dz + 0.03f), StructureMaterial.Iron, 6));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0.18f, h - 0.05f, -dz * 0.5f - 0.05f), new Vector3(0.07f, 0.2f, 0.03f), StructureMaterial.Iron, 7, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0.18f, h - 0.22f, -dz * 0.5f - 0.07f), new Vector3(0.12f, 0.12f, 0.05f), StructureMaterial.Iron, 8, StructureRole.Detail));
				}
				if (d.Fine)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Rod(new Vector3(s * (w * 0.5f + 0.04f), h * 0.6f, -0.12f), new Vector3(s * (w * 0.5f + 0.04f), h * 0.6f, 0.12f), 0.018f, 5, StructureMaterial.Iron, 9 + (s > 0 ? 1 : 0),
							StructureRole.Detail));
					}
				}
				return;
			}
			// The safe-chest: an iron box on four squat feet, a riveted band round it, a lid, the chains across it and a padlock.
			const float sw = 0.85f, sh = 0.55f, sd = 0.58f, feet = 0.08f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(sx * (sw * 0.5f - 0.07f), feet * 0.5f, sz * (sd * 0.5f - 0.07f)), new Vector3(0.1f, feet, 0.1f), StructureMaterial.Iron, 1 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
				}
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, feet + sh * 0.5f, 0f), new Vector3(sw, sh, sd), d.Bevel(0.02f), StructureMaterial.Iron, 5));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, feet + sh + 0.04f, 0f), new Vector3(sw + 0.04f, 0.08f, sd + 0.04f), d.Bevel(0.015f), StructureMaterial.Iron, 6));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, feet + sh * 0.55f, 0f), new Vector3(sw + 0.03f, 0.07f, sd + 0.03f), StructureMaterial.Iron, 7, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, feet + sh * 0.35f, -sd * 0.5f - 0.04f), new Vector3(0.14f, 0.16f, 0.05f), StructureMaterial.Iron, 8, StructureRole.Detail));
			}
			// The chains: one each way over the lid and down the sides, link by link at full detail.
			for (int c = 0; c < 2; c++)
			{
				bool alongX = c == 0;
				float span = alongX ? sw : sd;
				Vector3 Up(float t) => new Vector3(alongX ? t : 0.12f, 0f, alongX ? -0.08f : t);
				Vector3 a = Up(-span * 0.5f - 0.03f) + Vector3.up * (feet + 0.08f), b = Up(-span * 0.5f - 0.03f) + Vector3.up * (feet + sh + 0.1f);
				Vector3 e = Up(span * 0.5f + 0.03f) + Vector3.up * (feet + sh + 0.1f), f = Up(span * 0.5f + 0.03f) + Vector3.up * (feet + 0.08f);
				Vector3[] runs = { a, b, e, f };
				for (int r = 0; r < 3; r++)
				{
					Vector3 p = runs[r], q = runs[r + 1];
					if (!d.Fine)
					{
						d.Add(StructureShapes.Beam(p, q, 0.035f, 0.035f, StructureMaterial.Iron, 20 + c * 10 + r, StructureRole.Detail));
						continue;
					}
					int links = Mathf.Max(2, Mathf.RoundToInt((q - p).magnitude / 0.13f));
					Vector3 dir = (q - p).normalized;
					for (int l = 0; l < links; l++)
					{
						Vector3 at = Vector3.Lerp(p, q, (l + 0.5f) / links);
						Quaternion turn = Quaternion.LookRotation(dir, l % 2 == 0 ? Vector3.Cross(dir, alongX ? Vector3.forward : Vector3.right) : (alongX ? Vector3.forward : Vector3.right));
						d.Add(StructureShapes.Block(at, new Vector3(0.014f, 0.05f, 0.12f), turn, StructureMaterial.Iron, 100 + c * 50 + r * 16 + l, StructureRole.Detail));
					}
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0.12f, feet + sh + 0.02f, -sd * 0.5f - 0.1f), new Vector3(0.13f, 0.15f, 0.05f), StructureMaterial.Iron, 9, StructureRole.Detail));
		}

		/// <summary>
		/// An astronomer's table, its back to the wall: v0 a broad table spread with star charts, an astrolabe standing on them,
		/// rolled charts and two candles; v1 a sloped chart desk with a celestial globe on its stand at one end and a quadrant.
		/// </summary>
		private static void StarChartTable(StructureDraft d)
		{
			bool desk = d.Variant == 1;
			const float length = 1.7f, depth = 0.9f;
			float top = desk ? 0.9f : 0.78f;
			// The legs and the top (a desk's tilted toward the room, a rail along its foot).
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					float h = desk && sz > 0 ? top + 0.18f : top;
					d.Add(StructureShapes.Box(new Vector3(sx * (length * 0.5f - 0.08f), (h - 0.05f) * 0.5f, sz * (depth * 0.5f - 0.08f)), new Vector3(0.08f, h - 0.05f, 0.08f), StructureMaterial.Timber,
						1 + (sx > 0 ? 1 : 0) + (sz > 0 ? 2 : 0)));
				}
			}
			Quaternion slope = desk ? Quaternion.Euler(-11.5f, 0f, 0f) : Quaternion.identity;
			Vector3 board = new Vector3(0f, top + (desk ? 0.06f : -0.02f), 0f);
			d.Add(StructureShapes.Block(board, new Vector3(length, 0.05f, depth), slope, StructureMaterial.Timber, 5)).Grain = Vector3.right;
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0.15f, 0f), new Vector3(length - 0.16f, 0.05f, depth - 0.16f), StructureMaterial.Timber, 6, StructureRole.Detail)).Grain = Vector3.right;
			}
			// The charts on it.
			for (int i = 0; i < (d.Mid ? 3 : 1); i++)
			{
				var at = new Vector3(-0.45f + i * 0.42f, 0.03f + i * 0.004f, d.Range(10 + i, 1, -0.1f, 0.1f));
				d.Add(StructureShapes.Block(board + slope * at, new Vector3(0.55f, 0.008f, 0.42f), slope * Quaternion.Euler(0f, d.Range(10 + i, 2, -12f, 12f), 0f), StructureMaterial.Plaster,
					10 + i, StructureRole.Detail));
			}
			if (!desk)
			{
				// The astrolabe on its little stand, two candles, a roll of charts.
				var foot = new Vector3(0.55f, top, 0.18f);
				d.Add(StructureShapes.Box(foot + new Vector3(0f, 0.02f, 0f), new Vector3(0.16f, 0.04f, 0.1f), StructureMaterial.Timber, 20));
				d.Add(StructureShapes.Cylinder(0.16f, -0.012f, 0.012f, d.Seg(12, 6), StructureMaterial.Iron, 21).Transform(Matrix4x4.TRS(foot + Vector3.up * 0.2f, Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(foot + new Vector3(0f, 0.2f, -0.02f), new Vector3(0.3f, 0.02f, 0.008f), StructureMaterial.Iron, 22, StructureRole.Detail));
					d.Add(StructureShapes.Rod(new Vector3(-0.75f, top + 0.05f, 0.25f), new Vector3(-0.35f, top + 0.05f, 0.3f), 0.045f, d.Seg(8, 5), StructureMaterial.Plaster, 23, StructureRole.Detail));
				}
				for (int s = -1; s <= 1; s += 2)
				{
					var c = new Vector3(s * 0.15f, top, 0.32f);
					d.Add(StructureShapes.Cylinder(0.045f, top, top + 0.03f, 6, StructureMaterial.Iron, 24 + (s > 0 ? 2 : 0), false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(c.x, 0f, c.z))));
					d.Add(StructureShapes.Cylinder(0.018f, top + 0.03f, top + 0.2f, 6, StructureMaterial.Canvas, 25 + (s > 0 ? 2 : 0), false, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(c.x, 0f, c.z))));
				}
				return;
			}
			// The celestial globe on its turned stand at the desk's right end, its meridian ring; a quadrant on the left.
			var stand = new Vector3(length * 0.5f + 0.32f, 0f, 0.1f);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, 0f), new Vector2(0.2f, 0.05f), new Vector2(0.05f, 0.12f), new Vector2(0.05f, 0.8f), new Vector2(0.12f, 0.88f), new Vector2(0f, 0.88f) },
				d.Seg(10, 6), StructureMaterial.Timber, 30).Transform(Matrix4x4.Translate(stand)));
			Vector3 globe = stand + Vector3.up * 1.12f;
			d.Add(StructureShapes.Sphere(globe, Vector3.one * 0.22f, d.Seg(10, 6), d.Fine ? 6 : 4, StructureMaterial.Plaster, 31));
			Ring(d, globe, 0.26f, Vector3.forward, 0.012f, 32);
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(stand + Vector3.up * 0.86f, globe + Vector3.down * 0.25f, 0.025f, 0.025f, StructureMaterial.Iron, 33, StructureRole.Detail));
				var q = new List<Vector2> { Vector2.zero };
				int steps = d.Fine ? 5 : 3;
				for (int i = 0; i <= steps; i++)
				{
					float a = 0.5f * Mathf.PI * i / steps;
					q.Add(new Vector2(Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f));
				}
				d.Add(StructureShapes.Prism(q, -0.008f, 0.008f, StructureMaterial.Timber, 34, StructureRole.Detail)
					.Transform(Matrix4x4.TRS(board + slope * new Vector3(-0.55f, 0.03f, 0.1f), slope * Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
			}
		}
	}
}
#endif
