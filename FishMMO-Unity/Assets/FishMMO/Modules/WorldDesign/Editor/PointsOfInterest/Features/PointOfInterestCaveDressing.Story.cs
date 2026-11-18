#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>What the story pass laid: remains, carvings, falls (chokes dressed, ends fallen in, drifts half fallen), track pieces, carts, caches and their lootable chests.</summary>
			public int StoryRemains, StoryCarvings, StoryFalls, StoryTrack, StoryCarts, StoryCaches, StoryChests;

			/// <summary>
			/// What others left (2026-10-20, caves livened: "story props"): the remains of the lost, carvings on smooth walls, the
			/// dead ends that fell in, a mine's track down its level drifts and its abandoned tubs, and a cache hidden in a nook
			/// (<see cref="CaveStory"/> plans where; each spot is laid on a stream of its own, after every earlier pass, so nothing
			/// laid before it moves).
			/// </summary>
			private void Story()
			{
				CaveStoryPlan plan = CaveStory.Plan(system);
				for (int i = 0; i < plan.Spots.Count; i++)
				{
					CaveStorySpot spot = plan.Spots[i];
					var random = new DeterministicRNG((int)PointOfInterestPlanner.Hash(system.Seed, i, (int)spot.Kind, 0x5707));
					switch (spot.Kind)
					{
						case CaveStoryKind.Choke:
							StoryChoke(spot, random);
							break;
						case CaveStoryKind.CaveIn:
							StoryCaveIn(spot, random);
							break;
						case CaveStoryKind.Fall:
							StoryFall(spot, random);
							break;
						case CaveStoryKind.Rails:
							StoryRails(spot, random);
							break;
						case CaveStoryKind.Cart:
							StoryCart(spot, random);
							break;
						case CaveStoryKind.Cache:
							StoryCache(spot, random);
							break;
						case CaveStoryKind.Remains:
							StoryRemainsAt(spot, random);
							break;
						case CaveStoryKind.Carving:
							StoryCarving(spot, random);
							break;
					}
				}
				if (StoryRemains + StoryCarvings + StoryFalls + StoryTrack + StoryCarts + StoryCaches > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"cave story: {StoryRemains} remains, {StoryCarvings} carvings, {StoryFalls} falls, {StoryTrack} track pieces, {StoryCarts} carts, "
						+ $"{StoryCaches} caches ({StoryChests} lootable chests)");
				}
			}

			// ── Falls ─────────────────────────────────────────────

			/// <summary>
			/// A dead end the layout choked with a fall of the roof (a rubble mound of the field, CaveSystem.Variety): the blocks
			/// that rolled off its toe toward the way in, and in a mine the set it broke, across the drift before it.
			/// </summary>
			private void StoryChoke(in CaveStorySpot spot, DeterministicRNG random)
			{
				if (spot.Solid < 0 || spot.Solid >= system.Solids.Count)
				{
					return;
				}
				CaveSolid mound = system.Solids[spot.Solid];
				Vector3 dir = spot.Toward, side = Vector3.Cross(Vector3.up, dir);
				float w = Mathf.Max(0.8f, mound.Width);
				int blocks = random.Range(3, 7);
				bool any = false;
				for (int k = 0; k < blocks; k++)
				{
					float back = random.Range(0.7f, 1.7f) * w, across = random.Range(-0.6f, 0.6f) * spot.Radius, s = random.Range(0.22f, 0.6f);
					Quaternion turn = Quaternion.Euler(random.Range(-16f, 16f), random.Range(0f, 360f), random.Range(-16f, 16f));
					GameObject prefab = Formation(random, "Block");
					Vector3 q = mound.A - dir * back + side * across;
					any |= FallenBlock(prefab, q, spot.At.y, turn, s);
				}
				if (system.Form == CaveForm.Mine)
				{
					any |= BrokenSet(mound.A - dir * (w + 0.4f), dir, spot.Radius, spot.At.y);
				}
				if (any)
				{
					StoryFalls++;
				}
			}

			/// <summary>
			/// A dead end fallen in where the layout left it open: great blocks of the roof jammed into its end, a slab leant on them,
			/// the lesser rock spilled back toward the way in; in a mine the set it broke before them. Nothing lies beyond a dead end,
			/// so its last metres may be filled.
			/// </summary>
			private void StoryCaveIn(in CaveStorySpot spot, DeterministicRNG random)
			{
				Vector3 e = spot.At, dir = spot.Toward, side = Vector3.Cross(Vector3.up, dir);
				float r = Mathf.Max(1.2f, spot.Radius);
				float big = Mathf.Clamp(0.75f * r, 0.9f, 1.9f) * random.Range(0.9f, 1.1f);
				float hand = random.NextFloat() < 0.5f ? -1f : 1f;
				// Drawn before any is laid, so a piece refused leaves the rest where they were.
				GameObject first = Formation(random, "Block"), second = Formation(random, "Block"), third = Formation(random, "Block"), slab = Formation(random, "Slabs");
				Quaternion t1 = Quaternion.Euler(random.Range(-10f, 10f), random.Range(0f, 360f), random.Range(-10f, 10f));
				Quaternion t2 = Quaternion.Euler(random.Range(-18f, 18f), random.Range(0f, 360f), random.Range(-18f, 18f));
				Quaternion t3 = Quaternion.Euler(random.Range(15f, 30f), random.Range(0f, 360f), random.Range(-25f, 25f));
				float s2 = big * random.Range(0.55f, 0.75f), s3 = big * random.Range(0.45f, 0.6f), s4 = big * random.Range(0.5f, 0.7f);
				bool any = FallenBlock(first, e + dir * (0.35f * r), e.y, t1, big, sink: 0.15f);
				any |= FallenBlock(second, e + dir * (0.05f * r) + side * (hand * 0.45f * r), e.y, t2, s2);
				any |= FallenBlock(third, e + dir * (0.4f * r) - side * (hand * 0.25f * r), e.y, t3, s3, lift: 0.45f * big);
				// The slab, leant toward the fall on its foot before it.
				Vector3 slabAt = e - dir * (0.35f * r) + side * (hand * -0.2f * r);
				Quaternion lean = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-random.Range(30f, 48f), random.Range(-15f, 15f), 0f);
				any |= FallenBlock(slab, slabAt, e.y, lean, s4);
				int apron = random.Range(4, 8);
				for (int k = 0; k < apron; k++)
				{
					Vector3 q = e - dir * (random.Range(0.4f, 1.8f) * r) + side * (random.Range(-0.7f, 0.7f) * r);
					float s = random.Range(0.15f, 0.4f);
					Quaternion turn = Quaternion.Euler(random.Range(-14f, 14f), random.Range(0f, 360f), random.Range(-14f, 14f));
					any |= FallenBlock(Formation(random, set.Breakdown ?? "Block"), q, e.y, turn, s);
				}
				if (system.Form == CaveForm.Mine)
				{
					any |= BrokenSet(e - dir * (0.6f * r), dir, spot.Radius, e.y);
				}
				if (any)
				{
					StoryFalls++;
					bigs.Add(e);
				}
			}

			/// <summary>
			/// Part of a mine's drift fallen in along one wall: a set's snapped post and split cap thrown against it and the rock
			/// that came down with them heaped at its foot, the way past kept open.
			/// </summary>
			private void StoryFall(in CaveStorySpot spot, DeterministicRNG random)
			{
				Vector3 dir = spot.Toward;
				Vector3 side = Vector3.Cross(Vector3.up, dir) * (spot.Variant == 0 ? 1f : -1f);
				float floor = FloorUnder(spot.At, spot.At.y);
				int pieces = random.Range(3, 6);
				var rocks = new (GameObject prefab, float along, float off, float s, Quaternion turn)[pieces];
				for (int k = 0; k < pieces; k++)
				{
					rocks[k] = (Formation(random, set.Breakdown ?? "Block"), random.Range(-1.4f, 1.4f), random.Range(0.25f, 0.9f), random.Range(0.18f, 0.45f),
						Quaternion.Euler(random.Range(-14f, 14f), random.Range(0f, 360f), random.Range(-14f, 14f)));
				}
				if (!WallFoot(new Vector3(spot.At.x, floor + 0.5f, spot.At.z), side, 2f * spot.Radius + 1f, out Vector3 foot))
				{
					return;
				}
				// Wide enough to leave the way past: the wall's foot well out from the drift's line.
				if (new Vector2(foot.x - spot.At.x, foot.z - spot.At.z).magnitude < Lane + 0.6f)
				{
					return;
				}
				bool any = false;
				Vector3 debris = foot - side * 0.75f;
				var at = new Vector3(debris.x, FloorUnder(debris, floor), debris.z);
				if (!InLane(at, -0.4f) && !OnFeature(at, 0.2f) && !Submerged(at))
				{
					// The piece's +x (where its debris lies) toward the wall.
					any = Lay("CaveInTimbers", 1, at, Quaternion.LookRotation(Vector3.Cross(side, Vector3.up), Vector3.up), Vector3.one);
				}
				foreach ((GameObject prefab, float along, float off, float s, Quaternion turn) in rocks)
				{
					Vector3 q = foot - side * off + dir * along;
					if (FloorAndRoof(new Vector3(q.x, floor + 0.5f, q.z), out float f, out float roof) && roof - f > 1.2f)
					{
						var p = new Vector3(q.x, f, q.z);
						if (!InLane(p, 0.1f + 0.5f * s) && !OnFeature(p, 0.2f) && !Submerged(p) && Place(prefab, p - Vector3.up * (0.08f * s), turn, Vector3.one * s))
						{
							any = true;
						}
					}
				}
				if (any)
				{
					StoryFalls++;
				}
			}

			/// <summary>
			/// A block of fallen rock set on the floor under a point (<paramref name="level"/> the floor it is looked for from),
			/// sunk a little into it, or lifted to lie on what is under it; refused only on a way the layout built or in water.
			/// </summary>
			private bool FallenBlock(GameObject prefab, Vector3 near, float level, Quaternion turn, float scale, float sink = 0.08f, float lift = 0f)
			{
				Vector3 probe = new Vector3(near.x, level + 0.6f, near.z);
				if (system.Void(probe) >= 0f)
				{
					// In the mound or the wall: looked for a little higher (on the rubble).
					probe.y += 1.2f;
					if (system.Void(probe) >= 0f)
					{
						return false;
					}
				}
				if (!Ground(probe, 3f, out float floor))
				{
					return false;
				}
				var at = new Vector3(near.x, floor + lift - sink * scale, near.z);
				if (OnFeature(at, 0.2f) || Submerged(at))
				{
					return false;
				}
				return Place(prefab, at, turn, new Vector3(scale, scale, scale));
			}

			/// <summary>A mine's set broken by the fall (CaveInTimbers v0) across the drift at a point, sized to its bore, falling toward <paramref name="dir"/>.</summary>
			private bool BrokenSet(Vector3 near, Vector3 dir, float radius, float level)
			{
				float s = Mathf.Max(0.6f, radius / 2.1f);
				var at = new Vector3(near.x, FloorUnder(near, level), near.z);
				if (OnFeature(at, 0f) || Submerged(at))
				{
					return false;
				}
				return Lay("CaveInTimbers", 0, at, Quaternion.LookRotation(dir, Vector3.up), new Vector3(s, Mathf.Clamp(s, 0.95f, 1.1f), 1f));
			}

			// ── Track and tubs ────────────────────────────────────

			/// <summary>
			/// A level drift's track (a mine's trunk has its own, Mine): pieces end to end along its line between the plan's
			/// arc lengths, laid on the floor as carved and raised over its humps as the trunk's is, the run cut short before a
			/// climb's foot, a pit or water; a tub left on its rails at the end of a dead drift.
			/// </summary>
			private void StoryRails(in CaveStorySpot spot, DeterministicRNG random)
			{
				if (spot.Passage < 0 || spot.Passage >= system.Passages.Count)
				{
					return;
				}
				List<CaveNode> nodes = system.Passages[spot.Passage].Nodes;
				int count = Mathf.FloorToInt((spot.To - spot.From) / TrackPieceMetres);
				if (count < 1)
				{
					return;
				}
				var joints = new Vector3[count + 1];
				var nominal = new float[count + 1];
				for (int j = 0; j <= count; j++)
				{
					CentreAt(nodes, spot.From + j * TrackPieceMetres, out joints[j], out nominal[j]);
				}
				// Cut short where it would run onto a climb's foot, a pit, a crossing or water.
				for (int j = 0; j < count; j++)
				{
					Vector3 mid = 0.5f * (joints[j] + joints[j + 1]);
					var on = new Vector3(mid.x, 0.5f * (nominal[j] + nominal[j + 1]), mid.z);
					if (OnFeature(on, 0.3f) || Submerged(on))
					{
						count = j;
						break;
					}
				}
				if (count < 2)
				{
					return;
				}
				var floors = new float[count, TrackSamples];
				var heights = new float[count + 1];
				for (int j = 0; j < count; j++)
				{
					Vector3 a = joints[j], b = joints[j + 1];
					for (int k = 0; k < TrackSamples; k++)
					{
						float f = k / (TrackSamples - 1f);
						CentreAt(nodes, spot.From + (j + f) * TrackPieceMetres, out _, out float level);
						floors[j, k] = FloorUnder(new Vector3(Mathf.Lerp(a.x, b.x, f), 0f, Mathf.Lerp(a.z, b.z, f)), level);
					}
					heights[j] = j == 0 ? floors[0, 0] : Mathf.Min(heights[j], floors[j, 0]);
					heights[j + 1] = floors[j, TrackSamples - 1];
				}
				for (int pass = 0; pass < 8; pass++)
				{
					bool raised = false;
					for (int j = 0; j < count; j++)
					{
						float over = float.MinValue;
						for (int k = 0; k < TrackSamples; k++)
						{
							over = Mathf.Max(over, floors[j, k] - Mathf.Lerp(heights[j], heights[j + 1], k / (TrackSamples - 1f)));
						}
						if (over > SleeperBedMetres + 1e-3f)
						{
							heights[j] += over - SleeperBedMetres;
							heights[j + 1] += over - SleeperBedMetres;
							raised = true;
						}
					}
					if (!raised)
					{
						break;
					}
				}
				Vector3 lastCentre = default;
				Quaternion lastTurn = default;
				bool laid = false;
				for (int j = 0; j < count; j++)
				{
					bool worn = random.NextFloat() < 0.2f;
					var a = new Vector3(joints[j].x, heights[j], joints[j].z);
					var b = new Vector3(joints[j + 1].x, heights[j + 1], joints[j + 1].z);
					var flat = new Vector2(b.x - a.x, b.z - a.z);
					if (flat.magnitude < 0.5f)
					{
						continue;
					}
					Quaternion turn = Quaternion.Euler(-Mathf.Atan2(b.y - a.y, flat.magnitude) * Mathf.Rad2Deg, Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg, 0f);
					Vector3 centre = 0.5f * (a + b);
					if (Lay("MineTrack", worn ? 1 : 0, centre, turn, Vector3.one))
					{
						StoryTrack++;
						laid = true;
						lastCentre = centre;
						lastTurn = turn;
					}
				}
				// A tub left on the rails at the end of a drift that goes nowhere.
				if (laid && (spot.Variant & 1) != 0 && Lay("MineCart", 0, lastCentre + lastTurn * new Vector3(0f, RailTopMetres, 0f), lastTurn, Vector3.one))
				{
					StoryCarts++;
					bigs.Add(lastCentre);
				}
			}

			/// <summary>A tub tipped off the rails of a drift where it is wide enough, its ore spilled at the wall's foot.</summary>
			private void StoryCart(in CaveStorySpot spot, DeterministicRNG random)
			{
				if (spot.Passage < 0 || spot.Passage >= system.Passages.Count)
				{
					return;
				}
				List<CaveNode> nodes = system.Passages[spot.Passage].Nodes;
				float hand = random.NextFloat() < 0.5f ? -1f : 1f, yaw = random.Range(-25f, 25f);
				CentreAt(nodes, spot.From, out Vector3 c, out float nominal);
				CentreAt(nodes, spot.From + 0.5f, out Vector3 ahead, out _);
				Vector3 along = Flat(ahead - c), side = Vector3.Cross(Vector3.up, along) * hand;
				float floor = FloorUnder(c, nominal);
				if (!WallFoot(new Vector3(c.x, floor + 0.5f, c.z), side, 2f * RadiusAt(nodes, spot.From) + 1f, out Vector3 foot)
					|| new Vector2(foot.x - c.x, foot.z - c.z).magnitude < Lane + 0.9f)
				{
					return;
				}
				Vector3 q = foot - side * 0.85f;
				var at = new Vector3(q.x, FloorUnder(q, floor), q.z);
				if (InLane(at, -0.5f) || OnFeature(at, 0.3f) || Submerged(at) || Crowded(at, 1.2f, 3f) || system.Void(at + Vector3.up * 1.4f) >= 0f)
				{
					return;
				}
				// The tub's mouth (−x once tipped) spilling toward the way, its length along the drift.
				Quaternion turn = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * Vector3.Cross(side, Vector3.up), Vector3.up);
				if (Lay("MineCart", 1, at, turn, Vector3.one))
				{
					StoryCarts++;
					bigs.Add(at);
				}
			}

			// ── Caches ────────────────────────────────────────────

			/// <summary>How long an emptied cache's chest takes to come back, seconds: an hour or two.</summary>
			private static readonly Vector2 CacheRespawnSeconds = new Vector2(3600f, 7200f);

			/// <summary>
			/// A cache hidden in a nook: a strongbox against the rock at its back (CacheCoffer), what was stowed with it (a
			/// smuggler's barrels and crate, a miner's sacks and tools, an adventurer's crate), a block or two fallen before it, and
			/// the lootable chest (the Dungeon Chest, the project's lootable container) spawned inside the coffer, so emptied it
			/// comes back.
			/// </summary>
			private void StoryCache(in CaveStorySpot spot, DeterministicRNG random)
			{
				int coffer = random.Range(0, 2);
				float h0 = random.Range(0f, 360f);
				float level = FloorUnder(spot.At, spot.At.y);
				var from = new Vector3(spot.At.x, level + 0.5f, spot.At.z);
				bool inChamber = spot.Chamber >= 0 && spot.Chamber < system.Chambers.Count;
				CaveChamber room = inChamber ? system.Chambers[spot.Chamber] : default;
				float reach = inChamber ? Mathf.Max(room.Radii.x, room.Radii.z) + 2f : 2f * spot.Radius + 2f;
				// Straight on into the nook first (the end of a dead end, the back of an alcove), then round from there.
				float start = spot.Toward.sqrMagnitude > 1e-4f ? Mathf.Atan2(spot.Toward.x, spot.Toward.z) * Mathf.Rad2Deg : h0;
				for (int k = 0; k < 10; k++)
				{
					float heading = start + (k % 2 == 0 ? 1f : -1f) * 36f * ((k + 1) / 2);
					Vector3 dir = Dir(heading);
					Vector3 foot;
					bool hit = inChamber ? WallFoot(from, dir, reach, out foot, room) : WallFoot(from, dir, reach, out foot);
					if (!hit)
					{
						continue;
					}
					Vector3 q = foot - dir * 0.45f;
					if (!Ground(new Vector3(q.x, foot.y + 0.6f, q.z), 2f, out float f) || !Roof(new Vector3(q.x, f + 0.3f, q.z), 4f, out float roof) || roof - f < 1.3f)
					{
						continue;
					}
					var at = new Vector3(q.x, f, q.z);
					Vector3 across = Vector3.Cross(Vector3.up, dir);
					if (!FlatAround(at, across * 0.5f, 0.15f) || !FlatAround(at, dir * 0.3f, 0.15f) || InLane(at, -0.9f) || OnFeature(at, 0.3f) || Submerged(at) || NearMouth(at)
						|| Crowded(at, 1.5f, 3f) || DaylightAt(at, 1f) > 0.05f)
					{
						continue;
					}
					// The coffer's back (+z) to the rock; the chest spawns where it stands, turned as it is.
					Quaternion turn = Quaternion.LookRotation(dir, Vector3.up);
					if (!Lay("CacheCoffer", coffer, at - Vector3.up * 0.01f, turn, Vector3.one))
					{
						return;
					}
					StoryCaches++;
					bigs.Add(at);
					if (CacheChest(at, turn))
					{
						StoryChests++;
					}
					CacheStowed(spot.Variant, at, dir, across, random);
					// A block or two fallen before it, half hiding it from the way.
					for (int b = 0; b < 2; b++)
					{
						float s = random.Range(0.25f, 0.45f);
						Quaternion t = Quaternion.Euler(random.Range(-12f, 12f), random.Range(0f, 360f), random.Range(-12f, 12f));
						Vector3 screen = at - dir * random.Range(0.9f, 1.3f) + across * ((b == 0 ? 1f : -1f) * random.Range(0.55f, 0.8f));
						GameObject prefab = Formation(random, "Block");
						if (!InLane(new Vector3(screen.x, at.y, screen.z), 0f))
						{
							FallenBlock(prefab, screen, at.y, t, s);
						}
					}
					return;
				}
			}

			/// <summary>What was stowed with a cache, beside its coffer along the wall: by whose it was (<see cref="CaveStoryCacheKind"/>).</summary>
			private void CacheStowed(int kind, Vector3 at, Vector3 dir, Vector3 across, DeterministicRNG random)
			{
				(string id, int variant)[] stowed;
				switch ((CaveStoryCacheKind)kind)
				{
					case CaveStoryCacheKind.Smugglers:
						stowed = new[] { ("Barrel", random.Range(0, 2)), ("Barrel", 0), ("Crate", random.Range(0, 2)) };
						break;
					case CaveStoryCacheKind.Miners:
						stowed = new[] { (CaveArt.OreSacks, random.Range(0, 2)), (CaveArt.MiningTools, 1) };
						break;
					default:
						stowed = new[] { ("Crate", random.Range(0, 2)) };
						break;
				}
				float hand = random.NextFloat() < 0.5f ? -1f : 1f;
				for (int i = 0; i < stowed.Length; i++)
				{
					float off = (0.95f + 0.75f * (i / 2)) * (i % 2 == 0 ? hand : -hand);
					float yaw = random.Range(-30f, 30f);
					Vector3 q = at + across * off + dir * 0.05f;
					if (!Ground(new Vector3(q.x, at.y + 0.6f, q.z), 1.5f, out float f) || Mathf.Abs(f - at.y) > 0.4f || system.Void(new Vector3(q.x, f + 1.2f, q.z)) >= 0f)
					{
						continue;
					}
					var p = new Vector3(q.x, f, q.z);
					if (InLane(p, -0.9f) || OnFeature(p, 0.2f) || Submerged(p))
					{
						continue;
					}
					Lay(stowed[i].id, stowed[i].variant, p, Quaternion.Euler(0f, yaw, 0f) * Quaternion.LookRotation(dir, Vector3.up), Vector3.one);
				}
			}

			/// <summary>
			/// The cache's lootable chest: a spawner of the project's lootable container (GatheringContentGenerator.BasePrefabPath,
			/// the Dungeon Chest: a NetworkObject with a Container, its loot table, despawned when emptied) standing where the coffer
			/// does, so the chest comes back after it is emptied. Only in a scene (a probe's dressing has no site object to hold it).
			/// </summary>
			private bool CacheChest(Vector3 local, Quaternion rotation)
			{
				if (context.Root == null)
				{
					return false;
				}
				GameObject chest = AssetDatabase.LoadAssetAtPath<GameObject>(GatheringContentGenerator.BasePrefabPath);
				NetworkObject networkObject = chest != null ? chest.GetComponent<NetworkObject>() : null;
				if (networkObject == null)
				{
					Missing.Add("the cave cache's lootable chest (Dungeon Chest prefab)");
					return false;
				}
				var spawnables = new List<SpawnableSettings>
				{
					new SpawnableSettings
					{
						NetworkObject = networkObject,
						SpawnChance = 1f,
						MinimumRespawnTime = CacheRespawnSeconds.x,
						MaximumRespawnTime = CacheRespawnSeconds.y,
					},
				};
				// Area 0: it spawns exactly where the spawner stands (the cave's floor, not the ground over the cave), turned as it is.
				ObjectSpawner spawner = PointOfInterestFeatureKit.AddSpawner(context, PointOfInterestFeatureKit.UniqueName(context, $"Cave Cache {StoryCaches}"), Vector2.zero, spawnables, 1, 0f);
				spawner.transform.SetPositionAndRotation(system.Origin + local, rotation);
				EditorUtility.SetDirty(spawner);
				return true;
			}

			/// <summary>Whether the floor a little off a spot either way (±<paramref name="offset"/>) lies within <paramref name="tolerance"/> of it.</summary>
			private bool FlatAround(Vector3 at, Vector3 offset, float tolerance)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 q = at + offset * s;
					if (!Ground(new Vector3(q.x, at.y + 0.6f, q.z), 1.2f, out float f) || Mathf.Abs(f - at.y) > tolerance)
					{
						return false;
					}
				}
				return true;
			}

			// ── Remains ───────────────────────────────────────────

			/// <summary>
			/// The remains of one who never came out, near the plan's spot: slumped against the nearest wall (its back to the rock),
			/// or fallen on a floor level enough to lie along; mossed where the cave is wet. At a dead end they may lie on its way.
			/// </summary>
			private void StoryRemainsAt(in CaveStorySpot spot, DeterministicRNG random)
			{
				var pose = (CaveStoryPose)spot.Variant;
				float h0 = spot.Toward.sqrMagnitude > 1e-4f ? Mathf.Atan2(spot.Toward.x, spot.Toward.z) * Mathf.Rad2Deg : random.Range(0f, 360f);
				float yaw = random.Range(0f, 360f), jitterA = random.Range(0f, 360f), jitterR = random.Range(0f, 1.2f);
				float level = FloorUnder(spot.At, spot.At.y);
				// On a dead end's last stretch the way leads nowhere: only the layout's own ways are kept clear there.
				bool open = spot.DeadEnd;
				if (pose == CaveStoryPose.Seated)
				{
					var from = new Vector3(spot.At.x, level + 0.6f, spot.At.z);
					for (int k = 0; k < 8; k++)
					{
						float heading = h0 + (k % 2 == 0 ? 1f : -1f) * 45f * ((k + 1) / 2);
						if (!Wall(from, Dir(heading), 4.5f, out Vector3 surface, out Vector3 into))
						{
							continue;
						}
						Vector3 flat = Flat(into);
						Vector3 q = surface - flat * 0.33f;
						if (!Ground(new Vector3(q.x, surface.y + 0.2f, q.z), 1.6f, out float f) || Mathf.Abs(f - level) > 1.2f)
						{
							continue;
						}
						var at = new Vector3(q.x, f, q.z);
						Vector3 feet = at - flat * 0.75f;
						if (!Ground(new Vector3(feet.x, f + 0.6f, feet.z), 1.2f, out float ff) || Mathf.Abs(ff - f) > 0.3f
							|| !Roof(new Vector3(at.x, f + 0.3f, at.z), 3f, out float roof) || roof - f < 1.1f
							|| !open && (InLane(at, 0.2f) || InLane(new Vector3(feet.x, ff, feet.z), 0.2f)) || OnFeature(at, 0.2f) || OnFeature(feet, 0.2f)
							|| Submerged(at) || NearMouth(at) || Crowded(at, 1.2f, 4f))
						{
							continue;
						}
						if (Remains(0, at, Quaternion.LookRotation(flat, Vector3.up)))
						{
							return;
						}
					}
					return;
				}
				// Fallen along a level floor near the spot, the head toward −z of the piece.
				for (int k = 0; k < 6; k++)
				{
					float r = k == 0 ? 0f : jitterR + 0.4f * k;
					Vector3 c = spot.At + Dir(jitterA + 67f * k) * r;
					if (!Ground(new Vector3(c.x, level + 0.6f, c.z), 1.6f, out float f))
					{
						continue;
					}
					Vector3 fwd = Dir(yaw + 31f * k);
					Vector3 head = c - fwd * 0.95f, feet = c + fwd * 0.9f;
					if (!Ground(new Vector3(head.x, f + 0.6f, head.z), 1.2f, out float fh) || !Ground(new Vector3(feet.x, f + 0.6f, feet.z), 1.2f, out float fl)
						|| Mathf.Abs(fh - f) > 0.3f || Mathf.Abs(fl - f) > 0.3f || !Roof(new Vector3(c.x, f + 0.3f, c.z), 3f, out float roof) || roof - f < 0.9f)
					{
						continue;
					}
					var at = new Vector3(c.x, f, c.z);
					var h3 = new Vector3(head.x, fh, head.z);
					var l3 = new Vector3(feet.x, fl, feet.z);
					if (!open && (InLane(at, 0.2f) || InLane(h3, 0.2f) || InLane(l3, 0.2f)) || OnFeature(at, 0.2f) || OnFeature(h3, 0.2f) || OnFeature(l3, 0.2f)
						|| Submerged(at) || NearMouth(at) || Crowded(at, 1.5f, 4f))
					{
						continue;
					}
					// Along the floor's own fall from head to feet.
					Quaternion turn = Quaternion.LookRotation((l3 - h3).normalized, Vector3.up);
					if (Remains(pose == CaveStoryPose.Miner ? 2 : 1, at - Vector3.up * 0.02f, turn))
					{
						return;
					}
				}
			}

			/// <summary>Lays a variant of the remains, mossed where the rock about it is wet (not in an ice cave).</summary>
			private bool Remains(int variant, Vector3 at, Quaternion turn)
			{
				bool wet = system.Form != CaveForm.IceCave && HabitatAt(at, Vector3.up).Wet > 0.55f;
				GameObject prefab = StructureKit.Prefab("LostRemains", variant, wet ? StructureFinish.Mossy : StructureFinish.None);
				if (prefab == null)
				{
					Missing.Add("LostRemains");
					return false;
				}
				if (!Place(prefab, at, turn, Vector3.one))
				{
					return false;
				}
				StoryRemains++;
				return true;
			}

			// ── Carvings ──────────────────────────────────────────

			/// <summary>
			/// A carving on a smooth stretch of a chamber's wall: the wall met at the face's middle height, then the face's corners
			/// and edges sounded straight into the rock; laid only where every one finds the rock within a hand's breadth in front
			/// of the face's plane and a stride behind it (its root hides the rest), so it lies on the wall and not over a hollow.
			/// </summary>
			private void StoryCarving(in CaveStorySpot spot, DeterministicRNG random)
			{
				if (spot.Chamber < 0 || spot.Chamber >= system.Chambers.Count)
				{
					return;
				}
				CaveChamber c = system.Chambers[spot.Chamber];
				int variant = Mathf.Clamp(spot.Variant, 0, StructurePieces.CaveCarvingFaces.Length - 1);
				Vector2 face = StructurePieces.CaveCarvingFaces[variant];
				float sill = variant == 1 ? 0.05f : variant == 0 ? 0.55f : 0.7f;
				float h0 = random.Range(0f, 360f);
				var centre = new Vector3(c.Centre.x, CaveSystem.FloorOf(c, c.Centre) + 0.5f, c.Centre.z);
				float reach = Mathf.Max(c.Radii.x, c.Radii.z) + 2f;
				for (int k = 0; k < 10; k++)
				{
					Vector3 dir = Dir(h0 + 36f * k);
					if (!WallFoot(centre, dir, reach, out Vector3 foot, c))
					{
						continue;
					}
					Vector3 back = foot - dir * 0.4f;
					if (!Ground(new Vector3(back.x, foot.y + 0.3f, back.z), 2f, out float floor))
					{
						continue;
					}
					float mid = floor + sill + 0.5f * face.y;
					var probe = new Vector3(foot.x, mid, foot.z) - dir * 1.2f;
					if (system.Void(probe) >= 0f || !Wall(probe, dir, 3f, out Vector3 surface, out Vector3 into) || Mathf.Abs(into.y) > 0.35f)
					{
						continue;
					}
					Vector3 inward = Flat(into);
					Vector3 right = Vector3.Cross(Vector3.up, inward);
					if (!Flush(surface, inward, right, face, out float front))
					{
						continue;
					}
					Vector3 origin = surface + inward * front - Vector3.up * (0.5f * face.y);
					if (Submerged(origin) || NearMouth(origin) || DaylightAt(origin, 1f) > 0.05f || OnFeature(origin - inward * 0.6f, 0.2f))
					{
						continue;
					}
					if (Lay("CaveCarving", variant, origin, Quaternion.LookRotation(inward, Vector3.up), Vector3.one))
					{
						StoryCarvings++;
					}
					return;
				}
			}

			/// <summary>
			/// Whether the wall round a point is flush enough for a carving's face (<paramref name="face"/> across and up, centred on
			/// <paramref name="surface"/>): its corners and edge middles sounded along <paramref name="inward"/> from in front of
			/// the plane; <paramref name="front"/> is how far behind the plane the rock nearest the face stands (negative: before it).
			/// </summary>
			private bool Flush(Vector3 surface, Vector3 inward, Vector3 right, Vector2 face, out float front)
			{
				front = float.MaxValue;
				for (int i = 0; i < 3; i++)
				{
					for (int j = 0; j < 2; j++)
					{
						float u = (i - 1) * 0.45f * face.x, v = (j == 0 ? -0.45f : 0.45f) * face.y;
						Vector3 start = surface + right * u + Vector3.up * v - inward * 0.7f;
						if (system.Void(start) >= 0f || !Surface(start, inward, 1.5f, out Vector3 hit))
						{
							return false;
						}
						float depth = Vector3.Dot(hit - surface, inward);
						if (depth < -0.08f || depth > 0.32f)
						{
							return false;
						}
						front = Mathf.Min(front, depth);
					}
				}
				return true;
			}
		}
	}

	/// <summary>What a story spot is (<see cref="CaveStory"/>).</summary>
	public enum CaveStoryKind : byte
	{
		/// <summary>The remains of one who never came out.</summary>
		Remains,
		/// <summary>A carving or a painting on a chamber's wall.</summary>
		Carving,
		/// <summary>A dead end the layout choked with a fall (a Rubble solid): dressed with what rolled off it.</summary>
		Choke,
		/// <summary>An open dead end fallen in: blocks jammed into its end.</summary>
		CaveIn,
		/// <summary>Part of a mine's drift fallen in along one wall, the way past kept.</summary>
		Fall,
		/// <summary>Track along a level drift of a mine.</summary>
		Rails,
		/// <summary>A tub tipped off its rails.</summary>
		Cart,
		/// <summary>A cache hidden in a nook, its lootable chest spawned in its coffer.</summary>
		Cache,
	}

	/// <summary>How the remains lie: <see cref="CaveStorySpot.Variant"/> of a <see cref="CaveStoryKind.Remains"/> spot (the art's variant for the last two).</summary>
	public enum CaveStoryPose : byte
	{
		/// <summary>Slumped against the rock (LostRemains v0).</summary>
		Seated,
		/// <summary>Fallen face down (v1).</summary>
		Fallen,
		/// <summary>A miner fallen by his pick (v2).</summary>
		Miner,
	}

	/// <summary>Whose a cache was: <see cref="CaveStorySpot.Variant"/> of a <see cref="CaveStoryKind.Cache"/> spot.</summary>
	public enum CaveStoryCacheKind : byte
	{
		Adventurers,
		/// <summary>A sea cave's or a grotto's: barrels and a crate beside the coffer.</summary>
		Smugglers,
		/// <summary>A mine's: sacks of ore and tools.</summary>
		Miners,
	}

	/// <summary>One story spot in a cave system (its frame): what, where on the plan's floor, and toward what.</summary>
	public struct CaveStorySpot
	{
		public CaveStoryKind Kind;
		/// <summary>On the model's floor (a node's or a chamber's), system frame: where its piece is looked for from.</summary>
		public Vector3 At;
		/// <summary>
		/// A flat unit way: a dead end's on into its fall, a nook's into its back, a drift's along it, the wall the remains look
		/// for first; zero for none.
		/// </summary>
		public Vector3 Toward;
		/// <summary>The passage, chamber and solid it belongs to, or −1.</summary>
		public int Passage, Chamber, Solid;
		/// <summary>A track's run along its passage, arc lengths (Rails); a tub's arc length (Cart, in <see cref="From"/>).</summary>
		public float From, To;
		/// <summary>The bore's radius where it is, metres.</summary>
		public float Radius;
		/// <summary>
		/// By kind: the pose (<see cref="CaveStoryPose"/>), the carving's variant, the cache's owner (<see cref="CaveStoryCacheKind"/>),
		/// the side a drift fell on (0 or 1), a track's flags (bit 0: a tub left at its end).
		/// </summary>
		public int Variant;
		/// <summary>On a dead end's last stretch: the way's lane there leads nowhere and may be lain across.</summary>
		public bool DeadEnd;
	}

	/// <summary>What <see cref="CaveStory.Plan"/> found for a system.</summary>
	public sealed class CaveStoryPlan
	{
		public readonly List<CaveStorySpot> Spots = new List<CaveStorySpot>();

		public int Count(CaveStoryKind kind)
		{
			int n = 0;
			foreach (CaveStorySpot s in Spots)
			{
				if (s.Kind == kind)
				{
					n++;
				}
			}
			return n;
		}
	}

	/// <summary>One dead end of a system: a passage ending in no chamber, no other way and nothing climbed or crossed (<see cref="CaveStory.DeadEnds"/>).</summary>
	public struct CaveDeadEnd
	{
		public int Passage;
		/// <summary>Its last node's floor point (system frame).</summary>
		public Vector3 End;
		/// <summary>The way on into it, flat.</summary>
		public Vector3 Toward;
		public float Radius;
		/// <summary>The Rubble solid the layout choked it with, or −1.</summary>
		public int Solid;
	}

	/// <summary>
	/// Where a cave system's story goes (2026-10-20, caves livened: "bones/skeletons of lost adventurers, ancient carvings,
	/// cave-ins, mine carts and rails, hidden caches"): sparse and believable, from the system alone.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Falls.</b> Every dead end the layout choked (CaveSystem.Variety's Breakdown and a mine's Collapsed) is dressed; of the
	/// open ones a few more have fallen in (more in a mine), at most <see cref="MaxCaveIns"/> + the size class; a mine's drift
	/// has fallen in along one wall here and there. Only dead ends: nothing lies past one, so no way is ever blocked.
	/// </para>
	/// <para>
	/// <b>Track.</b> A mine's level drifts (its branches; its trunk is laid by the dressing's Mine) mostly have track, in runs
	/// only where the floor falls no more than <see cref="RailGrade"/>, from clear of the way they leave to short of their end;
	/// a dead drift keeps a tub at its end half the time, and a mine may have one tipped off its rails.
	/// </para>
	/// <para>
	/// <b>The lost.</b> One to three remains by the size of the system, at the foot of a climb (they fell), before a fall (it
	/// caught them), in a nook or at a dead end (they sat down and did not get up), against a far room's wall; never two within
	/// <see cref="RemainsApart"/>. A miner in a mine. <b>Carvings</b> in natural caves (not a mine's cut rock, not ice that flows,
	/// not a sea cave the waves scour), at most two, in rooms far in. <b>Caches</b>: one in a nook (an alcove, else an open dead
	/// end, else a far room) by chance growing with the system, a second in a large one now and then; a smuggler's by the sea, a
	/// miner's in a mine.
	/// </para>
	/// <para>
	/// Nothing within <see cref="MouthClear"/> of a mouth (the daylight's and the approach's), nothing in a site's own chamber
	/// (its camp's, its den's, its dungeon gate's). Pure and deterministic: a function of the system, each kind on a stream of
	/// its own, every candidate drawing the same numbers whether it is taken or not.
	/// </para>
	/// </remarks>
	public static class CaveStory
	{
		/// <summary>Nothing of the story nearer a mouth than this, metres (or than its zone, if more).</summary>
		public const float MouthClear = 14f;
		/// <summary>Open dead ends fallen in: at most this plus the system's size class.</summary>
		public const int MaxCaveIns = 1;
		/// <summary>The steepest a drift's floor may fall along a run of track (rise over run).</summary>
		public const float RailGrade = 0.1f;
		/// <summary>The shortest run of track laid, metres.</summary>
		public const float RailRunMin = 9f;
		/// <summary>The least distance between two remains, metres.</summary>
		public const float RemainsApart = 20f;
		/// <summary>The least distance between two carvings, metres.</summary>
		public const float CarvingsApart = 30f;

		public static CaveStoryPlan Plan(CaveSystem system)
		{
			var plan = new CaveStoryPlan();
			if (system == null || system.Passages.Count == 0)
			{
				return plan;
			}
			List<CaveDeadEnd> ends = DeadEnds(system);
			var fallen = new HashSet<int>();
			Falls(system, ends, plan, fallen, new DeterministicRNG(unchecked(system.Seed * 0x5A3D + 0x2C71)));
			if (system.Form == CaveForm.Mine)
			{
				Rails(system, ends, plan, new DeterministicRNG(unchecked(system.Seed * 0x6E1B + 0x4F93)));
			}
			var cached = new List<Vector3>();
			Caches(system, ends, fallen, plan, cached, new DeterministicRNG(unchecked(system.Seed * 0x3C59 + 0x7A27)));
			Lost(system, ends, fallen, plan, cached, new DeterministicRNG(unchecked(system.Seed * 0x47D3 + 0x1E6F)));
			Carvings(system, plan, new DeterministicRNG(unchecked(system.Seed * 0x2D8B + 0x6C35)));
			return plan;
		}

		// ── Dead ends ─────────────────────────────────────────────────

		/// <summary>
		/// Every passage that ends in no chamber, meets no other way at its end, and has no climb, chasm or pit at it, nor a mouth
		/// near: where a way goes nowhere. With the Rubble solid that chokes it, where the layout laid one.
		/// </summary>
		public static List<CaveDeadEnd> DeadEnds(CaveSystem system)
		{
			var ends = new List<CaveDeadEnd>();
			for (int pi = 0; pi < system.Passages.Count; pi++)
			{
				CavePassage passage = system.Passages[pi];
				int n = passage.Nodes.Count;
				if (n < 3 || passage.Kind == CavePassageKind.Link)
				{
					continue;
				}
				CaveNode last = passage.Nodes[n - 1];
				var end = new Vector3(last.Centre.x, last.Floor, last.Centre.z);
				if (InAnyChamber(system, end, last.Radius + 1f) || NearMouth(system, end) || Wet(system, end))
				{
					continue;
				}
				int solid = -1;
				for (int si = 0; si < system.Solids.Count; si++)
				{
					CaveSolid s = system.Solids[si];
					if (s.Kind == CaveSolidKind.Rubble && Plan2(s.A - end).magnitude < 1.6f * last.Radius + 0.5f && Mathf.Abs(s.A.y - end.y) < 2f)
					{
						solid = si;
						break;
					}
				}
				// An end the layout choked is a dead end already (its fall is rock of the field); an open one only where no other way,
				// climb, chasm or pit is at it (a branch that ends at a climb's foot goes on up it).
				if (solid < 0 && (NearOtherWay(system, pi, end, last.Radius + 2.5f) || system.NearChasmOrClimb(end + Vector3.up * 0.5f, last.Radius + 1f)
					|| NearPit(system, end, last.Radius + 2f)))
				{
					continue;
				}
				Vector3 toward = Flat(last.Centre - passage.Nodes[Mathf.Max(0, n - 3)].Centre);
				ends.Add(new CaveDeadEnd { Passage = pi, End = end, Toward = toward, Radius = last.Radius, Solid = solid });
			}
			return ends;
		}

		// ── Falls ─────────────────────────────────────────────────────

		private static void Falls(CaveSystem system, List<CaveDeadEnd> ends, CaveStoryPlan plan, HashSet<int> fallen, DeterministicRNG random)
		{
			bool mine = system.Form == CaveForm.Mine;
			float odds = mine ? 0.5f : system.Has(CaveTrait.Breakdown) ? 0.45f : 0.3f;
			int cap = MaxCaveIns + system.SizeClass, made = 0;
			foreach (CaveDeadEnd e in ends)
			{
				float roll = random.NextFloat();
				if (e.Solid >= 0)
				{
					plan.Spots.Add(new CaveStorySpot { Kind = CaveStoryKind.Choke, At = e.End, Toward = e.Toward, Passage = e.Passage, Chamber = -1, Solid = e.Solid, Radius = e.Radius, DeadEnd = true });
					fallen.Add(e.Passage);
				}
				else if (made < cap && roll < odds)
				{
					plan.Spots.Add(new CaveStorySpot { Kind = CaveStoryKind.CaveIn, At = e.End, Toward = e.Toward, Passage = e.Passage, Chamber = -1, Solid = -1, Radius = e.Radius, DeadEnd = true });
					fallen.Add(e.Passage);
					made++;
				}
			}
			if (!mine)
			{
				return;
			}
			// A drift fallen in along one wall here and there.
			int falls = random.Range(0, 2 + system.SizeClass);
			for (int k = 0, tries = 0; k < falls && tries < falls * 4; tries++)
			{
				int pi = random.Range(0, system.Passages.Count);
				CavePassage passage = system.Passages[pi];
				int i = passage.Nodes.Count >= 6 ? random.Range(2, passage.Nodes.Count - 3) : -1;
				int side = random.NextFloat() < 0.5f ? 0 : 1;
				if (i < 0 || passage.Kind == CavePassageKind.Link)
				{
					continue;
				}
				CaveNode node = passage.Nodes[i];
				var at = new Vector3(node.Centre.x, node.Floor, node.Centre.z);
				if (NearMouth(system, at) || Wet(system, at) || InAnyChamber(system, at, 1f) || system.NearChasmOrClimb(at + Vector3.up * 0.5f, 2f) || Taken(plan, at, 12f))
				{
					continue;
				}
				plan.Spots.Add(new CaveStorySpot
				{
					Kind = CaveStoryKind.Fall, At = at, Toward = Flat(passage.Nodes[i + 1].Centre - passage.Nodes[i - 1].Centre), Passage = pi, Chamber = -1, Solid = -1,
					Radius = node.Radius, Variant = side,
				});
				k++;
			}
		}

		// ── Track ─────────────────────────────────────────────────────

		private static void Rails(CaveSystem system, List<CaveDeadEnd> ends, CaveStoryPlan plan, DeterministicRNG random)
		{
			var railed = new List<(int passage, float from, float to)>();
			for (int pi = 0; pi < system.Passages.Count; pi++)
			{
				CavePassage passage = system.Passages[pi];
				float roll = random.NextFloat(), tub = random.NextFloat();
				int n = passage.Nodes.Count;
				if (passage.Kind != CavePassageKind.Branch || n < 3 || roll >= 0.7f)
				{
					continue;
				}
				CaveDeadEnd? end = null;
				foreach (CaveDeadEnd e in ends)
				{
					if (e.Passage == pi)
					{
						end = e;
					}
				}
				List<CaveNode> nodes = passage.Nodes;
				// Clear of the way it leaves, short of its end (of a fall at its end, by the fall's reach).
				float from = nodes[0].Along + nodes[0].Radius + 2.5f;
				float to = nodes[n - 1].Along - (end.HasValue && end.Value.Solid >= 0 ? 1.6f * end.Value.Radius + 1f : 1.2f);
				// Runs where the floor is near level, cut where it falls too steeply between nodes.
				float runStart = float.NaN;
				for (int i = 1; i < n; i++)
				{
					CaveNode a = nodes[i - 1], b = nodes[i];
					float run = b.Along - a.Along;
					bool level = run > 1e-3f && Mathf.Abs(b.Floor - a.Floor) / run <= RailGrade;
					if (level && float.IsNaN(runStart))
					{
						runStart = a.Along;
					}
					bool closes = !level || i == n - 1;
					if (closes && !float.IsNaN(runStart))
					{
						float runEnd = level ? b.Along : a.Along;
						float f0 = Mathf.Max(from, runStart), f1 = Mathf.Min(to, runEnd);
						if (f1 - f0 >= RailRunMin)
						{
							// A tub at the end of a drift that goes nowhere, when the run reaches it.
							bool atEnd = end.HasValue && f1 >= to - 0.5f && tub < 0.5f;
							plan.Spots.Add(new CaveStorySpot { Kind = CaveStoryKind.Rails, At = new Vector3(nodes[0].Centre.x, nodes[0].Floor, nodes[0].Centre.z), Passage = pi, Chamber = -1, Solid = -1,
								From = f0, To = f1, Radius = nodes[0].Radius, Variant = atEnd ? 1 : 0 });
							railed.Add((pi, f0, f1));
						}
						runStart = float.NaN;
					}
				}
			}
			// One tub tipped off its rails somewhere along them, now and then.
			float tip = random.NextFloat(), which = random.NextFloat(), where = random.NextFloat();
			if (railed.Count > 0 && tip < 0.45f)
			{
				(int passage, float from, float to) = railed[Mathf.Min(railed.Count - 1, Mathf.FloorToInt(which * railed.Count))];
				float along = Mathf.Lerp(from + 2f, to - 2f, where);
				CentreFloor(system.Passages[passage].Nodes, along, out Vector3 at);
				if (!NearMouth(system, at))
				{
					plan.Spots.Add(new CaveStorySpot { Kind = CaveStoryKind.Cart, At = at, Passage = passage, Chamber = -1, Solid = -1, From = along, To = along, Radius = system.Passages[passage].Nodes[0].Radius });
				}
			}
		}

		// ── Caches ────────────────────────────────────────────────────

		private static void Caches(CaveSystem system, List<CaveDeadEnd> ends, HashSet<int> fallen, CaveStoryPlan plan, List<Vector3> cached, DeterministicRNG random)
		{
			float[] odds = { 0.3f, 0.5f, 0.7f };
			float roll = random.NextFloat(), second = random.NextFloat();
			int wanted = roll < odds[Mathf.Clamp(system.SizeClass, 0, 2)] ? 1 + (system.SizeClass >= 2 && second < 0.35f ? 1 : 0) : 0;
			CaveStoryCacheKind kind = system.Form == CaveForm.Mine ? CaveStoryCacheKind.Miners
				: system.Form == CaveForm.SeaCave || system.Form == CaveForm.Grotto ? CaveStoryCacheKind.Smugglers : CaveStoryCacheKind.Adventurers;
			// The nooks, best first: alcoves, then open dead ends, then rooms far in (a mine's stopes).
			var nooks = new List<CaveStorySpot>();
			var ways = new List<CaveStorySpot>();
			var rooms = new List<CaveStorySpot>();
			for (int ci = 0; ci < system.Chambers.Count; ci++)
			{
				CaveChamber c = system.Chambers[ci];
				var at = new Vector3(c.Centre.x, CaveSystem.FloorOf(c, c.Centre), c.Centre.z);
				if (IsOwn(system, c) || NearMouth(system, at) || Wet(system, at))
				{
					continue;
				}
				var spot = new CaveStorySpot { Kind = CaveStoryKind.Cache, At = at, Toward = Vector3.zero, Passage = -1, Chamber = ci, Solid = -1, Radius = Mathf.Min(c.Radii.x, c.Radii.z), Variant = (int)kind };
				if (c.Kind == CaveChamberKind.Alcove)
				{
					spot.Toward = IntoAlcove(system, c);
					nooks.Add(spot);
				}
				else if (c.Kind == CaveChamberKind.Room || c.Kind == CaveChamberKind.Stope)
				{
					rooms.Add(spot);
				}
			}
			foreach (CaveDeadEnd e in ends)
			{
				if (!fallen.Contains(e.Passage) && !Wet(system, e.End))
				{
					ways.Add(new CaveStorySpot { Kind = CaveStoryKind.Cache, At = e.End - e.Toward * (0.3f * e.Radius), Toward = e.Toward, Passage = e.Passage, Chamber = -1, Solid = -1, Radius = e.Radius,
						Variant = (int)kind, DeadEnd = true });
				}
			}
			for (int k = 0; k < 2; k++)
			{
				float pick = random.NextFloat();
				List<CaveStorySpot> tier = nooks.Count > 0 ? nooks : ways.Count > 0 ? ways : rooms;
				if (k >= wanted || tier.Count == 0)
				{
					continue;
				}
				int i = Mathf.Min(tier.Count - 1, Mathf.FloorToInt(pick * tier.Count));
				CaveStorySpot chosen = tier[i];
				tier.RemoveAt(i);
				plan.Spots.Add(chosen);
				cached.Add(chosen.At);
			}
		}

		/// <summary>The way into an alcove: from the node of the passage that ends in it before its last, to its middle.</summary>
		private static Vector3 IntoAlcove(CaveSystem system, in CaveChamber c)
		{
			foreach (CavePassage p in system.Passages)
			{
				int n = p.Nodes.Count;
				if (n >= 2 && InFootprint(c, p.Nodes[n - 1].Centre, 1f))
				{
					return Flat(c.Centre - p.Nodes[Mathf.Max(0, n - 3)].Centre);
				}
			}
			return Vector3.zero;
		}

		// ── The lost ──────────────────────────────────────────────────

		private static void Lost(CaveSystem system, List<CaveDeadEnd> ends, HashSet<int> fallen, CaveStoryPlan plan, List<Vector3> cached, DeterministicRNG random)
		{
			bool mine = system.Form == CaveForm.Mine;
			float roll = random.NextFloat(), extra = random.NextFloat();
			int wanted = system.SizeClass == 0 ? (roll < 0.65f ? 1 : 0) : system.SizeClass == 1 ? 1 + (extra < 0.5f ? 1 : 0) : 2 + (extra < 0.5f ? 1 : 0);
			CaveStoryPose fell = mine ? CaveStoryPose.Miner : CaveStoryPose.Fallen;
			var candidates = new List<CaveStorySpot>();
			// At the foot of a climb, to one side of it: they fell.
			foreach (CaveClimb climb in system.Climbs)
			{
				Vector3 outward = Flat(climb.Out), side = Vector3.Cross(Vector3.up, outward);
				Vector3 at = climb.Bottom + outward * 1.2f + side * 2.2f;
				candidates.Add(new CaveStorySpot { Kind = CaveStoryKind.Remains, At = at, Toward = -side, Passage = -1, Chamber = -1, Solid = -1, Variant = (int)fell });
			}
			// Before a fall (it caught them), or sat down at the end of a way that went nowhere.
			foreach (CaveDeadEnd e in ends)
			{
				bool caught = fallen.Contains(e.Passage);
				Vector3 at = caught ? e.End - e.Toward * (1.7f * e.Radius + 1.2f) : e.End - e.Toward * (0.5f * e.Radius);
				candidates.Add(new CaveStorySpot { Kind = CaveStoryKind.Remains, At = at, Toward = caught ? Vector3.zero : e.Toward, Passage = e.Passage, Chamber = -1, Solid = -1, Radius = e.Radius,
					Variant = (int)(caught ? fell : CaveStoryPose.Seated), DeadEnd = true });
			}
			// In a nook, or against the wall of a room far in.
			for (int ci = 0; ci < system.Chambers.Count; ci++)
			{
				CaveChamber c = system.Chambers[ci];
				float heading = random.Range(0f, 360f), share = random.Range(0.45f, 0.8f);
				if (IsOwn(system, c) || c.Kind == CaveChamberKind.Cavern)
				{
					continue;
				}
				Vector3 dir = Dir(heading);
				Vector3 at = c.Kind == CaveChamberKind.Alcove ? c.Centre : c.Centre + dir * (share * Mathf.Min(c.Radii.x, c.Radii.z));
				at.y = CaveSystem.FloorOf(c, at);
				candidates.Add(new CaveStorySpot { Kind = CaveStoryKind.Remains, At = at, Toward = dir, Passage = -1, Chamber = ci, Solid = -1, Radius = Mathf.Min(c.Radii.x, c.Radii.z), Variant = (int)CaveStoryPose.Seated });
			}
			// In an order of their own, every candidate drawing its number whether taken or not.
			var order = new float[candidates.Count];
			for (int i = 0; i < order.Length; i++)
			{
				order[i] = random.NextFloat();
			}
			var index = new List<int>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
			{
				index.Add(i);
			}
			index.Sort((a, b) => order[a] != order[b] ? order[a].CompareTo(order[b]) : a.CompareTo(b));
			var laid = new List<Vector3>();
			foreach (int i in index)
			{
				if (laid.Count >= wanted)
				{
					break;
				}
				CaveStorySpot s = candidates[i];
				if (NearMouth(system, s.At) || Wet(system, s.At) || Near(laid, s.At, RemainsApart) || Near(cached, s.At, 6f))
				{
					continue;
				}
				plan.Spots.Add(s);
				laid.Add(s.At);
			}
		}

		// ── Carvings ──────────────────────────────────────────────────

		/// <summary>Whether a form's walls take carvings: a natural cave's rock (not a mine's cut rock, flowing ice or the waves' scour).</summary>
		public static bool Carves(CaveForm form) => form == CaveForm.Cave || form == CaveForm.Grotto || form == CaveForm.LavaTube;

		private static void Carvings(CaveSystem system, CaveStoryPlan plan, DeterministicRNG random)
		{
			float roll = random.NextFloat(), extra = random.NextFloat();
			if (!Carves(system.Form))
			{
				return;
			}
			int wanted = system.SizeClass == 0 ? (roll < 0.3f ? 1 : 0) : system.SizeClass == 1 ? (roll < 0.55f ? 1 : 0) : 1 + (extra < 0.4f ? 1 : 0);
			var candidates = new List<(int chamber, float order, int variant)>();
			for (int ci = 0; ci < system.Chambers.Count; ci++)
			{
				CaveChamber c = system.Chambers[ci];
				float order = random.NextFloat(), look = random.NextFloat();
				var at = new Vector3(c.Centre.x, CaveSystem.FloorOf(c, c.Centre), c.Centre.z);
				if (c.Kind == CaveChamberKind.Stope || IsOwn(system, c) || NearMouth(system, at, 18f) || Wet(system, at))
				{
					continue;
				}
				// A lava tube's builders cut runes; elsewhere any of the three.
				int variant = system.Form == CaveForm.LavaTube ? (look < 0.6f ? 1 : look < 0.8f ? 0 : 2) : Mathf.Min(2, Mathf.FloorToInt(look * 3f));
				candidates.Add((ci, order, variant));
			}
			candidates.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.chamber.CompareTo(b.chamber));
			var laid = new List<Vector3>();
			foreach ((int chamber, float _, int variant) in candidates)
			{
				if (laid.Count >= wanted)
				{
					break;
				}
				CaveChamber c = system.Chambers[chamber];
				var at = new Vector3(c.Centre.x, CaveSystem.FloorOf(c, c.Centre), c.Centre.z);
				if (Near(laid, at, CarvingsApart))
				{
					continue;
				}
				plan.Spots.Add(new CaveStorySpot { Kind = CaveStoryKind.Carving, At = at, Passage = -1, Chamber = chamber, Solid = -1, Radius = Mathf.Min(c.Radii.x, c.Radii.z), Variant = variant });
				laid.Add(at);
			}
		}

		// ── Geometry ──────────────────────────────────────────────────

		/// <summary>Within a mouth's clearance (<see cref="MouthClear"/>, or its zone where that is more, or <paramref name="clear"/>).</summary>
		public static bool NearMouth(CaveSystem system, Vector3 p, float clear = MouthClear)
		{
			foreach (CaveMouth mouth in system.Mouths)
			{
				float reach = Mathf.Max(clear, mouth.Zone);
				if ((p - mouth.Floor).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether a chamber is one site's own (its camp's, its den's, its dungeon gate's).</summary>
		public static bool IsOwn(CaveSystem system, in CaveChamber c)
		{
			foreach (CaveMouth mouth in system.Mouths)
			{
				if (system.TryChamberOf(mouth.SiteId, out CaveChamber own) && (own.Centre - c.Centre).sqrMagnitude < 0.01f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Under the system's standing water (a grotto's pool, a sea cave's tide).</summary>
		private static bool Wet(CaveSystem system, Vector3 p) => !float.IsNaN(system.WaterLevel) && system.Origin.y + p.y < system.WaterLevel + 0.3f;

		/// <summary>Inside a chamber's footprint grown by <paramref name="grow"/>, near its floor.</summary>
		private static bool InAnyChamber(CaveSystem system, Vector3 p, float grow)
		{
			foreach (CaveChamber c in system.Chambers)
			{
				if (InFootprint(c, p, grow) && Mathf.Abs(p.y - CaveSystem.FloorOf(c, p)) < 4f)
				{
					return true;
				}
			}
			return false;
		}

		private static bool InFootprint(in CaveChamber c, Vector3 p, float grow)
		{
			float r = c.Yaw * Mathf.Deg2Rad;
			float dx = p.x - c.Centre.x, dz = p.z - c.Centre.z;
			float u = (dx * Mathf.Sin(r) + dz * Mathf.Cos(r)) / Mathf.Max(0.5f, c.Radii.x + grow);
			float v = (dx * Mathf.Cos(r) - dz * Mathf.Sin(r)) / Mathf.Max(0.5f, c.Radii.z + grow);
			return u * u + v * v <= 1f;
		}

		/// <summary>Whether another passage (any but <paramref name="own"/>) runs within <paramref name="reach"/> of a point in plan, near its height.</summary>
		private static bool NearOtherWay(CaveSystem system, int own, Vector3 p, float reach)
		{
			for (int pi = 0; pi < system.Passages.Count; pi++)
			{
				if (pi == own)
				{
					continue;
				}
				List<CaveNode> nodes = system.Passages[pi].Nodes;
				for (int i = 0; i + 1 < nodes.Count; i++)
				{
					var a = new Vector3(nodes[i].Centre.x, nodes[i].Floor, nodes[i].Centre.z);
					var b = new Vector3(nodes[i + 1].Centre.x, nodes[i + 1].Floor, nodes[i + 1].Centre.z);
					Vector2 ab = Plan2(b - a), ap = Plan2(p - a);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if ((ap - ab * t).magnitude < reach && Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) < 4f)
					{
						return true;
					}
				}
			}
			return false;
		}

		private static bool NearPit(CaveSystem system, Vector3 p, float margin)
		{
			foreach (CavePit pit in system.Pits)
			{
				if (Plan2(p - pit.Rim).magnitude < pit.Radius + margin && p.y > pit.Bottom - 2f && p.y < pit.Rim.y + 4f)
				{
					return true;
				}
			}
			return false;
		}

		private static bool Taken(CaveStoryPlan plan, Vector3 p, float apart)
		{
			foreach (CaveStorySpot s in plan.Spots)
			{
				if ((s.At - p).sqrMagnitude < apart * apart)
				{
					return true;
				}
			}
			return false;
		}

		private static bool Near(List<Vector3> points, Vector3 p, float apart)
		{
			foreach (Vector3 q in points)
			{
				if ((q - p).sqrMagnitude < apart * apart)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The passage's floor point at an arc length (system frame), between its nodes.</summary>
		private static void CentreFloor(List<CaveNode> nodes, float along, out Vector3 at)
		{
			int i = 1;
			while (i < nodes.Count - 1 && nodes[i].Along < along)
			{
				i++;
			}
			CaveNode a = nodes[i - 1], b = nodes[i];
			float t = Mathf.Clamp01((along - a.Along) / Mathf.Max(1e-4f, b.Along - a.Along));
			Vector3 c = Vector3.Lerp(a.Centre, b.Centre, t);
			at = new Vector3(c.x, Mathf.Lerp(a.Floor, b.Floor, t), c.z);
		}

		private static Vector2 Plan2(Vector3 v) => new Vector2(v.x, v.z);

		private static Vector3 Flat(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
		}

		private static Vector3 Dir(float yaw)
		{
			float r = yaw * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
		}
	}
}
#endif
