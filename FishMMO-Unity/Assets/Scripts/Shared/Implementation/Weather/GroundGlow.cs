using System;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A ground layer's glow (2026-10-25, phase I2r, decision 65 (a); <see cref="BiomeGroundGlow"/>): the C# twin of FishGroundGlow.hlsl,
	/// constant for constant. Presentation only: the client draws it on the terrain on the GPU and writes its mean into the lava's
	/// molten mask; this side is what the tests pin.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Seams (OVERTURN).</b> The ground is tiled into crust plates, the Voronoi regions of points jittered in a grid of the layer's
	/// plate size. Each plate overturns once a period (a whole divisor of the shared clock's 10 000 s wrap) at its own hashed moment:
	/// its age since then is <c>frac(t / P + phase) · P</c>, so the pattern is a pure function of the shared clock and meets itself
	/// across the wrap. A crust's surface cools as a conducting half-space, <c>T = floor + ΔT / √(1 + age / τ)</c> (the lava's own
	/// law, FishLava.hlsl): a plate's skin with τ <see cref="SkinTauSeconds"/>, a seam (where two plates meet, the hotter plate's
	/// side) with the slower <see cref="SeamTauSeconds"/>, so the cracks keep glowing long after the plates have gone dark. In the
	/// last <see cref="OpenShare"/> of its period a plate tears open again (its temperature rises back to the melt), so the
	/// temperature never jumps in time; the seam is the hotter of the two plates either side, so it never jumps in space.
	/// </para>
	/// <para>
	/// <b>Whole.</b> The layer at the world's mean surface temperature plus its offset: a Venus plain is 740 K, below the Draper
	/// point, and shows nothing; a magma world's crust glows red.
	/// </para>
	/// <para>
	/// <b>Colour.</b> Planck's law sampled at three wavelengths (<see cref="RedNanometres"/>, green, blue), normalised to the green of
	/// a <see cref="ReferenceKelvin"/> melt; its luminance relative to that melt raised to the eye's response (0.45 in the dark, 1 by
	/// day, as the lava's FishLavaAdaptedResponse), the melt's brightest channel at <see cref="Exposure"/>.
	/// </para>
	/// </remarks>
	public static class GroundGlow
	{
		/// <summary>The motion clock's wrap, s (FISH_MOTION_WRAP).</summary>
		public const float WrapSeconds = BiomeGroundGlow.WrapSeconds;
		/// <summary>The crust's settled surface temperature, K (the lava material's _CrustFloorKelvin).</summary>
		public const float FloorKelvin = 550f;
		/// <summary>A plate's skin's cooling time, s (the lava material's _CoolingSeconds).</summary>
		public const float SkinTauSeconds = 3f;
		/// <summary>A seam's cooling time, s: the crack keeps its heat from below far longer than the open skin.</summary>
		public const float SeamTauSeconds = 30f;
		/// <summary>The share of its period at whose end a plate tears open again.</summary>
		public const float OpenShare = 0.03f;
		/// <summary>A seam's half-width, as a share of the plate size.</summary>
		public const float SeamShare = 0.07f;
		/// <summary>How far inside its grid cell a plate's point may sit, as a share of the cell.</summary>
		public const float Jitter = 0.8f;
		/// <summary>The plates' hash salt (FISH_GROUND_GLOW_SALT).</summary>
		public const uint Salt = 0x4C1A9E57u;
		/// <summary>The melt every glow is measured against, K (the lava material's _MeltKelvin).</summary>
		public const float ReferenceKelvin = 1420f;
		/// <summary>The melt's brightest channel (the lava material's _Exposure).</summary>
		public const float Exposure = 2f;
		/// <summary>The eye's response in the dark and in daylight (the lava material's _Response, _DayResponse).</summary>
		public const float NightResponse = 0.45f, DayResponse = 1f;
		/// <summary>The three wavelengths sampled, nm.</summary>
		public const float RedNanometres = 610f, GreenNanometres = 550f, BlueNanometres = 465f;
		/// <summary>Planck's second radiation constant, m·K.</summary>
		public const double SecondRadiation = 1.4388e-2;
		/// <summary>The faintest temperature drawn, K: below it nothing is visible (the lava's floor of 400 K).</summary>
		public const float VisibleFromKelvin = 400f;
		/// <summary>The Draper point and the solidus, K: a whole layer's share of the molten mask runs between them.</summary>
		public const float DraperKelvin = 800f, SolidusKelvin = 1300f;

		/// <summary>k = c₂ / λ for each sampled wavelength, K (FISH_GROUND_GLOW_K).</summary>
		public static readonly float RedK = (float)(SecondRadiation / (RedNanometres * 1e-9));
		public static readonly float GreenK = (float)(SecondRadiation / (GreenNanometres * 1e-9));
		public static readonly float BlueK = (float)(SecondRadiation / (BlueNanometres * 1e-9));
		/// <summary>λ⁻⁵ relative to green, for red and blue (FISH_GROUND_GLOW_LAMBDA).</summary>
		public static readonly float RedWeight = (float)Math.Pow(GreenNanometres / RedNanometres, 5.0);
		public static readonly float BlueWeight = (float)Math.Pow(GreenNanometres / BlueNanometres, 5.0);

		// ── Seams ──

		/// <summary>
		/// The plate a point lies on and the nearest other: their hashes, and the distance to the line between them (m, approximate:
		/// half the difference of the two distances).
		/// </summary>
		public static float PlatesAt(float x, float z, float cellMetres, out uint owner, out uint neighbour)
		{
			float cell = Math.Max(0.5f, cellMetres);
			float px = x / cell, pz = z / cell;
			int ix = (int)Math.Floor(px), iz = (int)Math.Floor(pz);
			float d1 = float.MaxValue, d2 = float.MaxValue;
			owner = neighbour = 0u;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int cx = ix + dx, cz = iz + dz;
					uint h = BurnMosaic.CellHash(cx, cz, Salt);
					float sx = cx + 0.5f * (1f - Jitter) + Jitter * BurnMosaic.Unit(BurnMosaic.Hash(h ^ 1u));
					float sz = cz + 0.5f * (1f - Jitter) + Jitter * BurnMosaic.Unit(BurnMosaic.Hash(h ^ 2u));
					float ex = px - sx, ez = pz - sz;
					float d = (float)Math.Sqrt(ex * ex + ez * ez);
					if (d < d1)
					{
						d2 = d1;
						neighbour = owner;
						d1 = d;
						owner = h;
					}
					else if (d < d2)
					{
						d2 = d;
						neighbour = h;
					}
				}
			}
			return 0.5f * (d2 - d1) * cell;
		}

		/// <summary>A plate's place in its overturn cycle, 0..1, at a moment of the shared clock (wrapped at <see cref="WrapSeconds"/>).</summary>
		public static float Phase(uint plate, float seconds, int periodSeconds)
		{
			float period = Math.Max(1, periodSeconds);
			float p = seconds / period + BurnMosaic.Unit(BurnMosaic.Hash(plate ^ 3u));
			return p - (float)Math.Floor(p);
		}

		/// <summary>A crust's surface temperature this long after it formed, K: floor + ΔT / √(1 + age / τ).</summary>
		public static float CrustKelvin(float ageSeconds, float meltKelvin, float tauSeconds)
		{
			return FloorKelvin + (meltKelvin - FloorKelvin) / (float)Math.Sqrt(1.0 + Math.Max(0f, ageSeconds) / Math.Max(0.1f, tauSeconds));
		}

		/// <summary>A plate's temperature now: its crust cooling since it last overturned, rising back to the melt as it tears open.</summary>
		public static float PlateKelvin(uint plate, float seconds, int periodSeconds, float meltKelvin, float tauSeconds)
		{
			float p = Phase(plate, seconds, periodSeconds);
			float crust = CrustKelvin(p * Math.Max(1, periodSeconds), meltKelvin, tauSeconds);
			float open = BurnMosaic.Smooth(1f - OpenShare, 1f, p);
			return Math.Max(crust, FloorKelvin + (meltKelvin - FloorKelvin) * open);
		}

		/// <summary>How much of a point is seam, 0..1: 1 on the line between two plates, none past a seam's half-width.</summary>
		public static float SeamWeight(float edgeMetres, float cellMetres) => 1f - BurnMosaic.Smooth(0f, SeamShare * Math.Max(0.5f, cellMetres), edgeMetres);

		/// <summary>A seamed layer's temperature at world (x, z) at a moment of the shared clock, K.</summary>
		public static float SeamKelvinAt(float x, float z, float seconds, float meltKelvin, float cellMetres, int periodSeconds)
		{
			float edge = PlatesAt(x, z, cellMetres, out uint owner, out uint neighbour);
			float seam = Math.Max(PlateKelvin(owner, seconds, periodSeconds, meltKelvin, SeamTauSeconds),
				PlateKelvin(neighbour, seconds, periodSeconds, meltKelvin, SeamTauSeconds));
			float skin = PlateKelvin(owner, seconds, periodSeconds, meltKelvin, SkinTauSeconds);
			float w = SeamWeight(edge, cellMetres);
			return skin + (seam - skin) * w;
		}

		/// <summary>A layer's temperature at world (x, z) from its packed table texel (<see cref="BiomeGroundGlow.Pack"/>), K; 0 for no glow.</summary>
		public static float KelvinAt(Vector4 packed, float x, float z, float seconds, float worldKelvin)
		{
			BiomeGroundGlowMode mode = BiomeGroundGlowModes.ModeOf(packed, out int period);
			switch (mode)
			{
				case BiomeGroundGlowMode.Seams:
					return SeamKelvinAt(x, z, seconds, packed.y, packed.w, period);
				case BiomeGroundGlowMode.Whole:
					return worldKelvin + packed.y;
			}
			return 0f;
		}

		// ── Colour ──

		/// <summary>Planck's law at the three wavelengths, relative to green at <see cref="ReferenceKelvin"/> (unclamped below <see cref="VisibleFromKelvin"/>: 0).</summary>
		public static Vector3 Radiance(float kelvin)
		{
			if (kelvin < VisibleFromKelvin)
			{
				return Vector3.zero;
			}
			float reference = (float)(Math.Exp(GreenK / ReferenceKelvin) - 1.0);
			float r = RedWeight * reference / (float)(Math.Exp(RedK / kelvin) - 1.0);
			float g = reference / (float)(Math.Exp(GreenK / kelvin) - 1.0);
			float b = BlueWeight * reference / (float)(Math.Exp(BlueK / kelvin) - 1.0);
			return new Vector3(r, g, b);
		}

		/// <summary>Rec. 709 luminance.</summary>
		public static float Luminance(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

		/// <summary>
		/// The emitted colour at a temperature under an eye response (<see cref="NightResponse"/>…<see cref="DayResponse"/>): the
		/// colour at its own luminance relative to the reference melt raised to the response, the melt's brightest channel at
		/// <see cref="Exposure"/>.
		/// </summary>
		public static Vector3 Glow(float kelvin, float response)
		{
			Vector3 rgb = Radiance(kelvin);
			float y = Luminance(rgb);
			if (y <= 1e-30f)
			{
				return Vector3.zero;
			}
			Vector3 melt = Radiance(ReferenceKelvin);
			float meltY = Luminance(melt);
			float peak = Math.Max(melt.x, Math.Max(melt.y, melt.z)) / meltY;
			float scale = (float)Math.Pow(y / meltY, response) * Exposure / peak / y;
			return rgb * scale;
		}

		// ── The molten mask ──

		/// <summary>
		/// The share of "molten" a glowing layer adds to the lava's mask (LavaMask: 1 = lava's own glow), the layer's mean over time:
		/// a seamed crust its seams' area at about half the melt's light; a whole layer by its temperature from the Draper point to
		/// the solidus. Times the strength; 0 for no glow.
		/// </summary>
		public static float MaskShare(Vector4 packed, float worldKelvin)
		{
			BiomeGroundGlowMode mode = BiomeGroundGlowModes.ModeOf(packed, out _);
			float strength = Mathf.Clamp01(packed.z);
			switch (mode)
			{
				case BiomeGroundGlowMode.Seams:
					return strength * Math.Min(1f, 2f * SeamShare) * 0.5f;
				case BiomeGroundGlowMode.Whole:
					return strength * BurnMosaic.Smooth(DraperKelvin, SolidusKelvin, worldKelvin + packed.y);
			}
			return 0f;
		}
	}
}

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The glowing ground's share of the lava's molten mask (2026-10-25, phase I2r, decision 65 (a)): the hand-off between the client,
	/// which knows which terrain layers glow (GroundGlowTable), and the water plugin's mask (LavaMask, which neither references), so the
	/// lava's light pass and the heat shimmer light and boil over glowing crust as over the lava. Null (the default): no ground glows,
	/// and the mask is the lava's alone, as before.
	/// </summary>
	public static class GroundGlowMask
	{
		/// <summary>The glowing ground's mean share at world (x, z), 0..1 (<see cref="GroundGlow.MaskShare"/> by its layers); null for none.</summary>
		public static System.Func<float, float, float> ShareAt { get; private set; }

		/// <summary>Moves on every change, so a mask built before it is built again.</summary>
		public static int Version { get; private set; }

		/// <summary>Hands over (or, with null, takes back) the glowing ground.</summary>
		public static void Set(System.Func<float, float, float> shareAt)
		{
			if (ReferenceEquals(shareAt, ShareAt))
			{
				return;
			}
			ShareAt = shareAt;
			Version++;
		}
	}
}
