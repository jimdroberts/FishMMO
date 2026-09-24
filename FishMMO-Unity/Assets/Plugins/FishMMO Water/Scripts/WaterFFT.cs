using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace FishMMO.Water
{
	/// <summary>
	/// Runs the ocean spectrum through an inverse FFT each frame and publishes the result as
	/// displacement and derivative maps for the water shader.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Three cascades, not one.</b> A single FFT tile repeats at its patch size, and on a calm
	/// sea that repeat is the first thing the eye finds. Three tiles of incommensurable size —
	/// hundreds of metres, tens, a few — summed together have a combined period far beyond
	/// anything visible, and they also let each scale carry the wavelengths it can actually
	/// resolve: the long swell in the big tile, the chop in the small one.
	/// </para>
	/// <para>
	/// <b>The dispersion is quantised to a loop period.</b> That costs nothing visible and makes
	/// the whole field exactly periodic, which is what lets the same spectrum be baked to a finite
	/// strip of frames for platforms with no compute shaders.
	/// </para>
	/// </remarks>
	public sealed class WaterFFT : IDisposable
	{
		/// <summary>Transform size. 256 is the usual compromise: 65,536 wave components per cascade.</summary>
		public const int Size = 256;

		/// <summary>How many tiles are summed.</summary>
		public const int Cascades = 3;

		private static readonly int H0Id = Shader.PropertyToID("_H0");
		private static readonly int SpectrumAId = Shader.PropertyToID("_SpectrumA");
		private static readonly int SpectrumBId = Shader.PropertyToID("_SpectrumB");
		private static readonly int DisplacementId = Shader.PropertyToID("_Displacement");
		private static readonly int DerivativesId = Shader.PropertyToID("_Derivatives");

		private readonly ComputeShader compute;
		private readonly int initialKernel;
		private readonly int timeKernel;
		private readonly int horizontalKernel;
		private readonly int verticalKernel;
		private readonly int assembleKernel;

		private readonly RenderTexture[] h0 = new RenderTexture[Cascades];
		private readonly RenderTexture[] spectrumA = new RenderTexture[Cascades];
		private readonly RenderTexture[] spectrumB = new RenderTexture[Cascades];
		private readonly RenderTexture[] displacement = new RenderTexture[Cascades];
		private readonly RenderTexture[] derivatives = new RenderTexture[Cascades];

		/// <summary>The patch size of each cascade, in metres.</summary>
		public readonly float[] PatchMetres = { 520f, 118f, 27f };

		/// <summary>The displacement maps: xyz displacement in metres.</summary>
		public RenderTexture[] Displacement => displacement;

		/// <summary>The derivative maps: xy slope, z folding.</summary>
		public RenderTexture[] Derivatives => derivatives;

		/// <summary>The static spectrum, for diagnostics.</summary>
		public RenderTexture[] Spectrum => h0;

		/// <summary>The working buffer the transform runs in, for diagnostics.</summary>
		public RenderTexture[] Working => spectrumA;

		/// <summary>True when this machine can run the transform at all.</summary>
		public static bool Supported => SystemInfo.supportsComputeShaders
			&& SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore);

		public WaterFFT(ComputeShader shader)
		{
			compute = shader;
			initialKernel = compute.FindKernel("InitialSpectrum");
			timeKernel = compute.FindKernel("TimeSpectrum");
			horizontalKernel = compute.FindKernel("HorizontalFFT");
			verticalKernel = compute.FindKernel("VerticalFFT");
			assembleKernel = compute.FindKernel("Assemble");

			for (int i = 0; i < Cascades; i++)
			{
				h0[i] = Create("FFT h0 " + i, GraphicsFormat.R32G32B32A32_SFloat);
				spectrumA[i] = Create("FFT spectrum A " + i, GraphicsFormat.R32G32B32A32_SFloat);
				spectrumB[i] = Create("FFT spectrum B " + i, GraphicsFormat.R32G32B32A32_SFloat);
				/* Full float, not half, and that is not a luxury.
				 *
				 * An HLSL RWTexture2D<float4> compiles to an rgba32f image unit. Direct3D treats
				 * the resource as typeless and tolerates a half-float texture bound to it;
				 * OPENGL DOES NOT — the formats must match exactly, and when they do not the
				 * writes are dropped with no error at all. Measured: the spectrum (float32) read
				 * back correctly while the displacement (half) came back uniformly zero. */
				displacement[i] = Create("FFT displacement " + i, GraphicsFormat.R32G32B32A32_SFloat);
				derivatives[i] = Create("FFT derivatives " + i, GraphicsFormat.R32G32B32A32_SFloat);
			}
		}

		private static RenderTexture Create(string name, GraphicsFormat format)
		{
			var texture = new RenderTexture(Size, Size, 0, format)
			{
				name = name,
				enableRandomWrite = true,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Bilinear,
				useMipMap = false,
				hideFlags = HideFlags.HideAndDontSave,
			};
			texture.Create();
			return texture;
		}

		/// <summary>
		/// Rebuilds the static spectrum. Needed whenever the wind, the gravity or the seed change,
		/// and not otherwise — this is the expensive half and it does not depend on time.
		/// </summary>
		public void SetSeaState(float windSpeed, Vector2 windDirection, float gravity,
			float amplitude, float choppiness, float loopPeriod, uint seed)
		{
			Wind = Mathf.Max(0.1f, windSpeed);
			WindDirection = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector2.up;
			Gravity = Mathf.Max(0.05f, gravity);
			Amplitude = amplitude;
			Choppiness = choppiness;
			LoopPeriod = loopPeriod;
			Seed = seed;

			for (int i = 0; i < Cascades; i++)
			{
				Bind(initialKernel, i);
				compute.SetTexture(initialKernel, H0Id, h0[i]);
				compute.Dispatch(initialKernel, Size / 8, Size / 8, 1);
			}
		}

		public float Wind { get; private set; } = 8f;
		public Vector2 WindDirection { get; private set; } = Vector2.up;
		public float Gravity { get; private set; } = WaterWaves.EarthGravity;
		/// <summary>
		/// The Phillips scale, calibrated against significant wave height.
		/// </summary>
		/// <remarks>
		/// Not a taste value. With the spectral shape correct, significant wave height came out a
		/// constant 0.07 of theory at every wind speed — a pure scale error — and Hs goes as the
		/// square root of this, so the correction is 1/0.07² ≈ 204. Measured after: Hs tracks
		/// 0.21·U²/g across 5 to 20 m/s, which is the fully developed sea it is meant to be.
		/// </remarks>
		public float Amplitude { get; private set; } = 8.2e-3f;
		public float Choppiness { get; private set; } = 1.2f;
		public float LoopPeriod { get; private set; } = 120f;
		public uint Seed { get; private set; } = 1u;

		/// <summary>Evolves the spectrum to a moment and transforms it. One frame's work.</summary>
		/// <param name="seconds">The instant to evaluate.</param>
		/// <param name="stages">
		/// How far to run: 1 evolves only, 2 adds the horizontal transform, 3 the vertical, 4
		/// assembles. For diagnostics — a transform that comes out empty has to be bisected, and
		/// there is no other way to see between its stages.
		/// </param>
		public void Evaluate(float seconds, int stages = 4)
		{
			for (int i = 0; i < Cascades; i++)
			{
				Bind(timeKernel, i);
				compute.SetFloat("_FFTTime", seconds);
				compute.SetTexture(timeKernel, H0Id, h0[i]);
				compute.SetTexture(timeKernel, SpectrumAId, spectrumA[i]);
				compute.SetTexture(timeKernel, SpectrumBId, spectrumB[i]);
				compute.Dispatch(timeKernel, Size / 8, Size / 8, 1);
				if (stages < 2)
				{
					continue;
				}

				// One dispatch per axis: every butterfly stage happens inside the thread group.
				compute.SetTexture(horizontalKernel, SpectrumAId, spectrumA[i]);
				compute.SetTexture(horizontalKernel, SpectrumBId, spectrumB[i]);
				compute.Dispatch(horizontalKernel, Size, 1, 1);
				if (stages < 3)
				{
					continue;
				}

				compute.SetTexture(verticalKernel, SpectrumAId, spectrumA[i]);
				compute.SetTexture(verticalKernel, SpectrumBId, spectrumB[i]);
				compute.Dispatch(verticalKernel, Size, 1, 1);
				if (stages < 4)
				{
					continue;
				}

				Bind(assembleKernel, i);
				compute.SetTexture(assembleKernel, SpectrumAId, spectrumA[i]);
				compute.SetTexture(assembleKernel, SpectrumBId, spectrumB[i]);
				compute.SetTexture(assembleKernel, DisplacementId, displacement[i]);
				compute.SetTexture(assembleKernel, DerivativesId, derivatives[i]);
				compute.Dispatch(assembleKernel, Size / 8, Size / 8, 1);
			}
		}

		private void Bind(int kernel, int cascade)
		{
			compute.SetFloat("_PatchMetres", PatchMetres[cascade]);
			compute.SetFloat("_WindSpeed", Wind);
			compute.SetVector("_WindDirection", new Vector4(WindDirection.x, WindDirection.y, 0f, 0f));
			compute.SetFloat("_Gravity", Gravity);
			compute.SetFloat("_Amplitude", Amplitude);
			compute.SetFloat("_Choppiness", Choppiness);
			compute.SetFloat("_LoopPeriod", LoopPeriod);
			compute.SetInt("_Seed", (int)(Seed + (uint)cascade * 7919u));
			compute.SetInt("_Step", 1);
			/* The shortest wave a cascade keeps is a couple of its own texels. Below that the
			 * component cannot be represented on the grid and folds back as noise, which is the
			 * classic FFT ocean artefact: a surface that sparkles with detail finer than its own
			 * resolution. */
			compute.SetFloat("_SmallWave", PatchMetres[cascade] / Size * 2f);
			compute.SetFloat("_Directionality", 4f);

			/* The band this cascade owns. The boundaries sit at the crossover wavenumbers between
			 * neighbouring patch sizes, so between them the three cascades partition the spectrum
			 * exactly once instead of overlapping. */
			/* A cascade owns waves from its OWN fundamental — the longest that fits in its tile —
			 * up to the next, shorter tile's fundamental. The big tile therefore carries the swell
			 * and the small tile the chop, which is the whole point of having three. */
			float low = 2f * Mathf.PI / PatchMetres[cascade];
			float high = cascade == Cascades - 1 ? 1e6f : 2f * Mathf.PI / PatchMetres[cascade + 1];
			compute.SetFloat("_MinWaveNumber", low);
			compute.SetFloat("_MaxWaveNumber", high);
		}

		public void Dispose()
		{
			for (int i = 0; i < Cascades; i++)
			{
				Release(h0[i]);
				Release(spectrumA[i]);
				Release(spectrumB[i]);
				Release(displacement[i]);
				Release(derivatives[i]);
			}
		}

		private static void Release(RenderTexture texture)
		{
			if (texture == null)
			{
				return;
			}
			texture.Release();
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(texture);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(texture);
			}
		}
	}
}
