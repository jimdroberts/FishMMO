#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
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
		/// when the scene asked for it and none otherwise. The planner passes the area the kind's biomes hold, by their
		/// weights (<see cref="State.HostedAreaKm2"/>), not the scene's.
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

		/// <summary>
		/// The scene's area as a kind sees it (2026-10-25): each biome's share of the scene's cells times its weight for the kind
		/// (<see cref="PointOfInterestBiome.WeightOf"/>), a cell with no biome at the kind's default. Weight 0 counts nothing, 0.2 a
		/// fifth and 2 twice over, so a biome's favoured kinds turn up more and a kind it does not host is not squeezed onto the
		/// scene's other ground at the whole scene's count, as it was while the weight only ranked the cells.
		/// </summary>
		/// <param name="cellsPerBiome">Cells per entry of <paramref name="biomes"/>; one more entry past them for the cells with no biome.</param>
		public static float HostedAreaKm2(PointOfInterestKindRule rule, float sceneAreaKm2, IReadOnlyList<int> cellsPerBiome, IReadOnlyList<PointOfInterestBiome> biomes)
		{
			if (rule == null || cellsPerBiome == null)
			{
				return 0f;
			}
			long cells = 0;
			float weighted = 0f;
			for (int b = 0; b < cellsPerBiome.Count; b++)
			{
				int count = cellsPerBiome[b];
				if (count <= 0)
				{
					continue;
				}
				cells += count;
				PointOfInterestBiome biome = biomes != null && b < biomes.Count ? biomes[b] : null;
				weighted += count * (biome != null ? biome.WeightOf(rule) : Mathf.Max(0f, rule.DefaultBiomeWeight));
			}
			return cells == 0 ? 0f : Mathf.Max(0f, sceneAreaKm2) * weighted / cells;
		}

		internal sealed partial class State
		{
			/// <summary>Face scores per cell, worked out once and shared by every face kind: (score, yaw).</summary>
			private Dictionary<int, (float score, float yaw)> faces;

			/// <summary>
			/// How deep a point lies under the water an underwater kind stands in: below a lake's own level where the kind lies under
			/// lakes (<see cref="PointOfInterestKindRule.InLakes"/>) and a lake covers it, else below the sea's (NaN with no sea of
			/// water), exactly as every sunken kind was sited before (decision 35 (d), 2026-10-18).
			/// </summary>
			private float UnderwaterDepth(PointOfInterestKindRule rule, float east, float north, float ground, WaterKind water)
			{
				float lake = float.NaN;
				if (rule.InLakes && water == WaterKind.Lake && Input.Water?.SurfaceAt != null)
				{
					lake = Input.Water.SurfaceAt(east, north);
				}
				if (!rule.InLakes)
				{
					return Input.SeaLevel - ground;
				}
				return PointOfInterestWaterSites.SunkenDepth(ground, lake, Input.HasSea && Input.SeaIsWater, Input.SeaLevel);
			}

			/// <summary>
			/// The depth band an underwater kind stands in at a cell: its biome's own under the sea (BiomeTemplate.UnderwaterSiteDepth,
			/// 2026-10-25, phase I2m, decision 36 (b): an abyss's wrecks at 6 000 m), else the kind's, as every sunken kind was sited
			/// before; always the kind's under a lake.
			/// </summary>
			private Vector2 DepthBand(PointOfInterestKindRule rule, int cell)
			{
				// Afloat (2026-10-25): the kind's own band, never the biome's floor-site depths.
				if (rule.Afloat)
				{
					return new Vector2(rule.MinDepth, rule.MaxDepth);
				}
				int b = G.Biome[cell];
				PointOfInterestBiome biome = b >= 0 && b < Input.Biomes.Count ? Input.Biomes[b] : null;
				bool lake = rule.InLakes && G.Water[cell] == WaterKind.Lake;
				return biome == null || lake ? new Vector2(rule.MinDepth, rule.MaxDepth) : BiomeSeaFloors.SiteDepth(biome.UnderwaterDepth, rule.MinDepth, rule.MaxDepth);
			}

			/// <summary>Cells per biome index (<see cref="Grid.Biome"/>), the last entry the cells with no biome; counted once.</summary>
			private int[] biomeCells;

			/// <summary>The area this scene's biomes hold for a kind, by their weights (<see cref="PointOfInterestPlanner.HostedAreaKm2"/>).</summary>
			internal float HostedAreaKm2(PointOfInterestKindRule rule)
			{
				if (biomeCells == null)
				{
					biomeCells = new int[Input.Biomes.Count + 1];
					foreach (int b in G.Biome)
					{
						biomeCells[b >= 0 && b < Input.Biomes.Count ? b : Input.Biomes.Count]++;
					}
				}
				return PointOfInterestPlanner.HostedAreaKm2(rule, areaKm2, biomeCells, Input.Biomes);
			}

			/// <summary>Sites a placed kind on the best cells it may stand on.</summary>
			public void Place(PointOfInterestKindRule rule)
			{
				int budget = Budget(rule, Input.Settings, HostedAreaKm2(rule), Input.Seed);
				if (budget <= 0)
				{
					return;
				}
				if (rule.RiverCrossing || rule.Kind == POIType.Bridge)
				{
					PlaceBridges(rule, budget);
					return;
				}
				if (rule.RiverBank)
				{
					PlaceRiverBanks(rule, budget);
					return;
				}
				if (rule.TidalIslandMetres > 0f)
				{
					PlaceTidalIslands(rule, budget);
					return;
				}
				// A kind under lakes too (decision 35 (d): InLakes) is sited under a lake by its own level even with no sea.
				if (rule.Underwater && (!Input.HasSea || !Input.SeaIsWater) && !(rule.InLakes && Input.Water != null && Input.Water.SurfaceAt != null))
				{
					return;
				}
				if (rule.NearSettlementMetres > 0f && settlements.Count == 0)
				{
					return;
				}

				PointOfInterestKindInfo info = PointOfInterestKinds.Info(rule.Kind);
				// Caves where their water is first (behind a waterfall, under water: PointOfInterestPlanner.CaveWater), in the room
				// the kind's most leaves past its budget: the fall or the shore decides where they stand, and a dry face found first
				// beside the gorge took it. The dry caves' budget is unchanged.
				PlaceWaterCaves(rule, info, rule.Max - budget);
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
				// An outlined site may reach past its drawn radius where its ground runs on level: kept that far from the edge.
				float stretch = Outlines(rule, info) ? OutlineStretch : 1f;
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
						if (!Inside(east, north, Input.EdgeMarginMetres + radius * stretch + blend) || G.Blocked[i])
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
							float depth = UnderwaterDepth(rule, east, north, h, G.Water[i]);
							Vector2 band = DepthBand(rule, i);
							if (!(depth >= band.x) || depth > band.y)
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
						if (rule.CoastMetres > 0f && (!Input.HasSea || G.OpenSeaDistance[i] > rule.CoastMetres))
						{
							continue;
						}
						if (rule.OpenWaterShare > 0f && (!Input.HasSea || G.OpenShareAt(i, PointOfInterestOpenSea.HarbourReach) < rule.OpenWaterShare))
						{
							continue;
						}
						// Adrift sites (2026-10-25): far out on the open sea only.
						if (rule.OffshoreMetres > 0f && (!Input.HasSea || G.LandDistance[i] < rule.OffshoreMetres))
						{
							continue;
						}
						float wet = Mathf.Min(G.WaterDistance[i], G.SeaDistance[i]);
						if (rule.NearWaterMetres > 0f && wet > rule.NearWaterMetres)
						{
							continue;
						}
						// By a landform a detector finds (2026-10-27: a pass fort at its pass, a cairn on its peak), the nearer the better.
						float landform = 0f;
						if (rule.NearDetector != PointOfInterestDetector.None)
						{
							float[] near = DetectedDistance(rule);
							if (near == null || near[i] > rule.NearDetectedMetres)
							{
								continue;
							}
							landform = 0.4f * (1f - near[i] / rule.NearDetectedMetres);
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
								score += 0.3f * (1f - Mathf.Clamp01(G.OpenSeaDistance[i] / rule.CoastMetres));
							}
							if (rule.Exposed)
							{
								score += 0.8f * Exposure(east, north, h);
							}
						}
						score += landform;
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
					if (!Shape(rule, info, ref site) || !Clear(rule, ref site) || !Footprint(rule, info, ref site))
					{
						continue;
					}
					site.Yaw = rule.Face ? FaceAt(site.Cell).yaw : PointOfInterestOutline.Drawn(site.Outline) && Plots(rule.Kind) ? PlotYaw(site) : YawFor(rule, site);
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
			/// Whether the whole footprint is ground the kind may stand on, read at its centre and on two rings (its edge and half
			/// way in: eight bearings each for a disc, sixteen along an outline); sets the site's height (a pad's: the median of
			/// what it covers).
			/// </summary>
			private bool Footprint(PointOfInterestKindRule rule, PointOfInterestKindInfo info, ref Site site)
			{
				bool outlined = PointOfInterestOutline.Drawn(site.Outline);
				int ring = outlined ? 16 : 8;
				var heights = new float[1 + 2 * ring];
				int n = 0;
				float r = outlined ? site.Drawn : site.Radius;
				for (int k = -1; k < 2 * ring; k++)
				{
					float x = site.X, z = site.Z;
					if (k >= 0)
					{
						float a = (k % ring) * (Mathf.PI * 2f / ring) + (k >= ring ? Mathf.PI / ring : 0f);
						float edge = outlined ? PointOfInterestOutline.ReachAt(site.Outline, a) : r;
						float rr = k >= ring ? edge * 0.5f : edge;
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
						// Under its water all over: a lake's own level for a kind that lies under lakes (decision 35 (d)), else the sea's.
						if (rule.InLakes ? !(UnderwaterDepth(rule, x, z, h, Input.Water?.KindAt != null ? Input.Water.KindAt(x, z) : WaterKind.None) >= 1f) : h > Input.SeaLevel - 1f)
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
				if (rule.Afloat)
				{
					// On the water, not its floor: a hull's builder rides the tide from here (PointOfInterestAdrift, FloatingBoats).
					site.Y = Input.SeaLevel;
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
					var levelled = new PointOfInterestPad { Id = record.Id, Centre = record.Position, Radius = record.Radius, Blend = blend, Outline = record.Outline };
					if (rule.RiverBank)
					{
						// Levelled behind the mill only: its front faces the water, which starts BankSetback out.
						float yaw = record.Yaw * Mathf.Deg2Rad;
						levelled.Shore = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
						levelled.ShoreDistance = BankSetback;
					}
					// On a hillside a town's pad steps up it in terraces (the site then stands at its centre's level).
					Terrace(record, ref levelled);
					Plan.Pads.Add(levelled);
				}
				float kept = pad ? blend * 0.5f : 2f;
				Plan.KeepOuts.Add(new PointOfInterestKeepOut
				{
					Id = record.Id,
					X = record.Position.x,
					Z = record.Position.z,
					Radius = record.Radius + kept,
					// The site's own outline, grown by the same margin all round.
					Outline = PointOfInterestOutline.Grown(record.Outline, kept),
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
					field = G.OpenSeaDistance;
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
				// Never on a waterfall's reach, lip to pool (the way's planner will not cross there either).
				FallReaches falls = FallReaches.Of(Input.Water.Rivers, MinFallMetres);
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
								if (width > 40f || falls.Contains(river.X[k], river.Z[k], 4f))
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

			/// <summary>
			/// How far ground stands over its surroundings, 0 … 1: its height over the mean of a ring 60 m out, full at 12 m
			/// (a windmill wants the wind off a rise, not a hollow's lee).
			/// </summary>
			private float Exposure(float east, float north, float h)
			{
				const int ring = 8;
				float sum = 0f;
				int n = 0;
				for (int k = 0; k < ring; k++)
				{
					float a = k * (Mathf.PI * 2f / ring);
					int c = G.CellAt(east + 60f * Mathf.Sin(a), north + 60f * Mathf.Cos(a));
					if (c >= 0)
					{
						sum += G.Height[c];
						n++;
					}
				}
				return n == 0 ? 0f : Mathf.Clamp01((h - sum / n) / 12f);
			}

			/// <summary>
			/// Sites on a river's bank (a watermill, a sawmill): along every perennial river 2.5–30 m wide, on a run or a
			/// riffle well clear of any fall, either bank, the site standing back from the water's edge by
			/// <see cref="BankSetback"/> so its building's front (the wheel's side) is at the edge, facing the river. The bank
			/// must stand 0.2–3.5 m over the water and the ground behind it be dry and near level. Its pad keeps off the water
			/// as every pad does.
			/// </summary>
			private void PlaceRiverBanks(PointOfInterestKindRule rule, int budget)
			{
				if (Input.Water == null || Input.Water.Rivers == null || Input.Water.Rivers.Count == 0)
				{
					return;
				}
				if (rule.NearSettlementMetres > 0f && settlements.Count == 0)
				{
					return;
				}
				FallReaches falls = FallReaches.Of(Input.Water.Rivers, MinFallMetres);
				var candidates = new List<Site>();
				foreach (RiverPath river in Input.Water.Rivers)
				{
					if (river == null || !river.Perennial || river.Count < 6)
					{
						continue;
					}
					for (int k = 2; k < river.Count - 2; k += 3)
					{
						float width = river.Width[k];
						if (width < 2.5f || width > 30f || falls.Contains(river.X[k], river.Z[k], 40f))
						{
							continue;
						}
						if (river.Reach != null && k < river.Reach.Length && river.Reach[k] != RiverReach.Run && river.Reach[k] != RiverReach.Riffle)
						{
							continue;
						}
						float tx = river.X[k + 1] - river.X[k - 1], tz = river.Z[k + 1] - river.Z[k - 1];
						float tl = Mathf.Max(1e-3f, Mathf.Sqrt(tx * tx + tz * tz));
						float nx = -tz / tl, nz = tx / tl;
						float surface = river.Surface != null && k < river.Surface.Length ? river.Surface[k] : float.NaN;
						for (int side = -1; side <= 1; side += 2)
						{
							float off = width * 0.5f + BankSetback;
							float bx = river.X[k] + nx * side * off, bz = river.Z[k] + nz * side * off;
							int cell = G.CellAt(bx, bz);
							if (cell < 0 || G.Blocked[cell] || G.Sea[cell] || !Inside(bx, bz, Input.EdgeMarginMetres + rule.FootprintMax))
							{
								continue;
							}
							float weight = BiomeWeight(rule, cell);
							if (weight <= 0f)
							{
								continue;
							}
							float ground = Input.Ground(bx, bz);
							float bank = float.IsNaN(surface) ? 1f : ground - surface;
							// The mill is seated by the water, not the bank (below): a higher bank is cut back behind it.
							if (bank < 0.2f || bank > MaxBankMetres || G.Slope[cell] > rule.MaxSlopeDegrees)
							{
								continue;
							}
							float altitude = ground - Input.SeaLevel;
							if (altitude < rule.MinAltitude || altitude > rule.MaxAltitude || (Input.HasSea && ground < Input.SeaLevel + Input.LandClearMetres))
							{
								continue;
							}
							// The yard behind the mill: dry, and not a bank climbing away.
							float yx = bx + nx * side * 12f, yz = bz + nz * side * 12f;
							int yard = G.CellAt(yx, yz);
							if (yard < 0 || G.Water[yard] != WaterKind.None && G.Water[yard] != WaterKind.Bank || Mathf.Abs(Input.Ground(yx, yz) - ground) > 4f)
							{
								continue;
							}
							uint hash = Hash(Input.Seed, (int)rule.Kind, Mathf.RoundToInt(bx), Mathf.RoundToInt(bz));
							float score = weight * (1f - 0.25f * Mathf.Abs(bank - StructurePieces.RiverBankFreeboard)) + 0.5f * Unit(Mix(hash ^ 0x5C0Eu));
							// The floor at the freeboard its wheel is hung for (2026-10-15: "Watermills seem to sink into the ground"):
							// seated on the bank's own height, a mill on a 3 m bank turned its wheel in the air, and its pad-less yard
							// rose behind it over its plinth. The pad levels the yard to this floor (PointOfInterestPad.Shore).
							float floor = float.IsNaN(surface) ? ground : surface + StructurePieces.RiverBankFreeboard;
							Site site = Site.At(bx, bz, floor, score);
							site.Radius = Mathf.Lerp(rule.FootprintMin, rule.FootprintMax, Unit(Mix(hash ^ 0x7A11u)));
							// Facing the river: a site's front faces its heading.
							site.Yaw = Mathf.Round(YawOf(-nx * side, -nz * side));
							site.RiverId = river.Id;
							site.PlanetRiver = river.PlanetRiver;
							site.Cell = cell;
							candidates.Add(site);
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
					if (!Clear(rule, ref site))
					{
						continue;
					}
					Accept(rule, info, site);
					taken++;
				}
				if (taken < budget && candidates.Count > 0)
				{
					Plan.Notes.Add($"{info.DisplayName}: {taken} of {budget} sited on river banks; no more banks fit.");
				}
			}

			/// <summary>
			/// How far back from the water's edge a river-bank site's centre stands, metres: half a mill's depth and a little,
			/// so its front wall stands at the water's edge and its wheel turns wholly over the water.
			/// </summary>
			private const float BankSetback = 3.8f;

			/// <summary>The highest bank over the water a mill is sited on, metres: the yard is cut down to the mill's floor behind it.</summary>
			private const float MaxBankMetres = 2.4f;
		}
	}
}
#endif
