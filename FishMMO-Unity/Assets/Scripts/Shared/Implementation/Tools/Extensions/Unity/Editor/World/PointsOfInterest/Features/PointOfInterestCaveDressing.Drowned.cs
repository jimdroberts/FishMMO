#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A cave's drowned galleries dressed (2026-10-25, phase I2n-c "drowned galleries", decision 86): what floats on its water (c),
	/// its water roots (d), its glow-worms threaded on the roots (e) and the roots screening its root windows (f). Each by the biome
	/// dressing the cave's inside (its interior where the host links one), each on its own random stream after every other pass,
	/// so nothing laid before moves; and each does nothing until that biome's template names it (no floats on its water, the
	/// hanging roots' art, glow-worms on the roofs, no root windows: every cave's until then).
	/// </summary>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>Whether the biome hangs water roots (its Cave Hangings' root art): set by the hanging pass before it lays its roots.</summary>
			private bool waterRoots;
			/// <summary>Where each root the hanging pass laid is fixed to the roof (system frame): what glow-worms on roots are threaded along.</summary>
			private readonly List<Vector3> rootHangs = new List<Vector3>();
			/// <summary>What the drowned passes laid: plants on the water, water roots, glow-worm colonies on roots, roots round root windows.</summary>
			private int floated, waterRooted, rootWorms, windowRoots;

			/// <summary>The most each pass lays a system.</summary>
			private const int MaxFloats = 600, MaxRootWorms = 80, MaxWindowRoots = 40;

			/// <summary>The drowned passes, called after the hanging pass (whose roots the glow-worms are threaded on).</summary>
			private void Drowned()
			{
				WormsOnRoots(new DeterministicRNG(unchecked(system.Seed * 0x2A6F + 0x13D9)));
				RootWindowRoots(new DeterministicRNG(unchecked(system.Seed * 0x7B05 + 0x4E61)));
				FloatsOnWater();
				if (floated + waterRooted + rootWorms + windowRoots > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"cave drowned galleries: {floated} floating on its water, {waterRooted} water roots, {rootWorms} glow-worm colonies on roots, {windowRoots} roots round its root windows");
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
				int own = CaveGlowPalettes.HueOf(CaveGlowKind.TapRoots, "WaterRoots", hue);
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

			// ── Glow-worms on the roots (e) ───────────────────

			/// <summary>
			/// Glow-worm colonies threaded along the roots the hanging pass laid, where the biome hangs its glow-worms on roots (its Cave
			/// Glows' mount, decision 86 (e)), in its glow-worms' hue (by name in the glow art; its first where the art has none so named):
			/// about half the roots at the biome's glow-worm chance, a small colony each, never low over a way.
			/// </summary>
			private void WormsOnRoots(DeterministicRNG random)
			{
				BiomeCaveGlow glow = CaveInteriorArt.Glow(biome, system.Form);
				if (glow.WormMount != BiomeCaveWormMount.Roots || glow.Worms <= 0f || rootHangs.Count == 0)
				{
					return;
				}
				int hue = string.IsNullOrEmpty(glow.WormHue) ? 0 : CaveGlowPalettes.HueOf(CaveGlowKind.GlowWorms, glow.WormHue, 0);
				int variants = CaveArt.VariantCount(CaveGlowKind.GlowWorms);
				float keep = Mathf.Min(0.85f, 0.45f * glow.Worms);
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
						if (!CaveArt.TryGlow(CaveGlowKind.HangingRoots, hue, Mathf.FloorToInt(pick * 991f), out CaveGlowArt roots))
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
