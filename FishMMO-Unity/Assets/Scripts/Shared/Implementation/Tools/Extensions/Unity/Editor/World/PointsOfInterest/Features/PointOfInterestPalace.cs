#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A capital's palace (2026-10-19: "massive palace style castles with interiors (kings chair?) for capitals"): the compound the
	/// capital's centre slot lays in place of a keep. An inner ward walled with towers, its gatehouse at the front; a court behind the
	/// gate with the formal garden in its middle; the throne hall at the back of the court, its door to the garden; a residential wing
	/// down each side, their doors to the garden too. The throne room, the wings' chambers and the gatehouse's and towers' guard rooms
	/// are each piece's own (StructurePieces.Palace.cs, StructurePieces.Towers.cs), furnished by PointOfInterestInteriors and their
	/// doors hung by PointOfInterestDoors like any building PropsFeature lays.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One citadel.</b> The capital templates name the palace in their centre slot (<see cref="PointOfInterestTemplateSpecs.Palace"/>,
	/// the throne hall's id), where the keep was. PropsFeature gives that slot's item the whole compound's footprint
	/// (<see cref="Reserve"/>), so the layout keeps the compound's ground as the city's citadel (the first centrepiece ten metres or
	/// more across: laid against the wall in the widest stretch between the gates' streets, the streets going round it, its front to
	/// the middle of the city), and lays the compound inside that rectangle (<see cref="Lay"/>) where it would have laid the one piece.
	/// </para>
	/// <para>
	/// <b>The plan</b> (<see cref="Plan"/>, pure) is in the compound's own frame: x to its right, z ahead, its front (the gate) down −z
	/// as every piece's front is, its origin the middle of its footprint. The garden stands in the court with its cross walks on the
	/// doors: its z walk runs from the gate to the hall's steps, its x walk from wing door to wing door. The court's walks between
	/// garden and buildings (<see cref="CourtWalk"/>), the forecourt between the gate and the garden (<see cref="Forecourt"/>), the lanes
	/// between the buildings' backs and the curtain (<see cref="Lane"/>) are kept as written. The curtain's centre line passes through
	/// every tower's and the gatehouse's middle (the fortification contract, StructurePieces.Towers.cs): its runs start and end at each
	/// station's join (its "curtain" mark less PointOfInterestLayouts.MarkEmbed), each run cut into whole modules stretched to fit it.
	/// Each corner is a corner tower (the kit's CornerTower: its walk doors on its +x and +z faces, the field its −x/−z quadrant),
	/// turned so its +x and +z run along the two runs into the ward, so the walk turns every corner through it; a wall tower's walk
	/// doors face each other across it, so at a square corner one opened on the drop beside the other run. The wall towers (one half
	/// way down each side beside the hall, two along the back behind it, where each has room and its door an approach) look straight
	/// out across their runs and take the walk through their walk doors.
	/// </para>
	/// <para>
	/// <b>Smaller where it must be.</b> The plan comes in <see cref="Tiers"/>: the walled compound; the palace unwalled (hall, wings,
	/// garden); the hall over its garden; the hall alone. A site takes the fullest whose reach, at its race's stature, is no more than
	/// <see cref="ReachShare"/> of its radius (<see cref="TierFor"/>). Measured on the capitals' layouts (2026-10-19, PalaceTests): a
	/// capital of the kit's stature takes the walled compound from about 175 m (of the planner's 160..220), and the unwalled palace
	/// below, where the walled one cost the city a third of its houses' frontage; a big race's capital (stature to
	/// RaceStature.MaxScale) steps down further, a giant's to the throne hall alone.
	/// </para>
	/// <para>
	/// <b>Its own draws.</b> The pieces' variants (the hall's, the wings', the garden's, the gatehouse's and the side towers') are hashes
	/// of the site's seed and this file's salt (<see cref="PalaceSalt"/>), never the site's sequence, and the compound's footprint is
	/// the same whatever they draw: the rest of the town draws and lays as it did.
	/// </para>
	/// </remarks>
	public static class PointOfInterestPalace
	{
		private const int PalaceSalt = 0x50414C41;

		/// <summary>The pieces of the compound, by id (StructurePieces.Palace.cs; the curtain's in StructurePieces.Buildings.cs, its towers' in Towers.cs).</summary>
		public const string HallId = "ThroneHall", WingId = "PalaceWing", GardenId = "PalaceGarden", GateId = "Gatehouse", TowerId = "WallTower", CurtainId = "WallSegment",
			CornerId = "CornerTower";

		/// <summary>The clear walk between the garden and the hall's steps or a wing's front, metres (at the kit's stature).</summary>
		public const float CourtWalk = 4f;
		/// <summary>The forecourt between the gatehouse's inner face and the garden, metres.</summary>
		public const float Forecourt = 6f;
		/// <summary>The lane between a building's back (or end) and the curtain's inner face, metres.</summary>
		public const float Lane = 2.5f;
		/// <summary>The ground kept clear before a tower's ground door, metres: a side tower stands only where it has this beside the hall.</summary>
		public const float TowerApproach = 2f;
		/// <summary>
		/// The most of a site's radius a compound's reach (half its footprint's diagonal) may take (<see cref="TierFor"/>): the walled
		/// compound (57 m) from a 173 m capital. At 0.38 a 160 m capital laid it and kept the frontage of 51..58% of its houses against
		/// 71..80% round a keep; unwalled, 60..62%.
		/// </summary>
		public const float ReachShare = 0.33f;

		/// <summary>The plans from the fullest to the least (<see cref="TierFor"/>).</summary>
		public enum Tier
		{
			/// <summary>The walled compound: curtain, corner, side and back towers, gatehouse, hall, two wings, garden.</summary>
			Walled,
			/// <summary>The palace unwalled: hall, two wings, garden.</summary>
			Open,
			/// <summary>The hall over its garden.</summary>
			Hall,
			/// <summary>The throne hall alone.</summary>
			Lone,
		}

		/// <summary>The number of tiers.</summary>
		public const int Tiers = 4;

		public enum PartKind
		{
			Hall,
			Wing,
			Garden,
			Gatehouse,
			Tower,
			Curtain,
			/// <summary>A corner tower (StructurePieces.Towers.cs): its origin the joint of the two runs' centre lines, the field its −x/−z quadrant.</summary>
			CornerTower,
		}

		/// <summary>One piece of the compound in its frame: its middle, its yaw (its front, −z, turned by it), its own footprint unturned.</summary>
		public struct Part
		{
			public PartKind Kind;
			public Vector2 At;
			public float Yaw;
			/// <summary>Its footprint in its own frame (x across its front, y deep); a curtain module's x is its stretched length.</summary>
			public Vector2 Size;
			/// <summary>The kit variant laid.</summary>
			public int Variant;
			/// <summary>A curtain module's stretch along its x (its run's length over whole modules); 1 for everything else.</summary>
			public float Stretch;
			/// <summary>A corner tower's ground door and the way out of it, in its own frame (the kit's, <see cref="Sizes.CornerDoor"/>).</summary>
			public Vector2 LocalDoor, LocalOut;

			/// <summary>
			/// Where its ground door is, compound frame: the middle of its front, a wall tower's back (which faces the ward), a corner
			/// tower's own (on the bailey diagonal of the round one, on the +z face of the square one).
			/// </summary>
			public Vector2 Door => At + PointOfInterestLayouts.Rotate(Kind == PartKind.CornerTower ? LocalDoor
				: new Vector2(0f, Kind == PartKind.Tower ? Size.y * 0.5f : -Size.y * 0.5f), Yaw);

			/// <summary>The way out of its ground door (unit), compound frame.</summary>
			public Vector2 Out => PointOfInterestLayouts.Rotate(Kind == PartKind.CornerTower ? LocalOut : Kind == PartKind.Tower ? Vector2.up : Vector2.down, Yaw);
		}

		/// <summary>The kit's measurements a plan is built from (<see cref="Measured"/>; tests give their own).</summary>
		public struct Sizes
		{
			public Vector2 Hall, Wing, Garden, Gate, Tower;
			/// <summary>The curtain's module length and its depth (its centre line to its faces, both ways).</summary>
			public float Module, CurtainDepth;
			/// <summary>Where the curtain meets the gatehouse and the towers, metres each side of their middles along their x, by variant.</summary>
			public float[] GateJoin, TowerJoin;
			/// <summary>The corner tower's body (twice its farthest walk-door mark: its steps into the ward are not counted, they are kept clear).</summary>
			public Vector2 Corner;
			/// <summary>Where the curtain meets a corner tower, metres out from its origin along its +x and its +z, by variant.</summary>
			public float[] CornerJoin;
			/// <summary>A corner tower's ground door by variant, its own frame: (x, z) of the door's middle and (x, z) of the way out.</summary>
			public Vector4[] CornerDoor;
		}

		/// <summary>A compound's plan: its parts, its footprint (centred on the origin) and its tier.</summary>
		public sealed class Layout
		{
			public Tier Tier;
			public readonly List<Part> Parts = new List<Part>();
			/// <summary>The footprint's size, x across its front, y deep, centred on the origin.</summary>
			public Vector2 Footprint;
			/// <summary>The garden's middle (the court's), compound frame.</summary>
			public Vector2 Court;

			/// <summary>Half the footprint's diagonal: how far from its middle the compound reaches.</summary>
			public float Reach => Footprint.magnitude * 0.5f;
		}

		private static Sizes? measured;

		/// <summary>The kit's measurements (the pieces' footprints over every variant, their curtain marks), measured once.</summary>
		public static Sizes Measured
		{
			get
			{
				if (measured.HasValue)
				{
					return measured.Value;
				}
				StructurePiece curtain = StructurePieces.Find(CurtainId);
				var sizes = new Sizes
				{
					Hall = FootprintOf(HallId),
					Wing = FootprintOf(WingId),
					Garden = FootprintOf(GardenId),
					Gate = FootprintOf(GateId),
					Tower = FootprintOf(TowerId),
					Module = curtain != null && curtain.ModuleLength > 0f ? curtain.ModuleLength : 6f,
					CurtainDepth = 2f * StructurePieces.CurtainHalfThickness,
					GateJoin = JoinsOf(GateId),
					TowerJoin = JoinsOf(TowerId),
					CornerJoin = JoinsOf(CornerId),
					CornerDoor = GroundDoorsOf(CornerId),
				};
				float cornerReach = 0f;
				foreach (float join in sizes.CornerJoin)
				{
					cornerReach = Mathf.Max(cornerReach, join + PointOfInterestLayouts.MarkEmbed);
				}
				sizes.Corner = Vector2.one * (2f * cornerReach);
				measured = sizes;
				return sizes;
			}
		}

		private static Vector2 FootprintOf(string id)
		{
			StructurePiece piece = StructurePieces.Find(id);
			return piece != null ? piece.Footprint : Vector2.one;
		}

		/// <summary>
		/// Each variant's join: the farthest "curtain" mark from the origin along x or z less MarkEmbed (a wall tower's and a gatehouse's
		/// are on ±x, a corner tower's on +x and +z), else its half width less StationEmbed (PropsFeature.StationJoin's rule).
		/// </summary>
		private static float[] JoinsOf(string id)
		{
			StructurePiece piece = StructurePieces.Find(id);
			if (piece == null)
			{
				return new[] { 0.5f };
			}
			var joins = new float[Mathf.Max(1, piece.Variants)];
			for (int v = 0; v < joins.Length; v++)
			{
				float join = 0f;
				foreach (StructureMark mark in StructurePieces.MarksOf(piece, v))
				{
					if (mark.Name == "curtain")
					{
						join = Mathf.Max(join, Mathf.Max(Mathf.Abs(mark.At.x), Mathf.Abs(mark.At.z)));
					}
				}
				joins[v] = join > 0f ? Mathf.Max(0.5f, join - PointOfInterestLayouts.MarkEmbed) : Mathf.Max(0.5f, piece.Footprint.x * 0.5f - PointOfInterestLayouts.StationEmbed);
			}
			return joins;
		}

		/// <summary>Each variant's ground door (the lowest), its own frame: the door's middle and the way out of it (x, z, out x, out z).</summary>
		private static Vector4[] GroundDoorsOf(string id)
		{
			StructurePiece piece = StructurePieces.Find(id);
			var doors = new Vector4[piece != null ? Mathf.Max(1, piece.Variants) : 1];
			for (int v = 0; piece != null && v < doors.Length; v++)
			{
				bool found = false;
				StructureDoor lowest = default;
				foreach (StructureDoor door in StructurePieces.DoorsOf(piece, v))
				{
					if (!found || door.Hinge.y < lowest.Hinge.y)
					{
						lowest = door;
						found = true;
					}
				}
				if (found)
				{
					Vector3 middle = lowest.Hinge + lowest.Across * (lowest.Width * 0.5f);
					doors[v] = new Vector4(middle.x, middle.z, -lowest.Inward.x, -lowest.Inward.z);
				}
			}
			return doors;
		}

		/// <summary>A variant of a piece with <paramref name="variants"/> of them, drawn from the site's seed and a key of the part's (pure).</summary>
		private static int Draw(int seed, int key, int variants) => variants <= 1 ? 0 : (int)(PointOfInterestPlanner.Hash(seed, PalaceSalt, key, 0) % (uint)variants);

		/// <summary>
		/// The compound's plan for a tier (pure): see the class remarks. <paramref name="seed"/> (the site's) draws only the variants; the
		/// parts' places and the footprint are the tier's alone.
		/// </summary>
		public static Layout Plan(in Sizes s, Tier tier, int seed)
		{
			var layout = new Layout { Tier = tier };
			List<Part> parts = layout.Parts;
			int hallVariant = Draw(seed, 1, 2), wingVariant = Draw(seed, 2, 2), gardenVariant = Draw(seed, 3, 2);
			// The court frame first (the garden's middle at the origin); moved to the footprint's middle at the end.
			if (tier == Tier.Lone)
			{
				parts.Add(new Part { Kind = PartKind.Hall, At = Vector2.zero, Yaw = 0f, Size = s.Hall, Variant = hallVariant, Stretch = 1f });
				return Finish(layout, Vector2.zero);
			}
			parts.Add(new Part { Kind = PartKind.Garden, At = Vector2.zero, Yaw = 0f, Size = s.Garden, Variant = gardenVariant, Stretch = 1f });
			// The hall behind the garden, its steps a walk from it, its door down the garden's z walk.
			float hallZ = s.Garden.y * 0.5f + CourtWalk + s.Hall.y * 0.5f;
			parts.Add(new Part { Kind = PartKind.Hall, At = new Vector2(0f, hallZ), Yaw = 0f, Size = s.Hall, Variant = hallVariant, Stretch = 1f });
			if (tier == Tier.Hall)
			{
				return Finish(layout, Vector2.zero);
			}
			// A wing down each side, its long front a walk from the garden, its door (the middle of that front) on the garden's x walk:
			// turned a quarter so its front looks across the court (the right wing's front looks −x at yaw 90, the left's +x at 270).
			float wingX = s.Garden.x * 0.5f + CourtWalk + s.Wing.y * 0.5f;
			parts.Add(new Part { Kind = PartKind.Wing, At = new Vector2(-wingX, 0f), Yaw = 270f, Size = s.Wing, Variant = wingVariant, Stretch = 1f });
			parts.Add(new Part { Kind = PartKind.Wing, At = new Vector2(wingX, 0f), Yaw = 90f, Size = s.Wing, Variant = wingVariant, Stretch = 1f });
			if (tier == Tier.Open)
			{
				return Finish(layout, Vector2.zero);
			}

			// The ward's walls: the curtain's centre line round it, a lane in from every building, the gatehouse across the front
			// a forecourt before the garden.
			float half = s.CurtainDepth * 0.5f;
			float front = -(s.Garden.y * 0.5f + Forecourt + s.Gate.y * 0.5f);
			float side = wingX + s.Wing.y * 0.5f + Lane + half;
			float back = hallZ + s.Hall.y * 0.5f + Lane + half;
			int gateVariant = Draw(seed, 4, s.GateJoin != null ? s.GateJoin.Length : 1);
			float gateJoin = JoinOf(s.GateJoin, gateVariant, s.Gate.x);
			parts.Add(new Part { Kind = PartKind.Gatehouse, At = new Vector2(0f, front), Yaw = 0f, Size = s.Gate, Variant = gateVariant, Stretch = 1f });
			// The towers, one design round the ward (a wall tower's variant and a corner tower's alike: round, or square under a
			// pyramid roof), each met square by the curtain at its walk doors.
			int towerVariant = Draw(seed, 5, s.TowerJoin != null ? s.TowerJoin.Length : 1);
			float towerJoin = JoinOf(s.TowerJoin, towerVariant, s.Tower.x);
			float cornerJoin = JoinOf(s.CornerJoin, towerVariant, s.Corner.x);
			float towerHalf = 0.5f * Mathf.Max(s.Tower.x, s.Tower.y);
			// A corner tower at each corner (2026-10-19: the kit's CornerTower, its walk doors on +x and +z, so the walk turns the
			// corner): its origin on the joint of the two runs' centre lines, turned so its +x and +z run along them into the ward and
			// its field quadrant (−x, −z) looks out, its ground door and steps on the ward's side.
			Vector4 door = s.CornerDoor != null && s.CornerDoor.Length > 0 ? s.CornerDoor[Mathf.Clamp(towerVariant, 0, s.CornerDoor.Length - 1)] : new Vector4(2.8f, 2.8f, 0.7071f, 0.7071f);
			var corners = new (Vector2 at, float yaw)[] { (new Vector2(-side, front), 0f), (new Vector2(side, front), 270f), (new Vector2(side, back), 180f), (new Vector2(-side, back), 90f) };
			foreach ((Vector2 at, float yaw) in corners)
			{
				parts.Add(new Part
				{
					Kind = PartKind.CornerTower, At = at, Yaw = yaw, Size = s.Corner, Variant = towerVariant, Stretch = 1f,
					LocalDoor = new Vector2(door.x, door.y), LocalOut = new Vector2(door.z, door.w).normalized,
				});
			}
			// Half way down each side between the wing's end and the back, where that leaves it room, its door an approach and the
			// curtain on to the corner tower at least three quarters of a module.
			float wingEnd = s.Wing.x * 0.5f;
			float sideTowerZ = 0.5f * (wingEnd + back);
			bool sideTowers = sideTowerZ - towerHalf - TowerApproach >= wingEnd && back - sideTowerZ - towerJoin - cornerJoin >= 0.75f * s.Module;
			// Two along the back, half way between the hall's flank and the side's inner face, where the tower and its door's approach
			// stand clear of the hall.
			float hallHalf = s.Hall.x * 0.5f;
			float backTowerX = 0.5f * (hallHalf + side - half);
			bool backTowers = backTowerX - towerHalf >= hallHalf + TowerApproach && side - backTowerX - towerJoin - cornerJoin >= 0.75f * s.Module;
			if (sideTowers)
			{
				parts.Add(new Part { Kind = PartKind.Tower, At = new Vector2(-side, sideTowerZ), Yaw = 90f, Size = s.Tower, Variant = towerVariant, Stretch = 1f });
				parts.Add(new Part { Kind = PartKind.Tower, At = new Vector2(side, sideTowerZ), Yaw = 270f, Size = s.Tower, Variant = towerVariant, Stretch = 1f });
			}
			if (backTowers)
			{
				parts.Add(new Part { Kind = PartKind.Tower, At = new Vector2(-backTowerX, back), Yaw = 180f, Size = s.Tower, Variant = towerVariant, Stretch = 1f });
				parts.Add(new Part { Kind = PartKind.Tower, At = new Vector2(backTowerX, back), Yaw = 180f, Size = s.Tower, Variant = towerVariant, Stretch = 1f });
			}
			// The runs, walked clockwise round the ward from the front left (Run), from station to station: the corner towers', the
			// gatehouse's, the side and back towers' walk doors.
			Run(parts, s, new Vector2(-side, front), new Vector2(0f, front), cornerJoin, gateJoin);
			Run(parts, s, new Vector2(0f, front), new Vector2(side, front), gateJoin, cornerJoin);
			RunVia(parts, s, new Vector2(side, front), new Vector2(side, back), cornerJoin, sideTowers ? new Vector2(side, sideTowerZ) : (Vector2?)null, towerJoin);
			if (backTowers)
			{
				Run(parts, s, new Vector2(side, back), new Vector2(backTowerX, back), cornerJoin, towerJoin);
				Run(parts, s, new Vector2(backTowerX, back), new Vector2(-backTowerX, back), towerJoin, towerJoin);
				Run(parts, s, new Vector2(-backTowerX, back), new Vector2(-side, back), towerJoin, cornerJoin);
			}
			else
			{
				Run(parts, s, new Vector2(side, back), new Vector2(-side, back), cornerJoin, cornerJoin);
			}
			RunVia(parts, s, new Vector2(-side, back), new Vector2(-side, front), cornerJoin, sideTowers ? new Vector2(-side, sideTowerZ) : (Vector2?)null, towerJoin);
			return Finish(layout, Vector2.zero);
		}

		/// <summary>A side's run from corner to corner (the corner towers' join <paramref name="corner"/> at each), through a tower at <paramref name="tower"/> where it has one.</summary>
		private static void RunVia(List<Part> parts, in Sizes s, Vector2 a, Vector2 b, float corner, Vector2? tower, float towerJoin)
		{
			if (tower.HasValue)
			{
				Run(parts, s, a, tower.Value, corner, towerJoin);
				Run(parts, s, tower.Value, b, towerJoin, corner);
			}
			else
			{
				Run(parts, s, a, b, corner, corner);
			}
		}

		private static float JoinOf(float[] joins, int variant, float width)
			=> joins != null && joins.Length > 0 ? joins[Mathf.Clamp(variant, 0, joins.Length - 1)] : Mathf.Max(0.5f, width * 0.5f - PointOfInterestLayouts.StationEmbed);

		/// <summary>
		/// The curtain from one station to the next: from <paramref name="joinA"/> past the first's middle to <paramref name="joinB"/>
		/// short of the second's, in whole modules stretched to fit. The runs are walked clockwise round the ward seen from above (x right,
		/// z up), so the ward is on a run's left and the field on its right, where its front (−z) looks.
		/// </summary>
		private static void Run(List<Part> parts, in Sizes s, Vector2 a, Vector2 b, float joinA, float joinB)
		{
			Vector2 along = b - a;
			float length = along.magnitude;
			if (length < 1e-3f)
			{
				return;
			}
			Vector2 dir = along / length;
			float from = joinA, to = length - joinB, span = to - from;
			if (span < 0.5f)
			{
				return;
			}
			int count = Mathf.Max(1, Mathf.RoundToInt(span / Mathf.Max(0.5f, s.Module)));
			float each = span / count;
			// Its x along the run; its front (−z) out of the ward: the run's right (along turned a quarter clockwise seen from above).
			var outward = new Vector2(dir.y, -dir.x);
			float yaw = PointOfInterestLayouts.FacingYaw(PointOfInterestLayouts.DirectionOf(outward));
			for (int k = 0; k < count; k++)
			{
				Vector2 at = a + dir * (from + each * (k + 0.5f));
				parts.Add(new Part { Kind = PartKind.Curtain, At = at, Yaw = yaw, Size = new Vector2(each, s.CurtainDepth), Variant = 0, Stretch = each / Mathf.Max(0.5f, s.Module) });
			}
		}

		/// <summary>Moves the parts so the footprint's middle is the origin, and measures it (every part's turned rectangle).</summary>
		private static Layout Finish(Layout layout, Vector2 court)
		{
			var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
			var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
			var four = new Vector2[4];
			foreach (Part p in layout.Parts)
			{
				PointOfInterestLayouts.Corners(p.At, p.Yaw, p.Size, four);
				foreach (Vector2 c in four)
				{
					min = Vector2.Min(min, c);
					max = Vector2.Max(max, c);
				}
			}
			Vector2 middle = 0.5f * (min + max);
			for (int i = 0; i < layout.Parts.Count; i++)
			{
				Part p = layout.Parts[i];
				p.At -= middle;
				layout.Parts[i] = p;
			}
			layout.Court = court - middle;
			layout.Footprint = max - min;
			return layout;
		}

		/// <summary>The fullest tier whose reach at <paramref name="scale"/> (the race's stature) is within <see cref="ReachShare"/> of <paramref name="radius"/>; the hall alone at least.</summary>
		public static Tier TierFor(in Sizes s, float radius, float scale)
		{
			for (int t = 0; t < Tiers - 1; t++)
			{
				if (Plan(s, (Tier)t, 0).Reach * scale <= ReachShare * radius)
				{
					return (Tier)t;
				}
			}
			return Tier.Lone;
		}

		/// <summary>The ground a site of <paramref name="radius"/> keeps for its palace at <paramref name="scale"/>: the footprint of the tier it lays, scaled.</summary>
		public static Vector2 Reserve(in Sizes s, float radius, float scale) => Plan(s, TierFor(s, radius, scale), 0).Footprint * scale;

		/// <summary><see cref="Reserve(in Sizes, float, float)"/> with the kit's measurements.</summary>
		public static Vector2 Reserve(float radius, float scale) => Reserve(Measured, radius, scale);

		// ── Laying ──────────────────────────────────────────────

		/// <summary>The kit query that lays a part's variant (StructureKitPieceSource's "#" pin).</summary>
		private static string QueryFor(in Part part) => "=" + IdOf(part.Kind) + "#" + part.Variant;

		/// <summary>The kit piece a part is.</summary>
		public static string IdOf(PartKind kind) => kind switch
		{
			PartKind.Hall => HallId,
			PartKind.Wing => WingId,
			PartKind.Garden => GardenId,
			PartKind.Gatehouse => GateId,
			PartKind.Tower => TowerId,
			PartKind.CornerTower => CornerId,
			_ => CurtainId,
		};

		/// <summary>How each part is told to the site's later passes (PropsFeature's grounds: yards, clutter, the clutter's keep-clear).</summary>
		private static PointOfInterestSlotRole RoleOf(PartKind kind)
		{
			switch (kind)
			{
				case PartKind.Hall:
				case PartKind.Wing:
					// A building: its apron and floor worn, a path out of its door, a bench or a lantern by it.
					return PointOfInterestSlotRole.Street;
				case PartKind.Gatehouse:
					return PointOfInterestSlotRole.Gate;
				case PartKind.Tower:
				case PartKind.CornerTower:
					return PointOfInterestSlotRole.WallTower;
				default:
					// The curtain and the garden: no apron worn round them and nothing stood against them (the garden's floor is laid here).
					return PointOfInterestSlotRole.Perimeter;
			}
		}

		/// <summary>
		/// Lays the compound in the rectangle the layout kept for it: its middle at <paramref name="offset"/> (the site's frame), its front
		/// turned by <paramref name="yaw"/> (the site's frame), at <paramref name="scale"/> (the race's stature), the tier a site of
		/// <paramref name="radius"/> takes (the one <see cref="Reserve(float, float)"/> kept room for). Each part is a kit piece added
		/// with context.AddProp (so its doors and rooms are the site's, hung and furnished after the template's features); the ward's
		/// walls and towers all seated at one height so the walk runs level into the towers' doors, each building at its own; the
		/// garden's ground and the court's walks worn. Returns the pieces laid; <paramref name="missing"/> those with no art yet.
		/// </summary>
		public static int Lay(PointOfInterestSiteContext context, Vector2 offset, float yaw, float scale, float radius, StructureFinish finish,
			BiomeStructurePalette palette, bool allowLocal, List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds,
			out int missing)
		{
			missing = 0;
			PointOfInterestRecord record = context.Record;
			Sizes sizes = Measured;
			Tier tier = TierFor(sizes, radius, scale);
			Layout plan = Plan(sizes, tier, record.SiteSeed);
			Vector2 Site(Vector2 local) => offset + PointOfInterestLayouts.Rotate(local * scale, yaw);

			// The ward's one height: the lowest any of its walls or towers would seat at, so none stands on air.
			float ring = float.PositiveInfinity;
			foreach (Part part in plan.Parts)
			{
				if (IsWall(part.Kind))
				{
					ring = Mathf.Min(ring, PropsFeature.SeatHeight(context, Site(part.At), yaw + part.Yaw, part.Size * scale, DepthOf(part) * scale));
				}
			}
			int laid = 0;
			foreach (Part part in plan.Parts)
			{
				var request = new PointOfInterestPieceRequest(QueryFor(part), PointOfInterestTemplateSpecs.Stone, record.SiteSeed, 0f, finish, allowLocal) { Palette = palette };
				GameObject prefab = PointOfInterestPieceResolver.Resolve(request);
				if (prefab == null)
				{
					missing++;
					continue;
				}
				Vector2 at = Site(part.At);
				float partYaw = yaw + part.Yaw;
				bool walled = IsWall(part.Kind);
				Vector3 position = context.OnGround(at);
				position.y = walled ? ring : PropsFeature.SeatHeight(context, at, partYaw, part.Size * scale, DepthOf(part) * scale);
				Quaternion turn = Quaternion.Euler(0f, record.Yaw + partYaw, 0f);
				context.AddProp(prefab, position, turn, part.Kind == PartKind.Curtain ? new Vector3(part.Stretch * scale, scale, scale) : Vector3.one * scale);
				grounds?.Add((position, turn, part.Size * scale, RoleOf(part.Kind), QueryFor(part)));
				laid++;
			}
			WearGround(context, plan, Site, yaw, scale);
			context.Notes?.Add($"{record.Name}: its palace laid {tier} ({plan.Footprint.x * scale:0} x {plan.Footprint.y * scale:0} m, {laid} pieces{(missing > 0 ? $", {missing} with no art yet" : string.Empty)}).");
			return laid;
		}

		/// <summary>The ward's walls and their towers and gate: seated at one height.</summary>
		private static bool IsWall(PartKind kind) => kind == PartKind.Curtain || kind == PartKind.Tower || kind == PartKind.CornerTower || kind == PartKind.Gatehouse;

		private static float DepthOf(in Part part)
		{
			StructurePiece piece = StructurePieces.Find(IdOf(part.Kind));
			return piece != null ? piece.Depth : 0.5f;
		}

		/// <summary>
		/// The ground the compound wears (as PointOfInterestFarmsteads wears a farmstead's): the garden's plan a floor (no grass up
		/// through its walks and beds), the court's gravel walks on the garden's cross from the gate to the hall's steps and out to each
		/// wing's door, the forecourt's flags inside the gate and a few paces out of it. The halls' and wings' own aprons and floors
		/// are PropsFeature's (its yards, from the grounds), as every building's are.
		/// </summary>
		private static void WearGround(PointOfInterestSiteContext context, Layout plan, Func<Vector2, Vector2> site, float yaw, float scale)
		{
			if (context.Points?.Paths == null)
			{
				return;
			}
			Part? garden = null, hall = null, gate = null;
			var wings = new List<Part>();
			foreach (Part part in plan.Parts)
			{
				switch (part.Kind)
				{
					case PartKind.Garden:
						garden = part;
						break;
					case PartKind.Hall:
						hall = part;
						break;
					case PartKind.Gatehouse:
						gate = part;
						break;
					case PartKind.Wing:
						wings.Add(part);
						break;
				}
			}
			const ScenePathPointFlags squared = ScenePathPointFlags.Squared;
			if (garden.HasValue)
			{
				Part g = garden.Value;
				Wear(context, site(g.At + new Vector2(0f, -g.Size.y * 0.5f)), site(g.At + new Vector2(0f, g.Size.y * 0.5f)), g.Size.x * 0.5f * scale, 1f,
					ScenePathSurface.Stone, squared | ScenePathPointFlags.Floor);
				float walk = 1.5f * scale;
				if (hall.HasValue)
				{
					Wear(context, site(g.At + new Vector2(0f, g.Size.y * 0.5f)), site(hall.Value.Door), walk, 0.95f, ScenePathSurface.Gravel, squared);
				}
				foreach (Part wing in wings)
				{
					Vector2 edge = g.At + new Vector2(Mathf.Sign(wing.At.x - g.At.x) * g.Size.x * 0.5f, 0f);
					Wear(context, site(edge), site(wing.Door), walk, 0.95f, ScenePathSurface.Gravel, squared);
				}
				if (gate.HasValue)
				{
					Part gh = gate.Value;
					Vector2 inner = gh.At + new Vector2(0f, gh.Size.y * 0.5f);
					Wear(context, site(inner), site(g.At + new Vector2(0f, -g.Size.y * 0.5f)), 2.5f * scale, 1f, ScenePathSurface.Stone, squared);
				}
			}
			if (gate.HasValue)
			{
				// Out of the gate toward the city's middle (the compound's front looks at it), as far as its square's streets come round.
				Part gh = gate.Value;
				Vector2 outer = gh.At + new Vector2(0f, -gh.Size.y * 0.5f);
				Wear(context, site(outer), site(outer + new Vector2(0f, -6f)), 2f * scale, 0.9f, ScenePathSurface.Stone, ScenePathPointFlags.None);
			}
		}

		/// <summary>Worn ground from <paramref name="a"/> to <paramref name="b"/> (the site's frame), <paramref name="half"/> metres either side.</summary>
		private static void Wear(PointOfInterestSiteContext context, Vector2 a, Vector2 b, float half, float wear, ScenePathSurface surface, ScenePathPointFlags shape)
		{
			if ((b - a).sqrMagnitude < 0.01f)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			byte flags = (byte)(ScenePathPointFlags.InSite | shape);
			context.Points.Paths.Add(new ScenePath
			{
				Id = id + 1,
				Class = ScenePathClass.Yard,
				FromId = -1,
				ToId = context.Record.Id,
				Points = new[] { context.OnGround(a), context.OnGround(b) },
				HalfWidth = new[] { half, half },
				Wear = new[] { wear, wear },
				Surface = new[] { (byte)surface, (byte)surface },
				Flags = new[] { flags, flags },
			});
		}
	}
}
#endif
