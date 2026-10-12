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
	public sealed class CaveSystem
	{
		public const int Format = 3;
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
		/// their mouths through two to five rooms to a cavern, with side passages; ice caves a little smaller and smoother; lava
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
						Pitch = 5f, Turn = 6f, Flat = 0.62f, Noise = 0.18f, RoomEveryMin = 70f, RoomEveryMax = 140f, RoomMin = 1.3f, RoomMax = 1.7f, CavernMin = 1.6f, CavernMax = 2.2f,
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
							RoomMin = 1.6f, RoomMax = 2.4f, CavernMin = 2.6f, CavernMax = 3.6f, BranchesMin = 0, BranchesMax = 1, BranchMin = 20f, BranchMax = 45f }
						: size == 1
						? new Profile { RadiusMin = 3f, RadiusMax = 5f, TrunkMin = 180f, TrunkMax = 300f, DepthMin = 40f, DepthMax = 80f, RoomEveryMin = 50f, RoomEveryMax = 95f,
							RoomMin = 1.7f, RoomMax = 2.6f, CavernMin = 3f, CavernMax = 4.5f, BranchesMin = 1, BranchesMax = 3, BranchMin = 25f, BranchMax = 70f }
						: new Profile { RadiusMin = 4f, RadiusMax = 7f, TrunkMin = 280f, TrunkMax = 480f, DepthMin = 70f, DepthMax = 140f, RoomEveryMin = 55f, RoomEveryMax = 110f,
							RoomMin = 1.8f, RoomMax = 2.8f, CavernMin = 3.2f, CavernMax = 5.5f, BranchesMin = 2, BranchesMax = 5, BranchMin = 30f, BranchMax = 90f };
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
		public static CaveSystem Plan(CaveForm form, int size, Vector3 mouth, float yaw, int seed, int siteId, CaveGround ground, float seaLevel, out string problem,
			float waterLevel = float.NaN)
		{
			problem = null;
			var rng = new DeterministicRNG(seed);
			Profile pf = ProfileFor(form, size);
			float r0 = rng.Range(pf.RadiusMin, pf.RadiusMax);
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
			float floor0 = foot + system.Rim;
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
			system.WaterLevel = awash ? water : float.NaN;
			// Every other cave keeps its floors over the sea, all of them (2026-10-11): the sea's surface stands at its
			// level everywhere, so a passage under it would be a flooded one.
			system.LowestFloor = awash ? water - AwashDepth : seaLevel + AboveSea;
			if (!awash && floor0 < system.LowestFloor)
			{
				problem = $"its mouth stands {floor0 - seaLevel:0.#} m over the sea; a cave that low is the sea's (a sea cave or a grotto)";
				return null;
			}
			system.Origin = new Vector3(mx, floor0, mz);
			var walker = new Walker(system, ground, pf, rng);
			float zone = Mathf.Max(14f, 3.5f * r0);
			system.Mouths.Add(new CaveMouth { Floor = Vector3.zero, Yaw = Mathf.Repeat(yaw, 360f), SiteId = siteId, Radius = r0, Zone = zone, Collar = 1.5f, CollarLow = 1.5f, Shell = 3f });

			// The trunk: level through the mouth, then down toward its depth, rooms along it, the cavern at its end.
			float trunkLength = rng.Range(pf.TrunkMin, pf.TrunkMax);
			float depth = rng.Range(pf.DepthMin, pf.DepthMax);
			float level = awash ? 0.6f * r0 + 6f : 0.8f * r0 + 1f;
			var start = new CaveNode { Centre = outward * (0.2f * r0) + Vector3.up * (FloorDepth * r0 * pf.Flat), Radius = r0, Floor = 0f, Along = 0f };
			CavePassage trunk = walker.Walk(CavePassageKind.Trunk, start, Mathf.Repeat(yaw + 180f, 360f), trunkLength, depth, level, zone, awash);
			float shortest = form == CaveForm.Grotto ? 8f : Mathf.Max(zone + 5f, 0.35f * pf.TrunkMin);
			float along = trunk.Nodes[trunk.Nodes.Count - 1].Along;
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
			system.Passages.Add(trunk);

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
				// An alcove at its end most of the time; otherwise it narrows and stops.
				if (rng.NextFloat() < 0.6f || pf.Square)
				{
					if (walker.TryChamber(branch, branch.Nodes.Count - 1, rng.Range(1.4f, 2f), pf.Square ? CaveKind.Stope : CaveKind.Alcove, siteId, out CaveChamber alcove))
					{
						system.Chambers.Add(alcove);
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

			private float Ground(Vector3 local) => ground.At(local.x + system.Origin.x, local.z + system.Origin.z) - system.Origin.y;
			private bool Inside(Vector3 local, float reach) => ground.Contains(local.x + system.Origin.x, local.z + system.Origin.z, reach);

			private float Cover(Vector3 centre, float radius, Vector3 previous, float heading)
			{
				float top = centre.y + radius * pf.Flat + amp;
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
				float aim = heading, pitch = open ? -pf.Pitch : 0f, along = 0f, floor = start.Floor, rad = start.Radius;
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
					float want = entering ? 0f : below < -1f ? -pf.Pitch : below > 2f ? pf.Pitch * 0.5f : tilt * 0.5f;
					float wanderPitch = Mathf.Clamp(Mathf.MoveTowards(pitch, want, 8f) + tilt * 0.4f, -pf.Pitch, pf.Pitch);
					var candidates = new (float heading, float pitch)[]
					{
						(wander, entering ? 0f : wanderPitch),
						(wander, entering ? 0f : -pf.Pitch),
						(heading + Mathf.Clamp(DeltaAngle(heading, uphill), -2f * Mathf.Max(6f, pf.Turn), 2f * Mathf.Max(6f, pf.Turn)), entering ? 0f : -pf.Pitch),
						(heading + 60f, entering ? 0f : -pf.Pitch),
						(heading - 60f, entering ? 0f : -pf.Pitch),
					};
					bool taken = false;
					foreach ((float h, float p) in candidates)
					{
						float hr = h * Mathf.Deg2Rad;
						float run = step * Mathf.Cos(p * Mathf.Deg2Rad);
						float nextFloor = floor + step * Mathf.Sin(p * Mathf.Deg2Rad);
						var next = new Vector3(centre.x + Mathf.Sin(hr) * run, nextFloor + FloorDepth * nextRadius * pf.Flat, centre.z + Mathf.Cos(hr) * run);
						if (nextFloor < MinFloor || !Inside(next, nextRadius + 12f))
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
						centre = next;
						heading = h;
						pitch = p;
						rad = nextRadius;
						floor = nextFloor;
						along = nextAlong;
						passage.Nodes.Add(new CaveNode { Centre = centre, Radius = rad, Floor = floor, Along = along });
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

			/// <summary>A chamber round node <paramref name="index"/> of a passage, its radius <paramref name="scale"/> of the bore's, if the rock holds it (shrinking up to four times).</summary>
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
				for (int shrink = 0; shrink < 5; shrink++, scale *= 0.85f)
				{
					float rx = at.Radius * scale, rz = rx * across;
					// A cavern is tall; a room a little over the bore; a stope low.
					float ry = kind == CaveKind.Cavern ? Mathf.Max(6f, rx * rng.Range(0.45f, 0.7f))
						: kind == CaveKind.Stope ? Mathf.Max(3.2f, 1.2f * at.Radius * pf.Flat)
						: Mathf.Max(3.4f, rx * rng.Range(0.4f, 0.6f));
					float shift = end ? 0.55f * rx : 0f;
					Vector3 c = at.Centre + dir * shift;
					float floor = at.Floor + slope * shift;
					// Its low end no lower than the lowest floor (the sea's reach): smaller, or a step back up the passage, where
					// it would run under it. Never flattened to fit: a floor flatter than the passage it opens off leaves a drop.
					if (floor - Mathf.Abs(slope) * rx < MinFloor)
					{
						continue;
					}
					c.y = floor + 0.4f * ry;
					float top = c.y + ry + amp;
					float cover = Ground(c) - top;
					var side = new Vector3(dir.z, 0f, -dir.x);
					for (int k = 0; k < 8; k++)
					{
						float a = k * Mathf.PI / 4f, x = Mathf.Cos(a) * 0.9f * rx;
						Vector3 p = c + dir * x + side * (Mathf.Sin(a) * 0.9f * rz);
						// The vault is sheared with the floor: its roof here no higher than the centre's, moved by the slope.
						cover = Mathf.Min(cover, Ground(p) - (top + Mathf.Max(0f, slope * x)));
					}
					if (cover >= MinCover && Inside(c, Mathf.Max(rx, rz) + 12f))
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
		}

		// ── Joining two systems ───────────────────────────────────

		/// <summary>
		/// Joins <paramref name="other"/> (a system planned before, from another cave site) into this one, deep down: a link
		/// passage from this system's deepest chamber to the other's nearest, walkable and under the rock all the way. On
		/// success the other's mouths, passages and chambers become this system's (in its frame) and the link is added;
		/// false (and nothing changed) where no link fits.
		/// </summary>
		/// <summary>Why the last <see cref="TryJoin"/> found no link, for the notes.</summary>
		public string JoinProblem;

		public bool TryJoin(CaveSystem other, CaveGround ground, float maxLength, int seed)
		{
			if (other == null || other.Chambers.Count == 0 || Chambers.Count == 0)
			{
				JoinProblem = "no chambers to join";
				return false;
			}
			Vector3 shift = other.Origin - Origin;
			// From this system's deepest chamber to the other's chamber nearest it across and in depth.
			CaveChamber from = Chambers[0];
			foreach (CaveChamber c in Chambers)
			{
				if (c.Floor < from.Floor)
				{
					from = c;
				}
			}
			CaveChamber to = other.Chambers[0];
			float best = float.MaxValue;
			foreach (CaveChamber c in other.Chambers)
			{
				// Near across and near in depth: a link climbing far to a shallow chamber runs out of rock over it.
				Vector3 apart = c.Centre + shift - from.Centre;
				float d = new Vector2(apart.x, apart.z).magnitude + 3f * Mathf.Abs(c.Floor + shift.y - from.Floor);
				if (d < best)
				{
					best = d;
					to = c;
				}
			}
			Vector3 target = to.Centre + shift;
			Vector3 flat = target - from.Centre;
			flat.y = 0f;
			if (flat.magnitude > maxLength)
			{
				JoinProblem = $"the chambers are {flat.magnitude:0} m apart";
				return false;
			}
			var rng = new DeterministicRNG(seed);
			Profile pf = ProfileFor(Form, SizeClass);
			float r = Mathf.Max(MinBore, Radius * 0.8f);
			float amp = NoiseShare * Radius;
			float step = Mathf.Clamp(0.9f * r, 2.5f, 5f);
			var link = new CavePassage { Kind = CavePassageKind.Link };
			Vector3 dir0 = flat.normalized;
			var centre = from.Centre + dir0 * (Mathf.Max(from.Radii.x, from.Radii.z) * 0.7f);
			float floor = FloorOf(from, centre);
			centre.y = floor + FloorDepth * r * Flat;
			link.Nodes.Add(new CaveNode { Centre = centre, Radius = r, Floor = floor, Along = 0f });
			float along = 0f;
			float heading = Mathf.Atan2(dir0.x, dir0.z) * Mathf.Rad2Deg;
			float GroundAt(Vector3 local) => ground.At(local.x + Origin.x, local.z + Origin.z) - Origin.y;
			for (int guard = 0; guard < 400 && along < maxLength * 1.6f; guard++)
			{
				Vector3 toTarget = target - centre;
				toTarget.y = 0f;
				float reach = Mathf.Max(to.Radii.x, to.Radii.z) * 0.6f;
				// The target chamber's floor under here (its floor is a sloping plane).
				float targetFloor = FloorOf(to, centre - shift) + shift.y;
				float gap = targetFloor - floor;
				if (toTarget.magnitude <= reach && Mathf.Abs(gap) <= 0.4f)
				{
					Passages.Add(link);
					Absorb(other, shift);
					Prepare();
					return true;
				}
				float aim = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
				// Toward the target's floor, no steeper than a walk: where the drop is too much for the way left, it winds
				// (a switchback) until it is not.
				float remaining = Mathf.Max(step, toTarget.magnitude - reach);
				if (Mathf.Abs(gap) > remaining * 0.33f)
				{
					aim += 75f;
				}
				heading += Mathf.Clamp(DeltaAngle(heading, aim), -20f, 20f) + rng.Range(-8f, 8f);
				float wantPitch = Mathf.Atan2(gap, remaining) * Mathf.Rad2Deg;
				// 2° gentler than a trunk: a link meets chambers at both ends, and each junction's crease steepens it.
				float p = Mathf.Clamp(wantPitch, -(WalkPitch - 2f), WalkPitch - 2f);
				float run = step * Mathf.Cos(p * Mathf.Deg2Rad);
				float nextFloor = 0f;
				Vector3 next = default;
				bool found = false;
				// Round the two systems' other passages and chambers, never through them: where it crossed a sloping room at
				// an angle, the two floors met in a drop. Only the chamber it leaves and the one it reaches are its to touch.
				foreach (float turn in LinkTurns)
				{
					float h = heading + turn, hr = h * Mathf.Deg2Rad;
					float nf = floor + step * Mathf.Sin(p * Mathf.Deg2Rad);
					var n = new Vector3(centre.x + Mathf.Sin(hr) * run, nf + FloorDepth * r * Flat, centre.z + Mathf.Cos(hr) * run);
					if (!ground.Contains(n.x + Origin.x, n.z + Origin.z, r + 12f))
					{
						continue;
					}
					if (GroundAt(n) - (n.y + r * Flat + amp) < MinCover)
					{
						// Too near the surface here: go deeper first.
						nf = floor - step * Mathf.Sin((WalkPitch - 2f) * Mathf.Deg2Rad);
						n.y = nf + FloorDepth * r * Flat;
						if (GroundAt(n) - (n.y + r * Flat + amp) < MinCover)
						{
							continue;
						}
					}
					var fromXz = new Vector2(n.x - from.Centre.x, n.z - from.Centre.z);
					var toXz = new Vector2(n.x - target.x, n.z - target.z);
					bool leaving = fromXz.magnitude < Mathf.Max(from.Radii.x, from.Radii.z) * 1.3f;
					bool arriving = toXz.magnitude < Mathf.Max(to.Radii.x, to.Radii.z) * 2f;
					// Crossing another passage or chamber is a junction only where their floors agree; elsewhere it is a drop.
					float clearance = r + 2f;
					if (!leaving && Smooth(n) < clearance && Mathf.Abs(FloorNear(n) - nf) > 0.5f
						|| !arriving && other.Smooth(n - shift) < clearance && Mathf.Abs(other.FloorNear(n - shift) + shift.y - nf) > 0.5f)
					{
						continue;
					}
					next = n;
					nextFloor = nf;
					heading = h;
					found = true;
					break;
				}
				if (!found)
				{
					JoinProblem = $"blocked {along:0} m along (cover, the ground's edge or another passage at another floor), floor {floor:0.#}, gap {gap:0.#}";
					return false;
				}
				// Never under the lower of the two systems' floors (the sea's level for a dry cave).
				if (nextFloor + Origin.y < Mathf.Max(LowestFloor, other.LowestFloor))
				{
					JoinProblem = "it would run under the sea's reach";
					return false;
				}
				centre = next;
				floor = nextFloor;
				along += step;
				link.Nodes.Add(new CaveNode { Centre = centre, Radius = r * rng.Range(0.92f, 1.08f), Floor = floor, Along = along });
			}
			JoinProblem = $"it wandered {along:0} m without arriving";
			return false;
		}

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

		/// <summary>Builds the segment tables and the 16 m buckets the field reads; call after the passages or chambers change.</summary>
		public void Prepare()
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
			float pad = Amp + Mathf.Max(ShellDeep, MaxMouthShell) + 2f * blend + 2f * MeshCell + MouthReach;
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
			// The outcrops' blocks, laid out here once (the mesher reads the field from several threads; nothing may fill a
			// cache under it).
			outcrops.Clear();
			if (HasNotch)
			{
				for (int m = 0; m < Mouths.Count; m++)
				{
					outcrops[m] = LayOutcrop(m, Mouths[m]);
				}
				for (int m = 0; m < Mouths.Count; m++)
				{
					NotchBounds(Mouths[m], out Vector3 lo, out Vector3 hi);
					AddToBuckets(lo - Vector3.one * pad, hi + Vector3.one * pad, NotchId - m);
				}
			}
		}

		// ── The notch before a mouth ─────────────────────────────

		/// <summary>Bucket ids at and under this are mouths' notches (NotchId − mouth index).</summary>
		private const int NotchId = -1000000;
		/// <summary>How far the notch runs back into the face behind the mouth, so it meets the bore, metres.</summary>
		private const float NotchBack = 0.6f;
		/// <summary>How much the notch widens per metre out from the face.</summary>
		private const float NotchFlare = 0.35f;
		/// <summary>The notch's highest reach over the mouth's floor, metres (above that the hillside is open sky anyway).</summary>
		private const float NotchTop = 40f;

		/// <summary>
		/// Whether this system's mouths open through a notch cut into the hillside: a natural cave's, a lava tube's and an ice
		/// cave's (reference photos, 2026-10-12: the mouth stands in a rock face that rises above it). A grotto and a sea
		/// cave open at the water, and a mine at its own timbered portal.
		/// </summary>
		public bool HasNotch => Form == CaveForm.Cave || Form == CaveForm.LavaTube || Form == CaveForm.IceCave;

		/// <summary>How far out from its face a mouth's notch runs, metres.</summary>
		public static float NotchDepth(CaveMouth mouth) => Mathf.Max(4f, 1.6f * mouth.Radius);

		/// <summary>The notch's half-width at the face, metres: the arch's foot.</summary>
		private static float NotchHalfWidth(CaveMouth mouth) => 1.15f * mouth.Radius;

		/// <summary>The notch's half-width <paramref name="outward"/> metres out from its face (what the mouth's dressing lines).</summary>
		public static float NotchHalfWidthAt(CaveMouth mouth, float outward) => NotchHalfWidth(mouth) + NotchFlare * Mathf.Max(0f, outward);

		private static void NotchBounds(CaveMouth mouth, out Vector3 lo, out Vector3 hi)
		{
			float reach = NotchDepth(mouth) + NotchBack + NotchHalfWidth(mouth) + NotchFlare * NotchDepth(mouth);
			lo = mouth.Floor - new Vector3(reach, 1f, reach);
			hi = mouth.Floor + new Vector3(reach, NotchTop, reach);
		}

		/// <summary>
		/// The notch before a mouth as a void (negative inside): a slot cut into the hillside from the mouth's face out over
		/// its apron, its floor the mouth's own, its sides widening outward, open to the sky. Its back is the face the arch
		/// opens in, and its sides the rock walls either side of the way in (reference photos, 2026-10-12): the hole cutting,
		/// the collar and the shell all see it as cave, so its walls are rock, flush at their tops with the hill.
		/// </summary>
		private static float Notch(CaveMouth mouth, Vector3 p)
		{
			float yaw = mouth.Yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
			var side = new Vector3(outward.z, 0f, -outward.x);
			Vector3 q = p - mouth.Floor;
			float w = q.x * outward.x + q.z * outward.z;
			float u = Mathf.Abs(q.x * side.x + q.z * side.z);
			float half = NotchHalfWidth(mouth) + NotchFlare * Mathf.Max(0f, w);
			float along = Mathf.Max(-NotchBack - w, w - NotchDepth(mouth));
			return Mathf.Max(Mathf.Max(along, u - half), -q.y);
		}

		/// <summary>How much further a mouth's rock reaches than its passage's: the arch's spread and the collar's wander.</summary>
		private float MouthReach => ArchSpread * (Radius + MaxMouthShell) + CollarWander + (HasNotch ? (OutcropWidth + OutcropBack) * Radius + 4f : 0f);

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
		private float Tube(Vector3 p, out float radius, out float floor)
		{
			radius = Radius;
			floor = 0f;
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
				float primRadius, primFloor;
				if (id <= NotchId)
				{
					CaveMouth mouth = Mouths[NotchId - id];
					prim = Notch(mouth, p);
					primRadius = mouth.Radius;
					primFloor = mouth.Floor.y;
				}
				else if (id >= 0)
				{
					Vector3 ap = p - segA[id];
					float t = Mathf.Clamp01(Vector3.Dot(ap, segAB[id]) / segLen2[id]);
					Vector3 q = ap - segAB[id] * t;
					q.y *= invFlat;
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
				}
				if (prim < best)
				{
					best = prim;
					radius = primRadius;
				}
				primBuffer[n] = prim;
				floorBuffer[n] = primFloor;
				n++;
				d = d == float.MaxValue ? prim : SMin(d, prim, id >= 0 || id <= NotchId ? blend : 1.5f * blend);
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
			Tube(p, out _, out float floor);
			return floor;
		}

		/// <summary>The void without noise.</summary>
		public float Smooth(Vector3 p) => Tube(p, out _, out _);

		/// <summary>The void with rock noise on walls and roofs and a near-flat floor, negative inside.</summary>
		public float Void(Vector3 p) => Void(p, out _);

		private float Void(Vector3 p, out float smooth)
		{
			smooth = Tube(p, out float radius, out float floor);
			if (smooth > 1e3f)
			{
				return smooth;
			}
			float w = Mathf.Clamp01((p.y - floor) / Mathf.Max(0.5f, 0.5f * radius * Flat));
			w = w * w * (3f - 2f * w);
			float noise = ProceduralNoise.Fbm3(p / (0.9f * Radius), 2, 0.5f, Seed + 17);
			// Round the opening the rock shows its bedding instead of the bore's knobs: layers each jutting at its top and
			// undercut at its foot, wavering, so the mouth has a brow and ledges rather than a smooth dome. Bounded by the
			// same amplitude as the knobs, so the holes, the collar and the buckets that allow for it still hold.
			float mouth = MouthWeight(p, out _, out _);
			float amp = Amp;
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
			return smooth - amp * w * noise - 0.06f * (1f - w) * ProceduralNoise.Gradient3(p / 1.3f, Seed + 23);
		}

		/// <summary>
		/// The outcrop a natural cave's mouth opens in (reference photos, 2026-10-12: the opening set in a rugged mass of rock
		/// rising out of the hillside, broken into great blocks): a mound of overlapping blocks across the mouth's face,
		/// tallest over the arch and falling away to either side, every block tilted along one bedding the rock splits on (with
		/// some play), stepped in and out along the front. Each block is an exact box, so its faces mesh flat and its edges
		/// crisp, as no noise on a smooth field could at the mesher's cell size. The mound is filled solid round and over the
		/// arch and the opening and notch are carved through it by the void, so nothing frames the arch like masonry and no
		/// window opens between it and the rock over it. It only adds rock, so the cut stays covered. Negative inside.
		/// </summary>
		private float Outcrop(int mouthIndex, CaveMouth mouth, Vector3 p)
		{
			if (!outcrops.TryGetValue(mouthIndex, out Outcropping o))
			{
				return float.MaxValue;
			}
			Vector3 q = p - mouth.Floor;
			float u = q.x * o.Side.x + q.z * o.Side.z;
			int cu = Mathf.FloorToInt((u - o.U0) / OutcropCell), cy = Mathf.FloorToInt((q.y - o.Y0) / OutcropCell);
			float d = float.MaxValue;
			// Every block is listed in each cell its reach touches, so a point's own cell holds every block near it.
			if (cu >= 0 && cy >= 0 && cu < o.Columns && cy < o.Rows)
			{
				List<int> ids = o.Cells[cy * o.Columns + cu];
				if (ids != null)
				{
					for (int i = 0; i < ids.Count; i++)
					{
						// Joined softly, so two blocks that nearly touch fill the hairline between them (a pinch the mesher would
						// make a non-manifold edge of) as rock fills a joint.
						float b = o.Blocks[ids[i]].Distance(p);
						d = d == float.MaxValue ? b : SMin(d, b, BlockJoin);
					}
				}
			}
			return d;
		}

		/// <summary>How softly an outcrop's blocks are joined to each other and to the hillside's rock, metres.</summary>
		private const float BlockJoin = 0.3f;

		/// <summary>The lookup grid's cell across a mouth's face (u, height), metres: as big as the largest block's reach.</summary>
		private const float OutcropCell = 3.2f;

		/// <summary>How far an outcrop reaches either side of its mouth and back from its face, a share of the bore's radius (for the buckets).</summary>
		public const float OutcropWidth = 2.6f, OutcropBack = 1.2f;

		/// <summary>One block of an outcrop: a box at a centre, with its own axes and half-extents.</summary>
		private struct Block
		{
			public Vector3 Centre, X, Y, Z, Half;
			/// <summary>How far its edges are rounded, metres.</summary>
			public float Bevel;

			public float Distance(Vector3 p)
			{
				Vector3 o = p - Centre;
				float qx = Mathf.Abs(Vector3.Dot(o, X)) - (Half.x - Bevel), qy = Mathf.Abs(Vector3.Dot(o, Y)) - (Half.y - Bevel), qz = Mathf.Abs(Vector3.Dot(o, Z)) - (Half.z - Bevel);
				float outside = new Vector3(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f), Mathf.Max(qz, 0f)).magnitude;
				return outside + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f) - Bevel;
			}
		}

		/// <summary>A mouth's outcrop: its blocks, and a grid across its face (u, height) of the blocks each cell may meet.</summary>
		private sealed class Outcropping
		{
			public Block[] Blocks;
			public Vector3 Side;
			public float U0, Y0;
			public int Columns, Rows;
			public List<int>[] Cells;
		}

		private readonly Dictionary<int, Outcropping> outcrops = new Dictionary<int, Outcropping>();

		/// <summary>Lays out a mouth's outcrop from the mouth and the system's seed.</summary>
		private Outcropping LayOutcrop(int mouthIndex, CaveMouth mouth)
		{
			uint h = (uint)(Seed * 2654435761u) ^ (uint)(mouthIndex * 40503);
			int salt = 0;
			float R(float lo, float hi) => lo + (hi - lo) * Unit(h, (uint)(++salt));
			float yaw = mouth.Yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
			var side = new Vector3(outward.z, 0f, -outward.x);
			float r = mouth.Radius;
			float crown = 2f * r * Mathf.Max(0.2f, Flat);
			// The mound: its half-width across the face, its height over the floor at the middle, its depth into the hill.
			float halfWidth = r * (1f + ArchSpread) + R(3f, 5f) + 0.5f * r;
			float peak = crown + R(1.8f, 3.2f) + 0.2f * r;
			float depth = R(2.5f, 3.5f) + 0.4f * r;
			// The bedding every block is tilted along (a dip across the face), and how much each block strays from it.
			float dip = R(-0.32f, 0.32f), pitch = R(-0.12f, 0.05f);
			var blocks = new List<Block>();
			void Add(Vector3 centre, Vector3 size)
			{
				float turn = R(-0.15f, 0.15f), roll = dip + R(-0.12f, 0.12f), lean = pitch + R(-0.1f, 0.1f);
				Vector3 x = (side * Mathf.Cos(turn) + outward * Mathf.Sin(turn)).normalized;
				Vector3 z = (outward * Mathf.Cos(turn) - side * Mathf.Sin(turn)).normalized;
				// Lean (about x) and roll (about z), by hand: no engine rotation here, so the field stays pure.
				Vector3 y1 = (Vector3.up * Mathf.Cos(lean) + z * Mathf.Sin(lean)).normalized;
				Vector3 z1 = Vector3.Cross(x, y1).normalized;
				Vector3 y2 = (y1 * Mathf.Cos(roll) - x * Mathf.Sin(roll)).normalized;
				Vector3 x2 = Vector3.Cross(y2, z1).normalized;
				blocks.Add(new Block
				{
					Centre = centre,
					X = x2,
					Y = y2,
					Z = z1,
					Half = size * 0.5f,
					Bevel = Mathf.Min(0.3f, 0.12f * Mathf.Min(size.x, Mathf.Min(size.y, size.z))),
				});
			}
			// Courses of blocks up the mound, each course's blocks laid across it at random widths, the front ones stepped
			// in and out, a second course of blocks behind them into the hill.
			float y0 = -1.2f;
			for (float y = y0; y < peak; )
			{
				float course = R(1.1f, 2.1f);
				float t = Mathf.Clamp01((y + course * 0.5f) / Mathf.Max(1f, peak));
				// Narrower with height: the mound's shoulders fall away from the middle.
				float half = halfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) + 0.5f;
				for (float u = -half; u < half; )
				{
					float width = R(1.5f, 3.2f);
					float cu = u + width * 0.5f;
					for (int layer = 0; layer < 2; layer++)
					{
						float bd = layer == 0 ? R(1.4f, 2.4f) : depth;
						float front = layer == 0 ? R(-0.5f, 0.6f) : R(-1.2f, -0.6f);
						Vector3 centre = mouth.Floor + side * cu + Vector3.up * (y + course * 0.5f) + outward * (front - bd * 0.5f);
						Add(centre, new Vector3(width * R(1f, 1.25f), course * R(1.05f, 1.3f), bd));
					}
					u += width * R(0.8f, 1f);
				}
				y += course * R(0.8f, 0.95f);
			}
			var o = new Outcropping { Blocks = blocks.ToArray(), Side = side };
			// The lookup grid: each block listed in every cell its reach (half-diagonal) touches.
			float uMin = float.MaxValue, uMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
			var reach = new float[o.Blocks.Length];
			var at = new Vector2[o.Blocks.Length];
			for (int i = 0; i < o.Blocks.Length; i++)
			{
				Vector3 c = o.Blocks[i].Centre - mouth.Floor;
				at[i] = new Vector2(c.x * side.x + c.z * side.z, c.y);
				reach[i] = o.Blocks[i].Half.magnitude + 0.5f;
				uMin = Mathf.Min(uMin, at[i].x - reach[i]);
				uMax = Mathf.Max(uMax, at[i].x + reach[i]);
				yMin = Mathf.Min(yMin, at[i].y - reach[i]);
				yMax = Mathf.Max(yMax, at[i].y + reach[i]);
			}
			o.U0 = uMin;
			o.Y0 = yMin;
			o.Columns = Mathf.Max(1, Mathf.CeilToInt((uMax - uMin) / OutcropCell));
			o.Rows = Mathf.Max(1, Mathf.CeilToInt((yMax - yMin) / OutcropCell));
			o.Cells = new List<int>[o.Columns * o.Rows];
			for (int i = 0; i < o.Blocks.Length; i++)
			{
				int c0 = Mathf.Clamp(Mathf.FloorToInt((at[i].x - reach[i] - uMin) / OutcropCell), 0, o.Columns - 1);
				int c1 = Mathf.Clamp(Mathf.FloorToInt((at[i].x + reach[i] - uMin) / OutcropCell), 0, o.Columns - 1);
				int r0 = Mathf.Clamp(Mathf.FloorToInt((at[i].y - reach[i] - yMin) / OutcropCell), 0, o.Rows - 1);
				int r1 = Mathf.Clamp(Mathf.FloorToInt((at[i].y + reach[i] - yMin) / OutcropCell), 0, o.Rows - 1);
				for (int row = r0; row <= r1; row++)
				{
					for (int col = c0; col <= c1; col++)
					{
						(o.Cells[row * o.Columns + col] ??= new List<int>()).Add(i);
					}
				}
			}
			return o;
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
				float reach = mapped ? cut : smooth - (width + wander);
				collar = Mathf.Max(reach, p.y - (g + rim));
			}
			float clip = collar == float.MaxValue ? buried : SMin(buried, collar, edgeBlend);
			float rock = SMax(shell, clip, edgeBlend);
			if (HasNotch)
			{
				// The outcrop the mouth stands in, its opening and notch cut through it.
				float outcrop = Outcrop(mouthIndex, mouth, p);
				rock = Mathf.Max(outcrop == float.MaxValue ? rock : SMin(rock, outcrop, BlockJoin), -v);
			}
			return rock;
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
				float grow = Mathf.Max(ShellDeep, MaxMouthShell) + Amp + 3f * MeshCell + MouthReach;
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

		/// <summary>Everything needed to build the same system again, as plain floats (a shape's values).</summary>
		public float[] ToValues()
		{
			var v = new List<float>
			{
				Format, (float)Form, SizeClass, Radius, Flat, NoiseShare, Rim, Skin, ShellDeep, MeshCell,
				Mouths.Count, Passages.Count, Chambers.Count,
				float.IsNaN(WaterLevel) ? NoWater : WaterLevel, float.IsNegativeInfinity(LowestFloor) ? NoWater : LowestFloor,
			};
			foreach (CaveMouth m in Mouths)
			{
				v.AddRange(new[] { m.Floor.x, m.Floor.y, m.Floor.z, m.Yaw, IdHigh(m.SiteId), IdLow(m.SiteId), m.Radius, m.Zone, m.Collar, m.CollarLow, m.Shell });
			}
			foreach (CavePassage p in Passages)
			{
				v.Add((float)p.Kind);
				v.Add(p.Nodes.Count);
				foreach (CaveNode n in p.Nodes)
				{
					v.AddRange(new[] { n.Centre.x, n.Centre.y, n.Centre.z, n.Radius, n.Floor, n.Along });
				}
			}
			foreach (CaveChamber c in Chambers)
			{
				v.AddRange(new[] { c.Centre.x, c.Centre.y, c.Centre.z, c.Radii.x, c.Radii.y, c.Radii.z, c.Floor, c.Yaw, (float)c.Kind, IdHigh(c.SiteId), IdLow(c.SiteId), c.Slope });
			}
			return v.ToArray();
		}

		/// <summary>The system stored by <see cref="ToValues"/>, its frame at <paramref name="origin"/>; null if they are not one.</summary>
		public static CaveSystem FromValues(float[] v, Vector3 origin, int seed)
		{
			if (v == null || v.Length < 15 || Mathf.RoundToInt(v[0]) != Format)
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
				int o = 15;
				for (int i = 0; i < mouths; i++, o += 11)
				{
					s.Mouths.Add(new CaveMouth
					{
						Floor = new Vector3(v[o], v[o + 1], v[o + 2]), Yaw = v[o + 3], SiteId = IdOf(v[o + 4], v[o + 5]), Radius = v[o + 6],
						Zone = v[o + 7], Collar = v[o + 8], CollarLow = v[o + 9], Shell = v[o + 10],
					});
				}
				for (int i = 0; i < passages; i++)
				{
					var p = new CavePassage { Kind = (CavePassageKind)Mathf.RoundToInt(v[o]) };
					int nodes = Mathf.RoundToInt(v[o + 1]);
					o += 2;
					for (int k = 0; k < nodes; k++, o += 6)
					{
						p.Nodes.Add(new CaveNode { Centre = new Vector3(v[o], v[o + 1], v[o + 2]), Radius = v[o + 3], Floor = v[o + 4], Along = v[o + 5] });
					}
					s.Passages.Add(p);
				}
				for (int i = 0; i < chambers; i++, o += 12)
				{
					s.Chambers.Add(new CaveChamber
					{
						Centre = new Vector3(v[o], v[o + 1], v[o + 2]), Radii = new Vector3(v[o + 3], v[o + 4], v[o + 5]), Floor = v[o + 6], Yaw = v[o + 7],
						Kind = (CaveChamberKind)Mathf.RoundToInt(v[o + 8]), SiteId = IdOf(v[o + 9], v[o + 10]), Slope = v[o + 11],
					});
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
