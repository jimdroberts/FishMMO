#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A scene's climate over one terrain tile on a coarse grid of its ground, for the splat painter's climate-banded layers
	/// (TerrainTextureLayer.useClimateBand, climate variants' ground overrides): the temperature and humidity at each node's
	/// ground, and how each changes per metre up, read back at any texel's own height.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The scatter's grid, for the ground.</b> The same construction as the climate-banded spawn rules' grid in
	/// TerrainScatter (ClimateGrid: <see cref="NodeMetres"/>, <see cref="RiseMetres"/> and <see cref="MaximumNodes"/> are its
	/// numbers): the field sampled at nodes 16 m apart, at the ground's own height and again 25 m above it, and a texel reads it
	/// bilinearly and then follows the per-metre change to its own height, so the lapse rate is followed to first order at the
	/// texel's true altitude. The ground layers and the plants authored with the same band therefore read the same climate at
	/// the same point (to the scatter's measured 10⁻⁵ in temperature, 0.006 in humidity at the ground).
	/// </para>
	/// <para>
	/// <b>Deterministic and seamless.</b> A function of the tile's position and ground alone; two tiles' nodes on their shared
	/// edge stand at the same places on the same ground, so a band runs straight through a seam, and a repaint paints the same.
	/// Built on the calling thread (the climate and the terrain are asked there), read from any.
	/// </para>
	/// </remarks>
	public sealed class TerrainClimateGrid
	{
		/// <summary>Spacing of the nodes, metres; a tile gets at least two a side and at most <see cref="MaximumNodes"/>.</summary>
		public const float NodeMetres = 16f;
		/// <summary>Most nodes along either side.</summary>
		public const int MaximumNodes = 257;
		/// <summary>How far above the ground the second sample is taken, scene metres: the per-metre change is the difference over it.</summary>
		public const float RiseMetres = 25f;

		private readonly int nodesX, nodesZ;
		private readonly float[] ground, temperature, humidity, temperatureRise, humidityRise;

		private TerrainClimateGrid(int nodesX, int nodesZ)
		{
			this.nodesX = nodesX;
			this.nodesZ = nodesZ;
			int count = nodesX * nodesZ;
			ground = new float[count];
			temperature = new float[count];
			humidity = new float[count];
			temperatureRise = new float[count];
			humidityRise = new float[count];
		}

		/// <summary>
		/// Samples the climate over a tile.
		/// </summary>
		/// <param name="climateAt">The climate at a world point (x, y, z): x the temperature, y the humidity, each −1 … 1.</param>
		/// <param name="origin">The tile's south-west corner, world metres.</param>
		/// <param name="sizeXZ">The tile's extent along world X and Z.</param>
		/// <param name="groundY">The ground's world height at a point of the tile, (u, v) each 0 … 1 across it.</param>
		public static TerrainClimateGrid Build(Func<float, float, float, Vector2> climateAt, Vector3 origin, Vector2 sizeXZ, Func<float, float, float> groundY)
		{
			if (climateAt == null) throw new ArgumentNullException(nameof(climateAt));
			if (groundY == null) throw new ArgumentNullException(nameof(groundY));
			int nodesX = Mathf.Clamp(Mathf.CeilToInt(sizeXZ.x / NodeMetres) + 1, 2, MaximumNodes);
			int nodesZ = Mathf.Clamp(Mathf.CeilToInt(sizeXZ.y / NodeMetres) + 1, 2, MaximumNodes);
			var grid = new TerrainClimateGrid(nodesX, nodesZ);
			for (int b = 0; b < nodesZ; b++)
			{
				float v = b / (nodesZ - 1f);
				float z = origin.z + v * sizeXZ.y;
				for (int a = 0; a < nodesX; a++)
				{
					float u = a / (nodesX - 1f);
					float x = origin.x + u * sizeXZ.x;
					float y = groundY(u, v);
					int n = b * nodesX + a;
					Vector2 at = climateAt(x, y, z);
					Vector2 above = climateAt(x, y + RiseMetres, z);
					grid.ground[n] = y;
					grid.temperature[n] = at.x;
					grid.humidity[n] = at.y;
					grid.temperatureRise[n] = (above.x - at.x) / RiseMetres;
					grid.humidityRise[n] = (above.y - at.y) / RiseMetres;
				}
			}
			return grid;
		}

		/// <summary>The climate at (u, v) of the tile and world height <paramref name="worldY"/>: x the temperature, y the humidity, clamped to −1 … 1.</summary>
		public Vector2 Read(float u, float v, float worldY)
		{
			float fx = Mathf.Clamp01(u) * (nodesX - 1);
			float fz = Mathf.Clamp01(v) * (nodesZ - 1);
			int x0 = Math.Min((int)fx, nodesX - 2);
			int z0 = Math.Min((int)fz, nodesZ - 2);
			float tx = fx - x0, tz = fz - z0;
			int a = z0 * nodesX + x0, b = a + nodesX;
			float w00 = (1f - tx) * (1f - tz), w10 = tx * (1f - tz), w01 = (1f - tx) * tz, w11 = tx * tz;
			float rise = worldY - (ground[a] * w00 + ground[a + 1] * w10 + ground[b] * w01 + ground[b + 1] * w11);
			float t = temperature[a] * w00 + temperature[a + 1] * w10 + temperature[b] * w01 + temperature[b + 1] * w11
				+ rise * (temperatureRise[a] * w00 + temperatureRise[a + 1] * w10 + temperatureRise[b] * w01 + temperatureRise[b + 1] * w11);
			float h = humidity[a] * w00 + humidity[a + 1] * w10 + humidity[b] * w01 + humidity[b + 1] * w11
				+ rise * (humidityRise[a] * w00 + humidityRise[a + 1] * w10 + humidityRise[b] * w01 + humidityRise[b + 1] * w11);
			return new Vector2(Mathf.Clamp(t, -1f, 1f), Mathf.Clamp(h, -1f, 1f));
		}
	}
}
#endif
