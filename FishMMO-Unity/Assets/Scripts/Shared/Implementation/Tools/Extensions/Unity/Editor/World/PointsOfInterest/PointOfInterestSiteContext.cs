#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The props every site in a scene lays, collected into one prop set (<see cref="PointOfInterestGenerator.PropSource"/>)
	/// so they draw instanced with streamed collision like the cliffs and boulders.
	/// </summary>
	public sealed class PointOfInterestPropSink
	{
		public readonly List<ScenePropSet.Prototype> Prototypes = new List<ScenePropSet.Prototype>();
		public readonly List<ScenePropSet.Prop> Props = new List<ScenePropSet.Prop>();
		private readonly Dictionary<GameObject, int> prototypeOf = new Dictionary<GameObject, int>();

		public void Add(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
		{
			if (prefab == null)
			{
				return;
			}
			if (!prototypeOf.TryGetValue(prefab, out int prototype))
			{
				prototype = Prototypes.Count;
				prototypeOf[prefab] = prototype;
				Prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = -1 });
			}
			Props.Add(new ScenePropSet.Prop { Prototype = prototype, Position = position, Rotation = rotation, Scale = scale });
		}
	}

	/// <summary>
	/// Everything a <see cref="PointOfInterestFeature"/> (or a registered <see cref="IPointOfInterestSiteBuilder"/>) is
	/// given to build one site with.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deterministic.</b> <see cref="Random"/> is seeded from the record's <see cref="PointOfInterestRecord.SiteSeed"/>
	/// and fresh for each feature, so a feature's draws do not shift when another feature is added before it.
	/// </para>
	/// <para>
	/// <b>Where things go.</b> Instanced visuals with collision go through <see cref="AddProp"/>; scene objects
	/// (spawners, a waypoint, a region) go under <see cref="Root"/> (the site's <see cref="ScenePointOfInterest"/>), which is
	/// shared by both builds. Client-only visuals that are not props go under a ClientOnlyObject child; colliders never do.
	/// </para>
	/// </remarks>
	public sealed class PointOfInterestSiteContext
	{
		public Scene Scene;
		/// <summary>The site's object, a child of the scene's "Points of Interest" root, at the record's anchor and heading.</summary>
		public Transform Root;
		public PointOfInterestRecord Record;
		/// <summary>The template the site was built from; null for a marker-only site (site builders run for those too).</summary>
		public PointOfInterestTemplate Template;
		/// <summary>Seeded from the site; see remarks.</summary>
		public DeterministicRNG Random;
		/// <summary>The scene's POI asset.</summary>
		public ScenePointsOfInterest Points;
		/// <summary>The scene's request (body, layer, settings).</summary>
		public SceneGenerationRequest Request;
		/// <summary>The scene's terrain folder: where a feature writes any asset it makes.</summary>
		public string TerrainFolder;
		/// <summary>The scene's rivers and lakes; may be null.</summary>
		public SceneWater Water;
		/// <summary>The ground at (east, north), scene metres.</summary>
		public Func<float, float, float> GroundAt;
		/// <summary>The ground's steepness at (east, north), degrees.</summary>
		public Func<float, float, float> SlopeAt;
		/// <summary>The biome at (east, north); may answer null.</summary>
		public Func<float, float, BiomeTemplate> BiomeAt;
		public List<string> Notes;
		internal PointOfInterestPropSink Sink;

		/// <summary>The style pieces are resolved in: the template's, else the site's race, else empty.</summary>
		public string Style => Template != null && !string.IsNullOrWhiteSpace(Template.Style) ? Template.Style : Record?.Race ?? string.Empty;

		/// <summary>A structure piece's prefab for this site's style, or null (<see cref="PointOfInterestPieces"/>).</summary>
		public GameObject ResolvePiece(string tag, int seed) => PointOfInterestPieces.Resolve(tag, Style, seed);

		/// <summary>Adds an instanced prop to the scene's POI prop set.</summary>
		public void AddProp(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Sink?.Add(prefab, position, rotation, scale);

		/// <summary>The world position of an offset in the site's own frame (x right, z ahead along its heading), on the ground.</summary>
		public Vector3 OnGround(Vector2 local)
		{
			Quaternion turn = Quaternion.Euler(0f, Record.Yaw, 0f);
			Vector3 offset = turn * new Vector3(local.x, 0f, local.y);
			float x = Record.Position.x + offset.x, z = Record.Position.z + offset.z;
			return new Vector3(x, GroundAt != null ? GroundAt(x, z) : Record.Position.y, z);
		}

		/// <summary>A new child object of the site, for spawners, waypoints and the like.</summary>
		public GameObject AddChild(string name)
		{
			var child = new GameObject(name);
			child.transform.SetParent(Root, false);
			return child;
		}
	}

	/// <summary>
	/// Something a template carries that is built at each of its sites: props, a waypoint, spawners, a portal.
	/// </summary>
	/// <remarks>
	/// Held by <c>[SerializeReference]</c> on the template, so features defined anywhere in the editor appear in its
	/// list. A feature must be deterministic in <see cref="PointOfInterestSiteContext.Random"/> and must not assume it runs
	/// once: a repaint builds every site again from scratch under a fresh site object.
	/// </remarks>
	[Serializable]
	public abstract class PointOfInterestFeature
	{
		public abstract void Build(PointOfInterestSiteContext context);
	}

	/// <summary>
	/// Built at EVERY site of the kinds it takes, with or without a template: the seam for gameplay that every site of a
	/// kind needs (a waypoint in every settlement, a boss spawner in every lair) whatever it was built from.
	/// </summary>
	public interface IPointOfInterestSiteBuilder
	{
		bool Handles(PointOfInterestRecord record);
		void Build(PointOfInterestSiteContext context);
	}

	/// <summary>The registered site builders (see <see cref="IPointOfInterestSiteBuilder"/>), run in registration order after a site's template features.</summary>
	public static class PointOfInterestSiteBuilders
	{
		private static readonly List<IPointOfInterestSiteBuilder> builders = new List<IPointOfInterestSiteBuilder>();

		public static IReadOnlyList<IPointOfInterestSiteBuilder> All => builders;

		public static void Register(IPointOfInterestSiteBuilder builder)
		{
			if (builder != null && !builders.Contains(builder))
			{
				builders.Add(builder);
			}
		}

		public static void Unregister(IPointOfInterestSiteBuilder builder) => builders.Remove(builder);
	}
}
#endif
