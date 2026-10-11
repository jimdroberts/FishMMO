#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestPlanner
	{
		/// <summary>How far past a pad's radius it eases back into the ground: a quarter of the radius, 8 … 15 m.</summary>
		public static float PadBlend(float radius) => Mathf.Clamp(radius * 0.25f, 8f, 15f);

		/// <summary>
		/// How many sites of a kind a scene of <paramref name="areaKm2"/> gets: per km² × area × density × multiplier,
		/// rounded down plus one more on a seeded draw against the fraction, clamped to [Min, Max]. The capital is one
		/// when the scene asked for it and none otherwise.
		/// </summary>
		public static int Budget(PointOfInterestKindRule rule, PointOfInterestPlanSettings settings, float areaKm2, int seed)
		{
			if (rule == null || !rule.Enabled || settings == null || settings.NaturalOnly)
			{
				return 0;
			}
			if (rule.Kind == POIType.Capital)
			{
				return settings.Capital && rule.Max > 0 ? 1 : 0;
			}
			float expected = Mathf.Max(0f, rule.PerKm2) * Mathf.Max(0f, areaKm2) * Mathf.Max(0f, settings.DensityScale) * Mathf.Max(0f, settings.Multiplier);
			int whole = Mathf.FloorToInt(expected);
			float fraction = expected - whole;
			int count = whole + (Unit(Hash(seed, (int)rule.Kind, 0xB0D6E7, 0)) < fraction ? 1 : 0);
			return Mathf.Clamp(count, Mathf.Max(0, rule.Min), Mathf.Max(0, rule.Max));
		}

		internal sealed partial class State
		{
			/// <summary>Face scores per cell, worked out once and shared by every face kind: (score, yaw).</summary>
			private Dictionary<int, (float score, float yaw)> faces;

			/// <summary>Sites a placed kind on the best cells it may stand on.</summary>
			public void Place(PointOfInterestKindRule rule)
			{
				int budget = Budget(rule, Input.Settings, areaKm2, Input.Seed);
				if (budget <= 0)
				{
					return;
				}
				if (rule.RiverCrossing || rule.Kind == POIType.Bridge)
				{
					PlaceBridges(rule, budget);
					return;
				}
				if (rule.Underwater && (!Input.HasSea || !Input.SeaIsWater))
				{
					return;
				}
				if (rule.NearSettlementMetres > 0f && settlements.Count == 0)
				{
					return;
				}

				PointOfInterestKindInfo info = PointOfInterestKinds.Info(rule.Kind);
				int taken = SiteKind(rule, info, budget, out int candidateCount);
				/* A kind the scene MUST have (the capital asked for before the cut, a kind's minimum) is not given up on
				 * because no spot meets its ideal: rugged, river-cut ground has no 400 m disc that is flat and dry. It is
				 * looked for again with a smaller footprint, steeper ground and more relief allowed (its pad levels the
				 * rest), up to three times. Only the shortfall is sited by a relaxed pass. */
				int required = rule.Kind == POIType.Capital ? budget : Mathf.Min(budget, Mathf.Max(0, rule.Min));
				for (int relax = 1; relax <= MaxRelax && taken < required; relax++)
				{
					taken += SiteKind(Relaxed(rule, relax), info, required - taken, out _);
					if (taken >= required)
					{
						Plan.Notes.Add($"{info.DisplayName}: sited only on relaxed ground (level {relax}): a smaller footprint on steeper ground than the kind prefers.");
					}
				}
				if (taken < budget && candidateCount > 0)
				{
					Plan.Notes.Add($"{info.DisplayName}: {taken} of {budget} sited; the scene has no more ground that fits.");
				}
			}

			/// <summary>How many times a required kind is looked for again on worse ground.</summary>
			private const int MaxRelax = 3;

			/// <summary>
			/// A copy of a rule that asks less of the ground: per level, the footprint down to 80 / 60 / 45 % of its
			/// smallest, 5° more slope and relief to match, a coast kind allowed twice as far per level from the sea.
			/// </summary>
			internal static PointOfInterestKindRule Relaxed(PointOfInterestKindRule rule, int level)
			{
				PointOfInterestKindRule copy = rule.Clone();
				float scale = level == 1 ? 0.8f : level == 2 ? 0.6f : 0.45f;
				float radius = Mathf.Max(12f, rule.FootprintMin * scale);
				copy.FootprintMin = radius;
				copy.FootprintMax = radius;
				copy.MaxSlopeDegrees = rule.MaxSlopeDegrees + 5f * level;
				copy.MaxReliefMetres = Mathf.Max(rule.ReliefFor(radius), 3f) * (1f + 0.75f * level);
				if (rule.CoastMetres > 0f)
				{
					copy.CoastMetres = rule.CoastMetres * (1 + level);
				}
				return copy;
			}

			/// <summary>Sites up to <paramref name="budget"/> of a kind on the best cells its rule allows; returns how many.</summary>
			private int SiteKind(PointOfInterestKindRule rule, PointOfInterestKindInfo info, int budget, out int candidateCount)
			{
				bool settlement = info.Has(PointOfInterestTraits.Settlement);
				var candidates = new List<Site>();
				for (int z = 0; z < G.Depth; z++)
				{
					for (int x = 0; x < G.Width; x++)
					{
						int i = z * G.Width + x;
						float east = G.EastOf(x), north = G.NorthOf(z);
						uint cellHash = Hash(Input.Seed, (int)rule.Kind, x, z);
						float radius = Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, Unit(Mix(cellHash ^ 0x7A11u)));
						float blend = info.Has(PointOfInterestTraits.Pad) ? PadBlend(radius) : 0f;
						if (!Inside(east, north, Input.EdgeMarginMetres + radius + blend) || G.Blocked[i])
						{
							continue;
						}
						float weight = BiomeWeight(rule, i);
						if (weight <= 0f)
						{
							continue;
						}
						float h = G.Height[i];
						float altitude = h - Input.SeaLevel;
						if (rule.Underwater)
						{
							float depth = Input.SeaLevel - h;
							if (depth < rule.MinDepth || depth > rule.MaxDepth)
							{
								continue;
							}
						}
						else
						{
							if (altitude < rule.MinAltitude || altitude > rule.MaxAltitude)
							{
								continue;
							}
							if (G.Water[i] != WaterKind.None && G.Water[i] != WaterKind.Bank || G.Sea[i]
								|| (Input.HasSea && h < Input.SeaLevel + Input.LandClearMetres))
							{
								continue;
							}
							/* No water anywhere under the footprint: the rings it is read on can step over a river narrower
							 * than their spacing, and a pad laid across one would dam it. */
							if (G.WaterDistance[i] < radius + 2f || (Input.HasSea && G.SeaDistance[i] < radius))
							{
								continue;
							}
						}
						// A face kind's foot cell reads the face it stands under; FaceScore judges its footing instead.
						if (!rule.Face && G.Slope[i] > rule.MaxSlopeDegrees)
						{
							continue;
						}
						if (!rule.Face && rule.MinHardness > 0f && G.Hardness[i] < rule.MinHardness)
						{
							continue;
						}
						if (rule.CoastMetres > 0f && (!Input.HasSea || G.SeaDistance[i] > rule.CoastMetres))
						{
							continue;
						}
						float wet = Mathf.Min(G.WaterDistance[i], G.SeaDistance[i]);
						if (rule.NearWaterMetres > 0f && wet > rule.NearWaterMetres)
						{
							continue;
						}

						float score;
						if (rule.Face)
						{
							if (!NearSteep(x, z))
							{
								continue;
							}
							(float face, float _) = FaceAt(i);
							float hard = FaceHardness(i);
							if (rule.MinHardness > 0f && hard < rule.MinHardness)
							{
								face *= 0.5f * hard / rule.MinHardness;
							}
							if (face < MinFaceScore)
							{
								continue;
							}
							score = weight * face;
						}
						else
						{
							float flat = 1f - Mathf.Clamp01(G.Slope[i] / Mathf.Max(1f, rule.MaxSlopeDegrees));
							score = weight * (0.6f + 0.4f * flat);
							if (settlement)
							{
								// People settle by water.
								score += 0.3f * (1f - Mathf.Clamp01(wet / 300f));
							}
							if (rule.CoastMetres > 0f)
							{
								score += 0.3f * (1f - Mathf.Clamp01(G.SeaDistance[i] / rule.CoastMetres));
							}
						}
						// A little less near the scene's edge, where a site is half in the next scene's view and far from its players.
						float edge = Mathf.Min(Input.WidthMetres * 0.5f - Mathf.Abs(east), Input.DepthMetres * 0.5f - Mathf.Abs(north));
						score *= 0.8f + 0.2f * Mathf.Clamp01(edge / 400f);
						// Ties between equal ground broken by the scene's seed, never by scan order.
						score += 0.5f * Unit(Mix(cellHash ^ 0x5C0Eu));
						Site site = Site.At(east, north, h, score);
						site.Radius = radius;
						site.Cell = i;
						candidates.Add(site);
					}
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
					site.Yaw = rule.Face ? FaceAt(site.Cell).yaw : YawFor(rule, site);
					Accept(rule, info, site);
					taken++;
				}
				candidateCount = candidates.Count;
				return taken;
			}

			/// <summary>Whether a site keeps its kind's spacing, the gap from every placed site, and its settlement rules.</summary>
			private bool Clear(PointOfInterestKindRule rule, ref Site site)
			{
				float gap = Input.SiteGapMetres;
				foreach (PointOfInterestRecord other in placed)
				{
					float dx = other.Position.x - site.X, dz = other.Position.z - site.Z;
					float d2 = dx * dx + dz * dz;
					if (other.Kind == rule.Kind && d2 < rule.SpacingMetres * rule.SpacingMetres)
					{
						return false;
					}
					float clear = site.Radius + other.Radius + gap;
					if (!(rule.NearSettlementMetres > 0f && settlements.Contains(other)))
					{
						clear = Mathf.Max(clear, Input.MinSiteSpacingMetres);
					}
					if (d2 < clear * clear)
					{
						return false;
					}
				}
				if (rule.AwayFromSettlementsMetres > 0f)
				{
					foreach (PointOfInterestRecord town in settlements)
					{
						float dx = town.Position.x - site.X, dz = town.Position.z - site.Z;
						float away = rule.AwayFromSettlementsMetres + town.Radius;
						if (dx * dx + dz * dz < away * away)
						{
							return false;
						}
					}
				}
				if (rule.NearSettlementMetres > 0f)
				{
					float best = float.PositiveInfinity;
					int parent = -1;
					foreach (PointOfInterestRecord town in settlements)
					{
						float dx = town.Position.x - site.X, dz = town.Position.z - site.Z;
						float edge = Mathf.Sqrt(dx * dx + dz * dz) - town.Radius;
						if (edge < best)
						{
							best = edge;
							parent = town.Id;
						}
					}
					if (best > rule.NearSettlementMetres)
					{
						return false;
					}
					site.ParentId = parent;
				}
				return true;
			}

			/// <summary>
			/// Whether the whole footprint is ground the kind may stand on, read at its centre and on two rings of eight;
			/// sets the site's height (a pad's: the median of what it covers).
			/// </summary>
			private bool Footprint(PointOfInterestKindRule rule, PointOfInterestKindInfo info, ref Site site)
			{
				const int ring = 8;
				var heights = new float[1 + 2 * ring];
				int n = 0;
				float r = site.Radius;
				for (int k = -1; k < 2 * ring; k++)
				{
					float x = site.X, z = site.Z;
					if (k >= 0)
					{
						float a = (k % ring) * (Mathf.PI * 2f / ring) + (k >= ring ? Mathf.PI / ring : 0f);
						float rr = k >= ring ? r * 0.5f : r;
						x += rr * Mathf.Sin(a);
						z += rr * Mathf.Cos(a);
					}
					float h = Input.Ground(x, z);
					if (Input.Blocked != null && Input.Blocked(x, z))
					{
						return false;
					}
					if (rule.Underwater)
					{
						if (h > Input.SeaLevel - 1f)
						{
							return false;
						}
					}
					else if (!DryLand(x, z, h))
					{
						return false;
					}
					if (!rule.Face && G.Sample(G.Slope, x, z) > rule.MaxSlopeDegrees + 5f)
					{
						return false;
					}
					heights[n++] = h;
				}
				float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
				for (int i = 0; i < n; i++)
				{
					lowest = Mathf.Min(lowest, heights[i]);
					highest = Mathf.Max(highest, heights[i]);
				}
				if (!rule.Underwater && !rule.Face && highest - lowest > rule.ReliefFor(r))
				{
					return false;
				}
				if (info.Has(PointOfInterestTraits.Pad) && !rule.Underwater)
				{
					Array.Sort(heights, 0, n);
					site.Y = heights[n / 2];
				}
				else
				{
					site.Y = heights[0];
				}
				return true;
			}

			/// <summary>Records a placed site, with its pad and keep-out when it will be built.</summary>
			private void Accept(PointOfInterestKindRule rule, PointOfInterestKindInfo info, in Site site)
			{
				PointOfInterestRecord record = Add(rule, site, true);
				if (!Builds(rule.Kind))
				{
					return;
				}
				bool pad = info.Has(PointOfInterestTraits.Pad) && !rule.Underwater;
				float blend = PadBlend(record.Radius);
				if (pad)
				{
					Plan.Pads.Add(new PointOfInterestPad { Id = record.Id, Centre = record.Position, Radius = record.Radius, Blend = blend });
				}
				Plan.KeepOuts.Add(new PointOfInterestKeepOut
				{
					Id = record.Id,
					X = record.Position.x,
					Z = record.Position.z,
					Radius = record.Radius + (pad ? blend * 0.5f : 2f),
				});
			}

			/// <summary>
			/// Which way a site faces: toward the sea for a coastal kind, toward water for a settlement beside it, else a
			/// seeded heading.
			/// </summary>
			private float YawFor(PointOfInterestKindRule rule, in Site site)
			{
				int i = site.Cell >= 0 ? site.Cell : G.CellAt(site.X, site.Z);
				float[] field = null;
				if (rule.CoastMetres > 0f && Input.HasSea)
				{
					field = G.SeaDistance;
				}
				else if (PointOfInterestKinds.Info(rule.Kind).Has(PointOfInterestTraits.Settlement) && G.WaterDistance[i] < 400f)
				{
					field = G.WaterDistance;
				}
				if (field != null)
				{
					float dx = G.Sample(field, site.X - G.Cell, site.Z) - G.Sample(field, site.X + G.Cell, site.Z);
					float dz = G.Sample(field, site.X, site.Z - G.Cell) - G.Sample(field, site.X, site.Z + G.Cell);
					if (dx * dx + dz * dz > 1e-4f)
					{
						return YawOf(dx, dz);
					}
				}
				return Mathf.Floor(Unit(Hash(Input.Seed, (int)rule.Kind, i, 0x4A3)) * 360f);
			}

			/// <summary>Whether a cell has steep ground within two cells: the cheap test before a face is scored.</summary>
			private bool NearSteep(int x, int z)
			{
				for (int dz = -2; dz <= 2; dz++)
				{
					for (int dx = -2; dx <= 2; dx++)
					{
						int xx = x + dx, zz = z + dz;
						if (xx >= 0 && zz >= 0 && xx < G.Width && zz < G.Depth && G.Slope[zz * G.Width + xx] >= FaceCellDegrees)
						{
							return true;
						}
					}
				}
				return false;
			}

			/// <summary>A cell's face score (hardness left out) and heading, worked out once for every face kind.</summary>
			private (float score, float yaw) FaceAt(int cell)
			{
				faces ??= new Dictionary<int, (float, float)>();
				if (!faces.TryGetValue(cell, out (float score, float yaw) face))
				{
					int x = cell % G.Width, z = cell / G.Width;
					float score = FaceScore(Input.Ground, null, G.EastOf(x), G.NorthOf(z), 0f, out float yaw);
					face = (score, yaw);
					faces[cell] = face;
				}
				return face;
			}

			/// <summary>The hardness of the rock in a cell's face: a cell behind its foot, the way the face lies.</summary>
			private float FaceHardness(int cell)
			{
				(float score, float yaw) = FaceAt(cell);
				if (score <= 0f)
				{
					return G.Hardness[cell];
				}
				float a = (yaw + 180f) * Mathf.Deg2Rad;
				int x = cell % G.Width, z = cell / G.Width;
				int behind = G.CellAt(G.EastOf(x) + Mathf.Sin(a) * G.Cell, G.NorthOf(z) + Mathf.Cos(a) * G.Cell);
				return Mathf.Max(G.Hardness[cell], G.Hardness[behind]);
			}

			/// <summary>
			/// Bridges where a river crosses the way between two settlements: the river point nearest the line joining
			/// them, spanning the water across its flow.
			/// </summary>
			private void PlaceBridges(PointOfInterestKindRule rule, int budget)
			{
				if (settlements.Count < 2 || Input.Water == null || Input.Water.Rivers == null || Input.Water.Rivers.Count == 0)
				{
					return;
				}
				var candidates = new List<Site>();
				for (int a = 0; a < settlements.Count; a++)
				{
					for (int b = a + 1; b < settlements.Count; b++)
					{
						Vector3 pa = settlements[a].Position, pb = settlements[b].Position;
						float sx = pb.x - pa.x, sz = pb.z - pa.z;
						float length2 = sx * sx + sz * sz;
						if (length2 < 1f || length2 > 3000f * 3000f)
						{
							continue;
						}
						foreach (RiverPath river in Input.Water.Rivers)
						{
							if (river == null || !river.Perennial || river.Count < 3)
							{
								continue;
							}
							Site best = default;
							bool found = false;
							for (int k = 1; k < river.Count - 1; k++)
							{
								float width = river.Width[k];
								if (width > 40f)
								{
									continue;
								}
								float px = river.X[k] - pa.x, pz = river.Z[k] - pa.z;
								float t = Mathf.Clamp01((px * sx + pz * sz) / length2);
								float ex = px - t * sx, ez = pz - t * sz;
								float off = Mathf.Sqrt(ex * ex + ez * ez);
								if (off > Mathf.Max(width, 10f) || t < 0.1f || t > 0.9f)
								{
									continue;
								}
								float score = 1f - Mathf.Abs(t - 0.5f) - off / 100f - width / 200f;
								if (found && score <= best.Score)
								{
									continue;
								}
								float tx = river.X[k + 1] - river.X[k - 1], tz = river.Z[k + 1] - river.Z[k - 1];
								float tl = Mathf.Max(1e-3f, Mathf.Sqrt(tx * tx + tz * tz));
								float nx = -tz / tl, nz = tx / tl;
								float reach = width * 0.5f + 4f;
								float y = Mathf.Max(Input.Ground(river.X[k] + nx * reach, river.Z[k] + nz * reach), Input.Ground(river.X[k] - nx * reach, river.Z[k] - nz * reach));
								best = Site.At(river.X[k], river.Z[k], y, score);
								best.Radius = Mathf.Clamp(width * 0.5f + 6f, rule.FootprintMin, rule.FootprintMax);
								best.Yaw = Mathf.Round(YawOf(nx, nz));
								best.RiverId = river.Id;
								best.PlanetRiver = river.PlanetRiver;
								best.ParentId = settlements[a].Id;
								best.Cell = G.CellAt(river.X[k], river.Z[k]);
								found = true;
							}
							if (found)
							{
								candidates.Add(best);
							}
						}
					}
				}
				candidates.Sort((p, q) =>
				{
					int c = q.Score.CompareTo(p.Score);
					if (c != 0) return c;
					c = p.X.CompareTo(q.X);
					return c != 0 ? c : p.Z.CompareTo(q.Z);
				});
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(rule.Kind);
				int taken = 0;
				foreach (Site candidate in candidates)
				{
					if (taken >= budget)
					{
						break;
					}
					Site site = candidate;
					if (!Inside(site.X, site.Z, Input.EdgeMarginMetres + site.Radius))
					{
						continue;
					}
					// A bridge stands among the towns it joins: only its own kind's spacing and the other non-settlement sites' gaps.
					bool clear = true;
					foreach (PointOfInterestRecord other in placed)
					{
						if (settlements.Contains(other))
						{
							continue;
						}
						float dx = other.Position.x - site.X, dz = other.Position.z - site.Z;
						float need = other.Kind == rule.Kind ? rule.SpacingMetres : site.Radius + other.Radius + Input.SiteGapMetres;
						if (dx * dx + dz * dz < need * need)
						{
							clear = false;
							break;
						}
					}
					if (!clear)
					{
						continue;
					}
					Accept(rule, info, site);
					taken++;
				}
			}
		}
	}
}
#endif
