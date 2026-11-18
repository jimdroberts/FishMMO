#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a sea hall's smoker is (<see cref="CaveSmoker"/>). Append only: the ordinal is stored with the cave.</summary>
	public enum CaveSmokerKind : byte
	{
		/// <summary>A black smoker: a sulphide chimney, its plume black with metal sulphides.</summary>
		Black = 0,
		/// <summary>A white smoker: a low anhydrite chimney, its plume white, liquid carbon dioxide beading off it.</summary>
		White = 1,
		/// <summary>A beehive diffuser: a knobbly mound the hot water seeps through, a shimmer over it and no plume to speak of.</summary>
		Beehive = 2,
	}

	/// <summary>
	/// One vent on a sea hall's floor (2026-10-26, phase I2n-b, decision 83): stored with the cave and read by the cut (the chimneys'
	/// formations, the floor's heat) and by the client (the plumes, through the cave-life spots the dressing writes from these), so
	/// one list says where every smoker is and nothing searches for them. System frame.
	/// </summary>
	public struct CaveSmoker
	{
		/// <summary>Its foot's centre on the hall's floor.</summary>
		public Vector3 Base;
		/// <summary>The chimney's height over its foot, metres (its orifice is at the top).</summary>
		public float Height;
		/// <summary>The chimney's radius at its foot, metres.</summary>
		public float Radius;
		public CaveSmokerKind Kind;
		/// <summary>The hall's one Great Smoker: a spire whose crown breaks the waterline into the air pocket.</summary>
		public bool Great;

		/// <summary>The orifice: the chimney's top.</summary>
		public Vector3 Orifice => Base + Vector3.up * Height;
	}

	/// <summary>What a sea hall's floor feature is (<see cref="CaveHallPool"/>). Append only: the ordinal is stored with the cave.</summary>
	public enum CaveHallPoolKind : byte
	{
		/// <summary>A hot brine pool (the HotBrinePool preset): a basin in the floor, a mirror of denser brine standing in it.</summary>
		Brine = 0,
		/// <summary>A lava-pillar pit (the LavaPillars preset): a collapse pit in the sheet flow, basalt pillars standing in it.</summary>
		LavaPillars = 1,
	}

	/// <summary>A basin on a sea hall's floor (system frame): its rim's centre, radius, and (a brine pool) its mirror's height.</summary>
	public struct CaveHallPool
	{
		public CaveHallPoolKind Kind;
		public Vector3 Rim;
		public float Radius;
		/// <summary>A brine pool's mirror (the brine's surface under the sea water), y; a pillar pit's bottom.</summary>
		public float Mirror;
	}

	public sealed partial class CaveSystem
	{
		/*
		 * The sea hall (2026-10-26, phase I2n-b, decision 83: Subsurface Ocean Vent becomes an INTERIOR biome). Under an ice moon's
		 * shell a warm sea fills a great dome of marine ice: its floor a field of smoking chimneys 25–60 m under the water, an air
		 * pocket under its crown with a strand on one side (the Merehall ledge), reached only down a melt shaft from the ice above by a
		 * stair wound round its wall (the Mere Stair), and a few flooded side passages to brine-pool grottoes that are only dived to.
		 * Laid on the machinery every cave is: the dome and the ledge are chambers, the shaft a shaft with a spiral ledge (as a ledged
		 * pit's), the ways passages, the basins hollows and the pillars columns; the hall's water is its WaterLevel, written as a pool
		 * over every void under it (CaveShaper.WritePool). What is a sea hall's own: its smokers and its floor's basins, stored after
		 * everything else (a trailing section only a sea hall writes, so every other cave's values are what they were).
		 */

		/// <summary>The marker that opens a sea hall's trailing section in its values (never a value any count or coordinate takes there).</summary>
		private const float SeaHallTag = -7719f;

		/// <summary>The smokers on a sea hall's floor (<see cref="CaveSmoker"/>); empty for every other cave.</summary>
		public readonly List<CaveSmoker> Smokers = new List<CaveSmoker>();

		/// <summary>The brine pools and pillar pits on a sea hall's floor (<see cref="CaveHallPool"/>); empty for every other cave.</summary>
		public readonly List<CaveHallPool> HallPools = new List<CaveHallPool>();

		/// <summary>How far down the melt shaft its stair runs to the ledge, metres: the ice over the hall, at least and at most.</summary>
		public const float SeaHallShaftMin = 30f, SeaHallShaftMax = 90f;

		/// <summary>The hall's great dome: the first Hall chamber of a sea hall; false for any other cave.</summary>
		public bool TryHallDome(out CaveChamber dome)
		{
			dome = default;
			if (Form != CaveForm.SeaHall)
			{
				return false;
			}
			foreach (CaveChamber c in Chambers)
			{
				if (c.Kind == CaveChamberKind.Hall)
				{
					dome = c;
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// How hot the floor is at a point (system frame), 0 cold sea floor … 1 a chimney's foot: each smoker's heat falling off with
		/// the square of the distance in plan over its reach (the Great Smoker's widest, a beehive's least), and a brine pool's warmth
		/// over its basin and a few metres round it; the most of them. 0 for every cave but a sea hall (no smokers, no pools).
		/// </summary>
		public float HeatAt(Vector3 p)
		{
			float heat = 0f;
			foreach (CaveSmoker s in Smokers)
			{
				SmokerHeat(s, out float peak, out float reach);
				float dx = p.x - s.Base.x, dz = p.z - s.Base.z;
				float d = Mathf.Sqrt(dx * dx + dz * dz);
				if (d < reach)
				{
					float f = 1f - d / reach;
					heat = Mathf.Max(heat, peak * f * f);
				}
			}
			foreach (CaveHallPool pool in HallPools)
			{
				if (pool.Kind != CaveHallPoolKind.Brine)
				{
					continue;
				}
				float d = new Vector2(p.x - pool.Rim.x, p.z - pool.Rim.z).magnitude;
				heat = Mathf.Max(heat, 0.7f * (1f - Mathf.Clamp01((d - pool.Radius) / 6f)));
			}
			return Mathf.Clamp01(heat);
		}

		/// <summary>A smoker's heat at its foot and how far in plan it reaches, metres.</summary>
		public static void SmokerHeat(in CaveSmoker s, out float peak, out float reach)
		{
			if (s.Great)
			{
				peak = 1f;
				reach = 40f;
				return;
			}
			switch (s.Kind)
			{
				case CaveSmokerKind.White:
					peak = 0.6f;
					reach = 8f + s.Height;
					return;
				case CaveSmokerKind.Beehive:
					peak = 0.5f;
					reach = 5f;
					return;
				default:
					peak = 0.9f;
					reach = 6f + s.Height;
					return;
			}
		}

		/// <summary>The proportions a sea hall's sampling and its holes reckon with (its dome is laid by <see cref="PlanSeaHall"/>, not walked).</summary>
		private static Profile SeaHallProfile(int size)
			=> new Profile
			{
				RadiusMin = 1.8f, RadiusMax = 2.2f, TrunkMin = 2f, TrunkMax = size >= 1 ? 260f : 200f, DepthMin = SeaHallShaftMin, DepthMax = SeaHallShaftMax,
				Pitch = WalkPitch, Turn = 0f, Flat = 1f, Noise = 0.15f, RoomEveryMin = 999f, RoomEveryMax = 999f, CavernMin = 1f, CavernMax = 1f,
			};

		private static Vector3 HallDir(float yaw)
		{
			float r = yaw * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
		}

		/// <summary>
		/// A sea hall planned from its site (the melt shaft's middle on the ice) into the ice under it: the dome, its ledge, the shaft and
		/// its stair, the dived side passages, the floor's basins and its smokers. Null, with <paramref name="problem"/> saying why, where
		/// the ice will not hold it (too thin over the dome within <see cref="SeaHallShaftMax"/>, or the sampled ground runs out).
		/// Deterministic in its inputs; every draw is made up front in a fixed order, so a refused place moves nothing after it.
		/// </summary>
		/// <remarks>
		/// No sea's level binds it: a sea hall is only ever laid on a world frozen through (its link is gated by its interior's world,
		/// CaveInteriorArt.Of), whose scenes have no sea surface at all (SceneGenerator.AddWater), so its water stands wherever the ice
		/// puts it, under the datum or over it.
		/// </remarks>
		private static CaveSystem PlanSeaHall(int size, Vector3 site, float yaw, int seed, int siteId, CaveGround ground, out string problem)
		{
			problem = null;
			size = Mathf.Clamp(size, 0, 2);
			var rng = new DeterministicRNG(seed);
			// ── Every draw, in order ──
			float radius = size == 0 ? rng.Range(45f, 62f) : size == 1 ? rng.Range(60f, 85f) : rng.Range(78f, 100f);
			float squash = rng.Range(0.85f, 1f);
			float waterDepth = size == 0 ? rng.Range(25f, 40f) : rng.Range(35f, 60f);
			float height = rng.Range(Mathf.Max(35f, waterDepth + 12f), Mathf.Max(70f, waterDepth + 16f));
			float ledgeOver = rng.Range(2f, 6f);
			float shaftRoll = rng.Range(SeaHallShaftMin, size == 0 ? 60f : 80f);
			float pitch = rng.Range(1.1f, 1.6f);
			float ledgeWidth = rng.Range(2.2f, 2.8f);
			float shaftRadius = Mathf.Clamp(pitch / LedgeGrade + ledgeWidth + rng.Range(0.5f, 2f), 6.5f, 9.5f);
			float hand = rng.NextFloat() < 0.5f ? -1f : 1f;
			float toward = Mathf.Repeat(yaw + 180f + rng.Range(-60f, 60f), 360f);
			float lobeAlong = rng.Range(12f, 18f), lobeAcross = rng.Range(18f, 28f), lobeHeight = rng.Range(5f, 7f);
			float gap = rng.Range(4f, 7f);
			int dives = rng.Range(0, 3);
			int smokers = size == 0 ? rng.Range(6, 13) : rng.Range(10, 21);
			bool whiteField = rng.NextFloat() < 0.5f;
			float whiteHeading = rng.Range(0f, 360f);
			int brinePools = rng.Range(0, 3), pillarPits = rng.Range(0, 4);
			int featureSeed = rng.Next();

			Profile pf = SeaHallProfile(size);
			var system = new CaveSystem
			{
				Form = CaveForm.SeaHall,
				SizeClass = (byte)size,
				Seed = seed,
				Radius = 2f,
				Flat = 1f,
				NoiseShare = pf.Noise,
				// A dome 90–200 m across meshed at a metre: smooth marine ice, and the budget a large cave's halls already meet.
				MeshCell = 1f,
				Character = CaveCharacter.Classic,
				Traits = CaveTrait.None,
				lavaHeld = true,
			};
			float amp = pf.Noise * system.Radius;

			// ── The dome over its floor, the ledge at the waterline (heights first from the ledge's floor, L = 0) ──
			float shaftGround = ground.At(site.x, site.z);
			float ry = height / 1.4f;
			float water = -ledgeOver, floor = water - waterDepth, centreY = floor + 0.4f * ry;
			// The dome's half-width where the ledge's floor meets it (along its heading, toward the shaft), from L alone.
			float atLedge = (0f - centreY) / ry;
			if (atLedge >= 0.98f)
			{
				problem = "the ledge would stand over the dome's crown";
				return null;
			}
			float ledgeRadius = radius * Mathf.Sqrt(1f - atLedge * atLedge);
			float lobeOut = ledgeRadius + 0.45f * lobeAlong;
			float shaftOut = lobeOut + lobeAlong + gap + shaftRadius;
			Vector3 toShaft = HallDir(Mathf.Repeat(toward + 180f, 360f));
			// World plan, the shaft's middle at the site.
			var shaftWorld = new Vector3(site.x, 0f, site.z);
			Vector3 domeWorld = shaftWorld - toShaft * shaftOut;
			Vector3 lobeWorld = domeWorld + toShaft * lobeOut;

			// ── How deep under the shaft's lip the ledge must lie for the ice to hold the dome: its vault under MinCover everywhere ──
			float need = shaftRoll;
			Vector3 along0 = HallDir(toward + 180f), side0 = new Vector3(along0.z, 0f, -along0.x);
			for (int k = 0; k <= 16; k++)
			{
				float r = k == 16 ? 0f : k < 8 ? 0.5f : 0.92f;
				float a = (k % 8) * 45f * Mathf.Deg2Rad;
				Vector3 p = domeWorld + along0 * (Mathf.Cos(a) * r * radius) + side0 * (Mathf.Sin(a) * r * radius * squash);
				if (!ground.Contains(p.x, p.z, 4f))
				{
					problem = "the ground sampled round the shaft does not reach over the whole dome";
					return null;
				}
				float g = ground.At(p.x, p.z) - shaftGround;
				float vault = centreY + ry * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
				// With the ledge's floor D under the lip, the vault there stands at vault − D; MinCover (the noise, a margin) under g.
				need = Mathf.Max(need, vault + MinCover + amp + 3f - g);
			}
			{
				// The ledge's lobe and the way off the stair: low vaults, but the ice over them must hold too.
				float g = Mathf.Min(ground.At(lobeWorld.x, lobeWorld.z), shaftGround) - shaftGround;
				need = Mathf.Max(need, 1.4f * lobeHeight + MinCover + amp + 2f - g);
			}
			if (need > SeaHallShaftMax)
			{
				problem = $"the ice is too thin to hold a {2f * radius:0} m dome within {SeaHallShaftMax:0} m of the surface (it needs {need:0} m)";
				return null;
			}

			// ── The frame: the stair's head on the shaft's lip ──
			// The stair winds down to its foot facing the dome; its head is where it started, that many turns back round the shaft.
			float midline = shaftRadius - 0.5f * ledgeWidth;
			float start = Mathf.Repeat(toward - hand * (need / pitch) * Mathf.Rad2Deg, 360f);
			Vector3 headPlan = shaftWorld + HallDir(start) * midline;
			float headGround = ground.At(headPlan.x, headPlan.z);
			system.Origin = new Vector3(headPlan.x, headGround, headPlan.z);
			Vector3 shaft = shaftWorld - new Vector3(system.Origin.x, 0f, system.Origin.z);
			Vector3 domePlan = domeWorld - new Vector3(system.Origin.x, 0f, system.Origin.z);
			Vector3 lobePlan = lobeWorld - new Vector3(system.Origin.x, 0f, system.Origin.z);
			float shaftTop = shaftGround - system.Origin.y + SkylightOverTop;
			float ledge = shaftGround - need - system.Origin.y;
			// Everything below the ledge's floor moves down with it.
			water += ledge;
			floor += ledge;
			centreY += ledge;

			system.WaterLevel = system.Origin.y + water;
			system.LowestFloor = system.Origin.y + floor - 6f;

			// ── The stair's head: the way in on the lip ──
			float zone = 2f * shaftRadius + 4f;
			system.Mouths.Add(new CaveMouth
			{
				Floor = Vector3.zero, Yaw = start, SiteId = siteId, Radius = 0.5f * ledgeWidth + 0.6f, Zone = zone, Collar = 1.5f, CollarLow = 1.5f, Shell = 3f,
				Kind = CaveMouthKind.Ground,
			});
			// The head's passage: the first metre or two of the ledge's own descent, so the way in has its trunk (every system does).
			float bore = Mathf.Max(MinBore, 0.5f * ledgeWidth + 0.4f);
			var head = new CavePassage { Kind = CavePassageKind.Trunk };
			for (int i = 0; i < 3; i++)
			{
				float turnDeg = hand * i * (1.2f / midline) * Mathf.Rad2Deg;
				Vector3 at = shaft + HallDir(start + turnDeg) * midline;
				float f = -pitch * i * (1.2f / midline);
				head.Nodes.Add(new CaveNode { Centre = new Vector3(at.x, f + FloorDepth * bore, at.z), Radius = bore, Floor = f, Along = 1.2f * i });
			}
			system.Passages.Add(head);

			// ── The melt shaft and the Mere Stair wound down its wall ──
			system.Shafts.Add(new CaveShaft { Bottom = new Vector3(shaft.x, ledge, shaft.z), Top = shaftTop, Radius = shaftRadius });
			system.Solids.Add(new CaveSolid
			{
				Kind = CaveSolidKind.Spiral,
				A = new Vector3(shaft.x, ledge, shaft.z),
				B = new Vector3(shaft.x, 0f, shaft.z),
				Width = ledgeWidth,
				Depth = shaftRadius,
				Arch = hand * pitch,
				Angle = start,
			});
			// Where it wound down to the ledge's level: its foot faces the dome.
			float footYaw = start + hand * ((0f - ledge) / pitch) * Mathf.Rad2Deg;
			Vector3 foot = shaft + HallDir(footYaw) * midline;

			// ── Off the stair's foot, through the ice to the ledge ──
			float wayRadius = 2.4f;
			var way = new CavePassage { Kind = CavePassageKind.Branch };
			Vector3 lobeIn = lobePlan + toShaft * (0.25f * lobeAlong);
			float run = new Vector2(lobeIn.x - foot.x, lobeIn.z - foot.z).magnitude;
			int steps = Mathf.Max(2, Mathf.CeilToInt(run / 2.5f));
			for (int i = 0; i <= steps; i++)
			{
				Vector3 at = Vector3.Lerp(foot, lobeIn, i / (float)steps);
				way.Nodes.Add(new CaveNode { Centre = new Vector3(at.x, ledge + FloorDepth * wayRadius, at.z), Radius = wayRadius, Floor = ledge, Along = run * i / steps });
			}
			system.Passages.Add(way);

			// ── The dome and the ledge (the Merehall) ──
			system.Chambers.Add(new CaveChamber
			{
				Centre = new Vector3(domePlan.x, centreY, domePlan.z), Radii = new Vector3(radius, ry, radius * squash), Floor = floor,
				Yaw = Mathf.Repeat(toward + 180f, 360f), Kind = CaveChamberKind.Hall, SiteId = siteId, Rough = 0.6f,
			});
			system.Chambers.Add(new CaveChamber
			{
				Centre = new Vector3(lobePlan.x, ledge + 0.4f * lobeHeight, lobePlan.z), Radii = new Vector3(lobeAlong, lobeHeight, lobeAcross), Floor = ledge,
				Yaw = Mathf.Repeat(toward + 180f, 360f), Kind = CaveChamberKind.Room, SiteId = siteId, Rough = 0.8f,
			});
			// The ways' floors lifted by what the field's smooth unions let them down (a passage's own segments union at their floors,
			// a metre-wide blend at a metre's mesh), so each meets the stair's crisp ledge level instead of a step down off it.
			LevelWay(system, head);
			LevelWay(system, way);
			system.PrepareField();

			var features = new DeterministicRNG(featureSeed);
			// ── Dived side passages to brine-pool grottoes, under the waterline ──
			for (int d = 0; d < dives; d++)
			{
				float heading = Mathf.Repeat(toward + 180f + features.Range(70f, 290f), 360f);
				float depthUnder = features.Range(6f, 12f);
				float length = features.Range(25f, 50f);
				float bore2 = features.Range(2.2f, 3f);
				float grotto = features.Range(6f, 9f), grottoHeight = features.Range(4f, 5f);
				float y0 = water - depthUnder;
				float wall = DomeHalfWidth(radius, radius * squash, toward + 180f, heading, (y0 - centreY) / ry);
				if (wall <= 4f)
				{
					continue;
				}
				Vector3 dir = HallDir(heading);
				Vector3 from = domePlan + dir * (0.85f * wall);
				float grottoFloor = water - 1.5f;
				var dive = new CavePassage { Kind = CavePassageKind.Branch };
				int n = Mathf.Max(3, Mathf.CeilToInt(length / 2.5f));
				bool fits = true;
				for (int i = 0; i <= n; i++)
				{
					float t = i / (float)n;
					Vector3 at = from + dir * (length * t);
					// Level under the water, then rising to the grotto's floor over its last third; the roof always under the surface.
					float f = Mathf.Lerp(y0 - 1f, grottoFloor - 2f * bore2, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, t)));
					var node = new CaveNode { Centre = new Vector3(at.x, f + FloorDepth * bore2, at.z), Radius = bore2, Floor = f, Along = length * t };
					if (!ground.Contains(at.x + system.Origin.x, at.z + system.Origin.z, bore2 + 6f))
					{
						fits = false;
						break;
					}
					dive.Nodes.Add(node);
				}
				if (!fits || dive.Nodes.Count < 4)
				{
					continue;
				}
				CaveNode last = dive.Nodes[dive.Nodes.Count - 1];
				Vector3 g = last.Centre + dir * (0.6f * grotto);
				system.Passages.Add(dive);
				system.Chambers.Add(new CaveChamber
				{
					Centre = new Vector3(g.x, grottoFloor + 0.4f * grottoHeight, g.z), Radii = new Vector3(grotto, grottoHeight, grotto * 0.85f), Floor = grottoFloor,
					Yaw = heading, Kind = CaveChamberKind.Alcove, SiteId = siteId, Rough = 0.8f,
				});
			}

			// ── The floor's basins: hot brine pools and lava-pillar pits (the HotBrinePool and LavaPillars presets, on the hall floor) ──
			Vector3 FloorPoint(float r, float a) => domePlan + along0 * (Mathf.Cos(a * Mathf.Deg2Rad) * r * radius) + side0 * (Mathf.Sin(a * Mathf.Deg2Rad) * r * radius * squash);
			for (int b = 0; b < brinePools; b++)
			{
				float r = features.Range(0.15f, 0.6f), a = features.Range(0f, 360f);
				float pr = Mathf.Min(features.Range(5f, 20f), 0.22f * radius);
				float drop = features.Range(0.5f, 2f), depth = features.Range(2.5f, 4f);
				Vector3 at = FloorPoint(r, a);
				if (Crowded(system, at, pr + 6f))
				{
					continue;
				}
				system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Bowl, A = new Vector3(at.x, floor, at.z), Radius = pr, Depth = depth, Top = floor + 0.6f });
				system.HallPools.Add(new CaveHallPool { Kind = CaveHallPoolKind.Brine, Rim = new Vector3(at.x, floor, at.z), Radius = pr, Mirror = floor - Mathf.Min(drop, depth - 0.3f) });
			}
			for (int b = 0; b < pillarPits; b++)
			{
				float r = features.Range(0.1f, 0.7f), a = features.Range(0f, 360f);
				float pr = features.Range(1.5f, 4f), depth = features.Range(2f, 3.5f);
				int pillars = features.Range(2, 6);
				Vector3 at = FloorPoint(r, a);
				float[] draws = new float[pillars * 3];
				for (int i = 0; i < draws.Length; i++)
				{
					draws[i] = features.NextFloat();
				}
				if (Crowded(system, at, pr + 4f))
				{
					continue;
				}
				float bottom = floor - depth;
				system.Hollows.Add(new CaveHollow { Kind = CaveHollowKind.Bowl, A = new Vector3(at.x, floor, at.z), Radius = pr, Depth = depth, Top = floor + 0.6f });
				system.HallPools.Add(new CaveHallPool { Kind = CaveHallPoolKind.LavaPillars, Rim = new Vector3(at.x, floor, at.z), Radius = pr, Mirror = bottom });
				for (int i = 0; i < pillars; i++)
				{
					float pa = draws[3 * i] * 360f, pd = Mathf.Sqrt(draws[3 * i + 1]) * 0.55f * pr;
					Vector3 c = at + HallDir(pa) * pd;
					float waist = Mathf.Lerp(0.35f, 0.7f, draws[3 * i + 2]);
					// From the pit's bottom to about the old crust's level: the pillars that held the roof of the drained flow.
					system.Solids.Add(new CaveSolid { Kind = CaveSolidKind.Column, A = new Vector3(c.x, bottom, c.z), B = new Vector3(c.x, floor - 0.2f, c.z), Width = waist, Depth = 0.35f });
				}
			}

			// ── The smokers ──
			{
				// The Great Smoker near the middle, its crown out of the water into the air pocket.
				float r = features.Range(0f, 0.3f), a = features.Range(0f, 360f), over = features.Range(2f, 6f), girth = features.Range(3f, 4.5f);
				Vector3 at = FloorPoint(r, a);
				float crown = Mathf.Min(water + over, centreY + ry * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) - 8f);
				if (crown - floor >= 20f && !InPool(system, at, girth + 2f))
				{
					system.Smokers.Add(new CaveSmoker { Base = new Vector3(at.x, floor, at.z), Height = crown - floor, Radius = girth, Kind = CaveSmokerKind.Black, Great = true });
				}
			}
			Vector3 whiteAt = FloorPoint(0.45f, whiteHeading);
			for (int i = 0; i < smokers; i++)
			{
				float kindRoll = features.NextFloat(), r = Mathf.Sqrt(features.NextFloat()) * 0.78f, a = features.Range(0f, 360f);
				float hRoll = features.NextFloat(), girthRoll = features.NextFloat();
				Vector3 jitter = new Vector3(features.Range(-12f, 12f), 0f, features.Range(-12f, 12f));
				CaveSmokerKind kind = kindRoll < 0.6f ? CaveSmokerKind.Black : kindRoll < 0.85f ? CaveSmokerKind.Beehive : CaveSmokerKind.White;
				Vector3 at = kind == CaveSmokerKind.White && whiteField ? whiteAt + jitter : FloorPoint(r, a);
				float h = kind == CaveSmokerKind.Black ? Mathf.Lerp(3f, 15f, hRoll * hRoll) : kind == CaveSmokerKind.White ? Mathf.Lerp(1.5f, 5f, hRoll) : Mathf.Lerp(0.6f, 1.2f, hRoll);
				float girth = kind == CaveSmokerKind.Black ? Mathf.Lerp(0.6f, 1.6f, girthRoll) : kind == CaveSmokerKind.White ? Mathf.Lerp(0.5f, 1f, girthRoll) : Mathf.Lerp(0.5f, 1f, girthRoll);
				if (!InDome(system, at, 0.85f) || InPool(system, at, girth + 2f) || NearSmoker(system, at, kind == CaveSmokerKind.Beehive ? 2.5f : 6f))
				{
					continue;
				}
				system.Smokers.Add(new CaveSmoker { Base = new Vector3(at.x, floor, at.z), Height = h, Radius = girth, Kind = kind });
			}

			system.PrepareField();
			// The stair walked from its head to the ledge (the field as cut, with every solid back): a hall nobody can get down to is no hall.
			var walk = new List<Vector3>();
			float turned = (0f - ledge) / pitch;
			int stairSteps = Mathf.Max(8, Mathf.CeilToInt(turned * midline / 2f));
			for (int i = 0; i <= stairSteps; i++)
			{
				float phi = turned * i / stairSteps;
				Vector3 at = shaft + HallDir(start + hand * phi * Mathf.Rad2Deg) * midline;
				walk.Add(new Vector3(at.x, -pitch * phi + 0.3f, at.z));
			}
			for (int i = 1; i < way.Nodes.Count; i++)
			{
				walk.Add(new Vector3(way.Nodes[i].Centre.x, ledge + 0.3f, way.Nodes[i].Centre.z));
			}
			// And on from the stair's head to its first steps, where the way in comes onto it.
			var top = new List<Vector3>();
			foreach (CaveNode n in head.Nodes)
			{
				top.Add(new Vector3(n.Centre.x, n.Floor + 0.3f, n.Centre.z));
			}
			if (!system.WalkFits(top, out string headWhy))
			{
				problem = $"its stair's head does not walk onto the stair ({headWhy})";
				return null;
			}
			if (!system.WalkFits(walk, out string why))
			{
				problem = $"its stair does not walk down to the ledge ({why})";
				return null;
			}
			system.LavaHeld = false;
			return system;
		}

		/// <summary>
		/// Lifts a way's nodes by how far the prepared field lets its floor down under each (its smooth unions), twice over, so the floor
		/// walked is the one planned: each node's own floor is its target.
		/// </summary>
		private static void LevelWay(CaveSystem system, CavePassage way)
		{
			var target = new float[way.Nodes.Count];
			for (int i = 0; i < target.Length; i++)
			{
				target[i] = way.Nodes[i].Floor;
			}
			for (int pass = 0; pass < 2; pass++)
			{
				system.PrepareField();
				var lift = new float[target.Length];
				for (int i = 0; i < target.Length; i++)
				{
					CaveNode n = way.Nodes[i];
					float found = system.FloorBelow(new Vector3(n.Centre.x, n.Floor + 1.2f, n.Centre.z), 3f);
					lift[i] = float.IsNaN(found) ? 0f : Mathf.Clamp(target[i] - found, 0f, 1.5f);
				}
				for (int i = 0; i < target.Length; i++)
				{
					CaveNode n = way.Nodes[i];
					n.Floor += lift[i];
					n.Centre.y += lift[i];
					way.Nodes[i] = n;
				}
			}
		}

		/// <summary>An ellipse's half-width along a heading (degrees) at a height share <paramref name="h"/> of its half-height from its centre.</summary>
		private static float DomeHalfWidth(float rx, float rz, float yaw, float heading, float h)
		{
			if (Mathf.Abs(h) >= 1f)
			{
				return 0f;
			}
			float a = (heading - yaw) * Mathf.Deg2Rad;
			float c = Mathf.Cos(a) / rx, s = Mathf.Sin(a) / rz;
			return Mathf.Sqrt(1f - h * h) / Mathf.Sqrt(c * c + s * s);
		}

		/// <summary>Whether a point on the floor stands within a share of the dome's floor radius (in its own ellipse).</summary>
		private static bool InDome(CaveSystem system, Vector3 p, float share)
		{
			if (!system.TryHallDome(out CaveChamber dome))
			{
				return false;
			}
			Vector3 along = HallDir(dome.Yaw), side = new Vector3(along.z, 0f, -along.x);
			var o = new Vector3(p.x - dome.Centre.x, 0f, p.z - dome.Centre.z);
			float u = Vector3.Dot(o, along) / dome.Radii.x, v = Vector3.Dot(o, side) / dome.Radii.z;
			// The floor's own edge: the ellipsoid's width at the floor, under its widest.
			float atFloor = (dome.Floor - dome.Centre.y) / dome.Radii.y;
			float edge = Mathf.Sqrt(Mathf.Max(0f, 1f - atFloor * atFloor));
			return u * u + v * v <= share * share * edge * edge;
		}

		private static bool InPool(CaveSystem system, Vector3 p, float margin)
		{
			foreach (CaveHallPool pool in system.HallPools)
			{
				if (new Vector2(p.x - pool.Rim.x, p.z - pool.Rim.z).magnitude < pool.Radius + margin)
				{
					return true;
				}
			}
			return false;
		}

		private static bool NearSmoker(CaveSystem system, Vector3 p, float clear)
		{
			foreach (CaveSmoker s in system.Smokers)
			{
				if (new Vector2(p.x - s.Base.x, p.z - s.Base.z).magnitude < clear + s.Radius)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether a basin here would crowd another, or leave the dome's floor.</summary>
		private static bool Crowded(CaveSystem system, Vector3 p, float clear) => !InDome(system, p, 0.8f) || InPool(system, p, clear) || NearSmoker(system, p, clear);

		/// <summary>A sea hall's own values, after everything every cave stores (nothing for any other cave).</summary>
		private void WriteSeaHall(List<float> v)
		{
			if (Form != CaveForm.SeaHall)
			{
				return;
			}
			v.Add(SeaHallTag);
			v.Add(Smokers.Count);
			v.Add(HallPools.Count);
			foreach (CaveSmoker s in Smokers)
			{
				v.AddRange(new[] { s.Base.x, s.Base.y, s.Base.z, s.Height, s.Radius, (float)s.Kind, s.Great ? 1f : 0f });
			}
			foreach (CaveHallPool p in HallPools)
			{
				v.AddRange(new[] { (float)p.Kind, p.Rim.x, p.Rim.y, p.Rim.z, p.Radius, p.Mirror });
			}
		}

		/// <summary>Reads what <see cref="WriteSeaHall"/> wrote at <paramref name="o"/>, where it did; the offset past it (−1 where the values run short).</summary>
		private int ReadSeaHall(float[] v, int o)
		{
			if (o < 0 || o >= v.Length || v[o] != SeaHallTag)
			{
				return o;
			}
			if (o + 3 > v.Length)
			{
				return -1;
			}
			int smokers = Mathf.RoundToInt(v[o + 1]), pools = Mathf.RoundToInt(v[o + 2]);
			o += 3;
			if (smokers < 0 || pools < 0 || o + 7 * smokers + 6 * pools > v.Length)
			{
				return -1;
			}
			for (int i = 0; i < smokers; i++, o += 7)
			{
				Smokers.Add(new CaveSmoker
				{
					Base = new Vector3(v[o], v[o + 1], v[o + 2]), Height = v[o + 3], Radius = v[o + 4], Kind = (CaveSmokerKind)Mathf.RoundToInt(v[o + 5]), Great = v[o + 6] > 0.5f,
				});
			}
			for (int i = 0; i < pools; i++, o += 6)
			{
				HallPools.Add(new CaveHallPool { Kind = (CaveHallPoolKind)Mathf.RoundToInt(v[o]), Rim = new Vector3(v[o + 1], v[o + 2], v[o + 3]), Radius = v[o + 4], Mirror = v[o + 5] });
			}
			return o;
		}
	}
}
#endif
