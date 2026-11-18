using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How fully something banded by the climate is present at a reading: a ground layer's temperature and humidity band
	/// (<see cref="TerrainTextureLayer.useClimateBand"/>) and a climate variant's texture overrides
	/// (<see cref="BiomeClimateVariant.TextureWeight"/>). The same curve as a spawn rule's climate band (TerrainScatter.ClimateBand,
	/// pinned equal by a test), so a ground and the plants authored with the same numbers change over at the same place.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 1 inside the range, 0 outside, with a smoothstep soft edge <c>falloff</c> wide CENTRED on each closed end (half at the
	/// end itself): the stated end is where the change is half done, so across a scene the ground changes where the author
	/// said it does. An end at or past ±1 is open, because the climate never passes ±1 (a closed end there would halve the
	/// ground at the hottest or wettest places a world has). A NaN reading (no climate known) passes.
	/// </para>
	/// <para>Pure arithmetic: no random draw, no allocation; safe on any thread.</para>
	/// </remarks>
	public static class BiomeClimateBand
	{
		/// <summary>The soft edge's width a band gets unless told otherwise, on the climate's scale (0.1 ≈ 3.3 °C).</summary>
		public const float DefaultFalloff = 0.1f;

		/// <summary>One axis: 1 inside <paramref name="range"/> (either order), 0 outside, a centred smoothstep edge at each closed end.</summary>
		public static float Weight(Vector2 range, float falloff, float value)
		{
			if (float.IsNaN(value))
			{
				return 1f;
			}
			float low = Mathf.Min(range.x, range.y), high = Mathf.Max(range.x, range.y);
			float weight = 1f;
			if (low > -1f)
			{
				weight *= Edge(value - low, falloff);
			}
			if (high < 1f)
			{
				weight *= Edge(high - value, falloff);
			}
			return weight;
		}

		/// <summary>Both axes: the temperature band times the humidity band.</summary>
		public static float Weight(Vector2 temperatureRange, float temperatureFalloff, Vector2 humidityRange, float humidityFalloff,
			float temperature, float humidity)
		{
			return Weight(temperatureRange, temperatureFalloff, temperature) * Weight(humidityRange, humidityFalloff, humidity);
		}

		/// <summary>True when a range closes at least one end (an end inside −1 … 1), so it bands anything at all.</summary>
		public static bool Closes(Vector2 range) => Mathf.Min(range.x, range.y) > -1f || Mathf.Max(range.x, range.y) < 1f;

		/// <summary>A closed end's soft edge, given how far inside the range the value is (negative outside).</summary>
		private static float Edge(float inside, float falloff)
		{
			if (!(falloff > 0f))
			{
				return inside >= 0f ? 1f : 0f;
			}
			float t = Mathf.Clamp01(inside / falloff + 0.5f);
			return t * t * (3f - 2f * t);
		}
	}
}
