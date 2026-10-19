#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A mine's working dress (2026-10-13: "Ore veins in mines, Torches, lanterns, hanging lanterns"): the ore it was dug
	/// for showing in its drifts' and stopes' walls (a glowing fantasy ore in its deepest level and in the natural cavern it
	/// broke into), lanterns hung where its ways meet, in its stopes and at its shaft heads, its tools and sacks in the
	/// stopes, boards at its shaft heads and its first level, and the miners' camp in a stope near the adit.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>No collar on a shaft.</b> Every shaft of a mine is entered from its side, the drift opening onto it at its floor
	/// and the ladder's step-off running back into the drift: a cribbed collar round the opening would lie across that
	/// step-off (which must stay free of colliders), so none is laid.
	/// </para>
	/// <para>
	/// <b>Lamps are not doubled.</b> A lantern is hung only where no torch, lamp or fire the dressing has laid is within six
	/// metres; where the drift is too low for a hung lantern to clear a tall person's head over the way, it goes on the wall.
	/// </para>
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>The least distance from a new lantern to any light already laid, metres.</summary>
			private const float LampSpacing = 6f;

			/// <summary>Where a mine's levels lie: the adit's lowest floor and the mine's lowest, and whether it goes down at all.</summary>
			private float firstLevel, lowestLevel;
			private bool hasLevels;

			private void Workings()
			{
				Colours();
				var random = new DeterministicRNG(unchecked(system.Seed * 0x6B2F + 0x13D7));
				firstLevel = float.MaxValue;
				lowestLevel = float.MaxValue;
				CavePassage adit = null;
				foreach (CavePassage passage in system.Passages)
				{
					foreach (CaveNode n in passage.Nodes)
					{
						lowestLevel = Mathf.Min(lowestLevel, n.Floor);
						if (passage.Kind == CavePassageKind.Trunk)
						{
							firstLevel = Mathf.Min(firstLevel, n.Floor);
						}
					}
					if (adit == null && passage.Kind == CavePassageKind.Trunk)
					{
						adit = passage;
					}
				}
				if (firstLevel == float.MaxValue)
				{
					return;
				}
				hasLevels = lowestLevel < firstLevel - 8f;
				var lights = new List<Vector3>();
				if (context.Points != null && context.Points.Emitters != null)
				{
					foreach (SceneEmitter e in context.Points.Emitters)
					{
						if (e.Kind == SceneEmitterKind.Fire || e.Kind == SceneEmitterKind.Lamp || e.Kind == SceneEmitterKind.Glow)
						{
							lights.Add(e.Position - system.Origin);
						}
					}
				}
				foreach (CavePassage passage in system.Passages)
				{
					OreAlong(random, passage);
				}
				foreach (CaveChamber c in system.Chambers)
				{
					OreRound(random, c);
				}
				// Lanterns where the ways meet, in the stopes, at the shaft heads.
				foreach (CavePassage passage in system.Passages)
				{
					if (passage.Kind != CavePassageKind.Trunk && passage.Nodes.Count >= 2)
					{
						CaveNode n = passage.Nodes[0];
						Lantern(random, new Vector3(n.Centre.x, FloorUnder(n.Centre, n.Floor), n.Centre.z), Flat(passage.Nodes[1].Centre - n.Centre), lights);
					}
				}
				foreach (CaveChamber c in system.Chambers)
				{
					Lantern(random, new Vector3(c.Centre.x, FloorUnder(c.Centre, c.Floor), c.Centre.z), Along(c), lights);
					StopeKit(random, c);
				}
				foreach (CaveClimb climb in system.Climbs)
				{
					Vector3 exit = Flat(climb.TopExit);
					Vector3 head = climb.Top + exit * 1.4f;
					Lantern(random, new Vector3(head.x, FloorUnder(head, climb.Top.y), head.z), exit, lights);
					// Read by those coming along the drift to the shaft: from the exit's side.
					Sign(random, climb.Top + exit * random.Range(1.8f, 2.6f), climb.Top.y, exit, 1, climb);
				}
				// The mine's board a few paces inside the portal, facing those coming in.
				if (adit != null && adit.Nodes.Count >= 2)
				{
					CentreAt(adit.Nodes, adit.Nodes[0].Along + 5f, out Vector3 c, out float floor);
					CentreAt(adit.Nodes, adit.Nodes[0].Along + 5.5f, out Vector3 ahead, out _);
					Sign(random, c, floor, -Flat(ahead - c), 0, null);
				}
				MinersCamp(random);
			}

			// ── Ore ───────────────────────────────────────────────

			/// <summary>
			/// The ore in a drift's walls every 8–20 m, the sides taken in turn, at a hand's to a head's height; a glowing ore
			/// for some of them on the deepest level.
			/// </summary>
			private void OreAlong(DeterministicRNG random, CavePassage passage)
			{
				List<CaveNode> nodes = passage.Nodes;
				if (nodes.Count < 2)
				{
					return;
				}
				float end = nodes[nodes.Count - 1].Along;
				float hand = random.NextFloat() < 0.5f ? -1f : 1f;
				for (float along = nodes[0].Along + random.Range(3f, 10f); along < end; along += random.Range(8f, 20f))
				{
					hand = -hand;
					float h = random.Range(0.9f, 2.1f), roll = random.NextFloat();
					CentreAt(nodes, along, out Vector3 c, out float nominal);
					CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
					if (NearMouth(c))
					{
						continue;
					}
					float floor = FloorUnder(c, nominal);
					bool glow = Deep(floor) && roll < 0.45f;
					Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
					Ore(random, new Vector3(c.x, floor + h, c.z), side, 2f * RadiusAt(nodes, along) + 1f, glow);
				}
			}

			/// <summary>The ore round a stope's walls (two to four seams), or round the natural cavern a mine broke into (more, most of them glowing).</summary>
			private void OreRound(DeterministicRNG random, in CaveChamber c)
			{
				bool cavern = c.Kind == CaveChamberKind.Hall && c.Rough > 1.5f;
				float round = Mathf.PI * (c.Radii.x + c.Radii.z);
				int seams = cavern ? Mathf.Clamp(Mathf.RoundToInt(round / 10f), 4, 12) : random.Range(2, 5);
				float floor = FloorUnder(c.Centre, c.Floor);
				bool deep = Deep(floor);
				for (int k = 0; k < seams; k++)
				{
					Vector3 dir = Dir(random.Range(0f, 360f));
					float h = random.Range(0.8f, cavern ? 3f : 2.2f), roll = random.NextFloat();
					Ore(random, new Vector3(c.Centre.x, floor + h, c.Centre.z), dir, Mathf.Max(c.Radii.x, c.Radii.z) + 3f, cavern ? roll < 0.6f : deep && roll < 0.45f);
				}
			}

			/// <summary>On the deepest level of a mine that goes down.</summary>
			private bool Deep(float floor) => hasLevels && floor < lowestLevel + 5f;

			/// <summary>One seam on the wall a ray meets: the mine's ore (its second ore a quarter of the time), or its glowing ore.</summary>
			private void Ore(DeterministicRNG random, Vector3 from, Vector3 dir, float reach, bool glow)
			{
				Palette p = Colours();
				bool accent = random.NextFloat() < 0.25f;
				float scale = random.Range(0.8f, 1.15f);
				if (glow)
				{
					WallGlow(random, CaveGlowKind.GlowOreVein, p.GlowOre, from, dir, reach, scale, light: true);
				}
				else
				{
					WallGlow(random, CaveGlowKind.OreVein, accent ? p.OreAccent : p.Ore, from, dir, reach, scale, light: false);
				}
			}

			// ── Lamps ─────────────────────────────────────────────

			/// <summary>
			/// A lantern over a spot on a mine's floor (system frame), unless a light is within <see cref="LampSpacing"/>: hung
			/// on its chain from the roof where it clears a tall person's head, else on its bracket on the nearer wall at
			/// head height, its arm across the way.
			/// </summary>
			private void Lantern(DeterministicRNG random, Vector3 at, Vector3 along, List<Vector3> lights)
			{
				int variant = random.Range(0, 2);
				float yaw = random.Range(0f, 360f), hand = random.NextFloat() < 0.5f ? 1f : -1f;
				// The lamp the biome hangs here (its Cave Lights, 2026-10-16: a hanging lantern, on a wall bracket where the roof is
				// low, in every biome before); a wall light goes straight to the wall, a ground light stands at the wall's foot.
				if (!CaveLight(BiomeCaveLightPlace.MineLamp, out BiomeCaveLight lamp))
				{
					return;
				}
				BiomeWayLightMount mount = BiomeWayLights.MountOf(lamp.Light);
				foreach (Vector3 lit in lights)
				{
					if ((lit - at).sqrMagnitude < LampSpacing * LampSpacing)
					{
						return;
					}
				}
				Vector3 above = at + Vector3.up * 1f;
				if (system.Void(above) >= 0f)
				{
					return;
				}
				if (mount == BiomeWayLightMount.Roof && Roof(above, 14f, out float roof))
				{
					// The long chain where it clears, else the short.
					for (int v = variant; v <= 1; v++)
					{
						float drop = CaveArt.HangingLanternDrop(v);
						if (roof - at.y - drop >= Headroom && Place(LightPiece(lamp.Light, v), new Vector3(at.x, roof + 0.02f, at.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one))
						{
							Lamps++;
							lights.Add(new Vector3(at.x, roof - drop, at.z));
							return;
						}
					}
				}
				Vector3 across = Vector3.Cross(Vector3.up, Flat(along));
				for (int k = 0; k < 2; k++)
				{
					Vector3 side = across * (k == 0 ? hand : -hand);
					if (Wall(at + Vector3.up * 1.9f, side, 5f, out Vector3 surface, out Vector3 into))
					{
						Vector3 flat = Flat(into);
						bool laid;
						if (mount == BiomeWayLightMount.Ground)
						{
							Vector3 foot = surface - flat * 0.5f;
							laid = Place(LightPiece(lamp.Light, variant), new Vector3(foot.x, FloorUnder(foot, at.y), foot.z), Quaternion.LookRotation(-flat, Vector3.up), Vector3.one);
						}
						else
						{
							// A hanging lantern's wall bracket where the roof is too low for its chain (the wall lantern), else the biome's wall light.
							BiomeWayLightKind onWall = mount == BiomeWayLightMount.Roof ? BiomeWayLightKind.WallLantern : lamp.Light;
							laid = Place(LightPiece(onWall, variant), surface - flat * 0.02f, Quaternion.LookRotation(flat, Vector3.up), Vector3.one);
						}
						if (laid)
						{
							Lamps++;
							lights.Add(surface);
						}
						return;
					}
				}
			}

			// ── Stopes ────────────────────────────────────────────

			/// <summary>A stope's tools against its wall, its ore in sacks, a bucket: each at a wall's foot off the way through it.</summary>
			private void StopeKit(DeterministicRNG random, in CaveChamber c)
			{
				if (c.Kind == CaveChamberKind.Hall)
				{
					return;
				}
				(string id, float chance, float inset, bool back)[] kit =
				{
					(CaveArt.MiningTools, 0.7f, 0.12f, true),
					(CaveArt.OreSacks, 0.6f, 0.7f, false),
					(CaveArt.WaterBucket, 0.4f, 0.55f, false),
				};
				float floor = FloorUnder(c.Centre, c.Floor);
				float a0 = random.Range(0f, 360f);
				int turn = 0;
				foreach ((string id, float chance, float inset, bool back) in kit)
				{
					bool want = random.NextFloat() < chance;
					int variant = random.Range(0, 2);
					float yaw = random.Range(0f, 360f);
					for (int tries = 0; tries < 6 && want; tries++, turn++)
					{
						Vector3 dir = Dir(a0 + turn * 67f);
						if (!WallFoot(new Vector3(c.Centre.x, floor + 0.5f, c.Centre.z), dir, Mathf.Max(c.Radii.x, c.Radii.z) + 2f, out Vector3 foot, c))
						{
							continue;
						}
						Vector3 near = foot - dir * inset;
						if (!Ground(new Vector3(near.x, foot.y + 0.5f, near.z), 2f, out float f))
						{
							continue;
						}
						var at = new Vector3(near.x, f, near.z);
						if (InLane(at, 0.1f))
						{
							continue;
						}
						// The tools' back (+z) to the wall; the rest any way round.
						Quaternion rotation = back ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.Euler(0f, yaw, 0f);
						if (Lay(id, variant, at, rotation, Vector3.one))
						{
							turn++;
							break;
						}
					}
				}
			}

			// ── Signs ─────────────────────────────────────────────

			/// <summary>
			/// A mine's board on its post by the wall of a drift, near a point (system frame), turned to face those coming
			/// <paramref name="from"/> (a horizontal direction toward them): v0 the mine's own board, v1 a warning at a shaft
			/// head. Never in a climb's step-off.
			/// </summary>
			private void Sign(DeterministicRNG random, Vector3 near, float level, Vector3 from, int variant, CaveClimb? climb)
			{
				float hand = random.NextFloat() < 0.5f ? 1f : -1f;
				Vector3 across = Vector3.Cross(Vector3.up, Flat(from));
				for (int k = 0; k < 2; k++)
				{
					Vector3 side = across * (k == 0 ? hand : -hand);
					if (!WallFoot(new Vector3(near.x, level + 0.5f, near.z), side, 5f, out Vector3 foot))
					{
						continue;
					}
					Vector3 p = foot - side * 0.35f;
					if (!Ground(new Vector3(p.x, level + 1f, p.z), 2f, out float floor) || Mathf.Abs(floor - level) > 0.6f)
					{
						continue;
					}
					var at = new Vector3(p.x, floor, p.z);
					if (climb.HasValue && NearLine(at, climb.Value.Top, climb.Value.Top + Flat(climb.Value.TopExit) * 1.2f, 1f, 2f))
					{
						continue;
					}
					// The board is read from −z: its back half to the wall, half to those it faces.
					Vector3 back = (side - Flat(from)).normalized;
					Lay(CaveArt.MineSign, variant, at, Quaternion.LookRotation(back, Vector3.up), Vector3.one);
					return;
				}
			}

			// ── The miners' camp ──────────────────────────────────

			/// <summary>The miners' camp in the largest stope of the first level that is not the site's own.</summary>
			private void MinersCamp(DeterministicRNG random)
			{
				CaveChamber best = default;
				float size = 0f;
				foreach (CaveChamber c in system.Chambers)
				{
					float area = c.Radii.x * c.Radii.z;
					if (c.Kind == CaveChamberKind.Hall || c.Floor < firstLevel - 5f || IsOwn(c) || area <= size)
					{
						continue;
					}
					best = c;
					size = area;
				}
				if (size <= 0f)
				{
					return;
				}
				float ring = Mathf.Clamp(0.45f * Mathf.Min(best.Radii.x, best.Radii.z), 1.4f, 2.2f);
				float a0 = random.Range(0f, 360f);
				for (int k = 0; k < 12; k++)
				{
					Vector3 dir = Dir(a0 + k * 30f);
					Vector3 p = best.Centre + dir * (random.Range(0.4f, 0.6f) * Mathf.Min(best.Radii.x, best.Radii.z));
					p.y = best.Floor;
					// Its ground looked over a little inside the ring (a stope's wall may close in on one side), its middle clear of the way.
					if (CampGround(p, 0.75f * ring, 0.5f, out Vector3 at) && Camp(random, at, ring, miners: true))
					{
						return;
					}
				}
			}
		}
	}
}
#endif
