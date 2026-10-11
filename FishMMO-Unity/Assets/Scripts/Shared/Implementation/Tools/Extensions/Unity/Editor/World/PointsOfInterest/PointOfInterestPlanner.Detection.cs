#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestPlanner
	{
		/// <summary>The least drop a waterfall is marked at, metres: the same as the falls the water draws.</summary>
		public const float MinFallMetres = 1.5f;
		/// <summary>The shortest run of rapids marked, metres along the river.</summary>
		public const float MinRapidsMetres = 25f;
		/// <summary>The shortest in-scene stretch a river is marked (and named) for, metres.</summary>
		public const float MinRiverMetres = 300f;

		/// <summary>
		/// The connected groups (4-neighbour) of a mask's set cells, each in scan order, the groups in the order their
		/// first cell is met. Pure; for the detectors and tests.
		/// </summary>
		public static List<List<int>> Components(bool[] mask, int width, int depth)
		{
			var groups = new List<List<int>>();
			var seen = new bool[mask.Length];
			var stack = new Stack<int>();
			for (int start = 0; start < mask.Length; start++)
			{
				if (!mask[start] || seen[start])
				{
					continue;
				}
				var group = new List<int>();
				seen[start] = true;
				stack.Push(start);
				while (stack.Count > 0)
				{
					int i = stack.Pop();
					group.Add(i);
					int x = i % width, z = i / width;
					if (x > 0 && mask[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
					if (x < width - 1 && mask[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
					if (z > 0 && mask[i - width] && !seen[i - width]) { seen[i - width] = true; stack.Push(i - width); }
					if (z < depth - 1 && mask[i + width] && !seen[i + width]) { seen[i + width] = true; stack.Push(i + width); }
				}
				group.Sort();
				groups.Add(group);
			}
			return groups;
		}

		internal sealed partial class State
		{
			/// <summary>Peaks found once, for both the Peak and the Pass kinds: cell and prominence, highest first.</summary>
			private List<(int cell, float prominence)> peaks;

			/// <summary>Marks a detected kind wherever the data holds it.</summary>
			public void Detect(PointOfInterestKindRule rule)
			{
				if (rule.Max <= 0)
				{
					return;
				}
				var sites = new List<Site>();
				switch (rule.Detector)
				{
					case PointOfInterestDetector.Falls: Falls(sites); break;
					case PointOfInterestDetector.Rapids: Rapids(sites); break;
					case PointOfInterestDetector.Rivers: Rivers(sites); break;
					case PointOfInterestDetector.Lakes: Lakes(rule, sites); break;
					case PointOfInterestDetector.Springs: Springs(rule, sites, false); break;
					case PointOfInterestDetector.HotSprings: Springs(rule, sites, true); break;
					case PointOfInterestDetector.RiverMouths: Mouths(sites); break;
					case PointOfInterestDetector.Deltas: Deltas(sites); break;
					case PointOfInterestDetector.Peaks: Peaks(sites); break;
					case PointOfInterestDetector.Passes: Passes(sites); break;
					case PointOfInterestDetector.Gorges: Gorges(sites); break;
					case PointOfInterestDetector.Mesas: Plateaus(rule, sites, true); break;
					case PointOfInterestDetector.Buttes: Plateaus(rule, sites, false); break;
					case PointOfInterestDetector.Islands: Islands(rule, sites); break;
					case PointOfInterestDetector.Bays: Coast(sites, true); break;
					case PointOfInterestDetector.Headlands: Coast(sites, false); break;
					case PointOfInterestDetector.Sinkholes: Sinkholes(rule, sites); break;
					case PointOfInterestDetector.BiomeCluster:
					case PointOfInterestDetector.BiomeClusterHighest:
					case PointOfInterestDetector.BiomeClusterLowest:
						Clusters(rule, sites);
						break;
					default:
						return;
				}
				Accept(rule, sites);
			}

			private IReadOnlyList<RiverPath> RiverList => Input.Water?.Rivers ?? Array.Empty<RiverPath>();

			/// <summary>Downstream heading at a river point.</summary>
			private static float Flow(RiverPath river, int k)
			{
				int a = Math.Max(0, k - 1), b = Math.Min(river.Count - 1, k + 1);
				return YawOf(river.X[b] - river.X[a], river.Z[b] - river.Z[a]);
			}

			private static Site OnRiver(RiverPath river, int k, float y, float score)
			{
				Site site = Site.At(river.X[k], river.Z[k], y, score);
				site.Yaw = Flow(river, k);
				site.RiverId = river.Id;
				site.PlanetRiver = river.PlanetRiver;
				return site;
			}

			// ── Water ───────────────────────────────────────────────

			private void Falls(List<Site> sites)
			{
				foreach (RiverPath river in RiverList)
				{
					if (river == null)
					{
						continue;
					}
					foreach (FallLedges.FallSpan fall in FallLedges.FindFalls(river, MinFallMetres))
					{
						// Between the lip and where the water lands, at the pool's surface: what a traveller looks up at.
						int mid = (fall.Lip + fall.Plunge) / 2;
						Site site = OnRiver(river, mid, river.Surface[fall.Plunge], fall.Drop);
						site.X = 0.5f * (river.X[fall.Lip] + river.X[fall.Plunge]);
						site.Z = 0.5f * (river.Z[fall.Lip] + river.Z[fall.Plunge]);
						site.Radius = Mathf.Max(6f, river.Width[fall.Lip]);
						sites.Add(site);
					}
				}
			}

			private void Rapids(List<Site> sites)
			{
				foreach (RiverPath river in RiverList)
				{
					if (river == null || !river.Perennial || river.Reach == null || river.Reach.Length != river.Count)
					{
						continue;
					}
					List<FallLedges.FallSpan> falls = FallLedges.FindFalls(river, MinFallMetres);
					int k = 0;
					while (k < river.Count)
					{
						if (river.Reach[k] != RiverReach.Rapid)
						{
							k++;
							continue;
						}
						int last = k;
						while (last + 1 < river.Count && river.Reach[last + 1] == RiverReach.Rapid)
						{
							last++;
						}
						float length = river.S[last] - river.S[k];
						if (length >= MinRapidsMetres)
						{
							int mid = (k + last) / 2;
							bool byFall = false;
							foreach (FallLedges.FallSpan fall in falls)
							{
								if (Mathf.Abs(river.S[fall.Lip] - river.S[mid]) < 60f)
								{
									byFall = true;
									break;
								}
							}
							if (!byFall)
							{
								Site site = OnRiver(river, mid, river.Surface[mid], length);
								site.Radius = Mathf.Max(6f, river.Width[mid]);
								sites.Add(site);
							}
						}
						k = last + 1;
					}
				}
			}

			/// <summary>One per planet river: the midpoint of the longest stretch any of its scene runs has inside the scene.</summary>
			private void Rivers(List<Site> sites)
			{
				var best = new Dictionary<int, Site>();
				var order = new List<int>();
				float margin = Input.EdgeMarginMetres;
				foreach (RiverPath river in RiverList)
				{
					if (river == null || !river.Perennial || river.Count < 2)
					{
						continue;
					}
					int runStart = -1, bestStart = -1, bestEnd = -1;
					float bestLength = 0f;
					for (int k = 0; k <= river.Count; k++)
					{
						bool inside = k < river.Count && Inside(river.X[k], river.Z[k], margin);
						if (inside && runStart < 0)
						{
							runStart = k;
						}
						else if (!inside && runStart >= 0)
						{
							float length = river.S[k - 1] - river.S[runStart];
							if (length > bestLength)
							{
								bestLength = length;
								bestStart = runStart;
								bestEnd = k - 1;
							}
							runStart = -1;
						}
					}
					if (bestStart < 0 || bestLength < MinRiverMetres)
					{
						continue;
					}
					float middle = 0.5f * (river.S[bestStart] + river.S[bestEnd]);
					int mid = bestStart;
					while (mid < bestEnd && river.S[mid] < middle)
					{
						mid++;
					}
					Site site = OnRiver(river, mid, river.Surface[mid], bestLength);
					site.Radius = Mathf.Max(6f, river.Width[mid]);
					// Keyed by planet river, so a river cut into two scene runs is marked (and named) once.
					int key = river.PlanetRiver >= 0 ? river.PlanetRiver : -1 - river.Id;
					if (!best.TryGetValue(key, out Site held))
					{
						order.Add(key);
						best[key] = site;
					}
					else if (site.Score > held.Score)
					{
						best[key] = site;
					}
				}
				foreach (int key in order)
				{
					sites.Add(best[key]);
				}
			}

			private void Lakes(PointOfInterestKindRule rule, List<Site> sites)
			{
				if (Input.Water?.Lakes == null)
				{
					return;
				}
				foreach (PointOfInterestLake lake in Input.Water.Lakes)
				{
					if (lake == null || lake.AreaM2 < rule.MinAreaM2)
					{
						continue;
					}
					Site site = Site.At(lake.X, lake.Z, lake.Level, lake.AreaM2);
					site.LakeId = lake.Id;
					site.Radius = Mathf.Sqrt(lake.AreaM2 / Mathf.PI);
					sites.Add(site);
				}
			}

			/// <summary>Where rivers rise: a hot spring where the ground there is the hot kind's, a spring elsewhere.</summary>
			private void Springs(PointOfInterestKindRule rule, List<Site> sites, bool hot)
			{
				PointOfInterestKindRule hotRule = Input.Rules.RuleFor(POIType.HotSpring);
				foreach (RiverPath river in RiverList)
				{
					if (river == null || !river.Perennial || river.Start != RiverEnd.Source || river.Count < 2)
					{
						continue;
					}
					float x = river.X[0], z = river.Z[0];
					string biome = BiomeName(G.CellAt(x, z));
					bool isHot = hotRule != null && hotRule.Enabled && hotRule.BiomeWeight(biome) > 0f;
					if (isHot != hot || rule.BiomeWeight(biome) <= 0f)
					{
						continue;
					}
					Site site = OnRiver(river, 0, Input.Ground(x, z), river.Discharge != null && river.Discharge.Length > 0 ? river.Discharge[0] : 1f);
					site.Radius = 6f;
					sites.Add(site);
				}
			}

			private void Mouths(List<Site> sites)
			{
				foreach (RiverPath river in RiverList)
				{
					if (river == null || !river.Perennial || river.End != RiverEnd.Sea || river.Count < 2)
					{
						continue;
					}
					int k = river.Count - 1;
					Site site = OnRiver(river, k, Input.SeaLevel, river.Width[k]);
					site.Radius = Mathf.Max(8f, river.Width[k]);
					sites.Add(site);
				}
			}

			/// <summary>A delta where two or more river mouths reach the sea within 600 m of each other, one of them broad.</summary>
			private void Deltas(List<Site> sites)
			{
				var mouths = new List<RiverPath>();
				foreach (RiverPath river in RiverList)
				{
					if (river != null && river.Perennial && river.End == RiverEnd.Sea && river.Count >= 2)
					{
						mouths.Add(river);
					}
				}
				var used = new bool[mouths.Count];
				for (int a = 0; a < mouths.Count; a++)
				{
					if (used[a])
					{
						continue;
					}
					RiverPath first = mouths[a];
					float ax = first.X[first.Count - 1], az = first.Z[first.Count - 1];
					float sx = ax, sz = az, widest = first.Width[first.Count - 1];
					int count = 1;
					for (int b = a + 1; b < mouths.Count; b++)
					{
						RiverPath other = mouths[b];
						float bx = other.X[other.Count - 1], bz = other.Z[other.Count - 1];
						if (!used[b] && (bx - ax) * (bx - ax) + (bz - az) * (bz - az) <= 600f * 600f)
						{
							used[b] = true;
							sx += bx;
							sz += bz;
							widest = Mathf.Max(widest, other.Width[other.Count - 1]);
							count++;
						}
					}
					if (count >= 2 && widest >= 20f)
					{
						Site site = Site.At(sx / count, sz / count, Input.SeaLevel, count * widest);
						site.RiverId = first.Id;
						site.PlanetRiver = first.PlanetRiver;
						site.Radius = 60f;
						sites.Add(site);
					}
				}
			}

			// ── Landforms ───────────────────────────────────────────

			/// <summary>
			/// Summits: the highest cell of a 64 m block that is also the highest within 320 m, standing at least
			/// max(30 m, 15% of the scene's relief) over the lowest ground within 640 m, away from the scene's edge.
			/// </summary>
			private List<(int cell, float prominence)> FindPeaks()
			{
				if (peaks != null)
				{
					return peaks;
				}
				peaks = new List<(int, float)>();
				const int block = 4;
				int bw = (G.Width + block - 1) / block, bd = (G.Depth + block - 1) / block;
				var top = new int[bw * bd];
				for (int bz = 0; bz < bd; bz++)
				{
					for (int bx = 0; bx < bw; bx++)
					{
						int bestCell = -1;
						for (int z = bz * block; z < Math.Min(G.Depth, (bz + 1) * block); z++)
						{
							for (int x = bx * block; x < Math.Min(G.Width, (bx + 1) * block); x++)
							{
								int i = z * G.Width + x;
								if (bestCell < 0 || G.Height[i] > G.Height[bestCell])
								{
									bestCell = i;
								}
							}
						}
						top[bz * bw + bx] = bestCell;
					}
				}
				float relief = G.Highest - Mathf.Max(G.Lowest, Input.HasSea ? Input.SeaLevel : G.Lowest);
				float minimum = Mathf.Max(30f, 0.15f * relief);
				for (int bz = 0; bz < bd; bz++)
				{
					for (int bx = 0; bx < bw; bx++)
					{
						int cell = top[bz * bw + bx];
						float h = G.Height[cell];
						if (Input.HasSea && h < Input.SeaLevel + Input.LandClearMetres)
						{
							continue;
						}
						bool highest = true;
						float low = h;
						for (int dz = -10; dz <= 10 && highest; dz++)
						{
							for (int dx = -10; dx <= 10; dx++)
							{
								int ox = bx + dx, oz = bz + dz;
								if ((dx == 0 && dz == 0) || ox < 0 || oz < 0 || ox >= bw || oz >= bd)
								{
									continue;
								}
								float other = G.Height[top[oz * bw + ox]];
								int d2 = dx * dx + dz * dz;
								if (d2 <= 25 && (other > h || (other == h && top[oz * bw + ox] < cell)))
								{
									highest = false;
									break;
								}
								if (d2 <= 100)
								{
									for (int z = oz * block; z < Math.Min(G.Depth, (oz + 1) * block); z += 2)
									{
										for (int x = ox * block; x < Math.Min(G.Width, (ox + 1) * block); x += 2)
										{
											low = Mathf.Min(low, G.Height[z * G.Width + x]);
										}
									}
								}
							}
						}
						float prominence = h - low;
						if (highest && prominence >= minimum)
						{
							peaks.Add((cell, prominence));
						}
					}
				}
				peaks.Sort((a, b) =>
				{
					int c = b.prominence.CompareTo(a.prominence);
					return c != 0 ? c : a.cell.CompareTo(b.cell);
				});
				return peaks;
			}

			private void Peaks(List<Site> sites)
			{
				foreach ((int cell, float prominence) in FindPeaks())
				{
					float x = G.EastOf(cell % G.Width), z = G.NorthOf(cell / G.Width);
					if (!Inside(x, z, Input.EdgeMarginMetres + 150f))
					{
						continue;
					}
					Site site = Site.At(x, z, Input.Ground(x, z), prominence);
					site.Radius = 20f;
					sites.Add(site);
				}
			}

			/// <summary>Saddles: the low point of the line between two peaks within 3.5 km, falling away on both sides across it.</summary>
			private void Passes(List<Site> sites)
			{
				List<(int cell, float prominence)> found = FindPeaks();
				int count = Math.Min(found.Count, 8);
				for (int a = 0; a < count; a++)
				{
					for (int b = a + 1; b < count; b++)
					{
						int ca = found[a].cell, cb = found[b].cell;
						float ax = G.EastOf(ca % G.Width), az = G.NorthOf(ca / G.Width);
						float bx = G.EastOf(cb % G.Width), bz = G.NorthOf(cb / G.Width);
						float dx = bx - ax, dz = bz - az;
						float length = Mathf.Sqrt(dx * dx + dz * dz);
						if (length > 3500f || length < 200f)
						{
							continue;
						}
						int steps = Mathf.CeilToInt(length / G.Cell);
						float lowT = 0.5f, lowH = float.PositiveInfinity;
						for (int s = Mathf.CeilToInt(steps * 0.15f); s <= Mathf.FloorToInt(steps * 0.85f); s++)
						{
							float t = s / (float)steps;
							float h = G.Sample(G.Height, ax + dx * t, az + dz * t);
							if (h < lowH)
							{
								lowH = h;
								lowT = t;
							}
						}
						float px = ax + dx * lowT, pz = az + dz * lowT;
						float depth = Mathf.Min(G.Height[ca], G.Height[cb]) - lowH;
						float nx = -dz / length, nz = dx / length;
						float left = G.Sample(G.Height, px + nx * 150f, pz + nz * 150f);
						float right = G.Sample(G.Height, px - nx * 150f, pz - nz * 150f);
						if (depth < 25f || left > lowH - 3f || right > lowH - 3f)
						{
							continue;
						}
						if (Input.HasSea && lowH < Input.SeaLevel + Input.LandClearMetres)
						{
							continue;
						}
						Site site = Site.At(px, pz, Input.Ground(px, pz), depth);
						site.Yaw = YawOf(dx, dz);
						site.Radius = 20f;
						sites.Add(site);
					}
				}
			}

			/// <summary>Narrow rivers with ground rising 30 m or more within 60 m of both banks, over 100 m of their length.</summary>
			private void Gorges(List<Site> sites)
			{
				foreach (RiverPath river in RiverList)
				{
					if (river == null || river.Count < 3)
					{
						continue;
					}
					int runStart = -1;
					float riseSum = 0f;
					int riseCount = 0;
					for (int k = 1; k <= river.Count - 1; k++)
					{
						bool walled = false;
						float rise = 0f;
						if (k < river.Count - 1 && river.Width[k] <= 40f)
						{
							float tx = river.X[k + 1] - river.X[k - 1], tz = river.Z[k + 1] - river.Z[k - 1];
							float tl = Mathf.Max(1e-3f, Mathf.Sqrt(tx * tx + tz * tz));
							float nx = -tz / tl, nz = tx / tl;
							float reach = river.Width[k] * 0.5f + 60f;
							float surface = river.Surface[k];
							float left = Input.Ground(river.X[k] + nx * reach, river.Z[k] + nz * reach) - surface;
							float right = Input.Ground(river.X[k] - nx * reach, river.Z[k] - nz * reach) - surface;
							rise = Mathf.Min(left, right);
							walled = rise >= 30f;
						}
						if (walled)
						{
							if (runStart < 0)
							{
								runStart = k;
								riseSum = 0f;
								riseCount = 0;
							}
							riseSum += rise;
							riseCount++;
							continue;
						}
						if (runStart >= 0)
						{
							int last = k - 1;
							if (river.S[last] - river.S[runStart] >= 100f)
							{
								int mid = (runStart + last) / 2;
								Site site = OnRiver(river, mid, river.Surface[mid], riseSum / Mathf.Max(1, riseCount));
								site.Radius = 30f;
								sites.Add(site);
							}
							runStart = -1;
						}
					}
				}
			}

			/// <summary>Stepped plateau ground in connected tables: a mesa when broad, a butte when small.</summary>
			private void Plateaus(PointOfInterestKindRule rule, List<Site> sites, bool mesa)
			{
				if (Input.PlateauAt == null)
				{
					return;
				}
				var mask = new bool[G.Height.Length];
				for (int i = 0; i < mask.Length; i++)
				{
					mask[i] = G.Plateau[i] >= 0.5f && !G.Sea[i];
				}
				float cellArea = G.Cell * G.Cell;
				PointOfInterestKindRule mesaRule = Input.Rules.RuleFor(POIType.Mesa);
				float mesaArea = mesaRule != null ? mesaRule.MinAreaM2 : 120000f;
				foreach (List<int> group in Components(mask, G.Width, G.Depth))
				{
					float area = group.Count * cellArea;
					bool fits = mesa ? area >= rule.MinAreaM2 : area >= rule.MinAreaM2 && area < mesaArea;
					if (fits)
					{
						sites.Add(AtCentroid(group, area, 0));
					}
				}
			}

			/// <summary>Land wholly inside the sea in this scene: components of land touching no edge.</summary>
			private void Islands(PointOfInterestKindRule rule, List<Site> sites)
			{
				if (!Input.HasSea)
				{
					return;
				}
				var mask = new bool[G.Height.Length];
				for (int i = 0; i < mask.Length; i++)
				{
					mask[i] = !G.Sea[i];
				}
				float cellArea = G.Cell * G.Cell;
				foreach (List<int> group in Components(mask, G.Width, G.Depth))
				{
					bool edge = false;
					foreach (int i in group)
					{
						int x = i % G.Width, z = i / G.Width;
						if (x == 0 || z == 0 || x == G.Width - 1 || z == G.Depth - 1)
						{
							edge = true;
							break;
						}
					}
					float area = group.Count * cellArea;
					if (!edge && area >= rule.MinAreaM2)
					{
						sites.Add(AtCentroid(group, area, 1));
					}
				}
			}

			/// <summary>
			/// Bays: coastal water with land round most of it (≥ 62% of the 800 m square about it). Headlands: shore with
			/// sea round most of it.
			/// </summary>
			private void Coast(List<Site> sites, bool bay)
			{
				if (!Input.HasSea || !Input.SeaIsWater)
				{
					return;
				}
				int w = G.Width, d = G.Depth;
				// Summed area of sea cells, for the share of sea in any square.
				var sum = new int[(w + 1) * (d + 1)];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						sum[(z + 1) * (w + 1) + x + 1] = (G.Sea[z * w + x] ? 1 : 0) + sum[z * (w + 1) + x + 1] + sum[(z + 1) * (w + 1) + x] - sum[z * (w + 1) + x];
					}
				}
				int k = Mathf.Max(2, Mathf.RoundToInt(400f / G.Cell));
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int i = z * w + x;
						bool sea = G.Sea[i];
						if (sea != bay)
						{
							continue;
						}
						// On the shore: a neighbour on the other side of the water line.
						bool shore = false;
						for (int n = 0; n < 4 && !shore; n++)
						{
							int xx = x + (n == 0 ? 1 : n == 1 ? -1 : 0), zz = z + (n == 2 ? 1 : n == 3 ? -1 : 0);
							shore = xx >= 0 && zz >= 0 && xx < w && zz < d && G.Sea[zz * w + xx] != sea;
						}
						if (!shore)
						{
							continue;
						}
						int x0 = Math.Max(0, x - k), x1 = Math.Min(w, x + k + 1), z0 = Math.Max(0, z - k), z1 = Math.Min(d, z + k + 1);
						int cells = (x1 - x0) * (z1 - z0);
						int seaCells = sum[z1 * (w + 1) + x1] - sum[z0 * (w + 1) + x1] - sum[z1 * (w + 1) + x0] + sum[z0 * (w + 1) + x0];
						// Only a square the scene holds whole: a coast cut by the edge cannot be judged.
						if (cells < (2 * k + 1) * (2 * k + 1))
						{
							continue;
						}
						float share = bay ? 1f - seaCells / (float)cells : seaCells / (float)cells;
						if (share < 0.62f)
						{
							continue;
						}
						float east = G.EastOf(x), north = G.NorthOf(z);
						Site site = Site.At(east, north, bay ? Input.SeaLevel : G.Height[i], share);
						site.Radius = bay ? 120f : 40f;
						site.Cell = i;
						sites.Add(site);
					}
				}
			}

			/// <summary>Closed hollows in the named (karst) ground: 3 m or more below every point 48 m round them, and dry.</summary>
			private void Sinkholes(PointOfInterestKindRule rule, List<Site> sites)
			{
				const int ring = 3;
				for (int z = ring; z < G.Depth - ring; z++)
				{
					for (int x = ring; x < G.Width - ring; x++)
					{
						int i = z * G.Width + x;
						if (BiomeWeight(rule, i) <= 0f || G.Water[i] != WaterKind.None || G.Sea[i])
						{
							continue;
						}
						float h = G.Height[i];
						bool lowest = true;
						for (int dz = -1; dz <= 1 && lowest; dz++)
						{
							for (int dx = -1; dx <= 1; dx++)
							{
								if ((dx != 0 || dz != 0) && G.Height[(z + dz) * G.Width + x + dx] < h)
								{
									lowest = false;
									break;
								}
							}
						}
						if (!lowest)
						{
							continue;
						}
						float rim = float.PositiveInfinity;
						for (int k = 0; k < 16; k++)
						{
							float a = k * (Mathf.PI / 8f);
							rim = Mathf.Min(rim, G.Sample(G.Height, G.EastOf(x) + Mathf.Sin(a) * ring * G.Cell, G.NorthOf(z) + Mathf.Cos(a) * ring * G.Cell));
						}
						float depth = rim - h;
						if (depth >= 3f)
						{
							float east = G.EastOf(x), north = G.NorthOf(z);
							Site site = Site.At(east, north, Input.Ground(east, north), depth);
							site.Radius = 15f;
							site.Cell = i;
							sites.Add(site);
						}
					}
				}
			}

			/// <summary>Connected ground of the rule's biomes, big enough, marked at its middle, top or bottom.</summary>
			private void Clusters(PointOfInterestKindRule rule, List<Site> sites)
			{
				var mask = new bool[G.Height.Length];
				bool any = false;
				for (int i = 0; i < mask.Length; i++)
				{
					mask[i] = BiomeWeight(rule, i) > 0f;
					any |= mask[i];
				}
				if (!any)
				{
					return;
				}
				float cellArea = G.Cell * G.Cell;
				int anchor = rule.Detector == PointOfInterestDetector.BiomeClusterHighest ? 2 : rule.Detector == PointOfInterestDetector.BiomeClusterLowest ? 3 : 0;
				foreach (List<int> group in Components(mask, G.Width, G.Depth))
				{
					float area = group.Count * cellArea;
					if (area >= rule.MinAreaM2)
					{
						sites.Add(AtCentroid(group, area, anchor));
					}
				}
			}

			/// <summary>
			/// A site for a group of cells: <paramref name="anchor"/> 0 = the member nearest its centroid, 1 = the same
			/// (named apart for islands), 2 = its highest cell, 3 = its lowest. Scored by area.
			/// </summary>
			private Site AtCentroid(List<int> group, float area, int anchor)
			{
				double sx = 0.0, sz = 0.0;
				foreach (int i in group)
				{
					sx += G.EastOf(i % G.Width);
					sz += G.NorthOf(i / G.Width);
				}
				float cx = (float)(sx / group.Count), cz = (float)(sz / group.Count);
				int pick = group[0];
				float best = float.PositiveInfinity;
				foreach (int i in group)
				{
					float key;
					if (anchor == 2)
					{
						key = -G.Height[i];
					}
					else if (anchor == 3)
					{
						key = G.Height[i];
					}
					else
					{
						float dx = G.EastOf(i % G.Width) - cx, dz = G.NorthOf(i / G.Width) - cz;
						key = dx * dx + dz * dz;
					}
					if (key < best)
					{
						best = key;
						pick = i;
					}
				}
				float x = G.EastOf(pick % G.Width), z = G.NorthOf(pick / G.Width);
				Site site = Site.At(x, z, Input.Ground(x, z), area);
				site.Radius = Mathf.Clamp(Mathf.Sqrt(area / Mathf.PI), 10f, 400f);
				site.Cell = pick;
				return site;
			}
		}
	}
}
#endif
