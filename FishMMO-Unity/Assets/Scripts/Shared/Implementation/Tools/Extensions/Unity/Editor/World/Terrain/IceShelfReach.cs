#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What part of the open ice shelf a point of the sea is (<see cref="IceShelfReach"/>).</summary>
	public enum IceShelfPart : byte
	{
		/// <summary>Not the shelf: land, open sea, or sea too warm or too far from a calving coast.</summary>
		None = 0,
		/// <summary>The floating shelf, from the grounding line out to its calving front.</summary>
		Shelf = 1,
		/// <summary>The fast ice before the front, out to the floe edge.</summary>
		Apron = 2,
	}

	/// <summary>
	/// Where the ice sheet meets a cold sea, the ice it runs out over that sea: the floating SHELF to a sheer calving front,
	/// then the FAST ICE to a ragged floe edge (biome suite decision 68, phase I2s). Laid as ground, as the ice sheet's own
	/// ice is (<see cref="IceSheetSurface"/>), so it has collision, footing, painting and cliffs for nothing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> A calving coast is low land (at most <see cref="SourceGroundMetres"/> under its ice) under the full ice
	/// sheet (at least <see cref="SourceIceMetres"/> of ice) beside sea whose annual mean sea-level air is at or below
	/// <see cref="IceOccurrence.IceShelfLimitC"/> (−5 °C, the thermal limit of ice-shelf viability, Morris &amp; Vaughan
	/// 2003). The shelf reaches out from it across sea that is itself that
	/// cold, so it never runs into warmer water than can hold it: the reach is a distance over the cold sea only, from the
	/// nearest calving coast, worked out once on a coarse grid (<see cref="CellMetres"/>) and read bilinearly.
	/// </para>
	/// <para>
	/// <b>How.</b> Out to a reach of <see cref="MinReachMetres"/>–<see cref="MaxReachMetres"/> the surface stands at a freeboard thinning seaward from
	/// <see cref="MaxFreeboardMetres"/> at the grounding line to <see cref="MinFreeboardMetres"/>–20 m at the front (about a
	/// ninth of the ice's thickness, as floating ice stands), meeting the sheet itself at the grounding line through a short
	/// hinge. At the reach it stops: the next sample is the fast ice at <see cref="ApronFreeboardMetres"/>, so the front is a
	/// vertical cliff. The fast ice runs on <see cref="MinApronMetres"/>–<see cref="MaxApronMetres"/> to the floe edge, and
	/// beyond that the sea is open: the pack and the bergs the ice placer floats start there.
	/// </para>
	/// <para>
	/// <b>Read at the coast, frayed at the point.</b> The reach, the fast ice's width and the front's freeboard are broad
	/// noises read at the calving coast a point was reached from (its place is carried with the distance), so they vary
	/// ALONG the coast — embayments and ice tongues — and stay constant out along each line from it, and the front and the
	/// floe edge are crossed once on the way out. Only a small ragged noise is read at the point itself, gentle enough (its
	/// slope well under one) to fray the line without folding it into islands and holes.
	/// </para>
	/// <para>
	/// <b>Only where opted in.</b> Nothing is built unless a biome holds the <see cref="BiomeWaterRole.IceShelf"/> water role
	/// (no name fallback stands in for it), the world's surface liquid is water, its seas are not frozen through (that sea is
	/// the frozen face, <see cref="SceneGeneration.IceShelfMetres(uint, float, float)"/>) and it can carry an ice sheet. A
	/// scene with no calving coast near it gets null, and its ground is exactly what it was.
	/// </para>
	/// <para>
	/// Every value is a pure function of the scene's request (the planet, its climate, the body's terrain seed and the
	/// scene's position): a cut, a re-cut and a repaint lay and find the same shelf, so nothing is stored.
	/// </para>
	/// </remarks>
	public sealed class IceShelfReach
	{
		/// <summary>Spacing of the reach grid, metres.</summary>
		public const float CellMetres = 128f;

		/// <summary>The least ice on a coast for it to calve a shelf, scene metres: half the sheet's full thickness.</summary>
		public const float SourceIceMetres = IceSheetSurface.MaxThicknessMetres * 0.5f;

		/// <summary>
		/// The highest the land under a calving coast's ice may stand, scene metres: a shelf is fed where the sheet comes down
		/// to the sea, not off an ice-capped sea cliff (which keeps its tidewater cliff).
		/// </summary>
		public const float SourceGroundMetres = 30f;

		/// <summary>The nearest a calving front stands to its grounding line, metres.</summary>
		public const float MinReachMetres = 2000f;

		/// <summary>The furthest a calving front stands from its grounding line, metres.</summary>
		public const float MaxReachMetres = 8000f;

		/// <summary>The narrowest the fast ice before the front runs, metres.</summary>
		public const float MinApronMetres = 500f;

		/// <summary>The widest the fast ice before the front runs, metres.</summary>
		public const float MaxApronMetres = 2000f;

		/// <summary>The shelf's freeboard at its grounding line, scene metres (the hinge rises on to the sheet above it).</summary>
		public const float MaxFreeboardMetres = 35f;

		/// <summary>The least freeboard the shelf stands at, at its front, scene metres.</summary>
		public const float MinFreeboardMetres = 12f;

		/// <summary>The most a front's own freeboard rises above <see cref="MinFreeboardMetres"/>, scene metres.</summary>
		public const float FrontRiseMetres = 8f;

		/// <summary>Where the fast ice stands above the sea, scene metres.</summary>
		public const float ApronFreeboardMetres = 0.6f;

		/// <summary>Metres over which the shelf falls from the sheet's surface to its own freeboard: the grounding-zone hinge.</summary>
		public const float HingeMetres = 300f;

		/// <summary>How much the shelf's surface swells rise and fall, scene metres.</summary>
		public const float SwellMetres = 0.8f;

		/// <summary>Metres between the shelf's surface swells, roughly (the sheet's own spacing).</summary>
		public const float SwellSpacingMetres = IceSheetSurface.SwellSpacingMetres;

		/// <summary>Distance a cell is given when no calving coast reaches it.</summary>
		private const float Unreached = float.PositiveInfinity;

		/// <summary>Grid columns, along scene X.</summary>
		public readonly int Width;
		/// <summary>Grid rows, along scene Z.</summary>
		public readonly int Height;
		/// <summary>Scene X/Z of the grid's south-west corner.</summary>
		public readonly Vector2 Origin;
		/// <summary>Metres per cell.</summary>
		public readonly float Cell;
		/// <summary>The terrain seed every profile noise is read with.</summary>
		public readonly uint Seed;
		/// <summary>Calving-coast cells the reach was measured from.</summary>
		public readonly int Sources;

		private readonly float[] distance;
		private readonly float[] grounding;
		private readonly float[] coastEast;
		private readonly float[] coastNorth;

		private IceShelfReach(int width, int height, float cell, Vector2 origin, uint seed, float[] distance, float[] grounding,
			float[] coastEast, float[] coastNorth, int sources)
		{
			this.coastEast = coastEast;
			this.coastNorth = coastNorth;
			Width = width;
			Height = height;
			Cell = cell;
			Origin = origin;
			Seed = seed;
			this.distance = distance;
			this.grounding = grounding;
			Sources = sources;
		}

		// ── Building ──────────────────────────────────────────────────

		/// <summary>
		/// The reach over a grid of the ground before any shelf: null when no calving coast stands beside the cold sea.
		/// </summary>
		/// <param name="width">Columns.</param>
		/// <param name="height">Rows.</param>
		/// <param name="cell">Metres per cell.</param>
		/// <param name="origin">Scene X/Z of the grid's south-west corner (cell centres at half a cell in from it).</param>
		/// <param name="ground">Scene metres at each cell centre, row by row from the south, ice sheet included; at or below 0 is sea.</param>
		/// <param name="ice">The ice sheet's thickness there, scene metres (0 off the sheet and at sea).</param>
		/// <param name="coldSea">True where the cell is sea whose annual mean is at or below the ice-shelf limit.</param>
		/// <param name="seed">The body's terrain seed, for the profile noises.</param>
		public static IceShelfReach Build(int width, int height, float cell, Vector2 origin, float[] ground, float[] ice, bool[] coldSea, uint seed)
		{
			int count = width * height;
			if (width <= 0 || height <= 0 || ground == null || ice == null || coldSea == null
				|| ground.Length != count || ice.Length != count || coldSea.Length != count)
			{
				throw new ArgumentException("One ground height, ice thickness and cold-sea flag per cell are required.");
			}
			var dist = new float[count];
			var surface = new float[count];
			var fromEast = new float[count];
			var fromNorth = new float[count];
			var passable = new bool[count];
			int sources = 0;
			for (int i = 0; i < count; i++)
			{
				dist[i] = Unreached;
				passable[i] = coldSea[i] && ground[i] <= 0f;
			}
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if (ground[i] <= 0f || ice[i] < SourceIceMetres || ground[i] - ice[i] > SourceGroundMetres || !Beside(passable, width, height, x, z))
					{
						continue;
					}
					dist[i] = 0f;
					surface[i] = ground[i];
					fromEast[i] = origin.x + (x + 0.5f) * cell;
					fromNorth[i] = origin.y + (z + 0.5f) * cell;
					sources++;
				}
			}
			if (sources == 0)
			{
				return null;
			}

			/* The distance over the cold sea from the nearest calving coast: raster sweeps forward and back (a chamfer of
			 * one cell and the diagonal), repeated until nothing shortens, since the land between bays means a straight
			 * two-pass sweep would not see round a headland. Each cell carries the surface and the place of the coast it was
			 * reached from. */
			float straight = cell, diagonal = cell * 1.41421356f;
			float limit = MaxReachMetres + MaxApronMetres + cell;
			for (int pass = 0; pass < 64; pass++)
			{
				bool changed = false;
				for (int z = 0; z < height; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int i = z * width + x;
						if (!passable[i])
						{
							continue;
						}
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x - 1, z, straight, limit);
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x - 1, z - 1, diagonal, limit);
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x, z - 1, straight, limit);
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x + 1, z - 1, diagonal, limit);
					}
				}
				for (int z = height - 1; z >= 0; z--)
				{
					for (int x = width - 1; x >= 0; x--)
					{
						int i = z * width + x;
						if (!passable[i])
						{
							continue;
						}
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x + 1, z, straight, limit);
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x + 1, z + 1, diagonal, limit);
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x, z + 1, straight, limit);
						changed |= Relax(dist, surface, fromEast, fromNorth, width, height, i, x - 1, z + 1, diagonal, limit);
					}
				}
				if (!changed)
				{
					break;
				}
			}
			return new IceShelfReach(width, height, cell, origin, seed, dist, surface, fromEast, fromNorth, sources);
		}

		private static bool Beside(bool[] passable, int width, int height, int x, int z)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int nx = x + dx, nz = z + dz;
					if ((dx != 0 || dz != 0) && nx >= 0 && nz >= 0 && nx < width && nz < height && passable[nz * width + nx])
					{
						return true;
					}
				}
			}
			return false;
		}

		private static bool Relax(float[] dist, float[] surface, float[] fromEast, float[] fromNorth, int width, int height, int i, int nx, int nz,
			float step, float limit)
		{
			if (nx < 0 || nz < 0 || nx >= width || nz >= height)
			{
				return false;
			}
			int n = nz * width + nx;
			float through = dist[n] + step;
			if (through >= dist[i] || through > limit)
			{
				return false;
			}
			dist[i] = through;
			surface[i] = surface[n];
			fromEast[i] = fromEast[n];
			fromNorth[i] = fromNorth[n];
			return true;
		}

		// ── Reading ───────────────────────────────────────────────────

		/// <summary>
		/// The distance from the nearest calving coast over the cold sea at a point, metres, and the surface of that coast's
		/// ice: false where no calving coast reaches (or off the grid).
		/// </summary>
		public bool Sample(float east, float north, out float metres, out float coastSurface)
		{
			return Sample(east, north, out metres, out coastSurface, out _);
		}

		/// <summary>The same, with the place (scene east, north) of the calving coast the point was reached from.</summary>
		public bool Sample(float east, float north, out float metres, out float coastSurface, out Vector2 coast)
		{
			float gx = (east - Origin.x) / Cell - 0.5f;
			float gz = (north - Origin.y) / Cell - 0.5f;
			int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
			float fx = gx - x0, fz = gz - z0;
			float weights = 0f, d = 0f, s = 0f;
			Vector2 at = Vector2.zero;
			Add(x0, z0, (1f - fx) * (1f - fz), ref weights, ref d, ref s, ref at);
			Add(x0 + 1, z0, fx * (1f - fz), ref weights, ref d, ref s, ref at);
			Add(x0, z0 + 1, (1f - fx) * fz, ref weights, ref d, ref s, ref at);
			Add(x0 + 1, z0 + 1, fx * fz, ref weights, ref d, ref s, ref at);
			if (weights <= 1e-6f)
			{
				metres = Unreached;
				coastSurface = 0f;
				coast = Vector2.zero;
				return false;
			}
			metres = d / weights;
			coastSurface = s / weights;
			coast = at / weights;
			return true;
		}

		private void Add(int x, int z, float weight, ref float weights, ref float d, ref float s, ref Vector2 at)
		{
			if (x < 0 || z < 0 || x >= Width || z >= Height || weight <= 0f)
			{
				return;
			}
			int i = z * Width + x;
			if (float.IsPositiveInfinity(distance[i]))
			{
				return;
			}
			weights += weight;
			d += distance[i] * weight;
			s += grounding[i] * weight;
			at += new Vector2(coastEast[i], coastNorth[i]) * weight;
		}

		/// <summary>True where a calving coast is near enough for a point to be shelf or fast ice: the cheap test before the ground is asked.</summary>
		public bool MayReach(float east, float north)
		{
			return Sample(east, north, out float metres, out _) && metres <= MaxReachMetres + MaxApronMetres;
		}

		/// <summary>What part of the shelf a point is, given the ground there before any shelf (at or below 0 is sea).</summary>
		public IceShelfPart PartAt(float east, float north, float groundBeforeShelf)
		{
			return PartAt(east, north, groundBeforeShelf, out _, out _, out _);
		}

		private IceShelfPart PartAt(float east, float north, float groundBeforeShelf, out float metres, out float coastSurface, out Vector2 coast)
		{
			metres = Unreached;
			coastSurface = 0f;
			coast = Vector2.zero;
			if (groundBeforeShelf > 0f || !Sample(east, north, out metres, out coastSurface, out coast))
			{
				return IceShelfPart.None;
			}
			return Part(metres, FrontMetres(Seed, coast, east, north), FastIceMetres(Seed, coast, east, north));
		}

		/// <summary>
		/// The ground at a point with the shelf laid: the shelf's surface or the fast ice where the point is either, else the
		/// ground before it, unchanged.
		/// </summary>
		public float SurfaceAt(float east, float north, float groundBeforeShelf)
		{
			switch (PartAt(east, north, groundBeforeShelf, out float metres, out float coastSurface, out Vector2 coast))
			{
				case IceShelfPart.Shelf:
					return ShelfSurfaceMetres(Seed, coast, east, north, metres, coastSurface);
				case IceShelfPart.Apron:
					return ApronFreeboardMetres;
				default:
					return groundBeforeShelf;
			}
		}

		// ── The profile (pure) ────────────────────────────────────────

		/// <summary>The part a point is at a distance from its calving coast, for a front at <paramref name="reach"/> and fast ice <paramref name="apron"/> wide.</summary>
		public static IceShelfPart Part(float metres, float reach, float apron)
		{
			if (metres <= reach)
			{
				return IceShelfPart.Shelf;
			}
			return metres <= reach + apron ? IceShelfPart.Apron : IceShelfPart.None;
		}

		/// <summary>How far the calving front stands out from a calving coast, read at that coast, metres: embayments and ice tongues along it.</summary>
		public static float ReachMetres(uint seed, float coastEast, float coastNorth)
		{
			float broad = Stretch(Fbm(seed ^ 0x5A17E1u, coastEast / 9000f, coastNorth / 9000f));
			return MinReachMetres + (MaxReachMetres - MinReachMetres) * broad;
		}

		/// <summary>How far the fast ice runs on past the front, read at the calving coast, metres.</summary>
		public static float ApronMetres(uint seed, float coastEast, float coastNorth)
		{
			float broad = Stretch(Fbm(seed ^ 0x5E11A9u, coastEast / 5000f, coastNorth / 5000f));
			return MinApronMetres + (MaxApronMetres - MinApronMetres) * broad;
		}

		/// <summary>The freeboard of the front, read at the calving coast, scene metres: <see cref="MinFreeboardMetres"/> to that plus <see cref="FrontRiseMetres"/>.</summary>
		public static float FrontFreeboardMetres(uint seed, float coastEast, float coastNorth)
		{
			return MinFreeboardMetres + FrontRiseMetres * ValueNoise(seed ^ 0x19D3A7u, coastEast / 2400f, coastNorth / 2400f);
		}

		/// <summary>
		/// The front's distance from its coast at a point, metres: the coast's <see cref="ReachMetres"/> frayed by a ragged line
		/// whose slope stays near a half, so the front is crossed once going out.
		/// </summary>
		public static float FrontMetres(uint seed, Vector2 coast, float east, float north)
		{
			float ragged = ValueNoise(seed ^ 0x3C6EF3u, east / 160f, north / 160f) * 2f - 1f;
			float fine = ValueNoise(seed ^ 0x77B1C5u, east / 45f, north / 45f) * 2f - 1f;
			return Mathf.Clamp(ReachMetres(seed, coast.x, coast.y) + ragged * 15f + fine * 4f, MinReachMetres, MaxReachMetres);
		}

		/// <summary>The fast ice's width past the front at a point, metres: the coast's <see cref="ApronMetres"/> with a ragged floe edge.</summary>
		public static float FastIceMetres(uint seed, Vector2 coast, float east, float north)
		{
			float ragged = ValueNoise(seed ^ 0x1F0E5Du, east / 90f, north / 90f) * 2f - 1f;
			return Mathf.Clamp(ApronMetres(seed, coast.x, coast.y) + ragged * 10f, MinApronMetres, MaxApronMetres);
		}

		/// <summary>
		/// The shelf's freeboard at a distance from its grounding line, scene metres, before its swells: from the coast's own
		/// ice surface through the hinge to at most <see cref="MaxFreeboardMetres"/>, thinning seaward (fastest near the
		/// grounding line, as a shelf spreads and thins) to the front's freeboard at the reach.
		/// </summary>
		/// <param name="metres">Distance from the calving coast.</param>
		/// <param name="reach">Where the front stands.</param>
		/// <param name="coastSurface">The ice sheet's surface at the coast, scene metres.</param>
		/// <param name="front">The front's freeboard (<see cref="FrontFreeboardMetres"/>).</param>
		public static float Freeboard(float metres, float reach, float coastSurface, float front)
		{
			float grounded = Mathf.Clamp(coastSurface, MinFreeboardMetres, MaxFreeboardMetres);
			float atFront = Mathf.Min(front, grounded);
			float left = 1f - Mathf.Clamp01(metres / Mathf.Max(1f, reach));
			float freeboard = atFront + (grounded - atFront) * left * left;
			if (coastSurface > grounded)
			{
				freeboard += (coastSurface - grounded) * Mathf.Exp(-Mathf.Max(0f, metres) / HingeMetres);
			}
			return freeboard;
		}

		/// <summary>The shelf's surface at a point, scene metres: its <see cref="Freeboard"/> with the surface swells, never under <see cref="MinFreeboardMetres"/>.</summary>
		public static float ShelfSurfaceMetres(uint seed, Vector2 coast, float east, float north, float metres, float coastSurface)
		{
			float reach = FrontMetres(seed, coast, east, north);
			float freeboard = Freeboard(metres, reach, coastSurface, FrontFreeboardMetres(seed, coast.x, coast.y));
			float swell = (ValueNoise(seed ^ 0x2B9F41u, east / SwellSpacingMetres, north / SwellSpacingMetres) * 2f - 1f) * SwellMetres;
			return Mathf.Max(MinFreeboardMetres, freeboard + swell);
		}

		/// <summary>The biome a cell takes: the Ice Shelf role's holder on the shelf and its fast ice, else the climate's own choice.</summary>
		public static T BiomeOf<T>(IceShelfPart part, T chosen, T shelf) where T : class
		{
			return part != IceShelfPart.None && shelf != null ? shelf : chosen;
		}

		// ── A scene's reach ───────────────────────────────────────────

		/// <summary>
		/// The reach a scene's request lays, built on first ask from its ground before any shelf and kept on the request:
		/// null when nothing opts in or no calving coast stands near the scene. Main thread only.
		/// </summary>
		internal static IceShelfReach For(SceneGenerationRequest request, SceneAltitude beforeShelf)
		{
			if (request == null)
			{
				return null;
			}
			if (!request.iceShelfReachBuilt || !ReferenceEquals(request.iceShelfReachBody, request.Body))
			{
				request.iceShelfReach = Lay(request, beforeShelf);
				request.iceShelfReachBuilt = true;
				request.iceShelfReachBody = request.Body;
			}
			return request.iceShelfReach;
		}

		private static IceShelfReach Lay(SceneGenerationRequest request, SceneAltitude beforeShelf)
		{
			// Opt-in first, and cheapest: nothing is laid until a template holds the role.
			if (BiomeTemplateArt.WaterBiome(BiomeWaterRole.IceShelf) == null)
			{
				return null;
			}
			WorldBody body = request.Body;
			if (body == null || request.FrozenSeas || request.Layer != null && request.Layer.Underground)
			{
				return null;
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(body);
			if (SurfaceLiquids.For(system, body) != SurfaceLiquid.Water || !new IceSheetSurface(system, body, false).Active)
			{
				return null;
			}
			PlanetClimateField climate = PlanetClimateField.For(system, body);
			uint seed = body.ResolvedTerrainSeed;

			/* The scene, its backdrop out to the horizon (which reads this same ground), and a margin as wide as a shelf and
			 * its fast ice reach, so a coast just past the backdrop still runs its shelf into it. */
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			float margin = SceneBackdropBuilder.ReachFor(request) + MaxReachMetres + MaxApronMetres + 2f * CellMetres;
			int width = Mathf.Max(1, Mathf.CeilToInt((plan.WidthMetres + 2f * margin) / CellMetres));
			int height = Mathf.Max(1, Mathf.CeilToInt((plan.DepthMetres + 2f * margin) / CellMetres));
			var origin = new Vector2(-width * CellMetres * 0.5f, -height * CellMetres * 0.5f);
			var ground = new float[width * height];
			var ice = new float[width * height];
			var cold = new bool[width * height];
			Parallel.For(0, height, z =>
			{
				float north = origin.y + (z + 0.5f) * CellMetres;
				for (int x = 0; x < width; x++)
				{
					float east = origin.x + (x + 0.5f) * CellMetres;
					int i = z * width + x;
					ground[i] = beforeShelf.BeforeShelf(east, north, out Vector3 direction, out ice[i]);
					cold[i] = ground[i] <= 0f && AnnualSeaLevelC(climate, direction) <= IceOccurrence.IceShelfLimitC;
				}
			});
			return Build(width, height, CellMetres, origin, ground, ice, cold, seed);
		}

		/// <summary>The year's mean sea-level air at a point, °C: the same monthly air the ice placer reads a coast by.</summary>
		private static float AnnualSeaLevelC(PlanetClimateField climate, Vector3 direction)
		{
			float[] seasons = IceOccurrence.SeasonalAirC(climate, PlanetClimateField.LatitudeOf(direction), direction);
			float sum = 0f;
			for (int s = 0; s < seasons.Length; s++)
			{
				sum += seasons[s];
			}
			return seasons.Length > 0 ? sum / seasons.Length : 0f;
		}

		/// <summary>A line for the cut's notes: how much of an area is shelf and fast ice, read at the grid's cells.</summary>
		public string Describe(Rect area)
		{
			int shelf = 0, apron = 0;
			for (int z = 0; z < Height; z++)
			{
				float north = Origin.y + (z + 0.5f) * Cell;
				for (int x = 0; x < Width; x++)
				{
					float east = Origin.x + (x + 0.5f) * Cell;
					int i = z * Width + x;
					if (!area.Contains(new Vector2(east, north)) || float.IsPositiveInfinity(distance[i]))
					{
						continue;
					}
					var coast = new Vector2(coastEast[i], coastNorth[i]);
					IceShelfPart part = Part(distance[i], FrontMetres(Seed, coast, east, north), FastIceMetres(Seed, coast, east, north));
					shelf += part == IceShelfPart.Shelf ? 1 : 0;
					apron += part == IceShelfPart.Apron ? 1 : 0;
				}
			}
			float km2 = Cell * Cell / 1e6f;
			return $"Ice shelf: {Sources} calving-coast cell(s) near the scene; in it about {shelf * km2:0.##} km² of floating shelf " +
				$"({MinFreeboardMetres:0}–{MaxFreeboardMetres:0} m freeboard to a calving front {MinReachMetres / 1000f:0}–{MaxReachMetres / 1000f:0} km out) " +
				$"and {apron * km2:0.##} km² of fast ice at +{ApronFreeboardMetres:0.0} m to the floe edge.";
		}

		// ── Noise (pure: the same on every platform and thread) ───────

		private static float Stretch(float value) => Mathf.Clamp01((value - 0.5f) * 1.8f + 0.5f);

		private static float Fbm(uint seed, float x, float y)
		{
			return ValueNoise(seed, x, y) * 0.7f + ValueNoise(seed ^ 0x9E3779B9u, x * 2.03f + 17.1f, y * 2.03f - 9.3f) * 0.3f;
		}

		/// <summary>Smoothed value noise in 0…1 from a hashed lattice.</summary>
		public static float ValueNoise(uint seed, float x, float y)
		{
			int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float a = Hash(seed, x0, y0), b = Hash(seed, x0 + 1, y0);
			float c = Hash(seed, x0, y0 + 1), d = Hash(seed, x0 + 1, y0 + 1);
			float bottom = a + (b - a) * fx, top = c + (d - c) * fx;
			return bottom + (top - bottom) * fy;
		}

		private static float Hash(uint seed, int x, int y)
		{
			unchecked
			{
				uint h = seed ^ ((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u);
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return (h & 0xFFFFFFu) / 16777215f;
			}
		}
	}
}
#endif
