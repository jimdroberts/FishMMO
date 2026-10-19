#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Editing a generated scene's terrain tiles in scene metres: the pads the planner asks for, and whatever a shaper
	/// cuts or raises. Every edit reads and writes only the samples it changes.
	/// </summary>
	public static class PointOfInterestTerrain
	{
		/// <summary>
		/// Rewrites every heightmap sample within <paramref name="area"/> (scene metres, x/z) to
		/// <paramref name="edit"/>(east, north, metres) and returns how many changed. Heights are clamped to each tile's
		/// span. Samples under a terrain hole are left alone when <paramref name="skipHoles"/> is set.
		/// </summary>
		public static int EditHeights(Terrain[,] terrains, TerrainTilePlan plan, Rect area, Func<float, float, float, float> edit, bool skipHoles = true)
		{
			int changed = 0;
			float halfW = plan.WidthMetres * 0.5f, halfD = plan.DepthMetres * 0.5f;
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					Terrain terrain = terrains[tx, tz];
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					TerrainData data = terrain.terrainData;
					Vector3 origin = terrain.transform.position;
					Vector3 size = data.size;
					var tile = new Rect(origin.x, origin.z, size.x, size.z);
					if (!tile.Overlaps(area))
					{
						continue;
					}
					int res = data.heightmapResolution;
					float step = size.x / (res - 1);
					int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - origin.x) / step), 0, res - 1);
					int x1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - origin.x) / step), 0, res - 1);
					int z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - origin.z) / step), 0, res - 1);
					int z1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - origin.z) / step), 0, res - 1);
					int w = x1 - x0 + 1, d = z1 - z0 + 1;
					if (w <= 0 || d <= 0)
					{
						continue;
					}
					float[,] heights = data.GetHeights(x0, z0, w, d);
					bool[,] solid = null;
					int holesRes = data.holesResolution;
					if (skipHoles)
					{
						solid = data.GetHoles(0, 0, holesRes, holesRes);
					}
					bool any = false;
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							int hx = x0 + i, hz = z0 + j;
							if (solid != null)
							{
								int ux = Mathf.Min(holesRes - 1, hx), uz = Mathf.Min(holesRes - 1, hz);
								if (!solid[uz, ux])
								{
									continue;
								}
							}
							float east = origin.x + hx * step, north = origin.z + hz * step;
							if (!area.Contains(new Vector2(east, north)))
							{
								continue;
							}
							float metres = origin.y + heights[j, i] * size.y;
							float next = edit(east, north, metres);
							if (Mathf.Abs(next - metres) < 1e-4f)
							{
								continue;
							}
							heights[j, i] = Mathf.Clamp01((next - origin.y) / Mathf.Max(1e-3f, size.y));
							any = true;
							changed++;
						}
					}
					if (any)
					{
						data.SetHeights(x0, z0, heights);
					}
				}
			}
			return changed;
		}

		/// <summary>
		/// Flattens a pad: the ground inside its outline (its radius for a disc) eased to its height, or its terraces' levels,
		/// by <paramref name="flatness"/>, banked back to the ground past its edge. Holes are kept. Returns the samples changed.
		/// </summary>
		/// <remarks>
		/// Past the edge everything is measured along the ray from the pad's centre: how far past the outline a sample lies,
		/// the bank traced out along that ray (<see cref="BankReach(Func{float, float, float}, in PointOfInterestPad, Func{float, float, bool})"/>),
		/// and the height the bank starts from, the level the outline's edge stands at there (a terraced pad's bank climbs
		/// with its terraces). A disc is the outline whose reach is its radius every way, and is banked exactly as before.
		/// </remarks>
		/// <param name="wet">Whether a scene position is water or its bank (east, north); a bank never runs into it. Null for none.</param>
		/// <param name="channel">
		/// Whether a scene position is in a river's channel or a lake, for a pad with a <see cref="PointOfInterestPad.Shore"/>:
		/// left as it is even inside the radius, where a river bends back toward the site. Null for none.
		/// </param>
		/// <param name="floor">
		/// The lowest the pad may cut the ground (east, north), metres; negative infinity for no limit. Beside water, its surface
		/// and a little: a bank is never cut under the water it holds. Ground already lower is left as it is.
		/// </param>
		public static int FlattenPad(Terrain[,] terrains, TerrainTilePlan plan, PointOfInterestPad pad, float flatness = 1f,
			Func<float, float, bool> wet = null, Func<float, float, bool> channel = null, Func<float, float, float> floor = null)
		{
			float reach = pad.Radius + Mathf.Max(Mathf.Max(0.01f, pad.Blend), MaxEmbankmentMetres);
			var area = new Rect(pad.Centre.x - reach, pad.Centre.z - reach, reach * 2f, reach * 2f);
			flatness = Mathf.Clamp01(flatness);
			bool shore = pad.Shore.sqrMagnitude > 0.25f;
			// How far the bank runs each way before it meets the ground, traced over the ground as it stands now.
			float[] bank = BankReach((east, north) => GroundAt(terrains, east, north), pad, wet);
			PointOfInterestPad levels = pad;
			return EditHeights(terrains, plan, area, (east, north, metres) =>
			{
				float dx = east - levels.Centre.x, dz = north - levels.Centre.z;
				float distance = Mathf.Sqrt(dx * dx + dz * dz);
				float rim = PointOfInterestOutline.Reach(levels.Outline, levels.Radius, dx, dz);
				float height = EdgeLevel(levels, east, north, dx, dz, distance, rim);
				float target = PadHeight(distance, metres, rim, levels.Blend, height, ReachToward(bank, dx, dz));
				float pull = flatness;
				if (shore)
				{
					if (channel != null && channel(east, north))
					{
						return metres;
					}
					pull *= ShoreWeight(dx * pad.Shore.x + dz * pad.Shore.y, pad.ShoreDistance);
				}
				float levelled = Mathf.Lerp(metres, target, pull);
				if (floor != null && levelled < metres)
				{
					levelled = Mathf.Max(levelled, Mathf.Min(metres, floor(east, north)));
				}
				return levelled;
			});
		}

		/// <summary>How far round a room's clear floor its walls are taken to stand, metres at a building's own scale (a house's 0.45 m and some).</summary>
		public const float RoomWallAllowance = 0.7f;

		/// <summary>How far under a room's floor the ground beneath it is kept, metres.</summary>
		public const float UnderFloorMetres = 0.05f;

		/// <summary>The deepest a building is dug into the ground over its floor, metres; deeper, it stands in a hollow or underground (a cave's chamber) and is left be.</summary>
		public const float MaxDigMetres = 2.5f;

		/// <summary>
		/// Digs a laid building's room out of the hillside (2026-10-15: "House/building interiors aren't always fully
		/// walkable"): the ground under the room and its walls lowered to just under its floor, and out past them banked back
		/// up to the natural ground at <see cref="EmbankmentSlope"/>. A building is seated no higher than its centre's ground
		/// (PropsFeature.SeatHeight), so on a slope the template's pad left part-flat the uphill ground rose through its floor
		/// (a cottage's at about 4° of slope). Only ever lowers, so a site built again digs the same; a room standing over its
		/// ground (a stilt house, an upper storey) changes nothing. Returns the samples lowered.
		/// </summary>
		public static int DigUnder(Terrain[,] terrains, TerrainTilePlan plan, StructureRoom room, Vector3 position, Quaternion rotation, float scale)
		{
			float step = SampleSpacing(terrains);
			if (step <= 0f)
			{
				return 0;
			}
			Vector3 centre = position + rotation * (room.Centre * scale);
			float under = centre.y - UnderFloorMetres;
			float ground = GroundAt(terrains, centre.x, centre.z);
			if (float.IsNaN(ground) || ground - under > MaxDigMetres)
			{
				return 0;
			}
			float wall = RoomWallAllowance * scale;
			Vector2 half = room.Round ? Vector2.one * (room.Size.x * 0.5f * scale + wall) : room.Size * (0.5f * scale) + Vector2.one * wall;
			// The footprint, one heightmap sample past it (so the ground between the last sample in and the first out never
			// rises over the floor inside the walls), and the bank's run back up to the ground.
			float reach = half.magnitude + step + MaxDigMetres / EmbankmentSlope + step;
			var area = new Rect(centre.x - reach, centre.z - reach, reach * 2f, reach * 2f);
			Quaternion back = Quaternion.Inverse(Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
			return EditHeights(terrains, plan, area, (east, north, metres) =>
			{
				if (metres <= under)
				{
					return metres;
				}
				Vector3 local = back * new Vector3(east - centre.x, 0f, north - centre.z);
				float outside = room.Round
					? Mathf.Max(0f, new Vector2(local.x, local.z).magnitude - half.x)
					: new Vector2(Mathf.Max(0f, Mathf.Abs(local.x) - half.x), Mathf.Max(0f, Mathf.Abs(local.z) - half.y)).magnitude;
				return Mathf.Min(metres, under + Mathf.Max(0f, outside - step) * EmbankmentSlope);
			});
		}

		/// <summary>The heightmap's sample spacing, metres (the first tile's); 0 with no terrain.</summary>
		public static float SampleSpacing(Terrain[,] terrains)
		{
			foreach (Terrain terrain in terrains)
			{
				if (terrain != null && terrain.terrainData != null && terrain.terrainData.heightmapResolution > 1)
				{
					return terrain.terrainData.size.x / (terrain.terrainData.heightmapResolution - 1);
				}
			}
			return 0f;
		}

		/// <summary>How far a shore pad hands back to the natural ground before its <see cref="PointOfInterestPad.ShoreDistance"/>, metres.</summary>
		public const float ShoreHandoverMetres = 0.8f;

		/// <summary>
		/// A shore pad's pull at a distance <paramref name="along"/> its way to the water: whole on the landward side, easing
		/// (smoothstep) to none over the <see cref="ShoreHandoverMetres"/> before <paramref name="shoreDistance"/>, none past it.
		/// </summary>
		public static float ShoreWeight(float along, float shoreDistance)
		{
			float t = Mathf.Clamp01((shoreDistance - along) / ShoreHandoverMetres);
			return t * t * (3f - 2f * t);
		}

		/// <summary>How many directions round a pad its bank is traced along (<see cref="BankReach"/>).</summary>
		public const int BankRays = 96;

		/// <summary>Metres past where it meets the ground over which a bank's edit hands back to the natural ground.</summary>
		public const float BankHandoverMetres = 4f;

		/// <summary>How far short of water a pad's bank has handed back to the natural ground, metres.</summary>
		public const float WaterSetbackMetres = 2f;

		/// <summary>
		/// How far out from a pad's centre its bank runs in each of <see cref="BankRays"/> directions (the first along +x,
		/// turning toward +z): from the rim out to where the bank first meets the natural ground, at most the full reach.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A bank stops where it meets the ground.</b> The cut-and-fill cone held every sample within its reach (160 m)
		/// to within the bank's slope of the pad, wherever it lay. A bank is earth piled against the pad's edge (or dug
		/// back from it) until it meets the hillside; past that the ground is the ground. Held to the cone everywhere, a
		/// pad at a clifftop filled whatever lay below the cliff within the reach to a 31° slope: the shrine above Flo
		/// Monolith's 70 m fall, 96 m from its gorge, filled the gorge, its plunge pool and a hundred metres of the river
		/// below it 36 m deep, and the fall landed on a grassy hillside with no pool and no river out of it (2026-10-13;
		/// found by the cut's river-burial check, which named the paint stage the pads are laid in).
		/// </para>
		/// <para>
		/// So the bank is traced out along each direction over the ground as it stands, and ends at the first point where
		/// the ground lies within the bank's reach of the pad's height. A pad on a hillside banks out as far as it needs
		/// (the ground stays below the fill, or above the cut, all the way); a pad at a clifftop meets the ground at its rim
		/// and leaves the cliff, and the valley past it, alone.
		/// </para>
		/// </remarks>
		/// <param name="groundAt">The natural ground (east, north), metres; NaN off the terrain, where the bank ends.</param>
		/// <param name="wet">
		/// Whether a scene position is water or its bank. A bank ends before it: handed back to the natural ground over
		/// <see cref="BankHandoverMetres"/> and <see cref="WaterSetbackMetres"/> more, short of the water. A town's pad 66 m
		/// from Flo Monolith's river, above it on ground falling gentler than the bank's slope, banked on down the hillside
		/// and over the river, filling its channel above its water (2026-10-13). Null for none.
		/// </param>
		public static float[] BankReach(Func<float, float, float> groundAt, Vector3 centre, float radius, float blend, float height,
			Func<float, float, bool> wet = null)
			=> BankReach(groundAt, centre, blend, (cos, sin) => (radius, height), wet);

		/// <summary>
		/// <see cref="BankReach(Func{float, float, float}, Vector3, float, float, float, Func{float, float, bool})"/> for a pad
		/// as it is drawn: each ray's bank starts at the outline's edge that way, from the level the pad stands at there.
		/// </summary>
		public static float[] BankReach(Func<float, float, float> groundAt, in PointOfInterestPad pad, Func<float, float, bool> wet = null)
		{
			PointOfInterestPad levels = pad;
			return BankReach(groundAt, pad.Centre, pad.Blend, (cos, sin) =>
			{
				float rim = PointOfInterestOutline.Reach(levels.Outline, levels.Radius, cos, sin);
				return (rim, EdgeLevel(levels, levels.Centre.x + cos * rim, levels.Centre.z + sin * rim, cos * rim, sin * rim, rim, rim));
			}, wet);
		}

		/// <summary>
		/// The level a pad's ground is eased to at a scene position (east, north) a <paramref name="distance"/> from its centre
		/// (offset dx, dz), its edge <paramref name="rim"/> out that way: its height, or its terraces' level there, and past
		/// the edge the level the edge stands at on the same ray.
		/// </summary>
		private static float EdgeLevel(in PointOfInterestPad pad, float east, float north, float dx, float dz, float distance, float rim)
		{
			if (!PointOfInterestTerraces.Terraced(pad))
			{
				return pad.Height;
			}
			if (distance <= rim || distance < 1e-3f)
			{
				return PointOfInterestTerraces.LevelAt(pad, east, north);
			}
			float k = rim / distance;
			return PointOfInterestTerraces.LevelAt(pad, pad.Centre.x + dx * k, pad.Centre.z + dz * k);
		}

		/// <summary>The rays' bank, each ray starting at its own rim and height (<paramref name="edge"/>(cos, sin)).</summary>
		private static float[] BankReach(Func<float, float, float> groundAt, Vector3 centre, float blend, Func<float, float, (float rim, float height)> edge,
			Func<float, float, bool> wet)
		{
			var reach = new float[BankRays];
			const float Step = 1f;
			for (int r = 0; r < BankRays; r++)
			{
				float angle = r * (2f * Mathf.PI / BankRays);
				float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
				(float radius, float height) = edge(cos, sin);
				float most = radius + MaxEmbankmentMetres;
				float found = most;
				for (float d = radius; d <= most; d += Step)
				{
					float east = centre.x + cos * d, north = centre.z + sin * d;
					if (wet != null && wet(east, north))
					{
						// The edit is the natural ground again before the water's bank begins.
						found = Mathf.Max(radius, d - BankHandoverMetres - WaterSetbackMetres);
						break;
					}
					float ground = groundAt(east, north);
					if (float.IsNaN(ground) || Mathf.Abs(ground - height) <= BankSlack(d - radius, blend) + 1e-3f)
					{
						found = d;
						break;
					}
				}
				reach[r] = found;
			}
			return reach;
		}

		/// <summary>How far the bank runs toward an offset (dx, dz) from the pad's centre: <see cref="BankReach"/> between its two nearest rays.</summary>
		public static float ReachToward(float[] reach, float dx, float dz)
		{
			if (reach == null || reach.Length == 0)
			{
				return float.PositiveInfinity;
			}
			float angle = Mathf.Atan2(dz, dx);
			if (angle < 0f)
			{
				angle += 2f * Mathf.PI;
			}
			float at = angle / (2f * Mathf.PI) * reach.Length;
			int a = Mathf.FloorToInt(at) % reach.Length, b = (a + 1) % reach.Length;
			return Mathf.Lerp(reach[a], reach[b], at - Mathf.Floor(at));
		}

		/// <summary>
		/// The ground at a distance from a pad's centre, as <see cref="PadHeight(float, float, float, float, float)"/>, but only
		/// as far as its bank runs that way (<paramref name="reach"/>, from <see cref="BankReach"/>): past it the natural
		/// ground, handed back over <see cref="BankHandoverMetres"/> (where the bank meets the ground it already is the ground).
		/// </summary>
		public static float PadHeight(float distance, float ground, float radius, float blend, float height, float reach)
		{
			float banked = PadHeight(distance, ground, radius, blend, height);
			if (distance <= reach || distance <= radius)
			{
				return banked;
			}
			float t = Mathf.Clamp01((distance - reach) / BankHandoverMetres);
			return Mathf.Lerp(banked, ground, t * t * (3f - 2f * t));
		}

		/// <summary>How far a bank may stand from the pad's height a run past its rim: the slope, after the rim's rounded fillet.</summary>
		private static float BankSlack(float run, float blend)
		{
			float b = Mathf.Max(0.01f, blend);
			run = Mathf.Max(0f, run);
			return (run < b ? run * run / (2f * b) : run - b * 0.5f) * EmbankmentSlope;
		}

		/// <summary>The terrain's height at a scene position (the tile under it); NaN off every tile.</summary>
		private static float GroundAt(Terrain[,] terrains, float east, float north)
		{
			foreach (Terrain terrain in terrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = terrain.terrainData.size;
				if (east >= origin.x && east <= origin.x + size.x && north >= origin.z && north <= origin.z + size.z)
				{
					return terrain.SampleHeight(new Vector3(east, 0f, north)) + origin.y;
				}
			}
			return float.NaN;
		}

		/// <summary>The steepest cut or fill slope a pad leaves at its edge (tan 31°): steep enough to stay close, shallow enough to read as ground.</summary>
		public const float EmbankmentSlope = 0.6f;

		/// <summary>
		/// How far past its radius a pad may reshape the ground, metres. The last quarter fades back to the natural ground, so
		/// the edit never ends in a step even where the bank has not met a steep hillside by then.
		/// </summary>
		public const float MaxEmbankmentMetres = 160f;

		/// <summary>
		/// The ground at a distance from a pad's centre: the pad's height inside its radius; past it the natural ground held to
		/// within a cut-and-fill cone that rises or falls at <see cref="EmbankmentSlope"/> from the pad's edge, with the first
		/// <paramref name="blend"/> metres rounded so the edge is not a kink.
		/// </summary>
		/// <remarks>
		/// A pad used to ease into the ground over a fixed 8–15 m, so a big pad on a slope stood in a circular cliff or sat in a
		/// circular trench as tall as the slope's fall across it (the rings around towns on the Flo Monolith probe map,
		/// 2026-10-10). The cone meets the ground wherever the ground is within its slope, so a pad on gentle ground changes
		/// nothing past its rim and one on a steep hillside gets a bank as wide as it needs.
		/// </remarks>
		public static float PadHeight(float distance, float ground, float radius, float blend, float height)
		{
			if (distance <= radius)
			{
				return height;
			}
			/* The rim is a parabolic fillet: the bank's slope ramps from level at the rim to EmbankmentSlope over the blend,
			 * then holds, so the edge is rounded and nowhere steeper than the bank. */
			float out_ = distance - radius;
			float b = Mathf.Max(0.01f, blend);
			float run = out_ < b ? out_ * out_ / (2f * b) : out_ - b * 0.5f;
			float slack = run * EmbankmentSlope;
			float banked = Mathf.Clamp(ground, height - slack, height + slack);
			// And hand back to the natural ground over the reach's last quarter: the edit stops there, so it must end on it.
			float fadeStart = radius + MaxEmbankmentMetres * 0.75f;
			if (distance > fadeStart)
			{
				float t = Mathf.Clamp01((distance - fadeStart) / (MaxEmbankmentMetres * 0.25f));
				banked = Mathf.Lerp(banked, ground, t * t * (3f - 2f * t));
			}
			return banked;
		}

		/// <summary>A pad's pull at a distance from its centre: 1 inside its radius, easing (smoothstep) to 0 over its blend.</summary>
		public static float PadWeight(float distance, float radius, float blend)
		{
			if (distance <= radius)
			{
				return 1f;
			}
			if (blend <= 0f || distance >= radius + blend)
			{
				return 0f;
			}
			float t = 1f - (distance - radius) / blend;
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// Cuts holes where <paramref name="hole"/>(east, north) is true within <paramref name="area"/> (a carved cave's
		/// mouth). Returns the hole texels cut.
		/// </summary>
		public static int CutHoles(Terrain[,] terrains, TerrainTilePlan plan, Rect area, Func<float, float, bool> hole)
			=> SetHoles(terrains, plan, area, hole, false);

		/// <summary>
		/// Fills the holes where <paramref name="fill"/>(east, north) is true within <paramref name="area"/> (a cave's mouth a
		/// new plan no longer opens). Returns the hole texels filled.
		/// </summary>
		public static int FillHoles(Terrain[,] terrains, TerrainTilePlan plan, Rect area, Func<float, float, bool> fill)
			=> SetHoles(terrains, plan, area, fill, true);

		/// <summary>The centres (east, north) of the terrain's hole texels within <paramref name="area"/>: what was actually cut.</summary>
		public static List<Vector2> HolesIn(Terrain[,] terrains, TerrainTilePlan plan, Rect area)
		{
			var holes = new List<Vector2>();
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					Terrain terrain = terrains[tx, tz];
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					TerrainData data = terrain.terrainData;
					Vector3 origin = terrain.transform.position;
					if (!new Rect(origin.x, origin.z, data.size.x, data.size.z).Overlaps(area))
					{
						continue;
					}
					int res = data.holesResolution;
					float step = data.size.x / res;
					int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - origin.x) / step), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - origin.x) / step), 0, res - 1);
					int z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - origin.z) / step), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - origin.z) / step), 0, res - 1);
					bool[,] solid = data.GetHoles(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
					for (int z = 0; z <= z1 - z0; z++)
					{
						for (int x = 0; x <= x1 - x0; x++)
						{
							if (!solid[z, x])
							{
								holes.Add(new Vector2(origin.x + (x0 + x + 0.5f) * step, origin.z + (z0 + z + 0.5f) * step));
							}
						}
					}
				}
			}
			return holes;
		}

		private static int SetHoles(Terrain[,] terrains, TerrainTilePlan plan, Rect area, Func<float, float, bool> where, bool solidity)
		{
			int cut = 0;
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					Terrain terrain = terrains[tx, tz];
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					TerrainData data = terrain.terrainData;
					Vector3 origin = terrain.transform.position;
					var tile = new Rect(origin.x, origin.z, data.size.x, data.size.z);
					if (!tile.Overlaps(area))
					{
						continue;
					}
					int res = data.holesResolution;
					float step = data.size.x / res;
					bool[,] solid = data.GetHoles(0, 0, res, res);
					bool any = false;
					for (int z = 0; z < res; z++)
					{
						float north = origin.z + (z + 0.5f) * step;
						if (north < area.yMin || north > area.yMax)
						{
							continue;
						}
						for (int x = 0; x < res; x++)
						{
							float east = origin.x + (x + 0.5f) * step;
							if (east < area.xMin || east > area.xMax || solid[z, x] == solidity || !where(east, north))
							{
								continue;
							}
							solid[z, x] = solidity;
							any = true;
							cut++;
						}
					}
					if (any)
					{
						data.SetHoles(0, 0, solid);
						if (!solidity)
						{
							ClearDetailsInHoles(data, solid, new Rect(area.xMin - origin.x, area.yMin - origin.z, area.width, area.height));
						}
					}
				}
			}
			return cut;
		}

		/// <summary>
		/// Empties every detail layer over the hole cells of <paramref name="solid"/> (true solid, <see cref="TerrainData.GetHoles"/>)
		/// within <paramref name="local"/> (metres from the tile's corner): a hole cut after the biome scatter left its
		/// grass and flowers standing in the cave mouth. Returns the detail cells emptied.
		/// </summary>
		public static int ClearDetailsInHoles(TerrainData data, bool[,] solid, Rect local)
		{
			int layers = data.detailPrototypes.Length, res = data.detailResolution, holes = solid.GetLength(0);
			if (layers == 0 || res <= 0 || holes <= 0)
			{
				return 0;
			}
			float texelX = data.size.x / res, texelZ = data.size.z / res;
			int x0 = Mathf.Clamp(Mathf.FloorToInt(local.xMin / texelX), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(local.xMax / texelX), 0, res - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt(local.yMin / texelZ), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt(local.yMax / texelZ), 0, res - 1);
			if (x1 < x0 || z1 < z0)
			{
				return 0;
			}
			int w = x1 - x0 + 1, h = z1 - z0 + 1;
			// The detail cells whose centre lies over a hole cell (the cell a plant's root reads, as the terrain clips it).
			var under = new bool[h, w];
			bool anyUnder = false;
			for (int j = 0; j < h; j++)
			{
				int hz = Mathf.Clamp(Mathf.FloorToInt((z0 + j + 0.5f) / res * holes), 0, holes - 1);
				for (int i = 0; i < w; i++)
				{
					int hx = Mathf.Clamp(Mathf.FloorToInt((x0 + i + 0.5f) / res * holes), 0, holes - 1);
					if (!solid[hz, hx])
					{
						under[j, i] = true;
						anyUnder = true;
					}
				}
			}
			if (!anyUnder)
			{
				return 0;
			}
			int cleared = 0;
			for (int layer = 0; layer < layers; layer++)
			{
				int[,] map = data.GetDetailLayer(x0, z0, w, h, layer);
				bool changed = false;
				for (int j = 0; j < h; j++)
				{
					for (int i = 0; i < w; i++)
					{
						if (under[j, i] && map[j, i] != 0)
						{
							map[j, i] = 0;
							changed = true;
							cleared++;
						}
					}
				}
				if (changed)
				{
					data.SetDetailLayer(x0, z0, layer, map);
				}
			}
			return cleared;
		}

		/// <summary>True where the tiles have a hole at (east, north).</summary>
		/// <summary>
		/// <see cref="HoleQuery(Terrain[,], TerrainTilePlan)"/> widened by <paramref name="margin"/> metres: a canyon wall stands
		/// in its hole but its bed juts past it, so a site or a way kept only off the hole could still be under the wall.
		/// </summary>
		public static Func<float, float, bool> HoleQuery(Terrain[,] terrains, TerrainTilePlan plan, float margin)
		{
			Func<float, float, bool> hole = HoleQuery(terrains, plan);
			if (hole == null || margin <= 0f)
			{
				return hole;
			}
			return (east, north) =>
			{
				if (hole(east, north))
				{
					return true;
				}
				for (int ring = 1; ring <= 2; ring++)
				{
					float r = margin * ring / 2f;
					for (int k = 0; k < 8; k++)
					{
						float a = k * (Mathf.PI / 4f);
						if (hole(east + r * Mathf.Cos(a), north + r * Mathf.Sin(a)))
						{
							return true;
						}
					}
				}
				return false;
			};
		}

		public static Func<float, float, bool> HoleQuery(Terrain[,] terrains, TerrainTilePlan plan)
		{
			int countX = plan.CountX, countZ = plan.CountZ;
			var maps = new bool[countX, countZ][,];
			var any = false;
			for (int tz = 0; tz < countZ; tz++)
			{
				for (int tx = 0; tx < countX; tx++)
				{
					TerrainData data = terrains[tx, tz] != null ? terrains[tx, tz].terrainData : null;
					if (data == null)
					{
						continue;
					}
					int res = data.holesResolution;
					bool[,] solid = data.GetHoles(0, 0, res, res);
					bool holed = false;
					foreach (bool s in solid)
					{
						if (!s)
						{
							holed = true;
							break;
						}
					}
					if (holed)
					{
						maps[tx, tz] = solid;
						any = true;
					}
				}
			}
			if (!any)
			{
				return null;
			}
			return (east, north) =>
			{
				int tx = Mathf.Clamp(Mathf.FloorToInt((east + plan.WidthMetres * 0.5f) / plan.TileMetres), 0, countX - 1);
				int tz = Mathf.Clamp(Mathf.FloorToInt((north + plan.DepthMetres * 0.5f) / plan.TileMetres), 0, countZ - 1);
				bool[,] solid = maps[tx, tz];
				if (solid == null)
				{
					return false;
				}
				int res = solid.GetLength(0);
				float u = (east + plan.WidthMetres * 0.5f - tx * plan.TileMetres) / plan.TileMetres;
				float v = (north + plan.DepthMetres * 0.5f - tz * plan.TileMetres) / plan.TileMetres;
				int x = Mathf.Clamp((int)(u * res), 0, res - 1), z = Mathf.Clamp((int)(v * res), 0, res - 1);
				return !solid[z, x];
			};
		}
	}
}
#endif
