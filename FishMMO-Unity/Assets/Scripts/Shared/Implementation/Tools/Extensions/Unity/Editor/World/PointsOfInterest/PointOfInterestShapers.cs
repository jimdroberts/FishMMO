#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A point of interest that changes the ground: a carved cave, an overhang, an arch. Planned with the ground (it may
	/// cut holes and move heights) and built with the scene (its meshes).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Plan</b> runs once per cut (and per Regenerate POIs), after the pads are flattened and before the paint and
	/// scatter, so the biomes paint the ground the shaper left and nothing grows in its keep-outs. It may edit the
	/// terrain (<see cref="PointOfInterestTerrain"/>), add keep-outs, store what it decided as
	/// <see cref="PointOfInterestShape"/>s (kept in the scene's POI asset) and reject a site it cannot build.
	/// </para>
	/// <para>
	/// <b>Place</b> runs on every cut and every repaint, before the props and NavMesh are baked, under a fresh site
	/// object: it builds from the stored shapes and must write any asset it makes in place, so a repaint replaces rather
	/// than duplicates. A repaint never calls Plan again: the ground already carries it.
	/// </para>
	/// </remarks>
	public interface IPointOfInterestShaper
	{
		bool Handles(POIType kind);
		void Plan(PointOfInterestPlanContext context, PointOfInterestRecord site);
		void Place(PointOfInterestPlaceContext context, PointOfInterestRecord site);
	}

	/// <summary>
	/// The registered shapers. Register from an <c>[InitializeOnLoad]</c> static constructor; the first registered shaper
	/// that handles a kind takes its sites.
	/// </summary>
	public static class PointOfInterestShapers
	{
		private static readonly List<IPointOfInterestShaper> shapers = new List<IPointOfInterestShaper>();

		public static IReadOnlyList<IPointOfInterestShaper> All => shapers;

		public static void Register(IPointOfInterestShaper shaper)
		{
			if (shaper != null && !shapers.Contains(shaper))
			{
				shapers.Add(shaper);
			}
		}

		public static void Unregister(IPointOfInterestShaper shaper) => shapers.Remove(shaper);

		/// <summary>The shaper that takes a kind's sites, or null.</summary>
		public static IPointOfInterestShaper For(POIType kind)
		{
			foreach (IPointOfInterestShaper shaper in shapers)
			{
				if (shaper.Handles(kind))
				{
					return shaper;
				}
			}
			return null;
		}

		public static bool Handles(POIType kind) => For(kind) != null;
	}

	/// <summary>What a shaper's <see cref="IPointOfInterestShaper.Plan"/> is given.</summary>
	public sealed class PointOfInterestPlanContext
	{
		public Scene Scene;
		public SceneGenerationRequest Request;
		public TerrainTilePlan Tiles;
		/// <summary>The scene's terrain tiles, [x, z]; edit through <see cref="PointOfInterestTerrain"/>.</summary>
		public Terrain[,] Terrains;
		/// <summary>The scene's rivers and lakes; may be null.</summary>
		public SceneWater Water;
		/// <summary>The plan being made: its records, pads, keep-outs and shapes.</summary>
		public PointOfInterestPlan Plan;
		/// <summary>The scene's POI seed.</summary>
		public int Seed;
		/// <summary>The ground at (east, north) as the tiles now stand, scene metres.</summary>
		public Func<float, float, float> GroundAt;
		/// <summary>Rock hardness 0 … 1 at (east, north, altitude); may be null.</summary>
		public Func<float, float, float, float> HardnessAt;
		public List<string> Notes;
		internal readonly HashSet<int> Rejected = new HashSet<int>();

		/// <summary>An RNG for a site, fresh each call, seeded from its <see cref="PointOfInterestRecord.SiteSeed"/> and a salt.</summary>
		public DeterministicRNG RandomFor(PointOfInterestRecord site, int salt = 0)
			=> new DeterministicRNG((int)PointOfInterestPlanner.Mix(unchecked((uint)site.SiteSeed ^ (uint)(salt * 0x9E3779B1u))));

		/// <summary>The face score where a site stands (<see cref="PointOfInterestPlanner.FaceScore"/>), on the ground as it now is.</summary>
		public float FaceScore(float x, float z, float minHardness, out float yaw)
			=> PointOfInterestPlanner.FaceScore(GroundAt, HardnessAt, x, z, minHardness, out yaw);

		/// <summary>Adds a disc nothing else (scatter, cliffs, boulders) is placed in.</summary>
		public void AddKeepOut(PointOfInterestRecord site, float x, float z, float radius)
			=> Plan.KeepOuts.Add(new PointOfInterestKeepOut { Id = site.Id, X = x, Z = z, Radius = radius });

		/// <summary>Keeps what the shaper decided for a site with the plan (and so in the scene's POI asset).</summary>
		public void AddShape(PointOfInterestShape shape)
		{
			if (shape != null)
			{
				Plan.Shapes.Add(shape);
			}
		}

		/// <summary>Drops a site the shaper cannot build: it leaves the plan (records, pads, keep-outs, shapes) before naming.</summary>
		public void Reject(PointOfInterestRecord site, string why)
		{
			if (site != null && Rejected.Add(site.Id))
			{
				Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}) dropped: {why}");
			}
		}
	}

	/// <summary>What a shaper's <see cref="IPointOfInterestShaper.Place"/> is given.</summary>
	public sealed class PointOfInterestPlaceContext
	{
		public Scene Scene;
		public SceneGenerationRequest Request;
		public TerrainTilePlan Tiles;
		public Terrain[,] Terrains;
		public SceneWater Water;
		/// <summary>The scene's POI asset (records, pads, keep-outs, shapes).</summary>
		public ScenePointsOfInterest Points;
		/// <summary>The site's object; build under it. Visuals that are not props go under a ClientOnlyObject child.</summary>
		public Transform Root;
		/// <summary>The scene's terrain folder: write meshes here, in place, named after the site's id.</summary>
		public string TerrainFolder;
		public Func<float, float, float> GroundAt;
		public List<string> Notes;
		internal PointOfInterestPropSink Sink;

		/// <summary>The shapes stored for a site.</summary>
		public List<PointOfInterestShape> ShapesOf(PointOfInterestRecord site)
		{
			var list = new List<PointOfInterestShape>();
			if (Points != null && Points.Shapes != null)
			{
				foreach (PointOfInterestShape shape in Points.Shapes)
				{
					if (shape != null && shape.SiteId == site.Id)
					{
						list.Add(shape);
					}
				}
			}
			return list;
		}

		/// <summary>Adds an instanced prop (with streamed collision) to the scene's POI prop set.</summary>
		public void AddProp(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Sink?.Add(prefab, position, rotation, scale);
	}
}
#endif
