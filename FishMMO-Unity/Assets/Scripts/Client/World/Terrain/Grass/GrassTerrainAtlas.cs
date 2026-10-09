using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// Every grass terrain's GPU data in one place, so FishGrassBlades.compute generates all terrains of a camera in ONE
	/// dispatch (one per resolution group, normally one) with bindings and constants that never change between terrains:
	/// the heights, density and surface-layer textures as slices of texture arrays, and each terrain's parameters in a
	/// table at the head of one shared buffer, followed by the albedo copies of the array sets in use
	/// (<see cref="GrassAlbedoTexels"/>). A work item carries its terrain's index into the table.
	/// </summary>
	/// <remarks>
	/// It was one dispatch per terrain, rebinding textures and re-uploading constants between them (23 a camera on
	/// Baoakraal). On OpenGL that disturbed the other GPU-driven renderers' culling: trees and cliff rocks flickered
	/// until the procedural grass was switched off (their own work was clean). The grass's own flicker had already been
	/// traced to constant arrays varied per dispatch. Vulkan was unaffected either way.
	/// </remarks>
	public sealed class GrassTerrainAtlas : IDisposable
	{
		/// <summary>Uints per terrain in the table (FishGrassBlades.compute GRASS_TERRAIN_UINTS; GrassLoadTerrain reads it).</summary>
		public const int TerrainUints = 25;

		/// <summary>Terrains whose textures share resolutions (and so one set of arrays and one dispatch).</summary>
		public sealed class Group
		{
			public int HeightResolution, DensityResolution, SurfaceResolution;
			public Texture2DArray Heights, Density0, Density1, Surface;
			/// <summary>The water-line maps (<see cref="GrassTerrain.Freeboard"/>) of the group's terrains that have one, a slice each; one unread 1×1 slice when none does.</summary>
			public Texture2DArray Water;
			public readonly List<GrassTerrain> Terrains = new List<GrassTerrain>();
		}

		public readonly List<Group> Groups = new List<Group>();
		public GraphicsBuffer Shared { get; private set; }

		private readonly Dictionary<GrassTerrain, int> index = new Dictionary<GrassTerrain, int>();
		private readonly Dictionary<GrassTerrain, Group> groupOf = new Dictionary<GrassTerrain, Group>();
		/// <summary>What the atlas was built from: per terrain itself, its array set and that set's albedo copy.</summary>
		private readonly List<object> builtFrom = new List<object>(), current = new List<object>();
		private readonly List<(Object Texture, int Frame)> retiredTextures = new List<(Object, int)>();
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retiredBuffers = new List<(GraphicsBuffer, int)>();
		private readonly Dictionary<int, Texture2D> blacks = new Dictionary<int, Texture2D>();

		public int IndexOf(GrassTerrain terrain) => index.TryGetValue(terrain, out int i) ? i : -1;

		/// <summary>
		/// Rebuilds when the terrains, their array sets or the albedo copies changed (a terrain taken or released; a copy
		/// built). <paramref name="albedoFor"/> returns a set's copy, or null while it is not built yet. Returns true when
		/// the atlas is usable.
		/// </summary>
		public bool Sync(IReadOnlyList<GrassTerrain> terrains, Func<FishMMO.Shared.TerrainArraySet, GrassAlbedoTexels> albedoFor)
		{
			ReleaseRetired(false);
			current.Clear();
			for (int t = 0; t < terrains.Count; t++)
			{
				GrassTerrain gt = terrains[t];
				if (!Usable(gt))
				{
					continue;
				}
				current.Add(gt);
				current.Add(gt.Arrays);
				current.Add(gt.Arrays != null ? albedoFor(gt.Arrays) : null);
			}
			if (Shared != null && Same(current, builtFrom))
			{
				return Groups.Count > 0;
			}
			Rebuild(terrains, albedoFor);
			builtFrom.Clear();
			builtFrom.AddRange(current);
			return Groups.Count > 0;
		}

		private static bool Usable(GrassTerrain gt) => gt != null && gt.Heightmap != null && gt.Density0 != null;

		private static bool Same(List<object> a, List<object> b)
		{
			if (a.Count != b.Count)
			{
				return false;
			}
			for (int i = 0; i < a.Count; i++)
			{
				if (!ReferenceEquals(a[i], b[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Counts the rebuilds: anything cached against the groups (the blade renderer's work list) is stale when it moves.</summary>
		public int Version { get; private set; }

		private void Rebuild(IReadOnlyList<GrassTerrain> terrains, Func<FishMMO.Shared.TerrainArraySet, GrassAlbedoTexels> albedoFor)
		{
			Version++;
			RetireAll();
			index.Clear();
			groupOf.Clear();

			// Group by resolutions, index in list order.
			var ordered = new List<GrassTerrain>();
			for (int t = 0; t < terrains.Count; t++)
			{
				GrassTerrain gt = terrains[t];
				if (!Usable(gt))
				{
					continue;
				}
				int surface = gt.SurfaceLayers != null ? gt.SurfaceLayers.width : 0;
				Group group = null;
				foreach (Group g in Groups)
				{
					if (g.HeightResolution == gt.Heightmap.width && g.DensityResolution == gt.Density0.width && g.SurfaceResolution == surface)
					{
						group = g;
						break;
					}
				}
				if (group == null)
				{
					group = new Group { HeightResolution = gt.Heightmap.width, DensityResolution = gt.Density0.width, SurfaceResolution = surface };
					Groups.Add(group);
				}
				group.Terrains.Add(gt);
				groupOf[gt] = group;
				index[gt] = ordered.Count;
				ordered.Add(gt);
			}
			if (ordered.Count == 0)
			{
				return;
			}

			// The albedo copies after the table, each once.
			var albedoBase = new Dictionary<GrassAlbedoTexels, int>();
			int total = ordered.Count * TerrainUints;
			foreach (GrassTerrain gt in ordered)
			{
				GrassAlbedoTexels texels = gt.Arrays != null && gt.Arrays.IsUsable && gt.SurfaceLayers != null ? albedoFor(gt.Arrays) : null;
				if (texels != null && !albedoBase.ContainsKey(texels))
				{
					albedoBase[texels] = total;
					total += texels.Data.Length;
				}
			}
			var data = new uint[Math.Max(1, total)];
			foreach (GrassTerrain gt in ordered)
			{
				Group g = groupOf[gt];
				int slice = g.Terrains.IndexOf(gt);
				GrassAlbedoTexels texels = gt.Arrays != null && gt.Arrays.IsUsable && gt.SurfaceLayers != null ? albedoFor(gt.Arrays) : null;
				int at = index[gt] * TerrainUints;
				// 0..3 origin xyz, metres per height unit; 4..7 size x, z, heightmap resolution, density resolution.
				Put(data, at + 0, gt.Origin.x); Put(data, at + 1, gt.Origin.y); Put(data, at + 2, gt.Origin.z); Put(data, at + 3, gt.HeightScale);
				Put(data, at + 4, gt.Size.x); Put(data, at + 5, gt.Size.z); Put(data, at + 6, gt.HeightResolution); Put(data, at + 7, gt.DensityResolution);
				// 8..15 the density channels' type indices (-1 none).
				for (int c = 0; c < GrassTerrain.MaxChannels; c++)
				{
					Put(data, at + 8 + c, gt.ChannelTypes[c]);
				}
				// 16 slice, 17 has a second density map, 18 surface resolution, 19 colour layers (0: none);
				// 20 albedo width, 21 albedo mips, 22 texels per layer, 23 the copy's first uint (raw);
				// 24 its water-line slice (raw; 0xFFFFFFFF none).
				data[at + 16] = (uint)slice;
				data[at + 17] = gt.Density1 != null ? 1u : 0u;
				Put(data, at + 18, g.SurfaceResolution);
				Put(data, at + 19, texels != null ? Mathf.Min(gt.Arrays.LayerCount, texels.Layers) : 0);
				Put(data, at + 20, texels != null ? texels.Width : 1);
				Put(data, at + 21, texels != null ? texels.Mips : 1);
				Put(data, at + 22, texels != null ? texels.LayerTexels : 0);
				data[at + 23] = texels != null ? (uint)albedoBase[texels] : 0u;
				data[at + 24] = gt.Freeboard != null ? (uint)WaterSliceOf(g, gt) : 0xFFFFFFFFu;
			}
			foreach (KeyValuePair<GrassAlbedoTexels, int> a in albedoBase)
			{
				Array.Copy(a.Key.Data, 0, data, a.Value, a.Key.Data.Length);
			}
			Shared = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, sizeof(uint)) { name = "Grass terrain atlas (table + albedo)" };
			Shared.SetData(data);

			// The texture arrays, slice by slice on the GPU.
			foreach (Group g in Groups)
			{
				int n = g.Terrains.Count;
				GrassTerrain first = g.Terrains[0];
				g.Heights = NewArray(g.HeightResolution, n, ((Texture2D)first.Heightmap).format, "heights");
				g.Density0 = NewArray(g.DensityResolution, n, TextureFormat.RGBA32, "density 0-3");
				g.Density1 = NewArray(g.DensityResolution, n, TextureFormat.RGBA32, "density 4-7");
				g.Surface = NewArray(Math.Max(1, g.SurfaceResolution), n, TextureFormat.RGBA32, "surface layers");
				int wet = 0;
				foreach (GrassTerrain gt in g.Terrains)
				{
					if (gt.Freeboard != null)
					{
						wet++;
					}
				}
				g.Water = NewArray(wet > 0 ? g.HeightResolution : 1, Math.Max(1, wet), TextureFormat.R8, "water line");
				if (wet == 0)
				{
					g.Water.Apply(false, false);
				}
				for (int s = 0; s < n; s++)
				{
					GrassTerrain gt = g.Terrains[s];
					Graphics.CopyTexture(gt.Heightmap, 0, 0, g.Heights, s, 0);
					Graphics.CopyTexture(gt.Density0, 0, 0, g.Density0, s, 0);
					Graphics.CopyTexture(gt.Density1 != null ? gt.Density1 : Black(g.DensityResolution), 0, 0, g.Density1, s, 0);
					Graphics.CopyTexture(gt.SurfaceLayers != null ? gt.SurfaceLayers : Black(Math.Max(1, g.SurfaceResolution)), 0, 0, g.Surface, s, 0);
					if (gt.Freeboard != null)
					{
						Graphics.CopyTexture(gt.Freeboard, 0, 0, g.Water, WaterSliceOf(g, gt), 0);
					}
				}
			}
		}

		/// <summary>A wet terrain's slice in its group's <see cref="Group.Water"/>: its rank among the group's terrains that have a water line.</summary>
		private static int WaterSliceOf(Group g, GrassTerrain terrain)
		{
			int slice = 0;
			foreach (GrassTerrain gt in g.Terrains)
			{
				if (gt == terrain)
				{
					return slice;
				}
				if (gt.Freeboard != null)
				{
					slice++;
				}
			}
			return -1;
		}

		private static void Put(uint[] data, int at, float value) => data[at] = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);

		private static Texture2DArray NewArray(int resolution, int slices, TextureFormat format, string what)
		{
			return new Texture2DArray(resolution, resolution, slices, format, false, true)
			{
				name = $"Grass {what} ({slices} terrain(s))",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
		}

		/// <summary>An all-zero RGBA32 square: the slice of a terrain with no second density map or no surface layers.</summary>
		private Texture2D Black(int resolution)
		{
			if (!blacks.TryGetValue(resolution, out Texture2D black) || black == null)
			{
				black = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true) { name = "Grass atlas black", hideFlags = HideFlags.DontSave };
				black.SetPixelData(new byte[resolution * resolution * 4], 0);
				black.Apply(false, true);
				blacks[resolution] = black;
			}
			return black;
		}

		/// <summary>The old arrays and buffer may still be read by a frame in flight: released a few frames later.</summary>
		private void RetireAll()
		{
			int frame = Time.frameCount;
			foreach (Group g in Groups)
			{
				foreach (Texture2DArray a in new[] { g.Heights, g.Density0, g.Density1, g.Surface, g.Water })
				{
					if (a != null)
					{
						retiredTextures.Add((a, frame));
					}
				}
			}
			Groups.Clear();
			if (Shared != null)
			{
				retiredBuffers.Add((Shared, frame));
				Shared = null;
			}
		}

		private void ReleaseRetired(bool all)
		{
			int frame = Time.frameCount;
			for (int i = retiredTextures.Count - 1; i >= 0; i--)
			{
				if (all || frame - retiredTextures[i].Frame >= 4)
				{
					Object.Destroy(retiredTextures[i].Texture);
					retiredTextures.RemoveAt(i);
				}
			}
			for (int i = retiredBuffers.Count - 1; i >= 0; i--)
			{
				if (all || frame - retiredBuffers[i].Frame >= 4)
				{
					retiredBuffers[i].Buffer.Release();
					retiredBuffers.RemoveAt(i);
				}
			}
		}

		public void Dispose()
		{
			RetireAll();
			ReleaseRetired(true);
			foreach (Texture2D black in blacks.Values)
			{
				if (black != null)
				{
					Object.Destroy(black);
				}
			}
			blacks.Clear();
			index.Clear();
			groupOf.Clear();
			builtFrom.Clear();
		}
	}
}
