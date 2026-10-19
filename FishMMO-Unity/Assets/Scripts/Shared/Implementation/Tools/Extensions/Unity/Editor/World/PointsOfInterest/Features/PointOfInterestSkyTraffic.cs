#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A cloud world's sky traffic (2026-10-14: "flying ships"): a timber landing stage at each of its docks, lanes through
	/// the air between them and out past the scene's edge, and the skiffs and sky-cogs that ply them, moved by
	/// <see cref="FloatingBoats"/> from the synced tick on the server and every client alike, solid, with a deck a player
	/// can ride, exactly as the sea's ships are (<see cref="PointOfInterestSeaTraffic"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The stages</b> are baked props (source <see cref="PropSource"/>): each runs from 2 m over its island's ground out
	/// to the dock's end over the void, facing its Outward, its walkway in the collision the NavMesh is baked from.
	/// </para>
	/// <para>
	/// <b>The lanes</b> (<see cref="SkyLanePlanner"/>) keep clear of every island's rock, its top (with its trees and
	/// towers) and its underside down to its keel, by margins. A ship berths bow-on to a stage, its deck level with the
	/// stage's and its boarding plank's tip <see cref="SkyLanePlanner.BerthGap"/> off the stage's end; it comes in and
	/// leaves straight along the stage's line, level, and climbs or descends to its cruising height once clear of its
	/// island. At the stage it lies still while its passengers come and go and swings end for end only at the last
	/// (<see cref="FloatingBoats.Lane.BerthSwing"/>). Where a lane runs out of the scene the ship waits past the edge,
	/// hidden, as a ship at sea does.
	/// </para>
	/// <para>Seeded by the scene's name: a re-cut flies the same ships on the same lanes.</para>
	/// </remarks>
	public static class PointOfInterestSkyTraffic
	{
		public const string ObjectName = "Sky Traffic";

		/// <summary>The baked props' source: the landing stages.</summary>
		public const string PropSource = "Sky Docks";

		/// <summary>The stage piece, and the airships.</summary>
		public const string DockPiece = "SkyDock", SkiffPiece = "SkySkiff", CogPiece = "SkyCog";

		private const int MaxLanes = 10;
		private const int MaxShips = 14;
		/// <summary>The farthest apart two docks are joined by a lane, metres.</summary>
		private const float PairReach = 7000f;
		/// <summary>How much higher a lane's second ship flies over the open air than its first, metres: they pass one over the other.</summary>
		private const float SecondShipLift = 34f;

		/// <remarks>
		/// <paramref name="palette"/>: the structure palette the airships and landing stages are laid in (2026-10-16): a cloud
		/// world's own, from its world body (it stands in no biome; CloudWorldStructures, 2026-10-18), null for the kit's own
		/// materials. <paramref name="structures"/>: the body's settings, for the light beside each stage's landward end.
		/// </remarks>
		public static void Build(Scene scene, Transform root, SceneCloudWorld world, SceneGenerationRequest request, float width, float depth,
			Func<float, float, float> ground, List<string> notes, FishMMO.Shared.Biomes.BiomeStructurePalette palette = null,
			FishMMO.Shared.Celestial.CloudWorldStructures structures = null)
		{
			if (root == null || world == null || request == null)
			{
				return;
			}
			for (int i = root.childCount - 1; i >= 0; i--)
			{
				if (root.GetChild(i).name == ObjectName)
				{
					UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
				}
			}
			var random = new DeterministicRNG(StableHash(request.SceneName) ^ 0x5C1A1F);
			SceneCloudWorld.Island[] islands = world.Islands ?? Array.Empty<SceneCloudWorld.Island>();
			SceneCloudWorld.Dock[] docks = world.Docks ?? Array.Empty<SceneCloudWorld.Dock>();

			int stages = BakeStages(scene, docks, notes, out int collidable, palette, StageLight(structures), world, ground);

			var planner = new SkyLanePlanner(Islands(islands, ground), width, depth);
			var kinds = new List<string>();
			List<FloatingBoats.Lane> lanes = PlanLanes(planner, docks, world.DeckTop, world.IslandFloor, world.IslandCeiling, width, depth, random, kinds);

			if (lanes.Count == 0)
			{
				notes?.Add($"Sky traffic: {stages} landing stage(s) ({collidable} collidable), no airship lane found among {islands.Length} island(s).");
				return;
			}

			/* Kept by the server, as the sea's ships are: the lanes are what every peer works a ship's pose out from, and
			 * each ship's root carries its hull's collider. Only the drawn hulls under the roots are client-only. */
			var holder = new GameObject(ObjectName);
			holder.transform.SetParent(root, false);
			var ships = new List<FloatingBoats.Sailing>();
			var flown = new List<FloatingBoats.Lane>();
			int missing = 0;
			for (int l = 0; l < lanes.Count && ships.Count < MaxShips; l++)
			{
				string piece = kinds[l];
				GameObject sails = StructureKit.Prefab(piece, 0), moored = StructureKit.Prefab(piece, 1);
				if (sails == null || !StructurePieces.TryAirship(piece, out float length, out _, out _))
				{
					missing++;
					continue;
				}
				FloatingBoats.Lane lane = lanes[l];
				float speed = (piece == SkiffPiece ? 7f : 5.2f) * random.Range(0.9f, 1.1f);
				float dwellStart = lane.PortAtStart ? random.Range(200f, 420f) : random.Range(300f, 900f);
				float dwellEnd = lane.PortAtEnd ? random.Range(200f, 420f) : random.Range(300f, 900f);
				/* A long lane takes a second ship half a round behind the first, on a copy of the lane flying higher over the
				 * open air. Its legs take exactly as long as the first's (its speed scaled to its longer way) and both waits
				 * are alike, so the two never drift: they pass each other at the middle of the way, one over the other, and
				 * each is at one stage while the other is at the far one. */
				FloatingBoats.Lane higher = lane.Length > 1400f ? planner.Raised(lane, SecondShipLift) : null;
				int count = higher != null ? 2 : 1;
				if (count == 2)
				{
					dwellStart = dwellEnd = 0.5f * (dwellStart + dwellEnd);
				}
				float period = (float)FloatingBoats.RoundSeconds(lane, new FloatingBoats.Sailing { Speed = speed, DwellStart = dwellStart, DwellEnd = dwellEnd });
				float offset = period * random.NextFloat();
				for (int k = 0; k < count && ships.Count < MaxShips; k++)
				{
					FloatingBoats.Lane way = k == 0 ? lane : higher;
					int index = flown.IndexOf(way);
					if (index < 0)
					{
						index = flown.Count;
						flown.Add(way);
					}
					var ship = new FloatingBoats.Sailing
					{
						Lane = index,
						Speed = speed * way.Length / lane.Length,
						DwellStart = dwellStart,
						DwellEnd = dwellEnd,
						Length = length,
						Phase = random.NextFloat(),
						Offset = offset + 0.5f * period * k,
					};
					Vector3 start = way.Points[0];
					// The kit's own materials (a cloud world has no biome to give a palette): palette-ready through Moor and Spawn.
					ship.Root = PointOfInterestHarbour.Moor(sails, piece, 0, holder.transform, start, Quaternion.identity, out ship.Ship, palette);
					if (moored != null && moored != sails)
					{
						ship.Furled = PointOfInterestHarbour.Spawn(moored, piece, 1, ship.Root, start, Quaternion.identity, palette);
						ship.Furled.gameObject.AddComponent<ClientOnlyObject>();
						ship.Furled.gameObject.SetActive(false);
					}
					if (ship.Root.TryGetComponent(out MeshCollider hull) && hull.sharedMesh != null)
					{
						// Where a rider can stand. No NavMesh obstacle: nothing that walks the NavMesh meets a ship in the air.
						ship.Deck = hull.sharedMesh.bounds;
					}
					ships.Add(ship);
				}
			}
			if (ships.Count == 0)
			{
				UnityEngine.Object.DestroyImmediate(holder);
				notes?.Add($"Sky traffic: {stages} landing stage(s), {lanes.Count} lane(s) planned but no airship placed: their art is missing (run Generate biome art, then re-cut).");
				return;
			}
			holder.AddComponent<FloatingBoats>().Configure(null, flown.ToArray(), ships.ToArray(), true);
			float total = 0f;
			int ported = 0;
			foreach (FloatingBoats.Lane lane in lanes)
			{
				total += lane.Length;
				ported += (lane.PortAtStart ? 1 : 0) + (lane.PortAtEnd ? 1 : 0);
			}
			notes?.Add($"Sky traffic: {stages} landing stage(s) ({collidable} collidable), {lanes.Count} lane(s), {total / 1000f:0.0} km, {ported} berth(s), " +
				$"{ships.Count} airship(s) under way{(missing > 0 ? $"; {missing} lane(s) without a ship, their art is missing (run Generate biome art)" : string.Empty)}.");
		}

		/// <summary>
		/// A lane from a stage out to the scene's edge, ending ON the edge, in sight, where the ship hangs, turns end for end
		/// and comes back, rather than past it: a ship hidden past the edge drops whoever is still aboard into the cloud,
		/// which on a cloud world is death (a sea's ships may wait out of sight; a swimmer only gets wet). Null stays null.
		/// </summary>
		private static FloatingBoats.Lane TurnAtEdge(FloatingBoats.Lane lane)
		{
			if (lane == null || lane.Points.Length < 3 || lane.Along.Length != lane.Points.Length)
			{
				return lane;
			}
			// The last point is the wait past the edge; the one before it is on the edge.
			int n = lane.Points.Length - 1;
			var turned = new FloatingBoats.Lane
			{
				Points = new Vector3[n],
				Along = new float[n],
				PortAtStart = lane.PortAtStart,
				PortAtEnd = true,
				BerthSwing = lane.BerthSwing,
			};
			Array.Copy(lane.Points, turned.Points, n);
			Array.Copy(lane.Along, turned.Along, n);
			return turned;
		}

		/// <summary>
		/// The landing stages as baked props: each piece's origin at its walkway's middle, its +z along the dock's Outward, its
		/// outer end at the dock's end. Clears the source when there are none. Returns how many were placed.
		/// </summary>
		private static int BakeStages(Scene scene, SceneCloudWorld.Dock[] docks, List<string> notes, out int collidable, FishMMO.Shared.Biomes.BiomeStructurePalette palette = null,
			string lightQuery = null, SceneCloudWorld world = null, Func<float, float, float> ground = null)
		{
			collidable = 0;
			var prototypes = new List<ScenePropSet.Prototype>();
			var props = new List<ScenePropSet.Prop>();
			var prototypeOf = new Dictionary<GameObject, int>();
			int missing = 0, lights = 0, unlit = 0;
			int stagesLaid = 0;
			void Add(GameObject prefab, Vector3 at, Quaternion rotation)
			{
				if (!prototypeOf.TryGetValue(prefab, out int index))
				{
					index = prototypes.Count;
					prototypeOf[prefab] = index;
					prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1, Materials = StructurePaletteDressing.MaterialsFor(prefab, palette) });
				}
				props.Add(new ScenePropSet.Prop { Prototype = index, Position = at, Rotation = rotation, Scale = Vector3.one });
			}
			for (int i = 0; i < docks.Length; i++)
			{
				SceneCloudWorld.Dock dock = docks[i];
				Vector3 outward = new Vector3(dock.Outward.x, 0f, dock.Outward.z);
				if (outward.sqrMagnitude < 1e-6f)
				{
					continue;
				}
				outward.Normalize();
				GameObject prefab = StructureKit.Prefab(DockPiece, (StableHash($"{dock.Position.x:0}:{dock.Position.z:0}") & 0x7fffffff) % 2);
				if (prefab == null)
				{
					missing++;
					continue;
				}
				Add(prefab, dock.Position - outward * (StructurePieces.SkyDockLength * 0.5f), Quaternion.LookRotation(outward, Vector3.up));
				stagesLaid++;
				// The light beside its landward end, where the world body names one (2026-10-18, I6e).
				if (lightQuery != null)
				{
					if (StageLight(dock, outward, world, ground, out Vector3 at))
					{
						GameObject light = PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest(lightQuery, "timber", (int)(StableHash($"light:{at.x:0}:{at.z:0}") & 0x7fffffff)));
						if (light != null)
						{
							Add(light, at, Quaternion.LookRotation(outward, Vector3.up));
							lights++;
						}
						else
						{
							missing++;
						}
					}
					else
					{
						unlit++;
					}
				}
			}
			collidable = ScenePropBaker.Write(scene, PropSource, prototypes, props);
			if (missing > 0)
			{
				notes?.Add($"Sky traffic: {missing} landing stage piece(s) skipped, their art is missing (run Generate biome art, then re-cut).");
			}
			if (lightQuery != null)
			{
				notes?.Add($"Sky traffic: {lights} landing-stage light(s){(unlit > 0 ? $", {unlit} stage(s) with no island ground beside its end for one" : string.Empty)}.");
			}
			return stagesLaid;
		}

		/// <summary>The kit piece query of the light a body names for its landing stages, standing on the ground; null for none.</summary>
		private static string StageLight(FishMMO.Shared.Celestial.CloudWorldStructures structures)
		{
			return structures != null && structures.TryLight(FishMMO.Shared.Celestial.CloudWorldWayPlace.LandingStage, out FishMMO.Shared.Celestial.CloudWorldWayLight light)
				&& FishMMO.Shared.Biomes.BiomeWayLights.MountOf(light.Light) == FishMMO.Shared.Biomes.BiomeWayLightMount.Ground ? light.PieceQuery : null;
		}

		/// <summary>How far inland of a stage's landward end its light stands, and how far to the side of its walkway, metres.</summary>
		public const float StageLightInland = 1f, StageLightAside = 0.8f;

		/// <summary>
		/// Where a landing stage's light stands (2026-10-18, I6e): beside its landward end on the side away from its mast (+x of
		/// the stage), a metre inland of the end, on its island's ground (the record's island the dock is on). False off the island.
		/// </summary>
		public static bool StageLight(SceneCloudWorld.Dock dock, Vector3 outward, SceneCloudWorld world, Func<float, float, float> ground, out Vector3 at)
		{
			var way = new Vector3(outward.x, 0f, outward.z).normalized;
			var right = new Vector3(way.z, 0f, -way.x);
			Vector3 p = dock.Position - way * (StructurePieces.SkyDockLength + StageLightInland) + right * (StructurePieces.SkyDockWidth * 0.5f + StageLightAside);
			at = new Vector3(p.x, ground != null ? ground(p.x, p.z) : dock.Position.y, p.z);
			return world == null || world.IslandAt(p.x, p.z) == dock.Island;
		}

		/// <summary>The islands as the planner sees them: each one's top found from its ground (the highest of its outline's reach).</summary>
		/// <summary>
		/// The lanes between the stages, out to the scene's edge and across it, and the kind of airship for each (into
		/// <paramref name="kinds"/>, lane by lane): pure, so a test can lay them over any islands.
		/// </summary>
		public static List<FloatingBoats.Lane> PlanLanes(SkyLanePlanner planner, SceneCloudWorld.Dock[] docks, float deckTop, float islandFloor, float islandCeiling,
			float width, float depth, DeterministicRNG random, List<string> kinds)
		{
			var lanes = new List<FloatingBoats.Lane>();
			var landed = new bool[docks.Length];

			/* Each dock to its nearest free dock on another island. A stage serves one lane, so its berth never holds two
			 * ships at once: a lane's own two ships are timed half a round apart (below). */
			for (int i = 0; i < docks.Length && lanes.Count < MaxLanes; i++)
			{
				if (landed[i])
				{
					continue;
				}
				int best = -1;
				float nearest = PairReach;
				for (int j = 0; j < docks.Length; j++)
				{
					if (j == i || landed[j] || docks[j].Island == docks[i].Island)
					{
						continue;
					}
					float dist = Vector3.Distance(docks[i].Position, docks[j].Position);
					if (dist < nearest)
					{
						nearest = dist;
						best = j;
					}
				}
				if (best < 0)
				{
					continue;
				}
				string kind = nearest < 1500f && random.NextFloat() < 0.6f ? SkiffPiece : CogPiece;
				if (!TryReach(kind, out float bowReach)
					|| !planner.TryPort(Dock(docks[i]), bowReach, out SkyLanePlanner.End a)
					|| !planner.TryPort(Dock(docks[best]), bowReach, out SkyLanePlanner.End b))
				{
					continue;
				}
				float cruise = Mathf.Max(a.Berth.y, b.Berth.y) + Lift(lanes.Count);
				FloatingBoats.Lane lane = planner.Plan(a, b, cruise);
				if (lane != null)
				{
					lanes.Add(lane);
					kinds.Add(kind);
					landed[i] = landed[best] = true;
				}
			}
			// Every dock no lane reached, out to the scene's edge: ships leaving the scene, and coming in from past it.
			for (int i = 0; i < docks.Length && lanes.Count < MaxLanes; i++)
			{
				if (landed[i])
				{
					continue;
				}
				string kind = random.NextFloat() < 0.7f ? CogPiece : SkiffPiece;
				if (!TryReach(kind, out float bowReach))
				{
					continue;
				}
				if (!planner.TryPort(Dock(docks[i]), bowReach, out SkyLanePlanner.End a))
				{
					// A scene too small to clear the island and turn before the edge: straight out to it and back, level.
					FloatingBoats.Lane straight = TurnAtEdge(planner.StraightOut(Dock(docks[i]), bowReach));
					if (straight != null)
					{
						lanes.Add(straight);
						kinds.Add(kind);
						landed[i] = true;
					}
					continue;
				}
				float cruise = a.Berth.y + Lift(lanes.Count);
				Vector2 outward = new Vector2(docks[i].Outward.x, docks[i].Outward.z);
				float turn = random.Range(-25f, 25f) * Mathf.Deg2Rad;
				outward = new Vector2(outward.x * Mathf.Cos(turn) - outward.y * Mathf.Sin(turn), outward.x * Mathf.Sin(turn) + outward.y * Mathf.Cos(turn));
				if (!planner.TryEdge(new Vector2(a.Lead.x, a.Lead.z), outward, a.Berth.y, cruise, out SkyLanePlanner.End b))
				{
					continue;
				}
				FloatingBoats.Lane lane = TurnAtEdge(planner.Plan(a, b, cruise));
				if (lane != null)
				{
					lanes.Add(lane);
					kinds.Add(kind);
					landed[i] = true;
				}
			}
			// Across the scene, edge to edge, through the islands' heights.
			int crossings = Mathf.Max(width, depth) >= 3000f ? 2 : Mathf.Max(width, depth) >= 1500f ? 1 : 0;
			float low = Mathf.Max(deckTop + 120f, islandFloor - 40f), high = Mathf.Max(low + 1f, islandCeiling + 120f);
			for (int attempt = 0; attempt < crossings * 6 && crossings > 0 && lanes.Count < MaxLanes; attempt++)
			{
				// A line through a point near the middle, at a height somewhere among the islands'.
				float altitude = random.Range(low, high);
				float bearing = random.Range(0f, Mathf.PI * 2f);
				var heading = new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing));
				var through = new Vector2(random.Range(-0.3f, 0.3f) * width, random.Range(-0.3f, 0.3f) * depth);
				if (!planner.TryEdge(through, -heading, altitude, altitude, out SkyLanePlanner.End a)
					|| !planner.TryEdge(through, heading, altitude, altitude, out SkyLanePlanner.End b))
				{
					continue;
				}
				FloatingBoats.Lane lane = planner.Plan(a, b, altitude);
				if (lane != null && lane.Length > 0.4f * Mathf.Max(width, depth))
				{
					lanes.Add(lane);
					kinds.Add(CogPiece);
					crossings--;
				}
			}

			return lanes;
		}

		public static SkyLanePlanner.Island[] Islands(SceneCloudWorld.Island[] islands, Func<float, float, float> ground)
		{
			var result = new SkyLanePlanner.Island[islands.Length];
			for (int i = 0; i < islands.Length; i++)
			{
				SceneCloudWorld.Island island = islands[i];
				float top = island.Centre.y;
				if (ground != null)
				{
					float step = Mathf.Max(6f, island.Radius / 14f);
					float reach = island.Radius * 0.95f;
					for (float dz = -reach; dz <= reach; dz += step)
					{
						for (float dx = -reach; dx <= reach; dx += step)
						{
							if (dx * dx + dz * dz > reach * reach)
							{
								continue;
							}
							float h = ground(island.Centre.x + dx, island.Centre.z + dz);
							if (!float.IsNaN(h) && !float.IsInfinity(h))
							{
								top = Mathf.Max(top, h);
							}
						}
					}
				}
				result[i] = new SkyLanePlanner.Island
				{
					Centre = new Vector2(island.Centre.x, island.Centre.z),
					Radius = island.Radius,
					Top = top,
					Keel = island.Keel,
				};
			}
			return result;
		}

		private static SkyLanePlanner.Dock Dock(SceneCloudWorld.Dock dock)
			=> new SkyLanePlanner.Dock { Island = dock.Island, Position = dock.Position, Outward = new Vector2(dock.Outward.x, dock.Outward.z) };

		private static bool TryReach(string piece, out float bowReach) => StructurePieces.TryAirship(piece, out _, out _, out bowReach);

		/// <summary>How high over its higher end a lane cruises: each lane a little higher than the last, so lanes that cross pass apart.</summary>
		private static float Lift(int lane) => 40f + (lane % 4) * 16f;

		private static int StableHash(string text)
		{
			unchecked
			{
				uint h = 2166136261u;
				foreach (char c in text ?? string.Empty)
				{
					h = (h ^ c) * 16777619u;
				}
				return (int)h;
			}
		}
	}

	/// <summary>
	/// Lanes through a cloud world's air (pure: islands in, lanes out), for <see cref="PointOfInterestSkyTraffic"/> and its tests.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What a lane keeps clear of.</b> Each island is a column: its outline's reach (<see cref="Island.Radius"/>) widened
	/// by <see cref="Margin"/>, from <see cref="Below"/> under its keel to <see cref="Above"/> over its highest ground. A
	/// lane's height stays between its ends' heights and its cruising height, so only the islands whose column reaches
	/// into that band stand in its way; it flies over or under the rest.
	/// </para>
	/// <para>
	/// <b>Its shape.</b> A port end is the ship's berth (its root, bow-on to the stage), then a level run straight out along
	/// the stage's line until the ship is clear of its own island's column (<see cref="End.Exit"/>), then on a little
	/// further (<see cref="End.Lead"/>) before it turns. Between the two ends' leads the way is the cheapest round the
	/// columns in the way (A* on a grid), pulled straight where the air allows and its corners rounded; its height climbs
	/// from the ends' to the cruising height (at most <see cref="Climb"/>, eased), and back down to the far end's.
	/// </para>
	/// </remarks>
	public sealed class SkyLanePlanner
	{
		/// <summary>One island: its outline's centre and reach, its highest ground and its keel's depth (y).</summary>
		public struct Island
		{
			public Vector2 Centre;
			public float Radius, Top, Keel;
		}

		/// <summary>A dock: its island, the stage's end (at its deck), and the way out over the void.</summary>
		public struct Dock
		{
			public int Island;
			public Vector3 Position;
			public Vector2 Outward;
		}

		/// <summary>One end of a lane, at a stage or past the scene's edge.</summary>
		public struct End
		{
			public bool Port;
			/// <summary>The dock's island (a port), else −1.</summary>
			public int Island;
			/// <summary>A port: where the ship's root lies at the stage. The scene's edge: where it waits, past it.</summary>
			public Vector3 Berth;
			/// <summary>A port: where the level run out ends, clear of its island. The scene's edge: on it.</summary>
			public Vector3 Exit;
			/// <summary>On along the same line from <see cref="Exit"/>: where the lane may first turn.</summary>
			public Vector3 Lead;
		}

		/// <summary>The grid the way round the islands is searched on, metres.</summary>
		public const float Cell = 20f;
		/// <summary>How far a lane keeps outside an island's outline's reach, metres: half a sky-cog, its fins, overhanging trees, and room.</summary>
		public const float Margin = 45f;
		/// <summary>How far over an island's highest ground a lane passing over it keeps, metres: trees, towers, and the hull under the deck.</summary>
		public const float Above = 50f;
		/// <summary>How far under an island's keel a lane passing under it keeps, metres: the gasbag over the deck, and room.</summary>
		public const float Below = 35f;
		/// <summary>How far a berthed ship's boarding plank's tip lies off the stage's end, metres: a step across.</summary>
		public const float BerthGap = 0.35f;
		/// <summary>The shortest level run in or out of a stage, metres.</summary>
		public const float MinApproach = 60f;
		/// <summary>The steepest a lane climbs over its cruise, rise over run (eased in and out, so half again at its steepest).</summary>
		public const float Climb = 0.16f;
		/// <summary>The steepest a lane may climb at all, when its ends are far apart in height and near in distance.</summary>
		public const float SteepestClimb = 0.4f;
		/// <summary>How far past the scene's edge a ship waits out of sight, metres.</summary>
		public const float Beyond = 60f;
		/// <summary>How long a ship turns end for end at its stage before it leaves, seconds (<see cref="FloatingBoats.Lane.BerthSwing"/>).</summary>
		public const float BerthSwingSeconds = 40f;
		/// <summary>The longest straight run between a lane's points, metres, so its heights follow their profile.</summary>
		public const float Spacing = 25f;

		private readonly Island[] islands;
		private readonly float halfWidth, halfDepth;
		private readonly int w, d;
		private readonly float x0, z0;

		public SkyLanePlanner(Island[] islands, float width, float depth)
		{
			this.islands = islands ?? Array.Empty<Island>();
			halfWidth = width * 0.5f;
			halfDepth = depth * 0.5f;
			w = Mathf.Max(2, Mathf.FloorToInt(width / Cell));
			d = Mathf.Max(2, Mathf.FloorToInt(depth / Cell));
			x0 = -w * Cell * 0.5f;
			z0 = -d * Cell * 0.5f;
		}

		/// <summary>Whether island <paramref name="i"/>'s column reaches into the heights from <paramref name="low"/> to <paramref name="high"/>.</summary>
		public bool Blocks(int i, float low, float high) => islands[i].Keel - Below < high && islands[i].Top + Above > low;

		/// <summary>Whether a point is inside any island's column (but <paramref name="except"/>'s), at its own height.</summary>
		public bool Inside(Vector3 p, int except = -1, float share = 1f)
		{
			for (int i = 0; i < islands.Length; i++)
			{
				if (i == except)
				{
					continue;
				}
				Island island = islands[i];
				float reach = (island.Radius + Margin) * share;
				if ((new Vector2(p.x, p.z) - island.Centre).sqrMagnitude < reach * reach
					&& p.y > island.Keel - Below * share && p.y < island.Top + Above * share)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether a straight run between two points (each at its own height) stays out of every column but <paramref name="except"/>'s.</summary>
		public bool Clear(Vector3 a, Vector3 b, int except = -1, float share = 1f)
		{
			int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 4f));
			for (int s = 0; s <= steps; s++)
			{
				if (Inside(Vector3.Lerp(a, b, s / (float)steps), except, share))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Whether a point is in the scene with <paramref name="room"/> metres to spare.</summary>
		private bool InScene(Vector2 p, float room) => Mathf.Abs(p.x) <= halfWidth - room && Mathf.Abs(p.y) <= halfDepth - room;

		/// <summary>
		/// A port end at <paramref name="dock"/> for a ship whose plank reaches <paramref name="bowReach"/> past its origin: its
		/// berth, and the level run out along the stage's line until clear of its island. False when that run meets another
		/// island or leaves the scene first.
		/// </summary>
		public bool TryPort(Dock dock, float bowReach, out End end)
		{
			end = default;
			Vector2 outward = dock.Outward;
			if (outward.sqrMagnitude < 1e-6f || dock.Island < 0 || dock.Island >= islands.Length)
			{
				return false;
			}
			outward.Normalize();
			float y = dock.Position.y;
			var berth = new Vector2(dock.Position.x, dock.Position.z) + outward * (bowReach + BerthGap);
			Island own = islands[dock.Island];
			float clear = own.Radius + Margin + 2f * Cell;
			float run = MinApproach;
			while ((berth + outward * run - own.Centre).magnitude < clear)
			{
				run += 5f;
				if (run > 2f * clear + 200f)
				{
					return false;
				}
			}
			Vector2 exit = berth + outward * run, lead = exit + outward * (3f * Cell);
			if (!InScene(lead, Cell))
			{
				return false;
			}
			var berth3 = new Vector3(berth.x, y, berth.y);
			var exit3 = new Vector3(exit.x, y, exit.y);
			var lead3 = new Vector3(lead.x, y, lead.y);
			// Out of every other island's column all the way, and out of its own once past the exit.
			if (!Clear(berth3, exit3, dock.Island) || !Clear(exit3, lead3))
			{
				return false;
			}
			end = new End { Port = true, Island = dock.Island, Berth = berth3, Exit = exit3, Lead = lead3 };
			return true;
		}

		/// <summary>
		/// A lane straight out from a stage to the scene's edge, level at the stage's height, ending past the edge as an edge
		/// lane does: for a scene too small for a ship to clear its island and still turn before the edge, where
		/// <see cref="TryPort"/>'s level run would reach past it. Null when the way is short or another island stands in it.
		/// </summary>
		public FloatingBoats.Lane StraightOut(Dock dock, float bowReach)
		{
			Vector2 outward = dock.Outward;
			if (outward.sqrMagnitude < 1e-6f || dock.Island < 0 || dock.Island >= islands.Length)
			{
				return null;
			}
			outward.Normalize();
			float y = dock.Position.y;
			Vector2 berth = new Vector2(dock.Position.x, dock.Position.z) + outward * (bowReach + BerthGap);
			if (!InScene(berth, Cell))
			{
				return null;
			}
			float hx = halfWidth - Cell, hz = halfDepth - Cell;
			float tx = Mathf.Abs(outward.x) > 1e-5f ? ((outward.x > 0f ? hx : -hx) - berth.x) / outward.x : float.PositiveInfinity;
			float tz = Mathf.Abs(outward.y) > 1e-5f ? ((outward.y > 0f ? hz : -hz) - berth.y) / outward.y : float.PositiveInfinity;
			float run = Mathf.Min(tx, tz);
			if (!(run >= MinApproach) || float.IsInfinity(run))
			{
				return null;
			}
			var berth3 = new Vector3(berth.x, y, berth.y);
			Vector2 edge = berth + outward * run;
			var edge3 = new Vector3(edge.x, y, edge.y);
			// Away from its own island all the way, so only the others can stand in it.
			if (!Clear(berth3, edge3, dock.Island))
			{
				return null;
			}
			var points = new List<Vector3>();
			int pieces = Mathf.Max(1, Mathf.CeilToInt(run / Spacing));
			for (int k = 0; k <= pieces; k++)
			{
				points.Add(Vector3.Lerp(berth3, edge3, k / (float)pieces));
			}
			Vector2 past = edge + outward * (Cell + Beyond);
			points.Add(new Vector3(past.x, y, past.y));
			return Measure(points, true, false);
		}

		/// <summary>
		/// An end at the scene's edge, the way <paramref name="heading"/> from <paramref name="from"/>: the edge's point on that
		/// ray (moved along the edge to the nearest point clear at <paramref name="altitude"/> if needed), a lead a little
		/// inside it, and the waiting place past it. <paramref name="low"/>..<paramref name="altitude"/> is the band the lane
		/// flies in. False when no clear point is near.
		/// </summary>
		public bool TryEdge(Vector2 from, Vector2 heading, float low, float altitude, out End end)
		{
			end = default;
			if (heading.sqrMagnitude < 1e-6f)
			{
				return false;
			}
			heading.Normalize();
			// Where the ray leaves the scene (a margin of a cell inside the edge).
			float hx = halfWidth - Cell, hz = halfDepth - Cell;
			float tx = Mathf.Abs(heading.x) > 1e-5f ? ((heading.x > 0f ? hx : -hx) - from.x) / heading.x : float.PositiveInfinity;
			float tz = Mathf.Abs(heading.y) > 1e-5f ? ((heading.y > 0f ? hz : -hz) - from.y) / heading.y : float.PositiveInfinity;
			float t = Mathf.Min(tx, tz);
			if (!(t > 0f) || float.IsInfinity(t))
			{
				return false;
			}
			Vector2 edge = from + heading * t;
			edge.x = Mathf.Clamp(edge.x, -hx, hx);
			edge.y = Mathf.Clamp(edge.y, -hz, hz);
			// The edge's outward normal, and the way along it.
			Vector2 normal = tx <= tz ? new Vector2(Mathf.Sign(heading.x), 0f) : new Vector2(0f, Mathf.Sign(heading.y));
			Vector2 along = new Vector2(-normal.y, normal.x);
			for (int k = 0; k < 40; k++)
			{
				float slide = (k % 2 == 0 ? 1f : -1f) * ((k + 1) / 2) * Cell * 2f;
				Vector2 at = edge + along * slide;
				if (Mathf.Abs(at.x) > hx || Mathf.Abs(at.y) > hz)
				{
					continue;
				}
				Vector2 lead = at - normal * (3f * Cell);
				var exit3 = new Vector3(at.x, altitude, at.y);
				var lead3 = new Vector3(lead.x, altitude, lead.y);
				if (!InScene(lead, Cell) || BlockedAt(lead, low, altitude) || BlockedAt(at, low, altitude) || !Clear(exit3, lead3))
				{
					continue;
				}
				Vector2 past = at + normal * (Cell + Beyond);
				end = new End { Port = false, Island = -1, Berth = new Vector3(past.x, altitude, past.y), Exit = exit3, Lead = lead3 };
				return true;
			}
			return false;
		}

		/// <summary>Whether a ground point is inside the reach of any island blocking the band.</summary>
		private bool BlockedAt(Vector2 p, float low, float high)
		{
			for (int i = 0; i < islands.Length; i++)
			{
				float reach = islands[i].Radius + Margin;
				if (Blocks(i, low, high) && (p - islands[i].Centre).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The lane from <paramref name="a"/> to <paramref name="b"/> cruising at <paramref name="cruise"/> (lowered to what the
		/// way's length allows at <see cref="Climb"/>), or null when there is no way through or it would climb steeper than
		/// <see cref="SteepestClimb"/>. Its points carry their heights; its distances are measured along them.
		/// </summary>
		public FloatingBoats.Lane Plan(End a, End b, float cruise)
		{
			float yA = a.Port ? a.Berth.y : cruise, yB = b.Port ? b.Berth.y : cruise;
			float low = Mathf.Min(yA, yB) - 1f, high = Mathf.Max(cruise, Mathf.Max(yA, yB)) + 1f;
			List<Vector2> path = Route(new Vector2(a.Lead.x, a.Lead.z), new Vector2(b.Lead.x, b.Lead.z), low, high);
			if (path == null)
			{
				return null;
			}
			// The way: exit, lead, the route between the leads pulled straight and rounded, lead, exit.
			var way = new List<Vector2> { new Vector2(a.Exit.x, a.Exit.z) };
			way.AddRange(Pull(path, low, high));
			way.Add(new Vector2(b.Exit.x, b.Exit.z));
			way = Round(way, low, high);

			// Its heights: level at each port's height out of the exit, eased up to the cruise and down again.
			var distance = new float[way.Count];
			for (int i = 1; i < way.Count; i++)
			{
				distance[i] = distance[i - 1] + Vector2.Distance(way[i - 1], way[i]);
			}
			float total = distance[way.Count - 1];
			if (total < 1f)
			{
				return null;
			}
			// An end at the scene's edge is at the cruise, whatever that turns out to be: only a port's height is fixed.
			float slope = Climb;
			float top = Mathf.Max(cruise, Mathf.Max(yA, yB));
			float up = (top - yA) / slope, down = (top - yB) / slope;
			if (up + down > total)
			{
				// Too short to reach the cruise: as high as the way allows, at least the higher port's height.
				top = a.Port && b.Port ? (total * slope + yA + yB) * 0.5f : a.Port ? yA + total * slope : b.Port ? yB + total * slope : top;
				yA = a.Port ? yA : top;
				yB = b.Port ? yB : top;
				if (top < Mathf.Max(yA, yB))
				{
					top = Mathf.Max(yA, yB);
					slope = Mathf.Abs(yA - yB) / total;
					if (slope > SteepestClimb)
					{
						return null;
					}
				}
				up = slope > 0f ? (top - yA) / slope : 0f;
				down = slope > 0f ? (top - yB) / slope : 0f;
			}
			float HeightAt(float s)
			{
				if (up > 0f && s < up)
				{
					return Mathf.Lerp(yA, top, Mathf.SmoothStep(0f, 1f, s / up));
				}
				if (down > 0f && s > total - down)
				{
					return Mathf.Lerp(yB, top, Mathf.SmoothStep(0f, 1f, (total - s) / down));
				}
				return top;
			}

			// An edge end waits past the edge at the height it leaves by.
			var points = new List<Vector3> { a.Port ? a.Berth : new Vector3(a.Berth.x, HeightAt(0f), a.Berth.z) };
			for (int i = 0; i < way.Count; i++)
			{
				if (i > 0)
				{
					// Split long runs so the heights follow the profile between the points.
					int pieces = Mathf.CeilToInt((distance[i] - distance[i - 1]) / Spacing);
					for (int k = 1; k < pieces; k++)
					{
						float u = k / (float)pieces;
						Vector2 p = Vector2.Lerp(way[i - 1], way[i], u);
						points.Add(new Vector3(p.x, HeightAt(Mathf.Lerp(distance[i - 1], distance[i], u)), p.y));
					}
				}
				points.Add(new Vector3(way[i].x, HeightAt(distance[i]), way[i].y));
			}
			points.Add(b.Port ? b.Berth : new Vector3(b.Berth.x, HeightAt(total), b.Berth.z));
			FloatingBoats.Lane lane = Measure(points, a.Port, b.Port);
			return Verify(lane, a, b) ? lane : null;
		}

		/// <summary>
		/// A copy of <paramref name="lane"/> flying <paramref name="lift"/> metres higher over the open air, the same at its
		/// ports' level runs and easing up from them over the next stretch; null when the higher way meets an island.
		/// </summary>
		public FloatingBoats.Lane Raised(FloatingBoats.Lane lane, float lift)
		{
			if (lane == null || lane.Points.Length < 4)
			{
				return null;
			}
			int n = lane.Points.Length;
			float total = lane.Length;
			// A port end's level run is its first (last) leg: berth to exit.
			float startRun = lane.PortAtStart ? lane.Along[1] : 0f, endRun = lane.PortAtEnd ? total - lane.Along[n - 2] : 0f;
			const float Ease = 220f;
			var points = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				float s = lane.Along[i];
				float fromStart = lane.PortAtStart ? (s - startRun) / Ease : 1f, fromEnd = lane.PortAtEnd ? (total - endRun - s) / Ease : 1f;
				float share = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(fromStart, fromEnd)));
				points[i] = lane.Points[i] + Vector3.up * (lift * share);
			}
			FloatingBoats.Lane raised = Measure(new List<Vector3>(points), lane.PortAtStart, lane.PortAtEnd);
			// Clear of every column but at the ports' own level runs (the original lane's check).
			int skipStart = lane.PortAtStart ? 1 : 0, skipEnd = lane.PortAtEnd ? 1 : 0;
			for (int i = skipStart; i < n - 1 - skipEnd; i++)
			{
				if (!Clear(raised.Points[i], raised.Points[i + 1]))
				{
					return null;
				}
			}
			return raised;
		}

		private static FloatingBoats.Lane Measure(List<Vector3> points, bool portAtStart, bool portAtEnd)
		{
			var lane = new FloatingBoats.Lane
			{
				Points = points.ToArray(),
				Along = new float[points.Count],
				PortAtStart = portAtStart,
				PortAtEnd = portAtEnd,
				BerthSwing = portAtStart || portAtEnd ? BerthSwingSeconds : 0f,
			};
			for (int i = 1; i < points.Count; i++)
			{
				lane.Along[i] = lane.Along[i - 1] + Vector3.Distance(points[i - 1], points[i]);
			}
			return lane;
		}

		/// <summary>
		/// Every run of the lane clear of every island's column, but a port's own island along its level run in (which starts
		/// at the stage, under the island's reach, and runs straight out over the void).
		/// </summary>
		private bool Verify(FloatingBoats.Lane lane, End a, End b)
		{
			int n = lane.Points.Length;
			for (int i = 0; i < n - 1; i++)
			{
				int except = -1;
				if (i == 0 && a.Port)
				{
					except = a.Island;
				}
				else if (i == n - 2 && b.Port)
				{
					except = b.Island;
				}
				if (!Clear(lane.Points[i], lane.Points[i + 1], except))
				{
					return false;
				}
			}
			return true;
		}

		// ── The way round ─────────────────────────────────────────

		private Vector2 Centre(int i) => new Vector2(x0 + (i % w + 0.5f) * Cell, z0 + (i / w + 0.5f) * Cell);

		private int CellOf(Vector2 p)
		{
			int x = Mathf.Clamp(Mathf.FloorToInt((p.x - x0) / Cell), 0, w - 1), z = Mathf.Clamp(Mathf.FloorToInt((p.y - z0) / Cell), 0, d - 1);
			return z * w + x;
		}

		/// <summary>The cells inside the reach (and a cell's half-diagonal) of an island blocking the band.</summary>
		private bool[] Blocked(float low, float high)
		{
			var blocked = new bool[w * d];
			for (int i = 0; i < islands.Length; i++)
			{
				if (!Blocks(i, low, high))
				{
					continue;
				}
				float reach = islands[i].Radius + Margin + Cell * 0.71f;
				int cx0 = Mathf.Max(0, Mathf.FloorToInt((islands[i].Centre.x - reach - x0) / Cell)), cx1 = Mathf.Min(w - 1, Mathf.FloorToInt((islands[i].Centre.x + reach - x0) / Cell));
				int cz0 = Mathf.Max(0, Mathf.FloorToInt((islands[i].Centre.y - reach - z0) / Cell)), cz1 = Mathf.Min(d - 1, Mathf.FloorToInt((islands[i].Centre.y + reach - z0) / Cell));
				for (int z = cz0; z <= cz1; z++)
				{
					for (int x = cx0; x <= cx1; x++)
					{
						int c = z * w + x;
						if ((Centre(c) - islands[i].Centre).sqrMagnitude < reach * reach)
						{
							blocked[c] = true;
						}
					}
				}
			}
			return blocked;
		}

		/// <summary>The cheapest way between two points round the columns in the band (A*, eight ways); null for none.</summary>
		private List<Vector2> Route(Vector2 fromPoint, Vector2 toPoint, float low, float high)
		{
			bool[] blocked = Blocked(low, high);
			int from = CellOf(fromPoint), to = CellOf(toPoint);
			blocked[from] = blocked[to] = false;
			int n = w * d;
			var cost = new float[n];
			var came = new int[n];
			var closed = new bool[n];
			for (int i = 0; i < n; i++)
			{
				cost[i] = float.PositiveInfinity;
				came[i] = -1;
			}
			var open = new SortedSet<(float, int)>();
			cost[from] = 0f;
			Vector2 goal = Centre(to);
			open.Add((Vector2.Distance(Centre(from), goal), from));
			while (open.Count > 0)
			{
				(float _, int i) = open.Min;
				open.Remove(open.Min);
				if (i == to)
				{
					break;
				}
				if (closed[i])
				{
					continue;
				}
				closed[i] = true;
				int cx = i % w, cz = i / w;
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int nx = cx + dx, nz = cz + dz;
						if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= w || nz >= d)
						{
							continue;
						}
						int j = nz * w + nx;
						if (blocked[j] || closed[j])
						{
							continue;
						}
						float next = cost[i] + (dx != 0 && dz != 0 ? 1.4142f : 1f) * Cell;
						if (next < cost[j])
						{
							if (!float.IsPositiveInfinity(cost[j]))
							{
								open.Remove((cost[j] + Vector2.Distance(Centre(j), goal), j));
							}
							cost[j] = next;
							came[j] = i;
							open.Add((next + Vector2.Distance(Centre(j), goal), j));
						}
					}
				}
			}
			if (from != to && came[to] < 0)
			{
				return null;
			}
			var path = new List<Vector2>();
			for (int c = to; c >= 0; c = came[c])
			{
				path.Add(Centre(c));
				if (c == from)
				{
					break;
				}
			}
			path.Reverse();
			// The ends exactly where they were asked for, not at their cells' centres.
			path[0] = fromPoint;
			if (path.Count > 1)
			{
				path[path.Count - 1] = toPoint;
			}
			else
			{
				path.Add(toPoint);
			}
			return path;
		}

		/// <summary>Whether a straight run across the ground stays out of the reach of every island blocking the band.</summary>
		private bool Sees(Vector2 a, Vector2 b, float low, float high)
		{
			int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 4f));
			for (int s = 0; s <= steps; s++)
			{
				if (BlockedAt(Vector2.Lerp(a, b, s / (float)steps), low, high))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>A route pulled straight: from each kept point to the farthest it can see.</summary>
		private List<Vector2> Pull(List<Vector2> path, float low, float high)
		{
			var straight = new List<Vector2> { path[0] };
			int at = 0;
			while (at < path.Count - 1)
			{
				int next = at + 1;
				for (int j = path.Count - 1; j > at + 1; j--)
				{
					if (Sees(path[at], path[j], low, high))
					{
						next = j;
						break;
					}
				}
				straight.Add(path[next]);
				at = next;
			}
			return straight;
		}

		/// <summary>
		/// The way's corners rounded twice (Chaikin), each pass kept only where it still sees its way; the ends stay put. The
		/// first and last legs run straight along the stages' lines, so the turn at each lead is rounded from them.
		/// </summary>
		private List<Vector2> Round(List<Vector2> way, float low, float high)
		{
			List<Vector2> smooth = way;
			for (int pass = 0; pass < 2; pass++)
			{
				var round = new List<Vector2> { smooth[0] };
				for (int i = 0; i < smooth.Count - 1; i++)
				{
					Vector2 p = smooth[i], q = smooth[i + 1];
					round.Add(Vector2.Lerp(p, q, 0.25f));
					round.Add(Vector2.Lerp(p, q, 0.75f));
				}
				round.Add(smooth[smooth.Count - 1]);
				bool clear = true;
				for (int i = 1; i < round.Count && clear; i++)
				{
					clear = Sees(round[i - 1], round[i], low, high);
				}
				if (!clear)
				{
					break;
				}
				smooth = round;
			}
			return smooth;
		}
	}
}
#endif
