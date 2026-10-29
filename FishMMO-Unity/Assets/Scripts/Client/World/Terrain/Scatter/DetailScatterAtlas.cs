using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// Every scattered terrain's GPU data in one place, as the blade grass keeps it (<see cref="GrassTerrainAtlas"/>), so
	/// FishDetailScatter.compute generates all terrains of a camera in ONE dispatch per resolution group with bindings and
	/// constants that never change between terrains: the heights and holes as slices of texture arrays, and one table
	/// buffer holding each terrain's parameters (and its channels' placement settings) and its sparse density (2026-10-27:
	/// item headers and entries, <see cref="DetailScatterDensity"/>; the dense half-float slices of eight channels it
	/// replaced cost 72 MB on Moron 2 for a fraction of its kinds). A work item carries its terrain's index. One dispatch
	/// per terrain, rebinding and re-uploading constants between them, disturbed other GPU-driven renderers on OpenGL; the
	/// grass learned it the hard way.
	/// </summary>
	/// <remarks>
	/// The table: <c>[terrain count]</c> each terrain's row offset; the rows (<see cref="HeaderUints"/> then
	/// <see cref="ChannelUints"/> per channel); then per terrain its item headers (<see cref="DetailScatterMath.ItemHeaderUints"/>
	/// an item) and its entries. One storage buffer, so the generate kernel stays at seven (WebGPU allows eight a stage).
	/// </remarks>
	public sealed class DetailScatterAtlas : IDisposable
	{
		/// <summary>Uints of a terrain row's header (FishDetailScatter.compute SCATTER_TERRAIN_HEADER; ScatterLoadTerrain reads it).</summary>
		public const int HeaderUints = 20;

		/// <summary>Uints per channel in a terrain's row, after the header (SCATTER_CHANNEL_UINTS).</summary>
		public const int ChannelUints = 10;

		/// <summary>
		/// The most uints the table may hold: WebGPU's default largest storage binding (128 MiB). A scene past it is refused
		/// with an error (the cut reports it too): more terrains than this scatters would need splitting.
		/// </summary>
		public const long MaxTableUints = 128L * 1024 * 1024 / 4;

		/// <summary>The uints of one terrain's row.</summary>
		public static int RowUints(DetailScatterTerrain st) => HeaderUints + st.Channels * ChannelUints;

		/// <summary>The uints of one terrain's density: its item headers and its entries.</summary>
		public static long DensityUints(DetailScatterTerrain st) => (long)st.ItemsSide * st.ItemsSide * DetailScatterMath.ItemHeaderUints + st.Entries.Length;

		/// <summary>Terrains whose textures share resolutions (and so one set of arrays and one dispatch).</summary>
		public sealed class Group
		{
			public int HeightResolution;
			/// <summary>Holes: a slice per terrain beside its height slice (same index), solid for a terrain with no hole.</summary>
			public Texture Heights, Holes;
			public readonly List<DetailScatterTerrain> Terrains = new List<DetailScatterTerrain>();
		}

		public readonly List<Group> Groups = new List<Group>();

		/// <summary>Counts every rebuild and table rewrite: generated instances from before a change are stale (DetailScatterRenderer's reuse).</summary>
		public int Version { get; private set; }
		public GraphicsBuffer Table { get; private set; }

		private readonly Dictionary<DetailScatterTerrain, int> index = new Dictionary<DetailScatterTerrain, int>();
		private readonly List<DetailScatterTerrain> builtFrom = new List<DetailScatterTerrain>();
		private readonly List<(Object Texture, int Frame)> retiredTextures = new List<(Object, int)>();
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retiredBuffers = new List<(GraphicsBuffer, int)>();
		private readonly Dictionary<int, Texture2D> solids = new Dictionary<int, Texture2D>();

		public int IndexOf(DetailScatterTerrain terrain) => index.TryGetValue(terrain, out int i) ? i : -1;

		/// <summary>Rebuilds when the terrains changed (one taken or released). Returns true when the atlas is usable.</summary>
		public bool Sync(IReadOnlyList<DetailScatterTerrain> terrains, Func<DetailScatterTerrain, float> drawDistance)
		{
			ReleaseRetired(false);
			bool same = Table != null && builtFrom.Count == terrains.Count;
			for (int i = 0; same && i < terrains.Count; i++)
			{
				same = ReferenceEquals(builtFrom[i], terrains[i]);
			}
			bool lost = same && AnyLost();
			if (lost)
			{
				Debug.Log("[Detail scatter] The scatter atlas's texture arrays were released by the engine (their maps live only on the GPU); built again.");
			}
			if (!same || lost)
			{
				Rebuild(terrains);
				builtFrom.Clear();
				builtFrom.AddRange(terrains);
			}
			// The draw distances follow the player's setting: rewritten when one changed.
			if (Table != null && DistancesChanged(drawDistance))
			{
				WriteTable(drawDistance);
			}
			return Groups.Count > 0;
		}

		private bool AnyLost()
		{
			foreach (Group g in Groups)
			{
				if (TerrainAtlasArrays.Lost(g.Heights) || TerrainAtlasArrays.Lost(g.Holes))
				{
					return true;
				}
			}
			return false;
		}

		private readonly List<float> distances = new List<float>();
		private readonly List<DetailScatterTerrain> ordered = new List<DetailScatterTerrain>();

		private bool DistancesChanged(Func<DetailScatterTerrain, float> drawDistance)
		{
			if (distances.Count != ordered.Count)
			{
				return true;
			}
			for (int i = 0; i < ordered.Count; i++)
			{
				if (distances[i] != drawDistance(ordered[i]))
				{
					return true;
				}
			}
			return false;
		}

		private static bool Usable(DetailScatterTerrain st) => st != null && st.Heightmap != null && st.Any;

		/// <summary>Bytes of the table on the GPU (its rows and every terrain's sparse density).</summary>
		public long TableBytes => Table != null ? (long)Table.count * 4 : 0;

		/// <summary>Where each terrain's row starts, and where its density starts (both uint offsets in the table).</summary>
		private readonly List<int> rowAt = new List<int>(), densityAt = new List<int>();
		private uint[] tableData = Array.Empty<uint>();
		private bool warnedTable;

		private void Rebuild(IReadOnlyList<DetailScatterTerrain> terrains)
		{
			Version++;
			RetireAll();
			index.Clear();
			ordered.Clear();
			distances.Clear();
			for (int t = 0; t < terrains.Count; t++)
			{
				DetailScatterTerrain st = terrains[t];
				if (!Usable(st))
				{
					continue;
				}
				Group group = null;
				foreach (Group g in Groups)
				{
					if (g.HeightResolution == st.Heightmap.width)
					{
						group = g;
						break;
					}
				}
				if (group == null)
				{
					group = new Group { HeightResolution = st.Heightmap.width };
					Groups.Add(group);
				}
				group.Terrains.Add(st);
				index[st] = ordered.Count;
				ordered.Add(st);
			}
			if (ordered.Count == 0)
			{
				return;
			}
			// The table's layout: the offsets, every row, then every terrain's density (headers and entries).
			rowAt.Clear();
			densityAt.Clear();
			long at = ordered.Count;
			foreach (DetailScatterTerrain st in ordered)
			{
				rowAt.Add((int)at);
				at += RowUints(st);
			}
			foreach (DetailScatterTerrain st in ordered)
			{
				densityAt.Add((int)Math.Min(at, int.MaxValue));
				at += DensityUints(st);
			}
			if (at > MaxTableUints)
			{
				if (!warnedTable)
				{
					warnedTable = true;
					Debug.LogError($"[Detail scatter] The scene's scatter table needs {at * 4 / 1048576.0:F0} MiB, over the {MaxTableUints * 4 / 1048576} MiB a storage buffer may bind on WebGPU: no mesh detail is drawn. Fewer painted kinds per 2 m, or fewer terrains in one scene.");
				}
				Groups.Clear();
				index.Clear();
				ordered.Clear();
				return;
			}
			tableData = new uint[at];
			for (int t = 0; t < ordered.Count; t++)
			{
				DetailScatterTerrain st = ordered[t];
				tableData[t] = (uint)rowAt[t];
				int headers = densityAt[t];
				uint entryBase = (uint)(headers + st.ItemsSide * st.ItemsSide * DetailScatterMath.ItemHeaderUints);
				DetailScatterDensity.WriteHeaders(st.ItemEntryStart, st.BlockStarts, entryBase, tableData, headers);
				Array.Copy(st.Entries, 0, tableData, entryBase, st.Entries.Length);
			}
			Table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(1, tableData.Length), sizeof(uint)) { name = "Detail scatter table" };

			// The texture arrays, slice by slice on the GPU: one height slice and one holes slice a terrain.
			foreach (Group g in Groups)
			{
				int n = g.Terrains.Count;
				g.Heights = NewArray(g.HeightResolution, n, ((Texture2D)g.Terrains[0].Heightmap).format, "heights");
				g.Holes = NewArray(g.HeightResolution, n, TextureFormat.R8, "holes");
				for (int s = 0; s < n; s++)
				{
					DetailScatterTerrain st = g.Terrains[s];
					Graphics.CopyTexture(st.Heightmap, 0, 0, g.Heights, s, 0);
					Graphics.CopyTexture(st.Holes != null ? st.Holes : Solid(g.HeightResolution), 0, 0, g.Holes, s, 0);
				}
			}
			// The rows are written (and the whole table uploaded) by the WriteTable that follows every rebuild.
			distances.Clear();
		}

		/// <summary>Writes every terrain's row (its frame, its slices, its draw distance and its channels) and uploads the table.</summary>
		private void WriteTable(Func<DetailScatterTerrain, float> drawDistance)
		{
			Version++;
			uint[] data = tableData;
			bool whole = distances.Count == 0;
			distances.Clear();
			foreach (Group g in Groups)
			{
				for (int s = 0; s < g.Terrains.Count; s++)
				{
					DetailScatterTerrain st = g.Terrains[s];
					int at = rowAt[index[st]];
					float distance = drawDistance(st);
					// 0..3 origin xyz, metres per height unit; 4..7 size x, z, heightmap resolution, blocks per side.
					Put(data, at + 0, st.Origin.x); Put(data, at + 1, st.Origin.y); Put(data, at + 2, st.Origin.z); Put(data, at + 3, st.HeightScale);
					Put(data, at + 4, st.Size.x); Put(data, at + 5, st.Size.z); Put(data, at + 6, st.HeightResolution); Put(data, at + 7, st.Blocks);
					// 8..11 block size x, z (m), height slice (raw), its item headers' offset in the table (raw); 12 channels
					// (raw), 13..14 world index of block 0 on x and z (raw int), 15 the draw distance (m); 16 items a side,
					// 17 detail cells a block side (the painted-cell masks), 18..19 unused.
					Put(data, at + 8, st.BlockX); Put(data, at + 9, st.BlockZ);
					data[at + 10] = (uint)s;
					data[at + 11] = (uint)densityAt[index[st]];
					data[at + 12] = (uint)st.Channels;
					data[at + 13] = unchecked((uint)st.WorldBlockX);
					data[at + 14] = unchecked((uint)st.WorldBlockZ);
					Put(data, at + 15, distance);
					data[at + 16] = (uint)st.ItemsSide;
					data[at + 17] = (uint)st.Factor;
					data[at + 18] = 0u;
					data[at + 19] = 0u;
					for (int c = 0; c < st.Channels; c++)
					{
						DetailScatterTerrain.Channel ch = st.ChannelSettings[c];
						int cb = at + HeaderUints + c * ChannelUints;
						// type (raw), width min/max, height min/max, noise spread, seed (raw), align to ground.
						data[cb + 0] = unchecked((uint)ch.Type);
						Put(data, cb + 1, ch.MinWidth); Put(data, cb + 2, ch.MaxWidth);
						Put(data, cb + 3, ch.MinHeight); Put(data, cb + 4, ch.MaxHeight);
						Put(data, cb + 5, ch.NoiseSpread);
						data[cb + 6] = unchecked((uint)ch.Seed);
						Put(data, cb + 7, ch.AlignToGround);
						// 8..9 the scatter modes' two words (BiomeScatterModeWord; 0 and 0 = every mode off).
						data[cb + 8] = ch.ModeWord;
						data[cb + 9] = ch.ModeAngles;
					}
				}
			}
			foreach (DetailScatterTerrain st in ordered)
			{
				distances.Add(drawDistance(st));
			}
			// After a rebuild the whole table (rows and density); after a distance change only the rows, which lead it.
			int rows = densityAt.Count > 0 ? densityAt[0] : data.Length;
			Table.SetData(data, 0, 0, whole ? data.Length : rows);
		}

		private static void Put(uint[] data, int at, float value) => data[at] = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);

		/// <summary>A GPU-only array (TerrainAtlasArrays: no system-memory copy, nothing uploaded), linear as the maps are.</summary>
		private static Texture NewArray(int resolution, int slices, TextureFormat format, string what)
		{
			return TerrainAtlasArrays.NewArray(resolution, slices, TerrainAtlasArrays.Linear(format), $"Detail scatter {what} ({slices} slice(s))");
		}

		/// <summary>An all-solid R8 square: the holes slice of a terrain with no hole.</summary>
		private Texture2D Solid(int resolution)
		{
			if (!solids.TryGetValue(resolution, out Texture2D solid) || solid == null)
			{
				var texels = new byte[resolution * resolution];
				for (int i = 0; i < texels.Length; i++)
				{
					texels[i] = 255;
				}
				solid = new Texture2D(resolution, resolution, TextureFormat.R8, false, true) { name = "Detail scatter solid", hideFlags = HideFlags.DontSave };
				solid.SetPixelData(texels, 0);
				solid.Apply(false, true);
				solids[resolution] = solid;
			}
			return solid;
		}

		/// <summary>The old arrays and table may still be read by a frame in flight: released a few frames later.</summary>
		private void RetireAll()
		{
			int frame = Time.frameCount;
			foreach (Group g in Groups)
			{
				if (g.Heights != null)
				{
					retiredTextures.Add((g.Heights, frame));
				}
				if (g.Holes != null)
				{
					retiredTextures.Add((g.Holes, frame));
				}
			}
			Groups.Clear();
			if (Table != null)
			{
				retiredBuffers.Add((Table, frame));
				Table = null;
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
			foreach (Texture2D solid in solids.Values)
			{
				if (solid != null)
				{
					Object.Destroy(solid);
				}
			}
			solids.Clear();
			index.Clear();
			ordered.Clear();
			distances.Clear();
			builtFrom.Clear();
		}
	}
}
