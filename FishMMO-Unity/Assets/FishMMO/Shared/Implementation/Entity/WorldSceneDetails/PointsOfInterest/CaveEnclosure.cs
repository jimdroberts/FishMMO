using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where a carved cave's air is, as the lit surfaces' underground darkness reads it (2026-10-21): per column of the world's
	/// <see cref="Cell"/> grid under the cave, the lowest and highest of the rock's inner faces (the floor and the roof seen
	/// from inside) and how shut in from the daylight the column is, 0 at a mouth or under a skylight to 1 deep in. Baked by
	/// the cut from the cave's own rock, kept on its <see cref="CaveVisibility"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> How far under the ground a surface lies (FishGroundMap.hlsl FishUnderground, from the 5 m ground map) was
	/// all that kept the sky and the sun off a cave's rock: no sky from twenty metres in, all of it within six. A hall under a
	/// hillside that falls away has only a few metres of rock over its far end, which then took the daylight, and past the
	/// shadow distance the sun too — far walls and floors lit grey that went dark as they were walked up to (Moron 2's lava
	/// tube). Inside a cave the air is the cave's however thin the rock over it: a point between a column's floor and roof
	/// is underground by the column's closure.
	/// </para>
	/// <para>
	/// <b>Never the hillside.</b> The cut keeps its outer shell <c>CaveSystem.CutSlab</c> under the ground and its roof a
	/// shell's thickness under that, so the ground over a column, and all that stands on it, is metres above the column's
	/// roof: past the gate's <see cref="Margin"/>. Only at a mouth may the rock come up to the ground, and there the closure is 0.
	/// </para>
	/// <para>
	/// <b>One reading.</b> <see cref="At"/> mirrors FishGroundMap.hlsl's FishCaveEnclosed line for line (the window
	/// CaveEnclosureMap gathers the live caves into): the four columns round a point filtered by their closure, those that
	/// hold cave rock only, and the point gated by the most open of their floor-to-roof spans. Change both together.
	/// </para>
	/// </remarks>
	public sealed class CaveEnclosure
	{
		/// <summary>A column's width, m (world-aligned: column i spans [i·Cell, (i+1)·Cell)).</summary>
		public const float Cell = 2.5f;
		/// <summary>How far over a column's roof a point is still in the cave, m (fully; gone at twice this).</summary>
		public const float Margin = 0.5f;
		/// <summary>How far under a column's floor, m (fully; gone at twice this): only rock is there, so generous.</summary>
		public const float FloorMargin = 2f;
		private const int Format = 1;

		/// <summary>The first column of the rectangle the cave's columns lie in (world cells of <see cref="Cell"/>).</summary>
		public readonly int X0, Z0;
		/// <summary>The rectangle's columns across x and z.</summary>
		public readonly int Width, Depth;
		private readonly float[] bottom;
		private readonly float[] top;
		private readonly float[] closure;

		/// <summary>A rectangle of columns, each empty until <see cref="Set"/>.</summary>
		public CaveEnclosure(int x0, int z0, int width, int depth)
		{
			X0 = x0;
			Z0 = z0;
			Width = Mathf.Max(0, width);
			Depth = Mathf.Max(0, depth);
			int n = Width * Depth;
			bottom = new float[n];
			top = new float[n];
			closure = new float[n];
			for (int i = 0; i < n; i++)
			{
				bottom[i] = float.MaxValue;
				top[i] = float.MinValue;
			}
		}

		/// <summary>The column a world x or z lies in.</summary>
		public static int ColumnOf(float worldXZ) => Mathf.FloorToInt(worldXZ / Cell);

		/// <summary>How many columns hold cave rock.</summary>
		public int Count
		{
			get
			{
				int n = 0;
				for (int i = 0; i < bottom.Length; i++)
				{
					if (bottom[i] <= top[i])
					{
						n++;
					}
				}
				return n;
			}
		}

		/// <summary>Sets a column (world column indices): its inner faces' lowest and highest (world y) and its closure (0..1).</summary>
		public void Set(int x, int z, float floor, float roof, float shut)
		{
			int i = Index(x, z);
			if (i >= 0)
			{
				bottom[i] = floor;
				top[i] = roof;
				closure[i] = Mathf.Clamp01(shut);
			}
		}

		/// <summary>A column (world column indices), if it holds cave rock.</summary>
		public bool TryColumn(int x, int z, out float floor, out float roof, out float shut)
		{
			int i = Index(x, z);
			if (i < 0 || bottom[i] > top[i])
			{
				floor = roof = shut = 0f;
				return false;
			}
			floor = bottom[i];
			roof = top[i];
			shut = closure[i];
			return true;
		}

		private int Index(int x, int z)
		{
			x -= X0;
			z -= Z0;
			return x < 0 || z < 0 || x >= Width || z >= Depth ? -1 : z * Width + x;
		}

		/// <summary>How much a height is within a column's span, 0..1 (FishGroundMap.hlsl FishCaveGate).</summary>
		public static float Gate(float y, float floor, float roof)
		{
			return Smooth(floor - 2f * FloorMargin, floor - FloorMargin, y) * (1f - Smooth(roof + Margin, roof + 2f * Margin, y));
		}

		private static float Smooth(float a, float b, float x)
		{
			float t = Mathf.Clamp01((x - a) / (b - a));
			return t * t * (3f - 2f * t);
		}

		/// <summary>How far in this cave a point is, 0..1: its columns' closure where it stands between their floor and roof.</summary>
		public float At(Vector3 p)
		{
			float ux = p.x / Cell - 0.5f, uz = p.z / Cell - 0.5f;
			int ix = Mathf.FloorToInt(ux), iz = Mathf.FloorToInt(uz);
			float fx = ux - ix, fz = uz - iz;
			float sumB = 0f, sumW = 0f, gate = 0f;
			for (int k = 0; k < 4; k++)
			{
				int ox = k & 1, oz = k >> 1;
				if (TryColumn(ix + ox, iz + oz, out float floor, out float roof, out float shut))
				{
					float b = (ox == 1 ? fx : 1f - fx) * (oz == 1 ? fz : 1f - fz);
					sumB += b;
					sumW += b * shut;
					gate = Mathf.Max(gate, Gate(p.y, floor, roof));
				}
			}
			return sumB > 1e-5f ? sumW / sumB * gate : 0f;
		}

		/// <summary>
		/// The columns as bytes a component keeps (a scene's text holds a byte array as one hex string): the rectangle, then
		/// each row's runs of columns holding rock, each column its floor (rounded down) and roof (rounded up) in tenths of a
		/// metre over a base, and its closure in 255ths.
		/// </summary>
		public byte[] Encode()
		{
			float lowest = float.MaxValue;
			for (int i = 0; i < bottom.Length; i++)
			{
				if (bottom[i] <= top[i])
				{
					lowest = Mathf.Min(lowest, bottom[i]);
				}
			}
			int baseY = lowest == float.MaxValue ? 0 : Mathf.FloorToInt(lowest);
			var bytes = new System.Collections.Generic.List<byte>(64 + bottom.Length * 3);
			void Int(int v)
			{
				bytes.Add((byte)v);
				bytes.Add((byte)(v >> 8));
				bytes.Add((byte)(v >> 16));
				bytes.Add((byte)(v >> 24));
			}
			void Short(int v)
			{
				v = Mathf.Clamp(v, 0, ushort.MaxValue);
				bytes.Add((byte)v);
				bytes.Add((byte)(v >> 8));
			}
			Int(Format);
			Int(X0);
			Int(Z0);
			Int(Width);
			Int(Depth);
			Int(baseY);
			for (int z = 0; z < Depth; z++)
			{
				int runsAt = bytes.Count;
				Short(0);
				int runs = 0;
				int x = 0;
				while (x < Width)
				{
					int i = z * Width + x;
					if (bottom[i] > top[i])
					{
						x++;
						continue;
					}
					int start = x;
					while (x < Width && bottom[z * Width + x] <= top[z * Width + x])
					{
						x++;
					}
					Short(start);
					Short(x - start);
					for (int c = start; c < x; c++)
					{
						int j = z * Width + c;
						Short(Mathf.FloorToInt((bottom[j] - baseY) * 10f));
						Short(Mathf.CeilToInt((top[j] - baseY) * 10f));
						bytes.Add((byte)Mathf.RoundToInt(closure[j] * 255f));
					}
					runs++;
				}
				bytes[runsAt] = (byte)runs;
				bytes[runsAt + 1] = (byte)(runs >> 8);
			}
			return bytes.ToArray();
		}

		/// <summary>The columns from <see cref="Encode"/>'s bytes; null for none, or for bytes of another format.</summary>
		public static CaveEnclosure Decode(byte[] data)
		{
			if (data == null || data.Length < 24)
			{
				return null;
			}
			int at = 0;
			int Int()
			{
				int v = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
				at += 4;
				return v;
			}
			int Short()
			{
				int v = data[at] | (data[at + 1] << 8);
				at += 2;
				return v;
			}
			try
			{
				if (Int() != Format)
				{
					return null;
				}
				int x0 = Int(), z0 = Int(), width = Int(), depth = Int(), baseY = Int();
				if (width <= 0 || depth <= 0 || (long)width * depth > 16 * 1024 * 1024)
				{
					return null;
				}
				var e = new CaveEnclosure(x0, z0, width, depth);
				for (int z = 0; z < depth; z++)
				{
					int runs = Short();
					for (int r = 0; r < runs; r++)
					{
						int start = Short(), length = Short();
						for (int c = start; c < start + length; c++)
						{
							float floor = baseY + Short() / 10f;
							float roof = baseY + Short() / 10f;
							float shut = data[at++] / 255f;
							e.Set(x0 + c, z0 + z, floor, roof, shut);
						}
					}
				}
				return e;
			}
			catch (System.IndexOutOfRangeException)
			{
				return null;
			}
		}
	}
}
