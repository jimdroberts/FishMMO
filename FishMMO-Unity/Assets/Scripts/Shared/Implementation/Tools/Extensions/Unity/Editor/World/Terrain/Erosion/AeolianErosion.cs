#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How the wind shapes a scene: its direction and the size of what it makes.</summary>
	public sealed class AeolianSettings
	{
		/// <summary>The way the prevailing wind blows, as a unit vector in scene axes (+X east, +Z north at a heading of 0).</summary>
		public float WindX = 1f;
		public float WindZ = 0f;

		/// <summary>How deep wind-blown sand cuts the corridors between yardangs in the softest rock, in metres.</summary>
		public float YardangDepthMetres = 5f;

		/// <summary>The distance from one yardang to the next, across the wind, in metres.</summary>
		public float YardangSpacingMetres = 45f;

		/// <summary>How long a yardang runs along the wind, roughly, in metres.</summary>
		public float YardangLengthMetres = 350f;

		/// <summary>How tall the dunes stand where the sand is deepest, in metres.</summary>
		public float DuneHeightMetres = 9f;

		/// <summary>The distance from one dune crest to the next, along the wind, in metres.</summary>
		public float DuneWavelengthMetres = 130f;

		/// <summary>
		/// Share of a dune's length taken by its lee: the steep slip face sand avalanches down, under
		/// its angle of repose, behind the long gentle slope the wind pushes the sand up.
		/// </summary>
		public float LeeShare = 0.2f;
	}

	/// <summary>
	/// The wind's work on dry, bare ground: yardangs cut into soft rock, and dunes where there is sand
	/// to build them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> Wind moves ground only where nothing holds it — no water, no roots — and there is
	/// air to blow (<see cref="IErosionGround.Aeolian"/>): deserts, steppes, a Mars. Its direction is
	/// the world's prevailing wind at the scene's latitude, from the same belts the weather drifts on,
	/// so every yardang and dune in a scene lines up, as they do in a real desert.
	/// </para>
	/// <para>
	/// <b>Yardangs</b> are what wind-blown sand leaves of soft rock: long streamlined ridges along the
	/// wind with wide corridors scoured between them, a few tens of metres apart. Cut in proportion
	/// to the rock's softness, so hard beds stand.
	/// </para>
	/// <para>
	/// <b>Dunes</b> are sand laid on top, where the ground has sand to give
	/// (<see cref="IErosionGround.DuneSand"/>): a long gentle stoss slope up into the wind, a short
	/// steep slip face down out of it, the crests across the wind. With sand to spare they run as
	/// continuous transverse ridges; where it is scarce the crests break into separate crescents, as
	/// barchans form on a bare floor.
	/// </para>
	/// <para>
	/// Shaped from the wind's geometry rather than simulated grain by grain: a dune field is decades
	/// of saltation, and its form — wavelength, asymmetry, alignment — is what reads. Pure, parallel,
	/// deterministic.
	/// </para>
	/// </remarks>
	public static class AeolianErosion
	{
		/// <summary>Shapes <paramref name="grid"/> in place. Returns the volume cut and laid, in m³.</summary>
		public static (double Cut, double Laid) Run(ErosionGrid grid, IErosionGround ground, AeolianSettings settings, int seed)
		{
			int width = grid.Width, depth = grid.Depth;
			float cell = grid.CellMetres;
			float length = MathF.Sqrt(settings.WindX * settings.WindX + settings.WindZ * settings.WindZ);
			float wx = length > 1e-6f ? settings.WindX / length : 1f, wz = length > 1e-6f ? settings.WindZ / length : 0f;
			float lee = Math.Clamp(settings.LeeShare, 0.05f, 0.5f);
			var cutRows = new double[depth];
			var laidRows = new double[depth];

			Parallel.For(0, depth, z =>
			{
				double cut = 0.0, laid = 0.0;
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if (grid.Height[i] < grid.LevelAt(i))
					{
						continue;
					}
					float wind = ground.Aeolian(i);
					if (wind <= 0f)
					{
						continue;
					}
					float px = x * cell, pz = z * cell;
					// Along the wind and across it.
					float u = px * wx + pz * wz;
					float v = -px * wz + pz * wx;
					float sand = Math.Clamp(ground.DuneSand(i), 0f, 1f);

					// Yardangs: corridors scoured along the wind between narrow ridges, the ridges
					// broken into separate hulls along their length.
					float softness = 1f - ground.RockHardness(i, grid.Height[i] - grid.Soil[i]);
					float across = v / settings.YardangSpacingMetres + 0.35f * (Noise(u / settings.YardangLengthMetres, v / (settings.YardangSpacingMetres * 3f), seed) - 0.5f);
					float fromRidge = MathF.Abs(2f * (across - MathF.Floor(across)) - 1f); // 0 mid-corridor, 1 on a ridge
					float ridge = Smooth(0.55f, 0.9f, fromRidge);
					float hull = Smooth(0.35f, 0.6f, Noise(u / settings.YardangLengthMetres, MathF.Floor(across) * 7.31f, seed + 17));
					float scour = settings.YardangDepthMetres * wind * softness * softness * (1f - sand) * (1f - ridge * hull);
					if (scour > 0f)
					{
						grid.Height[i] -= scour;
						grid.Soil[i] = Math.Max(0f, grid.Soil[i] - scour);
						cut += scour;
					}

					// Dunes: stoss up into the wind, slip face down out of it, crests across it.
					if (sand > 0f)
					{
						float along = u / settings.DuneWavelengthMetres + 0.3f * (Noise(v / (settings.DuneWavelengthMetres * 3f), u / (settings.DuneWavelengthMetres * 4f), seed + 31) - 0.5f);
						float phase = along - MathF.Floor(along);
						float profile = phase < 1f - lee
							? Smooth01(phase / (1f - lee))
							: 1f - Smooth01((phase - (1f - lee)) / lee);
						// Scarce sand breaks the crests into crescents; plentiful sand runs them on.
						float crest = Smooth(1f - 2f * sand, 1f, Noise(v / (settings.DuneWavelengthMetres * 0.8f), u / (settings.DuneWavelengthMetres * 2f), seed + 53));
						float dune = settings.DuneHeightMetres * wind * sand * crest * profile;
						grid.Height[i] += dune;
						grid.Soil[i] += dune;
						laid += dune;
					}
				}
				cutRows[z] = cut;
				laidRows[z] = laid;
			});

			double totalCut = 0.0, totalLaid = 0.0;
			for (int z = 0; z < depth; z++)
			{
				totalCut += cutRows[z];
				totalLaid += laidRows[z];
			}
			double area = (double)cell * cell;
			return (totalCut * area, totalLaid * area);
		}

		private static float Smooth01(float t)
		{
			t = Math.Clamp(t, 0f, 1f);
			return t * t * (3f - 2f * t);
		}

		private static float Smooth(float from, float to, float t) => Smooth01((t - from) / (to - from));

		/// <summary>Smooth 2D value noise in [0, 1].</summary>
		private static float Noise(float x, float y, int seed)
		{
			int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
			float fx = x - ix, fy = y - iy;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
			float south = a + (b - a) * fx, north = c + (d - c) * fx;
			return south + (north - south) * fy;
		}

		private static float Hash(int x, int y, int seed)
		{
			uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu;
			h ^= h >> 16;
			h *= 0x7feb352du;
			h ^= h >> 15;
			h *= 0x846ca68bu;
			h ^= h >> 16;
			return (h >> 8) * (1f / 16777216f);
		}
	}
}
#endif
