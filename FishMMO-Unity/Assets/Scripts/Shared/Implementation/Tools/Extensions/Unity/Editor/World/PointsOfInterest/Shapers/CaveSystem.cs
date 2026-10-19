#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Where a cave system is opened to the surface: one per cave site it joins.</summary>
	public struct CaveMouth
	{
		/// <summary>The mouth's floor, in the system's frame.</summary>
		public Vector3 Floor;
		/// <summary>The way the mouth faces, out of the slope, degrees about +y.</summary>
		public float Yaw;
		public int SiteId;
		/// <summary>The bore's radius at the mouth.</summary>
		public float Radius;
		/// <summary>How far in from the mouth the roof may still be thin, metres.</summary>
		public float Zone;
		/// <summary>How far from the bore the shell rises to the rim near this mouth, high and on the ground before it.</summary>
		public float Collar, CollarLow;
		/// <summary>The shell's thickness at this mouth.</summary>
		public float Shell;
		/// <summary>How the mouth is come to (2026-10-18): over the ground before it, by diving, or along a shelf behind a waterfall.</summary>
		public CaveMouthKind Kind;
		/// <summary>The water's surface over a <see cref="CaveMouthKind.Submerged"/> mouth, world y; unused otherwise.</summary>
		public float Water;
		/// <summary>
		/// Where the way to a <see cref="CaveMouthKind.Shelf"/> mouth ends, on the dry bank beside the fall's pool (the system's
		/// frame): the shelf runs from there behind the curtain to the mouth. Unused otherwise (<see cref="CaveSystem.LandingOf"/>).
		/// </summary>
		public Vector3 Landing;
	}

	/// <summary>How a cave mouth is come to.</summary>
	public enum CaveMouthKind : byte
	{
		/// <summary>Over the ground before it, graded to its floor.</summary>
		Ground,
		/// <summary>Under a lake or the sea: dived into, a flooded passage (a sump) rising from it to dry chambers.</summary>
		Submerged,
		/// <summary>Behind a waterfall's curtain, along a shelf over its pool from the dry bank beside it.</summary>
		Shelf,
	}

	/// <summary>What a passage is in the system.</summary>
	public enum CavePassageKind : byte
	{
		/// <summary>From a mouth down to the system's deepest cavern.</summary>
		Trunk,
		/// <summary>A side passage off a chamber, to a room or a dead end.</summary>
		Branch,
		/// <summary>A deep passage joining two caves' systems.</summary>
		Link,
	}

	public sealed class CavePassage
	{
		public CavePassageKind Kind;
		public readonly List<CaveNode> Nodes = new List<CaveNode>();
	}

	/// <summary>What a chamber is.</summary>
	public enum CaveChamberKind : byte
	{
		/// <summary>A room along a passage, its own size.</summary>
		Room,
		/// <summary>The system's great cavern at the bottom of a trunk.</summary>
		Cavern,
		/// <summary>A small room at a branch's end.</summary>
		Alcove,
		/// <summary>A mine's worked-out stope.</summary>
		Stope,
		/// <summary>One lobe of an enormous hall (2026-10-13), or a natural cavern a mine broke into.</summary>
		Hall,
	}

	public struct CaveChamber
	{
		public Vector3 Centre;
		/// <summary>Along its heading, up, across.</summary>
		public Vector3 Radii;
		/// <summary>The floor's height under the centre; it rises <see cref="Slope"/> metres a metre along <see cref="Yaw"/>.</summary>
		public float Floor;
		public float Yaw;
		/// <summary>
		/// How the floor (and the vault over it, sheared with it) climbs along the chamber, rise over run: the slope of the
		/// passage it opens off, so a room on a descending passage is entered at its own floor and not over a drop.
		/// </summary>
		public float Slope;
		public CaveChamberKind Kind;
		/// <summary>The site whose cave this chamber belongs to (each cave site of a joined system keeps its own).</summary>
		public int SiteId;
		/// <summary>
		/// How rough its walls are against the system's own (its noise's share times this; 0 or 1 is the system's): a natural
		/// cavern a mine broke into is as rough as a cave, though the mine round it is smooth-cut.
		/// </summary>
		public float Rough;
	}

	/// <summary>
	/// A cave system (2026-10-11: "caves should go very deep, open up into chambers, connect to each other"): from each
	/// mouth a trunk passage descends deep under the hill, opening into rooms along the way and into a great cavern at its
	/// bottom; side passages wander off the rooms to alcoves and dead ends; and the systems of two caves near each other are
	/// joined deep down by a link passage, so one can be entered by one mouth and left by the other. Mines are the same
	/// machinery driven straight and level: an adit, drifts off it at right angles, stopes at their ends.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One signed field.</b> The void is the smooth union of every passage (chains of flat-floored capsules) and chamber
	/// (ellipsoids on flat floors), with rock noise on walls and roofs; the meshed solid is the rock SHELL round it, its
	/// inner face the cave seen from inside and its outer face buried — thick at each mouth, where it rises to a rim over the
	/// holed ground, thin deep in. Primitives are bucketed in a 16 m grid so a point reads only those near it.
	/// </para>
	/// <para>
	/// <b>Walkable all the way.</b> No floor is steeper than <see cref="WalkPitch"/> (the character controller stands on
	/// 60°, the NavMesh agent walks 45°), floors are cut flat, and every passage keeps <see cref="MinCover"/> of rock over its
	/// roof past its mouth: where a step would break out, it turns or dives, and the walk ends where neither does.
	/// </para>
	/// <para>
	/// <b>Frame.</b> Everything is in the system's frame: world axes, origin at <see cref="Origin"/> (the first mouth's
	/// floor). Pure: no scene, no terrain, no editor.
	/// </para>
	/// </remarks>
	public sealed partial class CaveSystem
	{
		/// <summary>
		/// 6 (2026-10-18, later): each mouth's kind, water and landing (mouths under water and behind waterfalls, second mouths
		/// through a hill). 5 (2026-10-18): mouths' floors at the ground's own height with the approach graded to them, no outcrop or notch, and
		/// greater halls (a cave stored before is cut again: its floor stood a rim over the ground). 4 (2026-10-13): characters,
		/// solids, rifts, shafts, crossings, climbs, tall nodes, rough chambers.
		/// </summary>
		public const int Format = 6;
		public const float MinCover = 3f;
		public const float FloorDepth = 0.45f;
		public const float ApronBand = 2.5f;
		public const float CreaseDegrees = 50f;
		public const float SeaFootMetres = 4f;
		/// <summary>The steepest a passage's floor runs, degrees.</summary>
		public const float WalkPitch = 22f;
		/// <summary>The narrowest bore a passage is cut to: 2.3 m of headroom over its flat floor, a tall person's walk.</summary>
		public const float MinBore = 1.6f;
		/// <summary>How far over the sea every floor of a cave that is not the sea's stays, metres.</summary>
		public const float AboveSea = 2f;
		/// <summary>How far under the water an awash cave's (a sea cave's, a grotto's) floors may lie, metres.</summary>
		public const float AwashDepth = 1.5f;
		/// <summary>A grotto's chamber floor rises out of its pool at this slope, so its back is a dry ledge.</summary>
		public const float GrottoSlope = 0.2f;

		/// <summary>The water an awash cave opens on (world y; NaN for a dry cave): a grotto's pool stands at it.</summary>
		public float WaterLevel = float.NaN;
		/// <summary>The lowest any of its floors may lie, world y.</summary>
		public float LowestFloor = float.NegativeInfinity;

		/// <summary>The slope a mine's decline is sunk at where there is no face to drive an adit into, degrees.</summary>
		public const float DeclinePitch = 15f;
		/// <summary>The steepest a chamber's floor runs, rise over run (about 18°).</summary>
		public const float MaxSlope = 0.33f;
		/// <summary>The size of the field's buckets, metres.</summary>
		private const float Bucket = 16f;

		public CaveForm Form;
		public byte SizeClass;
		public int Seed;
		public Vector3 Origin;
		/// <summary>The trunk's nominal radius, metres.</summary>
		public float Radius;
		public float Flat = 1f;
		public float NoiseShare = 0.3f;
		public float Rim = 0.35f;
		/// <summary>How far from the opening the collar's top stands its full rim over the ground, metres; beyond, flush.</summary>
		public const float LipBand = 1.2f;
		/// <summary>The collar's top over the ground away from the lip, metres: just proud of the terrain, inside its contact band.</summary>
		public const float FlushRim = 0.06f;
		/// <summary>How far the collar's outline wanders out past the holes it covers, metres at most.</summary>
		public const float CollarWander = 1.2f;
		public float Skin = 1f;
		public float ShellDeep = 2f;
		public float MeshCell = 0.75f;
		public readonly List<CaveMouth> Mouths = new List<CaveMouth>();
		public readonly List<CavePassage> Passages = new List<CavePassage>();
		public readonly List<CaveChamber> Chambers = new List<CaveChamber>();

		// ── Proportions ───────────────────────────────────────────

		/// <summary>A form's proportions at a size.</summary>
		public struct Profile
		{
			public float RadiusMin, RadiusMax, TrunkMin, TrunkMax, DepthMin, DepthMax, Pitch, Turn, Flat, Noise;
			/// <summary>Metres between rooms along the trunk, min..max.</summary>
			public float RoomEveryMin, RoomEveryMax;
			/// <summary>A room's radius as a share of the bore's, min..max, and the cavern's.</summary>
			public float RoomMin, RoomMax, CavernMin, CavernMax;
			public int BranchesMin, BranchesMax;
			public float BranchMin, BranchMax;
			/// <summary>Side passages leave at right angles and run straight (a mine's drifts).</summary>
			public bool Square;
		}

		/// <summary>
		/// The forms at their sizes: a grotto a short passage into one chamber; caves 90–480 m of trunk falling 20–140 m below
		/// their mouths through two to five rooms to a cavern, with side passages (rooms and caverns a third greater since
		/// 2026-10-18, "Caves need even bigger hallows": a large cave's cavern up to eight bores, ~110 m across); ice caves a little smaller and smoother; lava
		/// tubes long, low, wide and nearly level; sea caves at the waterline and shallow; mines straight, level adits with
		/// drifts at right angles and stopes.
		/// </summary>
		public static Profile ProfileFor(CaveForm form, int size)
		{
			size = Mathf.Clamp(size, 0, 2);
			switch (form)
			{
				case CaveForm.Grotto:
					return new Profile { RadiusMin = 2f + 0.4f * size, RadiusMax = 2.8f + 0.6f * size, TrunkMin = 14f, TrunkMax = 30f, DepthMin = 2f, DepthMax = 6f, Pitch = 12f, Turn = 14f, Flat = 1f, Noise = 0.3f,
						RoomEveryMin = 999f, RoomEveryMax = 999f, CavernMin = 1.9f, CavernMax = 2.6f };
				case CaveForm.LavaTube:
					return new Profile { RadiusMin = 3f + size, RadiusMax = 4.5f + 1.5f * size, TrunkMin = 160f + 80f * size, TrunkMax = 260f + 160f * size, DepthMin = 6f, DepthMax = 14f + 6f * size,
						Pitch = 5f, Turn = 6f, Flat = 0.62f, Noise = 0.18f, RoomEveryMin = 70f, RoomEveryMax = 140f, RoomMin = 1.4f, RoomMax = 2f, CavernMin = 2f, CavernMax = 3f,
						BranchesMin = 0, BranchesMax = size, BranchMin = 30f, BranchMax = 80f };
				case CaveForm.Mine:
					return new Profile { RadiusMin = 1.9f, RadiusMax = 2.3f, TrunkMin = 70f + 40f * size, TrunkMax = 120f + 60f * size, DepthMin = 10f + 10f * size, DepthMax = 25f + 15f * size,
						Pitch = 8f, Turn = 3f, Flat = 0.95f, Noise = 0.07f, RoomEveryMin = 35f, RoomEveryMax = 60f, RoomMin = 1.8f, RoomMax = 2.4f, CavernMin = 2.2f, CavernMax = 2.8f,
						BranchesMin = 1 + size, BranchesMax = 2 + 2 * size, BranchMin = 18f, BranchMax = 45f, Square = true };
				default:
				{
					float scale = form == CaveForm.IceCave ? 0.8f : form == CaveForm.SeaCave ? 0.55f : 1f;
					float noise = form == CaveForm.IceCave ? 0.2f : 0.3f;
					Profile p = size == 0
						? new Profile { RadiusMin = 2f, RadiusMax = 3.5f, TrunkMin = 90f, TrunkMax = 160f, DepthMin = 20f, DepthMax = 40f, RoomEveryMin = 45f, RoomEveryMax = 80f,
							RoomMin = 2f, RoomMax = 3f, CavernMin = 3.4f, CavernMax = 4.8f, BranchesMin = 0, BranchesMax = 1, BranchMin = 20f, BranchMax = 45f }
						: size == 1
						? new Profile { RadiusMin = 3f, RadiusMax = 5f, TrunkMin = 180f, TrunkMax = 300f, DepthMin = 40f, DepthMax = 80f, RoomEveryMin = 50f, RoomEveryMax = 95f,
							RoomMin = 2.2f, RoomMax = 3.4f, CavernMin = 4f, CavernMax = 6.5f, BranchesMin = 1, BranchesMax = 3, BranchMin = 25f, BranchMax = 70f }
						: new Profile { RadiusMin = 4f, RadiusMax = 7f, TrunkMin = 280f, TrunkMax = 480f, DepthMin = 70f, DepthMax = 140f, RoomEveryMin = 55f, RoomEveryMax = 110f,
							RoomMin = 2.4f, RoomMax = 3.8f, CavernMin = 4.5f, CavernMax = 8f, BranchesMin = 2, BranchesMax = 5, BranchMin = 30f, BranchMax = 90f };
					p.TrunkMin *= scale;
					p.TrunkMax *= scale;
					p.DepthMin *= scale;
					p.DepthMax *= scale;
					p.Pitch = WalkPitch;
					p.Turn = 12f;
					p.Flat = 1f;
					p.Noise = noise;
					return p;
				}
			}
		}

		// ── Planning ──────────────────────────────────────────────

		/// <summary>
		/// Plans a system from a mouth into the slope behind it. Null, with <paramref name="problem"/> saying why, where the rock
		/// is too thin or too low. Deterministic in its inputs.
		/// </summary>
		/// <param name="opening">How the mouth is come to when not over the ground before it (2026-10-18): dived to under a lake
		/// or the sea (a sump rising from it to dry chambers), or behind a waterfall on a shelf over its pool. Either is a
		/// classic system (one line), its bore no wider than its water hides.</param>
		public static CaveSystem Plan(CaveForm form, int size, Vector3 mouth, float yaw, int seed, int siteId, CaveGround ground, float seaLevel, out string problem,
			float waterLevel = float.NaN, CaveOpening opening = default)
		{
			problem = null;
			var rng = new DeterministicRNG(seed);
			Profile pf = ProfileFor(form, size);
			float r0 = rng.Range(pf.RadiusMin, pf.RadiusMax);
			if (opening.MaxRadius > 0f)
			{
				r0 = Mathf.Max(MinBore, Mathf.Min(r0, opening.MaxRadius));
			}
			bool submerged = opening.Kind == CaveMouthKind.Submerged, shelf = opening.Kind == CaveMouthKind.Shelf;
			var system = new CaveSystem
			{
				Form = form,
				SizeClass = (byte)Mathf.Clamp(size, 0, 2),
				Seed = seed,
				Radius = r0,
				Flat = pf.Flat,
				NoiseShare = pf.Noise,
				MeshCell = Mathf.Clamp(r0 / 4.5f, 0.45f, 1f),
			};
			float yr = yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));

			// The mouth opens where the slope behind the site turns steep (40° over two metres), else at the site.
			float mx = mouth.x, mz = mouth.z;
			for (float d = 0f; d <= 20f; d += 0.5f)
			{
				float x = mouth.x - outward.x * d, z = mouth.z - outward.z * d;
				if (!ground.Contains(x, z, 4f))
				{
					break;
				}
				if (ground.At(x - outward.x * 2f, z - outward.z * 2f) - ground.At(x, z) >= 1.68f)
				{
					mx = x;
					mz = z;
					break;
				}
			}
			float foot = ground.At(mx, mz);
			// The floor at the ground's own height (2026-10-18): it stood a rim over it, a step up at the threshold. The ground
			// before the mouth is graded to meet it (Approach), so the way in runs on from the terrain onto the floor level.
			float floor0 = shelf ? opening.Floor : foot;
			if (submerged)
			{
				// Wholly under its water, the arch's top too, and no deeper than a breath's dive.
				float under = opening.Water - foot;
				if (under < 2f * r0 * pf.Flat + SubmergedClear)
				{
					problem = $"its mouth lies {under:0.#} m under the water, too shallow to be wholly under it";
					return null;
				}
				if (under > MaxDive)
				{
					problem = $"its mouth lies {under:0.#} m under the water, too deep to dive to";
					return null;
				}
			}
			if (form == CaveForm.SeaCave)
			{
				if (foot > seaLevel + SeaFootMetres)
				{
					problem = $"the face's foot stands {foot - seaLevel:0.#} m over the sea, too high for the waves to have opened it";
					return null;
				}
				floor0 = seaLevel - 0.4f;
			}
			// A grotto opens at the water's edge, its floor awash through the mouth and its chamber half flooded
			// (2026-10-11): the sea's own surface fills it at the coast, a pool of the lake's or river's level inland.
			bool awash = form == CaveForm.SeaCave || form == CaveForm.Grotto;
			float water = form == CaveForm.SeaCave ? seaLevel : waterLevel;
			if (form == CaveForm.Grotto)
			{
				if (float.IsNaN(waterLevel))
				{
					problem = "a grotto opens at the water's edge, and no water stands before this face";
					return null;
				}
				if (foot > waterLevel + SeaFootMetres)
				{
					problem = $"the face's foot stands {foot - waterLevel:0.#} m over the water, too high for it to have opened a grotto";
					return null;
				}
				floor0 = waterLevel - 0.6f;
			}
			system.WaterLevel = awash ? water : submerged ? opening.Water : float.NaN;
			// Every other cave keeps its floors over the sea, all of them (2026-10-11): the sea's surface stands at its
			// level everywhere, so a passage under it would be a flooded one. A submerged mouth's system keeps them over its
			// own water too, all but its sump (Walker.RiseTo): the ocean fills whatever lies under the sea, and a lake's pool
			// is written over whatever of the system lies under the lake (CaveShaper.WritePool).
			system.LowestFloor = awash ? water - AwashDepth : submerged ? Mathf.Max(seaLevel, opening.Water) + AboveSea : seaLevel + AboveSea;
			if (!awash && !submerged && floor0 < system.LowestFloor)
			{
				problem = $"its mouth stands {floor0 - seaLevel:0.#} m over the sea; a cave that low is the sea's (a sea cave or a grotto)";
				return null;
			}
			system.Origin = new Vector3(mx, floor0, mz);
			var walker = new Walker(system, ground, pf, rng);
			float zone = Mathf.Max(14f, 3.5f * r0);
			system.Mouths.Add(new CaveMouth
			{
				Floor = Vector3.zero, Yaw = Mathf.Repeat(yaw, 360f), SiteId = siteId, Radius = r0, Zone = zone, Collar = 1.5f, CollarLow = 1.5f, Shell = 3f,
				Kind = opening.Kind, Water = opening.Water, Landing = shelf ? opening.Landing - system.Origin : Vector3.zero,
			});

			// The trunk: level through the mouth, then down toward its depth, rooms along it, the cavern at its end.
			float trunkLength = rng.Range(pf.TrunkMin, pf.TrunkMax);
			float depth = rng.Range(pf.DepthMin, pf.DepthMax);
			// Its character, from the seed hashed (a fresh stream's first draw follows its seed too closely): what kind of
			// system the rest of the plan lays (CaveSystem.Layout).
			system.Character = CharacterFor(form, size, Unit(unchecked((uint)seed), 0x2C1Bu));
			if (opening.Kind != CaveMouthKind.Ground)
			{
				system.Character = CaveCharacter.Classic;
			}
			bool graph = form != CaveForm.Mine && system.Character != CaveCharacter.Classic;
			int levels = graph ? LevelsFor(system.Character, size, rng) : 1;
			if (system.Character == CaveCharacter.Abyss)
			{
				depth *= 1.25f;
			}
			float level = awash ? 0.6f * r0 + 6f : 0.8f * r0 + 1f;
			var start = new CaveNode { Centre = outward * (0.2f * r0) + Vector3.up * (FloorDepth * r0 * pf.Flat), Radius = r0, Floor = 0f, Along = 0f };
			float shortest = form == CaveForm.Grotto ? 8f : Mathf.Max(zone + 5f, 0.35f * pf.TrunkMin);
			// A graph's trunk is only its way down to the first hub; the rest of its length goes to the hubs' branches.
			float trunkWalk = graph ? Mathf.Max(shortest + 12f, trunkLength * rng.Range(0.3f, 0.45f)) : trunkLength;
			// Under water, the trunk is first a sump climbing from the mouth to dry floors, then winds on a few metres over them.
			// Behind a waterfall, the river's bed is over its threshold: it keeps its full cover from there in.
			if (submerged)
			{
				walker.RiseTo = system.LowestFloor - system.Origin.y + SumpClear;
				depth = -(walker.RiseTo + Mathf.Lerp(1f, 6f, Unit(unchecked((uint)seed), 0x5A3Du)));
			}
			CavePassage trunk = walker.Walk(CavePassageKind.Trunk, start, Mathf.Repeat(yaw + 180f, 360f), trunkWalk, graph ? depth / levels : depth, level, shelf ? level : zone, awash);
			float along = trunk.Nodes[trunk.Nodes.Count - 1].Along;
			if (submerged)
			{
				walker.RiseTo = float.NegativeInfinity;
				if (trunk.Nodes[trunk.Nodes.Count - 1].Floor < system.LowestFloor - system.Origin.y)
				{
					problem = "its sump finds no dry rock to rise into under the hill";
					return null;
				}
			}
			if (along < shortest && form == CaveForm.Mine)
			{
				// No face to drive an adit into: a decline instead, sunk from the site at a walkable 15° through an open cut
				// (no roof for its first stretch) until it is deep enough to have one, then on as an adit would.
				Profile steep = pf;
				steep.Pitch = DeclinePitch;
				walker = new Walker(system, ground, steep, rng);
				system.Origin = new Vector3(mouth.x, ground.At(mouth.x, mouth.z), mouth.z);
				if (system.Origin.y < system.LowestFloor)
				{
					problem = $"no face to drive an adit into, and the ground here stands too near the sea to sink a decline";
					return null;
				}
				zone = (MinCover + (1f + FloorDepth) * r0 * pf.Flat + pf.Noise * r0) / Mathf.Tan(DeclinePitch * Mathf.Deg2Rad) + 8f;
				system.Mouths[0] = new CaveMouth { Floor = Vector3.zero, Yaw = Mathf.Repeat(yaw, 360f), SiteId = siteId, Radius = r0, Zone = zone, Collar = 1.5f, CollarLow = 1.5f, Shell = 3f };
				start.Centre = Vector3.up * (FloorDepth * r0 * pf.Flat);
				trunk = walker.Walk(CavePassageKind.Trunk, start, Mathf.Repeat(yaw + 180f, 360f), trunkLength, depth, 0f, zone, false, -1f, open: true);
				shortest = Mathf.Max(zone + 5f, 0.35f * pf.TrunkMin);
				along = trunk.Nodes[trunk.Nodes.Count - 1].Along;
			}
			if (along < shortest)
			{
				problem = along < zone
					? $"the face is too low or too thin to take a {r0:0.#} m bore under {MinCover} m of rock ({along:0} m in)"
					: $"only {along:0} m of the {pf.TrunkMin:0} m a cave this size runs fits under {MinCover} m of rock";
				return null;
			}
			if (graph)
			{
				var layout = new Layout(system, walker, pf, rng, siteId, size, depth, levels, trunkLength * BudgetShare(system.Character));
				if (!layout.Run(trunk, out problem))
				{
					return null;
				}
				system.Prepare();
				return system;
			}
			system.Passages.Add(trunk);
			// The field as it stands, for the chambers to keep clear of (Walker.Crosses), and again after each one.
			system.PrepareField();

			// Rooms along the trunk, each where the rock over it holds one.
			float next = form == CaveForm.Grotto ? float.MaxValue : zone + rng.Range(pf.RoomEveryMin, pf.RoomEveryMax) * 0.6f;
			for (int i = 1; i < trunk.Nodes.Count - 2; i++)
			{
				if (trunk.Nodes[i].Along < next)
				{
					continue;
				}
				CaveKind roomKind = form == CaveForm.Mine ? CaveKind.Stope : CaveKind.Room;
				if (walker.TryChamber(trunk, i, rng.Range(pf.RoomMin, pf.RoomMax), roomKind, siteId, out CaveChamber room))
				{
					system.Chambers.Add(room);
					system.PrepareField();
					next = trunk.Nodes[i].Along + rng.Range(pf.RoomEveryMin, pf.RoomEveryMax);
				}
				else
				{
					next = trunk.Nodes[i].Along + 8f;
				}
			}
			// The cavern at the trunk's end (a grotto's one chamber, a mine's last stope): shrunk, then a step given back, until it fits.
			bool ended = false;
			for (int attempt = 0; attempt < 8 && trunk.Nodes.Count >= 3; attempt++)
			{
				CaveKind endKind = form == CaveForm.Mine ? CaveKind.Stope : CaveKind.Cavern;
				if (walker.TryChamber(trunk, trunk.Nodes.Count - 1, rng.Range(pf.CavernMin, pf.CavernMax), endKind, siteId, out CaveChamber cavern,
					form == CaveForm.Grotto ? GrottoSlope : (float?)null))
				{
					system.Chambers.Add(cavern);
					system.PrepareField();
					ended = true;
					break;
				}
				if (trunk.Nodes.Count <= 4 || trunk.Nodes[trunk.Nodes.Count - 2].Along < shortest)
				{
					break;
				}
				trunk.Nodes.RemoveAt(trunk.Nodes.Count - 1);
			}
			if (!ended)
			{
				problem = "no chamber fits under the rock at the trunk's end";
				return null;
			}

			// Side passages off the rooms (and the cavern), each to an alcove or a dead end.
			int branches = pf.BranchesMax > 0 ? rng.Range(pf.BranchesMin, pf.BranchesMax + 1) : 0;
			int rooms = system.Chambers.Count;
			for (int b = 0; b < branches && rooms > 0; b++)
			{
				CaveChamber from = system.Chambers[(int)(rng.NextFloat() * rooms) % rooms];
				float side = rng.NextFloat() < 0.5f ? -1f : 1f;
				float heading = from.Yaw + side * (pf.Square ? 90f : rng.Range(55f, 125f));
				float hr = heading * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr));
				float leave = Mathf.Max(from.Radii.x, from.Radii.z) * 0.75f;
				float br = Mathf.Max(MinBore, r0 * (pf.Square ? 1f : rng.Range(0.55f, 0.85f)));
				float startFloor = FloorOf(from, from.Centre + dir * leave);
				var branchStart = new CaveNode { Centre = from.Centre + dir * leave, Radius = br, Floor = startFloor, Along = 0f };
				branchStart.Centre.y = startFloor + FloorDepth * br * pf.Flat;
				float length = rng.Range(pf.BranchMin, pf.BranchMax);
				float drop = pf.Square ? 0f : rng.Range(-6f, 14f);
				CavePassage branch = walker.Walk(CavePassageKind.Branch, branchStart, heading, length, -startFloor + drop, 0f, 0f, false, br);
				if (branch.Nodes.Count < 4)
				{
					continue;
				}
				system.Passages.Add(branch);
				system.PrepareField();
				// An alcove at its end most of the time; otherwise it narrows and stops.
				if (rng.NextFloat() < 0.6f || pf.Square)
				{
					if (walker.TryChamber(branch, branch.Nodes.Count - 1, rng.Range(1.4f, 2f), pf.Square ? CaveKind.Stope : CaveKind.Alcove, siteId, out CaveChamber alcove))
					{
						system.Chambers.Add(alcove);
						system.PrepareField();
					}
				}
				else
				{
					for (int i = branch.Nodes.Count - 3; i < branch.Nodes.Count; i++)
					{
						CaveNode n = branch.Nodes[i];
						float t = (i - (branch.Nodes.Count - 3)) / 2f;
						n.Radius = Mathf.Lerp(n.Radius, Mathf.Max(MinBore, 0.7f * n.Radius), t);
						branch.Nodes[i] = n;
					}
				}
			}
			if (form == CaveForm.Mine)
			{
				// Its deeper levels: shafts and inclines down to drifts of their own, and now and then a natural cavern broken into.
				system.PrepareField();
				new Layout(system, walker, pf, rng, siteId, size, depth, 1, 0f).Levels(trunk);
			}
			system.Prepare();
			return system;
		}

		private enum CaveKind { Room, Cavern, Alcove, Stope }

		/// <summary>Walks passages and fits chambers under the ground (the cover rule), for one system.</summary>
		private sealed class Walker
		{
			private readonly CaveSystem system;
			private readonly CaveGround ground;
			private readonly Profile pf;
			private readonly DeterministicRNG rng;
			private readonly float amp;

			/// <summary>The lowest any floor may lie, in the system's frame (its <see cref="LowestFloor"/>).</summary>
			private float MinFloor => system.LowestFloor - system.Origin.y;

			public Walker(CaveSystem system, CaveGround ground, Profile pf, DeterministicRNG rng)
			{
				this.system = system;
				this.ground = ground;
				this.pf = pf;
				this.rng = rng;
				amp = pf.Noise * system.Radius;
			}

			/// <summary>How much taller than the system's section the next walk's bore stands (<see cref="CaveNode.Tall"/>).</summary>
			public float Tall = 1f;
			/// <summary>
			/// Whether the next walk keeps clear of the void already cut at other floors (past <see cref="AvoidFrom"/> metres
			/// along): a passage crossing another a few metres over its floor opened a hole into it. Meeting one at its own
			/// floor is a junction, and allowed.
			/// </summary>
			public bool Avoid;
			public float AvoidFrom;
			/// <summary>The steepest the next walk runs, degrees, in place of the profile's (a mine's incline); 0 for the profile's.</summary>
			public float PitchLimit;
			/// <summary>
			/// A submerged mouth's sump (system frame; −∞ for none): while its floor is under this the walk climbs as steeply as it
			/// may, under <see cref="LowestFloor"/> (the flooded part), never down.
			/// </summary>
			public float RiseTo = float.NegativeInfinity;

			public float Ground(Vector3 local) => ground.At(local.x + system.Origin.x, local.z + system.Origin.z) - system.Origin.y;
			public bool Inside(Vector3 local, float reach) => ground.Contains(local.x + system.Origin.x, local.z + system.Origin.z, reach);
			public float Amp => amp;

			private float Cover(Vector3 centre, float radius, Vector3 previous, float heading)
			{
				float top = centre.y + radius * pf.Flat * Tall + amp;
				float hr = heading * Mathf.Deg2Rad;
				var side = new Vector3(Mathf.Cos(hr), 0f, -Mathf.Sin(hr)) * (0.9f * radius);
				float cover = Ground(centre) - top;
				cover = Mathf.Min(cover, Ground(centre + side) - top);
				cover = Mathf.Min(cover, Ground(centre - side) - top);
				return Mathf.Min(cover, Ground((centre + previous) * 0.5f) - top);
			}

			/// <summary>
			/// A passage from <paramref name="start"/> on <paramref name="heading"/>, <paramref name="length"/> long: level
			/// for <paramref name="level"/>, then falling toward <paramref name="depth"/> metres below the system's origin and
			/// wandering about it there. Stops early where the rock will not hold it.
			/// </summary>
			/// <param name="open">An open cut through the mouth's zone (a decline): no roof needed there, and no level run.</param>
			public CavePassage Walk(CavePassageKind kind, CaveNode start, float heading, float length, float depth, float level, float zone, bool sea, float radius = -1f, bool open = false)
			{
				var passage = new CavePassage { Kind = kind };
				passage.Nodes.Add(start);
				float r0 = radius > 0f ? radius : system.Radius;
				float step = Mathf.Clamp(0.9f * r0, 2.5f, 5f);
				// No deeper than the lowest floor allows (the sea's level, or a little under the water at an awash mouth).
				depth = Mathf.Min(depth, Mathf.Max(0f, -MinFloor - 0.5f));
				float steepest = PitchLimit > 0f ? PitchLimit : pf.Pitch;
				float aim = heading, pitch = open ? -steepest : 0f, along = 0f, floor = start.Floor, rad = start.Radius;
				Vector3 centre = start.Centre;
				while (along < length)
				{
					float turn = rng.Range(-pf.Turn, pf.Turn);
					float tilt = rng.Range(-6f, 6f);
					float swell = rng.Range(0.93f, 1.07f);
					float nextRadius = Mathf.Clamp(rad * swell, Mathf.Max(MinBore, 0.85f * r0), Mathf.Max(MinBore, 1.15f * r0));
					float wander = heading + turn;
					wander += DeltaAngle(wander, aim) * (pf.Square ? 0.6f : 0.15f);
					wander = aim + Mathf.Clamp(DeltaAngle(aim, wander), -70f, 70f);
					float uphill = UphillHeading(ground, centre.x + system.Origin.x, centre.z + system.Origin.z, aim);
					// Level through the mouth; a sea cave's floor stays awash all through its mouth's zone.
					bool entering = along + step <= level || (sea && along + step <= zone);
					// Down toward the depth while above it; about level, wandering, once there.
					float below = -floor - depth;
					bool rising = floor < RiseTo;
					float want = entering ? 0f : rising ? steepest : below < -1f ? -steepest : below > 2f ? steepest * 0.5f : tilt * 0.5f;
					float wanderPitch = Mathf.Clamp(Mathf.MoveTowards(pitch, want, 8f) + tilt * 0.4f, -steepest, steepest);
					var candidates = new (float heading, float pitch)[]
					{
						(wander, entering ? 0f : wanderPitch),
						(wander, entering ? 0f : -steepest),
						(heading + Mathf.Clamp(DeltaAngle(heading, uphill), -2f * Mathf.Max(6f, pf.Turn), 2f * Mathf.Max(6f, pf.Turn)), entering ? 0f : -steepest),
						(heading + 60f, entering ? 0f : -steepest),
						(heading - 60f, entering ? 0f : -steepest),
					};
					bool taken = false;
					foreach ((float h, float p) in candidates)
					{
						float hr = h * Mathf.Deg2Rad;
						float run = step * Mathf.Cos(p * Mathf.Deg2Rad);
						float nextFloor = floor + step * Mathf.Sin(p * Mathf.Deg2Rad);
						var next = new Vector3(centre.x + Mathf.Sin(hr) * run, nextFloor + FloorDepth * nextRadius * pf.Flat * Tall, centre.z + Mathf.Cos(hr) * run);
						// The sump alone lies under the lowest floor, and only ever climbs.
						if (nextFloor < MinFloor && !(rising && nextFloor >= floor - 0.01f) || !Inside(next, nextRadius + 12f))
						{
							continue;
						}
						float nextAlong = along + step;
						float cover = Cover(next, nextRadius, centre, h);
						float need = kind != CavePassageKind.Trunk || nextAlong >= zone ? MinCover
							: open || nextAlong <= level ? float.NegativeInfinity : -(nextRadius * pf.Flat + amp);
						if (cover < need)
						{
							continue;
						}
						if (sea && nextAlong <= zone && Mathf.Abs(nextFloor) > 0.6f)
						{
							continue;
						}
						if (Avoid && nextAlong > AvoidFrom && (system.Smooth(next) < nextRadius * Mathf.Max(1f, pf.Flat * Tall) + 1.2f
							&& Mathf.Abs(system.FloorNear(next) - nextFloor) > 0.6f || system.NearChasmOrClimb(next, nextRadius + 1f)))
						{
							continue;
						}
						centre = next;
						heading = h;
						pitch = p;
						rad = nextRadius;
						floor = nextFloor;
						along = nextAlong;
						passage.Nodes.Add(new CaveNode { Centre = centre, Radius = rad, Floor = floor, Along = along, Tall = Tall });
						taken = true;
						break;
					}
					if (!taken)
					{
						break;
					}
				}
				return passage;
			}

			/// <summary>
			/// A chamber round node <paramref name="index"/> of a passage, its radius <paramref name="scale"/> of the bore's, if
			/// the rock holds it (shrinking up to six times). A cavern too tall for the rock over it is first given a lower vault
			/// (no lower than <see cref="LowestVault"/> of its breadth): a wide, low cavern before a small one.
			/// </summary>
			/// <param name="slopeOverride">The floor's slope to give it instead of the passage's (a grotto's rising out of its pool).</param>
			public bool TryChamber(CavePassage passage, int index, float scale, CaveKind kind, int siteId, out CaveChamber chamber, float? slopeOverride = null)
			{
				chamber = default;
				CaveNode at = passage.Nodes[index];
				CaveNode before = passage.Nodes[Mathf.Max(0, index - 1)];
				Vector3 dir = at.Centre - before.Centre;
				dir.y = 0f;
				dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
				bool end = index == passage.Nodes.Count - 1;
				float across = rng.Range(0.7f, 1f);
				// The passage's slope here, over the nodes either side (the last two at its end), at most a walk.
				CaveNode lo = passage.Nodes[Mathf.Max(0, end ? index - 2 : index - 1)], hi = passage.Nodes[Mathf.Min(passage.Nodes.Count - 1, index + 1)];
				float run = new Vector2(hi.Centre.x - lo.Centre.x, hi.Centre.z - lo.Centre.z).magnitude;
				float slope = slopeOverride ?? (run > 0.5f ? Mathf.Clamp((hi.Floor - lo.Floor) / run, -MaxSlope, MaxSlope) : 0f);
				var side = new Vector3(dir.z, 0f, -dir.x);
				for (int shrink = 0; shrink < 7; shrink++, scale *= 0.85f)
				{
					float rx = at.Radius * scale, rz = rx * across;
					// A cavern is tall; a room a little over the bore; a stope low.
					float ry = kind == CaveKind.Cavern ? Mathf.Max(6f, rx * rng.Range(0.45f, 0.7f))
						: kind == CaveKind.Stope ? Mathf.Max(3.2f, 1.2f * at.Radius * pf.Flat)
						: Mathf.Max(3.4f, rx * rng.Range(0.4f, 0.6f));
					float shift = end ? 0.55f * rx : 0f;
					Vector3 c = at.Centre + dir * shift;
					float floor = at.Floor + slope * shift;
					if (kind == CaveKind.Cavern)
					{
						ry = Mathf.Min(ry, Mathf.Max(Mathf.Max(6f, LowestVault * rx), RoomVault(c, dir, side, floor, rx, rz, slope) - 0.01f));
					}
					// Its low end no lower than the lowest floor (the sea's reach): smaller, or a step back up the passage, where
					// it would run under it. Never flattened to fit: a floor flatter than the passage it opens off leaves a drop.
					if (floor - Mathf.Abs(slope) * rx < MinFloor)
					{
						continue;
					}
					c.y = floor + 0.4f * ry;
					float top = c.y + ry + amp;
					float cover = Ground(c) - top;
					for (int k = 0; k < 8; k++)
					{
						float a = k * Mathf.PI / 4f, x = Mathf.Cos(a) * 0.9f * rx;
						Vector3 p = c + dir * x + side * (Mathf.Sin(a) * 0.9f * rz);
						// The vault is sheared with the floor: its roof here no higher than the centre's, moved by the slope.
						cover = Mathf.Min(cover, Ground(p) - (top + Mathf.Max(0f, slope * x)));
					}
					if (cover >= MinCover && Inside(c, Mathf.Max(rx, rz) + 12f)
						&& !Crosses(c, floor, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, new Vector3(rx, ry, rz), slope, q => NearChain(passage, q)))
					{
						chamber = new CaveChamber
						{
							Centre = c,
							Radii = new Vector3(rx, ry, rz),
							Floor = floor,
							Slope = slope,
							Yaw = Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f),
							Kind = kind == CaveKind.Cavern ? CaveChamberKind.Cavern : kind == CaveKind.Alcove ? CaveChamberKind.Alcove : kind == CaveKind.Stope ? CaveChamberKind.Stope : CaveChamberKind.Room,
							SiteId = siteId,
						};
						return true;
					}
				}
				return false;
			}

			/// <summary>
			/// Whether a chamber planned here would open into the void already cut at another floor (or leave less than two
			/// metres of rock between its vault and a passage over it): sampled through its volume and over its vault, a point
			/// in the void whose floor is not this chamber's own there is a crossing. A chamber opening on a passage at its own
			/// floor is a junction, and allowed; so is whatever <paramref name="exempt"/> lets be. With halls 250 m across
			/// (2026-10-18), a hall at a trunk's foot reached back under the trunk's first stretch and its vault broke up through
			/// the trunk's floor beside the mouth.
			/// </summary>
			public bool Crosses(Vector3 centre, float floor, float yaw, Vector3 radii, float slope, Func<Vector3, bool> exempt)
			{
				float yr = yaw * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
				var side = new Vector3(dir.z, 0f, -dir.x);
				for (int ring = 0; ring < 3; ring++)
				{
					float reach = 0.45f * ring;
					int spokes = ring == 0 ? 1 : 12;
					// Up through the vault and two metres of rock over it, the vault falling toward its rim.
					float height = (0.4f + Mathf.Sqrt(Mathf.Max(0f, 1f - reach * reach))) * radii.y + amp + 2f;
					for (int k = 0; k < spokes; k++)
					{
						float a = k * Mathf.PI / 6f, x = Mathf.Cos(a) * reach * radii.x;
						Vector3 at = new Vector3(centre.x, 0f, centre.z) + dir * x + side * (Mathf.Sin(a) * reach * radii.z);
						float own = floor + slope * x;
						for (float h = 1f; h <= height; h += 2.5f)
						{
							var p = new Vector3(at.x, own + h, at.z);
							if (system.Smooth(p) < 0.5f && Mathf.Abs(system.FloorNear(p) - own) > 0.6f && (exempt == null || !exempt(p)))
							{
								return true;
							}
						}
					}
				}
				return false;
			}

			/// <summary>Whether a point lies in or just round a passage's bore (the passage a chamber is entered by).</summary>
			public static bool NearChain(CavePassage passage, Vector3 p)
			{
				if (passage == null)
				{
					return false;
				}
				foreach (CaveNode n in passage.Nodes)
				{
					float r = n.Radius * Mathf.Max(1f, n.Height) + 2.5f;
					if ((p - n.Centre).sqrMagnitude < r * r)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>The lowest a great chamber's vault is let down to fit under the rock over it, a share of its breadth.</summary>
			public const float LowestVault = 0.22f;
			/// <summary>The cover a vault let down to the rock keeps past <see cref="MinCover"/>, a share of the noise's amplitude.</summary>
			private const float VaultMargin = 0.5f;

			/// <summary>
			/// The tallest vault (its half-height) the rock holds over a room or cavern of these radii about <paramref name="c"/>'s
			/// x/z on <paramref name="floor"/>: <see cref="TryChamber"/>'s own cover test (its middle and eight points round it at
			/// nine tenths of its reach, each under the vault's top raised by the floor's slope) solved for the height.
			/// </summary>
			private float RoomVault(Vector3 c, Vector3 dir, Vector3 side, float floor, float rx, float rz, float slope)
			{
				// The top stands 1.4 half-heights over the floor (the middle 0.4 up) and the noise over that; a vault let down
				// to the rock's limit keeps a margin past it, as the noise's sum of octaves can reach a little over its amplitude.
				float cover = MinCover + VaultMargin * amp + 0.25f;
				float best = (Ground(c) - floor - amp - cover) / 1.4f;
				for (int k = 0; k < 8; k++)
				{
					float a = k * Mathf.PI / 4f, x = Mathf.Cos(a) * 0.9f * rx;
					Vector3 p = c + dir * x + side * (Mathf.Sin(a) * 0.9f * rz);
					best = Mathf.Min(best, (Ground(p) - floor - amp - Mathf.Max(0f, slope * x) - cover) / 1.4f);
				}
				return best;
			}

			/// <summary>
			/// The tallest vault (its half-height) the rock holds over a hall's lobe of these radii (<see cref="TryLobe"/>'s cover
			/// test solved for the height: over its outer ring only the vault's own height there need be covered).
			/// </summary>
			public float LobeVault(Vector3 centre, float floor, float yaw, float rx, float rz, float slope, float rough)
			{
				float yr = yaw * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
				var side = new Vector3(dir.z, 0f, -dir.x);
				float noise = amp * Mathf.Max(1f, rough);
				float cover = MinCover + VaultMargin * noise + 0.25f;
				var c = new Vector3(centre.x, floor, centre.z);
				float best = (Ground(c) - floor - noise - cover) / 1.4f;
				for (int k = 0; k < 12; k++)
				{
					float a = k * Mathf.PI / 6f;
					for (float reach = 0.45f; reach <= 0.91f; reach += 0.45f)
					{
						float x = Mathf.Cos(a) * reach * rx;
						Vector3 p = c + dir * x + side * (Mathf.Sin(a) * reach * rz);
						float rise = 0.4f + Mathf.Sqrt(Mathf.Max(0f, 1f - reach * reach));
						best = Mathf.Min(best, (Ground(p) - floor - noise - Mathf.Max(0f, slope * x) - cover) / rise);
					}
				}
				return best;
			}

			/// <summary>
			/// A chamber of the given radii (along its heading, up, across) on a floor at <paramref name="floor"/> under
			/// <paramref name="centre"/>'s x/z, if the rock over it holds it and the ground's sample reaches round it: one lobe
			/// of a hall. No shrinking: the caller tries again smaller.
			/// </summary>
			/// <param name="exempt">Points of the void already cut that this lobe may open into at another floor: the passage it is
			/// entered by, the hall's other lobes (their terraces are ramped). Null for none.</param>
			public bool TryLobe(Vector3 centre, float floor, float yaw, Vector3 radii, float slope, CaveChamberKind kind, int siteId, float rough, out CaveChamber chamber,
				Func<Vector3, bool> exempt = null)
			{
				chamber = default;
				float rx = radii.x, ry = radii.y, rz = radii.z;
				if (floor - Mathf.Abs(slope) * rx < MinFloor)
				{
					return false;
				}
				float yr = yaw * Mathf.Deg2Rad;
				var dir = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
				var side = new Vector3(dir.z, 0f, -dir.x);
				Vector3 c = new Vector3(centre.x, floor + 0.4f * ry, centre.z);
				float top = c.y + ry + amp * Mathf.Max(1f, rough);
				float cover = Ground(c) - top;
				for (int k = 0; k < 12 && cover >= MinCover; k++)
				{
					float a = k * Mathf.PI / 6f;
					for (float reach = 0.45f; reach <= 0.91f; reach += 0.45f)
					{
						float x = Mathf.Cos(a) * reach * rx;
						Vector3 p = c + dir * x + side * (Mathf.Sin(a) * reach * rz);
						// The vault falls toward its rim: over the outer ring only its own height there need be covered.
						float rise = ry * Mathf.Sqrt(Mathf.Max(0f, 1f - reach * reach));
						cover = Mathf.Min(cover, Ground(p) - (c.y + rise + amp * Mathf.Max(1f, rough) + Mathf.Max(0f, slope * x)));
					}
				}
				if (cover < MinCover || !Inside(c, Mathf.Max(rx, rz) + 12f) || Crosses(c, floor, yaw, radii, slope, exempt))
				{
					return false;
				}
				chamber = new CaveChamber
				{
					Centre = c,
					Radii = radii,
					Floor = floor,
					Slope = slope,
					Yaw = Mathf.Repeat(yaw, 360f),
					Kind = kind,
					SiteId = siteId,
					Rough = rough,
				};
				return true;
			}
		}

		// ── Joining two systems ───────────────────────────────────

		/// <summary>Why the last <see cref="TryJoin"/> found no link, for the notes.</summary>
		public string JoinProblem;

		/// <summary>The headings a link tries off its course, degrees, in turn, to keep round the other passages and chambers.</summary>
		private static readonly float[] LinkTurns = { 0f, 35f, -35f, 70f, -70f, 105f, -105f, 140f, -140f };

		/// <summary>Takes another system's mouths, passages and chambers into this one's frame.</summary>
		private void Absorb(CaveSystem other, Vector3 shift)
		{
			foreach (CaveMouth m in other.Mouths)
			{
				CaveMouth moved = m;
				moved.Floor += shift;
				Mouths.Add(moved);
			}
			foreach (CavePassage p in other.Passages)
			{
				var moved = new CavePassage { Kind = p.Kind };
				foreach (CaveNode n in p.Nodes)
				{
					CaveNode m = n;
					m.Centre += shift;
					m.Floor += shift.y;
					moved.Nodes.Add(m);
				}
				Passages.Add(moved);
			}
			foreach (CaveChamber c in other.Chambers)
			{
				CaveChamber moved = c;
				moved.Centre += shift;
				moved.Floor += shift.y;
				Chambers.Add(moved);
			}
			foreach (CaveSolid o in other.Solids)
			{
				CaveSolid moved = o;
				moved.A += shift;
				moved.B += shift;
				Solids.Add(moved);
			}
			foreach (CaveRift r in other.Rifts)
			{
				CaveRift moved = r;
				moved.A += shift;
				moved.B += shift;
				moved.Top += shift.y;
				Rifts.Add(moved);
			}
			foreach (CaveShaft h in other.Shafts)
			{
				CaveShaft moved = h;
				moved.Bottom += shift;
				moved.Top += shift.y;
				Shafts.Add(moved);
			}
			foreach (CaveCrossing c in other.Crossings)
			{
				CaveCrossing moved = c;
				moved.A += shift;
				moved.B += shift;
				Crossings.Add(moved);
			}
			foreach (CaveClimb c in other.Climbs)
			{
				CaveClimb moved = c;
				moved.Bottom += shift;
				moved.Top += shift;
				Climbs.Add(moved);
			}
		}

		// ── The way in ────────────────────────────────────────────

		/// <summary>The grade the ground before a mouth ramps back to its own at, rise over run (about 18°).</summary>
		public const float ApproachGrade = 0.32f;
		/// <summary>The grade the approach's sides ease back to the ground at, rise over run (35°).</summary>
		public const float ApproachSideGrade = 0.7f;
		/// <summary>The most the approach cuts or fills the ground, metres; past it the ground is left as it is.</summary>
		public const float ApproachMaxGive = 10f;

		/// <summary>
		/// Whether this system's mouths are entered over the ground before them, which <see cref="Approach"/> then grades to
		/// their floors: every dry cave's. An awash cave (a sea cave, a grotto) is entered from its water, its floor under it;
		/// its shore is left as it is. Ask the system that planned a mouth (a joined host may be of another form).
		/// </summary>
		public bool GradesApproach => float.IsNaN(WaterLevel);

		/// <summary>
		/// A mouth's approach in plan (world x/z): its floor's point, the way out, the level run out to (metres from the
		/// floor's point, past the bore's end, its cut and the collar over it), its half-width (the arch's foot and a
		/// margin), and how far out from the face the hill's own foot is eased into it.
		/// </summary>
		private void ApproachFrame(CaveMouth mouth, out Vector2 at, out Vector2 outward, out float flat, out float half, out float face)
		{
			at = new Vector2(Origin.x + mouth.Floor.x, Origin.z + mouth.Floor.z);
			float yr = mouth.Yaw * Mathf.Deg2Rad;
			outward = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
			// The trunk's first node stands a fifth of the bore out of the face, its cap a bore further; the hole runs a margin
			// past that, and the collar a margin and its wander past the hole.
			flat = 1.2f * mouth.Radius + HoleMargin + CollarMargin + CollarWander + 1f;
			half = mouth.Radius * (1f + ArchSpread) + 1.5f;
			face = 0.6f * mouth.Radius;
		}

		/// <summary>
		/// The ground's height at (x, z) (world metres) with the approach before a mouth graded into it, given its height
		/// <paramref name="h"/> there now (2026-10-18: "allow a smoother transition into the cave itself"): level with the
		/// mouth's floor over a lane straight out from the opening, as wide as the arch's foot and running past the cut, then
		/// ramping back to the ground at <see cref="ApproachGrade"/>, its sides at <see cref="ApproachSideGrade"/>. Cut where
		/// the ground stood over the floor and filled where it fell away (a talus apron), never more than
		/// <see cref="ApproachMaxGive"/>; the hill's own foot is eased into it at the face, and nothing behind the face (the
		/// hill over the bore) is touched.
		/// </summary>
		/// <remarks>
		/// <b>Idempotent:</b> the ground is clamped into a band about the floor that depends on the position alone, so grading
		/// graded ground leaves it as it is, and a cut made again (or a Regenerate) never digs or builds it up further. Pure: a
		/// test grades a ground function with it exactly as the shaper grades the terrain's samples.
		/// </remarks>
		public float Approach(int mouthIndex, float x, float z, float h)
		{
			CaveMouth mouth = Mouths[mouthIndex];
			ApproachFrame(mouth, out Vector2 at, out Vector2 outward, out float flat, out float half, out float face);
			var d = new Vector2(x - at.x, z - at.y);
			float u = Vector2.Dot(d, outward);
			if (u < 0f)
			{
				return h;
			}
			float v = Mathf.Abs(d.x * outward.y - d.y * outward.x);
			float give = Mathf.Max(0f, u - flat) * ApproachGrade + Mathf.Max(0f, v - half) * ApproachSideGrade + Mathf.Max(0f, face - u);
			// The band widens without limit toward the most it gives, so the edit fades into the ground continuously and the
			// clamp stays one to a band fixed by position alone.
			float s = Mathf.Clamp01((give - 0.7f * ApproachMaxGive) / (0.3f * ApproachMaxGive));
			if (s >= 1f)
			{
				return h;
			}
			float band = give / (1f - s * s * (3f - 2f * s) + 1e-4f);
			float floor = Origin.y + mouth.Floor.y;
			return Mathf.Clamp(h, floor - band, floor + band);
		}

		/// <summary>The plan box (world x/z) <see cref="Approach"/> can change the ground in before a mouth.</summary>
		public Rect ApproachArea(int mouthIndex)
		{
			ApproachFrame(Mouths[mouthIndex], out Vector2 at, out Vector2 outward, out float flat, out float half, out _);
			float run = flat + ApproachMaxGive / ApproachGrade, wide = half + ApproachMaxGive / ApproachSideGrade;
			var side = new Vector2(outward.y, -outward.x);
			Vector2 lo = at, hi = at;
			foreach (Vector2 corner in new[] { at + side * wide, at - side * wide, at + outward * run + side * wide, at + outward * run - side * wide })
			{
				lo = Vector2.Min(lo, corner);
				hi = Vector2.Max(hi, corner);
			}
			return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
		}

		// ── The holes at a mouth ──────────────────────────────────

		public float HoleMargin => Rim + 1.25f * MeshCell;

		/// <summary>
		/// The terrain's hole quads one mouth opens (see CaveSolid.HoleCells): every quad near it whose surface comes within
		/// <see cref="HoleMargin"/> of the void. Sets that mouth's collar and shell from how far they reach.
		/// </summary>
		public List<Vector2Int> HoleCells(int mouthIndex, Func<float, float, float> groundWorld, float step, float gridX, float gridZ)
		{
			if (segA == null)
			{
				Prepare();
			}
			var cells = new List<Vector2Int>();
			CaveMouth mouth = Mouths[mouthIndex];
			float reachBox = mouth.Zone + 2f * mouth.Radius + 10f;
			Vector3 c0 = mouth.Floor + Origin;
			int i0 = Mathf.FloorToInt((c0.x - reachBox - gridX) / step), i1 = Mathf.CeilToInt((c0.x + reachBox - gridX) / step);
			int k0 = Mathf.FloorToInt((c0.z - reachBox - gridZ) / step), k1 = Mathf.CeilToInt((c0.z + reachBox - gridZ) / step);
			float needed = 0f, neededLow = 0f;
			var heights = new float[9];
			for (int k = k0; k < k1; k++)
			{
				for (int i = i0; i < i1; i++)
				{
					float x0 = gridX + i * step, z0 = gridZ + k * step;
					float lo = float.MaxValue, hi = float.MinValue;
					for (int s = 0; s < 9; s++)
					{
						heights[s] = groundWorld(x0 + (s % 3) * 0.5f * step, z0 + (s / 3) * 0.5f * step);
						lo = Mathf.Min(lo, heights[s]);
						hi = Mathf.Max(hi, heights[s]);
					}
					float reach = Mathf.Sqrt(2f * step * step + (hi - lo) * (hi - lo));
					Vector3 mid = new Vector3(x0 + 0.5f * step, heights[4], z0 + 0.5f * step) - Origin;
					if (Smooth(mid) - Amp > reach + HoleMargin)
					{
						continue;
					}
					float nearest = float.MaxValue, furthest = 0f, furthestLow = 0f;
					for (int s = 0; s < 9; s++)
					{
						var p = new Vector3(x0 + (s % 3) * 0.5f * step, heights[s], z0 + (s / 3) * 0.5f * step) - Origin;
						nearest = Mathf.Min(nearest, Void(p));
						if (p.y - mouth.Floor.y < ApronBand)
						{
							furthestLow = Mathf.Max(furthestLow, Smooth(p));
						}
						else
						{
							furthest = Mathf.Max(furthest, Smooth(p));
						}
					}
					if (nearest < HoleMargin)
					{
						cells.Add(new Vector2Int(i, k));
						needed = Mathf.Max(needed, furthest);
						neededLow = Mathf.Max(neededLow, furthestLow);
					}
				}
			}
			float h = MeshCell;
			mouth.Collar = Mathf.Max(needed + 0.4f, 3f * h);
			mouth.CollarLow = Mathf.Max(neededLow + 0.4f, 3f * h);
			mouth.Shell = Mathf.Max(mouth.Collar, mouth.CollarLow) + NoiseShare * Radius + 2f * h;
			Mouths[mouthIndex] = mouth;
			holeMaps[mouthIndex] = cells.Count > 0 ? HoleMap.Of(cells, step, gridX, gridZ, Origin, HoleMapMargin) : null;
			ShellDeep = Mathf.Max(3f * h, 1.2f) + NoiseShare * Radius;
			// The buckets' reach grows with the shell.
			Prepare();
			return cells;
		}

		/// <summary>
		/// Takes the terrain actually cut away round a mouth (<see cref="PointOfInterestTerrain.HolesIn"/>, as hole-grid
		/// cells) for its collar to follow: a system rebuilt from its stored values has lost the cells its plan cut. Only cells
		/// this system's bore could have opened are kept (a canyon wall's or another cave's holes nearby are not its own).
		/// </summary>
		public void UseCutHoles(int mouthIndex, List<Vector2> holeCentres, Func<float, float, float> groundWorld, float step, float gridX, float gridZ)
		{
			if (segA == null)
			{
				Prepare();
			}
			// The test HoleCells applies before it cuts a cell (its surface within reach of the bore), so every cell the plan
			// cut is kept, and a canyon wall's or another cave's further off are not.
			var cells = new List<Vector2Int>();
			var heights = new float[9];
			foreach (Vector2 c in holeCentres)
			{
				int i = Mathf.FloorToInt((c.x - gridX) / step), k = Mathf.FloorToInt((c.y - gridZ) / step);
				float x0 = gridX + i * step, z0 = gridZ + k * step;
				float lo = float.MaxValue, hi = float.MinValue;
				for (int s = 0; s < 9; s++)
				{
					heights[s] = groundWorld(x0 + (s % 3) * 0.5f * step, z0 + (s / 3) * 0.5f * step);
					lo = Mathf.Min(lo, heights[s]);
					hi = Mathf.Max(hi, heights[s]);
				}
				float reach = Mathf.Sqrt(2f * step * step + (hi - lo) * (hi - lo));
				Vector3 mid = new Vector3(x0 + 0.5f * step, heights[4], z0 + 0.5f * step) - Origin;
				if (Smooth(mid) - Amp <= reach + HoleMargin)
				{
					cells.Add(new Vector2Int(i, k));
				}
			}
			holeMaps[mouthIndex] = cells.Count > 0 ? HoleMap.Of(cells, step, gridX, gridZ, Origin, HoleMapMargin) : null;
		}

		/// <summary>How far past a mouth's cut its hole map reaches, metres: the collar's margin and wander and a little more.</summary>
		private const float HoleMapMargin = CollarMargin + CollarWander + 1f;
		/// <summary>The least the collar runs past the edge of the terrain cut away at its mouth, metres.</summary>
		public const float CollarMargin = 0.45f;
		/// <summary>How deep under where the ground was the rock fills under a mouth's cut, metres (its underside is buried).</summary>
		public const float CutSlab = 2.5f;

		private readonly Dictionary<int, HoleMap> holeMaps = new Dictionary<int, HoleMap>();

		/// <summary>
		/// Writes the state the field reads beyond what <see cref="ToValues"/> stores: the cut each mouth's collar follows
		/// (<see cref="HoleCells"/>, <see cref="UseCutHoles"/>), as the field sees it. CaveShaper keys a cave's meshes on this
		/// with its values and its ground, and keeps them while the key is unchanged, so any state the field comes to read
		/// that the stored values do not carry must be written here too, or a changed cave keeps its old rock.
		/// </summary>
		public void WriteDerivedState(System.IO.BinaryWriter writer)
		{
			var mouths = new List<int>(holeMaps.Keys);
			mouths.Sort();
			writer.Write(mouths.Count);
			foreach (int m in mouths)
			{
				writer.Write(m);
				HoleMap map = holeMaps[m];
				writer.Write(map != null);
				map?.Write(writer);
			}
		}

		/// <summary>
		/// The terrain cut away at one mouth as a signed distance in plan (negative inside the cut, metres; the system's frame),
		/// on a fine grid: so the collar can follow the cut's own outline (2026-10-12: "We have the exact cave entrance
		/// dimensions 'paint hole?', make it look interesting without leaving gaps").
		/// </summary>
		private sealed class HoleMap
		{
			private const float Cell = 0.25f;
			private float x0, z0;
			private int w, h;
			private float[] distance;

			public static HoleMap Of(List<Vector2Int> cells, float step, float gridX, float gridZ, Vector3 origin, float margin)
			{
				float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
				foreach (Vector2Int c in cells)
				{
					float ax = gridX + c.x * step - origin.x, az = gridZ + c.y * step - origin.z;
					minX = Mathf.Min(minX, ax);
					minZ = Mathf.Min(minZ, az);
					maxX = Mathf.Max(maxX, ax + step);
					maxZ = Mathf.Max(maxZ, az + step);
				}
				var map = new HoleMap { x0 = minX - margin, z0 = minZ - margin };
				map.w = Mathf.CeilToInt((maxX - minX + 2f * margin) / Cell) + 1;
				map.h = Mathf.CeilToInt((maxZ - minZ + 2f * margin) / Cell) + 1;
				int n = map.w * map.h;
				var inside = new bool[n];
				foreach (Vector2Int c in cells)
				{
					float ax = gridX + c.x * step - origin.x, az = gridZ + c.y * step - origin.z;
					int i0 = Mathf.FloorToInt((ax - map.x0) / Cell), i1 = Mathf.CeilToInt((ax + step - map.x0) / Cell);
					int k0 = Mathf.FloorToInt((az - map.z0) / Cell), k1 = Mathf.CeilToInt((az + step - map.z0) / Cell);
					for (int k = Mathf.Max(0, k0); k < Mathf.Min(map.h, k1); k++)
					{
						for (int i = Mathf.Max(0, i0); i < Mathf.Min(map.w, i1); i++)
						{
							inside[k * map.w + i] = true;
						}
					}
				}
				// Distance to the nearest sample on the other side, both ways (a chamfer pass is plenty at this cell size).
				float[] outside = Chamfer(inside, map.w, map.h, true);
				float[] within = Chamfer(inside, map.w, map.h, false);
				map.distance = new float[n];
				for (int i = 0; i < n; i++)
				{
					map.distance[i] = inside[i] ? -(within[i] - 0.5f) * Cell : (outside[i] - 0.5f) * Cell;
				}
				return map;
			}

			/// <summary>Cells to the nearest cell of the other kind (<paramref name="toInside"/>: to the nearest inside cell).</summary>
			private static float[] Chamfer(bool[] inside, int w, int h, bool toInside)
			{
				const float a = 1f, b = 1.4142f;
				var d = new float[w * h];
				for (int i = 0; i < d.Length; i++)
				{
					d[i] = inside[i] == toInside ? 0f : 1e6f;
				}
				for (int k = 0; k < h; k++)
				{
					for (int i = 0; i < w; i++)
					{
						int j = k * w + i;
						float v = d[j];
						if (i > 0) v = Mathf.Min(v, d[j - 1] + a);
						if (k > 0)
						{
							v = Mathf.Min(v, d[j - w] + a);
							if (i > 0) v = Mathf.Min(v, d[j - w - 1] + b);
							if (i < w - 1) v = Mathf.Min(v, d[j - w + 1] + b);
						}
						d[j] = v;
					}
				}
				for (int k = h - 1; k >= 0; k--)
				{
					for (int i = w - 1; i >= 0; i--)
					{
						int j = k * w + i;
						float v = d[j];
						if (i < w - 1) v = Mathf.Min(v, d[j + 1] + a);
						if (k < h - 1)
						{
							v = Mathf.Min(v, d[j + w] + a);
							if (i < w - 1) v = Mathf.Min(v, d[j + w + 1] + b);
							if (i > 0) v = Mathf.Min(v, d[j + w - 1] + b);
						}
						d[j] = v;
					}
				}
				return d;
			}

			/// <summary>Everything <see cref="At"/> reads.</summary>
			public void Write(System.IO.BinaryWriter writer)
			{
				writer.Write(x0);
				writer.Write(z0);
				writer.Write(w);
				writer.Write(h);
				writer.Write(distance.Length);
				foreach (float d in distance)
				{
					writer.Write(d);
				}
			}

			/// <summary>The signed distance in plan from the cut's edge (negative inside), bilinear; far outside the map, large.</summary>
			public float At(float x, float z)
			{
				float fx = (x - x0) / Cell, fz = (z - z0) / Cell;
				if (fx < 0f || fz < 0f || fx > w - 1 || fz > h - 1)
				{
					return 1e3f;
				}
				int i = Mathf.Min(w - 2, (int)fx), k = Mathf.Min(h - 2, (int)fz);
				float u = fx - i, v = fz - k;
				float a = distance[k * w + i], b = distance[k * w + i + 1], c = distance[(k + 1) * w + i], d = distance[(k + 1) * w + i + 1];
				return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
			}
		}

		// ── The field ─────────────────────────────────────────────

		private Vector3[] segA, segAB;
		private float[] segLen2, segLen;
		private CaveNode[] segNodeA, segNodeB;
		private Vector3[] chamberX, chamberZ;
		private Dictionary<long, List<int>> buckets;
		private float blend, floorBlend, edgeBlend;

		/// <summary>The rock noise's amplitude, metres.</summary>
		public float Amp => NoiseShare * Radius;

		/// <summary>
		/// Builds the segment tables and the 16 m buckets the field reads; call after the passages or chambers change. Fills
		/// every table the field reads, here and never lazily: the mesher reads the field from several threads.
		/// </summary>
		/// <remarks>
		/// The mouths' outcrops (a mound of coursed blocks the opening was set in) and notches (a slot cut out over the apron,
		/// rock walls either side of it) are gone (2026-10-18: "Caves entrances need to remove the rocky border and allow a
		/// smoother transition into the cave itself"): the hillside runs into the opening itself, and the ground before it is
		/// graded to the mouth's floor (<see cref="Approach"/>).
		/// </remarks>
		public void Prepare() => PrepareField();

		/// <summary>The segment tables and buckets: what the planner reads between its steps, and the finished field.</summary>
		private void PrepareField()
		{
			var a = new List<Vector3>();
			var ab = new List<Vector3>();
			var na = new List<CaveNode>();
			var nb = new List<CaveNode>();
			foreach (CavePassage p in Passages)
			{
				for (int i = 0; i + 1 < p.Nodes.Count; i++)
				{
					a.Add(p.Nodes[i].Centre);
					ab.Add(p.Nodes[i + 1].Centre - p.Nodes[i].Centre);
					na.Add(p.Nodes[i]);
					nb.Add(p.Nodes[i + 1]);
				}
			}
			segA = a.ToArray();
			segAB = ab.ToArray();
			segNodeA = na.ToArray();
			segNodeB = nb.ToArray();
			segLen2 = new float[segA.Length];
			segLen = new float[segA.Length];
			for (int i = 0; i < segA.Length; i++)
			{
				segLen2[i] = Mathf.Max(1e-6f, segAB[i].sqrMagnitude);
				segLen[i] = Mathf.Sqrt(segLen2[i]);
			}
			chamberX = new Vector3[Chambers.Count];
			chamberZ = new Vector3[Chambers.Count];
			for (int c = 0; c < Chambers.Count; c++)
			{
				float cy = Chambers[c].Yaw * Mathf.Deg2Rad;
				chamberZ[c] = new Vector3(Mathf.Sin(cy), 0f, Mathf.Cos(cy));
				chamberX[c] = new Vector3(chamberZ[c].z, 0f, -chamberZ[c].x);
			}
			blend = Mathf.Max(2.5f * MeshCell, 0.3f * Radius);
			floorBlend = Mathf.Max(2.5f * MeshCell, 0.25f * Radius);
			edgeBlend = 2.5f * MeshCell;
			// Each primitive in every bucket its reach (radius, noise, shell, blends) touches. Chambers are coded −1 − index.
			float pad = Amp * MaxRough + Mathf.Max(ShellDeep, MaxMouthShell) + 2f * blend + 2f * MeshCell + MouthReach;
			buckets = new Dictionary<long, List<int>>();
			for (int s = 0; s < segA.Length; s++)
			{
				float r = Mathf.Max(segNodeA[s].Radius, segNodeB[s].Radius) + pad;
				Vector3 lo = Vector3.Min(segA[s], segA[s] + segAB[s]) - Vector3.one * r;
				Vector3 hi = Vector3.Max(segA[s], segA[s] + segAB[s]) + Vector3.one * r;
				AddToBuckets(lo, hi, s);
			}
			for (int c = 0; c < Chambers.Count; c++)
			{
				float r = Mathf.Max(Chambers[c].Radii.x, Mathf.Max(Chambers[c].Radii.y, Chambers[c].Radii.z)) + pad;
				AddToBuckets(Chambers[c].Centre - Vector3.one * r, Chambers[c].Centre + Vector3.one * r, -1 - c);
			}
			AddFeaturesToBuckets(pad);
		}

		/// <summary>How much further a mouth's rock reaches than its passage's: the arch's spread and the collar's wander.</summary>
		private float MouthReach => ArchSpread * (Radius + MaxMouthShell) + CollarWander;

		private float MaxMouthShell
		{
			get
			{
				float m = 0f;
				foreach (CaveMouth mouth in Mouths)
				{
					m = Mathf.Max(m, mouth.Shell);
				}
				return m;
			}
		}

		private static long BucketKey(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

		private void AddToBuckets(Vector3 lo, Vector3 hi, int id)
		{
			int x0 = Mathf.FloorToInt(lo.x / Bucket), x1 = Mathf.FloorToInt(hi.x / Bucket);
			int y0 = Mathf.FloorToInt(lo.y / Bucket), y1 = Mathf.FloorToInt(hi.y / Bucket);
			int z0 = Mathf.FloorToInt(lo.z / Bucket), z1 = Mathf.FloorToInt(hi.z / Bucket);
			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					for (int z = z0; z <= z1; z++)
					{
						long key = BucketKey(x, y, z);
						if (!buckets.TryGetValue(key, out List<int> list))
						{
							buckets[key] = list = new List<int>();
						}
						list.Add(id);
					}
				}
			}
		}

		/// <summary>Whether any primitive's reach touches the bucket at a point: the mesher skips chunks with none.</summary>
		public bool Near(Vector3 p) => buckets != null && buckets.ContainsKey(BucketKey(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.y / Bucket), Mathf.FloorToInt(p.z / Bucket)));

		/// <summary>Whether any primitive's reach touches a box: the mesher samples only the chunks this is true of.</summary>
		public bool NearBox(Vector3 lo, Vector3 hi)
		{
			if (buckets == null)
			{
				Prepare();
			}
			int x0 = Mathf.FloorToInt(lo.x / Bucket), x1 = Mathf.FloorToInt(hi.x / Bucket);
			int y0 = Mathf.FloorToInt(lo.y / Bucket), y1 = Mathf.FloorToInt(hi.y / Bucket);
			int z0 = Mathf.FloorToInt(lo.z / Bucket), z1 = Mathf.FloorToInt(hi.z / Bucket);
			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					for (int z = z0; z <= z1; z++)
					{
						if (buckets.ContainsKey(BucketKey(x, y, z)))
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		/// <summary>The bare void (no noise), negative inside; and the nearest primitive's floor and radius.</summary>
		private float Tube(Vector3 p, out float radius, out float floor) => Tube(p, out radius, out floor, out _);

		/// <summary>As above, with the nearest primitive's roughness (<see cref="CaveChamber.Rough"/>).</summary>
		private float Tube(Vector3 p, out float radius, out float floor, out float rough)
		{
			radius = Radius;
			floor = 0f;
			rough = 1f;
			if (buckets == null)
			{
				Prepare();
			}
			p = ArchAt(p);
			if (!buckets.TryGetValue(BucketKey(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.y / Bucket), Mathf.FloorToInt(p.z / Bucket)), out List<int> ids))
			{
				return 1e4f;
			}
			float d = float.MaxValue, best = float.MaxValue;
			if (primBuffer == null || primBuffer.Length < ids.Count)
			{
				primBuffer = new float[Mathf.Max(64, ids.Count * 2)];
				floorBuffer = new float[primBuffer.Length];
			}
			int n = 0;
			float invFlat = 1f / Mathf.Max(0.2f, Flat);
			foreach (int id in ids)
			{
				float prim;
				float primRadius, primFloor, primRough = 1f;
				if (id <= SolidId)
				{
					// Solids are put back after the union (SmoothWithSolids, Void).
					continue;
				}
				if (id <= ShaftId)
				{
					CaveShaft shaft = Shafts[ShaftId - id];
					prim = ShaftField(shaft, p);
					primRadius = shaft.Radius;
					primFloor = shaft.Bottom.y;
				}
				else if (id <= RiftId)
				{
					CaveRift rift = Rifts[RiftId - id];
					prim = RiftField(rift, p, out primFloor);
					primRadius = 2f * rift.Width;
				}
				else if (id >= 0)
				{
					Vector3 ap = p - segA[id];
					float t = Mathf.Clamp01(Vector3.Dot(ap, segAB[id]) / segLen2[id]);
					Vector3 q = ap - segAB[id] * t;
					q.y *= invFlat / (segNodeA[id].Height + (segNodeB[id].Height - segNodeA[id].Height) * t);
					primRadius = segNodeA[id].Radius + (segNodeB[id].Radius - segNodeA[id].Radius) * t;
					primFloor = segNodeA[id].Floor + (segNodeB[id].Floor - segNodeA[id].Floor) * t;
					prim = SMax(q.magnitude - primRadius, primFloor - p.y, floorBlend);
				}
				else
				{
					int c = -1 - id;
					CaveChamber ch = Chambers[c];
					Vector3 o = p - ch.Centre;
					float along = Vector3.Dot(o, chamberZ[c]);
					// Sheared along its heading by its slope: the floor a plane through the passage's, the vault over it.
					var e = new Vector3(along, o.y - ch.Slope * along, Vector3.Dot(o, chamberX[c]));
					var r1 = new Vector3(e.x / ch.Radii.x, e.y / ch.Radii.y, e.z / ch.Radii.z);
					var r2 = new Vector3(r1.x / ch.Radii.x, r1.y / ch.Radii.y, r1.z / ch.Radii.z);
					float k0 = r1.magnitude, k1 = Mathf.Max(1e-6f, r2.magnitude);
					primFloor = ch.Floor + ch.Slope * along;
					prim = SMax(k0 * (k0 - 1f) / k1, primFloor - p.y, floorBlend);
					primRadius = Mathf.Max(ch.Radii.x, ch.Radii.z);
					primRough = ch.Rough > 0f ? ch.Rough : 1f;
				}
				if (prim < best)
				{
					best = prim;
					radius = primRadius;
					rough = primRough;
				}
				primBuffer[n] = prim;
				floorBuffer[n] = primFloor;
				n++;
				// A shaft or a chasm meets the floor it opens through at a crisp lip: a rounded one let a climb's top down a
				// metre short of the floor it steps off onto.
				float k = id <= RiftId ? LipBlend : id >= 0 ? blend : 1.5f * blend;
				d = d == float.MaxValue ? prim : SMin(d, prim, k);
			}
			if (d == float.MaxValue)
			{
				return 1e4f;
			}
			// The floor the wall noise is kept off: the highest of those shaping this point, so where a passage's segments or
			// a chamber meet, the noise never carves under the floor a person walks on.
			floor = float.MinValue;
			for (int i = 0; i < n; i++)
			{
				if (primBuffer[i] <= best + 1.5f * blend && floorBuffer[i] > floor)
				{
					floor = floorBuffer[i];
				}
			}
			return d;
		}

		[ThreadStatic] private static float[] primBuffer, floorBuffer;

		// ── The mouth's arch ─────────────────────────────────────

		/// <summary>How much wider than the bore a mouth opens at its floor (1 + this), narrowing to the bore at its roof.</summary>
		public const float ArchSpread = 0.45f;

		/// <summary>
		/// How strongly a point is shaped by the nearest mouth (1 at and outside it, fading to 0 a bore's width past its
		/// zone), that mouth, and the point's offset across it (along the mouth's face, metres; signed).
		/// </summary>
		private float MouthWeight(Vector3 p, out CaveMouth mouth, out float across)
		{
			mouth = default;
			across = 0f;
			float best = 0f;
			for (int m = 0; m < Mouths.Count; m++)
			{
				CaveMouth candidate = Mouths[m];
				float yaw = candidate.Yaw * Mathf.Deg2Rad;
				var outward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
				Vector3 d = p - candidate.Floor;
				float inward = -(d.x * outward.x + d.z * outward.z);
				float reach = candidate.Zone + 2f * candidate.Radius;
				if (inward > reach || d.sqrMagnitude > (reach + 4f * candidate.Radius) * (reach + 4f * candidate.Radius))
				{
					continue;
				}
				float w = 1f - Mathf.Clamp01((inward - candidate.Zone * 0.5f) / Mathf.Max(1f, reach - candidate.Zone * 0.5f));
				w = w * w * (3f - 2f * w);
				if (w > best)
				{
					best = w;
					mouth = candidate;
					across = d.x * outward.z - d.z * outward.x;
				}
			}
			return best;
		}

		/// <summary>
		/// The point the bore's field is read at near a mouth: drawn in across the mouth's face, most at its floor and none
		/// at its roof, so the opening is a low arch, broad at its foot, rather than the round bore of the passage behind it
		/// (2026-10-12: "Cave entrances are a bit boring, just round rocky holes"). Height is untouched, so floors and
		/// headroom are the passage's own; only void is added. Everything that reads the field (the holes, the collar, the
		/// mesher, the dressing) reads it through here, so they agree.
		/// </summary>
		private Vector3 ArchAt(Vector3 p)
		{
			if (Mouths.Count == 0)
			{
				return p;
			}
			float w = MouthWeight(p, out CaveMouth mouth, out float across);
			if (w <= 0f)
			{
				return p;
			}
			float h = Mathf.Clamp01((p.y - mouth.Floor.y) / Mathf.Max(0.5f, 2f * mouth.Radius * Mathf.Max(0.2f, Flat)));
			// A low-frequency wander on the spread, so the two sides of the arch are not mirror images.
			float wander = 0.75f + 0.5f * ProceduralNoise.Fbm3(p / (2.2f * Radius), 1, 0.5f, Seed + 41) ;
			float spread = 1f + ArchSpread * w * (1f - h) * (1f - h) * Mathf.Clamp(wander, 0.4f, 1.3f);
			float yaw = mouth.Yaw * Mathf.Deg2Rad;
			var side = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
			return p - side * (across * (1f - 1f / spread));
		}

		/// <summary>The floor the void's nearest passages and chambers have under a point (system frame).</summary>
		public float FloorNear(Vector3 p)
		{
			SmoothWithSolids(p, out _, out float floor, out _);
			return floor;
		}

		/// <summary>The void without noise, the solids put back.</summary>
		public float Smooth(Vector3 p) => SmoothWithSolids(p, out _, out _, out _);

		/// <summary>The void with rock noise on walls and roofs and a near-flat floor, negative inside.</summary>
		public float Void(Vector3 p) => Void(p, out _);

		private float Void(Vector3 p, out float smooth)
		{
			float tube = Tube(p, out float radius, out float floor, out float rough);
			smooth = tube;
			if (tube > 1e3f)
			{
				return tube;
			}
			float solid = Solid(p, out float top);
			if (solid < 1e3f)
			{
				smooth = Mathf.Max(tube, -solid);
				if (top != float.MaxValue && p.y >= top - 0.2f && solid < 2f)
				{
					floor = Mathf.Max(floor, top);
				}
			}
			float w = Mathf.Clamp01((p.y - floor) / Mathf.Max(0.5f, 0.5f * radius * Flat));
			w = w * w * (3f - 2f * w);
			float noise = ProceduralNoise.Fbm3(p / (0.9f * Radius), 2, 0.5f, Seed + 17);
			// Round the opening the rock shows its bedding instead of the bore's knobs: layers each jutting at its top and
			// undercut at its foot, wavering, so the mouth has a brow and ledges rather than a smooth dome. Bounded by the
			// same amplitude as the knobs, so the holes, the collar and the buckets that allow for it still hold.
			float mouth = MouthWeight(p, out _, out _);
			float amp = Amp * rough * (1f - Quiet(p));
			if (mouth > 0f)
			{
				// On the walls only, fading out up toward the roof: the roof over a mouth is thin by design (its zone), and
				// ledges cut up into it broke through to the hillside as a skylight beside the opening.
				float up = Mathf.Clamp01((p.y - floor) / Mathf.Max(0.5f, 1.6f * radius * Flat));
				float walls = mouth * (1f - up) * (1f - up);
				// Fractured rock, not a smooth bore: each cell of the rock its own tilted plane (Voronoi facets), stepping where
				// one block meets the next — the blocky slabs and ledges of a real cave mouth's face.
				float facets = Facets(p);
				noise = Mathf.Lerp(noise, 0.25f * noise + 0.75f * facets, walls);
				amp *= 1f - 0.5f * mouth * up;
			}
			float v = tube - amp * w * noise - 0.06f * (1f - w) * ProceduralNoise.Gradient3(p / 1.3f, Seed + 23);
			if (solid < 1e3f)
			{
				// The rock put back keeps its walking top exact; under it, its sides are as rough as the walls round it.
				float below = top == float.MaxValue ? 1f : Mathf.Clamp01((top - p.y) / 1.5f);
				v = Mathf.Max(v, -(solid - 0.4f * Amp * rough * below * noise));
			}
			return v;
		}

		/// <summary>
		/// Faceted rock in [-1, 1]: the signed distance of a point from the fracture plane of the nearest of a jittered grid of
		/// blocks (each block a random plane through a random point in it), so the surface it displaces is made of flat,
		/// tilted faces that step where blocks meet.
		/// </summary>
		private float Facets(Vector3 p)
		{
			float size = Mathf.Max(1.6f, 0.5f * Radius);
			Vector3 g = p / size;
			int gx = Mathf.FloorToInt(g.x), gy = Mathf.FloorToInt(g.y), gz = Mathf.FloorToInt(g.z);
			// The nearest two blocks' planes, blended across the boundary between them: a field that jumps where one block
			// meets the next gives the mesher ambiguous cells (pinched, non-manifold edges); a quarter-cell blend keeps the step
			// a step to the eye.
			float nearest = float.MaxValue, second = float.MaxValue, value = 0f, secondValue = 0f;
			for (int z = -1; z <= 1; z++)
			{
				for (int y = -1; y <= 1; y++)
				{
					for (int x = -1; x <= 1; x++)
					{
						int cx = gx + x, cy = gy + y, cz = gz + z;
						uint h = (uint)(cx * 73856093) ^ (uint)(cy * 19349663) ^ (uint)(cz * 83492791) ^ (uint)(Seed * 2654435761u);
						var site = new Vector3(cx + Unit(h, 1), cy + Unit(h, 2), cz + Unit(h, 3));
						float d = (g - site).sqrMagnitude;
						if (d >= second)
						{
							continue;
						}
						// A fracture plane: steep more often than flat, as rock splits along its joints.
						var n = new Vector3(Unit(h, 4) * 2f - 1f, (Unit(h, 5) * 2f - 1f) * 0.6f, Unit(h, 6) * 2f - 1f);
						n = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.up;
						float v = Mathf.Clamp(Vector3.Dot(g - site, n) * 1.6f, -1f, 1f);
						if (d < nearest)
						{
							second = nearest;
							secondValue = value;
							nearest = d;
							value = v;
						}
						else
						{
							second = d;
							secondValue = v;
						}
					}
				}
			}
			float gap = Mathf.Sqrt(second) - Mathf.Sqrt(nearest);
			float t = Mathf.Clamp01(gap / 0.25f);
			return Mathf.Lerp(0.5f * (value + secondValue), value, t * t * (3f - 2f * t));
		}

		private static float Unit(uint h, uint salt)
		{
			h ^= salt * 0x9E3779B9u;
			h ^= h >> 16;
			h *= 0x7FEB352Du;
			h ^= h >> 15;
			h *= 0x846CA68Bu;
			h ^= h >> 16;
			return (h & 0xFFFFFF) / (float)0x1000000;
		}

		/// <summary>The nearest mouth and how far it is (the shell is thick and rises to a rim near one).</summary>
		private int NearestMouth(Vector3 p, out float distance)
		{
			int best = 0;
			distance = float.MaxValue;
			for (int m = 0; m < Mouths.Count; m++)
			{
				float d = (p - Mouths[m].Floor).magnitude;
				if (d < distance)
				{
					distance = d;
					best = m;
				}
			}
			return best;
		}

		/// <summary>The rock shell, negative inside the rock (see CaveSolid.Field): thick near a mouth, rising to its rim there.</summary>
		public float Field(Vector3 p, CaveGround ground)
		{
			float v = Void(p, out float smooth);
			if (smooth > 1e3f)
			{
				return 1e3f;
			}
			int mouthIndex = NearestMouth(p, out float fromMouth);
			CaveMouth mouth = Mouths[mouthIndex];
			float t = Mathf.Clamp01((fromMouth - mouth.Zone) / Mathf.Max(1f, 2f * mouth.Radius));
			t = t * t * (3f - 2f * t);
			float thickness = mouth.Shell + (ShellDeep - mouth.Shell) * t;
			float g = ground.At(p.x + Origin.x, p.z + Origin.z) - Origin.y;
			float buried = p.y - (g - Skin);
			// The terrain actually cut away at this mouth (HoleCells), where it is known: the collar follows it in plan, a
			// ragged margin past the cut's edge, and the shell fills a slab under all of it, however far the cut runs up a
			// steep face from the mouth (past where the shell's own thickness reaches, the cut showed under the hillside).
			bool mapped = holeMaps.TryGetValue(mouthIndex, out HoleMap holes) && holes != null;
			float wander = CollarWander * Mathf.Clamp01(ProceduralNoise.Fbm3(p / (1.6f * Radius), 2, 0.5f, Seed + 53) + 0.15f);
			float cut = mapped ? holes.At(p.x, p.z) - (CollarMargin + wander) : float.MaxValue;
			float shell = Mathf.Max(-v, Mathf.Min(smooth - thickness, Mathf.Max(cut, (g - CutSlab) - p.y)));
			// Within a mouth's zone (or over its cut) the shell may stand its rim over the ground; elsewhere it stays buried.
			float collar = float.MaxValue;
			float k = edgeBlend;
			if (fromMouth < mouth.Zone + 4f * mouth.Radius || cut < 1f)
			{
				float b = Mathf.Clamp01((p.y - mouth.Floor.y - (ApronBand - 2f)) / 2f);
				float width = mouth.CollarLow + (mouth.Collar - mouth.CollarLow) * (b * b * (3f - 2f * b));
				// Its top stands the full rim over the ground only at the opening's lip, and lies all but flush with it beyond,
				// where the rock's ground contact (CaveVisibility's cave mode) draws it as the ground: a lip of bedrock in the
				// hillside, not a stone plate on it. Its outline wanders out past what the holes need (never in: every hole
				// stays covered), so it is not an ellipse.
				float lip = Mathf.Clamp01(smooth / LipBand);
				float rim = Mathf.Lerp(Rim, FlushRim, lip * lip * (3f - 2f * lip));
				// Flush wherever the ground stands at the mouth's floor (2026-10-18: "allow a smoother transition into the
				// cave itself"): that is where the way in crosses from the terrain onto the rock, and a lip there was a step to
				// climb at the threshold. The lip stands only up the opening's sides and over its brow.
				// An awash mouth is waded into at its water, not at its floor under it.
				float walkLevel = float.IsNaN(WaterLevel) ? mouth.Floor.y : Mathf.Max(mouth.Floor.y, WaterLevel - Origin.y);
				float level = Mathf.Clamp01((g - walkLevel - WalkFlushHeight) / WalkFlushBand);
				level = level * level * (3f - 2f * level);
				rim = Mathf.Lerp(FlushRim, rim, level);
				// And joined crisply there: the soft join rounded the edge where the collar's top meets the floor's end, and
				// ate a dip half a metre deep into the threshold (the two meet nearly level, so the rounding reached far).
				k = Mathf.Lerp(WalkBlend * MeshCell, edgeBlend, level);
				float reach = mapped ? cut : smooth - (width + wander);
				collar = Mathf.Max(reach, p.y - (g + rim) + ClipLift(rim, k));
			}
			float clip = collar == float.MaxValue ? buried : SMin(buried, collar, k);
			return SMax(shell, clip, k);
		}

		/// <summary>The collar's join to the shell and the ground where the way in crosses it, a share of the mesher's cell.</summary>
		private const float WalkBlend = 0.4f;

		/// <summary>The ground's height over the mouth's floor under which the collar lies flush (the way in), metres, and the band it rises to its lip over.</summary>
		private const float WalkFlushHeight = 0.8f, WalkFlushBand = 1.2f;

		/// <summary>
		/// How much the soft union of the collar with the buried shell under it lifts the collar's top (metres), for a collar
		/// whose top stands <paramref name="rim"/> over the ground, joined over <paramref name="k"/>: the two are level planes <c>rim + Skin</c> apart, and a
		/// smooth minimum of two such planes lies under both by a constant. Taken off the collar beforehand, so its top stands
		/// at its rim and not a fifth of a metre over it (a step at the threshold with the large caves' coarser cells).
		/// </summary>
		private float ClipLift(float rim, float k)
		{
			float lift = 0f;
			// A fixed point: lifting the collar narrows the gap the lift is reckoned from (converged in two steps to < 1 mm).
			for (int i = 0; i < 3; i++)
			{
				float h = Mathf.Max(k - (rim + Skin - lift), 0f) / k;
				lift = h * h * k * 0.25f;
			}
			return lift;
		}

		public static float SMin(float a, float b, float k)
		{
			float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
			return Mathf.Min(a, b) - h * h * k * 0.25f;
		}

		public static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

		/// <summary>The box everything lies in, grown by the shell and a margin, in the system's frame.</summary>
		public Bounds Extent
		{
			get
			{
				var b = new Bounds(Mouths.Count > 0 ? Mouths[0].Floor : Vector3.zero, Vector3.zero);
				float grow = Mathf.Max(ShellDeep, MaxMouthShell) + Amp * MaxRough + 3f * MeshCell + MouthReach;
				foreach (CavePassage p in Passages)
				{
					foreach (CaveNode n in p.Nodes)
					{
						b.Encapsulate(n.Centre + Vector3.one * (n.Radius + grow));
						b.Encapsulate(n.Centre - Vector3.one * (n.Radius + grow));
					}
				}
				foreach (CaveChamber c in Chambers)
				{
					float r = Mathf.Max(c.Radii.x, Mathf.Max(c.Radii.y, c.Radii.z)) + grow;
					b.Encapsulate(c.Centre + Vector3.one * r);
					b.Encapsulate(c.Centre - Vector3.one * r);
				}
				EncapsulateFeatures(ref b, grow);
				return b;
			}
		}

		/// <summary>Total passage length, metres.</summary>
		public float PassageLength
		{
			get
			{
				float l = 0f;
				foreach (CavePassage p in Passages)
				{
					if (p.Nodes.Count > 0)
					{
						l += p.Nodes[p.Nodes.Count - 1].Along;
					}
				}
				return l;
			}
		}

		/// <summary>How deep under its first mouth the system goes, metres.</summary>
		public float Depth
		{
			get
			{
				float low = 0f;
				foreach (CavePassage p in Passages)
				{
					foreach (CaveNode n in p.Nodes)
					{
						low = Mathf.Min(low, n.Floor);
					}
				}
				return -low;
			}
		}

		/// <summary>A site's own chamber: its deepest (where its camp, its den or its gathering is); false when it has none.</summary>
		public bool TryChamberOf(int siteId, out CaveChamber chamber)
		{
			chamber = default;
			bool found = false;
			foreach (CaveChamber c in Chambers)
			{
				if (c.SiteId == siteId && (!found || c.Kind == CaveChamberKind.Cavern && chamber.Kind != CaveChamberKind.Cavern || c.Floor < chamber.Floor && chamber.Kind != CaveChamberKind.Cavern))
				{
					chamber = c;
					found = true;
				}
			}
			return found;
		}

		// ── Utility ───────────────────────────────────────────────

		/// <summary>A chamber's floor under a point (system frame): the plane through its floor along its heading.</summary>
		public static float FloorOf(in CaveChamber chamber, Vector3 p)
		{
			float r = chamber.Yaw * Mathf.Deg2Rad;
			return chamber.Floor + chamber.Slope * ((p.x - chamber.Centre.x) * Mathf.Sin(r) + (p.z - chamber.Centre.z) * Mathf.Cos(r));
		}

		private static float UphillHeading(CaveGround ground, float x, float z, float fallback)
		{
			float gx = ground.At(x + 2f, z) - ground.At(x - 2f, z), gz = ground.At(x, z + 2f) - ground.At(x, z - 2f);
			return gx * gx + gz * gz < 1e-6f ? fallback : Mathf.Repeat(Mathf.Atan2(gx, gz) * Mathf.Rad2Deg, 360f);
		}

		public static float DeltaAngle(float from, float to)
		{
			float d = Mathf.Repeat(to - from, 360f);
			return d > 180f ? d - 360f : d;
		}

		// ── Storage ───────────────────────────────────────────────

		/// <summary>
		/// A site id as two floats, its high and low sixteen bits: ids are 32-bit hashes, and a float holds an integer
		/// exactly only below 2^24 (808268731 came back 808268740, and no chamber was its site's).
		/// </summary>
		public static float IdHigh(int id) => (uint)id >> 16;
		public static float IdLow(int id) => (uint)id & 0xFFFFu;
		public static int IdOf(float high, float low) => (int)(((uint)Mathf.RoundToInt(high) << 16) | ((uint)Mathf.RoundToInt(low) & 0xFFFFu));

		/// <summary>Stored for "none" (no water, no floor limit): plain floats keep no NaN or infinity well.</summary>
		private const float NoWater = -1e6f;

		/// <summary>The values one stored mouth takes.</summary>
		private const int MouthValues = 16;

		/// <summary>The values before the first mouth: the header <see cref="ToValues"/> writes.</summary>
		private const int HeaderValues = 21;

		private static CaveMouth MouthAt(float[] v, int o)
			=> new CaveMouth
			{
				Floor = new Vector3(v[o], v[o + 1], v[o + 2]), Yaw = v[o + 3], SiteId = IdOf(v[o + 4], v[o + 5]), Radius = v[o + 6],
				Zone = v[o + 7], Collar = v[o + 8], CollarLow = v[o + 9], Shell = v[o + 10],
				Kind = (CaveMouthKind)Mathf.RoundToInt(v[o + 11]), Water = v[o + 12], Landing = new Vector3(v[o + 13], v[o + 14], v[o + 15]),
			};

		/// <summary>
		/// The mouths stored values hold, read without building the system (its field is costly to prepare): what the way
		/// planner asks of a cave. Empty when the values are not a system of this format.
		/// </summary>
		public static List<CaveMouth> MouthsFromValues(float[] v)
		{
			var mouths = new List<CaveMouth>();
			if (v == null || v.Length < HeaderValues || Mathf.RoundToInt(v[0]) != Format)
			{
				return mouths;
			}
			int count = Mathf.RoundToInt(v[10]);
			for (int i = 0, o = HeaderValues; i < count && o + MouthValues <= v.Length; i++, o += MouthValues)
			{
				mouths.Add(MouthAt(v, o));
			}
			return mouths;
		}

		/// <summary>
		/// Every mouth's floor point (system frame), zone and radius in stored values of this format or the two before it (4 and
		/// 5 stored a mouth in 11 values after the same header): what a plan cut in an older format holed, for Regenerate POIs to
		/// fill again before it plans afresh. Empty for values that are none of these.
		/// </summary>
		public static List<(Vector3 Floor, float Zone, float Radius)> StoredMouths(float[] v)
		{
			var mouths = new List<(Vector3, float, float)>();
			int format = v != null && v.Length >= HeaderValues ? Mathf.RoundToInt(v[0]) : 0;
			int stride = format == Format ? MouthValues : format == 4 || format == 5 ? 11 : 0;
			if (stride == 0 || Mathf.RoundToInt(v[1]) == -1)
			{
				return mouths;
			}
			int count = Mathf.RoundToInt(v[10]);
			for (int i = 0, o = HeaderValues; i < count && o + stride <= v.Length; i++, o += stride)
			{
				mouths.Add((new Vector3(v[o], v[o + 1], v[o + 2]), v[o + 7], v[o + 6]));
			}
			return mouths;
		}

		/// <summary>How far out from a mouth's floor point the way to it ends over the ground, metres: past its lane (Approach).</summary>
		public static float LaneEnd(in CaveMouth mouth) => 1.2f * mouth.Radius + 6f;

		/// <summary>
		/// Where the way to a mouth ends, in the system's frame: on the bank for a shelf behind a waterfall, out along its lane
		/// for one over the ground; false for one under water, which is dived to and has no way.
		/// </summary>
		public static bool LandingOf(in CaveMouth mouth, out Vector3 landing)
		{
			switch (mouth.Kind)
			{
				case CaveMouthKind.Submerged:
					landing = mouth.Floor;
					return false;
				case CaveMouthKind.Shelf:
					landing = mouth.Landing;
					return true;
				default:
				{
					float yr = mouth.Yaw * Mathf.Deg2Rad;
					landing = mouth.Floor + new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr)) * LaneEnd(mouth);
					return true;
				}
			}
		}

		/// <summary>Everything needed to build the same system again, as plain floats (a shape's values).</summary>
		public float[] ToValues()
		{
			var v = new List<float>
			{
				Format, (float)Form, SizeClass, Radius, Flat, NoiseShare, Rim, Skin, ShellDeep, MeshCell,
				Mouths.Count, Passages.Count, Chambers.Count,
				float.IsNaN(WaterLevel) ? NoWater : WaterLevel, float.IsNegativeInfinity(LowestFloor) ? NoWater : LowestFloor,
				(float)Character, Solids.Count, Rifts.Count, Shafts.Count, Crossings.Count, Climbs.Count,
			};
			foreach (CaveMouth m in Mouths)
			{
				v.AddRange(new[] { m.Floor.x, m.Floor.y, m.Floor.z, m.Yaw, IdHigh(m.SiteId), IdLow(m.SiteId), m.Radius, m.Zone, m.Collar, m.CollarLow, m.Shell,
					(float)m.Kind, m.Water, m.Landing.x, m.Landing.y, m.Landing.z });
			}
			foreach (CavePassage p in Passages)
			{
				v.Add((float)p.Kind);
				v.Add(p.Nodes.Count);
				foreach (CaveNode n in p.Nodes)
				{
					v.AddRange(new[] { n.Centre.x, n.Centre.y, n.Centre.z, n.Radius, n.Floor, n.Along, n.Tall });
				}
			}
			foreach (CaveChamber c in Chambers)
			{
				v.AddRange(new[] { c.Centre.x, c.Centre.y, c.Centre.z, c.Radii.x, c.Radii.y, c.Radii.z, c.Floor, c.Yaw, (float)c.Kind, IdHigh(c.SiteId), IdLow(c.SiteId), c.Slope, c.Rough });
			}
			foreach (CaveSolid o in Solids)
			{
				v.AddRange(new[] { (float)o.Kind, o.A.x, o.A.y, o.A.z, o.B.x, o.B.y, o.B.z, o.Width, o.Depth, o.Arch, o.Angle });
			}
			foreach (CaveRift r in Rifts)
			{
				v.AddRange(new[] { r.A.x, r.A.y, r.A.z, r.B.x, r.B.y, r.B.z, r.Top, r.Width, r.Narrow });
			}
			foreach (CaveShaft h in Shafts)
			{
				v.AddRange(new[] { h.Bottom.x, h.Bottom.y, h.Bottom.z, h.Top, h.Radius, h.Square ? 1f : 0f, h.Yaw });
			}
			foreach (CaveCrossing c in Crossings)
			{
				v.AddRange(new[] { (float)c.Kind, c.A.x, c.A.y, c.A.z, c.B.x, c.B.y, c.B.z, c.HalfWidth });
			}
			foreach (CaveClimb c in Climbs)
			{
				v.AddRange(new[] { (float)c.Kind, c.Bottom.x, c.Bottom.y, c.Bottom.z, c.Top.x, c.Top.y, c.Top.z, c.Out.x, c.Out.y, c.Out.z, c.TopExit.x, c.TopExit.y, c.TopExit.z });
			}
			return v.ToArray();
		}

		/// <summary>The system stored by <see cref="ToValues"/>, its frame at <paramref name="origin"/>; null if they are not one.</summary>
		public static CaveSystem FromValues(float[] v, Vector3 origin, int seed)
		{
			if (v == null || v.Length < HeaderValues || Mathf.RoundToInt(v[0]) != Format)
			{
				return null;
			}
			try
			{
				var s = new CaveSystem
				{
					Form = (CaveForm)Mathf.RoundToInt(v[1]),
					SizeClass = (byte)Mathf.RoundToInt(v[2]),
					Radius = v[3],
					Flat = v[4],
					NoiseShare = v[5],
					Rim = v[6],
					Skin = v[7],
					ShellDeep = v[8],
					MeshCell = v[9],
					Seed = seed,
					Origin = origin,
				};
				int mouths = Mathf.RoundToInt(v[10]), passages = Mathf.RoundToInt(v[11]), chambers = Mathf.RoundToInt(v[12]);
				s.WaterLevel = v[13] <= NoWater ? float.NaN : v[13];
				s.LowestFloor = v[14] <= NoWater ? float.NegativeInfinity : v[14];
				s.Character = (CaveCharacter)Mathf.RoundToInt(v[15]);
				int solids = Mathf.RoundToInt(v[16]), rifts = Mathf.RoundToInt(v[17]), shafts = Mathf.RoundToInt(v[18]);
				int crossings = Mathf.RoundToInt(v[19]), climbs = Mathf.RoundToInt(v[20]);
				int o = HeaderValues;
				Vector3 V(int at) => new Vector3(v[at], v[at + 1], v[at + 2]);
				for (int i = 0; i < mouths; i++, o += MouthValues)
				{
					s.Mouths.Add(MouthAt(v, o));
				}
				for (int i = 0; i < passages; i++)
				{
					var p = new CavePassage { Kind = (CavePassageKind)Mathf.RoundToInt(v[o]) };
					int nodes = Mathf.RoundToInt(v[o + 1]);
					o += 2;
					for (int k = 0; k < nodes; k++, o += 7)
					{
						p.Nodes.Add(new CaveNode { Centre = V(o), Radius = v[o + 3], Floor = v[o + 4], Along = v[o + 5], Tall = v[o + 6] });
					}
					s.Passages.Add(p);
				}
				for (int i = 0; i < chambers; i++, o += 13)
				{
					s.Chambers.Add(new CaveChamber
					{
						Centre = V(o), Radii = V(o + 3), Floor = v[o + 6], Yaw = v[o + 7],
						Kind = (CaveChamberKind)Mathf.RoundToInt(v[o + 8]), SiteId = IdOf(v[o + 9], v[o + 10]), Slope = v[o + 11], Rough = v[o + 12],
					});
				}
				for (int i = 0; i < solids; i++, o += 11)
				{
					s.Solids.Add(new CaveSolid
					{
						Kind = (CaveSolidKind)Mathf.RoundToInt(v[o]), A = V(o + 1), B = V(o + 4), Width = v[o + 7], Depth = v[o + 8], Arch = v[o + 9], Angle = v[o + 10],
					});
				}
				for (int i = 0; i < rifts; i++, o += 9)
				{
					s.Rifts.Add(new CaveRift { A = V(o), B = V(o + 3), Top = v[o + 6], Width = v[o + 7], Narrow = v[o + 8] });
				}
				for (int i = 0; i < shafts; i++, o += 7)
				{
					s.Shafts.Add(new CaveShaft { Bottom = V(o), Top = v[o + 3], Radius = v[o + 4], Square = v[o + 5] > 0.5f, Yaw = v[o + 6] });
				}
				for (int i = 0; i < crossings; i++, o += 8)
				{
					s.Crossings.Add(new CaveCrossing { Kind = (CaveCrossingKind)Mathf.RoundToInt(v[o]), A = V(o + 1), B = V(o + 4), HalfWidth = v[o + 7] });
				}
				for (int i = 0; i < climbs; i++, o += 13)
				{
					s.Climbs.Add(new CaveClimb { Kind = (CaveClimbKind)Mathf.RoundToInt(v[o]), Bottom = V(o + 1), Top = V(o + 4), Out = V(o + 7), TopExit = V(o + 10) });
				}
				if (o != v.Length || s.Mouths.Count == 0 || s.Passages.Count == 0)
				{
					return null;
				}
				s.Prepare();
				return s;
			}
			catch (IndexOutOfRangeException)
			{
				return null;
			}
		}
	}
}
#endif
