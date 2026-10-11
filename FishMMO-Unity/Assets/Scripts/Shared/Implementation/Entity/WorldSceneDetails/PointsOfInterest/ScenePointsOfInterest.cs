using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One generated point of interest: what it is, where, what it is called and what it links to.
	/// </summary>
	/// <remarks>
	/// Plain data both peers can read. The name is baked as a string, so the server, the client, the world map
	/// and the atlas all show the same words with nothing on the wire, and a change to the naming data renames
	/// nothing until the scene is cut again.
	/// </remarks>
	[Serializable]
	public class PointOfInterestRecord
	{
		/// <summary>Stable within the scene: a hash of the kind and its rounded cell, never a list index.</summary>
		public int Id;
		public POIType Kind;
		/// <summary>The site's anchor in world space (metres; y is the ground or, underwater, the floor).</summary>
		public Vector3 Position;
		/// <summary>The site's heading in degrees about +y.</summary>
		public float Yaw;
		/// <summary>The footprint radius in metres.</summary>
		public float Radius;
		/// <summary>0 = small, 1 = medium, 2 = large; picks layouts, pack sizes and cave depth.</summary>
		public byte SizeClass;

		/// <summary>The biome at the anchor (<see cref="Biomes.BiomeRegistry"/> cached ID), 0 for none.</summary>
		public int BiomeID;
		/// <summary>The biome's climate variant as index + 1 into its variants, 0 for none (same rule as the namer).</summary>
		public byte VariantIndex;
		/// <summary>The race's naming key when the kind takes one (<see cref="PointOfInterestTraits.UsesRace"/>), else empty.</summary>
		public string Race;

		public string Name;
		[TextArea(1, 3)]
		public string Description;
		/// <summary>The seed the name was drawn from, kept so a tool can show the draw again.</summary>
		public int NameSeed;

		/// <summary>The world map zoom tier; 0 = always, higher = only zoomed in.</summary>
		public int DetailTier;
		/// <summary>Hidden until the fog of war reveals it. Every generated POI starts true (Jim, 2026-10-10).</summary>
		public bool RequiresDiscovery = true;

		/// <summary>The POI this one belongs to (a graveyard's town, a cave's dungeon), or -1.</summary>
		public int ParentId = -1;
		/// <summary>The scene river (<see cref="Biomes.SceneHydrology.River.Id"/>) it names or stands on, or -1.</summary>
		public int RiverId = -1;
		/// <summary>The planet river the scene river is part of, or -1; rivers are named from it.</summary>
		public int PlanetRiver = -1;
		/// <summary>The scene lake it names or stands on, or -1.</summary>
		public int LakeId = -1;

		/// <summary>The template the site was built from (asset name), empty for detected kinds.</summary>
		public string Template;
		/// <summary>The seed every built choice for the site was drawn from (layout, pieces, scope).</summary>
		public int SiteSeed;
		/// <summary>A per-scene index for things players unlock by index (a waypoint, a portal), or -1.</summary>
		public int UnlockIndex = -1;

		public PointOfInterestKindInfo Info => PointOfInterestKinds.Info(Kind);

		/// <summary>The map entry this POI is drawn as.</summary>
		public MapPointOfInterestDetails ToDetails()
			=> new MapPointOfInterestDetails
			{
				Name = Name,
				Description = Description,
				Position = Position,
				Type = PointOfInterestKinds.MarkerFor(Kind),
				DetailTier = Mathf.Max(0, DetailTier),
				RequiresDiscovery = RequiresDiscovery,
				ShowOnMinimap = true,
			};
	}

	/// <summary>A flattened pad under a site: the ground eased to <see cref="Height"/> inside the radius.</summary>
	[Serializable]
	public struct PointOfInterestPad
	{
		public int Id;
		public Vector3 Centre;
		public float Radius;
		/// <summary>How far past the radius the pad eases back into the ground, metres.</summary>
		public float Blend;
		public float Height => Centre.y;
	}

	/// <summary>A disc nothing else is placed in (scatter, cliff rocks, boulders): x, z, radius.</summary>
	[Serializable]
	public struct PointOfInterestKeepOut
	{
		public int Id;
		public float X;
		public float Z;
		public float Radius;

		public bool Contains(float x, float z)
		{
			float dx = x - X, dz = z - Z;
			return dx * dx + dz * dz <= Radius * Radius;
		}
	}

	/// <summary>
	/// What a terrain shaper (a carved cave, an overhang, an arch) decided for one site when the scene was planned:
	/// kept with the plan so a repaint can build the same mesh again without planning (or carving) twice.
	/// </summary>
	/// <remarks>
	/// Plain values on purpose: the asset is read in builds, so it can hold no editor-only type. Each shaper owns the
	/// meaning of <see cref="Values"/> for its own <see cref="Shaper"/> name.
	/// </remarks>
	[Serializable]
	public class PointOfInterestShape
	{
		/// <summary>The site (<see cref="PointOfInterestRecord.Id"/>) it belongs to.</summary>
		public int SiteId;
		/// <summary>The shaper that wrote it, by its stable name.</summary>
		public string Shaper;
		/// <summary>Its anchor (a cave's mouth), scene metres.</summary>
		public Vector3 Position;
		/// <summary>Its heading in degrees about +y.</summary>
		public float Yaw;
		/// <summary>Its extents, metres (a tunnel's width, height and length).</summary>
		public Vector3 Size;
		public int Seed;
		/// <summary>Anything else the shaper needs, in an order it defines.</summary>
		public float[] Values = Array.Empty<float>();
	}

	/// <summary>
	/// The points of interest a generated scene was given, written beside its terrain as
	/// <c>&lt;Scene&gt; Points of Interest.asset</c>: the one source of truth everything else is derived from.
	/// </summary>
	/// <remarks>
	/// The scene's <see cref="ScenePointOfInterest"/> objects, the "POI" prop set, the spawners, the world map's
	/// entries and the atlas's view are all built from this. The PLAN (pads, keep-outs, cave descriptors) is kept
	/// too, because the plan changes the ground: a repaint reads it back instead of planning again on ground that
	/// the first plan already flattened.
	/// </remarks>
	public class ScenePointsOfInterest : ScriptableObject
	{
		/// <summary>Bumped when the meaning of the stored data changes, so an old asset is regenerated, not misread.</summary>
		public const int CurrentFormat = 1;

		public int Format = CurrentFormat;
		/// <summary>The scene's POI seed (scene seed and the "POI" salt), for display and tests.</summary>
		public int Seed;
		/// <summary>The scene this was generated for.</summary>
		public string SceneName;

		public List<PointOfInterestRecord> Points = new List<PointOfInterestRecord>();
		public List<PointOfInterestPad> Pads = new List<PointOfInterestPad>();
		public List<PointOfInterestKeepOut> KeepOuts = new List<PointOfInterestKeepOut>();
		/// <summary>What the terrain shapers planned for their sites (see <see cref="PointOfInterestShape"/>).</summary>
		public List<PointOfInterestShape> Shapes = new List<PointOfInterestShape>();
		/// <summary>
		/// The ways between the sites: roads, tracks, footpaths and trails routed on the ground, and each settlement's own
		/// streets (<see cref="ScenePath"/>). Drawn by the client's path surface (<see cref="ScenePathSurfaceBinder"/>).
		/// </summary>
		public List<ScenePath> Paths = new List<ScenePath>();
		/// <summary>
		/// The terrain array slices the paths are surfaced from: x trodden earth, y gravel, z stone (−1 for none). The
		/// scene's palette carries the three grounds for them; the slice is the palette's layer index.
		/// </summary>
		public Vector4 PathLayers = new Vector4(-1f, -1f, -1f, -1f);
		/// <summary>
		/// Per terrain layer (array slice), the slice of its biome's own path ground (footpaths, trails, tracks) and road
		/// ground, −1 for none: the overlay wears a way into the ground of the biome under each pixel. 32 entries each.
		/// </summary>
		public float[] PathEarthMap = Array.Empty<float>();
		public float[] PathRoadMap = Array.Empty<float>();

		/// <summary>The record with this id, or null.</summary>
		public PointOfInterestRecord Find(int id)
		{
			foreach (PointOfInterestRecord record in Points)
			{
				if (record != null && record.Id == id)
				{
					return record;
				}
			}
			return null;
		}

		/// <summary>Whether (x, z) lies in any keep-out disc.</summary>
		public bool IsKeptOut(float x, float z)
		{
			foreach (PointOfInterestKeepOut keepOut in KeepOuts)
			{
				if (keepOut.Contains(x, z))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The asset path beside a scene's terrain folder.</summary>
		public static string AssetPath(string terrainFolder, string sanitizedSceneName)
			=> $"{terrainFolder}/{sanitizedSceneName} Points of Interest.asset";
	}
}
