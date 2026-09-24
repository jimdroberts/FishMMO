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
	/// <b>Two ways to run it, one sea.</b> Compute where the machine has it; otherwise the same
	/// arithmetic as render passes (<c>FishWaterFFT.shader</c>), which is what WebGL2 and GLES3 get.
	/// Both include FishWaterSpectrum.hlsl, so they draw the same waves from the same seed, and the
	/// textures they leave behind are the same — nothing downstream knows which one ran.
	/// </para>
	/// <para>
	/// <b>The dispersion is quantised to a loop period.</b> That costs nothing visible and makes
	/// the whole field exactly periodic.
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

		private static readonly int H0SourceId = Shader.PropertyToID("_H0Source");
		private static readonly int SourceAId = Shader.PropertyToID("_SourceA");
		private static readonly int SourceBId = Shader.PropertyToID("_SourceB");
		private static readonly int StageId = Shader.PropertyToID("_Stage");
		private static readonly int HorizontalId = Shader.PropertyToID("_Horizontal");
		private static readonly int SeedValueId = Shader.PropertyToID("_SeedValue");

		private readonly ComputeShader compute;

		// The render-pass path: null when compute is running it.
		private readonly Material passes;
		private readonly int initialPass, timePass, butterflyPass, assemblePass;
		private readonly RenderTexture scratchA;
		private readonly RenderTexture scratchB;
		private readonly RenderBuffer[] pair = new RenderBuffer[2];
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

		/// <summary>True when this machine can run the transform one way or the other.</summary>
		public static bool Supported => ComputeSupported || PassesSupported;

		/// <summary>True when the compute version can run.</summary>
		public static bool ComputeSupported => SystemInfo.supportsComputeShaders
			&& SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.LoadStore);

		/// <summary>
		/// True when the render-pass version can run: two targets at once, and a float format it can
		/// render the spectrum into.
		/// </summary>
		public static bool PassesSupported => SystemInfo.supportedRenderTargetCount >= 2 && WorkingFormat != GraphicsFormat.None;

		/// <summary>True when this instance is running as render passes rather than compute.</summary>
		public bool UsingPasses => passes != null;

		/* The spectrum is summed 65,536 ways, so it wants full float where the machine can render to
		 * it; half float is the floor, and the one every WebGL2 with float targets has. */
		private static GraphicsFormat WorkingFormat =>
			SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R32G32B32A32_SFloat
			: SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R16G16B16A16_SFloat
			: GraphicsFormat.None;

		/* The results are SAMPLED, with a bilinear filter, by the sea's vertex and fragment stages —
		 * and WebGL2 cannot filter full-float textures without an extension many devices lack; the
		 * texture then reads as nothing at all. Half float filters everywhere and holds a
		 * displacement to millimetres. */
		private static GraphicsFormat OutputFormat =>
			SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.Render)
				&& SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat, GraphicsFormatUsage.Linear) ? GraphicsFormat.R32G32B32A32_SFloat
			: GraphicsFormat.R16G16B16A16_SFloat;

		/// <summary>
		/// The transform this machine can run: compute where it can, render passes where it cannot,
		/// and null where neither is possible.
		/// </summary>
		/// <param name="forcePasses">
		/// Run the render passes even where compute is there — the only way to see the WebGL path
		/// on a desktop, and to put the two side by side.
		/// </param>
		public static WaterFFT Create(ComputeShader computeShader, Shader passesShader, bool forcePasses = false)
		{
			if (computeShader != null && ComputeSupported && !(forcePasses && passesShader != null && PassesSupported))
			{
				return new WaterFFT(computeShader);
			}
			if (passesShader != null && passesShader.isSupported && PassesSupported)
			{
				return new WaterFFT(passesShader);
			}
			return null;
		}

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

		/// <summary>The render-pass version, for machines with no compute shaders.</summary>
		public WaterFFT(Shader passesShader)
		{
			passes = new Material(passesShader) { name = "Water FFT passes", hideFlags = HideFlags.HideAndDontSave };
			initialPass = passes.FindPass("InitialSpectrum");
			timePass = passes.FindPass("TimeSpectrum");
			butterflyPass = passes.FindPass("Butterfly");
			assemblePass = passes.FindPass("Assemble");

			GraphicsFormat working = WorkingFormat;
			GraphicsFormat output = OutputFormat;
			for (int i = 0; i < Cascades; i++)
			{
				h0[i] = CreateTarget("FFT h0 " + i, working, FilterMode.Point);
				spectrumA[i] = CreateTarget("FFT spectrum A " + i, working, FilterMode.Point);
				spectrumB[i] = CreateTarget("FFT spectrum B " + i, working, FilterMode.Point);
				displacement[i] = CreateTarget("FFT displacement " + i, output, FilterMode.Bilinear);
				derivatives[i] = CreateTarget("FFT derivatives " + i, output, FilterMode.Bilinear);
			}
			// The butterflies ping-pong through one spare pair, shared: the cascades run one at a time.
			scratchA = CreateTarget("FFT scratch A", working, FilterMode.Point);
			scratchB = CreateTarget("FFT scratch B", working, FilterMode.Point);
		}

		private static RenderTexture CreateTarget(string name, GraphicsFormat format, FilterMode filter)
		{
			var texture = new RenderTexture(Size, Size, 0, format)
			{
				name = name,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = filter,
				useMipMap = false,
				hideFlags = HideFlags.HideAndDontSave,
			};
			texture.Create();
			return texture;
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

			if (passes != null)
			{
				RenderTexture previous = RenderTexture.active;
				for (int i = 0; i < Cascades; i++)
				{
					BindPasses(i);
					Draw(initialPass, h0[i], null);
				}
				RenderTexture.active = previous;
				return;
			}
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
			if (passes != null)
			{
				EvaluatePasses(seconds, stages);
				return;
			}
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

		/// <summary>
		/// One frame of the render-pass version: evolve, eight butterfly passes along the rows and
		/// eight down the columns, then assemble — per cascade, drawn at once like a dispatch.
		/// </summary>
		private void EvaluatePasses(float seconds, int stages)
		{
			RenderTexture previous = RenderTexture.active;
			for (int i = 0; i < Cascades; i++)
			{
				BindPasses(i);
				passes.SetFloat("_FFTTime", seconds);
				passes.SetTexture(H0SourceId, h0[i]);
				Draw(timePass, spectrumA[i], spectrumB[i]);
				if (stages < 2)
				{
					continue;
				}

				Transform(i, true);
				if (stages < 3)
				{
					continue;
				}
				Transform(i, false);
				if (stages < 4)
				{
					continue;
				}

				passes.SetTexture(SourceAId, spectrumA[i]);
				passes.SetTexture(SourceBId, spectrumB[i]);
				Draw(assemblePass, displacement[i], derivatives[i]);
			}
			RenderTexture.active = previous;
		}

		/// <summary>
		/// One axis of the inverse transform. Eight stages, ping-ponging through the scratch pair — an
		/// even number, so the result lands back in the cascade's own spectrum textures.
		/// </summary>
		private void Transform(int cascade, bool horizontal)
		{
			RenderTexture sourceA = spectrumA[cascade], sourceB = spectrumB[cascade];
			RenderTexture targetA = scratchA, targetB = scratchB;
			passes.SetFloat(HorizontalId, horizontal ? 1f : 0f);
			for (int stage = 1; stage <= LogSize; stage++)
			{
				passes.SetFloat(StageId, stage);
				passes.SetTexture(SourceAId, sourceA);
				passes.SetTexture(SourceBId, sourceB);
				Draw(butterflyPass, targetA, targetB);
				(sourceA, targetA) = (targetA, sourceA);
				(sourceB, targetB) = (targetB, sourceB);
			}
		}

		private const int LogSize = 8;

		/// <summary>Draws one full-target triangle with a pass, into one target or two at once.</summary>
		private void Draw(int pass, RenderTexture targetA, RenderTexture targetB)
		{
			if (targetB != null)
			{
				pair[0] = targetA.colorBuffer;
				pair[1] = targetB.colorBuffer;
				Graphics.SetRenderTarget(pair, targetA.depthBuffer);
			}
			else
			{
				Graphics.SetRenderTarget(targetA);
			}
			passes.SetPass(pass);
			Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
		}

		/// <summary>The same parameters <see cref="Bind"/> gives the compute version, on the pass material.</summary>
		private void BindPasses(int cascade)
		{
			passes.SetFloat("_PatchMetres", PatchMetres[cascade]);
			passes.SetFloat("_WindSpeed", Wind);
			passes.SetVector("_WindDirection", new Vector4(WindDirection.x, WindDirection.y, 0f, 0f));
			passes.SetFloat("_Gravity", Gravity);
			passes.SetFloat("_Amplitude", Amplitude);
			passes.SetFloat("_Choppiness", Choppiness);
			passes.SetFloat("_LoopPeriod", LoopPeriod);
			passes.SetFloat(SeedValueId, Seed + (uint)cascade * 7919u);
			passes.SetFloat("_SmallWave", PatchMetres[cascade] / Size * 2f);
			passes.SetFloat("_Directionality", 4f);
			float low = 2f * Mathf.PI / PatchMetres[cascade];
			float high = cascade == Cascades - 1 ? 1e6f : 2f * Mathf.PI / PatchMetres[cascade + 1];
			passes.SetFloat("_MinWaveNumber", low);
			passes.SetFloat("_MaxWaveNumber", high);
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
			Release(scratchA);
			Release(scratchB);
			if (passes != null)
			{
				if (Application.isPlaying)
				{
					UnityEngine.Object.Destroy(passes);
				}
				else
				{
					UnityEngine.Object.DestroyImmediate(passes);
				}
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
