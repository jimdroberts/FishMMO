using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// Every scattered terrain's GPU data in one place, as the blade grass keeps it (<see cref="GrassTerrainAtlas"/>), so
	/// FishDetailScatter.compute generates all terrains of a camera in ONE dispatch per resolution group with bindings and
	/// constants that never change between terrains: the heights and the density slices as slices of texture arrays, and
	/// each terrain's parameters (and its channels' placement settings) in one table. A work item carries its terrain's
	/// row. One dispatch per terrain, rebinding and re-uploading constants between them, disturbed other GPU-driven
	/// renderers on OpenGL; the grass learned it the hard way.
	/// </summary>
	public sealed class DetailScatterAtlas : IDisposable
	{
		/// <summary>Uints per terrain in the table (FishDetailScatter.compute SCATTER_TERRAIN_UINTS; ScatterLoadTerrain reads it).</summary>
		public const int TerrainUints = 96;

		/// <summary>Uints per channel in a terrain's row, from <see cref="ChannelBase"/> (SCATTER_CHANNEL_UINTS).</summary>
		public const int ChannelUints = 10;

		public const int ChannelBase = 16;

		/// <summary>Terrains whose textures share resolutions (and so one set of arrays and one dispatch).</summary>
		public sealed class Group
		{
			public int HeightResolution, Blocks;
			public Texture2DArray Heights, Density;
			public readonly List<DetailScatterTerrain> Terrains = new List<DetailScatterTerrain>();
		}

		public readonly List<Group> Groups = new List<Group>();
		public GraphicsBuffer Table { get; private set; }

		private readonly Dictionary<DetailScatterTerrain, int> index = new Dictionary<DetailScatterTerrain, int>();
		private readonly List<DetailScatterTerrain> builtFrom = new List<DetailScatterTerrain>();
		private readonly List<(Object Texture, int Frame)> retiredTextures = new List<(Object, int)>();
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retiredBuffers = new List<(GraphicsBuffer, int)>();
		private readonly Dictionary<int, Texture2D> blanks = new Dictionary<int, Texture2D>();

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
			if (!same)
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

		private static bool Usable(DetailScatterTerrain st) => st != null && st.Heightmap != null && st.Density0 != null;

		private void Rebuild(IReadOnlyList<DetailScatterTerrain> terrains)
		{
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
					if (g.HeightResolution == st.Heightmap.width && g.Blocks == st.Blocks)
					{
						group = g;
						break;
					}
				}
				if (group == null)
				{
					group = new Group { HeightResolution = st.Heightmap.width, Blocks = st.Blocks };
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
			Table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ordered.Count * TerrainUints, sizeof(uint)) { name = "Detail scatter terrain table" };

			// The texture arrays, slice by slice on the GPU: one height slice and two density slices a terrain.
			foreach (Group g in Groups)
			{
				int n = g.Terrains.Count;
				g.Heights = NewArray(g.HeightResolution, n, ((Texture2D)g.Terrains[0].Heightmap).format, "heights");
				g.Density = NewArray(g.Blocks, n * 2, TextureFormat.RGBAHalf, "density");
				for (int s = 0; s < n; s++)
				{
					DetailScatterTerrain st = g.Terrains[s];
					Graphics.CopyTexture(st.Heightmap, 0, 0, g.Heights, s, 0);
					Graphics.CopyTexture(st.Density0, 0, 0, g.Density, 2 * s, 0);
					Graphics.CopyTexture(st.Density1 != null ? st.Density1 : Blank(g.Blocks), 0, 0, g.Density, 2 * s + 1, 0);
				}
			}
		}

		/// <summary>Writes every terrain's row: its frame, its slices, its draw distance and its channels.</summary>
		private void WriteTable(Func<DetailScatterTerrain, float> drawDistance)
		{
			var data = new uint[ordered.Count * TerrainUints];
			distances.Clear();
			foreach (Group g in Groups)
			{
				for (int s = 0; s < g.Terrains.Count; s++)
				{
					DetailScatterTerrain st = g.Terrains[s];
					int at = index[st] * TerrainUints;
					float distance = drawDistance(st);
					// 0..3 origin xyz, metres per height unit; 4..7 size x, z, heightmap resolution, blocks per side.
					Put(data, at + 0, st.Origin.x); Put(data, at + 1, st.Origin.y); Put(data, at + 2, st.Origin.z); Put(data, at + 3, st.HeightScale);
					Put(data, at + 4, st.Size.x); Put(data, at + 5, st.Size.z); Put(data, at + 6, st.HeightResolution); Put(data, at + 7, st.Blocks);
					// 8..11 block size x, z (m), height slice, first density slice (raw); 12 channels (raw), 13..14 world
					// index of block 0 on x and z (raw int), 15 the draw distance (m).
					Put(data, at + 8, st.BlockX); Put(data, at + 9, st.BlockZ);
					data[at + 10] = (uint)s;
					data[at + 11] = (uint)(2 * s);
					data[at + 12] = (uint)st.Channels;
					data[at + 13] = unchecked((uint)st.WorldBlockX);
					data[at + 14] = unchecked((uint)st.WorldBlockZ);
					Put(data, at + 15, distance);
					for (int c = 0; c < DetailScatterMath.MaxChannels; c++)
					{
						DetailScatterTerrain.Channel ch = st.ChannelSettings[c];
						int cb = at + ChannelBase + c * ChannelUints;
						// type (raw), width min/max, height min/max, noise spread, seed (raw), align to ground.
						data[cb + 0] = unchecked((uint)(c < st.Channels ? ch.Type : -1));
						Put(data, cb + 1, ch.MinWidth); Put(data, cb + 2, ch.MaxWidth);
						Put(data, cb + 3, ch.MinHeight); Put(data, cb + 4, ch.MaxHeight);
						Put(data, cb + 5, ch.NoiseSpread);
						data[cb + 6] = unchecked((uint)ch.Seed);
						Put(data, cb + 7, ch.AlignToGround);
					}
				}
			}
			foreach (DetailScatterTerrain st in ordered)
			{
				distances.Add(drawDistance(st));
			}
			Table.SetData(data);
		}

		private static void Put(uint[] data, int at, float value) => data[at] = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);

		private static Texture2DArray NewArray(int resolution, int slices, TextureFormat format, string what)
		{
			return new Texture2DArray(resolution, resolution, slices, format, false, true)
			{
				name = $"Detail scatter {what} ({slices} slice(s))",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
		}

		/// <summary>An all-zero half-float square: the second density slice of a terrain with four channels or fewer.</summary>
		private Texture2D Blank(int resolution)
		{
			if (!blanks.TryGetValue(resolution, out Texture2D blank) || blank == null)
			{
				blank = new Texture2D(resolution, resolution, TextureFormat.RGBAHalf, false, true) { name = "Detail scatter blank", hideFlags = HideFlags.DontSave };
				blank.SetPixelData(new ushort[resolution * resolution * 4], 0);
				blank.Apply(false, true);
				blanks[resolution] = blank;
			}
			return blank;
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
				if (g.Density != null)
				{
					retiredTextures.Add((g.Density, frame));
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
			foreach (Texture2D blank in blanks.Values)
			{
				if (blank != null)
				{
					Object.Destroy(blank);
				}
			}
			blanks.Clear();
			index.Clear();
			ordered.Clear();
			distances.Clear();
			builtFrom.Clear();
		}
	}
}
