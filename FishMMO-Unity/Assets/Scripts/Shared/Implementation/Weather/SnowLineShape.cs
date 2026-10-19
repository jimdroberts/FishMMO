using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The shape of the seasonal snow line on the ground: where the snowpack starts, how it wanders, and how deep it lies
	/// above it. The C# twin of FishSnowLine.hlsl, which every surface, the grass and the cover map's own readers share, so
	/// what a footprint sinks into is what is drawn.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>By height, per point.</b> The line is a height (<see cref="GroundCover.SnowLine"/>, world y, from the season, the
	/// air added to it and the lapse rate per world metre), moved at each place by two things: how much warmer or colder its
	/// climate is at sea level than the area's middle (<see cref="SnowLineField"/>: latitude, how far inland, the regional
	/// wobble) and a wander of about a kelvin over a few hundred metres, so the line is a coastline on the hills and not a
	/// contour ruled across them. The cover map used to carry the pack too, at its texel centres, which drew the line as a
	/// 40 m staircase near the camera and, past the map, as one height for the whole scene.
	/// </para>
	/// <para>
	/// <b>Deeper as it rises.</b> The pack is whole within <see cref="SpanKelvin"/> of the line and goes on deepening up to
	/// <see cref="GroundCover.PackDeepKelvin"/> colder, so a range carries a dusting at its snow line and its deepest snow
	/// on its summits.
	/// </para>
	/// </remarks>
	public static class SnowLineShape
	{
		/// <summary>Kelvin between where the pack starts and where it is whole: <see cref="GroundCover.PackWarm"/> − <see cref="GroundCover.PackCold"/>. FISH_PACK_SPAN_K mirrors it.</summary>
		public const float SpanKelvin = 0.7f;

		/// <summary>How far the line wanders either way, kelvin of the climate. FISH_SNOW_WANDER_K mirrors it.</summary>
		/// <remarks>0.8 K is about 120 m of altitude on an Earth-like world: a snow line's lobes up a gully and down a spur.</remarks>
		public const float WanderKelvin = 0.8f;

		/// <summary>The wander's two scales, metres. FishSnowLineWander mirrors them.</summary>
		public const float WanderCoarseMetres = 240f, WanderFineMetres = 80f;

		/// <summary>
		/// How far the line is lifted at a place, world metres: its sea-level climate against the middle's and the wander,
		/// each at the metres per kelvin the published line stands for. FishSnowLineLift mirrors it.
		/// </summary>
		/// <param name="fieldKelvin">The place's sea-level climate less the middle's, K (<see cref="SnowLineField.KelvinAt"/>).</param>
		/// <param name="startY">Where the middle's pack starts, world y.</param>
		/// <param name="wholeY">Where it is whole, world y.</param>
		public static float LiftAt(float x, float z, float fieldKelvin, float startY, float wholeY)
		{
			float perKelvin = Mathf.Max(1e-3f, wholeY - startY) / SpanKelvin;
			return (fieldKelvin + Wander(x, z) * WanderKelvin) * perKelvin;
		}

		/// <summary>The pack's cover at a height, 0..1 of the share: eased from the lifted start to the lifted whole. FishPackCover mirrors it.</summary>
		public static float CoverAt(float worldY, float lift, float startY, float wholeY, float share)
		{
			float t = Mathf.Clamp01((worldY - lift - startY) / Mathf.Max(1e-3f, wholeY - startY));
			return share * t * t * (3f - 2f * t);
		}

		/// <summary>The pack's depth past the blanket at a height, metres: none at the whole line, all of <paramref name="metres"/> at the deep line. FishPackDepth mirrors it.</summary>
		public static float DepthAt(float worldY, float lift, float wholeY, float deepY, float share, float metres)
		{
			return share * Mathf.Clamp01((worldY - lift - wholeY) / Mathf.Max(1e-3f, deepY - wholeY)) * metres;
		}

		/// <summary>The wander at a place, about −1..1: two octaves of value noise. FishSnowLineWander mirrors it bit for bit in its hash.</summary>
		public static float Wander(float x, float z)
		{
			float coarse = ValueNoise(x / WanderCoarseMetres, z / WanderCoarseMetres);
			float fine = ValueNoise(x / WanderFineMetres + 31.7f, z / WanderFineMetres + 31.7f);
			return (coarse * 0.65f + fine * 0.35f) * 2f - 1f;
		}

		private static float ValueNoise(float x, float z)
		{
			float fx = Mathf.Floor(x), fz = Mathf.Floor(z);
			int ix = (int)fx, iz = (int)fz;
			float tx = x - fx, tz = z - fz;
			tx = tx * tx * (3f - 2f * tx);
			tz = tz * tz * (3f - 2f * tz);
			float a = Cell(ix, iz), b = Cell(ix + 1, iz), c = Cell(ix, iz + 1), d = Cell(ix + 1, iz + 1);
			return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
		}

		/// <summary>A cell's value, 0..1: FishSnowHash's 24 low bits.</summary>
		private static float Cell(int x, int z)
		{
			unchecked
			{
				uint h = (uint)x * 0x8DA6B343u + (uint)z * 0xD8163841u;
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return (h & 0xFFFFFFu) / 16777215f;
			}
		}
	}
}
