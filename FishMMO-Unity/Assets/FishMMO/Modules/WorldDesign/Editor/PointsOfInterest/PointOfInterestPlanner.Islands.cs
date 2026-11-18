#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestPlanner
	{
		/// <summary>The largest island a tidal island's kind stands on, m²: a holy isle, not a land of its own.</summary>
		public const float MaxTidalIslandM2 = 150000f;

		/// <summary>The deepest sea a tidal causeway crosses, metres under sea level: deeper, and no low tide uncovers it.</summary>
		public const float MaxCausewayDepth = 4f;

		internal sealed partial class State
		{
			/// <summary>
			/// Sites a tidal island's kind (<see cref="PointOfInterestKindRule.TidalIslandMetres"/>, 2026-10-27: a tidal abbey): on an island
			/// the scene holds whole, no larger than <see cref="MaxTidalIslandM2"/>, whose shore comes within the rule's reach of bigger land
			/// across sea no deeper than <see cref="MaxCausewayDepth"/> (the causeway its shaper lays); at the island's heart, facing the
			/// crossing.
			/// </summary>
			/// <remarks>
			/// The scene's tide is not known when the sites are planned (SceneSeaFloor measures it from the coast's biomes after them), so
			/// the island is judged by the sea floor's depth alone, and the causeway is built to stand just over the mean sea: awash at
			/// high water, dry at low.
			/// </remarks>
			private void PlaceTidalIslands(PointOfInterestKindRule rule, int budget)
			{
				if (!Input.HasSea || !Input.SeaIsWater)
				{
					return;
				}
				var land = new bool[G.Height.Length];
				for (int i = 0; i < land.Length; i++)
				{
					land[i] = !G.Sea[i];
				}
				List<List<int>> groups = Components(land, G.Width, G.Depth);
				var groupOf = new int[land.Length];
				for (int i = 0; i < groupOf.Length; i++)
				{
					groupOf[i] = -1;
				}
				for (int g = 0; g < groups.Count; g++)
				{
					foreach (int i in groups[g])
					{
						groupOf[i] = g;
					}
				}
				float cellArea = G.Cell * G.Cell;
				int reach = Mathf.CeilToInt(rule.TidalIslandMetres / G.Cell);
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(rule.Kind);
				var candidates = new List<Site>();
				for (int g = 0; g < groups.Count; g++)
				{
					List<int> group = groups[g];
					float area = group.Count * cellArea;
					if (area > MaxTidalIslandM2 || TouchesEdge(group))
					{
						continue;
					}
					// The nearest bigger land across shallow sea: from each shore cell, every land cell of another, bigger group in reach.
					float best = float.PositiveInfinity;
					int from = -1, to = -1;
					foreach (int i in group)
					{
						if (!Shore(i))
						{
							continue;
						}
						int x = i % G.Width, z = i / G.Width;
						for (int dz = -reach; dz <= reach; dz++)
						{
							for (int dx = -reach; dx <= reach; dx++)
							{
								int xx = x + dx, zz = z + dz;
								if (xx < 0 || zz < 0 || xx >= G.Width || zz >= G.Depth)
								{
									continue;
								}
								int j = zz * G.Width + xx;
								int other = groupOf[j];
								if (other < 0 || other == g || groups[other].Count <= group.Count)
								{
									continue;
								}
								float d = Mathf.Sqrt(dx * dx + dz * dz) * G.Cell;
								if (d <= rule.TidalIslandMetres && d < best && Shallow(i, j))
								{
									best = d;
									from = i;
									to = j;
								}
							}
						}
					}
					if (to < 0)
					{
						continue;
					}
					// At the island's heart: the cell farthest from its sea.
					int heart = group[0];
					foreach (int i in group)
					{
						if (G.SeaDistance[i] > G.SeaDistance[heart])
						{
							heart = i;
						}
					}
					if (BiomeWeight(rule, heart) <= 0f || G.Blocked[heart])
					{
						continue;
					}
					float east = G.EastOf(heart % G.Width), north = G.NorthOf(heart / G.Width);
					uint hash = Hash(Input.Seed, (int)rule.Kind, heart % G.Width, heart / G.Width);
					float radius = Mathf.Min(Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, Unit(Mix(hash ^ 0x7A11u))), G.SeaDistance[heart] - 4f);
					if (radius < rule.FootprintMin * 0.8f || !Inside(east, north, Input.EdgeMarginMetres + radius))
					{
						continue;
					}
					Site site = Site.At(east, north, G.Height[heart], BiomeWeight(rule, heart) - best / (4f * rule.TidalIslandMetres) + 0.5f * Unit(Mix(hash ^ 0x5C0Eu)));
					site.Radius = radius;
					site.Cell = heart;
					// Facing the crossing to the mainland.
					site.Yaw = Mathf.Round(YawOf(G.EastOf(to % G.Width) - G.EastOf(from % G.Width), G.NorthOf(to / G.Width) - G.NorthOf(from / G.Width)));
					candidates.Add(site);
				}
				candidates.Sort((a, b) =>
				{
					int c = b.Score.CompareTo(a.Score);
					return c != 0 ? c : a.Cell.CompareTo(b.Cell);
				});
				int taken = 0;
				foreach (Site candidate in candidates)
				{
					if (taken >= budget)
					{
						break;
					}
					Site site = candidate;
					if (!Clear(rule, ref site) || !Footprint(rule, info, ref site))
					{
						continue;
					}
					Accept(rule, info, site);
					taken++;
				}
				if (taken < budget && candidates.Count == 0)
				{
					Plan.Notes.Add($"{info.DisplayName}: no small island lies within {rule.TidalIslandMetres:0} m of the shore across shallow sea.");
				}
			}

			/// <summary>Whether a land cell has the sea beside it.</summary>
			private bool Shore(int i)
			{
				int x = i % G.Width, z = i / G.Width;
				return (x > 0 && G.Sea[i - 1]) || (x < G.Width - 1 && G.Sea[i + 1]) || (z > 0 && G.Sea[i - G.Width]) || (z < G.Depth - 1 && G.Sea[i + G.Width]);
			}

			private bool TouchesEdge(List<int> group)
			{
				foreach (int i in group)
				{
					int x = i % G.Width, z = i / G.Width;
					if (x == 0 || z == 0 || x == G.Width - 1 || z == G.Depth - 1)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Whether the sea between two cells is nowhere deeper than <see cref="MaxCausewayDepth"/>.</summary>
			private bool Shallow(int a, int b)
			{
				float ax = G.EastOf(a % G.Width), az = G.NorthOf(a / G.Width), bx = G.EastOf(b % G.Width), bz = G.NorthOf(b / G.Width);
				float length = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
				int steps = Mathf.Max(1, Mathf.CeilToInt(length / (G.Cell * 0.5f)));
				for (int s = 1; s < steps; s++)
				{
					float t = s / (float)steps;
					if (Input.SeaLevel - G.Sample(G.Height, ax + (bx - ax) * t, az + (bz - az) * t) > MaxCausewayDepth)
					{
						return false;
					}
				}
				return true;
			}
		}
	}
}
#endif
