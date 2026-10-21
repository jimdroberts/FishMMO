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
		/// <summary>
		/// Half the width a player needs to pass, metres: the controller's capsule (0.3) and a hand's breadth. Every part of a
		/// room's floor must stay reachable from its way in by a body this wide (<see cref="Plan"/>'s walk check).
		/// </summary>
		public const float WalkRadius = 0.32f;
		/// <summary>The walk check's grid, metres.</summary>
		private const float WalkCell = 0.1f;
		/// <summary>
		/// How much nearer than its line the walk check takes a room's wall to stand, metres: a round wall is a ring of flat
		/// blocks whose faces stand a few centimetres inside its circle, and a way along a wall no wider than that is no way.
		/// </summary>
		private const float WallSlack = 0.08f;
		/// <summary>A pocket of floor smaller than this the walk check lets be cut off, in grid cells (a corner behind a chest).</summary>
		private const int WalkPocket = 30;
		/// <summary>How far above a room's floor a door may stand and still be its way in (a keep's door on its landing), metres.</summary>
		private const float LandingDoor = 1f;

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

		/// <summary>Whether a piece of furniture stops a player (a stool does not: Collides false).</summary>
		private static bool Collides(string piece)
		{
			StructurePiece p = StructurePieces.Find(piece);
			return p == null || p.Collides;
		}

		/// <summary>
		/// A room's floor on a <see cref="WalkCell"/> grid, for the walk check: which cells a body <see cref="WalkRadius"/>
		/// wide can stand on (inside the walls by its radius, clear of every solid piece by it), and whether those are all one
		/// floor reachable from the way in.
		/// </summary>
		private sealed class WalkGrid
		{
			private readonly int nx, nz;
			private readonly Vector2 origin;
			private readonly bool[] inside;
			private readonly bool[] free;
			private readonly int[] queue;

			public WalkGrid(StructureRoom room, Vector2 centre, Vector2 roomMin, Vector2 roomMax, float radius)
			{
				origin = roomMin;
				nx = Mathf.Max(1, Mathf.CeilToInt((roomMax.x - roomMin.x) / WalkCell));
				nz = Mathf.Max(1, Mathf.CeilToInt((roomMax.y - roomMin.y) / WalkCell));
				inside = new bool[nx * nz];
				free = new bool[nx * nz];
				queue = new int[nx * nz];
				for (int z = 0; z < nz; z++)
				{
					for (int x = 0; x < nx; x++)
					{
						Vector2 p = Cell(x, z);
						float edge = WalkRadius + WallSlack;
						inside[z * nx + x] = room.Round
							? (p - centre).magnitude <= radius - edge
							: p.x >= roomMin.x + edge && p.x <= roomMax.x - edge && p.y >= roomMin.y + edge && p.y <= roomMax.y - edge;
					}
				}
			}

			private Vector2 Cell(int x, int z) => origin + new Vector2((x + 0.5f) * WalkCell, (z + 0.5f) * WalkCell);

			/// <summary>
			/// With <paramref name="extra"/> laid among <paramref name="solid"/>: whether every standable cell (all but pockets
			/// under <see cref="WalkPocket"/> cells) is reachable from an entry (a door's way in), or, with none, from the
				/// clear areas (an upper floor's stair landing). True when there is nowhere to start from.
			/// </summary>
			public bool Connected(List<Laid> solid, in Laid extra, List<Vector2> entries, List<Shape> clears)
			{
				int standable = 0;
				for (int z = 0; z < nz; z++)
				{
					for (int x = 0; x < nx; x++)
					{
						int i = z * nx + x;
						bool ok = inside[i];
						if (ok)
						{
							Vector2 p = Cell(x, z);
							ok = !Near(extra, p);
							for (int k = 0; ok && k < solid.Count; k++)
							{
								ok = !Near(solid[k], p);
							}
						}
						free[i] = ok;
						if (ok)
						{
							standable++;
						}
					}
				}
				// Seed from the ways in: the nearest standable cell to each entry, else every standable cell in a clear area.
				int head = 0, tail = 0;
				var seen = new bool[nx * nz];
				void Push(int i)
				{
					if (i >= 0 && free[i] && !seen[i])
					{
						seen[i] = true;
						queue[tail++] = i;
					}
				}
				foreach (Vector2 entry in entries)
				{
					Push(Nearest(entry));
				}
				if (entries.Count == 0)
				{
					for (int z = 0; z < nz; z++)
					{
						for (int x = 0; x < nx; x++)
						{
							Vector2 p = Cell(x, z);
							foreach (Shape c in clears)
							{
								if (p.x >= c.Min.x && p.x <= c.Max.x && p.y >= c.Min.y && p.y <= c.Max.y)
								{
									Push(z * nx + x);
									break;
								}
							}
						}
					}
				}
				if (tail == 0)
				{
					// Nowhere to start from (no way in marked, or none standable even empty): nothing to judge by.
					return true;
				}
				while (head < tail)
				{
					int i = queue[head++];
					int x = i % nx, z = i / nx;
					if (x > 0) Push(i - 1);
					if (x < nx - 1) Push(i + 1);
					if (z > 0) Push(i - nx);
					if (z < nz - 1) Push(i + nx);
				}
				if (tail == standable)
				{
					return true;
				}
				// What was not reached: allowed only as small pockets.
				for (int start = 0; start < free.Length; start++)
				{
					if (!free[start] || seen[start])
					{
						continue;
					}
					int size = 0;
					head = tail = 0;
					Push(start);
					while (head < tail)
					{
						int i = queue[head++];
						size++;
						int x = i % nx, z = i / nx;
						if (x > 0) Push(i - 1);
						if (x < nx - 1) Push(i + 1);
						if (z > 0) Push(i - nx);
						if (z < nz - 1) Push(i + nx);
					}
					if (size >= WalkPocket)
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>The standable cell nearest a point (within a metre), or −1.</summary>
			private int Nearest(Vector2 at)
			{
				int best = -1;
				float bestD = 1f;
				int cx = Mathf.FloorToInt((at.x - origin.x) / WalkCell), cz = Mathf.FloorToInt((at.y - origin.y) / WalkCell);
				int reach = Mathf.CeilToInt(1f / WalkCell);
				for (int z = Mathf.Max(0, cz - reach); z <= Mathf.Min(nz - 1, cz + reach); z++)
				{
					for (int x = Mathf.Max(0, cx - reach); x <= Mathf.Min(nx - 1, cx + reach); x++)
					{
						int i = z * nx + x;
						if (!free[i])
						{
							continue;
						}
						float d = (Cell(x, z) - at).magnitude;
						if (d < bestD)
						{
							bestD = d;
							best = i;
						}
					}
				}
				return best;
			}

			/// <summary>Whether a body standing at <paramref name="p"/> would touch the piece.</summary>
			private static bool Near(in Laid s, Vector2 p)
			{
				Vector2 d = p - s.Centre;
				var local = new Vector2(d.x * s.Cos - d.y * s.Sin, d.x * s.Sin + d.y * s.Cos);
				var nearest = new Vector2(Mathf.Clamp(local.x, -s.Half.x, s.Half.x), Mathf.Clamp(local.y, -s.Half.y, s.Half.y));
				return (nearest - local).sqrMagnitude < WalkRadius * WalkRadius;
			}
		}

		/// <summary>
		/// A piece's footprint as laid, for the walk check: its box turned by its yaw, not the circle a round room fits it by
		/// (a bed's corners stand off a curved wall, and the floor between can be shut in).
		/// </summary>
		private struct Laid
		{
			public Vector2 Centre, Half;
			public float Cos, Sin;

			public static Laid Of(Vector2 at, Vector2 size, float yawDegrees)
			{
				float a = yawDegrees * Mathf.Deg2Rad;
				return new Laid { Centre = at, Half = size * 0.5f, Cos = Mathf.Cos(a), Sin = Mathf.Sin(a) };
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
						W("Crate", 1, 2, Mode.Wall), W("Barrel", 1, 1, Mode.Wall), W("Bed", area > 9f ? 1 : 0, area > 9f ? 1 : 0, Mode.Wall, 0),
						// Last (2026-10-19), so every earlier piece of a guard room lies where it did: an armour stand where there is room.
						W("ArmourStand", 0, area > 12f ? 1 : 0, Mode.Wall) };
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
				case StructureRoomKind.Mill:
					// The meal floor of a tower mill: what is ground and waiting round the wall, the miller's tally in the middle.
					return new[] { W("Crate", 2, 3, Mode.Wall), W("Barrel", 1, 2, Mode.Wall), W("Chest", 1, 1, Mode.Wall), W("Shelf", 0, 1, Mode.Wall),
						W("Table", area > 14f ? 1 : 0, area > 14f ? 1 : 0, Mode.Centre, 1, 1) };
				// The town's other buildings (2026-10-19).
				case StructureRoomKind.Study:
					// A wizard's room: where the work is done first, then the books, then where they sleep.
					return new[] { W("Desk", 1, 1, Mode.Wall), W("Bookcase", 1, area > 20f ? 3 : 2, Mode.Wall), W("ArcaneFocus", area > 12f ? 1 : 0, 1, Mode.Centre),
						W("AlchemyBench", 0, 1, Mode.Wall), W("Chest", 1, 1, Mode.Wall), W("Bed", area > 16f ? 1 : 0, area > 16f ? 1 : 0, Mode.Wall, 1) };
				case StructureRoomKind.Library:
					return new[] { W("Bookcase", 3, area > 60f ? 10 : area > 30f ? 6 : 4, Mode.Wall), W("Table", 1, area > 50f ? 2 : 1, Mode.Centre, 0, 4),
						W("Lectern", 0, 1, Mode.Centre, 0), W("Desk", 1, 2, Mode.Wall), W("Chest", 0, 1, Mode.Wall) };
				case StructureRoomKind.Barracks:
					return new[] { W("BunkBed", 2, area > 60f ? 8 : area > 30f ? 5 : 3, Mode.Wall), W("WeaponRack", 1, 2, Mode.Wall), W("ArmourStand", 0, 2, Mode.Wall),
						W("Table", 1, 1, Mode.Centre, 0, 4), W("Chest", 1, 2, Mode.Wall), W("Barrel", 0, 1, Mode.Wall) };
				case StructureRoomKind.Infirmary:
					// The sick on pallets round the walls, the healer's herbs and table among them.
					return new[] { W("Bed", 2, area > 40f ? 6 : area > 20f ? 4 : 2, Mode.Wall, 0), W("HerbShelf", 1, 2, Mode.Wall), W("Table", 1, 1, Mode.Centre, 0, 1),
						W("Hearth", 0, 1, Mode.Wall), W("Chest", 1, 1, Mode.Wall), W("WaterButt", 0, 1, Mode.Wall) };
				case StructureRoomKind.Barn:
					return new[] { W("HayBales", 2, area > 120f ? 5 : 3, Mode.Wall), W("Sacks", 1, 3, Mode.Wall), W("Crate", 1, 2, Mode.Wall), W("Barrel", 0, 2, Mode.Wall),
						W("Cart", area > 100f ? 1 : 0, area > 100f ? 1 : 0, Mode.Centre) };
				case StructureRoomKind.Bakery:
					return new[] { W("Hearth", 0, 1, Mode.Wall), W("Table", 1, area > 30f ? 2 : 1, Mode.Centre, 0), W("Sacks", 1, 2, Mode.Wall), W("Shelf", 1, 2, Mode.Wall),
						W("Barrel", 1, 1, Mode.Wall) };
				case StructureRoomKind.Workshop:
					return new[] { W("Workbench", 1, area > 30f ? 2 : 1, Mode.Wall), W("Crate", 1, 2, Mode.Wall), W("Barrel", 1, 2, Mode.Wall), W("Shelf", 0, 1, Mode.Wall) };
				case StructureRoomKind.Storehouse:
					return new[] { W("Crate", 3, area > 80f ? 8 : 5, Mode.Wall), W("Barrel", 2, area > 80f ? 5 : 3, Mode.Wall), W("Sacks", 1, 3, Mode.Wall) };
				case StructureRoomKind.Shop:
					return new[] { W("BarCounter", 1, 1, Mode.Wall, 0), W("Shelf", 1, 2, Mode.Wall), W("Crate", 1, 2, Mode.Wall), W("Barrel", 0, 1, Mode.Wall) };
				// A shop-house let to a trade (2026-10-19, PointOfInterestTrades): the trade's bench first, then its stocked shelves
				// (TradeShelf, the trade's own variant), then what it keeps its stuff in.
				case StructureRoomKind.Tailor:
					return new[] { W("CuttingTable", 1, 1, Mode.Wall), W("DressForm", 1, area > 20f ? 2 : 1, Mode.Wall), W("TradeShelf", 1, area > 30f ? 3 : 2, Mode.Wall, (int)StructureTrade.Tailor),
						W("BarCounter", 0, 1, Mode.Wall, 0), W("Chest", 0, 1, Mode.Wall) };
				case StructureRoomKind.Leatherworker:
					return new[] { W("StitchingBench", 1, 1, Mode.Wall), W("HideRack", 1, area > 20f ? 2 : 1, Mode.Wall), W("TradeShelf", 1, area > 30f ? 2 : 1, Mode.Wall, (int)StructureTrade.Leatherworker),
						W("Chest", 0, 1, Mode.Wall), W("Barrel", 0, 1, Mode.Wall) };
				case StructureRoomKind.Fletcher:
					return new[] { W("FletchingBench", 1, 1, Mode.Wall), W("BowRack", 1, 2, Mode.Wall), W("TradeShelf", 1, area > 30f ? 2 : 1, Mode.Wall, (int)StructureTrade.Fletcher),
						W("Crate", 1, 1, Mode.Wall), W("Chest", 0, 1, Mode.Wall) };
				case StructureRoomKind.Cobbler:
					return new[] { W("CobblerBench", 1, 1, Mode.Wall, 0), W("CobblerBench", 1, 1, Mode.Wall, 1), W("TradeShelf", 1, area > 30f ? 2 : 1, Mode.Wall, (int)StructureTrade.Cobbler),
						W("Chest", 0, 1, Mode.Wall), W("Crate", 0, 1, Mode.Wall) };
				case StructureRoomKind.Jeweller:
					return new[] { W("JewellerBench", 1, 1, Mode.Wall, 0), W("JewellerBench", 1, 1, Mode.Wall, 1), W("Chest", 1, 1, Mode.Wall, 0),
						W("TradeShelf", 1, area > 30f ? 2 : 1, Mode.Wall, (int)StructureTrade.Jeweller), W("BarCounter", 0, 1, Mode.Wall, 0) };
				case StructureRoomKind.Chandler:
					return new[] { W("CandleRack", 1, 2, Mode.Wall), W("TradeShelf", 1, area > 30f ? 3 : 2, Mode.Wall, (int)StructureTrade.Chandler), W("Barrel", 1, 2, Mode.Wall),
						W("Crate", 0, 1, Mode.Wall) };
				case StructureRoomKind.Cartographer:
					// The map table in the middle to work round, a stool at it.
					return new[] { W("MapTable", 1, 1, Mode.Centre, -1, 1), W("ChartPress", 1, 1, Mode.Wall), W("TradeShelf", 1, area > 30f ? 2 : 1, Mode.Wall, (int)StructureTrade.Cartographer),
						W("Bookcase", 0, 1, Mode.Wall), W("Chest", 0, 1, Mode.Wall) };
				case StructureRoomKind.Scribe:
					return new[] { W("Desk", 1, area > 20f ? 2 : 1, Mode.Wall), W("Bookcase", 1, 2, Mode.Wall), W("TradeShelf", 1, area > 30f ? 2 : 1, Mode.Wall, (int)StructureTrade.Scribe),
						W("Lectern", 0, 1, Mode.Centre, 0), W("Chest", 0, 1, Mode.Wall) };
				// The professions' own houses (2026-10-19, StructurePieces.Landmarks.cs).
				case StructureRoomKind.CountingHouse:
					// The counting table facing the door, the coin in strongboxes round the walls, the ledgers on shelves.
					return new[] { W("CountingTable", 1, 1, Mode.Wall), W("Strongbox", 1, area > 40f ? 3 : 2, Mode.Wall), W("Shelf", 1, 2, Mode.Wall),
						W("Chest", 0, 1, Mode.Wall), W("Stool", 0, 1, Mode.Wall, 1) };
				case StructureRoomKind.Observatory:
					// The star charts in the middle, the books and the desk round the wall, an orrery when there is room.
					return new[] { W("StarChartTable", 1, 1, Mode.Centre), W("Bookcase", 1, area > 25f ? 2 : 1, Mode.Wall), W("Desk", 0, 1, Mode.Wall),
						W("ArcaneFocus", area > 25f ? 1 : 0, 1, Mode.Centre), W("Chest", 1, 1, Mode.Wall) };
				case StructureRoomKind.Guildhall:
					// The council's long tables with forms down their sides, the charters in chests, the speaker's lectern, a fire.
					return new[] { W("Table", 1, area > 60f ? 2 : 1, Mode.Centre, 0, 4), W("Pew", 0, area > 50f ? 2 : 1, Mode.Wall, 1), W("Lectern", 0, 1, Mode.Wall, 0),
						W("Chest", 1, 2, Mode.Wall), W("Hearth", 0, 1, Mode.Wall), W("Shelf", 0, 1, Mode.Wall) };
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
			// Where a player comes into the room: just inside each of its doors, else (upstairs) the stair's landing.
			var entries = new List<Vector2>();
			foreach (StructureDoor door in doors)
			{
				var hinge = new Vector2(door.Hinge.x, door.Hinge.z);
				var across = new Vector2(door.Across.x, door.Across.z);
				var inward = new Vector2(door.Inward.x, door.Inward.z);
				float above = door.Hinge.y - floor;
				if (hinge.x < roomMin.x - 1f || hinge.x > roomMax.x + 1f || hinge.y < roomMin.y - 1f || hinge.y > roomMax.y + 1f || above > LandingDoor || above < -0.6f)
				{
					continue; // another room's door
				}
				entries.Add(hinge + across * (door.Width * 0.5f) + inward * 0.5f);
				keepOut.Add(Bounds(hinge, hinge + across * door.Width, hinge + inward * door.Width, hinge + across * door.Width + inward * door.Width, 0.1f));
				Vector2 mid = hinge + across * (door.Width * 0.5f);
				// The way in: 1.4 m, or a third of a small room's depth (a hut's bed may come close to its door).
				float approach = Mathf.Min(Approach, 0.3f * Mathf.Min(room.Size.x, room.Size.y));
				keepOut.Add(Bounds(mid - across * (door.Width * 0.5f + 0.25f), mid + across * (door.Width * 0.5f + 0.25f),
					mid - across * (door.Width * 0.5f + 0.25f) + inward * approach, mid + across * (door.Width * 0.5f + 0.25f) + inward * approach, 0f));
			}

			// What the building keeps clear: a stair and its landing, the well it climbs through.
			var clears = new List<Shape>();
			if (room.Clear != null)
			{
				foreach (Vector4 c in room.Clear)
				{
					var min = new Vector2(c.x, c.y);
					var max = new Vector2(c.z, c.w);
					clears.Add(Shape.Box((min + max) * 0.5f, max - min));
				}
			}
			keepOut.AddRange(clears);

			// What a player cannot walk through: every piece that collides (a stool does not), as laid, without its clearance.
			var solid = new List<Laid>();
			var walk = new WalkGrid(room, centre, roomMin, roomMax, radius);

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

			// Fits, and (if it is solid) leaves every part of the floor reachable from the way in (2026-10-15: "House/building
			// interiors aren't always fully walkable" — pieces kept 8 cm apart, and a table's walkway only from what came after
			// it, closed off a room's back behind a bed and a table).
			bool Clear(string piece, in Shape s, Vector2 at, float yaw) =>
				Fits(s) && (!Collides(piece) || walk.Connected(solid, Laid.Of(at, footprint(piece), yaw), entries, clears));

			void Put(string piece, int variant, Vector2 at, float yaw, in Shape shape, float clearance)
			{
				placed.Add(new InteriorItem { Piece = piece, Variant = variant, Position = new Vector3(at.x, floor, at.y), Yaw = yaw });
				if (Collides(piece))
				{
					solid.Add(Laid.Of(at, footprint(piece), yaw));
				}
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
			// A throne room's own layout (2026-10-19): the throne on its dais, the court's benches along the walls facing the aisle.
			if (room.Kind == StructureRoomKind.ThroneRoom && !room.Round)
			{
				FurnishThroneRoom(centre, roomMin, roomMax, floor, footprint, placed, taken, keepOut, rng);
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
						if (Clear("StallPartition", s, at, 0f))
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
						if (Clear(piece, s, at, yaw))
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
					// Its back to the wall: its +z points out along dir.
					float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
					if (Clear(piece, s, at, yaw))
					{
						Put(piece, variant, at, yaw, s, 0f);
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
						if (Clear(want.Piece, s2, at, across ? 90f : 0f))
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
					if (!Clear(want.Piece, s, at, yaw))
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
				// In the chancel's corner beside the altar, facing the nave: not in the walk before the altar, and no strip of
				// floor shut in behind it.
				var at = new Vector3(roomMax.x - lectern.x * 0.5f - 0.05f, floor, roomMax.y - lectern.y * 0.5f - 0.05f);
				placed.Add(new InteriorItem { Piece = "Lectern", Variant = 0, Position = at, Yaw = 180f });
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
			// The front pew a walk's width (1.3 m) short of the altar, so the chancel either side of it is reachable
			// (2026-10-15: it stood 0.9 m off, the lectern in that gap, and both front corners were shut in).
			float front = roomMin.y + Approach + 0.3f, back = altarZ - altar.y - 1.3f - pew.y * 0.5f;
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

		/// <summary>
		/// A throne room (2026-10-19, StructurePieces.Palace.cs): the throne at the back wall's middle ON the dais (the room's back
		/// <see cref="StructurePieces.ThroneDaisDepth"/>, <see cref="StructurePieces.ThroneDaisRise"/> over its floor: an item's
		/// height is its own, so these stand on the dais' top), a brazier either side of it and the guard's armour by the side walls
		/// up there; a brazier either side of the runner at the dais' foot; and down both side walls, facing the aisle, the court's
		/// benches (pews) in runs with an armour stand after each pair and a chest last before the dais. The building keeps the
		/// dais, its flight and the runner clear (StructureDraft.RoomClear), so only the dais' own pieces are laid there; the
		/// nave's pieces keep out of every clear area and the doors' swings, and nothing stands in the way from the door to the
		/// throne. The same room is furnished the same at every cut: only the variants are drawn.
		/// </summary>
		private static void FurnishThroneRoom(Vector2 centre, Vector2 roomMin, Vector2 roomMax, float floor, Func<string, Vector2> footprint,
			List<InteriorItem> placed, List<Shape> taken, List<Shape> keepOut, DeterministicRNG rng)
		{
			float dais = floor + StructurePieces.ThroneDaisRise;
			float daisFront = roomMax.y - StructurePieces.ThroneDaisDepth, stepFoot = daisFront - StructurePieces.ThroneDaisSteps;
			int Draw() => (int)(rng.NextFloat() * 2f) % 2;
			bool Free(in Shape s, bool onDais)
			{
				if (s.Min.x < roomMin.x - 1e-3f || s.Min.y < roomMin.y - 1e-3f || s.Max.x > roomMax.x + 1e-3f || s.Max.y > roomMax.y + 1e-3f)
				{
					return false;
				}
				// On the dais: inside it. Below: clear of the clear areas (the dais among them) and the doors.
				if (onDais ? s.Min.y < daisFront + 0.05f : Blocked(s))
				{
					return false;
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
			bool Blocked(in Shape s)
			{
				foreach (Shape k in keepOut)
				{
					if (s.Overlaps(k, 0f))
					{
						return true;
					}
				}
				return false;
			}
			// A piece laid at a place and a turn if it fits there, its footprint turned with it.
			bool Lay(string piece, int variant, Vector2 at, float yaw, bool onDais)
			{
				Vector2 size = footprint(piece);
				if (size == Vector2.zero)
				{
					return false;
				}
				Vector2 turned = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f ? new Vector2(size.y, size.x) : size;
				Shape s = Shape.Box(at, turned);
				if (!Free(s, onDais))
				{
					return false;
				}
				placed.Add(new InteriorItem { Piece = piece, Variant = variant, Position = new Vector3(at.x, onDais ? dais : floor, at.y), Yaw = yaw });
				taken.Add(s);
				return true;
			}

			// The throne, its back to the cloth of estate on the back wall, facing down the hall.
			Vector2 throne = footprint("Throne");
			if (throne != Vector2.zero)
			{
				Lay("Throne", Draw(), new Vector2(centre.x, roomMax.y - 0.3f - throne.y * 0.5f), 0f, true);
			}
			// Braziers either side of it, and the guard's armour by the side walls, their backs to the back wall.
			int brazier = Draw();
			for (int s = -1; s <= 1; s += 2)
			{
				Lay("Brazier", brazier, new Vector2(centre.x + s * 2.8f, roomMax.y - 1.4f), 0f, true);
				Vector2 armour = footprint("ArmourStand");
				Lay("ArmourStand", Draw(), new Vector2(centre.x + s * ((roomMax.x - roomMin.x) * 0.5f - 1.2f), roomMax.y - armour.y * 0.5f - 0.08f), 0f, true);
			}
			// A brazier either side of the runner at the foot of the dais' flight.
			Vector2 bowl = footprint("Brazier");
			int foot = Draw();
			for (int s = -1; s <= 1; s += 2)
			{
				Lay("Brazier", foot, new Vector2(centre.x + s * 2.6f, stepFoot - 0.4f - bowl.y * 0.5f), 0f, false);
			}

			// Down both side walls, facing the aisle: pews in pairs, an armour stand after each pair, a chest last before the dais.
			int pew = Draw();
			for (int s = -1; s <= 1; s += 2)
			{
				float yaw = s < 0 ? -90f : 90f, z = roomMin.y + Approach + 0.6f, end = stepFoot - 0.5f;
				int n = 0;
				while (z < end)
				{
					string piece = n % 3 == 2 ? "ArmourStand" : "Pew";
					Vector2 size = footprint(piece);
					if (size == Vector2.zero)
					{
						n++;
						if (n > 64)
						{
							break;
						}
						continue;
					}
					// Turned a quarter: its depth across the wall, its length along it.
					float across = size.y, along = size.x;
					if (z + along > end)
					{
						break;
					}
					float x = s < 0 ? roomMin.x + across * 0.5f + 0.05f : roomMax.x - across * 0.5f - 0.05f;
					if (Lay(piece, piece == "Pew" ? pew : Draw(), new Vector2(x, z + along * 0.5f), yaw, false))
					{
						z += along + 0.35f;
						n++;
					}
					else
					{
						z += 0.25f;
					}
				}
				Vector2 chest = footprint("Chest");
				if (chest != Vector2.zero)
				{
					for (float cz = end - chest.x * 0.5f; cz > roomMin.y + Approach; cz -= 0.25f)
					{
						float x = s < 0 ? roomMin.x + chest.y * 0.5f + 0.05f : roomMax.x - chest.y * 0.5f - 0.05f;
						if (Lay("Chest", Draw(), new Vector2(x, cz), yaw, false))
						{
							break;
						}
					}
				}
			}
		}
	}
}
#endif
