#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A mine's clutter (2026-10-20, cave realism: "Caves of all types and mines look barren"): what a medieval working leaves
	/// lying about, from the kit it already has — heaps of spoil at the stopes' walls, ore heaped against a breast-wall, spare
	/// props and offcuts (a woodpile), crates, barrels (some on their sides), buckets, sacks of ore and tools against the rock;
	/// in the wider places along the drifts a crate, a barrel or a little spoil; and on the rotting timber deep in, a few
	/// glowing tufts and puffballs.
	/// </summary>
	/// <remarks>Its own stream, after the workings: the mine's sets, track, ore, lamps, signs and camp stand as they did.</remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>Pieces of clutter laid in a mine.</summary>
			public int Clutter;

			/// <summary>One kind of clutter: its kit piece and variant, how likely it is, how far it stands off the wall and whether its back (+z) goes to it.</summary>
			private static readonly (string id, int variant, float weight, float inset, bool back)[] Clutters =
			{
				("Crate", 0, 0.14f, 0.6f, false),
				("Crate", 1, 0.08f, 0.6f, false),
				("Barrel", 0, 0.08f, 0.55f, false),
				("Barrel", 1, 0.12f, 0.7f, false),
				("Woodpile", 0, 0.1f, 0.15f, true),
				("Woodpile", 1, 0.12f, 0.9f, false),
				(CaveArt.WaterBucket, 0, 0.1f, 0.5f, false),
				(CaveArt.OreSacks, 0, 0.1f, 0.7f, false),
				(CaveArt.OreSacks, 1, 0.06f, 0.75f, false),
				(CaveArt.MiningTools, 1, 0.06f, 0.12f, true),
			};

			private static int PickClutter(float draw)
			{
				float total = 0f;
				foreach ((string _, int _, float w, float _, bool _) in Clutters)
				{
					total += w;
				}
				float at = draw * total;
				for (int i = 0; i < Clutters.Length; i++)
				{
					if (at < Clutters[i].weight)
					{
						return i;
					}
					at -= Clutters[i].weight;
				}
				return Clutters.Length - 1;
			}

			private void MineClutter()
			{
				CaveFungusPlan p = Plan;
				if (p.Clutter <= 0f)
				{
					return;
				}
				var random = new DeterministicRNG(unchecked(system.Seed * 0x4F2D + 0x1A6B));
				// Spoil round the winzes the layout sank (their own stream, as a natural cave's pits have).
				TerracesAndPits(new DeterministicRNG(unchecked(system.Seed * 0x0F4B + 0x7D21)), p);
				foreach (CaveChamber c in system.Chambers)
				{
					float floor = FloorUnder(c.Centre, c.Floor);
					var from = new Vector3(c.Centre.x, floor + 0.5f, c.Centre.z);
					float reach = Mathf.Max(c.Radii.x, c.Radii.z) + 2f;
					int heaps = random.Range(1, 3), nooks = Mathf.RoundToInt(random.Range(1f, 4f) * p.Clutter);
					bool oreHeap = c.Kind != CaveChamberKind.Hall && random.NextFloat() < 0.55f;
					float a0 = random.Range(0f, 360f);
					// The spoil heaped at the walls (in a natural cavern the mine broke into, its own breakdown).
					for (int k = 0; k < heaps; k++)
					{
						Vector3 dir = Dir(a0 + 137f * k + random.Range(-20f, 20f));
						float inset = random.Range(1f, 2.2f);
						int pieces = random.Range(3, 7);
						if (WallFoot(from, dir, reach, out Vector3 foot, c))
						{
							RubbleHeap(random, foot - dir * inset, pieces, 0.8f);
						}
					}
					// The ore tipped against a plank breast-wall, its back to the rock.
					if (oreHeap)
					{
						Vector3 dir = Dir(a0 + 70f);
						if (WallFoot(from, dir, reach, out Vector3 foot, c))
						{
							ClutterAt(foot - dir * 0.9f, dir, "OreBin", 1, back: true, 0.5f);
						}
					}
					for (int k = 0; k < nooks; k++)
					{
						Vector3 dir = Dir(a0 + 200f + 53f * k + random.Range(-15f, 15f));
						int pick = PickClutter(random.NextFloat());
						float yaw = random.Range(0f, 360f);
						if (WallFoot(from, dir, reach, out Vector3 foot, c))
						{
							(string id, int variant, float _, float inset, bool back) = Clutters[pick];
							ClutterAt(foot - dir * inset, back ? dir : Dir(yaw), id, variant, back, 0.1f);
						}
					}
				}
				// Along the drifts: where they are wide enough, a crate, a barrel, a little spoil against the wall; and a glowing
				// tuft or puffball where the timber rots in the dark.
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(6f, 14f); along < end; along += random.Range(12f, 26f) / p.Clutter)
					{
						float hand = random.NextFloat() < 0.5f ? -1f : 1f, roll = random.NextFloat(), yaw = random.Range(0f, 360f);
						int pick = PickClutter(random.NextFloat());
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						if (NearMouth(c))
						{
							continue;
						}
						Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
						float floor = FloorUnder(c, nominal);
						if (!WallFoot(new Vector3(c.x, floor + 0.5f, c.z), side, 2f * RadiusAt(nodes, along) + 1f, out Vector3 foot))
						{
							continue;
						}
						// Wide enough: the wall's foot well out from the way's line (the track, the sets' posts).
						float off = new Vector2(foot.x - c.x, foot.z - c.z).magnitude;
						if (off < Lane + 0.9f)
						{
							if (roll < 0.3f * p.Puffballs && DaylightAt(c, 1f) <= 0f)
							{
								// Too narrow for clutter: a tuft on the rotting foot of a set's post instead.
								Vector3 tuft = foot - side * 0.25f;
								if (Ground(tuft + Vector3.up * 0.6f, 1.5f, out float f))
								{
									FloorGlow(random, CaveGlowKind.Fungus, Colours().Fungus, new Vector3(tuft.x, f, tuft.z), random.Range(0.6f, 0.9f), 4f, light: false);
								}
							}
							continue;
						}
						if (roll < 0.25f)
						{
							RubbleHeap(random, foot - side * 0.6f, random.Range(2, 4), 0.5f);
						}
						else if (roll < 0.4f && DaylightAt(c, 1f) <= 0f)
						{
							Vector3 q = foot - side * 0.5f;
							if (Ground(q + Vector3.up * 0.6f, 1.5f, out float f) && !InLane(new Vector3(q.x, f, q.z), -0.6f))
							{
								FloorGlow(random, CaveGlowKind.Puffballs, Colours().Fungus, new Vector3(q.x, f, q.z), random.Range(0.5f, 0.8f), 4f, light: false);
							}
						}
						else
						{
							(string id, int variant, float _, float inset, bool back) = Clutters[pick];
							ClutterAt(foot - side * inset, back ? side : Dir(yaw), id, variant, back, -0.6f);
						}
					}
				}
			}

			/// <summary>
			/// A kit piece of clutter on the floor near a point (system frame), facing <paramref name="facing"/> (its +z: toward the
			/// wall for one whose back goes to it), off the ways by <paramref name="margin"/> (negative: nearer the line, for the
			/// drifts' wide places), with headroom over it.
			/// </summary>
			private void ClutterAt(Vector3 near, Vector3 facing, string id, int variant, bool back, float margin)
			{
				if (!Ground(new Vector3(near.x, near.y + 0.6f, near.z), 2f, out float floor))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (InLane(at, margin) || Crowded(at, 1.2f, 3f) || system.Void(at + Vector3.up * 1.6f) >= 0f)
				{
					return;
				}
				if (Lay(id, variant, at, Quaternion.LookRotation(Flat(facing), Vector3.up), Vector3.one))
				{
					Clutter++;
					bigs.Add(at);
				}
			}
		}
	}
}
#endif
