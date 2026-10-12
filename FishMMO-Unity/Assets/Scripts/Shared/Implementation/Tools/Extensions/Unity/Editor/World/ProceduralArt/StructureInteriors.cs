#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One piece of furniture placed in a room, in the building piece's own frame.</summary>
	public struct InteriorItem
	{
		public string Piece;
		public int Variant;
		/// <summary>On the room's floor, in the building's frame.</summary>
		public Vector3 Position;
		/// <summary>Degrees about up, in the building's frame: the piece's −z faces into the room.</summary>
		public float Yaw;
	}

	/// <summary>
	/// Furnishes a building's room (2026-10-11: every building with a door gets an interior): a recipe per
	/// <see cref="StructureRoomKind"/> laid against the walls (backs to the wall, facing in), in the middle with room to walk
	/// round, and seats round tables; the doors' swing and the way in from each door kept clear.
	/// </summary>
	/// <remarks>
	/// Pure: the room, its doors, a seed and the pieces' footprints in, the placements out; the same building furnished
	/// the same at every cut. Rectangular rooms place on quarter turns (boxes against boxes), round rooms round the wall
	/// (circles against circles).
	/// </remarks>
	public static class StructureInteriors
	{
		/// <summary>Space kept between pieces, metres.</summary>
		private const float Gap = 0.08f;
		/// <summary>Space kept round a piece in the middle of a room, to walk round it.</summary>
		private const float Aisle = 0.55f;
		/// <summary>How far in from a door the way in is kept clear, metres.</summary>
		private const float Approach = 1.4f;

		private enum Mode { Wall, Centre, BackCentre }

		private struct Want
		{
			public string Piece;
			public int Min, Max;
			public Mode Mode;
			/// <summary>-1: either variant at random.</summary>
			public int Variant;
			/// <summary>Seats round it (tables).</summary>
			public int Seats;
		}

		private struct Shape
		{
			public bool Circle;
			public Vector2 Min, Max;
			public Vector2 Centre;
			public float Radius;

			public static Shape Box(Vector2 centre, Vector2 size) => new Shape { Min = centre - size * 0.5f, Max = centre + size * 0.5f, Centre = centre };
			public static Shape Round(Vector2 centre, float radius) => new Shape { Circle = true, Centre = centre, Radius = radius };

			public bool Overlaps(in Shape o, float gap)
			{
				if (!Circle && !o.Circle)
				{
					return Min.x < o.Max.x + gap && o.Min.x < Max.x + gap && Min.y < o.Max.y + gap && o.Min.y < Max.y + gap;
				}
				if (Circle && o.Circle)
				{
					float r = Radius + o.Radius + gap;
					return (Centre - o.Centre).sqrMagnitude < r * r;
				}
				Shape c = Circle ? this : o, b = Circle ? o : this;
				Vector2 nearest = new Vector2(Mathf.Clamp(c.Centre.x, b.Min.x, b.Max.x), Mathf.Clamp(c.Centre.y, b.Min.y, b.Max.y));
				float rr = c.Radius + gap;
				return (nearest - c.Centre).sqrMagnitude < rr * rr;
			}
		}

		private static Want W(string piece, int min, int max, Mode mode, int variant = -1, int seats = 0) =>
			new Want { Piece = piece, Min = min, Max = max, Mode = mode, Variant = variant, Seats = seats };

		/// <summary>What a room of a kind holds, most important first (placed first, so a small room keeps the essentials).</summary>
		private static Want[] Recipe(StructureRoomKind kind, float area, bool longRoom)
		{
			switch (kind)
			{
				case StructureRoomKind.Dwelling:
					if (area < 14f)
					{
						// A one-room hut: a bed first, then the hearth and a chest as there is room.
						return new[] { W("Bed", 1, 1, Mode.Wall, 0), W("Hearth", 1, 1, Mode.Wall), W("Chest", 1, 1, Mode.Wall), W("Barrel", 0, 1, Mode.Wall) };
					}
					return new[] { W("Hearth", 1, 1, Mode.Wall), W("Bed", 1, area > 22f ? 2 : 1, Mode.Wall), W("Table", 1, 1, Mode.Centre, -1, 2),
						W("Chest", 1, 1, Mode.Wall), W("Shelf", 0, 1, Mode.Wall), W("Barrel", 0, 1, Mode.Wall) };
				case StructureRoomKind.Hall:
					// A long hall (a longhouse) has its fire down the middle; a squarer stone hall a hearth on the wall.
					return new[] { W("CampfireRing", longRoom ? 1 : 0, longRoom ? 1 : 0, Mode.Centre, 0), W("Table", 1, area > 40f ? 2 : 1, Mode.Centre, 0, 4),
						W("Bed", 2, 4, Mode.Wall), W("Chest", 1, 2, Mode.Wall), W("WeaponRack", 0, 1, Mode.Wall), W("Barrel", 1, 2, Mode.Wall), W("Hearth", longRoom ? 0 : 1, longRoom ? 0 : 1, Mode.Wall) };
				case StructureRoomKind.Tavern:
					return new[] { W("BarCounter", 1, 1, Mode.Wall), W("Barrel", 2, 3, Mode.Wall), W("Hearth", 1, 1, Mode.Wall), W("Table", 2, 3, Mode.Centre, -1, 3),
						W("Shelf", 1, 1, Mode.Wall) };
				case StructureRoomKind.Temple:
					return new[] { W("Statue", 1, 1, Mode.BackCentre), W("Altar", 1, 1, Mode.Centre, 0) };
				case StructureRoomKind.Tomb:
					return new[] { W("Sarcophagus", 1, 1, Mode.BackCentre) };
				case StructureRoomKind.Guard:
					return new[] { W("WeaponRack", 1, 1, Mode.Wall), W("Table", area > 6f ? 1 : 0, area > 6f ? 1 : 0, Mode.Centre, 1, 2), W("Chest", 1, 1, Mode.Wall),
						W("Crate", 1, 2, Mode.Wall), W("Barrel", 1, 1, Mode.Wall), W("Bed", area > 9f ? 1 : 0, area > 9f ? 1 : 0, Mode.Wall, 0) };
				case StructureRoomKind.Keeper:
					return new[] { W("Bed", 1, 1, Mode.Wall), W("Table", 1, 1, Mode.Centre, 1, 1), W("Chest", 1, 1, Mode.Wall), W("Shelf", 1, 1, Mode.Wall),
						W("Barrel", 1, 2, Mode.Wall) };
				case StructureRoomKind.Stable:
					return new[] { W("HayBales", 1, 2, Mode.Wall), W("WaterButt", 1, 1, Mode.Wall), W("Barrel", 0, 1, Mode.Wall) };
				case StructureRoomKind.Chamber:
					// Upstairs in a house: where the family sleeps and keeps its things.
					return new[] { W("Bed", 1, area > 20f ? 2 : 1, Mode.Wall), W("Chest", 1, 2, Mode.Wall), W("Shelf", 0, 1, Mode.Wall),
						W("Table", area > 16f ? 1 : 0, area > 16f ? 1 : 0, Mode.Centre, 1, 1), W("Barrel", 0, 1, Mode.Wall) };
				case StructureRoomKind.Lodging:
					// Upstairs in a tavern: its guests' beds round the walls and a table among them.
					return new[] { W("Bed", 2, area > 30f ? 4 : 3, Mode.Wall), W("Chest", 1, 2, Mode.Wall), W("Table", 1, 1, Mode.Centre, -1, 2),
						W("Shelf", 0, 1, Mode.Wall) };
				default:
					return Array.Empty<Want>();
			}
		}

		/// <summary>
		/// The furniture for one room of a building (its frame), with its doors' keep-outs. <paramref name="footprint"/> gives a
		/// piece's (x, z) size, or zero for a piece that does not exist (it is skipped).
		/// </summary>
		public static List<InteriorItem> Plan(StructureRoom room, IReadOnlyList<StructureDoor> doors, int seed, Func<string, Vector2> footprint)
		{
			var placed = new List<InteriorItem>();
			var taken = new List<Shape>();
			var rng = new DeterministicRNG(seed);
			float floor = room.Centre.y;
			var centre = new Vector2(room.Centre.x, room.Centre.z);
			float halfX = room.Size.x * 0.5f, halfZ = room.Size.y * 0.5f, radius = room.Size.x * 0.5f;
			Vector2 roomMin = centre - new Vector2(halfX, halfZ), roomMax = centre + new Vector2(halfX, halfZ);

			// What the doors need clear: the leaf's swing and the way in.
			var keepOut = new List<Shape>();
			foreach (StructureDoor door in doors)
			{
				var hinge = new Vector2(door.Hinge.x, door.Hinge.z);
				var across = new Vector2(door.Across.x, door.Across.z);
				var inward = new Vector2(door.Inward.x, door.Inward.z);
				if (hinge.x < roomMin.x - 1f || hinge.x > roomMax.x + 1f || hinge.y < roomMin.y - 1f || hinge.y > roomMax.y + 1f || Mathf.Abs(door.Hinge.y - floor) > 0.6f)
				{
					continue; // another room's door
				}
				keepOut.Add(Bounds(hinge, hinge + across * door.Width, hinge + inward * door.Width, hinge + across * door.Width + inward * door.Width, 0.1f));
				Vector2 mid = hinge + across * (door.Width * 0.5f);
				// The way in: 1.4 m, or a third of a small room's depth (a hut's bed may come close to its door).
				float approach = Mathf.Min(Approach, 0.3f * Mathf.Min(room.Size.x, room.Size.y));
				keepOut.Add(Bounds(mid - across * (door.Width * 0.5f + 0.25f), mid + across * (door.Width * 0.5f + 0.25f),
					mid - across * (door.Width * 0.5f + 0.25f) + inward * approach, mid + across * (door.Width * 0.5f + 0.25f) + inward * approach, 0f));
			}

			// What the building keeps clear: a stair and its landing, the well it climbs through.
			if (room.Clear != null)
			{
				foreach (Vector4 c in room.Clear)
				{
					var min = new Vector2(c.x, c.y);
					var max = new Vector2(c.z, c.w);
					keepOut.Add(Shape.Box((min + max) * 0.5f, max - min));
				}
			}

			bool Fits(in Shape s)
			{
				if (room.Round)
				{
					float r = s.Circle ? s.Radius : (s.Max - s.Min).magnitude * 0.5f;
					Vector2 c = s.Circle ? s.Centre : (s.Min + s.Max) * 0.5f;
					if ((c - centre).magnitude + r > radius + 1e-3f)
					{
						return false;
					}
				}
				else if (s.Min.x < roomMin.x - 1e-3f || s.Min.y < roomMin.y - 1e-3f || s.Max.x > roomMax.x + 1e-3f || s.Max.y > roomMax.y + 1e-3f)
				{
					return false;
				}
				foreach (Shape k in keepOut)
				{
					if (s.Overlaps(k, 0f))
					{
						return false;
					}
				}
				foreach (Shape t in taken)
				{
					if (s.Overlaps(t, Gap))
					{
						return false;
					}
				}
				return true;
			}

			void Put(string piece, int variant, Vector2 at, float yaw, in Shape shape, float clearance)
			{
				placed.Add(new InteriorItem { Piece = piece, Variant = variant, Position = new Vector3(at.x, floor, at.y), Yaw = yaw });
				Shape kept = shape;
				if (clearance > 0f)
				{
					kept = shape.Circle ? Shape.Round(shape.Centre, shape.Radius + clearance) : Shape.Box((shape.Min + shape.Max) * 0.5f, shape.Max - shape.Min + Vector2.one * (2f * clearance));
				}
				taken.Add(kept);
			}

			// The church's own layout: the altar at the far wall, a lectern beside it, pews either side of an aisle facing it.
			if (room.Kind == StructureRoomKind.Church && !room.Round)
			{
				FurnishChurch(centre, roomMin, roomMax, floor, footprint, placed, taken, keepOut);
				return placed;
			}
			if (room.Kind == StructureRoomKind.Stable && !room.Round)
			{
				Vector2 stall = footprint("StallPartition");
				if (stall != Vector2.zero)
				{
					// Stalls along the back wall, a partition every 2.4 m, their open ends to the room.
					for (float x = roomMin.x + 2.4f; x < roomMax.x - 0.5f; x += 2.4f)
					{
						var at = new Vector2(x, roomMax.y - stall.y * 0.5f - 0.02f);
						Shape s = Shape.Box(at, new Vector2(Mathf.Max(0.2f, stall.x), stall.y));
						if (Fits(s))
						{
							Put("StallPartition", (int)(rng.NextFloat() * 2f) % 2, at, 0f, s, 0f);
						}
					}
				}
			}

			float area = room.Round ? Mathf.PI * radius * radius : room.Size.x * room.Size.y;
			bool longRoom = !room.Round && Mathf.Max(room.Size.x, room.Size.y) > 2.2f * Mathf.Min(room.Size.x, room.Size.y);
			foreach (Want want in Recipe(room.Kind, area, longRoom))
			{
				Vector2 size = footprint(want.Piece);
				if (size == Vector2.zero)
				{
					continue;
				}
				// The fireplace against the chimney breast, where the building put it (its smoke leaves by that chimney).
				if (want.Piece == "Hearth" && room.HasHearth && want.Max > 0)
				{
					float hr = room.HearthYaw * Mathf.Deg2Rad;
					bool turned = Mathf.Abs(Mathf.Sin(hr)) > 0.5f;
					Shape hearth = Shape.Box(room.Hearth, turned ? new Vector2(size.y, size.x) : size);
					Put(want.Piece, want.Variant >= 0 ? want.Variant : (int)(rng.NextFloat() * 2f) % 2, room.Hearth, room.HearthYaw, hearth, 0f);
					continue;
				}
				int count = want.Min + (want.Max > want.Min ? (int)(rng.NextFloat() * (want.Max - want.Min + 1)) % (want.Max - want.Min + 1) : 0);
				for (int n = 0; n < count; n++)
				{
					int variant = want.Variant >= 0 ? want.Variant : (int)(rng.NextFloat() * 2f) % 2;
					bool ok = want.Mode == Mode.Wall
						? (room.Round ? PlaceRound(size, want.Piece, variant) : PlaceAgainstWall(size, want.Piece, variant))
						: PlaceInMiddle(size, want, variant);
					if (!ok)
					{
						break;
					}
				}
			}
			return placed;

			// ── Placement ──

			bool PlaceAgainstWall(Vector2 size, string piece, int variant)
			{
				// Walls in a seeded order, the back wall (far from the door, +z) first most of the time.
				var walls = new List<Vector2> { new Vector2(0f, -1f), new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f) };
				for (int i = walls.Count - 1; i > 0; i--)
				{
					int j = (int)(rng.NextFloat() * (i + 1)) % (i + 1);
					(walls[i], walls[j]) = (walls[j], walls[i]);
				}
				if (rng.NextFloat() < 0.6f)
				{
					walls.Remove(new Vector2(0f, -1f));
					walls.Insert(0, new Vector2(0f, -1f));
				}
				foreach (Vector2 inward in walls)
				{
					bool alongX = Mathf.Abs(inward.y) > 0.5f;
					Vector2 rotated = alongX ? size : new Vector2(size.y, size.x);
					float lo = alongX ? roomMin.x : roomMin.y, hi = alongX ? roomMax.x : roomMax.y;
					float half = alongX ? rotated.x * 0.5f : rotated.y * 0.5f;
					var slots = new List<float>();
					for (float t = lo + half + 0.05f; t <= hi - half - 0.05f; t += 0.25f)
					{
						slots.Add(t);
					}
					Shuffle(slots);
					// The wall's line, and the piece set out from it by half its depth.
					float wallLine = inward.y < -0.5f ? roomMax.y : inward.y > 0.5f ? roomMin.y : inward.x > 0.5f ? roomMin.x : roomMax.x;
					float depth = alongX ? rotated.y : rotated.x;
					float yaw = Mathf.Atan2(-inward.x, -inward.y) * Mathf.Rad2Deg;
					foreach (float t in slots)
					{
						Vector2 at = alongX ? new Vector2(t, wallLine + inward.y * (depth * 0.5f + 0.02f)) : new Vector2(wallLine + inward.x * (depth * 0.5f + 0.02f), t);
						Shape s = Shape.Box(at, rotated);
						if (Fits(s))
						{
							Put(piece, variant, at, yaw, s, 0f);
							return true;
						}
					}
				}
				return false;
			}

			bool PlaceRound(Vector2 size, string piece, int variant)
			{
				float r = Mathf.Max(size.x, size.y) * 0.5f;
				var angles = new List<float>();
				for (int a = 0; a < 36; a++)
				{
					angles.Add(a * 10f * Mathf.Deg2Rad);
				}
				Shuffle(angles);
				foreach (float a in angles)
				{
					var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
					Vector2 at = centre + dir * (radius - r - 0.05f);
					Shape s = Shape.Round(at, r);
					if (Fits(s))
					{
						// Its back to the wall: its +z points out along dir.
						Put(piece, variant, at, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, s, 0f);
						return true;
					}
				}
				return false;
			}

			bool PlaceInMiddle(Vector2 size, in Want want, int variant)
			{
				// A long room's tables run along it; otherwise as built.
				bool turn = !room.Round && room.Size.y > room.Size.x * 1.3f && size.x > size.y * 1.3f;
				Vector2 rotated = turn ? new Vector2(size.y, size.x) : size;
				float yaw = turn ? 90f : 0f;
				var candidates = new List<Vector2>();
				if (want.Mode == Mode.BackCentre && !room.Round)
				{
					// Against the back wall's middle: as built (its long side toward the door), else turned to lie along the wall.
					foreach (bool across in new[] { false, true })
					{
						Vector2 r2 = across ? new Vector2(size.y, size.x) : size;
						var at = new Vector2(centre.x, roomMax.y - r2.y * 0.5f - 0.05f);
						Shape s2 = Shape.Box(at, r2);
						if (Fits(s2))
						{
							Put(want.Piece, variant, at, across ? 90f : 0f, s2, 0.25f);
							return true;
						}
					}
					return false;
				}
				else
				{
					float step = 0.25f;
					for (float x = roomMin.x + rotated.x * 0.5f + Aisle; x <= roomMax.x - rotated.x * 0.5f - Aisle + 1e-3f; x += step)
					{
						for (float z = roomMin.y + rotated.y * 0.5f + Aisle; z <= roomMax.y - rotated.y * 0.5f - Aisle + 1e-3f; z += step)
						{
							candidates.Add(new Vector2(x, z));
						}
					}
					if (candidates.Count == 0)
					{
						candidates.Add(centre);
					}
					// Nearest the middle (a little toward the back) first, ties broken by the seed.
					Vector2 aim = centre + new Vector2(0f, room.Size.y * 0.1f);
					var keyed = new List<(float, Vector2)>();
					foreach (Vector2 c in candidates)
					{
						keyed.Add(((c - aim).sqrMagnitude + rng.NextFloat() * 0.3f, c));
					}
					keyed.Sort((a, b) => a.Item1.CompareTo(b.Item1));
					candidates.Clear();
					foreach ((float _, Vector2 c) in keyed)
					{
						candidates.Add(c);
					}
				}
				foreach (Vector2 at in candidates)
				{
					Shape s = room.Round ? Shape.Round(at, Mathf.Max(rotated.x, rotated.y) * 0.5f) : Shape.Box(at, rotated);
					if (!Fits(s))
					{
						continue;
					}
					Put(want.Piece, variant, at, yaw, s, want.Seats > 0 ? 0f : 0.25f);
					if (want.Seats > 0)
					{
						Seat(at, rotated, want.Seats);
						// Then the table and its seats keep a walkway round them.
						taken.Add(Shape.Box(at, rotated + Vector2.one * 1.3f));
					}
					return true;
				}
				return false;
			}

			void Seat(Vector2 table, Vector2 size, int seats)
			{
				Vector2 stool = footprint("Stool");
				if (stool == Vector2.zero)
				{
					return;
				}
				// Round the long sides first, then the ends; each seat faces the table.
				var spots = new List<(Vector2 at, float yaw)>();
				bool longX = size.x >= size.y;
				float sx = size.x * 0.5f + stool.y * 0.5f + 0.1f, sz = size.y * 0.5f + stool.y * 0.5f + 0.1f;
				if (longX)
				{
					spots.Add((table + new Vector2(-size.x * 0.25f, -sz), 0f));
					spots.Add((table + new Vector2(size.x * 0.25f, sz), 180f));
					spots.Add((table + new Vector2(size.x * 0.25f, -sz), 0f));
					spots.Add((table + new Vector2(-size.x * 0.25f, sz), 180f));
					spots.Add((table + new Vector2(-sx, 0f), 90f));
					spots.Add((table + new Vector2(sx, 0f), -90f));
				}
				else
				{
					spots.Add((table + new Vector2(-sx, -size.y * 0.25f), 90f));
					spots.Add((table + new Vector2(sx, size.y * 0.25f), -90f));
					spots.Add((table + new Vector2(sx, -size.y * 0.25f), -90f));
					spots.Add((table + new Vector2(-sx, size.y * 0.25f), 90f));
					spots.Add((table + new Vector2(0f, -sz), 0f));
					spots.Add((table + new Vector2(0f, sz), 180f));
				}
				int made = 0;
				foreach ((Vector2 at, float yaw) in spots)
				{
					if (made >= seats)
					{
						break;
					}
					Shape s = Shape.Box(at, stool);
					if (Fits(s))
					{
						// A seat faces the table: its front (−z) toward it, so its yaw is the table's way + 180.
						Put("Stool", (int)(rng.NextFloat() * 2f) % 2, at, yaw + 180f, s, 0f);
						made++;
					}
				}
			}

			void Shuffle<T>(List<T> list)
			{
				for (int i = list.Count - 1; i > 0; i--)
				{
					int j = (int)(rng.NextFloat() * (i + 1)) % (i + 1);
					(list[i], list[j]) = (list[j], list[i]);
				}
			}
		}

		private static Shape Bounds(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float pad)
		{
			Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)) - Vector2.one * pad;
			Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)) + Vector2.one * pad;
			return new Shape { Min = min, Max = max, Centre = (min + max) * 0.5f };
		}

		private static void FurnishChurch(Vector2 centre, Vector2 roomMin, Vector2 roomMax, float floor, Func<string, Vector2> footprint,
			List<InteriorItem> placed, List<Shape> taken, List<Shape> keepOut)
		{
			Vector2 altar = footprint("Altar"), pew = footprint("Pew"), lectern = footprint("Lectern");
			float altarZ = roomMax.y - 0.6f;
			if (altar != Vector2.zero)
			{
				placed.Add(new InteriorItem { Piece = "Altar", Variant = 0, Position = new Vector3(centre.x, floor, altarZ - altar.y * 0.5f), Yaw = 0f });
			}
			if (lectern != Vector2.zero)
			{
				placed.Add(new InteriorItem { Piece = "Lectern", Variant = 0, Position = new Vector3(centre.x + Mathf.Min(1.8f, (roomMax.x - roomMin.x) * 0.3f), floor, altarZ - 1.6f), Yaw = 200f });
			}
			if (pew == Vector2.zero)
			{
				return;
			}
			// Two columns either side of a 1.2 m aisle, facing the altar (+z): a pew's front (−z) turned to +z.
			const float aisle = 1.2f, row = 1.05f;
			float span = Mathf.Min(pew.x, (roomMax.x - roomMin.x - aisle) * 0.5f - 0.15f);
			if (span < 1f)
			{
				return;
			}
			float front = roomMin.y + Approach + 0.3f, back = altarZ - 2.4f;
			int index = 0;
			for (float z = back; z >= front; z -= row)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					float x = centre.x + s * (aisle * 0.5f + span * 0.5f);
					var at = new Vector2(x, z);
					Shape shape = Shape.Box(at, new Vector2(span, pew.y));
					bool clear = true;
					foreach (Shape k in keepOut)
					{
						clear &= !shape.Overlaps(k, 0f);
					}
					if (clear)
					{
						placed.Add(new InteriorItem { Piece = "Pew", Variant = index++ % 3 == 2 ? 1 : 0, Position = new Vector3(x, floor, z), Yaw = 180f });
						taken.Add(shape);
					}
				}
			}
		}
	}
}
#endif
