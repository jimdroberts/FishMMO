#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A cave's drowned galleries dressed (2026-10-25, phase I2n-c "drowned galleries", decision 86): what floats on its water and its
	/// stream pools (c), its water roots (d), its glow-worms threaded on the roots or hung on drowned and fallen logs (e) and the
	/// roots screening its root windows (f). Each by the biome dressing the cave's inside (its interior where the host links one),
	/// each on its own random stream after every other pass, so nothing laid before moves; and each does nothing until that
	/// biome's template names it (no floats on its water, the hanging roots' art, glow-worms on the roofs, no root windows: every
	/// cave's until then).
	/// </summary>
	public sealed partial class PointOfInterestCaveDressing
	{
		/// <summary>The root art's hue water roots are drawn in (ProceduralArtCatalogue.RootHues, decision 86 (d)).</summary>
		public const string WaterRootsHueName = "WaterRoots";

		private sealed partial class Dresser
		{
			/// <summary>Whether the biome hangs water roots (its Cave Hangings' root art): set by the hanging pass before it lays its roots.</summary>
			private bool waterRoots;
			/// <summary>Where each root the hanging pass laid is fixed to the roof (system frame): what glow-worms on roots are threaded along.</summary>
			private readonly List<Vector3> rootHangs = new List<Vector3>();
			/// <summary>
			/// What the drowned passes laid: plants on the water (its level's and its stream pools'), water roots, glow-worm colonies on
			/// roots, roots round root windows, logs and the colonies on them.
			/// </summary>
			private int floated, waterRooted, rootWorms, windowRoots, logs, woodWorms;

			/// <summary>The most each pass lays a system.</summary>
			private const int MaxFloats = 600, MaxRootWorms = 80, MaxWindowRoots = 40, MaxWoodWorms = 60;

			/// <summary>
			/// The drowned passes, called after the hanging pass (whose roots the glow-worms are threaded on): the roots round the root
			/// windows first, so their roots carry glow-worms too.
			/// </summary>
			private void Drowned()
			{
				RootWindowRoots(new DeterministicRNG(unchecked(system.Seed * 0x7B05 + 0x4E61)));
				WormsOnRoots(new DeterministicRNG(unchecked(system.Seed * 0x2A6F + 0x13D9)));
				FloatsOnWater();
				FloatsOnStreamPools();
				DrownedWood(new DeterministicRNG(unchecked(system.Seed * 0x3D2B + 0x61A7)));
				if (floated + waterRooted + rootWorms + windowRoots + logs > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"cave drowned galleries: {floated} floating on its water, {waterRooted} water roots, {rootWorms} glow-worm colonies on roots, {windowRoots} roots round its root windows, {logs} logs with {woodWorms} glow-worm colonies on them");
				}
			}

			// ── Water roots (d) ───────────────────────────────

			/// <summary>
			/// A tap root over the cave's water hung down to the water's surface, its tassels a hand's depth into it (decision 86 (d):
			/// "tap roots reach the water surface, not the floor, where it floods"), in the root art's WaterRoots hue where the art
			/// has one. False where it does not fit (too short a drop or too long), so the caller hangs its tangle as before.
			/// </summary>
			private bool WaterRoot(Vector3 ground, float roof, int hue, float pick, float yaw, float girth)
			{
				float surface = system.WaterLevel - system.Origin.y;
				float drop = roof - surface;
				if (float.IsNaN(drop) || drop < 0.6f || drop > CaveHanging.TapRoomMax)
				{
					return false;
				}
				int own = CaveGlowPalettes.HueOf(CaveGlowKind.TapRoots, WaterRootsHue, hue);
				if (!CaveArt.TryGlow(CaveGlowKind.TapRoots, own, Mathf.FloorToInt(pick * 997f), out CaveGlowArt tap))
				{
					return false;
				}
				float sy = CaveHanging.TapScaleY(drop + 0.15f, tap.Height);
				if (!Place(tap.Prefab, new Vector3(ground.x, roof + 0.03f, ground.z), Quaternion.Euler(0f, yaw, 0f), new Vector3(girth, sy, girth)))
				{
					return false;
				}
				waterRooted++;
				rootHangs.Add(new Vector3(ground.x, roof, ground.z));
				return true;
			}

			private const string WaterRootsHue = WaterRootsHueName;

			/// <summary>A hanging-roots hue as the biome hangs it: its water roots' where its root art is theirs (the art's own otherwise).</summary>
			private int HangingRootsHue(int hue) => waterRoots ? CaveGlowPalettes.HueOf(CaveGlowKind.HangingRoots, WaterRootsHue, hue) : hue;

			// ── Glow-worms on the roots (e) ───────────────────

			/// <summary>
			/// Glow-worm colonies threaded along the roots the hanging pass laid, where the biome hangs its glow-worms on roots (its Cave
			/// Glows' mount, decision 86 (e)), in its glow-worms' hue (the palette's: by name in the glow art; its first where the art has
			/// none so named): about half the roots at the biome's glow-worm chance, a small colony each, never low over a way.
			/// </summary>
			private void WormsOnRoots(DeterministicRNG random)
			{
				CaveGlowPalette colours = Colours();
				if (colours.WormMount != BiomeCaveWormMount.Roots || !colours.Worms || colours.WormChance <= 0f || rootHangs.Count == 0)
				{
					return;
				}
				int hue = colours.WormHue;
				int variants = CaveArt.VariantCount(CaveGlowKind.GlowWorms);
				float keep = Mathf.Min(0.85f, 0.45f * colours.WormChance);
				foreach (Vector3 root in rootHangs)
				{
					// Every draw before any test, so a refused colony never shifts the next.
					float roll = random.NextFloat(), a = random.Range(0f, 2f * Mathf.PI), r = random.Range(0.2f, 0.7f), scale = random.Range(0.35f, 0.6f), yaw = random.Range(0f, 360f);
					int variant = random.Range(0, Mathf.Max(1, variants));
					if (roll >= keep || rootWorms >= MaxRootWorms)
					{
						continue;
					}
					var p = new Vector3(root.x + Mathf.Cos(a) * r, root.y - 0.5f, root.z + Mathf.Sin(a) * r);
					if (!Roof(p, 2f, out float roof) || !Ground(p, 40f, out float floor) || !CaveArt.TryGlow(CaveGlowKind.GlowWorms, hue, variant, out CaveGlowArt art))
					{
						continue;
					}
					var under = new Vector3(p.x, floor, p.z);
					if ((InLane(under, 0f) || OnWay(under, 0f)) && roof - floor - art.Height * scale < Headroom)
					{
						continue;
					}
					if (Place(art.Prefab, new Vector3(p.x, roof + 0.03f, p.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale))
					{
						rootWorms++;
						Glowing++;
					}
				}
			}

			// ── Root windows (f) ──────────────────────────────

			/// <summary>
			/// The host's tree roots hung round each root window's throat (CaveSystem.IsRootWindow, decision 86 (f): "screened by
			/// host-tree roots"): a ring of curtains at the vault round the opening, in the biome's root art, down to the water where
			/// the floor under them is flooded.
			/// </summary>
			private void RootWindowRoots(DeterministicRNG random)
			{
				for (int m = 0; m < system.Mouths.Count; m++)
				{
					CaveMouth mouth = system.Mouths[m];
					if (!CaveSystem.IsRootWindow(in mouth))
					{
						continue;
					}
					float vault = CaveSystem.SkylightVault(in mouth);
					int curtains = random.Range(5, 10);
					for (int k = 0; k < curtains; k++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), r = mouth.Radius * random.Range(0.85f, 1.35f), pick = random.NextFloat(), yaw = random.Range(0f, 360f);
						float girth = random.Range(0.8f, 1.2f);
						if (windowRoots >= MaxWindowRoots)
						{
							continue;
						}
						var p = new Vector3(mouth.Floor.x + Mathf.Cos(a) * r, vault - 0.6f, mouth.Floor.z + Mathf.Sin(a) * r);
						if (!Roof(p, 3f, out float roof) || !Ground(p, 40f, out float floor))
						{
							continue;
						}
						var ground = new Vector3(p.x, floor, p.z);
						int hue = pick < 0.65f ? 0 : 1;
						if (waterRoots && Submerged(ground) && WaterRoot(ground, roof, hue, pick, yaw, girth))
						{
							windowRoots++;
							continue;
						}
						if (!CaveArt.TryGlow(CaveGlowKind.HangingRoots, HangingRootsHue(hue), Mathf.FloorToInt(pick * 991f), out CaveGlowArt roots))
						{
							Missing.Add("cave glow art");
							continue;
						}
						float s = HangScale(ground, roof - floor, roots.Height, girth);
						if (s >= 0.35f && Place(roots.Prefab, new Vector3(p.x, roof + 0.03f, p.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * s))
						{
							windowRoots++;
							rootHangs.Add(new Vector3(p.x, roof, p.z));
						}
					}
				}
			}

			// ── What floats on the water (c) ──────────────────

			private Dictionary<BiomeFloatingPlantKind, GameObject> floatPrefabs;

			/// <summary>
			/// The plants and litter on the cave's water (decision 86 (c), the still-water floats of phase I2i: TanninFoam and its kin), by
			/// the biome's Floating Plants: one chance a lattice point over the water the system holds at its level (a drowned gallery's, a
			/// grotto's pool, a sump's) of a cave whose host links an interior, kept by the kind's cover at that depth and distance from the wall, as on any still water
			/// (FloatingPlants.TryPlant: a cave's water is sheltered, its shelter 1). None where the biome's list is empty: every
			/// biome's until its round.
			/// </summary>
			private void FloatsOnWater()
			{
				// A linked interior's only: a host's own still-water plants never went into its caves (FloatingPlants.IsGrottoPool), and
				// still do not.
				if (float.IsNaN(system.WaterLevel) || !caves.Linked)
				{
					return;
				}
				IReadOnlyList<BiomeFloatingPlant> plants = BiomeTemplateArt.FloatingPlants(caves.Interior);
				if (!BiomeFloatingPlants.Any(plants))
				{
					return;
				}
				float level = system.WaterLevel, y = level - system.Origin.y, cell = FloatingPlants.CellMetres;
				Bounds extent = system.Extent;
				uint seed = unchecked((uint)system.Seed * 0x9E3779B1u ^ 0x5A17C0DEu);
				int i0 = Mathf.CeilToInt((system.Origin.x + extent.min.x) / cell), i1 = Mathf.FloorToInt((system.Origin.x + extent.max.x) / cell);
				int j0 = Mathf.CeilToInt((system.Origin.z + extent.min.z) / cell), j1 = Mathf.FloorToInt((system.Origin.z + extent.max.z) / cell);
				for (int j = j0; j <= j1 && floated < MaxFloats; j++)
				{
					for (int i = i0; i <= i1 && floated < MaxFloats; i++)
					{
						float wx = (i + 0.4f * (FloatingPlants.Unit(FloatingPlants.Hash(seed, i, j, 5)) * 2f - 1f)) * cell;
						float wz = (j + 0.4f * (FloatingPlants.Unit(FloatingPlants.Hash(seed, i, j, 6)) * 2f - 1f)) * cell;
						var p = new Vector3(wx - system.Origin.x, y, wz - system.Origin.z);
						// Open air over the surface and water under it, inside the cave.
						if (system.Void(p + Vector3.up * 0.1f) >= 0f || system.Void(p - Vector3.up * 0.3f) >= 0f || !Ground(p, 6f, out float floor))
						{
							continue;
						}
						float depth = y - floor;
						if (!(depth > 0.05f))
						{
							continue;
						}
						if (!FloatingPlants.TryPlant(seed, i, j, wx, wz, level, depth, WallDistance(p), 1f, plants, out FloatingPlants.Plant plant))
						{
							continue;
						}
						GameObject prefab = FloatPrefab(plant.Kind);
						if (prefab == null)
						{
							Missing.Add($"floating {plant.Kind}");
							continue;
						}
						if (Place(prefab, plant.Position - system.Origin, Quaternion.Euler(0f, plant.Yaw, 0f), Vector3.one * plant.Scale))
						{
							floated++;
						}
					}
				}
			}

			/// <summary>
			/// The same floats on the cave's stream pools (2026-10-26, I2n-c: "floats on stream pools"), each pool at its own level: the
			/// lattice points within its rim (FloatingPlants.TryPlant at its depth and distance from the rim), for a linked interior's
			/// Floating Plants only, as on its level's water. Laid as on any still water (a pool's water is no stream's to refuse them).
			/// </summary>
			private void FloatsOnStreamPools()
			{
				CaveStreamPlan plan = StreamPlan;
				if (!caves.Linked || !plan.Active || plan.Frozen || plan.Bodies.Count == 0)
				{
					return;
				}
				IReadOnlyList<BiomeFloatingPlant> plants = BiomeTemplateArt.FloatingPlants(caves.Interior);
				if (!BiomeFloatingPlants.Any(plants))
				{
					return;
				}
				float cell = FloatingPlants.CellMetres;
				for (int b = 0; b < plan.Bodies.Count && floated < MaxFloats; b++)
				{
					CaveStreamBody body = plan.Bodies[b];
					if (body.Kind != CaveStreamKind.Pool || body.Path == null || body.Path.Length == 0 || !(body.Width > 0.3f))
					{
						continue;
					}
					Vector3 c = body.Path[0];
					// A pool under the level's own water is the level's (its floats are FloatsOnWater's).
					if (!float.IsNaN(system.WaterLevel) && system.Origin.y + c.y <= system.WaterLevel + 0.05f)
					{
						continue;
					}
					float level = system.Origin.y + c.y, r = body.Width;
					uint seed = unchecked((uint)system.Seed * 0x85EBCA6Bu ^ (uint)(b + 1) * 0xC2B2AE35u ^ 0x2F00D5EEu);
					int i0 = Mathf.CeilToInt((system.Origin.x + c.x - r) / cell), i1 = Mathf.FloorToInt((system.Origin.x + c.x + r) / cell);
					int j0 = Mathf.CeilToInt((system.Origin.z + c.z - r) / cell), j1 = Mathf.FloorToInt((system.Origin.z + c.z + r) / cell);
					for (int j = j0; j <= j1 && floated < MaxFloats; j++)
					{
						for (int i = i0; i <= i1 && floated < MaxFloats; i++)
						{
							float wx = (i + 0.4f * (FloatingPlants.Unit(FloatingPlants.Hash(seed, i, j, 5)) * 2f - 1f)) * cell;
							float wz = (j + 0.4f * (FloatingPlants.Unit(FloatingPlants.Hash(seed, i, j, 6)) * 2f - 1f)) * cell;
							var p = new Vector3(wx - system.Origin.x, c.y, wz - system.Origin.z);
							float rim = r - new Vector2(p.x - c.x, p.z - c.z).magnitude;
							if (rim < 0.1f || system.Void(p + Vector3.up * 0.1f) >= 0f || !Ground(p, 6f, out float floor))
							{
								continue;
							}
							float depth = c.y - floor;
							if (!(depth > 0.05f) || !FloatingPlants.TryPlant(seed, i, j, wx, wz, level, depth, Mathf.Min(rim, WallDistance(p)), 1f, plants, out FloatingPlants.Plant plant))
							{
								continue;
							}
							GameObject prefab = FloatPrefab(plant.Kind);
							if (prefab == null)
							{
								Missing.Add($"floating {plant.Kind}");
								continue;
							}
							if (!InLava(plant.Position - system.Origin))
							{
								context.AddProp(prefab, plant.Position, Quaternion.Euler(0f, plant.Yaw, 0f), Vector3.one * plant.Scale);
								Placed++;
								floated++;
							}
						}
					}
				}
			}

			// ── Drowned and fallen wood (e: WormMount Wood) ───

			/// <summary>The logs a wood-mounted cave lies: (deadwood species, the least and most scale).</summary>
			private static readonly (string name, float min, float max)[] DrownedLogs = { ("FallenLog_Birch", 0.5f, 0.75f), ("Driftwood", 0.75f, 1.05f) };

			/// <summary>
			/// Logs the water carried in, and the glow-worm colonies hung along them (decision 86 (e): WormMount Wood, "larvae thread the
			/// drowned logs"), where the biome hangs its glow-worms on wood: about one log a 40 m of the cave's ways
			/// (BiomeCaveWaters.LogsWanted), lying along a way's side, half sunk and awash where the floor is under the water, else fallen
			/// on the floor off the trodden lane; on each a few small colonies on its flanks, their threads short of the water or the
			/// floor, in the worms' hue. Every draw of a try before any test of it.
			/// </summary>
			private void DrownedWood(DeterministicRNG random)
			{
				CaveGlowPalette colours = Colours();
				if (colours.WormMount != BiomeCaveWormMount.Wood || !colours.Worms || colours.WormChance <= 0f || system.Passages.Count == 0)
				{
					return;
				}
				float metres = 0f;
				foreach (CavePassage passage in system.Passages)
				{
					if (passage.Nodes.Count >= 2)
					{
						metres += passage.Nodes[passage.Nodes.Count - 1].Along - passage.Nodes[0].Along;
					}
				}
				int want = BiomeCaveWaters.LogsWanted(metres, colours.WormChance);
				int variants = CaveArt.VariantCount(CaveGlowKind.GlowWorms);
				float water = float.IsNaN(system.WaterLevel) ? float.NaN : system.WaterLevel - system.Origin.y;
				for (int t = 0; t < want * 6 && logs < want; t++)
				{
					int which = random.Range(0, system.Passages.Count), kind = random.Range(0, DrownedLogs.Length);
					float share = random.NextFloat(), side = random.NextFloat() < 0.5f ? -1f : 1f, across = random.Range(0.35f, 0.7f), turn = random.Range(-14f, 14f);
					float size = random.NextFloat(), sink = random.Range(0.2f, 0.45f);
					int colonies = random.Range(2, 6);
					List<CaveNode> nodes = system.Passages[which].Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float along = Mathf.Lerp(nodes[0].Along, nodes[nodes.Count - 1].Along, share);
					CentreAt(nodes, along, out Vector3 c, out float nominal);
					CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
					Vector3 dir = Flat(ahead - c);
					if (dir.sqrMagnitude < 1e-6f)
					{
						continue;
					}
					dir = Quaternion.AngleAxis(turn, Vector3.up) * dir.normalized;
					Vector3 flank = Vector3.Cross(Vector3.up, dir);
					Vector3 sideways = flank * (side * across * RadiusAt(nodes, along));
					var p = new Vector3(c.x + sideways.x, nominal + 0.8f, c.z + sideways.z);
					if (!Ground(p, 4f, out float floor))
					{
						continue;
					}
					(string name, float min, float max) log = DrownedLogs[kind];
					DeadwoodSpecies species = DeadwoodOf(log.name);
					float scale = Mathf.Lerp(log.min, log.max, size), half = 0.5f * species.Length * scale, r = species.Radius * scale;
					var ground = new Vector3(p.x, floor, p.z);
					bool drowned = Submerged(ground);
					// Its axis: awash in the water (a share of its girth under), or lying on the floor.
					float axis = drowned ? water + r * (1f - 2f * sink) : floor + r * 0.74f;
					if (drowned && axis - r < floor)
					{
						axis = floor + r * 0.74f;
					}
					if (!drowned && (InLane(ground, 0.4f) || OnWay(ground, 0.4f)))
					{
						continue;
					}
					// Room for all of it: its ends and middle in the air over it, clear of the wall either side.
					bool fits = true;
					for (int k = -2; k <= 2 && fits; k++)
					{
						Vector3 q = new Vector3(p.x, axis + r + 0.15f, p.z) + dir * (half * k * 0.5f);
						fits = system.Void(q) < 0f && system.Void(q + flank * (r + 0.2f)) < 0f && system.Void(q - flank * (r + 0.2f)) < 0f;
					}
					GameObject prefab = fits ? DeadwoodPrefabOf(log.name) : null;
					if (prefab == null)
					{
						if (fits)
						{
							Missing.Add(ProceduralArtCatalogue.DeadwoodPrefab(log.name));
						}
						continue;
					}
					// The log's length lies along its own x; its pivot under its axis by 0.74 of its girth (DeadwoodMeshes).
					float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;
					var foot = new Vector3(p.x, axis - r * 0.74f, p.z);
					// Laid as the water left it: a drowned log is no stream's to refuse.
					if (InLava(foot))
					{
						continue;
					}
					context.AddProp(prefab, system.Origin + foot, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
					Placed++;
					logs++;
					// The colonies: on its flanks a little over its axis, alternately each side, their threads short of the water or floor.
					for (int k = 0; k < colonies; k++)
					{
						float at = random.Range(-0.8f, 0.8f) * half, keep = random.NextFloat(), wscale = random.Range(0.22f, 0.38f), wyaw = random.Range(0f, 360f);
						int variant = random.Range(0, Mathf.Max(1, variants));
						if (keep >= Mathf.Min(0.9f, 0.5f * colours.WormChance) || woodWorms >= MaxWoodWorms
							|| !CaveArt.TryGlow(CaveGlowKind.GlowWorms, colours.WormHue, variant, out CaveGlowArt art))
						{
							continue;
						}
						float below = (drowned ? water : floor) + 0.05f;
						Vector3 hang = new Vector3(p.x, axis + 0.25f * r, p.z) + dir * at + flank * ((k % 2 == 0 ? 1f : -1f) * r * 0.95f);
						if (hang.y - art.Height * wscale < below || system.Void(hang) >= 0f)
						{
							continue;
						}
						context.AddProp(art.Prefab, system.Origin + hang, Quaternion.Euler(0f, wyaw, 0f), Vector3.one * wscale);
						Placed++;
						woodWorms++;
						Glowing++;
					}
				}
			}

			private Dictionary<string, GameObject> deadwoodPrefabs;

			private GameObject DeadwoodPrefabOf(string name)
			{
				deadwoodPrefabs ??= new Dictionary<string, GameObject>();
				if (!deadwoodPrefabs.TryGetValue(name, out GameObject prefab))
				{
					prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(ProceduralArtCatalogue.DeadwoodPrefab(name)));
					deadwoodPrefabs[name] = prefab;
				}
				return prefab;
			}

			/// <summary>A deadwood species by name (its length and girth: what a log's room is measured against).</summary>
			private static DeadwoodSpecies DeadwoodOf(string name)
			{
				foreach (DeadwoodSpecies d in ProceduralArtCatalogue.Deadwood)
				{
					if (d.Name == name)
					{
						return d;
					}
				}
				return new DeadwoodSpecies { Name = name, Length = 4f, Radius = 0.16f };
			}

			/// <summary>How far a point on the water (system frame) lies from the cave's wall at its surface, metres (to 6 m, in 0.5 m steps on 8 bearings).</summary>
			private float WallDistance(Vector3 p)
			{
				float least = 6f;
				for (int k = 0; k < 8; k++)
				{
					float a = k * Mathf.PI * 0.25f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
					for (float d = 0.5f; d < least; d += 0.5f)
					{
						if (system.Void(new Vector3(p.x + ca * d, p.y + 0.1f, p.z + sa * d)) >= 0f)
						{
							least = d;
							break;
						}
					}
				}
				return least;
			}

			private GameObject FloatPrefab(BiomeFloatingPlantKind kind)
			{
				floatPrefabs ??= new Dictionary<BiomeFloatingPlantKind, GameObject>();
				if (!floatPrefabs.TryGetValue(kind, out GameObject prefab))
				{
					string name = ProceduralArtCatalogue.FloatingPrefab(kind);
					prefab = name != null ? AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name)) : null;
					floatPrefabs[kind] = prefab;
				}
				return prefab;
			}
		}
	}
}
#endif
