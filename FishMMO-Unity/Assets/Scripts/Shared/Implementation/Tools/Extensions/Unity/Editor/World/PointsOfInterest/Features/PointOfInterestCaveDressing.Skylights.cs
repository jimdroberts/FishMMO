#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>Skylights dressed, and the pieces laid round them (for the note).</summary>
			public int SkylightsDressed, SkylightPieces;

			/// <summary>How far past the opening's lip, beyond its collar, the growth on the ground round it reaches, metres.</summary>
			private const float RimGrowth = 4.5f;
			/// <summary>How much of the cover between the vault and the ground hanging roots may reach down through, metres.</summary>
			private const float RootCover = 8f;
			/// <summary>How far from the terrain's holes the boulders and the growth on the hill keep, metres: off the cut, and the growth off the rock lip round it too.</summary>
			private const float StoneHoleClear = 0.8f, GreenHoleClear = 2.2f;

			/// <summary>The terrain's holes widened for the boulders and for the growth (read once, for a cave with an opening in its roof).</summary>
			private System.Func<float, float, bool> stoneHoles, greenHoles;
			private bool holesRead;

			/// <summary>
			/// Daylight through the roof (2026-10-20, caves livened; CaveSystem.Skylights.cs): for each opening in a chamber's
			/// roof, the light it lets in (a <see cref="SceneEmitterKind.Skylight"/> on the lit floor: the shaft, the motes, the
			/// sky's share round it and a soft light off the sunlit floor, all the client's), and what grows and lies round it —
			/// on the hill, a broken ring of boulders and the biome's ferns, bushes and creeper thick at the lip, so the pit is seen
			/// before it is walked into; down the throat, the biome's vines spilling in over the lip and roots hanging from a thin
			/// vault; on the floor, the blocks of the roof that fell to open it, and now and then a trunk that came down with it.
			/// The sunlit floor's own green is the vines' pass's (PointOfInterestCaveDressing.Vines.cs SunlitFloor). Each opening
			/// on a stream of its own, after every earlier pass, so nothing they laid moves.
			/// </summary>
			private void Skylights()
			{
				for (int m = 0; m < system.Mouths.Count; m++)
				{
					CaveMouth mouth = system.Mouths[m];
					if (mouth.Kind != CaveMouthKind.Skylight)
					{
						continue;
					}
					var random = new DeterministicRNG(unchecked(system.Seed * 0x7A3B + 0x1C55 + m * 0x9E37));
					Skylight(random, mouth, m);
					SkylightsDressed++;
				}
				if (SkylightsDressed > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"skylights: {SkylightsDressed} opening(s) in the roof, {SkylightPieces} piece(s) round them");
				}
			}

			private void Skylight(DeterministicRNG random, in CaveMouth mouth, int index)
			{
				Vector3 opening = CaveSystem.SkylightOpening(mouth);
				float radius = mouth.Radius;
				// The light it lets in, over the lit floor: the client reads the opening's height off its seed.
				Vector3 lit = mouth.Floor + Vector3.up * SceneSkylight.LitOver;
				uint hash = unchecked((uint)(system.Seed * 0x2C1B3C6D) ^ (uint)(index * 0x297A2D39));
				Emit(lit, SceneEmitterKind.Skylight, SceneSkylight.Seed(opening.y - lit.y, hash), radius);

				growth ??= PointOfInterestCaveMouths.GrowthFor(PointOfInterestCaveMouths.BiomeOf(context, system));
				if (!holesRead)
				{
					holesRead = true;
					if (context.Terrains != null)
					{
						stoneHoles = PointOfInterestTerrain.HoleQuery(context.Terrains, context.Tiles, StoneHoleClear);
						greenHoles = PointOfInterestTerrain.HoleQuery(context.Terrains, context.Tiles, GreenHoleClear);
					}
				}
				RimStones(random, mouth, opening);
				RimGreen(random, mouth, opening);
				SpillIn(random, mouth, opening);
				VaultRoots(random, mouth, opening);
				FallenIn(random, mouth);
			}

			/// <summary>
			/// A broken ring of boulders on the hill round the lip, sunk a third into the ground, with gaps between: the pit's edge
			/// seen from afar, and something to stop at (their colliders are the props'). Off the collar, never over the hole.
			/// </summary>
			private void RimStones(DeterministicRNG random, in CaveMouth mouth, Vector3 opening)
			{
				if (growth.Boulders.Count == 0 || context.GroundAt == null)
				{
					return;
				}
				float ring = mouth.Radius * (1f + CaveSystem.SkylightFlare) + mouth.Collar + 0.6f;
				int count = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * ring / 4.5f), 4, 14);
				float start = random.Range(0f, 360f);
				for (int i = 0; i < count; i++)
				{
					float a = start + i * 360f / count + random.Range(-12f, 12f), wander = random.Range(0f, 1.6f), size = random.Range(0.45f, 1f);
					float gap = random.NextFloat(), yaw = random.Range(0f, 360f), tilt = random.Range(-10f, 10f);
					int pick = random.Range(0, growth.Boulders.Count);
					// A gap now and then: a ring, not a wall.
					if (gap < 0.25f)
					{
						continue;
					}
					Vector3 p = Surface(opening, a, ring + wander + 0.5f * size, stoneHoles);
					if (float.IsNaN(p.y))
					{
						continue;
					}
					p.y -= 0.3f * size;
					if (Place(growth.Boulders[pick], p, Quaternion.Euler(tilt, yaw, -tilt), Vector3.one * size))
					{
						SkylightPieces++;
					}
				}
			}

			/// <summary>
			/// The biome's ferns, bushes and creeper on the ground round the lip, thick at it and thinning out over
			/// <see cref="RimGrowth"/>: the green ring a pit in the hill wears, fed by the damp air breathing out of it.
			/// </summary>
			private void RimGreen(DeterministicRNG random, in CaveMouth mouth, Vector3 opening)
			{
				if (growth.Species.Count == 0 || context.GroundAt == null)
				{
					return;
				}
				float inner = mouth.Radius * (1f + CaveSystem.SkylightFlare) + 0.4f;
				float outer = inner + mouth.Collar + RimGrowth;
				int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * (outer * outer - inner * inner) * 0.18f), 8, 90);
				for (int i = 0; i < count; i++)
				{
					float a = random.Range(0f, 360f), t = random.NextFloat(), pick = random.NextFloat(), size = random.NextFloat(), yaw = random.Range(0f, 360f);
					float r = Mathf.Lerp(inner, outer, t * t);
					float near = 1f - t;
					if (random.NextFloat() > 0.35f + 0.65f * near)
					{
						continue;
					}
					Vector3 p = Surface(opening, a, r, greenHoles);
					if (float.IsNaN(p.y))
					{
						continue;
					}
					int species;
					float scale;
					if (growth.Bushes.Count > 0 && pick < 0.22f && r > inner + 1.2f)
					{
						species = growth.Bushes[Mathf.FloorToInt(size * 997f) % growth.Bushes.Count];
						scale = 0.6f + 0.5f * size;
					}
					else if (growth.Creeper >= 0 && pick < 0.5f)
					{
						species = growth.Creeper;
						scale = 0.8f + 0.5f * size;
					}
					else if (growth.Ferns.Count > 0)
					{
						species = growth.Ferns[Mathf.FloorToInt(size * 991f) % growth.Ferns.Count];
						scale = (0.7f + 0.4f * near) * (0.8f + 0.4f * size);
					}
					else
					{
						continue;
					}
					if (Place(growth.Species[species], p - Vector3.up * 0.03f, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale))
					{
						SkylightPieces++;
					}
				}
			}

			/// <summary>The biome's vines spilling in over the lip and down the throat's wall, curtains a few metres long round it.</summary>
			private void SpillIn(DeterministicRNG random, in CaveMouth mouth, Vector3 opening)
			{
				// The biome's vines, read once with the cave's plan (PointOfInterestCaveDressing.Fungi.cs).
				_ = Plan;
				if (vines == null || vines.Species == null || vines.Species.Length == 0)
				{
					return;
				}
				float depth = opening.y - CaveSystem.SkylightVault(mouth);
				int count = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * mouth.Radius / 2.2f), 3, 16);
				float start = random.Range(0f, 360f);
				for (int i = 0; i < count; i++)
				{
					float a = start + i * 360f / count + random.Range(-10f, 10f), pick = random.NextFloat(), roll = random.NextFloat();
					float hang = random.Range(1.5f, 5.5f), width = random.Range(1.2f, 2.4f), down = random.Range(0.3f, 0.9f);
					if (roll > 0.75f)
					{
						continue;
					}
					// The throat's wall a little under the lip, found from its axis outward.
					float y = opening.y - down;
					var dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad));
					var from = new Vector3(mouth.Floor.x, y, mouth.Floor.z);
					if (system.Void(from) >= 0f)
					{
						continue;
					}
					Vector3 wall = from;
					bool found = false;
					for (float d = 0.25f; d <= mouth.Radius * (1f + CaveSystem.SkylightFlare) + 3f; d += 0.25f)
					{
						Vector3 q = from + dir * d;
						if (system.Void(q) >= 0f)
						{
							found = true;
							break;
						}
						wall = q;
					}
					if (!found)
					{
						continue;
					}
					VineSpot curtain = VineRules.Curtain(vines.Pick(pick), wall + Vector3.up * 0.1f, VineRules.YawOf(-dir), Mathf.Min(hang, Mathf.Max(1f, depth - down)), width, random);
					LayVine(in curtain);
					SkylightPieces++;
				}
			}

			/// <summary>
			/// Where the vault round the throat's foot lies under little enough ground (<see cref="RootCover"/>), the roots of
			/// what grows round the pit hang down out of it: thinning out from the throat.
			/// </summary>
			private void VaultRoots(DeterministicRNG random, in CaveMouth mouth, Vector3 opening)
			{
				if (context.GroundAt == null || !CaveArt.TryGlow(CaveGlowKind.HangingRoots, 0, 0, out _))
				{
					return;
				}
				float vault = CaveSystem.SkylightVault(mouth);
				int count = Mathf.Clamp(Mathf.RoundToInt(mouth.Radius * 2.5f), 4, 18);
				for (int i = 0; i < count; i++)
				{
					float a = random.Range(0f, 2f * Mathf.PI), r = mouth.Radius * (1f + CaveSystem.SkylightFlare) + random.Range(0.5f, 4f);
					float scale = random.Range(0.7f, 1.2f), yaw = random.Range(0f, 360f);
					int variant = random.Range(0, CaveArt.VariantCount(CaveGlowKind.HangingRoots)), hue = random.NextFloat() < 0.7f ? 0 : 1;
					var q = new Vector3(mouth.Floor.x + Mathf.Cos(a) * r, vault - 1.5f, mouth.Floor.z + Mathf.Sin(a) * r);
					if (system.Void(q) >= 0f || !Roof(q, 6f, out float roof) || !Ground(q, 60f, out float floor))
					{
						continue;
					}
					Vector3 world = system.Origin + q;
					float cover = context.GroundAt(world.x, world.z) - (system.Origin.y + roof);
					if (cover < 0.3f || cover > RootCover)
					{
						continue;
					}
					if (Hang(CaveGlowKind.HangingRoots, hue, variant, new Vector3(q.x, floor, q.z), roof, scale, yaw, light: false))
					{
						SkylightPieces++;
					}
				}
			}

			/// <summary>
			/// What fell in when the roof opened: blocks of it heaped about under the opening (off the ways), and now and then a
			/// trunk of a tree that stood at the edge, lying across the lit floor.
			/// </summary>
			private void FallenIn(DeterministicRNG random, in CaveMouth mouth)
			{
				int count = Mathf.Clamp(Mathf.RoundToInt(mouth.Radius * 1.4f), 3, 12);
				string shape = set.Breakdown ?? "Block";
				for (int i = 0; i < count; i++)
				{
					float a = random.Range(0f, 2f * Mathf.PI), r = mouth.Radius * Mathf.Sqrt(random.NextFloat()) * 1.3f, size = random.Range(0.25f, 0.8f);
					float yaw = random.Range(0f, 360f), tilt = random.Range(-12f, 12f);
					int variant = random.Range(0, RockTypes.VariantCount);
					var q = new Vector3(mouth.Floor.x + Mathf.Cos(a) * r, mouth.Floor.y + 1.2f, mouth.Floor.z + Mathf.Sin(a) * r);
					if (!Ground(q, 4f, out float floor))
					{
						continue;
					}
					var at = new Vector3(q.x, floor - 0.1f * size, q.z);
					if (InLane(at, 0.8f + size) || Submerged(at))
					{
						continue;
					}
					if (Place(FallenRock(shape, variant), at, Quaternion.Euler(tilt, yaw, -tilt), new Vector3(size * random.Range(0.8f, 1.2f), size, size * random.Range(0.8f, 1.2f))))
					{
						SkylightPieces++;
					}
				}
				// A trunk that came down with the roof's edge, lying on the floor under the opening.
				float log = random.NextFloat(), la = random.Range(0f, 2f * Mathf.PI), lr = random.Range(0f, 0.8f) * mouth.Radius, lyaw = random.Range(0f, 360f);
				if (growth.Logs.Count > 0 && log < 0.45f)
				{
					GameObject prefab = growth.Logs[Mathf.FloorToInt(log * 997f) % growth.Logs.Count];
					var q = new Vector3(mouth.Floor.x + Mathf.Cos(la) * lr, mouth.Floor.y + 1.2f, mouth.Floor.z + Mathf.Sin(la) * lr);
					if (Ground(q, 4f, out float floor))
					{
						var at = new Vector3(q.x, floor - 0.1f, q.z);
						if (!InLane(at, 2.5f) && !Submerged(at) && Place(prefab, at, Quaternion.Euler(0f, lyaw, 0f), Vector3.one * random.Range(0.8f, 1.1f)))
						{
							SkylightPieces++;
						}
					}
				}
			}

			/// <summary>A fallen block of the cave's rock in a variant drawn from the opening's own stream (not the formations' one).</summary>
			private GameObject FallenRock(string shape, int variant)
			{
				(string rock, string artShape) = FormationArt(set.Rock, shape, system.Form);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(RockArtNames.FormationPrefab(rock, artShape, variant)));
				if (prefab == null)
				{
					Missing.Add($"{rock} {artShape}");
				}
				return prefab;
			}

			/// <summary>
			/// A point on the ground round an opening (system frame): <paramref name="distance"/> metres out from its middle at
			/// <paramref name="degrees"/>, on the terrain; NaN in y where it is water, too steep for anything to lie, or within the
			/// widened <paramref name="holes"/> (the cut and the rock lip round it).
			/// </summary>
			private Vector3 Surface(Vector3 opening, float degrees, float distance, System.Func<float, float, bool> holes)
			{
				float r = degrees * Mathf.Deg2Rad;
				float x = system.Origin.x + opening.x + Mathf.Sin(r) * distance, z = system.Origin.z + opening.z + Mathf.Cos(r) * distance;
				if (context.Water != null && context.Water.KindAt(x, z) != WaterKind.None || context.SlopeAt != null && context.SlopeAt(x, z) > 38f
					|| holes != null && holes(x, z))
				{
					return new Vector3(0f, float.NaN, 0f);
				}
				return new Vector3(x, context.GroundAt(x, z), z) - system.Origin;
			}
		}
	}
}
#endif
