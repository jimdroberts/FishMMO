#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The town's other buildings (2026-10-19: "more structure types for towns, villages, and cities"): the garrison's barracks,
	/// archery range and training yard, and their furniture.
	/// </summary>
	/// <remarks>
	/// The garrison of a medieval town lived, drilled and shot where its people could see it: a long hall by the gate or the
	/// keep, a sanded yard with pells and a quintain, and the butts every able man was bound by law to shoot at on holy days.
	/// The range and the yard are open ground with a few solids on it (the butts, the targets, the racks, a fence): what a player
	/// walks through stays clear at every level, since the last level is the collider.
	/// </remarks>
	public static partial class StructurePieces
	{
		private static void AddMilitary(List<StructurePiece> list)
		{
			Piece(list, "Barracks", StructureStyle.Stone, StructureSize.Building,
				"A garrison's long hall: v0 18×9 m, a stone ground floor under a jettied, timber-framed dormitory storey up a stair, shingled, a studded door with a bench, a spear rack and a lantern by it and the garrison's shield over it; v1 a single-storey plank hall, 16×8 m, under thatch, a banner pole and a spear rack by the door.",
				Barracks, "building", "barracks", "military", "town");
			Piece(list, "ArcheryRange", StructureStyle.Timber, StructureSize.Medium,
				"A practice range 12 m wide and 30 long, its shooting line at the front (−z), open ground to three straw bosses at the far end, a rack of bows and quivers and an open-fronted shelter with a bench behind the line; v0 the bosses on easels before a turfed earth butt, a timber mark on the ground and a peg a lane; v1 hung on a timber frame before a plank backstop, a rail along the line.",
				ArcheryRange, "archery", "military", "town");
			Piece(list, "TrainingYard", StructureStyle.Timber, StructureSize.Medium,
				"A sanded practice yard, 12×10 m, inside a post-and-rail fence open at the front (−z), a rack of wasters and staves; v0 hacked pells, straw dummies on crossed poles and a rack of shields; v1 a quintain (a pivoting arm with a shield and a sandbag) for tilting, and dummies.",
				TrainingYard, "training", "military", "town");
			Piece(list, "BunkBed", StructureStyle.Timber, StructureSize.Small,
				"A soldier's bed, head to the wall: v0 a two-tier timber bunk, 1×2.2 m, straw mattresses and blankets, a ladder up its foot; v1 two low camp cots side by side with a footlocker at their feet.",
				BunkBed, "furniture", "bunk");
			Piece(list, "ArmourStand", StructureStyle.Timber, StructureSize.Small,
				"Armour on a stand, its back to the wall: v0 a mail hauberk and a nasal helm on a T-stand, a heater shield leaning on its foot; v1 a plate cuirass, pauldrons and a great helm, a halberd leaning beside it.",
				ArmourStand, "furniture", "armour");
			Piece(list, "GuardHouse", StructureStyle.Stone, StructureSize.Building,
				"The watch's house by a gate, 5.5×6 m, a guard room inside: v0 stone under shingles, its gable to the road, a brazier burning by the door; v1 a timber-framed lodge under thatch with a chimney, a bench by its door.",
				GuardHouse, "building", "military", "town");
		}

		// ── Barracks ──────────────────────────────────────────────────

		/// <summary>
		/// A garrison's long hall, its long side to the street: the men sleep in bunks down both walls (StructureRoomKind.Barracks,
		/// upstairs too when there is a storey), their arms racked by the door. The town's v0 is the stone hall a lord built
		/// by his keep, its dormitory a framed storey jettied over it; v1 the plank hall a town raised for its watch.
		/// </summary>
		private static void Barracks(StructureDraft d)
		{
			bool storeyed = d.Variant == 0;
			House h = storeyed
				? new House
				{
					Width = 18f, Depth = 9f, WallHeight = 2.8f, Upper = 2.7f, Rise = 2.6f, Overhang = 0.5f, Wall = 0.4f, Plinth = 0.4f,
					WallMaterial = StructureMaterial.Stone, RoofMaterial = StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
					Frame = false, RidgeAlongX = true, Chimney = true, Windows = 3,
					// Off the middle: the jetty's middle brace comes down over the front's middle.
					DoorWidth = 1.4f, DoorHeight = 2.4f, DoorX = 1.5f, DoorKind = StructureDoorKind.Studded,
					RoomKind = StructureRoomKind.Barracks, UpperRoomKind = StructureRoomKind.Barracks,
					// The dormitory over the stone hall is a timber-framed plaster storey, as the tall house's over its stone floor.
					UpperWallMaterial = StructureMaterial.Plaster, UpperFrame = true, Key = 1,
				}
				: new House
				{
					Width = 16f, Depth = 8f, WallHeight = 3f, Rise = 2.4f, Overhang = 0.6f, Wall = 0.25f, Plinth = 0.35f,
					WallMaterial = StructureMaterial.Timber, RoofMaterial = StructureMaterial.Thatch, PlinthMaterial = StructureMaterial.Fieldstone,
					Frame = false, RidgeAlongX = true, Chimney = true, Windows = 4,
					DoorWidth = 1.4f, DoorHeight = 2.4f, DoorX = 0f, DoorKind = StructureDoorKind.Planks,
					RoomKind = StructureRoomKind.Barracks, Key = 1,
				};
			BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f;
			if (storeyed)
			{
				// The gables stand on the framed storey: plaster, as its walls are.
				foreach (StructureSolid s in d.Solids)
				{
					if ((s.Key == 13 || s.Key == 14) && s.Material == StructureMaterial.Stone)
					{
						s.Material = StructureMaterial.Plaster;
					}
				}
			}
			float doorLeft = h.DoorX - h.DoorWidth * 0.5f, doorRight = h.DoorX + h.DoorWidth * 0.5f;
			// A lantern on an iron bracket beside the door: the watch comes and goes all night.
			GarrisonLantern(d, new Vector3(storeyed ? doorRight + 0.35f : doorLeft - 0.35f, floor + 2.25f, front), 9000);
			// A bench by the door, where the men off watch sit, and the rack their spears stand in.
			GarrisonBench(d, new Vector3(storeyed ? doorLeft - 1.05f : doorRight + 1.05f, 0f, front - 0.4f), 1.5f, 9010);
			GarrisonSpearRack(d, new Vector3(storeyed ? -3f : 3.6f, 0f, front - 0.35f), 4, 9020);
			if (storeyed)
			{
				// The garrison's shield over the door, on the jettied storey's face, two spears crossed behind it.
				float storey = floor + h.WallHeight + 0.24f, face = -(h.Depth * 0.5f + DefaultJetty) - 0.05f;
				var at = new Vector3(h.DoorX, storey + 1.3f, face);
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						Vector3 a = at + new Vector3(-s * 0.85f, -0.8f, -0.02f), b = at + new Vector3(s * 0.85f, 0.8f, -0.02f);
						d.Add(StructureShapes.Beam(a, b, 0.04f, 0.04f, StructureMaterial.Timber, 9060 + (s > 0 ? 1 : 0), StructureRole.Detail));
						Vector3 dir = (b - a).normalized;
						d.Add(StructureShapes.Beam(b, b + dir * 0.22f, 0.07f, 0.015f, StructureMaterial.Iron, 9062 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
				GarrisonHeater(d, Matrix4x4.Translate(at + new Vector3(0f, 0f, -0.04f)), 0.8f, 1f, 9070);
				return;
			}
			// The hall's banner on its pole before the door, clear of the eaves.
			var foot = new Vector3(-3.6f, 0f, front - 1.4f);
			const float poleTop = 4.6f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.26f, -0.2f), new Vector2(0.22f, 0.16f), new Vector2(0f, 0.2f) }, d.Seg(8, 5), StructureMaterial.Fieldstone, 9080, false)
				.Transform(Matrix4x4.Translate(foot)));
			Log(d, foot + Vector3.down * 0.3f, foot + Vector3.up * poleTop, 0.06f, 9081, 0f, StructureMaterial.Timber, 8);
			float bar = poleTop - 0.35f;
			d.Add(StructureShapes.Beam(foot + new Vector3(-0.5f, bar, -0.07f), foot + new Vector3(0.5f, bar, -0.07f), 0.06f, 0.06f, StructureMaterial.Timber, 9082));
			var banner = new[] { new Vector2(-0.42f, bar), new Vector2(0.42f, bar), new Vector2(0.42f, bar - 1.6f), new Vector2(0f, bar - 1.85f), new Vector2(-0.42f, bar - 1.6f) };
			d.Add(StructureShapes.Prism(banner, -0.11f, -0.09f, StructureMaterial.Dyed, 9083, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(foot.x, 0f, foot.z))));
			if (d.Mid)
			{
				d.Add(StructureShapes.Sphere(foot + Vector3.up * (poleTop + 0.07f), new Vector3(0.09f, 0.09f, 0.09f), d.Seg(8, 4), 4, StructureMaterial.Iron, 9084, StructureRole.Detail));
				// The plank hall's corner posts, carrying its wall plates.
				float top = floor + h.WallHeight;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						var c = new Vector3(sx * (h.Width * 0.5f + 0.02f), 0f, sz * (h.Depth * 0.5f + 0.02f));
						d.Add(StructureShapes.Beam(c, c + Vector3.up * top, 0.24f, 0.24f, StructureMaterial.Timber, 9090 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0), StructureRole.Detail));
					}
				}
			}
		}

		/// <summary>
		/// The watch's house by a gate (2026-10-19): one guard room (StructureRoomKind.Guard) where the men on the gate keep
		/// warm, rack their arms and sleep in turn; its lantern lit all night and, on the stone house, a brazier at the door.
		/// </summary>
		private static void GuardHouse(StructureDraft d)
		{
			bool stone = d.Variant == 0;
			House h = stone
				? new House
				{
					Width = 5.5f, Depth = 6f, WallHeight = 2.8f, Rise = 1.8f, Overhang = 0.4f, Wall = 0.5f, Plinth = 0.3f,
					WallMaterial = StructureMaterial.Stone, RoofMaterial = StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
					Frame = false, RidgeAlongX = false, Chimney = false, Windows = 1,
					DoorWidth = 1.1f, DoorHeight = 2.3f, DoorX = 0f, DoorKind = StructureDoorKind.Studded, RoomKind = StructureRoomKind.Guard, Key = 1,
				}
				: new House
				{
					Width = 6f, Depth = 5.5f, WallHeight = 2.8f, Rise = 2f, Overhang = 0.5f, Wall = 0.25f, Plinth = 0.35f,
					WallMaterial = StructureMaterial.Plaster, RoofMaterial = StructureMaterial.Thatch, PlinthMaterial = StructureMaterial.Fieldstone,
					Frame = true, RidgeAlongX = true, Chimney = true, Windows = 2,
					DoorWidth = 1.1f, DoorHeight = 2.3f, DoorX = -0.9f, DoorKind = StructureDoorKind.Planks, RoomKind = StructureRoomKind.Guard, Key = 1,
				};
			BuildHouse(d, h);
			float floor = h.Plinth, front = -h.Depth * 0.5f;
			float doorLeft = h.DoorX - h.DoorWidth * 0.5f, doorRight = h.DoorX + h.DoorWidth * 0.5f;
			GarrisonLantern(d, new Vector3(doorLeft - 0.35f, floor + 2.15f, front), 9000);
			if (!stone)
			{
				GarrisonBench(d, new Vector3(doorRight + 1.2f, 0f, front - 0.4f), 1.4f, 9010);
				return;
			}
			// The brazier the watch warms its hands at, on its iron tripod by the door.
			var at = new Vector3(-1.9f, 0f, front - 1f);
			var bowl = new List<Vector2> { new Vector2(0.16f, 0.62f), new Vector2(0.3f, 0.72f), new Vector2(0.36f, 0.95f) };
			d.Add(StructureShapes.Lathe(bowl, d.Seg(10, 6), StructureMaterial.Iron, 9020, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
			for (int i = 0; i < 3; i++)
			{
				Vector3 out1 = Quaternion.Euler(0f, i * 120f + 30f, 0f) * Vector3.forward;
				d.Add(StructureShapes.Beam(at + out1 * 0.38f, at + out1 * 0.2f + Vector3.up * 0.72f, 0.04f, 0.04f, StructureMaterial.Iron, 9021 + i, StructureRole.Detail));
			}
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(at + Vector3.up * 0.9f, new Vector3(0.42f, 0.06f, 0.42f), StructureMaterial.Ash, 9025, StructureRole.Detail));
			}
			d.Emit(at + Vector3.up * 0.98f, SceneEmitterKind.Fire);
		}

		/// <summary>An iron lantern on a bracket out of a wall whose outer face is at <paramref name="wall"/> (facing −z): its glass lit at night.</summary>
		private static void GarrisonLantern(StructureDraft d, Vector3 wall, int key)
		{
			Vector3 tip = wall + new Vector3(0f, 0f, -0.45f);
			d.Add(StructureShapes.Beam(wall + new Vector3(0f, 0f, 0.03f), tip, 0.05f, 0.05f, StructureMaterial.Iron, key, StructureRole.Detail));
			Vector3 at = tip + Vector3.down * 0.3f;
			d.Add(StructureShapes.Box(at, new Vector3(0.18f, 0.26f, 0.18f), StructureMaterial.Glass, key + 1, StructureRole.Detail));
			d.Emit(at, SceneEmitterKind.Lamp);
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(tip, at + Vector3.up * 0.13f, 0.02f, 0.02f, StructureMaterial.Iron, key + 2, StructureRole.Detail));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.16f, at.y + 0.13f), new Vector2(0f, at.y + 0.26f) }, 4, StructureMaterial.Iron, key + 3, false, Mathf.PI * 0.25f, false, false, StructureRole.Detail)
					.Transform(Matrix4x4.Translate(new Vector3(at.x, 0f, at.z))));
				d.Add(StructureShapes.Box(at + Vector3.down * 0.15f, new Vector3(0.22f, 0.04f, 0.22f), StructureMaterial.Iron, key + 4, StructureRole.Detail));
			}
		}

		/// <summary>A plank bench along x, <paramref name="length"/> long, its foot's middle at <paramref name="at"/>.</summary>
		private static void GarrisonBench(StructureDraft d, Vector3 at, float length, int key)
		{
			d.Add(StructureShapes.Box(at + new Vector3(0f, 0.45f, 0f), new Vector3(length, 0.07f, 0.34f), StructureMaterial.Timber, key, StructureRole.Detail)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(at + new Vector3(s * (length * 0.5f - 0.18f), 0.21f, 0f), new Vector3(0.08f, 0.45f, 0.3f), StructureMaterial.Timber, key + 1 + (s > 0 ? 1 : 0), StructureRole.Detail));
			}
		}

		/// <summary>
		/// A rack of spears against a wall behind it (+z): two posts, a trough their butts stand in and a rail their shafts lean
		/// on, <paramref name="count"/> spears; its foot's middle at <paramref name="at"/>.
		/// </summary>
		private static void GarrisonSpearRack(StructureDraft d, Vector3 at, int count, int key)
		{
			const float half = 0.8f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(at + new Vector3(s * half, -0.3f, -0.05f), at + new Vector3(s * half, 1.6f, 0.12f), 0.08f, 0.08f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(at + new Vector3(0f, 0.15f, -0.07f), new Vector3(2f * half + 0.1f, 0.18f, 0.22f), StructureMaterial.Timber, key + 2)).Grain = Vector3.right;
			d.Add(StructureShapes.Beam(at + new Vector3(-half - 0.05f, 1.5f, 0.11f), at + new Vector3(half + 0.05f, 1.5f, 0.11f), 0.07f, 0.07f, StructureMaterial.Timber, key + 3));
			if (!d.Mid)
			{
				return;
			}
			for (int i = 0; i < count; i++)
			{
				float x = Mathf.Lerp(-half + 0.25f, half - 0.25f, i / Mathf.Max(1f, count - 1f));
				Vector3 butt = at + new Vector3(x, 0.22f, -0.08f);
				Vector3 head = at + new Vector3(x + d.Range(key + 10 + i, 1, -0.06f, 0.06f), 2.3f, 0.17f);
				Vector3 dir = (head - butt).normalized;
				d.Add(StructureShapes.Beam(butt, head, 0.035f, 0.035f, StructureMaterial.Timber, key + 10 + i, StructureRole.Detail));
				// The leaf-bladed head, flat across the rack.
				d.Add(StructureShapes.Beam(head, head + dir * 0.26f, 0.06f, 0.015f, StructureMaterial.Iron, key + 20 + i, StructureRole.Detail));
			}
		}

		/// <summary>
		/// A heater shield, <paramref name="width"/> × <paramref name="height"/>, flat-topped and its sides curving to a point, its
		/// back at z = 0 and its face toward −z in <paramref name="place"/>'s frame (centred on its origin); from the middle level a
		/// pale cross on the dyed field.
		/// </summary>
		private static void GarrisonHeater(StructureDraft d, Matrix4x4 place, float width, float height, int key)
		{
			float w = width * 0.5f, h = height * 0.5f;
			var outline = new[]
			{
				new Vector2(-w, h), new Vector2(w, h), new Vector2(w, height * 0.05f), new Vector2(width * 0.36f, -height * 0.25f),
				new Vector2(0f, -h), new Vector2(-width * 0.36f, -height * 0.25f), new Vector2(-w, height * 0.05f),
			};
			d.Add(StructureShapes.Prism(outline, -0.05f, 0f, StructureMaterial.Dyed, key, StructureRole.Detail).Transform(place));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, height * 0.05f, -0.06f), new Vector3(width * 0.16f, height * 0.85f, 0.02f), StructureMaterial.Canvas, key + 1, StructureRole.Detail).Transform(place));
				d.Add(StructureShapes.Box(new Vector3(0f, height * 0.2f, -0.06f), new Vector3(width * 0.85f, height * 0.13f, 0.02f), StructureMaterial.Canvas, key + 2, StructureRole.Detail).Transform(place));
			}
		}

		/// <summary>A round shield of <paramref name="radius"/>, its back at z = 0 and its face toward −z in <paramref name="place"/>'s frame, an iron boss from the middle level.</summary>
		private static void GarrisonRoundShield(StructureDraft d, Matrix4x4 place, float radius, int key)
		{
			// A cylinder about y turned so its axis runs along −z: y → −z.
			Matrix4x4 disc = place * Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f));
			d.Add(StructureShapes.Cylinder(radius, 0f, 0.04f, d.Seg(12, 8), StructureMaterial.Dyed, key, false, StructureRole.Detail).Transform(disc));
			if (d.Mid)
			{
				d.Add(StructureShapes.Sphere(new Vector3(0f, 0f, -0.04f), new Vector3(0.07f, 0.07f, 0.05f), d.Seg(8, 5), 3, StructureMaterial.Iron, key + 1, StructureRole.Detail).Transform(place));
			}
		}

		// ── Archery range ─────────────────────────────────────────────

		/// <summary>
		/// The butts (2026-10-19): the shooting line at the front, the bosses 22 m on, the open ground between them clear of every
		/// solid. A medieval range was a pair of earthen butts with the marks pinned before them; here the bosses stand before a
		/// turf bank (v0) or hang on a frame before a plank backstop (v1) that stops what misses.
		/// </summary>
		private static void ArcheryRange(StructureDraft d)
		{
			bool butts = d.Variant == 0;
			const float line = -12f, targetZ = 10.4f;
			float[] lanes = { -3.6f, 0f, 3.6f };
			if (butts)
			{
				// The butt: a bank of earth, turfed, 12 m along the end, 2.1 m high, its faces battered and its ends sloped off.
				const float bankZ = 13.4f, foot = 1.45f, crest = 0.45f, high = 2.1f;
				using (d.Builds(BiomeStructureRole.Turf))
				{
					var ends = new[] { new Vector2(-6f, -0.4f), new Vector2(6f, -0.4f), new Vector2(4.8f, high), new Vector2(-4.8f, high) };
					StructureSolid bank = StructureShapes.Prism(ends, bankZ - foot - 0.3f, bankZ + foot + 0.3f, StructureMaterial.Earth, 1);
					float run = foot - crest;
					var faceIn = new Vector3(0f, run, -high).normalized;
					bank.Clip(faceIn, Vector3.Dot(faceIn, new Vector3(0f, 0f, bankZ - foot)));
					var faceOut = new Vector3(0f, run, high).normalized;
					bank.Clip(faceOut, Vector3.Dot(faceOut, new Vector3(0f, 0f, bankZ + foot)));
					d.Add(bank);
				}
				if (d.Fine)
				{
					// Arrows that missed, stuck in the butt's face.
					for (int i = 0; i < 4; i++)
					{
						float y = d.Range(60 + i, 1, 0.4f, 1.7f);
						var p = new Vector3(d.Range(60 + i, 2, -4.5f, 4.5f), y, bankZ - foot + y * (foot - crest) / high + 0.06f);
						var dir = new Vector3(d.Range(60 + i, 3, -0.15f, 0.15f), 0.25f, -1f).normalized;
						GarrisonArrow(d, p, dir, 0.7f, 60 + i * 2);
					}
				}
			}
			else
			{
				// The backstop: planks on posts, shored from behind, a capping board over its top to keep the rain from the planks' ends.
				const float stopZ = 12.4f, stopTop = 2.8f;
				d.Add(StructureShapes.Box(new Vector3(0f, (stopTop - 0.3f) * 0.5f, stopZ), new Vector3(12f, stopTop + 0.3f, 0.12f), StructureMaterial.Timber, 1)).Grain = Vector3.up;
				for (int i = 0; i < 4; i++)
				{
					float x = -5.8f + i * (11.6f / 3f);
					d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, stopZ + 0.13f), new Vector3(x, stopTop + 0.1f, stopZ + 0.13f), 0.16f, 0.14f, StructureMaterial.Timber, 2 + i));
					d.Add(StructureShapes.Beam(new Vector3(x, stopTop - 0.4f, stopZ + 0.22f), new Vector3(x, -0.3f, stopZ + 1.9f), 0.12f, 0.12f, StructureMaterial.Timber, 6 + i));
				}
				d.Add(StructureShapes.Block(new Vector3(0f, stopTop + 0.04f, stopZ + 0.05f), new Vector3(12.3f, 0.06f, 0.5f), Quaternion.Euler(-10f, 0f, 0f), StructureMaterial.Timber, 10, StructureRole.Roof)).Grain = Vector3.right;
				// The target frame the bosses hang on: posts between the lanes, a beam over them and one under.
				const float frameZ = targetZ + 0.25f;
				for (int i = 0; i < 4; i++)
				{
					float x = -5.4f + i * 3.6f;
					d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, frameZ), new Vector3(x, 2.4f, frameZ), 0.14f, 0.14f, StructureMaterial.Timber, 12 + i));
				}
				d.Add(StructureShapes.Beam(new Vector3(-5.6f, 2.32f, frameZ), new Vector3(5.6f, 2.32f, frameZ), 0.12f, 0.14f, StructureMaterial.Timber, 16));
				d.Add(StructureShapes.Beam(new Vector3(-5.6f, 0.5f, frameZ), new Vector3(5.6f, 0.5f, frameZ), 0.1f, 0.12f, StructureMaterial.Timber, 17));
			}

			// The bosses: straw, their faces painted in rings, arrows in them.
			for (int i = 0; i < lanes.Length; i++)
			{
				int key = 100 + i * 40;
				if (butts)
				{
					// Leaned back on an easel: two legs behind it, a ledge it rests on and a prop behind.
					Matrix4x4 place = Matrix4x4.TRS(new Vector3(lanes[i], 1.25f, targetZ), Quaternion.Euler(15f, 0f, 0f), Vector3.one);
					GarrisonBoss(d, place, key);
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(place.MultiplyPoint3x4(new Vector3(s * 0.42f, -1.6f, 0.22f)), place.MultiplyPoint3x4(new Vector3(s * 0.36f, 0.75f, 0.22f)), 0.07f, 0.07f,
							StructureMaterial.Timber, key + 30 + (s > 0 ? 1 : 0)));
					}
					d.Add(StructureShapes.Beam(place.MultiplyPoint3x4(new Vector3(-0.48f, -0.66f, 0f)), place.MultiplyPoint3x4(new Vector3(0.48f, -0.66f, 0f)), 0.1f, 0.08f, StructureMaterial.Timber, key + 32));
					d.Add(StructureShapes.Beam(new Vector3(lanes[i], -0.3f, targetZ + 1.05f), place.MultiplyPoint3x4(new Vector3(0f, 0.7f, 0.24f)), 0.06f, 0.06f, StructureMaterial.Timber, key + 33));
				}
				else
				{
					// Hung flat on the frame from its beam by two cords.
					Matrix4x4 place = Matrix4x4.Translate(new Vector3(lanes[i], 1.35f, targetZ));
					GarrisonBoss(d, place, key);
					if (d.Mid)
					{
						for (int s = -1; s <= 1; s += 2)
						{
							d.Add(StructureShapes.Beam(new Vector3(lanes[i] + s * 0.3f, 2.28f, targetZ + 0.1f), new Vector3(lanes[i] + s * 0.3f, 1.8f, targetZ + 0.1f), 0.025f, 0.025f,
								StructureMaterial.Canvas, key + 30 + (s > 0 ? 1 : 0), StructureRole.Detail));
						}
					}
				}
			}

			// The shooting line: v0 a squared timber laid on the ground as the mark and a peg at each lane's station; v1 a rail
			// along it in a bay a lane, open between the bays to walk up to the bosses.
			if (butts)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 0f, line), new Vector3(10.4f, 0.16f, 0.16f), StructureMaterial.Timber, 200)).Grain = Vector3.right;
				for (int i = 0; i < lanes.Length; i++)
				{
					var peg = new Vector3(lanes[i] + 0.8f, 0f, line - 0.3f);
					d.Add(StructureShapes.Beam(peg + Vector3.down * 0.3f, peg + Vector3.up * 0.6f, 0.07f, 0.07f, StructureMaterial.Timber, 201 + i));
					if (d.Fine && i == 1)
					{
						// A quiver hung on the middle one.
						GarrisonQuiver(d, Matrix4x4.TRS(peg + new Vector3(0.07f, 0.05f, -0.06f), Quaternion.Euler(0f, 0f, -8f), Vector3.one), 210 + i * 4);
					}
				}
			}
			else
			{
				for (int i = 0; i < lanes.Length; i++)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						var post = new Vector3(lanes[i] + s * 1.2f, 0f, line);
						d.Add(StructureShapes.Beam(post + Vector3.down * 0.3f, post + Vector3.up * 1.05f, 0.1f, 0.1f, StructureMaterial.Timber, 201 + i * 2 + (s > 0 ? 1 : 0)));
					}
					d.Add(StructureShapes.Beam(new Vector3(lanes[i] - 1.25f, 1f, line), new Vector3(lanes[i] + 1.25f, 1f, line), 0.08f, 0.1f, StructureMaterial.Timber, 210 + i));
				}
			}

			// Behind the line: the rack of bows and quivers to the left, the shelter with its bench to the right.
			GarrisonBowRack(d, new Vector3(-5.1f, 0f, line - 2.2f), 300);
			GarrisonShelter(d, new Vector3(4.7f, 0f, -14.2f), 400);
		}

		/// <summary>
		/// A straw boss in <paramref name="place"/>'s frame (centred on its origin, its face toward −z): a thick disc of straw
		/// 1.2 m across, its face painted white with a coloured ring, a white ring and the gold at the finest level, arrows in it.
		/// </summary>
		private static void GarrisonBoss(StructureDraft d, Matrix4x4 place, int key)
		{
			// A cylinder about y turned so its axis runs along −z: y → −z.
			Matrix4x4 disc = place * Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f));
			d.Add(StructureShapes.Cylinder(0.6f, -0.15f, 0.15f, d.Seg(12, 8), StructureMaterial.Thatch, key).Transform(disc));
			d.Add(StructureShapes.Cylinder(0.5f, 0.15f, 0.165f, d.Seg(12, 8), StructureMaterial.Canvas, key + 1, false, StructureRole.Detail).Transform(disc));
			if (d.Mid)
			{
				d.Add(StructureShapes.Cylinder(0.36f, 0.165f, 0.18f, d.Seg(12, 8), StructureMaterial.Dyed, key + 2, false, StructureRole.Detail).Transform(disc));
			}
			if (!d.Fine)
			{
				return;
			}
			d.Add(StructureShapes.Cylinder(0.22f, 0.18f, 0.19f, 10, StructureMaterial.Canvas, key + 3, false, StructureRole.Detail).Transform(disc));
			d.Add(StructureShapes.Cylinder(0.09f, 0.19f, 0.2f, 8, StructureMaterial.Dyed, key + 4, false, StructureRole.Detail).Transform(disc));
			// The arrows, come in on a falling arc: their tails up and back toward the line.
			int arrows = 2 + Mathf.FloorToInt(d.Rand(key, 5) * 2.99f);
			for (int i = 0; i < arrows; i++)
			{
				float a = d.Range(key + 10 + i, 1, 0f, 2f * Mathf.PI), r = d.Range(key + 10 + i, 2, 0.04f, 0.45f);
				var p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.05f);
				var dir = new Vector3(d.Range(key + 10 + i, 3, -0.12f, 0.12f), d.Range(key + 10 + i, 4, 0.05f, 0.2f), -1f).normalized;
				GarrisonArrow(d, place.MultiplyPoint3x4(p), place.MultiplyVector(dir), 0.75f, key + 10 + i * 2);
			}
		}

		/// <summary>An arrow stuck in something at <paramref name="tip"/>, its shaft running back along <paramref name="dir"/> (the shaft alone: its fletching is lost at any distance a range is seen from).</summary>
		private static void GarrisonArrow(StructureDraft d, Vector3 tip, Vector3 dir, float length, int key)
		{
			d.Add(StructureShapes.Beam(tip, tip + dir * length, 0.018f, 0.018f, StructureMaterial.Timber, key, StructureRole.Detail));
		}

		/// <summary>A leather quiver of arrows in <paramref name="place"/>'s frame, its foot at the origin: the fletchings stand out of its mouth.</summary>
		private static void GarrisonQuiver(StructureDraft d, Matrix4x4 place, int key)
		{
			d.Add(StructureShapes.Cylinder(0.07f, 0f, 0.6f, d.Seg(8, 6), StructureMaterial.Canvas, key, true, StructureRole.Detail).Transform(place));
			if (d.Fine)
			{
				d.Add(StructureShapes.Cylinder(0.055f, 0.6f, 0.74f, 6, StructureMaterial.Dyed, key + 1, false, StructureRole.Detail).Transform(place));
			}
		}

		/// <summary>
		/// A rack of bows and quivers behind the shooting line, along x, its face toward +z (the line), its foot's middle at
		/// <paramref name="at"/>: two posts, a trough for the bows' lower tips, a rail they lean on, the quivers hung at its end.
		/// </summary>
		private static void GarrisonBowRack(StructureDraft d, Vector3 at, int key)
		{
			const float half = 0.85f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(at + new Vector3(s * half, -0.3f, 0f), at + new Vector3(s * half, 1.5f, 0f), 0.09f, 0.09f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(at + new Vector3(0f, 0.12f, 0.12f), new Vector3(2f * half + 0.1f, 0.18f, 0.22f), StructureMaterial.Timber, key + 2)).Grain = Vector3.right;
			d.Add(StructureShapes.Beam(at + new Vector3(-half - 0.05f, 1.42f, 0f), at + new Vector3(half + 0.05f, 1.42f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, key + 3));
			if (!d.Mid)
			{
				return;
			}
			// Longbows, kept unstrung so the staves take no set, leaned on the rail: two limbs from the grip, a little bent.
			for (int i = 0; i < 3; i++)
			{
				float x = -half + 0.3f + i * 0.4f;
				var lower = at + new Vector3(x, 0.2f, 0.16f);
				var upper = at + new Vector3(x + d.Range(key + 10 + i, 1, -0.05f, 0.05f), 1.95f, -0.05f);
				Vector3 grip = (lower + upper) * 0.5f + new Vector3(0f, 0f, 0.05f);
				d.Add(StructureShapes.Beam(lower, grip, 0.035f, 0.035f, StructureMaterial.Timber, key + 10 + i * 3, StructureRole.Detail));
				d.Add(StructureShapes.Beam(grip, upper, 0.035f, 0.035f, StructureMaterial.Timber, key + 11 + i * 3, StructureRole.Detail));
			}
			for (int i = 0; i < 2; i++)
			{
				var hang = at + new Vector3(half - 0.12f - i * 0.22f, 0.72f, 0.1f + i * 0.04f);
				GarrisonQuiver(d, Matrix4x4.TRS(hang, Quaternion.Euler(6f, 0f, (i == 0 ? -1f : 1f) * 5f), Vector3.one), key + 30 + i * 4);
			}
		}

		/// <summary>
		/// An open-fronted shelter behind the line, 2.6 × 2 m, its open side toward the range (+z), its back wall and its outer
		/// side planked, a pent roof sloping to the back and a bench along the back wall; <paramref name="at"/> its foot's middle.
		/// </summary>
		private static void GarrisonShelter(StructureDraft d, Vector3 at, int key)
		{
			const float halfX = 1.3f, halfZ = 1f, high = 2.75f, low = 2.4f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					float top = sz > 0 ? high : low;
					d.Add(StructureShapes.Beam(at + new Vector3(sx * halfX, -0.3f, sz * halfZ), at + new Vector3(sx * halfX, top, sz * halfZ), 0.14f, 0.14f,
						StructureMaterial.Timber, key + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
				}
			}
			// The back wall (−z, toward the street) and the outer side, planked to head height.
			d.Add(StructureShapes.Box(at + new Vector3(0f, 0.95f, -halfZ), new Vector3(2f * halfX - 0.14f, 2.1f, 0.08f), StructureMaterial.Timber, key + 4)).Grain = Vector3.up;
			d.Add(StructureShapes.Box(at + new Vector3(halfX, 0.95f, 0f), new Vector3(0.08f, 2.1f, 2f * halfZ - 0.14f), StructureMaterial.Timber, key + 5)).Grain = Vector3.up;
			// The pent roof: high over the open front, falling to the back wall, overhanging all round.
			float rise = high - low, run = 2f * halfZ;
			float tilt = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
			using (d.Builds(RoofRoles))
			{
				d.Add(StructureShapes.Block(at + new Vector3(0f, (high + low) * 0.5f + 0.05f, 0f), new Vector3(2f * halfX + 0.5f, 0.08f, Mathf.Sqrt(rise * rise + run * run) + 0.5f),
					Quaternion.Euler(-tilt, 0f, 0f), StructureMaterial.Timber, key + 6, StructureRole.Roof)).Grain = Vector3.forward;
			}
			// The bench along the back wall inside.
			GarrisonBench(d, at + new Vector3(-0.05f, 0f, -halfZ + 0.3f), 2.2f, key + 10);
		}

		// ── Training yard ─────────────────────────────────────────────

		/// <summary>
		/// The garrison's practice yard (2026-10-19): sand over the ground so a man thrown lands soft and the footing does not cut
		/// up, a low fence round it with the way in at the front. v0 drills on foot: pells hacked by sword practice (a post a man's
		/// height a recruit learned his cuts on), straw men on crossed poles, a rack of shields. v1 drills the horse: a quintain in
		/// the middle, its arm turning on its post when the lance strikes the shield, the sandbag coming round at the rider's back.
		/// </summary>
		private static void TrainingYard(StructureDraft d)
		{
			bool quintain = d.Variant == 1;
			const float halfX = 6f, halfZ = 5f;
			// The sanded floor, a hand's depth of it laid over the ground.
			using (d.Builds(BiomeStructureRole.Turf))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.11f, 0f), new Vector3(2f * halfX - 0.2f, 0.28f, 2f * halfZ - 0.2f), StructureMaterial.Earth, 1));
			}
			GarrisonRailFence(d, halfX, halfZ, 2.4f, 10);
			// The rack of wasters and staves against the left fence, facing into the yard.
			GarrisonPracticeRack(d, Matrix4x4.TRS(new Vector3(-halfX + 0.55f, 0f, -1.2f), Quaternion.Euler(0f, -90f, 0f), Vector3.one), 100);
			if (!quintain)
			{
				// Three pells along the back, each a little out of true from the blows.
				for (int i = 0; i < 3; i++)
				{
					var foot = new Vector3(-3.8f + i * 1.7f, 0f, halfZ - 1.3f);
					var top = foot + new Vector3(d.Range(200 + i, 1, -0.06f, 0.06f), 1.75f, d.Range(200 + i, 2, -0.06f, 0.06f));
					Log(d, foot + Vector3.down * 0.4f, top, 0.13f, 200 + i * 8, 0f, StructureMaterial.Timber, 8);
					if (d.Fine)
					{
						// Chips hacked from it, at its foot.
						for (int c = 0; c < 2; c++)
						{
							float a = d.Range(200 + i * 8 + c, 3, 0f, 360f);
							Vector3 chip = foot + Quaternion.Euler(0f, a, 0f) * new Vector3(0.28f, 0.015f, 0f);
							d.Add(StructureShapes.Block(chip, new Vector3(0.14f, 0.03f, 0.05f), Quaternion.Euler(0f, a + 40f, 0f), StructureMaterial.Timber, 201 + i * 8 + c, StructureRole.Detail));
						}
					}
				}
				GarrisonDummy(d, new Vector3(2.4f, 0f, halfZ - 1.6f), 10f, 300);
				GarrisonDummy(d, new Vector3(4.2f, 0f, 1.2f), -30f, 320);
				// The rack of shields against the right fence, facing into the yard.
				GarrisonShieldRack(d, Matrix4x4.TRS(new Vector3(halfX - 0.55f, 0f, -1.4f), Quaternion.Euler(0f, 90f, 0f), Vector3.one), 400);
				return;
			}
			GarrisonQuintain(d, new Vector3(0.6f, 0f, 0.8f), 500);
			GarrisonDummy(d, new Vector3(-2.6f, 0f, halfZ - 1.5f), 0f, 300);
			GarrisonDummy(d, new Vector3(4.4f, 0f, halfZ - 1.7f), -20f, 320);
		}

		/// <summary>
		/// A low post-and-rail fence round a <paramref name="halfX"/> × <paramref name="halfZ"/> rectangle, a post every 2.4 m or
		/// so, two rails, the way in (<paramref name="gap"/> wide) in the middle of the front.
		/// </summary>
		private static void GarrisonRailFence(StructureDraft d, float halfX, float halfZ, float gap, int key)
		{
			using (d.Builds(FenceRoles))
			{
				int k = key;
				void Post(float x, float z, float top = 1.1f)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, z), new Vector3(x, top, z), 0.13f, 0.13f, StructureMaterial.Timber, k++));
				}
				void Rails(Vector3 a, Vector3 b)
				{
					foreach (float y in new[] { 0.5f, 0.95f })
					{
						float sag = d.Range(k, 3, -0.03f, 0.03f);
						d.Add(StructureShapes.Beam(new Vector3(a.x, y, a.z), new Vector3(b.x, y + sag, b.z), 0.07f, 0.1f, StructureMaterial.Timber, k++, StructureRole.Detail));
					}
				}
				// The back and the sides, their posts spread evenly.
				int across = Mathf.Max(2, Mathf.RoundToInt(2f * halfX / 2.4f));
				for (int i = 0; i <= across; i++)
				{
					Post(-halfX + 2f * halfX * i / across, halfZ);
				}
				Rails(new Vector3(-halfX, 0f, halfZ), new Vector3(halfX, 0f, halfZ));
				int along = Mathf.Max(2, Mathf.RoundToInt(2f * halfZ / 2.4f));
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = 0; i < along; i++)
					{
						Post(s * halfX, -halfZ + 2f * halfZ * i / along);
					}
					Rails(new Vector3(s * halfX, 0f, -halfZ), new Vector3(s * halfX, 0f, halfZ));
				}
				// The front either side of the way in: the gateposts a little taller.
				float g = gap * 0.5f;
				int bays = Mathf.Max(1, Mathf.RoundToInt((halfX - g) / 2.4f));
				for (int s = -1; s <= 1; s += 2)
				{
					for (int i = 1; i < bays; i++)
					{
						Post(s * (g + (halfX - g) * i / bays), -halfZ);
					}
					Post(s * g, -halfZ, 1.35f);
					Rails(new Vector3(s * halfX, 0f, -halfZ), new Vector3(s * g, 0f, -halfZ));
				}
			}
		}

		/// <summary>
		/// A rack of wasters (wooden practice swords) and quarterstaves in <paramref name="place"/>'s frame, its face toward −z,
		/// its foot's middle at the origin: two posts, a rail the points stand on and one the blades lean on.
		/// </summary>
		private static void GarrisonPracticeRack(StructureDraft d, Matrix4x4 place, int key)
		{
			int first = d.Solids.Count;
			const float half = 0.85f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * half, -0.3f, 0f), new Vector3(s * half, 1.35f, 0.05f), 0.08f, 0.08f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-half - 0.05f, 0.3f, -0.03f), new Vector3(half + 0.05f, 0.3f, -0.03f), 0.08f, 0.12f, StructureMaterial.Timber, key + 2));
			d.Add(StructureShapes.Beam(new Vector3(-half - 0.05f, 1.15f, 0.06f), new Vector3(half + 0.05f, 1.15f, 0.06f), 0.07f, 0.07f, StructureMaterial.Timber, key + 3));
			if (d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					// Point down on the low rail, the hilt up over the high one.
					float x = -0.6f + i * 0.3f;
					var point = new Vector3(x, 0.37f, -0.05f);
					var guard = new Vector3(x + 0.01f, 1.2f, 0.07f);
					d.Add(StructureShapes.Beam(point, guard, 0.055f, 0.022f, StructureMaterial.Timber, key + 10 + i * 3, StructureRole.Detail));
					d.Add(StructureShapes.Beam(guard + new Vector3(-0.1f, 0f, 0f), guard + new Vector3(0.1f, 0f, 0f), 0.04f, 0.035f, StructureMaterial.Timber, key + 11 + i * 3, StructureRole.Detail));
					if (d.Fine)
					{
						d.Add(StructureShapes.Beam(guard, guard + new Vector3(0f, 0.22f, 0.02f), 0.035f, 0.035f, StructureMaterial.Canvas, key + 12 + i * 3, StructureRole.Detail));
					}
				}
				for (int i = 0; i < 2; i++)
				{
					float x = 0.55f + i * 0.14f;
					d.Add(StructureShapes.Beam(new Vector3(x, 0.37f, -0.05f), new Vector3(x + 0.03f, 1.95f, 0.12f), 0.04f, 0.04f, StructureMaterial.Timber, key + 30 + i, StructureRole.Detail));
				}
			}
			TransformSince(d, first, place);
		}

		/// <summary>A rack of round shields in <paramref name="place"/>'s frame, its face toward −z, its foot's middle at the origin.</summary>
		private static void GarrisonShieldRack(StructureDraft d, Matrix4x4 place, int key)
		{
			int first = d.Solids.Count;
			const float half = 1.05f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * half, -0.3f, 0.05f), new Vector3(s * half, 1.4f, 0.05f), 0.08f, 0.08f, StructureMaterial.Timber, key + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(-half - 0.05f, 1.3f, 0.05f), new Vector3(half + 0.05f, 1.3f, 0.05f), 0.07f, 0.07f, StructureMaterial.Timber, key + 2));
			d.Add(StructureShapes.Beam(new Vector3(-half - 0.05f, 0.45f, 0.05f), new Vector3(half + 0.05f, 0.45f, 0.05f), 0.07f, 0.07f, StructureMaterial.Timber, key + 3));
			for (int i = 0; i < 3; i++)
			{
				// Hung on the high rail, leaning back on the low one.
				var at = new Vector3((i - 1) * 0.66f, 0.92f, -0.01f - (i % 2) * 0.05f);
				GarrisonRoundShield(d, Matrix4x4.TRS(at, Quaternion.Euler(8f, 0f, 0f), Vector3.one), 0.32f, key + 10 + i * 2);
			}
			TransformSince(d, first, place);
		}

		/// <summary>
		/// A straw man to cut at, at <paramref name="at"/> turned <paramref name="yaw"/>: a pole and a cross pole for its arms, a
		/// body of bound straw, a head of straw, the rag of an old tabard over its chest.
		/// </summary>
		private static void GarrisonDummy(StructureDraft d, Vector3 at, float yaw, int key)
		{
			int first = d.Solids.Count;
			Log(d, new Vector3(0f, -0.4f, 0f), new Vector3(0f, 1.62f, 0f), 0.05f, key, 0f, StructureMaterial.Timber, 6);
			d.Add(StructureShapes.Beam(new Vector3(-0.5f, 1.37f, 0f), new Vector3(0.5f, 1.4f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, key + 1));
			var body = new List<Vector2> { new Vector2(0.13f, 0.7f), new Vector2(0.2f, 0.9f), new Vector2(0.22f, 1.2f), new Vector2(0.19f, 1.42f), new Vector2(0.09f, 1.5f) };
			d.Add(StructureShapes.Lathe(body, d.Seg(8, 5), StructureMaterial.Thatch, key + 2).Transform(Matrix4x4.Scale(new Vector3(1f, 1f, 0.8f))));
			d.Add(StructureShapes.Sphere(new Vector3(0f, 1.65f, 0f), new Vector3(0.13f, 0.15f, 0.13f), d.Seg(8, 5), d.Mid ? 4 : 3, StructureMaterial.Thatch, key + 3));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 1.12f, -0.185f), new Vector3(0.34f, 0.5f, 0.02f), StructureMaterial.Dyed, key + 4, StructureRole.Detail));
			}
			if (d.Fine)
			{
				// Straw bound over the cross pole's ends: its fists.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Sphere(new Vector3(s * 0.46f, 1.39f, 0f), new Vector3(0.08f, 0.07f, 0.07f), 6, 3, StructureMaterial.Thatch, key + 5 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}
			TransformSince(d, first, Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one));
		}

		/// <summary>
		/// A quintain at <paramref name="at"/>: a stout post set in crossed sleepers and braced, an iron pivot on its head and the
		/// arm across it, a shield hung at one end for the rider's lance and a sandbag at the other that comes round when he strikes.
		/// </summary>
		private static void GarrisonQuintain(StructureDraft d, Vector3 at, int key)
		{
			const float head = 2.35f, arm = 2.5f;
			Log(d, at + Vector3.down * 0.5f, at + Vector3.up * head, 0.14f, key, 0f, StructureMaterial.Timber, 8);
			d.Add(StructureShapes.Beam(at + new Vector3(-0.9f, 0.07f, 0f), at + new Vector3(0.9f, 0.07f, 0f), 0.16f, 0.14f, StructureMaterial.Timber, key + 1));
			d.Add(StructureShapes.Beam(at + new Vector3(0f, 0.07f, -0.9f), at + new Vector3(0f, 0.07f, 0.9f), 0.16f, 0.14f, StructureMaterial.Timber, key + 2));
			if (d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					Vector3 out1 = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.right;
					d.Add(StructureShapes.Beam(at + out1 * 0.8f + Vector3.up * 0.14f, at + out1 * 0.13f + Vector3.up * 0.95f, 0.1f, 0.1f, StructureMaterial.Timber, key + 3 + i, StructureRole.Detail));
				}
			}
			// The pivot: an iron cap on the post's head, the arm turning on its pin.
			d.Add(StructureShapes.Cylinder(0.17f, head - 0.1f, head + 0.04f, d.Seg(10, 6), StructureMaterial.Iron, key + 8).Transform(Matrix4x4.Translate(new Vector3(at.x, 0f, at.z))));
			float armY = head + 0.11f;
			d.Add(StructureShapes.Beam(at + new Vector3(-arm * 0.6f, armY, 0f), at + new Vector3(arm * 0.4f + 0.2f, armY, 0f), 0.13f, 0.14f, StructureMaterial.Timber, key + 9));
			// The shield, hung under the arm's long end and facing the rider's run.
			GarrisonHeater(d, Matrix4x4.Translate(at + new Vector3(-arm * 0.6f + 0.12f, armY - 0.45f, -0.08f)), 0.62f, 0.76f, key + 10);
			// The sandbag on its chain at the short end.
			Vector3 hook = at + new Vector3(arm * 0.4f + 0.1f, armY - 0.07f, 0f);
			d.Add(StructureShapes.Beam(hook, hook + Vector3.down * 0.42f, 0.03f, 0.03f, StructureMaterial.Iron, key + 14, StructureRole.Detail));
			d.Add(StructureShapes.Sphere(hook + Vector3.down * 0.7f, new Vector3(0.22f, 0.3f, 0.22f), d.Seg(8, 6), d.Mid ? 5 : 3, StructureMaterial.Canvas, key + 15));
		}

		// ── Furniture ─────────────────────────────────────────────────

		/// <summary>
		/// A soldier's bed, head to the wall (+z), running out along −z: v0 a two-tier bunk with a ladder up its foot, v1 two
		/// low camp cots (a canvas sling on two poles and crossed legs) side by side with a footlocker at their feet.
		/// </summary>
		private static void BunkBed(StructureDraft d)
		{
			if (d.Variant == 1)
			{
				for (int c = -1; c <= 1; c += 2)
				{
					float cx = c * 0.5f;
					int key = c > 0 ? 40 : 20;
					d.Add(StructureShapes.Box(new Vector3(cx, 0.37f, 0f), new Vector3(0.7f, 0.03f, 1.9f), StructureMaterial.Canvas, key));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(new Vector3(cx + s * 0.37f, 0.38f, -1f), new Vector3(cx + s * 0.37f, 0.38f, 1f), 0.05f, 0.05f, StructureMaterial.Timber, key + 1 + (s > 0 ? 1 : 0)));
					}
					for (int e = -1; e <= 1; e += 2)
					{
						float z = e * 0.85f;
						d.Add(StructureShapes.Beam(new Vector3(cx - 0.37f, 0f, z), new Vector3(cx + 0.37f, 0.36f, z), 0.04f, 0.04f, StructureMaterial.Timber, key + 3 + (e > 0 ? 2 : 0)));
						d.Add(StructureShapes.Beam(new Vector3(cx + 0.37f, 0f, z), new Vector3(cx - 0.37f, 0.36f, z), 0.04f, 0.04f, StructureMaterial.Timber, key + 4 + (e > 0 ? 2 : 0)));
					}
					if (d.Mid)
					{
						// A blanket folded at the foot, a rolled cloak for a pillow.
						d.Add(StructureShapes.Box(new Vector3(cx, 0.42f, -0.55f), new Vector3(0.64f, 0.07f, 0.55f), StructureMaterial.Dyed, key + 7, StructureRole.Detail));
						d.Add(StructureShapes.Cylinder(0.08f, -0.26f, 0.26f, d.Seg(8, 6), StructureMaterial.Canvas, key + 8, false, StructureRole.Detail)
							.Transform(Matrix4x4.TRS(new Vector3(cx, 0.46f, 0.75f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
					}
				}
				// The footlocker between the cots' feet.
				const float lockerZ = -1.27f;
				d.Add(StructureShapes.Box(new Vector3(0f, 0.21f, lockerZ), new Vector3(0.8f, 0.42f, 0.42f), StructureMaterial.Timber, 60)).Grain = Vector3.right;
				d.Add(StructureShapes.Box(new Vector3(0f, 0.445f, lockerZ), new Vector3(0.84f, 0.05f, 0.46f), StructureMaterial.Timber, 61)).Grain = Vector3.right;
				if (d.Mid)
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * 0.26f, 0.24f, lockerZ), new Vector3(0.05f, 0.5f, 0.48f), StructureMaterial.Iron, 62 + (s > 0 ? 1 : 0), StructureRole.Detail));
					}
					d.Add(StructureShapes.Box(new Vector3(0f, 0.36f, lockerZ - 0.23f), new Vector3(0.08f, 0.1f, 0.02f), StructureMaterial.Iron, 64, StructureRole.Detail));
				}
				return;
			}
			const float width = 1f, length = 2.2f, top = 1.9f;
			float hw = width * 0.5f, hl = length * 0.5f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(sx * (hw - 0.04f), top * 0.5f, sz * (hl - 0.04f)), new Vector3(0.08f, top, 0.08f), StructureMaterial.Timber, 1 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0))).Grain = Vector3.up;
				}
			}
			float[] tiers = { 0.3f, 1.35f };
			for (int t = 0; t < tiers.Length; t++)
			{
				float y = tiers[t];
				int key = 10 + t * 20;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (hw - 0.04f), y, 0f), new Vector3(0.06f, 0.14f, length - 0.16f), StructureMaterial.Timber, key + (s > 0 ? 1 : 0))).Grain = Vector3.forward;
					d.Add(StructureShapes.Box(new Vector3(0f, y, s * (hl - 0.04f)), new Vector3(width - 0.16f, 0.14f, 0.06f), StructureMaterial.Timber, key + 2 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
				}
				d.Add(StructureShapes.Box(new Vector3(0f, y + 0.02f, 0f), new Vector3(width - 0.14f, 0.04f, length - 0.14f), StructureMaterial.Timber, key + 4)).Grain = Vector3.forward;
				// The straw tick, and from the middle level the blanket over it and the pillow at its head.
				d.Add(StructureShapes.Box(new Vector3(0f, y + 0.11f, -0.02f), new Vector3(width - 0.18f, 0.14f, length - 0.22f), StructureMaterial.Thatch, key + 5));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, y + 0.2f, -0.35f), new Vector3(width - 0.14f, 0.04f, 1.2f), StructureMaterial.Dyed, key + 6, StructureRole.Detail));
					d.Add(StructureShapes.Box(new Vector3(0f, y + 0.23f, hl - 0.4f), new Vector3(0.55f, 0.1f, 0.3f), StructureMaterial.Canvas, key + 7, StructureRole.Detail));
				}
			}
			// A guard board along the upper bunk's open side, toward the head.
			d.Add(StructureShapes.Box(new Vector3(-(hw - 0.04f), 1.68f, 0.3f), new Vector3(0.05f, 0.14f, 1.3f), StructureMaterial.Timber, 60)).Grain = Vector3.forward;
			// The ladder up the foot: two rails and its rungs.
			float lz = -hl - 0.07f;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.22f, 0.9f, lz), new Vector3(0.05f, 1.8f, 0.05f), StructureMaterial.Timber, 61 + (s > 0 ? 1 : 0))).Grain = Vector3.up;
			}
			if (d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.55f + i * 0.33f, lz), new Vector3(0.42f, 0.04f, 0.04f), StructureMaterial.Timber, 63 + i, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// Armour on a stand, its back to the wall (+z), its front toward −z: a post on crossed feet with a bar for the shoulders;
		/// v0 a knee-long mail hauberk belted at the waist, a nasal helm on the post's head and a heater shield leaning on the foot;
		/// v1 a plate cuirass over its fauld, pauldrons, a great helm with its eye slit, a halberd leaning beside it.
		/// </summary>
		private static void ArmourStand(StructureDraft d)
		{
			bool plate = d.Variant == 1;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.62f, 0.08f, 0.1f), StructureMaterial.Timber, 1)).Grain = Vector3.right;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.08f, 0.5f), StructureMaterial.Timber, 2)).Grain = Vector3.forward;
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.08f, 0f), new Vector3(0f, 1.58f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, 3));
			d.Add(StructureShapes.Beam(new Vector3(-0.25f, 1.42f, 0f), new Vector3(0.25f, 1.42f, 0f), 0.06f, 0.06f, StructureMaterial.Timber, 4));
			int seg = d.Seg(10, 6);
			// The body a little flatter front to back than across.
			Matrix4x4 flat = Matrix4x4.Scale(new Vector3(1f, 1f, 0.72f));
			if (!plate)
			{
				var mail = new List<Vector2> { new Vector2(0.27f, 0.62f), new Vector2(0.2f, 0.98f), new Vector2(0.23f, 1.24f), new Vector2(0.21f, 1.42f), new Vector2(0.08f, 1.5f) };
				d.Add(StructureShapes.Lathe(mail, seg, StructureMaterial.Iron, 10).Transform(flat));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.12f, 1.56f), new Vector2(0.125f, 1.66f), new Vector2(0.08f, 1.77f), new Vector2(0f, 1.81f) }, seg, StructureMaterial.Iron, 20));
				if (d.Mid)
				{
					// Short sleeves, the belt, the helm's nasal.
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Rod(new Vector3(s * 0.18f, 1.4f, 0f), new Vector3(s * 0.33f, 1.02f, 0f), 0.07f, d.Seg(6, 4), StructureMaterial.Iron, 11 + (s > 0 ? 1 : 0)));
					}
					d.Add(StructureShapes.Cylinder(0.212f, 0.95f, 1.01f, seg, StructureMaterial.Timber, 13, false, StructureRole.Detail).Transform(flat));
					d.Add(StructureShapes.Box(new Vector3(0f, 1.6f, -0.125f), new Vector3(0.03f, 0.13f, 0.02f), StructureMaterial.Iron, 21, StructureRole.Detail));
				}
				// The shield leaning back on the stand's foot, at its right.
				GarrisonHeater(d, Matrix4x4.TRS(new Vector3(0.42f, 0.39f, -0.24f), Quaternion.Euler(14f, -18f, 0f), Vector3.one), 0.5f, 0.64f, 30);
				return;
			}
			var cuirass = new List<Vector2> { new Vector2(0.2f, 0.9f), new Vector2(0.215f, 1.08f), new Vector2(0.25f, 1.26f), new Vector2(0.22f, 1.42f), new Vector2(0.09f, 1.5f) };
			d.Add(StructureShapes.Lathe(cuirass, seg, StructureMaterial.Iron, 10).Transform(flat));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.27f, 0.72f), new Vector2(0.21f, 0.92f) }, seg, StructureMaterial.Iron, 11).Transform(flat));
			d.Add(StructureShapes.Cylinder(0.13f, 1.55f, 1.85f, seg, StructureMaterial.Iron, 20));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Sphere(new Vector3(s * 0.24f, 1.41f, 0f), new Vector3(0.12f, 0.1f, 0.11f), d.Seg(8, 6), 4, StructureMaterial.Iron, 12 + (s > 0 ? 1 : 0)));
				}
				// The great helm's eye slit, dark.
				d.Add(StructureShapes.Box(new Vector3(0f, 1.74f, -0.125f), new Vector3(0.2f, 0.025f, 0.02f), StructureMaterial.Ash, 21, StructureRole.Detail));
			}
			if (d.Fine)
			{
				// A crest of dyed horsehair along its crown.
				d.Add(StructureShapes.Box(new Vector3(0f, 1.89f, 0.02f), new Vector3(0.03f, 0.08f, 0.22f), StructureMaterial.Dyed, 22, StructureRole.Detail));
			}
			// The halberd leaning beside it: its haft, the spike, the axe blade and the hook behind.
			var foot = new Vector3(0.46f, 0f, -0.08f);
			var top = new Vector3(0.41f, 2.05f, 0.1f);
			Vector3 dir = (top - foot).normalized;
			d.Add(StructureShapes.Beam(foot, top, 0.035f, 0.035f, StructureMaterial.Timber, 30));
			d.Add(StructureShapes.Beam(top, top + dir * 0.3f, 0.025f, 0.025f, StructureMaterial.Iron, 31));
			Vector3 blade = top - dir * 0.1f;
			d.Add(StructureShapes.Beam(blade, blade + new Vector3(-0.2f, 0f, 0f), 0.012f, 0.22f, StructureMaterial.Iron, 32));
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(blade, blade + new Vector3(0.11f, 0.03f, 0f), 0.012f, 0.05f, StructureMaterial.Iron, 33, StructureRole.Detail));
			}
		}
	}
}
#endif
