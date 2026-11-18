using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldMaps
{
	/// <summary>
	/// Holds a scene at full detail for one overhead capture, and puts every setting back when
	/// disposed.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the map needs it.</b> Every level-of-detail rule in Unity assumes a camera near
	/// the ground. The map camera is orthographic, two kilometres up, framing a whole zone, so by
	/// those rules everything is tiny and far away:</para>
	/// <list type="bullet">
	/// <item><b>Trees</b> are LOD groups whose last level is a cross of two quads, a billboard that
	/// reads as a tree from the side. Seen from straight above it is a thin X — or nothing, once the
	/// tree is small enough on screen to be culled outright. An orthographic camera sizes a group
	/// against its own height in world units, so a ten-metre tree in a two-kilometre frame is half a
	/// percent of the screen. <see cref="QualitySettings.lodBias"/> is raised far enough that anything
	/// a metre across counts as filling the screen, so every group draws its first level; and
	/// <see cref="QualitySettings.maximumLODLevel"/> is held at 0 in case the quality level in use
	/// has dropped the first level.</item>
	/// <item><b>Terrain trees</b> stop at <see cref="Terrain.treeDistance"/> and are replaced by the
	/// terrain's own billboards past <see cref="Terrain.treeBillboardDistance"/>; both are measured
	/// from the camera, which is further than either from every tree in the scene.</item>
	/// <item><b>The terrain's surface</b> switches to its low-resolution base map past
	/// <see cref="Terrain.basemapDistance"/>, and simplifies its mesh by
	/// <see cref="Terrain.heightmapPixelError"/>. Both are pushed to full detail.</item>
	/// </list>
	/// <para><b>Detail layers (grass and the like) are not forced, deliberately.</b>
	/// <see cref="Terrain.detailObjectDistance"/> is clamped by Unity to 250 m and the camera is
	/// never closer than that to the ground, so no setting would draw them. That is accepted rather
	/// than worked round: the biome's ground texture already carries the colour the grass would add,
	/// a blade is far below a map pixel (2048 pixels across a zone is about a metre a pixel), and
	/// drawing tens of millions of instances for one frame would cost minutes per scene for nothing
	/// the map can show.</para>
	/// <para>Every value is read before it is written and written back in <see cref="Dispose"/>, in
	/// the spirit of the fog handling around the capture: these are global and per-scene settings,
	/// and a bake that left the editor's LOD bias at four thousand would be felt in every scene view
	/// afterwards.</para>
	/// </remarks>
	public sealed class WorldMapDetailOverride : IDisposable
	{
		/// <summary>
		/// LOD bias per metre of orthographic half-height: at this rate an object one metre across
		/// counts as filling the frame twice over, so every LOD group selects its first level.
		/// </summary>
		public const float LodBiasPerMetre = 4f;

		/// <summary>The most full-detail trees a terrain may draw; effectively all of them.</summary>
		public const int FullLodTrees = 1 << 30;

		private readonly float lodBias;
		private readonly int maximumLodLevel;
		private readonly List<TerrainSettings> terrains = new List<TerrainSettings>();
		private bool disposed;

		private struct TerrainSettings
		{
			public Terrain Terrain;
			public float TreeDistance;
			public float TreeBillboardDistance;
			public int TreeMaximumFullLODCount;
			public float BasemapDistance;
			public float HeightmapPixelError;
			public int HeightmapMaximumLOD;
		}

		private WorldMapDetailOverride()
		{
			lodBias = QualitySettings.lodBias;
			maximumLodLevel = QualitySettings.maximumLODLevel;
		}

		/// <summary>The LOD bias that draws every group at its first level in a frame this tall.</summary>
		/// <param name="orthographicSize">The camera's orthographic half-height, in metres.</param>
		public static float LodBiasFor(float orthographicSize) => Mathf.Max(1f, LodBiasPerMetre * orthographicSize);

		/// <summary>
		/// Forces full detail on the scene's terrains and on the LOD system, for a camera that sees
		/// <paramref name="reachMetres"/> at most. Never lowers a setting that is already higher.
		/// </summary>
		/// <param name="scene">The scene being captured.</param>
		/// <param name="reachMetres">The furthest anything in the frame can be from the camera.</param>
		/// <param name="orthographicSize">The camera's orthographic half-height, in metres.</param>
		public static WorldMapDetailOverride Apply(Scene scene, float reachMetres, float orthographicSize)
		{
			var held = new WorldMapDetailOverride();
			try
			{
				QualitySettings.lodBias = Mathf.Max(held.lodBias, LodBiasFor(orthographicSize));
				QualitySettings.maximumLODLevel = 0;

				if (scene.IsValid() && scene.isLoaded)
				{
					foreach (GameObject root in scene.GetRootGameObjects())
					{
						foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
						{
							held.Hold(terrain, reachMetres);
						}
					}
				}
			}
			catch
			{
				// Whatever was changed before the failure goes back before the exception leaves.
				held.Dispose();
				throw;
			}
			return held;
		}

		private void Hold(Terrain terrain, float reach)
		{
			terrains.Add(new TerrainSettings
			{
				Terrain = terrain,
				TreeDistance = terrain.treeDistance,
				TreeBillboardDistance = terrain.treeBillboardDistance,
				TreeMaximumFullLODCount = terrain.treeMaximumFullLODCount,
				BasemapDistance = terrain.basemapDistance,
				HeightmapPixelError = terrain.heightmapPixelError,
				HeightmapMaximumLOD = terrain.heightmapMaximumLOD,
			});
			terrain.treeDistance = Mathf.Max(terrain.treeDistance, reach);
			terrain.treeBillboardDistance = Mathf.Max(terrain.treeBillboardDistance, reach);
			terrain.treeMaximumFullLODCount = FullLodTrees;
			terrain.basemapDistance = Mathf.Max(terrain.basemapDistance, reach);
			terrain.heightmapPixelError = 1f;
			terrain.heightmapMaximumLOD = 0;
		}

		/// <summary>Puts back every value <see cref="Apply"/> changed. Safe to call twice.</summary>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			for (int i = terrains.Count - 1; i >= 0; i--)
			{
				TerrainSettings held = terrains[i];
				if (held.Terrain == null)
				{
					continue;
				}
				held.Terrain.treeDistance = held.TreeDistance;
				held.Terrain.treeBillboardDistance = held.TreeBillboardDistance;
				held.Terrain.treeMaximumFullLODCount = held.TreeMaximumFullLODCount;
				held.Terrain.basemapDistance = held.BasemapDistance;
				held.Terrain.heightmapPixelError = held.HeightmapPixelError;
				held.Terrain.heightmapMaximumLOD = held.HeightmapMaximumLOD;
			}
			terrains.Clear();
			QualitySettings.maximumLODLevel = maximumLodLevel;
			QualitySettings.lodBias = lodBias;
		}
	}
}
