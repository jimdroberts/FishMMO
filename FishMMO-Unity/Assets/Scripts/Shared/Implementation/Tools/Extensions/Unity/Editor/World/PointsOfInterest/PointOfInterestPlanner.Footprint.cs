#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class PointOfInterestPlanner
	{
		/// <summary>How far past its drawn radius a site's outline may reach where its ground runs on level, as a share.</summary>
		public const float OutlineStretch = 1.3f;

		/// <summary>The least a site's outline reaches toward any bearing, as a share of its drawn radius.</summary>
		public const float OutlineLeast = 0.6f;

		/// <summary>The least share of its drawn disc's area an outline keeps; a site squeezed smaller by water or a cliff is not sited.</summary>
		public const float OutlineLeastArea = 0.55f;

		/// <summary>The fall across a footprint, up its slope, at which a settlement's pad is terraced rather than levelled, metres.</summary>
		public const float TerraceMinFall = 4f;

		/// <summary>The rise a terrace aims for, metres: a storey and a half, banked.</summary>
		public const float TerraceTargetRise = 3.2f;

		/// <summary>The least level ground a terrace keeps either side of its ramps' runs, metres.</summary>
		public const float TerraceLeastLevel = 10f;

		/// <summary>True for a kind whose site is drawn as an outline: every pad kind but a face's, a river bank's and a bridge's.</summary>
		internal static bool Outlines(PointOfInterestKindRule rule, PointOfInterestKindInfo info)
			=> info.Has(PointOfInterestTraits.Pad) && !rule.Face && !rule.RiverBank && !rule.RiverCrossing && !rule.Underwater && rule.Kind != POIType.Bridge;

		/// <summary>True for a kind whose pad may be terraced: the places people live and hold, laid out by streets and walls.</summary>
		internal static bool Terraces(POIType kind)
		{
			switch (kind)
			{
				case POIType.Village:
				case POIType.Town:
				case POIType.City:
				case POIType.Capital:
				case POIType.Port:
				case POIType.Keep:
				case POIType.Castle:
				case POIType.Fortress:
				case POIType.Monastery:
					return true;
				default:
					return false;
			}
		}

		/// <summary>How irregular a kind's outline is drawn: (elongation, ripple, how strongly it follows the ground).</summary>
		private static (float elongation, float ripple, float follow) Irregularity(POIType kind, PointOfInterestKindInfo info)
		{
			switch (kind)
			{
				case POIType.Village:
				case POIType.Town:
				case POIType.City:
				case POIType.Capital:
				case POIType.Port:
				case POIType.StiltVillage:
					// Towns grow along their roads and waters and round what they cannot build on.
					return (0.24f, 0.07f, 0.6f);
				case POIType.Keep:
				case POIType.Castle:
				case POIType.Fortress:
					// A stronghold keeps to its rise: it follows the ground's lie closely, its wall otherwise compact.
					return (0.14f, 0.04f, 0.75f);
			}
			return info.Has(PointOfInterestTraits.Settlement) ? (0.18f, 0.05f, 0.5f) : (0.12f, 0.04f, 0.4f);
		}

		internal sealed partial class State
		{
			/// <summary>
			/// Draws a site's outline (<see cref="PointOfInterestOutline"/>) on the ground it stands on: further where the ground
			/// runs on level with it, nearer where it climbs or falls away, never over water, a holed canyon wall or the scene's
			/// edge, stretched along the lie of the land (a hillside's contour, a river's bank, a ridge or a valley's floor) and
			/// rippled by the site's seed. Its area stays near the drawn disc's, so a template's pieces still find room. Sets the
			/// site's outline, keeps its drawn radius and makes its radius the outline's bounding reach; false when the ground
			/// squeezes it below <see cref="OutlineLeastArea"/> of the disc.
			/// </summary>
			/// <remarks>
			/// Its own stream (the kind, the cell and a salt), drawn after every other choice for the site, so drawing the outline
			/// moves no other site and no other draw.
			/// </remarks>
			public bool Shape(PointOfInterestKindRule rule, PointOfInterestKindInfo info, ref Site site)
			{
				site.Drawn = site.Radius;
				if (!Outlines(rule, info))
				{
					return true;
				}
				const int N = PointOfInterestOutline.Bearings;
				float r0 = site.Radius, h0 = Input.Ground(site.X, site.Z);
				float margin = Input.EdgeMarginMetres + PadBlend(r0);
				(float elongation, float ripple, float follow) = Irregularity(rule.Kind, info);
				uint hash = Hash(Input.Seed, (int)rule.Kind, site.Cell, 0x0071E5);

				var limit = new float[N];
				var grade = new float[N];
				float step = Mathf.Max(2f, r0 / 16f);
				for (int k = 0; k < N; k++)
				{
					Vector2 dir = PointOfInterestOutline.Direction(k, N);
					float most = r0 * OutlineStretch;
					// Out along the bearing until water, a holed wall or the scene's edge: the outline stops short of it.
					for (float t = r0 * 0.3f; t <= most; t += step)
					{
						float x = site.X + dir.x * t, z = site.Z + dir.y * t;
						if (!Inside(x, z, margin))
						{
							most = Mathf.Max(r0 * 0.3f, t - step);
							break;
						}
						if ((Input.Blocked != null && Input.Blocked(x, z)) || !DryLand(x, z, Input.Ground(x, z)))
						{
							most = Mathf.Max(r0 * 0.3f, t - 6f);
							break;
						}
					}
					limit[k] = most;
					// The ground's grade from the site along the bearing: level ground is built on, a hillside is not.
					float sum = 0f;
					int n = 0;
					foreach (float reachShare in new[] { 0.5f, 0.8f, 1.1f })
					{
						float t = Mathf.Min(r0 * reachShare, most);
						if (t < 1f)
						{
							continue;
						}
						sum += Mathf.Abs(Input.Ground(site.X + dir.x * t, site.Z + dir.y * t) - h0) / t;
						n++;
					}
					grade[k] = n > 0 ? sum / n : 0f;
				}

				// The lie of the land: along a near shore, else across the slope (its contour), else the seed's.
				float axis = Unit(Mix(hash ^ 0xA715u)) * Mathf.PI;
				float wet = Mathf.Min(G.Sample(G.WaterDistance, site.X, site.Z), Input.HasSea ? G.Sample(G.SeaDistance, site.X, site.Z) : float.PositiveInfinity);
				Vector2 slope = Gradient(site.X, site.Z, r0 * 0.6f);
				if (wet < r0 * 2.5f)
				{
					float[] field = Input.HasSea && G.Sample(G.SeaDistance, site.X, site.Z) <= G.Sample(G.WaterDistance, site.X, site.Z) ? G.SeaDistance : G.WaterDistance;
					float gx = G.Sample(field, site.X + G.Cell, site.Z) - G.Sample(field, site.X - G.Cell, site.Z);
					float gz = G.Sample(field, site.X, site.Z + G.Cell) - G.Sample(field, site.X, site.Z - G.Cell);
					if (gx * gx + gz * gz > 1e-4f)
					{
						// Along the shore: square to the way toward the water.
						axis = Mathf.Atan2(gx, gz) + Mathf.PI * 0.5f;
					}
				}
				else if (slope.sqrMagnitude > 0.03f * 0.03f)
				{
					axis = Mathf.Atan2(slope.x, slope.y) + Mathf.PI * 0.5f;
				}
				float stretch = elongation * (0.45f + 0.55f * Unit(Mix(hash ^ 0xE10Bu)));
				var amplitude = new float[4];
				var phase = new float[4];
				for (int m = 0; m < 4; m++)
				{
					amplitude[m] = ripple * Unit(Mix(hash ^ (0x51u + (uint)m * 0x9E3779B1u))) / (1f + 0.35f * m);
					phase[m] = Unit(Mix(hash ^ (0x7A3u + (uint)m * 0x85EBCA6Bu))) * Mathf.PI * 2f;
				}

				var log = new float[N];
				for (int k = 0; k < N; k++)
				{
					float a = k * (2f * Mathf.PI / N);
					// Ground falling or climbing at 12 % away halves a bearing's pull; follow says how far the outline heeds it.
					float lie = -follow * grade[k] / 0.12f * Mathf.Log(2f);
					float shape = stretch * Mathf.Cos(2f * (a - axis));
					for (int m = 0; m < 4; m++)
					{
						shape += amplitude[m] * Mathf.Cos((m + 3) * a + phase[m]);
					}
					log[k] = Mathf.Max(lie, -1.5f) + shape;
				}
				// Smoothed round the ring once, so the edge bends rather than zigzags.
				var share = new float[N];
				for (int k = 0; k < N; k++)
				{
					share[k] = Mathf.Exp(0.25f * log[(k + N - 1) % N] + 0.5f * log[k] + 0.25f * log[(k + 1) % N]);
				}
				// The drawn disc's area kept: the polygon's area goes as the sum of neighbouring reaches' products.
				float products = 0f;
				for (int k = 0; k < N; k++)
				{
					products += share[k] * share[(k + 1) % N];
				}
				float scale = Mathf.Sqrt(N / Mathf.Max(1e-4f, products));
				var outline = new float[N];
				for (int k = 0; k < N; k++)
				{
					float r = Mathf.Clamp(share[k] * scale, OutlineLeast, OutlineStretch) * r0;
					outline[k] = Mathf.Round(Mathf.Min(r, limit[k]) * 100f) / 100f;
				}
				if (PointOfInterestOutline.Area(outline, r0) < OutlineLeastArea * Mathf.PI * r0 * r0)
				{
					return false;
				}
				site.Outline = outline;
				site.Radius = PointOfInterestOutline.Max(outline, r0);
				return true;
			}

			/// <summary>The ground's mean slope across a ring of <paramref name="reach"/> round a point (rise per metre, x east, y north).</summary>
			private Vector2 Gradient(float x, float z, float reach)
			{
				const int ring = 16;
				float gx = 0f, gz = 0f;
				for (int k = 0; k < ring; k++)
				{
					float a = k * (2f * Mathf.PI / ring);
					float s = Mathf.Sin(a), c = Mathf.Cos(a);
					float h = Input.Ground(x + s * reach, z + c * reach);
					gx += h * s;
					gz += h * c;
				}
				// A plane's height round a ring is its mean plus g·(s, c)·reach: the first harmonic's weights over the ring.
				return new Vector2(gx, gz) * (2f / (ring * Mathf.Max(1f, reach)));
			}

			/// <summary>
			/// Terraces a settlement's pad on a hillside (<see cref="PointOfInterestTerraces"/>): where the ground falls
			/// <see cref="TerraceMinFall"/> or more up its slope across the outline, two to four levels in bands across the slope,
			/// each the median of its own band's ground, each bank given a ramp, the ramps alternating sides so a lane climbs the
			/// town in switchbacks. Leaves the pad level where the ground does not fall that far or the bands would be too narrow
			/// for their ramps. Sets the pad's (and so the site's) height to the level its centre stands on.
			/// </summary>
			public void Terrace(PointOfInterestRecord record, ref PointOfInterestPad pad)
			{
				if (!Terraces(record.Kind) || !PointOfInterestOutline.Drawn(record.Outline))
				{
					return;
				}
				float[] outline = record.Outline;
				int n = outline.Length;
				float cx = record.Position.x, cz = record.Position.z;
				float least = PointOfInterestOutline.Min(outline, record.Radius);
				Vector2 slope = Gradient(cx, cz, least * 0.6f);
				float grade = slope.magnitude;
				if (grade < 1e-3f)
				{
					return;
				}
				Vector2 axis = slope / grade;
				float uMin = float.PositiveInfinity, uMax = float.NegativeInfinity;
				for (int k = 0; k < n; k++)
				{
					Vector2 p = PointOfInterestOutline.Direction(k, n) * outline[k];
					float u = Vector2.Dot(p, axis);
					uMin = Mathf.Min(uMin, u);
					uMax = Mathf.Max(uMax, u);
				}
				// The pad stands inside its outline's 0.9 (the layout's boundary) and its ramps' runs need room besides.
				float span = (uMax - uMin) * 0.9f;
				uMin *= 0.9f;
				float fall = grade * span;
				if (fall < TerraceMinFall)
				{
					return;
				}
				// The ground inside the outline, sampled once, to take each band's level from.
				float spacing = Mathf.Max(3f, record.Radius / 12f);
				var samples = new List<(float u, float h)>();
				for (float z = cz - record.Radius; z <= cz + record.Radius; z += spacing)
				{
					for (float x = cx - record.Radius; x <= cx + record.Radius; x += spacing)
					{
						if (PointOfInterestOutline.Contains(outline, record.Radius, x - cx, z - cz))
						{
							samples.Add(((x - cx) * axis.x + (z - cz) * axis.y, Input.Ground(x, z)));
						}
					}
				}
				for (int levels = Mathf.Clamp(Mathf.RoundToInt(fall / TerraceTargetRise), 2, 4); levels >= 2; levels--)
				{
					float width = span / levels, rise = fall / levels;
					// Each band wide enough for a ramp's run up both its banks and level ground between them.
					if (width < rise / PointOfInterestTerraces.RampGrade + 2f * TerraceLeastLevel)
					{
						continue;
					}
					var steps = new float[levels - 1];
					for (int i = 0; i < steps.Length; i++)
					{
						steps[i] = Mathf.Round((uMin + (i + 1) * width) * 100f) / 100f;
					}
					var heights = new float[levels];
					bool rising = true;
					for (int b = 0; b < levels; b++)
					{
						float lo = b == 0 ? float.NegativeInfinity : steps[b - 1], hi = b == levels - 1 ? float.PositiveInfinity : steps[b];
						var band = new List<float>();
						foreach ((float u, float h) in samples)
						{
							if (u > lo && u <= hi)
							{
								band.Add(h);
							}
						}
						band.Sort();
						heights[b] = Mathf.Round((band.Count > 0 ? band[band.Count / 2] : record.Position.y + grade * (uMin + (b + 0.5f) * width)) * 100f) / 100f;
						rising &= b == 0 || heights[b] - heights[b - 1] >= 1f;
					}
					if (!rising)
					{
						continue;
					}
					// The ramps: one up each bank, on alternate sides, as far out as the bank's own width allows.
					Vector2 across = PointOfInterestTerraces.Across(axis);
					float rampHalf = Mathf.Clamp(record.Radius * 0.05f, 3.5f, 6f);
					uint hash = Hash(Input.Seed, (int)record.Kind, record.Id, 0x7E44A5);
					float side = Unit(Mix(hash)) < 0.5f ? -1f : 1f;
					var ramps = new float[steps.Length];
					for (int i = 0; i < steps.Length; i++)
					{
						float chord = HalfChord(outline, record.Radius, axis, across, steps[i]);
						float room = Mathf.Max(0f, chord * 0.9f - rampHalf - 8f);
						float off = steps.Length == 1 ? 0.15f : 0.4f;
						ramps[i] = Mathf.Round(side * Mathf.Min(room, chord * off) * 100f) / 100f;
						side = -side;
					}
					pad.TerraceAxis = axis;
					pad.TerraceSteps = steps;
					pad.TerraceLevels = heights;
					pad.TerraceRamps = ramps;
					pad.RampHalfWidth = rampHalf;
					float centre = Mathf.Round(PointOfInterestTerraces.LevelAt(pad, cx, cz) * 100f) / 100f;
					pad.Centre = new Vector3(cx, centre, cz);
					record.Position = new Vector3(record.Position.x, centre, record.Position.z);
					return;
				}
			}

			/// <summary>How far across the axis an outline reaches either side at a distance along it, metres (the lesser side).</summary>
			private static float HalfChord(float[] outline, float radius, Vector2 axis, Vector2 across, float along)
			{
				float least = float.PositiveInfinity;
				for (int side = -1; side <= 1; side += 2)
				{
					float v = 0f;
					while (v < radius * 2f && PointOfInterestOutline.Contains(outline, radius, axis.x * along + across.x * side * (v + 1f), axis.y * along + across.y * side * (v + 1f)))
					{
						v += 1f;
					}
					least = Mathf.Min(least, v);
				}
				return least;
			}
		}
	}
}
#endif
