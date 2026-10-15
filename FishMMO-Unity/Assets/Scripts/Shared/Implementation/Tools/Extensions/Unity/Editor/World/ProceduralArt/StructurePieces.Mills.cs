#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A part of a piece that turns (a tower mill's sails, a mill's water wheel): another piece, set at a hub in the owner's
	/// frame and spun about an axis there. Built as its own piece so the owner stays a static instanced prop; the cut lays
	/// the turning part as a client-only object (PointOfInterestRotors) that <see cref="MillRotor"/> spins.
	/// </summary>
	public struct StructureRotor
	{
		/// <summary>The hub in the owner's frame, and the axis it turns about there (unit).</summary>
		public Vector3 Hub, Axis;
		/// <summary>The turning piece and its variant.</summary>
		public string Piece;
		public int Variant;
		/// <summary>The hub in the turning piece's own frame (its origin is the foot of its sweep, as every piece stands on y = 0).</summary>
		public Vector3 PieceHub;
		/// <summary>Turns a minute; 0 stands still (a mill with its sails furled). For sails in the wind, the most they are let turn.</summary>
		public float Rpm;
		/// <summary>Sails: turned by the live wind (<see cref="MillRotor"/>), not at a steady <see cref="Rpm"/>.</summary>
		public bool Wind;
		/// <summary>
		/// What turns about the vertical through <see cref="Pivot"/> to face the wind, carrying the rotor with it (a tower
		/// mill's cap), and its variant; null when the rotor's axis is fixed. Its piece's origin sits at the pivot.
		/// </summary>
		public string Head;
		public int HeadVariant;
		/// <summary>Where the head turns, in the owner's frame; <see cref="Hub"/> is given with the head at rest, facing −z as the owner does.</summary>
		public Vector3 Pivot;
	}

	/// <summary>
	/// Mills and the rural work round a settlement (2026-10-14: "Windmills, watermills, lumbermills, and more ... all
	/// medieval fantasy"): a tower mill, its cap and sails, a watermill and its wheel, a water-powered sawmill, a saw pit, log
	/// and plank stacks, a granary on staddles, a dovecote, bee skeps, a charcoal clamp, a lime kiln and a tanning yard.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Medieval forms only</b> (from the record): the stone tower mill (raised from the later 13th century; the post mill
	/// is older, but its buck on a mound or an open trestle read as a box on a platform, 2026-10-15), its cap turned to the
	/// wind by a tailpole, four common sails of canvas over a lattice spanning 15 m; the
	/// watermill a stone ground floor under a timber loft with its wheel on the water side (undershot on a river, overshot
	/// fed by a timber launder); the sawmill the water-driven sash saw Villard de Honnecourt drew about 1230, but most planks
	/// were sawn over a pit by a top and a pit sawyer. Sizes from survivors and excavations.
	/// </para>
	/// <para>
	/// <b>Turning parts</b> are their own pieces (<see cref="RotorsOf"/>), each with its origin at the foot of its sweep so it
	/// stands like every other piece; the owner names where its hub is.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>The sails' reach from the hub, metres (a 15 m span).</summary>
		private const float SailReach = 7.6f;
		/// <summary>The post mill's sails' hub above the turning piece's foot.</summary>
		private const float SailHub = SailReach + 0.35f;
		/// <summary>An undershot wheel's radius and an overshot's, metres (survivors 3.7–4.6 m across).</summary>
		private const float UndershotRadius = 2.4f, OvershotRadius = 2.1f;

		private static void AddMills(List<StructurePiece> list)
		{
			// Its sails, as the cap comes round, sweep a ring about 8 m out at a cottage's roof (and its tailpole 7 m out at a
			// man's height over the ground): nothing else is laid in it.
			Piece(list, "TowerMill", StructureStyle.Stone, StructureSize.Building,
				"A medieval tower mill: a battered round tower 6.6 m across at its foot on a fieldstone footing, a door into its meal floor, small windows up it and the curb its cap turns on; v0 rubble stone 9.2 m to the curb, v1 lime-washed and lower.",
				TowerMill, "building", "mill", "windmill", "farm").Sweep = new Vector2(16.5f, 16.5f);
			StructurePiece cap = Piece(list, "MillCap", StructureStyle.Timber, StructureSize.Medium,
				"A tower mill's cap, turned on its curb to face the wind with the sails: v0 a boarded ogee cap, v1 a thatched cone; the windshaft out of its front and the tailpole down its back. Its origin is on the curb.",
				MillCap, "mill");
			cap.Collides = false;
			StructurePiece sails = Piece(list, "MillSails", StructureStyle.Timber, StructureSize.Medium,
				"A windmill's four common sails, 15 m across: lattices on whips through the poll end; v0 the canvas spread, v1 furled along the whips. Turned by the mill (its origin is the foot of the sweep).",
				MillSails, "mill");
			sails.Collides = false;
			Piece(list, "Watermill", StructureStyle.Stone, StructureSize.Building,
				"A watermill, 8×7 m: a stone ground floor under a timber loft on a footing down into the river, the wheel on its water side between stone race walls, sacks and a spare millstone by the door; v0 an undershot wheel, v1 an overshot wheel fed by a timber launder on trestles.",
				Watermill, "building", "mill", "watermill").WaterFront = true;
			StructurePiece wheel = Piece(list, "WaterWheel", StructureStyle.Timber, StructureSize.Medium,
				"A mill's water wheel, its axle along z: v0 undershot, 4.8 m across with sixteen paddles; v1 overshot, 4.2 m with buckets between shrouds. Turned by its mill (its origin is the foot of the wheel).",
				WaterWheel, "mill");
			wheel.Collides = false;
			Piece(list, "Sawmill", StructureStyle.Timber, StructureSize.Building,
				"A water-powered sawmill, 12×6 m open-sided under its roof: the sash saw's frame between guide posts, a log on the carriage running through it on long rails, its wheel on the water side; v1 thatched.",
				Sawmill, "building", "mill", "sawmill", "lumber").WaterFront = true;
			Piece(list, "SawPit", StructureStyle.Timber, StructureSize.Medium,
				"A saw pit 4.6×1.2 m, its timber-lined mouth dark, a log on the cross-bearers over it with the long pit saw standing in it, sawdust and the planks it gave; v1 under a lean-to roof.",
				SawPit, "lumber", "clutter");
			Piece(list, "LogPile", StructureStyle.Timber, StructureSize.Small,
				"Felled trunks five metres long stacked between stakes: v0 four, three and two; v1 three and two with one rolled off.",
				LogPile, "lumber", "clutter");
			Piece(list, "PlankStack", StructureStyle.Timber, StructureSize.Small,
				"Sawn planks stacked to season on bearers, sticks between the layers: v0 five layers, v1 three and a few leaning on it.",
				PlankStack, "lumber", "clutter");
			Piece(list, "Granary", StructureStyle.Timber, StructureSize.Medium,
				"A plank granary 4×5 m raised on nine mushroom staddle stones against rats, a thatched roof and loose steps that stop short of its door; v1 a shingled roof.",
				Granary, "farm", "granary");
			Piece(list, "Dovecote", StructureStyle.Stone, StructureSize.Building,
				"A round stone dovecote 5.5 m across, a string course, flight holes with alighting ledges under a conical roof and its louvre; v1 thatched.",
				Dovecote, "farm", "dovecote");
			Piece(list, "Apiary", StructureStyle.Timber, StructureSize.Small,
				"Straw bee skeps: v0 four on a plank bench under a lean-to roof, v1 three in bee boles in a short stone wall.",
				Apiary, "farm", "apiary", "clutter");
			Piece(list, "CharcoalClamp", StructureStyle.Earth, StructureSize.Medium,
				"A charcoal burner's clamp, a smoking turf dome 5 m across with a ladder up it and cordwood stacked by; v1 with the collier's conical hut.",
				CharcoalClamp, "lumber", "kiln", "fire");
			Piece(list, "LimeKiln", StructureStyle.Stone, StructureSize.Medium,
				"A lime kiln, a battered stone stack with its draw-arch glowing and smoke from its bowl, limestone heaped by and burnt lime; v1 round.",
				LimeKiln, "kiln", "fire");
			Piece(list, "TanningYard", StructureStyle.Timber, StructureSize.Medium,
				"A tanning yard: timber-lined pits of dark liquor in rows, hides stretched to dry on frames, a stack of bark; v1 fewer pits and a lime pit.",
				TanningYard, "tannery", "farm");
		}

		// ── Turning parts ─────────────────────────────────────────────

		/// <summary>The parts of a piece's variant that turn, in its own frame; none for most pieces.</summary>
		public static IReadOnlyList<StructureRotor> RotorsOf(StructurePiece piece, int variant)
		{
			if (piece == null || piece.RuinOf != null)
			{
				return Array.Empty<StructureRotor>();
			}
			switch (piece.Id)
			{
				case "TowerMill":
				{
					// The windshaft out of the cap's front, raised 8° toward the sails; the cap turns on the tower's curb.
					float tilt = 8f * Mathf.Deg2Rad;
					float top = TowerMillHeight(variant);
					return new[]
					{
						new StructureRotor
						{
							Hub = new Vector3(0f, top, 0f) + TowerMillCapHub(variant), Axis = new Vector3(0f, Mathf.Sin(tilt), -Mathf.Cos(tilt)),
							Piece = "MillSails", Variant = 0, PieceHub = new Vector3(0f, SailHub, 0f), Rpm = AirDrift.SailMostRpm,
							Wind = true, Head = "MillCap", HeadVariant = variant, Pivot = new Vector3(0f, top, 0f),
						},
					};
				}
				case "Watermill":
					return new[] { MillWheel(variant, WatermillWheelX, -WatermillDepth * 0.5f) };
				case "Sawmill":
					return new[] { MillWheel(0, SawmillWheelX, SawmillWheelWall) };
				default:
					return Array.Empty<StructureRotor>();
			}
		}

		/// <summary>A water wheel on a mill's front (−z) wall at <paramref name="x"/>: its axle square to the wall, its foot in the water under the mill's floor.</summary>
		private static StructureRotor MillWheel(int variant, float x, float wallZ)
		{
			bool overshot = variant == 1;
			float r = overshot ? OvershotRadius : UndershotRadius;
			// The mill's floor stands RiverBankFreeboard over the water (PointOfInterestPlanner.PlaceRiverBanks): an undershot
			// wheel's paddles dip 0.3 m, an overshot's buckets clear the tail water by a hand.
			float sink = overshot ? RiverBankFreeboard - 0.1f : RiverBankFreeboard + 0.3f;
			return new StructureRotor
			{
				Hub = new Vector3(x, r - sink, wallZ - WheelGap - 0.6f), Axis = Vector3.forward,
				Piece = "WaterWheel", Variant = overshot ? 1 : 0, PieceHub = new Vector3(0f, r + 0.05f, 0f), Rpm = overshot ? 5f : 7f,
			};
		}

		/// <summary>
		/// How far a river-bank mill's origin (its floor's foot) stands over the river's surface, metres: the planner seats a
		/// watermill or sawmill there, and the wheel is hung to reach the water from it.
		/// </summary>
		public const float RiverBankFreeboard = 0.8f;

		/// <summary>How far below its origin a river-bank mill's water side is founded (its front footing, the race walls), metres: down into the bed.</summary>
		private const float RaceFooting = 2.6f;

		/// <summary>The gap between a mill's wall and its wheel's near face, metres.</summary>
		private const float WheelGap = 0.35f;

		// ── Tower mill ────────────────────────────────────────────────

		/// <summary>The tower's height to the curb the cap turns on, metres: v0 stone, v1 lime-washed and lower.</summary>
		private static float TowerMillHeight(int variant) => variant == 1 ? 8.6f : 9.2f;
		/// <summary>The tower's outer radius at its foot and at its curb.</summary>
		private static float TowerMillFoot(int variant) => variant == 1 ? 3.1f : 3.3f;
		private static float TowerMillCurb(int variant) => variant == 1 ? 2.35f : 2.5f;
		/// <summary>The ground storey's wall, the floor's height (one step up from the ground) and its ceiling.</summary>
		private const float TowerMillWall = 0.75f, TowerMillFloor = 0.2f, TowerMillStorey = 3.4f;

		/// <summary>The sails' hub in the cap's own frame (its origin on the curb, on the tower's axis): out over the front, so the sails clear the tower's batter.</summary>
		private static Vector3 TowerMillCapHub(int variant) => new Vector3(0f, 1.15f, -(TowerMillCurb(variant) + 1.25f));

		/// <summary>
		/// A medieval tower mill (2026-10-15: post mills "stand on weird platforms, unrealistic"): a battered round tower on its
		/// own footing, a door into its meal floor and small windows up it, the curb on top that its cap (<c>MillCap</c>, a
		/// turning part with the sails) turns on to face the wind. Stone-built tower mills were raised from the later 13th
		/// century; v0 is rubble stone, v1 lime-washed and lower.
		/// </summary>
		private static void TowerMill(StructureDraft d)
		{
			bool washed = d.Variant == 1;
			float h = TowerMillHeight(d.Variant), r0 = TowerMillFoot(d.Variant), r1 = TowerMillCurb(d.Variant);
			float inner = r0 - TowerMillWall;
			int seg = d.Seg(24, 12);
			StructureMaterial wall = washed ? StructureMaterial.Plaster : StructureMaterial.Stone;
			// The footing, its top the meal floor: a solid drum a little wider than the tower.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -FoundationDepth), new Vector2(r0 + 0.22f, -FoundationDepth), new Vector2(r0 + 0.22f, TowerMillFloor - 0.04f),
				new Vector2(r0 + 0.12f, TowerMillFloor), new Vector2(0f, TowerMillFloor) }, seg, StructureMaterial.Fieldstone, 1));

			// The ground storey: its wall with the door at −z (1.5π) and a window either side, its ceiling, the room.
			const float doorWidth = 1.2f, doorHeight = 2.3f;
			float sill = TowerMillFloor + 1.15f;
			var holes = new List<(float, float, float, float)>
			{
				(1.5f * Mathf.PI, doorWidth, TowerMillFloor, TowerMillFloor + doorHeight),
				(0.15f * Mathf.PI, WindowWidth, sill, sill + WindowHeight),
				(0.85f * Mathf.PI, WindowWidth, sill, sill + WindowHeight),
			};
			RingWallWithOpenings(d, Vector3.zero, inner, r0, TowerMillFloor, TowerMillStorey, seg, 1.5f * Mathf.PI, holes, wall, 10);
			for (int i = 1; i < holes.Count; i++)
			{
				float a = holes[i].Item1;
				var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Window(d, outward * r0 + Vector3.up * (sill + WindowHeight * 0.5f), outward, TowerMillWall, 300 + i * 20);
			}
			d.Add(StructureShapes.Cylinder(inner + 0.05f, TowerMillStorey - 0.25f, TowerMillStorey, seg, StructureMaterial.Timber, 2));
			HangDoor(d, new Vector3(0f, TowerMillFloor, -Mathf.Sqrt(inner * inner - 0.25f * doorWidth * doorWidth) + 0.05f), Vector3.right, Vector3.forward, doorWidth, doorHeight,
				StructureDoorKind.Planks);
			d.RoundRoom(new Vector3(0f, TowerMillFloor, 0f), inner, TowerMillStorey - TowerMillFloor - 0.25f, StructureRoomKind.Mill);
			// The door's lintel and jambs proud of the wall.
			if (d.Mid)
			{
				float face = -r0 - 0.03f;
				d.Add(StructureShapes.Box(new Vector3(0f, TowerMillFloor + doorHeight + 0.12f, face), new Vector3(doorWidth + 0.5f, 0.24f, 0.12f), StructureMaterial.Fieldstone, 20, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (doorWidth * 0.5f + 0.12f), TowerMillFloor + doorHeight * 0.5f, face), new Vector3(0.24f, doorHeight, 0.1f), StructureMaterial.Fieldstone,
						21 + (s > 0 ? 1 : 0), StructureRole.Detail));
				}
			}

			// The tower above, battered in to the curb, solid (its floors are the miller's, not the player's), a string course
			// where the storey meets it.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r0, TowerMillStorey), new Vector2(r1, h) }, seg, wall, 3));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r0 + 0.1f, TowerMillStorey - 0.12f), new Vector2(r0 + 0.1f, TowerMillStorey + 0.12f) }, seg, StructureMaterial.Fieldstone, 4));
			// The curb: a timber ring on a stone course, the cap's track.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r1 + 0.12f, h - 0.45f), new Vector2(r1 + 0.12f, h - 0.2f), new Vector2(0f, h - 0.2f) }, seg, StructureMaterial.Fieldstone, 5));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(r1 + 0.05f, h - 0.2f), new Vector2(r1 + 0.05f, h), new Vector2(0f, h) }, seg, StructureMaterial.Timber, 6));
			// Small windows up the tower, lighting its stair, never two on one line.
			if (d.Mid)
			{
				float[] at = { 5.4f, 7.3f };
				float[] turn = { 0.05f * Mathf.PI, 1.05f * Mathf.PI };
				for (int i = 0; i < at.Length; i++)
				{
					float r = Mathf.Lerp(r0, r1, (at[i] - TowerMillStorey) / (h - TowerMillStorey));
					var outward = new Vector3(Mathf.Cos(turn[i]), 0f, Mathf.Sin(turn[i]));
					Window(d, outward * r + Vector3.up * at[i], outward, 400 + i * 20);
				}
			}
			if (!d.Mid)
			{
				return;
			}
			// Sacks waiting by the door, and a spare millstone against the tower.
			for (int i = 0; i < 3; i++)
			{
				var at = new Vector3(1.4f + i * 0.5f + (i == 2 ? -0.25f : 0f), 0.35f + (i == 2 ? 0.55f : 0f), -r0 - 0.6f);
				d.Add(StructureShapes.Sphere(at, new Vector3(0.3f, 0.36f, 0.26f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 500 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Cylinder(0.65f, -0.15f, 0.15f, d.Seg(14, 8), StructureMaterial.Stone, 510)
				.Transform(Matrix4x4.TRS(new Vector3(r0 + 0.3f, 0.65f, 1.0f), Quaternion.Euler(0f, 0f, 78f), Vector3.one)));
		}

		/// <summary>
		/// A tower mill's cap, its origin on the tower's curb and axis (the variant matches the tower's): a boarded ogee cap
		/// (v0) or a thatched cone (v1), the windshaft out of its front to the sails' poll end, and the tailpole down its back
		/// the miller hauls round to luff it into the wind. Turned whole with the sails, so it is a turning part, not the tower.
		/// </summary>
		private static void MillCap(StructureDraft d)
		{
			bool thatch = d.Variant == 1;
			float r1 = TowerMillCurb(d.Variant), h = TowerMillHeight(d.Variant);
			float rc = r1 + 0.3f;
			int seg = d.Seg(20, 10);
			if (thatch)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(rc + 0.25f, 0f), new Vector2(rc + 0.25f, 0.15f), new Vector2(0.35f, 3.0f), new Vector2(0f, 3.05f) },
					seg, StructureMaterial.Thatch, 1, true, 0f, false, false, StructureRole.Roof));
			}
			else
			{
				// Boarded, swelling out over the curb and drawn in to its finial: the ogee of the old caps.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(rc, 0f), new Vector2(rc, 0.35f), new Vector2(rc - 0.05f, 0.75f), new Vector2(rc * 0.78f, 1.6f),
					new Vector2(rc * 0.42f, 2.3f), new Vector2(0.18f, 2.75f), new Vector2(0f, 2.8f) }, seg, StructureMaterial.Timber, 1, true, 0f, false, false, StructureRole.Roof));
			}
			float tip = thatch ? 3.05f : 2.8f;
			d.Add(StructureShapes.Sphere(new Vector3(0f, tip + 0.12f, 0f), new Vector3(0.16f, 0.2f, 0.16f), d.Seg(8, 4), d.Fine ? 4 : 2, StructureMaterial.Timber, 2));

			// The windshaft from inside the cap out to the poll end, on its neck bearing at the cap's front.
			Vector3 hub = TowerMillCapHub(d.Variant);
			float tilt = 8f * Mathf.Deg2Rad;
			var axis = new Vector3(0f, Mathf.Sin(tilt), -Mathf.Cos(tilt));
			d.Add(StructureShapes.Rod(hub - axis * 3.2f, hub - axis * 0.3f, 0.22f, d.Seg(10, 6), StructureMaterial.Timber, 3));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, hub.y - 0.15f - 1.05f * Mathf.Sin(tilt), -rc + 0.05f), new Vector3(0.9f, 0.55f, 0.5f), d.Bevel(0.03f),
				StructureMaterial.Timber, 4));

			// The tailpole: from the cap's back, down and out to a man's reach over the ground, braced from the cap's sides.
			var head = new Vector3(0f, 0.35f, rc - 0.1f);
			var foot = new Vector3(0f, -(h - 2.4f), rc + 4.4f);
			d.Add(StructureShapes.Beam(head, foot, 0.22f, 0.26f, StructureMaterial.Timber, 10));
			Vector3 brace = Vector3.Lerp(head, foot, 0.42f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * (rc - 0.4f), 0.25f, rc * 0.55f), brace, 0.12f, 0.12f, StructureMaterial.Timber, 11 + (s > 0 ? 1 : 0)));
			}
			// The chain and its post-hook at the foot, for the miller to haul on.
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(foot, foot + new Vector3(0f, -0.9f, 0.1f), 0.04f, 0.04f, StructureMaterial.Iron, 13, StructureRole.Detail));
				// The cap's weatherboards' ends and the little door on the back the miller climbs out by.
				d.Add(StructureShapes.Box(new Vector3(0f, 0.75f, rc - 0.02f), new Vector3(0.6f, 0.9f, 0.06f), StructureMaterial.Ash, 14, StructureRole.Detail));
			}
		}

		private static void MillSails(StructureDraft d)
		{
			var hub = new Vector3(0f, SailHub, 0f);
			bool spread = d.Variant == 0;
			// The poll end: the stocks' block on the windshaft's end.
			d.Add(StructureShapes.ChamferBox(hub, new Vector3(0.75f, 0.75f, 0.7f), d.Bevel(0.05f), StructureMaterial.Timber, 1));
			for (int i = 0; i < 4; i++)
			{
				// At rest a saltire, as mills were left.
				float a = (45f + 90f * i) * Mathf.Deg2Rad;
				var u = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
				var w = new Vector3(-u.y, u.x, 0f);
				int key = 10 + i * 40;
				// The whip, the stock under it, the hemlath along the sail's trailing edge.
				d.Add(StructureShapes.Beam(hub + u * 0.3f, hub + u * SailReach, 0.2f, 0.16f, StructureMaterial.Timber, key));
				const float from = 2.0f, back = 1.75f, lead = 0.35f;
				Vector3 trail = w * back;
				d.Add(StructureShapes.Beam(hub + u * from + trail + Vector3.forward * 0.02f, hub + u * SailReach + trail + Vector3.forward * 0.02f, 0.09f, 0.08f, StructureMaterial.Timber, key + 1));
				d.Add(StructureShapes.Beam(hub + u * from - w * lead, hub + u * from + trail, 0.08f, 0.07f, StructureMaterial.Timber, key + 2));
				d.Add(StructureShapes.Beam(hub + u * SailReach - w * lead, hub + u * SailReach + trail, 0.08f, 0.07f, StructureMaterial.Timber, key + 3));
				// The sail bars across: every 0.55 m at full detail, half as many at the middle level, none farther.
				int bars = d.Fine ? 11 : d.Mid ? 5 : 0;
				for (int b = 1; b <= bars; b++)
				{
					float r = Mathf.Lerp(from, SailReach, b / (float)(bars + 1));
					d.Add(StructureShapes.Beam(hub + u * r - w * lead, hub + u * r + trail, 0.05f, 0.05f, StructureMaterial.Timber, key + 4 + b, StructureRole.Detail));
				}
				if (spread)
				{
					// The canvas over the lattice, a little behind it.
					Vector3 c = hub + u * (0.5f * (from + SailReach)) + w * (0.5f * (back - 0.1f)) + Vector3.forward * 0.07f;
					d.Add(StructureShapes.Block(c, new Vector3(SailReach - from - 0.1f, back - 0.1f, 0.03f), Quaternion.LookRotation(Vector3.forward, w),
						StructureMaterial.Canvas, key + 30, StructureRole.Detail));
				}
				else
				{
					// Furled: the canvas rolled along the whip.
					d.Add(StructureShapes.Rod(hub + u * from + w * 0.18f + Vector3.forward * 0.1f, hub + u * (SailReach - 0.2f) + w * 0.18f + Vector3.forward * 0.1f, 0.13f, d.Seg(6, 4),
						StructureMaterial.Canvas, key + 30, StructureRole.Detail));
				}
			}
		}

		// ── Watermill ─────────────────────────────────────────────────

		private const float WatermillWidth = 8f, WatermillDepth = 7f, WatermillWheelX = 1.5f;

		private static void Watermill(StructureDraft d)
		{
			bool overshot = d.Variant == 1;
			var h = new House
			{
				Width = WatermillWidth, Depth = WatermillDepth, WallHeight = 3f, Upper = 2.5f, Rise = 3.4f, Overhang = 0.5f, Wall = 0.45f, Plinth = 0.3f,
				WallMaterial = StructureMaterial.Stone, UpperWallMaterial = StructureMaterial.Plaster, UpperFrame = true,
				RoofMaterial = overshot ? StructureMaterial.Thatch : StructureMaterial.Timber, PlinthMaterial = StructureMaterial.Fieldstone,
				Frame = false, RidgeAlongX = true, Chimney = false, Windows = 1, DoorWidth = 1.3f, DoorHeight = 2.2f, DoorX = -2.4f,
				RoomKind = StructureRoomKind.Hall, Key = 1,
			};
			BuildHouse(d, h);
			// The water side's footing, down into the bed under the front wall, so the river's edge shows masonry, not a gap.
			WaterFooting(d, WatermillWidth + 0.3f, -WatermillDepth * 0.5f + 0.2f, 0.5f, h.Plinth, 390);
			StructureRotor rotor = MillWheel(d.Variant, WatermillWheelX, -WatermillDepth * 0.5f);
			MillRace(d, rotor, overshot, 400);
			if (overshot)
			{
				Launder(d, rotor, OvershotRadius, WatermillWidth * 0.5f + 4.5f, 450);
			}
			if (!d.Mid)
			{
				return;
			}
			// Sacks against the side wall by the door (not on its step) and a spare millstone leant on the wall.
			float front = -WatermillDepth * 0.5f;
			for (int i = 0; i < 3; i++)
			{
				var at = new Vector3(-WatermillWidth * 0.5f - 0.5f, 0.35f + (i == 2 ? 0.55f : 0f), front + 0.7f + i * 0.5f + (i == 2 ? -0.25f : 0f));
				d.Add(StructureShapes.Sphere(at, new Vector3(0.3f, 0.36f, 0.26f), d.Seg(8, 5), 4, StructureMaterial.Canvas, 500 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Cylinder(0.65f, -0.15f, 0.15f, d.Seg(14, 8), StructureMaterial.Stone, 510)
				.Transform(Matrix4x4.TRS(new Vector3(-WatermillWidth * 0.5f - 0.25f, 0.65f, 1.2f), Quaternion.Euler(0f, 0f, 78f), Vector3.one)));
		}

		/// <summary>A river-bank mill's footing along its water side: a stone wall from <paramref name="top"/> down to <see cref="RaceFooting"/>, centred at z.</summary>
		private static void WaterFooting(StructureDraft d, float width, float z, float thick, float top, int key)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.5f * (top - RaceFooting), z), new Vector3(width, top + RaceFooting, thick), d.Bevel(0.04f), StructureMaterial.Fieldstone, key));
		}

		/// <summary>
		/// The wheel's race: stone walls either side of the wheel out from the mill's wall into the water, the axle from the
		/// wall to a bearing pier on the outer wall, and (undershot) a sluice frame upstream.
		/// </summary>
		private static void MillRace(StructureDraft d, StructureRotor rotor, bool overshot, int key)
		{
			float r = overshot ? OvershotRadius : UndershotRadius;
			float wallZ = rotor.Hub.z + WheelGap + 0.6f;
			float outer = rotor.Hub.z - 0.6f - 0.35f;
			for (int s = -1; s <= 1; s += 2)
			{
				float x = rotor.Hub.x + s * (r + 0.45f);
				// From a little over the floor down into the bed.
				d.Add(StructureShapes.ChamferBox(new Vector3(x, 0.5f * (0.35f - RaceFooting), 0.5f * (wallZ + outer) - 0.2f), new Vector3(0.6f, 0.35f + RaceFooting, wallZ - outer + 0.6f), d.Bevel(0.04f),
					StructureMaterial.Fieldstone, key + (s > 0 ? 1 : 0)));
			}
			// The bearing pier and the axle.
			d.Add(StructureShapes.ChamferBox(new Vector3(rotor.Hub.x, 0.5f * (rotor.Hub.y - 0.3f - RaceFooting), outer - 0.1f), new Vector3(0.7f, rotor.Hub.y - 0.3f + RaceFooting, 0.5f), d.Bevel(0.04f),
				StructureMaterial.Fieldstone, key + 2));
			d.Add(StructureShapes.Rod(new Vector3(rotor.Hub.x, rotor.Hub.y, wallZ + 0.1f), new Vector3(rotor.Hub.x, rotor.Hub.y, outer - 0.3f), 0.17f, d.Seg(10, 6), StructureMaterial.Timber, key + 3));
			if (!overshot)
			{
				// The sluice: two posts and a lifted board across the race on the upstream side.
				float x = rotor.Hub.x - r - 0.9f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, -RaceFooting, s > 0 ? wallZ - 0.1f : outer + 0.1f), new Vector3(x, 1.4f, s > 0 ? wallZ - 0.1f : outer + 0.1f), 0.18f, 0.18f,
						StructureMaterial.Timber, key + 4 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(x, 0.9f, 0.5f * (wallZ + outer)), new Vector3(0.08f, 0.6f, wallZ - outer - 0.25f), StructureMaterial.Timber, key + 6)).Grain = Vector3.forward;
				d.Add(StructureShapes.Beam(new Vector3(x, 1.4f, wallZ - 0.1f), new Vector3(x, 1.4f, outer + 0.1f), 0.14f, 0.14f, StructureMaterial.Timber, key + 7, StructureRole.Detail));
			}
		}

		/// <summary>An overshot wheel's launder: a timber trough on trestles bringing the water from <paramref name="toX"/> to just over the wheel's top.</summary>
		private static void Launder(StructureDraft d, StructureRotor rotor, float radius, float toX, int key)
		{
			float y = rotor.Hub.y + radius + 0.35f;
			float z = rotor.Hub.z;
			float from = rotor.Hub.x - 0.3f;
			float length = toX - from;
			d.Add(StructureShapes.Box(new Vector3(from + 0.5f * length, y - 0.25f, z), new Vector3(length, 0.08f, 0.8f), StructureMaterial.Timber, key)).Grain = Vector3.right;
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(from + 0.5f * length, y, z + s * 0.38f), new Vector3(length, 0.5f, 0.06f), StructureMaterial.Timber, key + 1 + (s > 0 ? 1 : 0))).Grain = Vector3.right;
			}
			int trestles = Mathf.Max(1, Mathf.FloorToInt(length / 3f));
			for (int i = 1; i <= trestles; i++)
			{
				float x = from + length * i / (trestles + 0.5f);
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(x, -0.3f, z + s * 0.75f), new Vector3(x, y - 0.3f, z + s * 0.32f), 0.14f, 0.14f, StructureMaterial.Timber, key + 10 + i * 3 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Beam(new Vector3(x, y - 0.32f, z - 0.55f), new Vector3(x, y - 0.32f, z + 0.55f), 0.12f, 0.12f, StructureMaterial.Timber, key + 12 + i * 3, StructureRole.Detail));
			}
		}

		private static void WaterWheel(StructureDraft d)
		{
			bool overshot = d.Variant == 1;
			float r = overshot ? OvershotRadius : UndershotRadius;
			float width = overshot ? 1.2f : 1.0f;
			var hub = new Vector3(0f, r + 0.05f, 0f);
			int segments = d.Seg(16, 8);
			// The axle's boss, the two rims of felloes and their spokes.
			d.Add(StructureShapes.Rod(hub + Vector3.back * (0.5f * width + 0.2f), hub + Vector3.forward * (0.5f * width + 0.2f), 0.26f, d.Seg(10, 6), StructureMaterial.Timber, 1));
			for (int s = -1; s <= 1; s += 2)
			{
				float z = s * 0.5f * width;
				for (int i = 0; i < segments; i++)
				{
					float a0 = i * 2f * Mathf.PI / segments, a1 = (i + 1) * 2f * Mathf.PI / segments;
					Vector3 p0 = hub + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * (r - 0.12f) + Vector3.forward * z;
					Vector3 p1 = hub + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * (r - 0.12f) + Vector3.forward * z;
					// A little long, so the felloes overlap at their joints.
					Vector3 m = 0.5f * (p0 + p1), half = 0.54f * (p1 - p0);
					d.Add(StructureShapes.Beam(m - half, m + half, 0.12f, 0.24f, StructureMaterial.Timber, 10 + (s > 0 ? 100 : 0) + i));
				}
				int spokes = d.Mid ? 8 : 4;
				for (int i = 0; i < spokes; i++)
				{
					float a = (i + 0.5f) * 2f * Mathf.PI / spokes + (s > 0 ? 0f : 0f);
					var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
					d.Add(StructureShapes.Beam(hub + dir * 0.2f + Vector3.forward * z, hub + dir * (r - 0.2f) + Vector3.forward * z, 0.12f, 0.1f, StructureMaterial.Timber, 60 + (s > 0 ? 100 : 0) + i));
				}
			}
			// Paddles (undershot) or buckets (overshot) right across between the rims.
			int blades = d.Fine ? (overshot ? 20 : 16) : d.Mid ? (overshot ? 14 : 12) : 8;
			for (int i = 0; i < blades; i++)
			{
				float a = i * 360f / blades;
				var dir = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad), 0f);
				if (overshot)
				{
					// A bucket: a board set at 35° to the radius, open toward the turn.
					Vector3 at = hub + dir * (r - 0.18f);
					d.Add(StructureShapes.Block(at, new Vector3(0.05f, 0.42f, width), Quaternion.Euler(0f, 0f, a - 90f + 35f), StructureMaterial.Timber, 300 + i, StructureRole.Detail));
				}
				else
				{
					Vector3 at = hub + dir * (r - 0.2f);
					d.Add(StructureShapes.Block(at, new Vector3(0.05f, 0.6f, width + 0.1f), Quaternion.Euler(0f, 0f, a - 90f), StructureMaterial.Timber, 300 + i, StructureRole.Detail));
				}
			}
			if (overshot && d.Mid)
			{
				// The sole: boards closing the buckets' inner side.
				for (int i = 0; i < segments; i++)
				{
					float a0 = i * 2f * Mathf.PI / segments, a1 = (i + 1) * 2f * Mathf.PI / segments;
					Vector3 p0 = hub + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * (r - 0.42f);
					Vector3 p1 = hub + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * (r - 0.42f);
					Vector3 m = 0.5f * (p0 + p1);
					d.Add(StructureShapes.Block(m, new Vector3((p1 - p0).magnitude * 1.04f, 0.04f, width - 0.1f), Quaternion.Euler(0f, 0f, (a0 + a1) * 0.5f * Mathf.Rad2Deg + 90f),
						StructureMaterial.Timber, 400 + i, StructureRole.Detail));
				}
			}
		}

		// ── Sawmill ───────────────────────────────────────────────────

		private const float SawmillWidth = 12f, SawmillDepth = 6f;
		/// <summary>The sawmill's wheel: across the shed, and the race's landward end a little out from its front posts so the wheel turns wholly over the water.</summary>
		private const float SawmillWheelX = 2.2f, SawmillWheelWall = -SawmillDepth * 0.5f - 0.6f;

		private static void Sawmill(StructureDraft d)
		{
			const float eave = 3.2f;
			float halfW = SawmillWidth * 0.5f, halfD = SawmillDepth * 0.5f;
			// Posts on stone pads, open all round; a plank wall half up the back.
			for (int i = 0; i < 4; i++)
			{
				float x = Mathf.Lerp(-halfW + 0.2f, halfW - 0.2f, i / 3f);
				for (int s = -1; s <= 1; s += 2)
				{
					var foot = new Vector3(x, 0f, s * (halfD - 0.2f));
					d.Add(StructureShapes.ChamferBox(foot + Vector3.up * 0.1f, new Vector3(0.55f, 0.6f, 0.55f), d.Bevel(0.03f), StructureMaterial.Fieldstone, 10 + i * 2 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Beam(foot + Vector3.up * 0.4f, foot + Vector3.up * eave, 0.24f, 0.24f, StructureMaterial.Timber, 20 + i * 2 + (s > 0 ? 1 : 0)));
				}
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(-halfW, eave, s * (halfD - 0.2f)), new Vector3(halfW, eave, s * (halfD - 0.2f)), 0.24f, 0.26f, StructureMaterial.Timber, 30 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, 1.05f, halfD - 0.2f), new Vector3(SawmillWidth - 0.4f, 1.3f, 0.1f), StructureMaterial.Timber, 32)).Grain = Vector3.right;
			GableRoof(d, SawmillDepth, SawmillWidth, eave, 2f, 0.55f, 0.12f, d.Variant == 1 ? StructureMaterial.Thatch : StructureMaterial.Timber, 40,
				Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one));
			// The floor the carriage runs on: a plank deck the length of the shed.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.35f, 0f), new Vector3(SawmillWidth - 0.6f, 0.12f, 2.6f), StructureMaterial.Timber, 50)).Grain = Vector3.right;
			// The rails, longer than the shed, and the carriage with its log half through the saw.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(-halfW - 3f, 0.5f, s * 0.6f), new Vector3(halfW + 3f, 0.5f, s * 0.6f), 0.16f, 0.16f, StructureMaterial.Timber, 51 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.ChamferBox(new Vector3(-halfW - 2.6f, 0.2f, s * 0.6f), new Vector3(0.4f, 0.6f, 0.4f), d.Bevel(0.02f), StructureMaterial.Fieldstone, 53 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.ChamferBox(new Vector3(halfW + 2.6f, 0.2f, s * 0.6f), new Vector3(0.4f, 0.6f, 0.4f), d.Bevel(0.02f), StructureMaterial.Fieldstone, 55 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Box(new Vector3(-1.5f, 0.68f, 0f), new Vector3(5.5f, 0.18f, 1.5f), StructureMaterial.Timber, 57)).Grain = Vector3.right;
			Log(d, new Vector3(-5.2f, 1.12f, 0f), new Vector3(2.4f, 1.12f, 0f), 0.36f, 58);
			// The sash saw: two guide posts to the tie beam, the frame between them and its blade.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(0.8f, 0.4f, s * 0.95f), new Vector3(0.8f, eave, s * 0.95f), 0.26f, 0.22f, StructureMaterial.Timber, 60 + (s > 0 ? 1 : 0)));
			}
			d.Add(StructureShapes.Beam(new Vector3(0.8f, eave - 0.1f, -1f), new Vector3(0.8f, eave - 0.1f, 1f), 0.24f, 0.24f, StructureMaterial.Timber, 62));
			d.Add(StructureShapes.Box(new Vector3(0.8f, 2.25f, 0f), new Vector3(0.12f, 0.12f, 1.6f), StructureMaterial.Timber, 63));
			d.Add(StructureShapes.Box(new Vector3(0.8f, 0.85f, 0f), new Vector3(0.12f, 0.12f, 1.6f), StructureMaterial.Timber, 64));
			d.Add(StructureShapes.Box(new Vector3(0.8f, 1.55f, 0f), new Vector3(0.02f, 1.3f, 0.16f), StructureMaterial.Iron, 65, StructureRole.Detail));
			// The wheel's race on the water side and the crank's pitman up to the frame.
			StructureRotor rotor = MillWheel(0, SawmillWheelX, SawmillWheelWall);
			MillRace(d, rotor, false, 100);
			// The water side's footing under the front posts' line.
			WaterFooting(d, SawmillWidth, -halfD + 0.2f, 0.45f, 0.1f, 90);
			d.Add(StructureShapes.Beam(new Vector3(rotor.Hub.x, rotor.Hub.y, -halfD + 0.4f), new Vector3(0.9f, 0.9f, -0.4f), 0.14f, 0.14f, StructureMaterial.Timber, 66, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			// Slabwood and sawdust under the saw's end.
			d.Add(StructureShapes.Sphere(new Vector3(2.4f, 0.3f, 1.8f), new Vector3(1.1f, 0.35f, 0.7f), d.Seg(8, 5), 3, StructureMaterial.Earth, 70, StructureRole.Detail));
			for (int i = 0; i < 3; i++)
			{
				d.Add(StructureShapes.Box(new Vector3(4.2f, 0.5f + i * 0.11f, 1.6f + (i % 2) * 0.1f), new Vector3(2.8f, 0.09f, 0.32f), StructureMaterial.Timber, 71 + i, StructureRole.Detail)).Grain = Vector3.right;
			}
		}

		// ── Saw pit, logs, planks ─────────────────────────────────────

		private static void SawPit(StructureDraft d)
		{
			const float length = 4.6f, width = 1.2f;
			// The pit's mouth: its dark, and the timber that lines its edge.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.01f, 0f), new Vector3(length, 0.04f, width), StructureMaterial.Ash, 1));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(-length * 0.5f - 0.2f, 0.08f, s * (width * 0.5f + 0.12f)), new Vector3(length * 0.5f + 0.2f, 0.08f, s * (width * 0.5f + 0.12f)), 0.24f, 0.24f,
					StructureMaterial.Timber, 2 + (s > 0 ? 1 : 0)));
				d.Add(StructureShapes.Beam(new Vector3(s * (length * 0.5f + 0.12f), 0.08f, -width * 0.5f), new Vector3(s * (length * 0.5f + 0.12f), 0.08f, width * 0.5f), 0.22f, 0.22f,
					StructureMaterial.Timber, 4 + (s > 0 ? 1 : 0)));
			}
			// Two cross-bearers and the log along the pit, the pit saw standing through it.
			for (int s = -1; s <= 1; s += 2)
			{
				Log(d, new Vector3(s * 1.4f, 0.32f, -1.1f), new Vector3(s * 1.4f, 0.32f, 1.1f), 0.17f, 6 + (s > 0 ? 1 : 0));
			}
			Log(d, new Vector3(-2.9f, 0.82f, 0f), new Vector3(2.7f, 0.82f, 0.05f), 0.33f, 8);
			d.Add(StructureShapes.Box(new Vector3(0.6f, 0.6f, 0f), new Vector3(0.22f, 2.4f, 0.02f), StructureMaterial.Iron, 9, StructureRole.Detail));
			d.Add(StructureShapes.Beam(new Vector3(0.6f, 1.82f, -0.35f), new Vector3(0.6f, 1.82f, 0.35f), 0.06f, 0.06f, StructureMaterial.Timber, 10, StructureRole.Detail));
			if (d.Mid)
			{
				d.Add(StructureShapes.Sphere(new Vector3(-0.8f, 0.05f, 1.35f), new Vector3(0.8f, 0.25f, 0.45f), d.Seg(8, 5), 3, StructureMaterial.Earth, 11, StructureRole.Detail));
				for (int i = 0; i < 4; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(0.4f, 0.06f + i * 0.07f, -1.55f), new Vector3(3.6f, 0.06f, 0.3f), StructureMaterial.Timber, 12 + i, StructureRole.Detail)).Grain = Vector3.right;
				}
			}
			if (d.Variant == 1)
			{
				// A lean-to roof over it on four posts.
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						float h = sz > 0 ? 2.9f : 2.3f;
						d.Add(StructureShapes.Beam(new Vector3(sx * 2.7f, -0.3f, sz * 1.5f), new Vector3(sx * 2.7f, h, sz * 1.5f), 0.16f, 0.16f, StructureMaterial.Timber, 20 + (sx > 0 ? 2 : 0) + (sz > 0 ? 1 : 0)));
					}
				}
				d.Add(StructureShapes.Block(new Vector3(0f, 2.68f, 0f), new Vector3(6f, 0.1f, 3.6f), Quaternion.Euler(-9.5f, 0f, 0f), StructureMaterial.Timber, 25)).Grain = Vector3.forward;
			}
		}

		private static void LogPile(StructureDraft d)
		{
			int[] rows = d.Variant == 0 ? new[] { 4, 3, 2 } : new[] { 3, 2 };
			const float radius = 0.3f, length = 5f;
			int key = 1;
			for (int row = 0; row < rows.Length; row++)
			{
				int n = rows[row];
				for (int i = 0; i < n; i++)
				{
					float z = (i - (n - 1) * 0.5f) * radius * 2.02f;
					float y = radius + row * radius * 1.73f;
					float shift = (d.Rand(key, 1) - 0.5f) * 0.4f;
					float r = radius * d.Range(key, 2, 0.88f, 1.08f);
					Log(d, new Vector3(-length * 0.5f + shift, y, z), new Vector3(length * 0.5f + shift, y, z + (d.Rand(key, 3) - 0.5f) * 0.1f), r, key++, 0f, StructureMaterial.Timber, 8);
				}
			}
			// Stakes holding the bottom row.
			float edge = rows[0] * radius + 0.12f;
			for (int s = -1; s <= 1; s += 2)
			{
				for (int e = -1; e <= 1; e += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(e * 1.6f, -0.3f, s * edge), new Vector3(e * 1.6f, 0.75f, s * (edge + 0.08f)), 0.1f, 0.1f, StructureMaterial.Timber, 40 + (s > 0 ? 2 : 0) + (e > 0 ? 1 : 0)));
				}
			}
			if (d.Variant == 1)
			{
				Log(d, new Vector3(-2.2f, 0.28f, edge + 0.9f), new Vector3(2.4f, 0.28f, edge + 1.3f), 0.28f, 50);
			}
		}

		private static void PlankStack(StructureDraft d)
		{
			int layers = d.Variant == 0 ? 5 : 3;
			const float length = 4f, plank = 0.32f, thick = 0.06f;
			for (int b = -1; b <= 1; b++)
			{
				d.Add(StructureShapes.Beam(new Vector3(b * 1.6f, 0.08f, -0.85f), new Vector3(b * 1.6f, 0.08f, 0.85f), 0.16f, 0.16f, StructureMaterial.Timber, 1 + b + 1));
			}
			float y = 0.16f;
			for (int l = 0; l < layers; l++)
			{
				if (d.Mid)
				{
					for (int p = 0; p < 4; p++)
					{
						float z = (p - 1.5f) * (plank + 0.06f);
						d.Add(StructureShapes.Box(new Vector3((d.Rand(10 + l, p) - 0.5f) * 0.15f, y + thick * 0.5f, z), new Vector3(length, thick, plank), StructureMaterial.Timber, 10 + l * 8 + p)).Grain = Vector3.right;
					}
				}
				else
				{
					d.Add(StructureShapes.Box(new Vector3(0f, y + thick * 0.5f, 0f), new Vector3(length, thick, 4f * plank + 0.18f), StructureMaterial.Timber, 10 + l * 8)).Grain = Vector3.right;
				}
				y += thick;
				if (l < layers - 1)
				{
					// Sticks across, so the air reaches every board.
					for (int s = -1; s <= 1; s++)
					{
						d.Add(StructureShapes.Box(new Vector3(s * 1.6f, y + 0.015f, 0f), new Vector3(0.04f, 0.03f, 1.6f), StructureMaterial.Timber, 60 + l * 3 + s + 1, StructureRole.Detail));
					}
					y += 0.03f;
				}
			}
			if (d.Variant == 1)
			{
				for (int i = 0; i < 3; i++)
				{
					d.Add(StructureShapes.Block(new Vector3(-0.9f + i * 0.45f, 1.05f, 0.95f), new Vector3(0.3f, 2.2f, thick), Quaternion.Euler(-14f, 0f, 0f), StructureMaterial.Timber, 80 + i)).Grain = Vector3.up;
				}
			}
		}

		// ── Farm: granary, dovecote, bees ─────────────────────────────

		private static void Granary(StructureDraft d)
		{
			const float width = 4f, depth = 5f, floor = 0.85f, wall = 2.2f;
			// Nine staddles: a stone stalk and a wider cap no rat can climb round.
			for (int i = 0; i < 3; i++)
			{
				for (int k = 0; k < 3; k++)
				{
					var at = new Vector3((i - 1) * (width * 0.5f - 0.3f), 0f, (k - 1) * (depth * 0.5f - 0.3f));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.2f), new Vector2(0.22f, -0.2f), new Vector2(0.16f, 0.5f), new Vector2(0.42f, 0.55f), new Vector2(0.4f, 0.68f), new Vector2(0f, 0.68f) },
						d.Seg(8, 5), StructureMaterial.Stone, 1 + i * 3 + k).Transform(Matrix4x4.Translate(at)));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, floor - 0.08f, 0f), new Vector3(width + 0.1f, 0.2f, depth + 0.1f), StructureMaterial.Timber, 20)).Grain = Vector3.right;
			Walls(d, Vector3.zero, width, depth, 0.1f, floor, floor + wall, StructureMaterial.Timber, 21);
			const float rise = 1.9f;
			GableRoof(d, width, depth, floor + wall, rise, 0.4f, d.Variant == 1 ? 0.12f : 0.3f, d.Variant == 1 ? StructureMaterial.Timber : StructureMaterial.Thatch, 30, Matrix4x4.identity);
			Gable(d, width - 0.05f, floor + wall, rise - 0.1f, -depth * 0.5f + 0.05f, 0.1f, StructureMaterial.Timber, 34, Matrix4x4.identity);
			Gable(d, width - 0.05f, floor + wall, rise - 0.1f, depth * 0.5f - 0.05f, 0.1f, StructureMaterial.Timber, 35, Matrix4x4.identity);
			// The door in the front gable, and stone steps that stop a stride short of it.
			d.Add(StructureShapes.Box(new Vector3(0f, floor + 0.85f, -depth * 0.5f - 0.04f), new Vector3(0.9f, 1.6f, 0.04f), StructureMaterial.Ash, 40, StructureRole.Detail));
			d.Add(StructureShapes.Box(new Vector3(0f, floor + 0.85f, -depth * 0.5f - 0.07f), new Vector3(0.85f, 1.55f, 0.04f), StructureMaterial.Timber, 41, StructureRole.Detail)).Grain = Vector3.up;
			for (int i = 0; i < 2; i++)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.12f + i * 0.25f, -depth * 0.5f - 1.1f + i * 0.3f), new Vector3(1.1f, 0.25f + i * 0.25f, 0.4f), d.Bevel(0.03f),
					StructureMaterial.Fieldstone, 42 + i));
			}
		}

		private static void Dovecote(StructureDraft d)
		{
			const float outer = 2.75f, inner = 2.15f, height = 6f;
			bool thatch = d.Variant == 1;
			RingWallWithDoor(d, Vector3.zero, inner, outer, -0.3f, height, d.Seg(24, 12), 1.5f * Mathf.PI, 0.9f, 1.9f, StructureMaterial.Stone, 1);
			// A string course halfway, and the eaves' course.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 3f), new Vector2(outer + 0.1f, 3f), new Vector2(outer + 0.1f, 3.2f), new Vector2(0f, 3.2f) }, d.Seg(24, 12), StructureMaterial.Fieldstone, 20));
			// The roof, a cone, and its louvre: a little timber lantern the birds come and go by.
			float roofRise = thatch ? 3.4f : 3f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, height - 0.1f), new Vector2(outer + 0.45f, height - 0.1f), new Vector2(outer + 0.45f, height + 0.05f), new Vector2(0.5f, height + roofRise - 0.35f), new Vector2(0f, height + roofRise - 0.3f) },
				d.Seg(24, 12), thatch ? StructureMaterial.Thatch : StructureMaterial.Stone, 30, true, 0f, false, false, StructureRole.Roof));
			float lt = height + roofRise - 0.4f;
			d.Add(StructureShapes.Box(new Vector3(0f, lt + 0.4f, 0f), new Vector3(0.9f, 0.8f, 0.9f), StructureMaterial.Timber, 31, StructureRole.Roof));
			for (int s = 0; s < 4; s++)
			{
				float a = s * 90f * Mathf.Deg2Rad;
				d.Add(StructureShapes.Box(new Vector3(Mathf.Sin(a) * 0.46f, lt + 0.4f, Mathf.Cos(a) * 0.46f), new Vector3(Mathf.Abs(Mathf.Cos(a)) * 0.5f + 0.02f, 0.35f, Mathf.Abs(Mathf.Sin(a)) * 0.5f + 0.02f),
					StructureMaterial.Ash, 32 + s, StructureRole.Detail));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, lt + 0.75f), new Vector2(0.75f, lt + 0.75f), new Vector2(0f, lt + 1.4f) }, d.Seg(8, 4), StructureMaterial.Timber, 36, true, 0f, false, false, StructureRole.Roof));
			// Flight holes with stone ledges under them, round the upper wall.
			if (d.Mid)
			{
				int holes = d.Fine ? 10 : 6;
				for (int i = 0; i < holes; i++)
				{
					float a = (i + 0.5f) * 2f * Mathf.PI / holes;
					var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
					Quaternion turn = Quaternion.LookRotation(dir);
					d.Add(StructureShapes.Block(dir * (outer + 0.005f) + Vector3.up * 4.6f, new Vector3(0.18f, 0.2f, 0.04f), turn, StructureMaterial.Ash, 40 + i * 2, StructureRole.Detail));
					d.Add(StructureShapes.Block(dir * (outer + 0.12f) + Vector3.up * 4.44f, new Vector3(0.4f, 0.06f, 0.26f), turn, StructureMaterial.Fieldstone, 41 + i * 2, StructureRole.Detail));
				}
			}
			// The door's leaf, set in the doorway.
			d.Add(StructureShapes.Box(new Vector3(0f, 0.95f, -outer + 0.25f), new Vector3(0.85f, 1.85f, 0.06f), StructureMaterial.Timber, 70, StructureRole.Detail)).Grain = Vector3.up;
		}

		private static void Apiary(StructureDraft d)
		{
			void Skep(Vector3 at, int key)
			{
				// Coiled straw: a dome, banded at the middle level and finer.
				var profile = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.2f, 0.12f), new Vector2(0.18f, 0.28f), new Vector2(0.12f, 0.4f), new Vector2(0f, 0.45f) };
				d.Add(StructureShapes.Lathe(profile, d.Seg(10, 6), StructureMaterial.Thatch, key).Transform(Matrix4x4.Translate(at)));
				d.Add(StructureShapes.Box(at + new Vector3(0f, 0.03f, -0.19f), new Vector3(0.08f, 0.05f, 0.04f), StructureMaterial.Ash, key + 1, StructureRole.Detail));
			}
			if (d.Variant == 0)
			{
				// A plank bench on two trestles under a lean-to roof.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * 1.05f, 0.25f, 0f), new Vector3(0.12f, 0.6f, 0.5f), StructureMaterial.Timber, 1 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Beam(new Vector3(s * 1.3f, -0.3f, 0.35f), new Vector3(s * 1.3f, 1.55f, 0.35f), 0.1f, 0.1f, StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0)));
					d.Add(StructureShapes.Beam(new Vector3(s * 1.3f, -0.3f, -0.35f), new Vector3(s * 1.3f, 1.2f, -0.35f), 0.1f, 0.1f, StructureMaterial.Timber, 5 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.58f, 0f), new Vector3(2.5f, 0.06f, 0.55f), StructureMaterial.Timber, 7)).Grain = Vector3.right;
				d.Add(StructureShapes.Block(new Vector3(0f, 1.42f, 0f), new Vector3(2.9f, 0.05f, 1.1f), Quaternion.Euler(-18f, 0f, 0f), StructureMaterial.Timber, 8)).Grain = Vector3.forward;
				for (int i = 0; i < 4; i++)
				{
					Skep(new Vector3(-0.9f + i * 0.6f, 0.61f, 0f), 20 + i * 2);
				}
			}
			else
			{
				// Bee boles: a short stone wall with three niches, a skep in each.
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.55f, 0.15f), new Vector3(2.6f, 1.4f, 0.6f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
				for (int i = 0; i < 3; i++)
				{
					float x = -0.8f + i * 0.8f;
					d.Add(StructureShapes.Box(new Vector3(x, 0.72f, -0.14f), new Vector3(0.55f, 0.6f, 0.04f), StructureMaterial.Ash, 2 + i, StructureRole.Detail));
					Skep(new Vector3(x, 0.44f, -0.32f), 20 + i * 2);
					d.Add(StructureShapes.Box(new Vector3(x, 0.42f, -0.3f), new Vector3(0.6f, 0.05f, 0.4f), StructureMaterial.Fieldstone, 10 + i));
				}
			}
		}

		// ── Kilns and pits ────────────────────────────────────────────

		private static void CharcoalClamp(StructureDraft d)
		{
			// The clamp: cordwood stacked round a flue, turfed over and smouldering.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.3f), new Vector2(2.6f, -0.3f), new Vector2(2.5f, 0.3f), new Vector2(2.0f, 1.25f), new Vector2(1.1f, 1.8f), new Vector2(0.35f, 1.95f), new Vector2(0f, 1.95f) },
				d.Seg(16, 8), StructureMaterial.Earth, 1));
			d.Add(StructureShapes.Cylinder(0.3f, 1.9f, 1.99f, d.Seg(8, 5), StructureMaterial.Ash, 2, false, StructureRole.Detail));
			d.Emit(new Vector3(0f, 2.1f, 0f), SceneEmitterKind.Smoke);
			d.Emit(new Vector3(0f, 1.9f, 0f), SceneEmitterKind.Fire);
			// The ladder up its side.
			var foot = new Vector3(2.9f, 0f, -0.4f);
			var head = new Vector3(1.0f, 1.95f, -0.3f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(foot + Vector3.forward * s * 0.25f, head + Vector3.forward * s * 0.25f, 0.06f, 0.06f, StructureMaterial.Timber, 3 + (s > 0 ? 1 : 0)));
			}
			if (d.Mid)
			{
				for (int i = 1; i < 6; i++)
				{
					Vector3 p = Vector3.Lerp(foot, head, i / 6f);
					d.Add(StructureShapes.Beam(p + Vector3.back * 0.25f, p + Vector3.forward * 0.25f, 0.04f, 0.04f, StructureMaterial.Timber, 5 + i, StructureRole.Detail));
				}
			}
			// Cordwood stacked ready for the next burn.
			for (int row = 0; row < 3; row++)
			{
				for (int i = 0; i < 4 - row; i++)
				{
					float z = -3.4f + (i - (3 - row) * 0.5f) * 0.32f;
					Log(d, new Vector3(-0.6f, 0.15f + row * 0.27f, z), new Vector3(0.6f, 0.15f + row * 0.27f, z), 0.15f, 20 + row * 5 + i, 0f, StructureMaterial.Timber, 6);
				}
			}
			if (d.Variant == 1)
			{
				// The collier's hut: poles in a cone, turfed, a dark door.
				var hut = new Vector3(-3.6f, 0f, 1.8f);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.3f), new Vector2(1.6f, -0.3f), new Vector2(1.5f, 0.1f), new Vector2(0.1f, 2.6f), new Vector2(0f, 2.65f) }, d.Seg(10, 6),
					StructureMaterial.Earth, 40).Transform(Matrix4x4.Translate(hut)));
				d.Add(StructureShapes.Block(hut + new Vector3(0.95f, 0.6f, -0.95f), new Vector3(0.6f, 1.1f, 0.05f), Quaternion.Euler(0f, -45f, -18f), StructureMaterial.Ash, 41, StructureRole.Detail));
				for (int i = 0; i < 4; i++)
				{
					float a = (i * 90f + 20f) * Mathf.Deg2Rad;
					d.Add(StructureShapes.Beam(hut + new Vector3(Mathf.Cos(a) * 0.15f, 2.4f, Mathf.Sin(a) * 0.15f), hut + new Vector3(Mathf.Cos(a) * 0.35f, 3.0f, Mathf.Sin(a) * 0.35f), 0.07f, 0.07f,
						StructureMaterial.Timber, 42 + i, StructureRole.Detail));
				}
			}
		}

		private static void LimeKiln(StructureDraft d)
		{
			const float height = 3.8f;
			if (d.Variant == 0)
			{
				// A battered square stack: wider at its foot.
				var rings = new List<Vector3[]>();
				foreach ((float y, float half) in new[] { (-0.3f, 2.6f), (height, 2.0f) })
				{
					rings.Add(new[] { new Vector3(-half, y, -half), new Vector3(half, y, -half), new Vector3(half, y, half), new Vector3(-half, y, half) });
				}
				d.Add(StructureShapes.Loft(rings, StructureMaterial.Fieldstone, 1));
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.3f), new Vector2(2.5f, -0.3f), new Vector2(2.0f, height), new Vector2(0f, height) }, d.Seg(18, 9), StructureMaterial.Fieldstone, 1));
			}
			// The bowl's dark mouth at the top, burning, and the draw-arch at the front, glowing.
			d.Add(StructureShapes.Cylinder(1.1f, height - 0.02f, height + 0.04f, d.Seg(12, 6), StructureMaterial.Ash, 2, false, StructureRole.Detail));
			d.Emit(new Vector3(0f, height + 0.3f, 0f), SceneEmitterKind.Smoke);
			float front = d.Variant == 0 ? -2.45f : -2.35f;
			d.Add(StructureShapes.Box(new Vector3(0f, 0.65f, front - 0.04f), new Vector3(1.1f, 1.3f, 0.06f), StructureMaterial.Ash, 3, StructureRole.Detail));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.42f, front - 0.12f), new Vector3(1.7f, 0.3f, 0.3f), d.Bevel(0.03f), StructureMaterial.Stone, 4));
			d.Emit(new Vector3(0f, 0.45f, front + 0.2f), SceneEmitterKind.Forge);
			if (!d.Mid)
			{
				return;
			}
			// Limestone heaped to one side, burnt lime white on the other, a woodpile behind.
			for (int i = 0; i < 6; i++)
			{
				var at = new Vector3(3.3f + (i % 3) * 0.55f, 0.2f + (i / 3) * 0.35f, -1.2f + (i % 2) * 0.5f);
				d.Add(RoughStone(d, at, new Vector3(0.55f, 0.45f, 0.5f), i * 47f, 10 + i));
			}
			d.Add(StructureShapes.Sphere(new Vector3(-3.2f, 0f, -1.0f), new Vector3(1.0f, 0.55f, 0.8f), d.Seg(10, 6), 4, StructureMaterial.Plaster, 20, StructureRole.Detail));
			for (int i = 0; i < 4; i++)
			{
				Log(d, new Vector3(-1.4f, 0.16f + (i / 2) * 0.3f, 3.0f + (i % 2) * 0.32f), new Vector3(1.4f, 0.16f + (i / 2) * 0.3f, 3.0f + (i % 2) * 0.32f), 0.15f, 30 + i, 0f, StructureMaterial.Timber, 6);
			}
		}

		private static void TanningYard(StructureDraft d)
		{
			int cols = d.Variant == 0 ? 3 : 2;
			const float pit = 1.6f, gap = 0.5f;
			int key = 1;
			for (int r = 0; r < 2; r++)
			{
				for (int c = 0; c < cols; c++)
				{
					var at = new Vector3((c - (cols - 1) * 0.5f) * (pit + gap), 0f, (r - 0.5f) * (pit + gap));
					bool lime = d.Variant == 1 && r == 1 && c == 1;
					// The liquor, dark (or a lime pit white), in its timber lining.
					d.Add(StructureShapes.Box(at + Vector3.up * 0.04f, new Vector3(pit - 0.1f, 0.04f, pit - 0.1f), lime ? StructureMaterial.Plaster : StructureMaterial.Ash, key++));
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Beam(at + new Vector3(-pit * 0.5f - 0.06f, 0.1f, s * pit * 0.5f), at + new Vector3(pit * 0.5f + 0.06f, 0.1f, s * pit * 0.5f), 0.13f, 0.2f, StructureMaterial.Timber, key++));
						d.Add(StructureShapes.Beam(at + new Vector3(s * pit * 0.5f, 0.1f, -pit * 0.5f + 0.06f), at + new Vector3(s * pit * 0.5f, 0.1f, pit * 0.5f - 0.06f), 0.13f, 0.2f, StructureMaterial.Timber, key++));
					}
				}
			}
			// Hides stretched to dry on two frames behind the pits.
			float back = pit + gap + 1.0f;
			for (int f = 0; f < 2; f++)
			{
				float x = (f - 0.5f) * 2.6f;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(x + s * 1.0f, -0.3f, back), new Vector3(x + s * 1.0f, 2.0f, back), 0.1f, 0.1f, StructureMaterial.Timber, 60 + f * 4 + (s > 0 ? 1 : 0)));
				}
				d.Add(StructureShapes.Beam(new Vector3(x - 1.1f, 1.9f, back), new Vector3(x + 1.1f, 1.9f, back), 0.08f, 0.08f, StructureMaterial.Timber, 62 + f * 4));
				d.Add(StructureShapes.Box(new Vector3(x, 1.15f, back + 0.02f), new Vector3(1.6f, 1.35f, 0.03f), StructureMaterial.Canvas, 63 + f * 4, StructureRole.Detail));
			}
			if (d.Mid)
			{
				// Oak bark stacked for the liquor.
				for (int i = 0; i < 3; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(-cols * (pit + gap) * 0.5f - 1.0f, 0.2f + i * 0.2f, -0.6f + (i % 2) * 0.15f), new Vector3(0.9f, 0.18f, 1.6f), StructureMaterial.Timber, 80 + i, StructureRole.Detail)).Grain = Vector3.forward;
				}
			}
		}
	}
}
#endif
