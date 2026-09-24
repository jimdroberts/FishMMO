#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What to cut out of a globe and make a scene from.</summary>
	public sealed class SceneGenerationRequest
	{
		public string SceneName;
		public WorldBody Body;
		public WorldAtlasLayer Layer;
		/// <summary>Centre of the rectangle on the globe, in degrees.</summary>
		public double Latitude;
		public double Longitude;
		/// <summary>The scene's size in kilometres (east-west, north-south).</summary>
		public Vector2 SizeKm = new Vector2(2f, 2f);
		/// <summary>Which way the scene's +Z faces, clockwise from north.</summary>
		public float HeadingDegrees;

		/// <summary>
		/// Whether to add finer octaves on top of the planet's own shape.
		/// </summary>
		/// <remarks>
		/// The planet function is continent-scale: sampled across a few kilometres it is very
		/// smooth, so a scene cut straight from it reads as gentle rolling ground wherever it is.
		/// The extra octaves are the same function at higher frequency, so they cost nothing to
		/// store and the scene still agrees with the globe about where the mountain is — they only
		/// decide what the mountain looks like close up.
		/// </remarks>
		public bool FineDetail = true;

		public AtlasFootprint Footprint => new AtlasFootprint
		{
			Latitude = Latitude,
			Longitude = Longitude,
			SizeKm = SizeKm,
			HeadingDegrees = HeadingDegrees,
		};
	}

	/// <summary>How a scene's terrain is cut into Unity terrains.</summary>
	public struct TerrainTilePlan
	{
		/// <summary>Tiles across (X) and down (Z).</summary>
		public int CountX;
		public int CountZ;
		/// <summary>One tile's size in metres.</summary>
		public float TileMetres;
		/// <summary>Heightmap resolution of one tile, always 2^n + 1.</summary>
		public int Resolution;

		public int TotalTiles => CountX * CountZ;
		public float WidthMetres => CountX * TileMetres;
		public float DepthMetres => CountZ * TileMetres;
		/// <summary>Metres between heightmap samples.</summary>
		public float MetresPerSample => Resolution > 1 ? TileMetres / (Resolution - 1) : TileMetres;

		public override string ToString() =>
			$"{CountX}x{CountZ} tiles of {TileMetres:0} m at {Resolution} ({MetresPerSample:0.##} m/sample)";
	}

	/// <summary>
	/// Cutting a new scene out of a planet: what may be cut, where the tiles go, and how high the
	/// ground is.
	/// </summary>
	/// <remarks>
	/// Pure on purpose. Whether a name may be used, whether a rectangle lands on another scene and
	/// how the ground is shaped are all answerable without opening a scene, creating an asset or
	/// touching a terrain — so the rules can be tested, and the generator that does the irreversible
	/// part has nothing left to decide.
	/// </remarks>
	public static class SceneGeneration
	{
		/// <summary>The tile size aimed for, in metres.</summary>
		/// <remarks>
		/// About a kilometre is what Unity's terrain is built around: small enough that one
		/// heightmap stays cheap and a tile can be streamed on its own, large enough that a scene
		/// is not made of hundreds of them.
		/// </remarks>
		public const float PreferredTileMetres = 1000f;

		/// <summary>Heightmap resolution of a tile. 513 over a kilometre is about two metres a sample.</summary>
		public const int TileResolution = 513;

		/// <summary>The shallowest a generated terrain is allowed to be, in metres.</summary>
		/// <remarks>
		/// A terrain stores its heights as fractions of its own height, so a flat scene given a
		/// height of a few metres would quantise its gentle slopes into steps. A floor costs
		/// nothing and keeps the precision sane.
		/// </remarks>
		public const float MinimumTerrainHeightMetres = 200f;

		// ── What may be cut ───────────────────────────────────────────

		/// <summary>
		/// Why this name cannot be used, or null when it can.
		/// </summary>
		/// <param name="sceneName">The proposed name.</param>
		/// <param name="existingSceneNames">Every world scene name already in the project, at any depth.</param>
		/// <remarks>
		/// <para>
		/// Compared without regard to case, and against the whole project rather than one world's
		/// folder. A scene is identified by its NAME everywhere that matters — the world scene
		/// details cache is keyed by it, the atlas entry finds a scene by it, and a character's
		/// location names it — so two worlds each holding a "Coast" is not a collision the folders
		/// resolve. Case is included because a Windows file system would refuse the second file
		/// even though the ordinal comparison the runtime uses would think them distinct.
		/// </para>
		/// </remarks>
		public static string NameProblem(string sceneName, IEnumerable<string> existingSceneNames)
		{
			if (string.IsNullOrWhiteSpace(sceneName))
			{
				return "A scene needs a name.";
			}
			if (sceneName.Trim() != sceneName)
			{
				return "A scene name cannot start or end with a space.";
			}
			foreach (char c in Path.GetInvalidFileNameChars())
			{
				if (sceneName.IndexOf(c) >= 0)
				{
					return "That name has characters a file cannot have.";
				}
			}
			if (existingSceneNames != null)
			{
				foreach (string existing in existingSceneNames)
				{
					if (string.Equals(existing, sceneName, StringComparison.OrdinalIgnoreCase))
					{
						return $"There is already a world scene called \"{existing}\". Scene names must be unique across every world, because a scene is identified by name and not by folder.";
					}
				}
			}
			return null;
		}

		/// <summary>
		/// The already-placed scenes a new rectangle would land on.
		/// </summary>
		/// <remarks>
		/// Only scenes in the same layer, because layers are how a cave system sits under a forest
		/// without the two being the same place.
		/// </remarks>
		public static List<WorldAtlasScene> Collisions(
			SceneGenerationRequest request, IEnumerable<WorldAtlasScene> placed, double radiusKm)
		{
			var hits = new List<WorldAtlasScene>();
			if (request == null || placed == null)
			{
				return hits;
			}
			AtlasFootprint footprint = request.Footprint;
			foreach (WorldAtlasScene other in placed)
			{
				if (other == null || !other.Placed || other.Layer != request.Layer)
				{
					continue;
				}
				if (AtlasGeometry.Overlaps(footprint, AtlasFootprint.Of(other), radiusKm))
				{
					hits.Add(other);
				}
			}
			return hits;
		}

		// ── Where the tiles go ────────────────────────────────────────

		/// <summary>
		/// How to cut a scene of this size into stitched terrain tiles.
		/// </summary>
		/// <remarks>
		/// The tile count is chosen so each tile lands near <see cref="PreferredTileMetres"/> and
		/// the grid covers the whole scene. Every tile in a scene is the same size and, when the
		/// generator makes them, shares one base height and one height range — which is what makes
		/// the stitched result one landmass rather than a set of terraces with cracks along the
		/// seams.
		/// </remarks>
		public static TerrainTilePlan PlanTiles(Vector2 sizeKm)
		{
			float widthMetres = Mathf.Max(1f, Mathf.Abs(sizeKm.x) * 1000f);
			float depthMetres = Mathf.Max(1f, Mathf.Abs(sizeKm.y) * 1000f);

			int countX = Mathf.Max(1, Mathf.RoundToInt(widthMetres / PreferredTileMetres));
			int countZ = Mathf.Max(1, Mathf.RoundToInt(depthMetres / PreferredTileMetres));

			/* One tile size for the whole scene, taken from the longer side. Square tiles because
			 * Unity's terrain heightmap is square: a rectangular tile would either waste samples on
			 * one axis or under-sample the other. */
			float tileMetres = Mathf.Max(widthMetres / countX, depthMetres / countZ);
			countX = Mathf.Max(1, Mathf.CeilToInt(widthMetres / tileMetres));
			countZ = Mathf.Max(1, Mathf.CeilToInt(depthMetres / tileMetres));

			return new TerrainTilePlan
			{
				CountX = countX,
				CountZ = countZ,
				TileMetres = tileMetres,
				Resolution = TileResolution,
			};
		}

		// ── How high the ground is ────────────────────────────────────

		/// <summary>
		/// The altitude, in metres above the body's sea level, at a point inside a scene.
		/// </summary>
		/// <param name="request">The scene being cut.</param>
		/// <param name="eastMetres">Metres east of the scene's centre.</param>
		/// <param name="northMetres">Metres north of the scene's centre.</param>
		/// <remarks>
		/// The point is carried back onto the globe and the planet is asked, so the scene and the
		/// world map cannot disagree: a coastline on the globe is the coastline underfoot. The
		/// finer octaves are added afterwards and average to nothing, so they roughen the ground
		/// without moving it.
		/// </remarks>
		public static float AltitudeMetres(SceneGenerationRequest request, float eastMetres, float northMetres)
		{
			if (request == null)
			{
				return 0f;
			}
			double radiusKm = request.Body != null ? Math.Max(0.001, request.Body.SkyRadiusKm) : PlanetSurface.EarthRadiusKm;
			Vector3 direction = AtlasGeometry.Offset(request.Latitude, request.Longitude, eastMetres / 1000.0, northMetres / 1000.0, radiusKm).ToVector3();

			uint seed = request.Body != null ? request.Body.ResolvedTerrainSeed : 1u;
			float altitude = PlanetSurface.AltitudeMetres(seed, request.Body, direction);
			if (request.FineDetail)
			{
				/* Mixed with the scene's name so two scenes cut from nearby ground do not get the
				 * same hills, and so re-generating one scene cannot change another. */
				uint detailSeed = seed ^ unchecked((uint)(request.SceneName ?? string.Empty).GetDeterministicHashCode());
				altitude += PlanetSurface.LocalDetailMetres(detailSeed, eastMetres, northMetres, request.Body);
			}
			return altitude;
		}

		/// <summary>One sample of the planet's own shape per this many metres, when bounding a scene.</summary>
		/// <remarks>
		/// The planet term is continent-scale — its finest feature spans hundreds of kilometres —
		/// so 250 m is far finer than it needs and a 20 km scene still costs under seven thousand
		/// samples. Clamped below so a small scene is not bounded from a handful of points.
		/// </remarks>
		public const float BoundsSampleMetres = 250f;
		private const int MinimumBoundsSteps = 96;

		/// <summary>
		/// The lowest and highest ground a scene can possibly have, in metres above the body's sea
		/// level.
		/// </summary>
		/// <param name="request">The scene being cut.</param>
		/// <param name="plan">Its tile grid, for how far the scene reaches.</param>
		/// <param name="lowest">Metres above sea level of the lowest possible ground.</param>
		/// <param name="highest">Metres above sea level of the highest possible ground.</param>
		/// <remarks>
		/// <para>
		/// <b>A bound, not a measurement.</b> Every tile's heightmap is stored as a fraction of one
		/// shared height range, so a height outside that range is not merely rounded — it is
		/// clamped, and the ground comes out with a flat top where a summit should be and a flat
		/// floor where a gully should be. A scene is written at about two metres a sample; a range
		/// sampled every forty-seven metres, as this was, misses most of what local detail does,
		/// and the terrain then flattens every peak and pit between the samples it took.
		/// </para>
		/// <para>
		/// So the two terms are bounded differently, which is the whole point: the planet's own
		/// shape is sampled, because it is smooth over a scene and sampling it closely is cheap,
		/// while local detail is bounded <em>exactly</em> from
		/// <see cref="PlanetSurface.LocalDetailAmplitudeMetres"/> — it cannot leave ±amplitude
		/// anywhere, so no sampling can improve on that and none is done. The result is a range
		/// the ground provably fits inside at every one of the millions of points that get written.
		/// </para>
		/// </remarks>
		public static void Bounds(SceneGenerationRequest request, TerrainTilePlan plan, out float lowest, out float highest)
		{
			lowest = 0f;
			highest = 0f;
			if (request == null)
			{
				return;
			}

			float halfWidth = plan.WidthMetres * 0.5f;
			float halfDepth = plan.DepthMetres * 0.5f;
			int steps = Mathf.Clamp(
				Mathf.CeilToInt(Mathf.Max(plan.WidthMetres, plan.DepthMetres) / BoundsSampleMetres),
				MinimumBoundsSteps, 512);

			// The planet's own shape only: local detail is added back as an exact bound below, and
			// sampling it here would understate it however fine the grid was.
			bool fineDetail = request.FineDetail;
			request.FineDetail = false;
			try
			{
				lowest = float.MaxValue;
				highest = float.MinValue;
				for (int z = 0; z <= steps; z++)
				{
					float north = Mathf.Lerp(-halfDepth, halfDepth, z / (float)steps);
					for (int x = 0; x <= steps; x++)
					{
						float east = Mathf.Lerp(-halfWidth, halfWidth, x / (float)steps);
						float altitude = AltitudeMetres(request, east, north);
						lowest = Mathf.Min(lowest, altitude);
						highest = Mathf.Max(highest, altitude);
					}
				}
			}
			finally
			{
				request.FineDetail = fineDetail;
			}

			if (fineDetail)
			{
				float amplitude = PlanetSurface.LocalDetailAmplitudeMetres(request.Body);
				lowest -= amplitude;
				highest += amplitude;
			}
		}
	}
}
#endif
