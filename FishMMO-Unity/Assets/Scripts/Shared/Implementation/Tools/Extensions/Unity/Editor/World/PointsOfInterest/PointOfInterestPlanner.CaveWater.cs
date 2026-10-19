#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Caves where their water is (2026-10-18: "Caves should be able to appear behind waterfalls and under water"): sited
	/// by the water itself, not by the faces a dry cave is looked for on, which keep off every river, lake and the sea.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Behind a waterfall.</b> On every fall with room for one (<see cref="CaveFallMouth"/>: tall and wide enough, a face at
	/// a shelf's height under its lip, a shelf from there to a dry bank), the site at the mouth itself, facing downstream, its
	/// record's river the fall's: what tells the shaper (CaveShaper) to open it behind the curtain. Exempt from the water rule
	/// only so.
	/// </para>
	/// <para>
	/// <b>Under water.</b> On the bed of a lake or the sea, five to thirteen metres down, at the foot of a face that rises out of
	/// the water to a hill standing well over it (rock for the dry cave past its sump), facing out into the open water; its
	/// biome the land's behind it. The shaper finds the water over the site and dives its mouth.
	/// </para>
	/// <para>
	/// <b>Before the rest.</b> Sited before the kind's dry caves, in what the kind's most leaves past its budget (the dry caves'
	/// budget is unchanged; they keep their spacing from these as from each other), half the kind's spacing from each other.
	/// Sited after, a dry face on the gorge's wall had always taken the fall's ground first. One of each at most.
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestPlanner
	{
		/// <summary>The most of a scene's caves sited behind waterfalls, and under water.</summary>
		public const int MaxFallCaves = 1, MaxDrownedCaves = 1;

		/// <summary>The depths of water a drowned cave's mouth is sited in, metres under the surface.</summary>
		public const float DrownedDepthMin = 5f, DrownedDepthMax = 13f;

		/// <summary>How far over its water the hill behind a drowned cave must stand, metres: a dry cave's height of rock past its sump.</summary>
		public const float DrownedHillRise = 20f;

		internal sealed partial class State
		{
			/// <summary>
			/// Sites up to <paramref name="room"/> caves where their water is: one behind a waterfall, one under water. Returns how
			/// many were sited.
			/// </summary>
			private int PlaceWaterCaves(PointOfInterestKindRule rule, PointOfInterestKindInfo info, int room)
			{
				if (rule.Kind != POIType.Cave || room <= 0 || !Builds(rule.Kind) || Input.Ground == null)
				{
					return 0;
				}
				// Half the kind's spacing from each other: the water decides where these stand.
				PointOfInterestKindRule near = rule.Clone();
				near.SpacingMetres = 0.5f * rule.SpacingMetres;
				int taken = Take(near, info, BehindFalls(rule), Mathf.Min(room, MaxFallCaves));
				taken += Take(near, info, Drowned(rule), Mathf.Min(room - taken, MaxDrownedCaves));
				return taken;
			}

			/// <summary>The best of <paramref name="sites"/> that keep their spacing, up to <paramref name="most"/>, accepted.</summary>
			private int Take(PointOfInterestKindRule rule, PointOfInterestKindInfo info, List<Site> sites, int most)
			{
				sites.Sort((a, b) =>
				{
					int c = b.Score.CompareTo(a.Score);
					if (c != 0) return c;
					c = a.X.CompareTo(b.X);
					return c != 0 ? c : a.Z.CompareTo(b.Z);
				});
				int taken = 0;
				foreach (Site candidate in sites)
				{
					if (taken >= most)
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
				return taken;
			}

			/// <summary>Where the water stands (a river, a lake: not a dry bank), as the cave shaper reads it (CaveShaper.WetIn).</summary>
			private bool Wet(float x, float z)
			{
				WaterKind kind = Input.Water?.KindAt != null ? Input.Water.KindAt(x, z) : WaterKind.None;
				return kind != WaterKind.None && kind != WaterKind.Bank;
			}

			/// <summary>Every fall with room for a cave behind its curtain, sited at its mouth; the taller the fall, the better.</summary>
			private List<Site> BehindFalls(PointOfInterestKindRule rule)
			{
				var sites = new List<Site>();
				if (Input.Water?.Rivers == null)
				{
					return sites;
				}
				foreach (RiverPath river in Input.Water.Rivers)
				{
					foreach (CaveFallMouth mouth in CaveFallMouth.Along(river, Input.Ground, Wet))
					{
						float x = mouth.Mouth.x, z = mouth.Mouth.z;
						if (!Inside(x, z, Input.EdgeMarginMetres + rule.FootprintMin) || Input.Blocked != null && Input.Blocked(x, z))
						{
							continue;
						}
						if (Input.HasSea && mouth.Floor < Input.SeaLevel + CaveSystem.AboveSea + 1f)
						{
							continue;
						}
						int cell = G.CellAt(x, z);
						float weight = BiomeWeight(rule, cell);
						if (weight <= 0f)
						{
							continue;
						}
						Site site = Site.At(x, z, mouth.Floor, weight * (1f + mouth.Drop / 30f) + 0.1f * Unit(Mix(Hash(Input.Seed, (int)rule.Kind, river.Id, mouth.Shelf.Count) ^ 0xFA11u)));
						site.Yaw = mouth.Yaw;
						site.Radius = rule.FootprintMin;
						site.Cell = cell;
						site.RiverId = river.Id;
						site.PlanetRiver = river.PlanetRiver;
						sites.Add(site);
					}
				}
				return sites;
			}

			/// <summary>
			/// Every foot of a face under a lake or the sea, at a diver's depth, where the hill over it stands well out of the
			/// water, sited facing out into the open water.
			/// </summary>
			private List<Site> Drowned(PointOfInterestKindRule rule)
			{
				var sites = new List<Site>();
				bool sea = Input.HasSea && Input.SeaIsWater;
				for (int z = 0; z < G.Depth; z++)
				{
					for (int x = 0; x < G.Width; x++)
					{
						int i = z * G.Width + x;
						float east = G.EastOf(x), north = G.NorthOf(z);
						float surface = float.NegativeInfinity;
						if (G.Sea[i] && sea)
						{
							surface = Input.SeaLevel;
						}
						else if (G.Water[i] == WaterKind.Lake && Input.Water?.SurfaceAt != null)
						{
							surface = Input.Water.SurfaceAt(east, north);
						}
						if (float.IsNegativeInfinity(surface))
						{
							continue;
						}
						float h = G.Height[i];
						if (surface - h < DrownedDepthMin || !Inside(east, north, Input.EdgeMarginMetres + rule.FootprintMin) || G.Blocked[i] || !NearSteep(x, z))
						{
							continue;
						}
						// The face's foot within the cell, up the slope from its middle: the scoring grid's cells are 16 m and a sea
						// cliff's foot is a few metres wide.
						float gx = G.Height[z * G.Width + Mathf.Min(G.Width - 1, x + 1)] - G.Height[z * G.Width + Mathf.Max(0, x - 1)];
						float gz = G.Height[Mathf.Min(G.Depth - 1, z + 1) * G.Width + x] - G.Height[Mathf.Max(0, z - 1) * G.Width + x];
						float gl = Mathf.Sqrt(gx * gx + gz * gz);
						if (gl < 1e-3f)
						{
							continue;
						}
						gx /= gl;
						gz /= gl;
						float face = 0f, yaw = 0f, depth = 0f;
						for (float d = 0f; d <= 16f && face < MinFaceScore; d += 4f)
						{
							float px = east + gx * d, pz = north + gz * d;
							depth = surface - Input.Ground(px, pz);
							if (depth < DrownedDepthMin || depth > DrownedDepthMax)
							{
								continue;
							}
							face = FaceScore(Input.Ground, null, px, pz, 0f, out yaw);
							if (face >= MinFaceScore)
							{
								east = px;
								north = pz;
							}
						}
						if (face < MinFaceScore)
						{
							continue;
						}
						// The hill behind it, out of the water: rock for the dry cave past the sump, and the land it belongs to.
						float back = (yaw + 180f) * Mathf.Deg2Rad;
						float bx = Mathf.Sin(back), bz = Mathf.Cos(back), rise = float.NegativeInfinity;
						for (float t = 24f; t <= 60f; t += 12f)
						{
							rise = Mathf.Max(rise, Input.Ground(east + bx * t, north + bz * t) - surface);
						}
						if (rise < DrownedHillRise)
						{
							continue;
						}
						int land = G.CellAt(east + bx * 32f, north + bz * 32f);
						float weight = BiomeWeight(rule, land);
						if (weight <= 0f)
						{
							continue;
						}
						Site site = Site.At(east, north, surface - depth, weight * face + 0.5f * Unit(Mix(Hash(Input.Seed, (int)rule.Kind, x, z) ^ 0xD1FEu)));
						site.Yaw = yaw;
						site.Radius = rule.FootprintMin;
						site.Cell = i;
						sites.Add(site);
					}
				}
				return sites;
			}
		}
	}
}
#endif
