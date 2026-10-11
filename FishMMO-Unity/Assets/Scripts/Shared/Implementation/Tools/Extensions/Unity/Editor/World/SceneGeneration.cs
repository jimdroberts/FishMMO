#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FishMMO.Shared.Biomes;
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
		/// The planet's finest features are about 1.7 km across in a scene cut at the atlas
		/// radius, so without these the ground is right in shape but smooth underfoot. They are
		/// centred on zero and follow the planet's own ruggedness, so the scene still agrees with
		/// the globe about where the mountain is — they only decide what it looks like close up.
		/// </remarks>
		public bool FineDetail = true;

		/// <summary>
		/// Whether to wear the ground before it is written: rain cutting gullies and laying fans,
		/// steep ground slumping, soil creeping (<see cref="SceneErosion"/>).
		/// </summary>
		/// <remarks>
		/// Off cuts the planet's ground exactly as the globe and the backdrop have it — what the scene-cut
		/// snapshot compares a terrain against, and a way to see what erosion did.
		/// </remarks>
		public bool Erosion = true;

		/// <summary>
		/// Whether the planet's rivers and lakes are laid into the ground (<see cref="SceneWater"/>). Only
		/// with <see cref="Erosion"/>: without it the ground is the planet's exactly.
		/// </summary>
		public bool Rivers = true;

		/// <summary>How deep erosion cuts: rivers, glaciers and rain together. 1 the defaults; the scene's atlas entry carries it for re-cuts.</summary>
		public float ErosionStrength = 1f;

		/// <summary>
		/// The radius the scene's kilometres are laid over the globe at. Zero means the body's
		/// atlas radius now, which is what the rectangle was drawn on.
		/// </summary>
		/// <remarks>
		/// Only set by something re-cutting a scene that was first cut at a different radius.
		/// </remarks>
		public double RadiusKm;

		/// <summary>
		/// True only when re-cutting a scene the generator made: its own name, scene file and
		/// terrain folder are expected to exist and are replaced. See <see cref="SceneGenerator.Recut"/>.
		/// </summary>
		public bool ReplaceExisting;

		/// <summary>
		/// The scene's point-of-interest choices (capital, density, per-kind overrides), asked before the cut and kept on
		/// its atlas entry. Null cuts the scene with the catalogue's Normal defaults and no capital.
		/// </summary>
		public PointOfInterestSettings PointsOfInterest;

		/// <summary>The radius actually used: <see cref="RadiusKm"/>, or the body's atlas radius.</summary>
		public double ResolvedRadiusKm => RadiusKm > 0.0 ? RadiusKm : PlanetSurface.SceneRadiusKm(Body);

		/// <summary>Scene metres per metre of the planet's own altitude.</summary>
		public float VerticalScale => PlanetSurface.SceneVerticalScale(Body, ResolvedRadiusKm);

		/// <summary>Scene metres per metre of crater depth: craters keep their own proportions (<see cref="PlanetSurface.SceneCraterScale"/>).</summary>
		public float CraterScale => PlanetSurface.SceneCraterScale(Body, ResolvedRadiusKm);

		/// <summary>The scene's relief budget in metres: the body's relief at <see cref="VerticalScale"/>.</summary>
		public float SceneReliefMetres => PlanetSurface.ReliefMetres(Body) * VerticalScale;

		public AtlasFootprint Footprint => new AtlasFootprint
		{
			Latitude = Latitude,
			Longitude = Longitude,
			SizeKm = SizeKm,
			HeadingDegrees = HeadingDegrees,
		};

		/// <summary>
		/// True when the body's seas are frozen through, so the generator lays an ice shelf where
		/// the sea would be. See <see cref="SceneGeneration.IceShelfMetres"/>.
		/// </summary>
		/// <remarks>
		/// Resolved once per request: <see cref="SceneGeneration.AltitudeMetres"/> asks it for every
		/// one of the millions of heights a scene is written from.
		/// </remarks>
		public bool FrozenSeas
		{
			get
			{
				if (frozenSeas == null)
				{
					/* Only from the system the body is actually in. A body in none (one being set up, or a test's) would be
					 * read against the active system, where it has no orbit and so no sunlight, and come out frozen through:
					 * its whole sea floor laid over with an ice shelf at the datum. */
					SolarSystemProfile system = Body != null ? SolarSystemProfile.Resolve(Body) : null;
					frozenSeas = system != null && system.Contains(Body) && BiomeWorldConditions.For(system, Body).IsFrozenThrough;
				}
				return frozenSeas.Value;
			}
		}

		[System.NonSerialized] private bool? frozenSeas;
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
		/// Only scenes on the same body and in the same layer, because layers are how a cave system
		/// sits under a forest without the two being the same place.
		///
		/// The body check cannot be left to the layer: bodies without layers of their own share the
		/// atlas's default layers, so the same surface layer is on every planet and a layer match
		/// alone made a cut on one world collide with scenes on all of them. <paramref name="bodyOf"/>
		/// resolves a scene's body, since an entry with no body belongs to the home world.
		/// </remarks>
		public static List<WorldAtlasScene> Collisions(
			SceneGenerationRequest request, IEnumerable<WorldAtlasScene> placed, double radiusKm,
			Func<WorldAtlasScene, WorldBody> bodyOf)
		{
			var hits = new List<WorldAtlasScene>();
			if (request == null || placed == null || bodyOf == null)
			{
				return hits;
			}
			AtlasFootprint footprint = request.Footprint;
			foreach (WorldAtlasScene other in placed)
			{
				if (other == null || !other.Placed || other.Layer != request.Layer || bodyOf(other) != request.Body)
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
		/// The altitude, in scene metres above the body's sea level, at a point inside a scene.
		/// </summary>
		/// <param name="request">The scene being cut.</param>
		/// <param name="eastMetres">Metres along the scene's +X from its centre: east, at a heading of 0.</param>
		/// <param name="northMetres">Metres along the scene's +Z from its centre: north, at a heading of 0.</param>
		/// <remarks>
		/// <para>
		/// The point is carried back onto the globe at the <b>atlas</b> radius — the globe the
		/// rectangle was drawn on — and the planet is asked, so the scene is exactly the ground the
		/// rectangle covers and a coastline on the globe is the coastline underfoot.
		/// </para>
		/// <para>
		/// The planet's altitude is then brought to scene height by
		/// <see cref="PlanetSurface.SceneVerticalScale"/>, and the finer octaves are added on top:
		/// gentle on low ground, broken and ridged on high ground. They are centred on zero, so
		/// they roughen the ground without moving it.
		/// </para>
		/// </remarks>
		public static float AltitudeMetres(SceneGenerationRequest request, float eastMetres, float northMetres)
		{
			return request != null ? new SceneAltitude(request).At(eastMetres, northMetres) : 0f;
		}

		/// <summary>Height of a frozen sea's surface above the datum, in scene metres, before its ridges.</summary>
		/// <remarks>Sea ice floats with about a tenth of its thickness showing; a metre is a thick shelf's freeboard.</remarks>
		public const float IceFreeboardMetres = 1f;

		/// <summary>How high the tallest pressure ridges stand above the shelf, in scene metres.</summary>
		public const float IceRidgeMetres = 1.5f;

		/// <summary>Metres between neighbouring pressure ridges, roughly.</summary>
		public const float IceRidgeSpacingMetres = 90f;

		/// <summary>
		/// The surface of a frozen-through sea at a point of the scene: a shelf just above the datum,
		/// crossed by pressure ridges.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why the ground and not a surface over it.</b> A world frozen through (Galris, a Europa)
		/// has no sea under its ice in any sense a player meets: the shell is kilometres thick. A
		/// water or lava plane at the datum is a liquid's renderer and collider; laying the ice as
		/// terrain instead gives it collision, footing, painting and scatter for nothing, and it is
		/// what the scene would be if it were sculpted by hand. Without it the seas came out as empty
		/// basins once the water test stopped calling a frozen world wet.
		/// </para>
		/// <para>
		/// <b>Ridges, not noise.</b> Ice under stress buckles along lines where floes were pushed
		/// together, so the relief is a few long crests over flat pans rather than bumps everywhere:
		/// a ridged noise, sharpened so most of the shelf stays flat.
		/// </para>
		/// </remarks>
		public static float IceShelfMetres(SceneGenerationRequest request, float eastMetres, float northMetres)
		{
			return IceShelfMetres(request.Body != null ? request.Body.ResolvedTerrainSeed : 1u, eastMetres, northMetres);
		}

		/// <summary><see cref="IceShelfMetres(SceneGenerationRequest, float, float)"/> for a body's terrain seed.</summary>
		public static float IceShelfMetres(uint seed, float eastMetres, float northMetres)
		{
			float offset = (seed & 0xFFFF) * 0.173f;
			float u = eastMetres / IceRidgeSpacingMetres + offset;
			float v = northMetres / IceRidgeSpacingMetres - offset;
			float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(u, v) * 2f - 1f);
			// Cubed: a crest where the noise crosses its middle, flat pan everywhere else.
			ridge = ridge * ridge * ridge;
			return IceFreeboardMetres + ridge * IceRidgeMetres;
		}

		/// <summary>
		/// The planet's own altitude under a point of the scene, in the planet's metres: what the
		/// globe says, before the scene's vertical scale or any local detail.
		/// </summary>
		public static float PlanetAltitudeMetres(SceneGenerationRequest request, float eastMetres, float northMetres)
		{
			if (request == null)
			{
				return 0f;
			}
			/* Through the footprint, so the heading turns the ground exactly as the atlas turns
			 * the rectangle. With a heading of 0 the scene's +X is east and +Z north. */
			Vector3 direction = AtlasGeometry.SceneToUnit(request.Footprint,
				eastMetres / 1000.0, northMetres / 1000.0, request.ResolvedRadiusKm).ToVector3();
			uint seed = request.Body != null ? request.Body.ResolvedTerrainSeed : 1u;
			return PlanetSurface.AltitudeMetres(seed, request.Body, direction);
		}
	}

	/// <summary>
	/// One scene's ground as a function of position, with everything that does not depend on the
	/// position worked out once.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What <see cref="SceneGeneration.AltitudeMetres"/> is.</b> It builds one of these and asks it
	/// once; a caller asking millions of times builds one and keeps it.
	/// </para>
	/// <para>
	/// <b>Safe to share between threads once built</b>, which is why it is built on the main thread
	/// and holds values rather than the request. Asking the request per sample is not merely slow: a
	/// body's seed can come from its asset name, which Unity only answers on the main thread, and the
	/// planet's profile sits in a cache that is not safe to share.
	/// </para>
	/// </remarks>
	public readonly struct SceneAltitude
	{
		private readonly bool valid;
		private readonly AtlasFootprint footprint;
		private readonly double radiusKm;
		private readonly uint seed;
		private readonly uint detailSeed;
		private readonly float cratering;
		private readonly PlanetSurface.PlanetProfile profile;
		private readonly float bodyRelief;
		private readonly float verticalScale;
		private readonly float craterScale;
		private readonly float sceneRelief;
		private readonly bool fineDetail;
		private readonly bool frozenSeas;
		private readonly IceSheetSurface iceSheet;

		/// <summary>Resolves the request's ground. Main thread only; <see cref="At"/> is then safe anywhere.</summary>
		public SceneAltitude(SceneGenerationRequest request)
		{
			valid = request != null;
			if (!valid)
			{
				this = default;
				return;
			}
			WorldBody body = request.Body;
			footprint = request.Footprint;
			radiusKm = request.ResolvedRadiusKm;
			seed = body != null ? body.ResolvedTerrainSeed : 1u;
			/* Mixed with the scene's name so two scenes cut from nearby ground do not get the
			 * same hills, and so re-generating one scene cannot change another. */
			detailSeed = seed ^ unchecked((uint)(request.SceneName ?? string.Empty).GetDeterministicHashCode());
			cratering = PlanetSurface.CrateringOf(body);
			profile = PlanetSurface.ProfileOf(seed, body);
			bodyRelief = PlanetSurface.ReliefMetres(body);
			verticalScale = request.VerticalScale;
			craterScale = request.CraterScale;
			sceneRelief = request.SceneReliefMetres;
			fineDetail = request.FineDetail;
			frozenSeas = request.FrozenSeas;
			iceSheet = new IceSheetSurface(body != null ? SolarSystemProfile.Resolve(body) : null, body, frozenSeas);
		}

		/// <summary>The altitude in scene metres above the body's sea level: see <see cref="SceneGeneration.AltitudeMetres"/>.</summary>
		public float At(float eastMetres, float northMetres)
		{
			if (!valid)
			{
				return 0f;
			}
			/* Through the footprint, so the heading turns the ground exactly as the atlas turns
			 * the rectangle. With a heading of 0 the scene's +X is east and +Z north. */
			Vector3 direction = AtlasGeometry.SceneToUnit(footprint, eastMetres / 1000.0, northMetres / 1000.0, radiusKm).ToVector3();
			PlanetSurface.AltitudeParts(seed, cratering, profile, bodyRelief, direction, out float planet, out float uncratered);
			/* The planet's ground and its craters come to scene height apart: the ground by the vertical
			 * scale, the craters with the kilometres (CraterScale), or every crater would be a pit about
			 * as deep as it is wide. */
			float altitude = uncratered * verticalScale + (planet - uncratered) * craterScale;
			if (fineDetail)
			{
				float ruggedness = PlanetSurface.Ruggedness(planet, bodyRelief);
				altitude += PlanetSurface.LocalDetailMetres(detailSeed, eastMetres, northMetres, sceneRelief, ruggedness);
			}
			if (frozenSeas)
			{
				altitude = Mathf.Max(altitude, SceneGeneration.IceShelfMetres(seed, eastMetres, northMetres));
			}
			// Land whose summer never thaws is buried under the Ice Sheet's ice; at a cold coast that is the calving front.
			altitude += iceSheet.ThicknessMetres(direction.normalized, planet, altitude, eastMetres, northMetres);
			return altitude;
		}
	}
}
#endif
