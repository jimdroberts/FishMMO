using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Cached static casters for the main light's cascaded shadows: the GPU-driven props' static casters (structures, rocks,
	/// cliffs — TerrainGpuRenderer.ShadowCache.cs) are drawn into a persistent copy of URP's cascade atlas only when a cascade
	/// changes, and that copy is merged, depth-tested, into URP's own shadow map every frame after URP has drawn the rest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a merge, not a replacement.</b> URP keeps its cascade slices and its shadow pass to itself, and every receiver
	/// (props, vegetation, GPU terrain, grass, scatter, water, the clouds' shadow reads) samples URP's map with URP's matrices.
	/// So URP still draws the shadow map — every live caster: trees and plants (their sway stays), characters, ships, scatter,
	/// grass, terrain — and only the batch group's static casters move out of it (TerrainGpuRenderer.Brg.cs leaves them out
	/// of the main light's culling while this serves the camera). After URP's pass the cache is merged into the same map with
	/// the depth test, so each texel keeps the nearer of the live and the cached depth: exactly what drawing them together
	/// gives. Nothing a receiver reads changes.
	/// </para>
	/// <para>
	/// <b>Windows, not URP's slices (2026-11-03).</b> URP re-fits each cascade to the camera every frame, snapped to its texels.
	/// Caching URP's slice itself meant a moving camera redrew every cascade nearly every frame (natively ~1,240 static draws a
	/// frame, +2 ms CPU, no saving). Each cascade is now cached over a <b>window</b>: URP's slice at the refresh with its
	/// orthographic projection widened by <see cref="MarginDivisor"/> of its texels on each side (and a quarter of its depth
	/// range at each end), at URP's own texel size and rotation. URP's slice in a later frame is the same orthographic grid
	/// moved by whole texels (stable cascades), so it maps into the window as a whole-texel shift and a depth offset
	/// (<see cref="TryMap"/>), and the merge copies from there. A cascade is redrawn only when URP's slice leaves its window
	/// (its tile or its depth range), the slice's rotation, texel size, resolution or bias changes (a sun step, a setting),
	/// the atlas changes, or the static casters themselves change (a layout or an instance upload: the camera no longer
	/// counts). A redraw culls the casters itself for the whole window, from the camera's position then
	/// (TerrainGpuRenderer.RecordShadowCacheCull): its levels chosen there and drawn whole (no crossfade dither, which would
	/// freeze mid-fade), so a cached shadow can hold an instance's level from up to a margin away until its window moves on.
	/// While caching, the sky lights' directions step on a fixed azimuth/elevation lattice (<see cref="SunStepDegrees"/>):
	/// the sun's direction is a pure function of the shared clock and the lattice is a pure function of the direction, so
	/// every client steps at the same world time; between steps the windows hold.
	/// </para>
	/// <para>
	/// <b>Limits.</b> A sun step redraws every cascade that frame. A redraw costs what every frame cost before (one indirect
	/// draw per static command; Unity pays a SetPass for each, whatever their order). Only the props' batch-group casters are
	/// cached; MeshRenderers, scatter and the GPU terrain stay live. Needs the instance index to carry the run's start
	/// (Vulkan; WebGPU with indirect-first-instance); elsewhere it does nothing.
	/// </para>
	/// </remarks>
	public static class CachedMainLightShadows
	{
		/// <summary>
		/// The switch (harness setting `cachedShadows`), on by default in browser players only (<see cref="DefaultOn"/>). Off: URP
		/// draws every caster every frame.
		/// </summary>
		public static bool Enabled = DefaultOn;

		/// <summary>
		/// Whether the cache starts on. The owner chose on in browser players and off natively (2026-11-03); it stays off
		/// everywhere until its merge is right. 2026-11-06: with URP's three cascades in the browser, the cache's image has dark
		/// areas the live map has nowhere (the live map with one cascade has none either), and the merge alone, with nothing
		/// cached (harness `shadowCacheDrawNothing`), still changed ~8% of the close view's pixels: on WebGPU the merge writes
		/// depth that no caster put there. The editor (Vulkan) merge wrote nothing in the same state (RenderDoc). See
		/// Docs/Rendering/2026-11-05/round-2.
		/// </summary>
		public const bool DefaultOn = false;

		/// <summary>Diagnostic: every frame drawn over exact windows (the refused frame's path), to test that path alone.</summary>
		public static bool DebugForceExact;

		/// <summary>Diagnostic: the merge overwrites URP's map with the cache (depth test off), showing only the cached casters.</summary>
		public static bool DebugOverwrite;

		/// <summary>
		/// Diagnostic: a raw depth the cache's tiles clear to instead of the far plane (negative: the far plane). With
		/// <see cref="DebugOverwrite"/> a mid value shades every cascade, which shows the merge reads the cache.
		/// </summary>
		public static float DebugClearDepth = -1f;

		/// <summary>Diagnostic: set to read back the static commands' instance counts once (both args buffers, probes).</summary>
		public static bool ReadbackRequested;

		/// <summary>Diagnosis (harness `shadowCacheLiveCull`): the cache draws the camera's own culling of the static casters, not its own.</summary>
		public static bool DebugLiveCull;
		/// <summary>The last readback: instances in the cache's args and in the batch group's, over the static commands, and how many readbacks landed.</summary>
		public static long ReadbackCacheInstances, ReadbackLiveInstances, ReadbackBadStarts;
		public static int ReadbacksLanded;

		/// <summary>The sky lights' direction lattice while caching, in degrees of azimuth and elevation.</summary>
		public static float SunStepDegrees = 0.1f;

		public const int MaxCascades = 4;
		public const string MergeShaderName = "Hidden/FishMMO/Shadow Cache Merge";

		/// <summary>Frames that redrew at least one cascade, cascades redrawn, and the last redraw's static draws (probes).</summary>
		public static int RefreshFrames { get; private set; }
		public static int RefreshedCascades { get; private set; }
		public static int LastStaticDraws { get; private set; }
		/// <summary>Frames the cache was merged into URP's map.</summary>
		public static int MergedFrames { get; private set; }
		/// <summary>
		/// Diagnosis: frames the cache pass or the merge did not merge, by reason (<see cref="SkipNames"/>; the browser probe logs
		/// them). While the cache is enabled the batch group leaves the static casters out of URP's culling, so every skip but the
		/// first four (no main-light shadows at all) is a frame whose structures cast nothing.
		/// </summary>
		public static readonly string[] SkipNames = { "no main light shadows", "not a shadowed directional light", "slice extraction", "no cascade", "refused: exact windows drawn", "exact windows impossible", "merge: cache not ready", "merge: no shadow map" };
		public static readonly int[] Skips = new int[8];
		private const int SkipNoShadows = 0, SkipNotDirectional = 1, SkipSlice = 2, SkipNoCascade = 3, SkipRefused = 4, SkipExactFailed = 5, SkipNotReady = 6, SkipNoShadowMap = 7;

		private static void Skip(int reason) => Skips[reason]++;

		/// <summary>The cache's counters as one line (the browser probe and the harness's `shadowCacheLog`).</summary>
		public static string Describe()
		{
			var line = new System.Text.StringBuilder();
			line.Append($"enabled {Enabled}, supported {Supported}, merged {MergedFrames}, redraw frames {RefreshFrames} ({TerrainGpuRenderer.CoverageRedraws} for new shadow work), maps {MapSuccesses}");
			for (int i = 0; i < Skips.Length; i++)
			{
				if (Skips[i] > 0)
				{
					line.Append($", [{SkipNames[i]} {Skips[i]}]");
				}
			}
			for (int i = 0; i < MapFailures.Length; i++)
			{
				if (MapFailures[i] > 0)
				{
					line.Append($", map refused [{MapFailureNames[i]} {MapFailures[i]}]");
				}
			}
			return line.ToString();
		}

		/// <summary>The shader the merge and the tile clears draw with (FishCloudsFeature carries it into builds).</summary>
		public static Shader MergeShader;

		/// <summary>Whether this device can draw the cached casters: their procedural variants need the run's start in the instance index.</summary>
		public static bool Supported => SystemInfo.supportsComputeShaders && TerrainGpuRenderer.InstanceIdCarriesBase;

		/// <summary>
		/// Whether the batch group leaves the static casters out of URP's culling for this camera: the cache serves it, and the
		/// last frame merged. Culling comes before the cache pass knows whether it can merge, so a frame after one that could not
		/// keeps them in URP's map (drawn twice if this one merges too: the depth test keeps one depth), and only the first frame
		/// of a failure lacks them. Before 2026-11-05 every refused frame did.
		/// </summary>
		public static bool OmitsStaticCasters(Camera camera) => AppliesTo(camera) && !lastFrameUnmerged && !DebugDrawNothing;

		/// <summary>
		/// Diagnosis (harness `shadowCacheDrawNothing`): the cache's passes run and set their state, but draw no casters, and the
		/// batch group keeps the static casters in URP's map: what the cache's passes do to a frame besides its casters.
		/// </summary>
		public static bool DebugDrawNothing;

		/// <summary>Diagnosis (harness `shadowCacheSkipPass`, `shadowCacheSkipCull`, `shadowCacheSkipMerge`): one of the cache's passes left out.</summary>
		public static bool DebugSkipPass, DebugSkipCull, DebugSkipMerge;

		/// <summary>Diagnosis (harness `shadowCacheMergeNoDraw`, `shadowCacheMergeNoState`): the merge pass without its draws, or without its depth bias and viewport.</summary>
		public static bool DebugMergeNoDraw, DebugMergeNoState;

		private static bool lastFrameUnmerged;

		/// <summary>Whether the cache serves this camera's main-light shadows this frame.</summary>
		public static bool AppliesTo(Camera camera) => Enabled && Supported && camera != null && camera.cameraType == CameraType.Game && MergeMaterial() != null;

		/// <summary>A sky light's direction for the light itself: on the lattice while caching, unchanged otherwise.</summary>
		public static Vector3 StableDirection(Vector3 towardLight) => Enabled && SunStepDegrees > 0f ? LatticeDirection(towardLight, SunStepDegrees) : towardLight;

		/// <summary>The nearest direction on an azimuth/elevation lattice of <paramref name="stepDegrees"/> (a pure function).</summary>
		public static Vector3 LatticeDirection(Vector3 towardLight, float stepDegrees)
		{
			double x = towardLight.x, y = towardLight.y, z = towardLight.z;
			double length = Math.Sqrt(x * x + y * y + z * z);
			if (length <= 0.0 || stepDegrees <= 0f)
			{
				return towardLight;
			}
			double step = stepDegrees * Math.PI / 180.0;
			double elevation = Math.Asin(Math.Max(-1.0, Math.Min(1.0, y / length)));
			double azimuth = Math.Atan2(x, z);
			elevation = Math.Round(elevation / step) * step;
			azimuth = Math.Round(azimuth / step) * step;
			double flat = Math.Cos(elevation);
			return new Vector3((float)(flat * Math.Sin(azimuth)), (float)Math.Sin(elevation), (float)(flat * Math.Cos(azimuth)));
		}

		/// <summary>How many of a slice's texels its window adds on each side: a quarter of the resolution (the tile is 1.5× URP's).</summary>
		public const int MarginDivisor = 4;

		/// <summary>One of URP's cascade slices this frame, as ShadowUtils.ExtractDirectionalLightMatrix gives it.</summary>
		public struct CascadeKey
		{
			public Matrix4x4 View;
			/// <summary>Orthographic, not yet the GPU's (cmd.SetViewProjectionMatrices takes this form).</summary>
			public Matrix4x4 Projection;
			public int OffsetX, OffsetY, Resolution;
			public Vector4 Bias;
			public Vector3 LightDirection;
		}

		/// <summary>A cascade's cached window: what its tile in the cache was drawn with, and for which slice shape.</summary>
		public struct Window
		{
			public Matrix4x4 View, Projection;
			public int TileX, TileY, Size;
			/// <summary>The slice it serves: its resolution, bias and light, and its width, height and depth range (far − near).</summary>
			public int Resolution;
			public Vector4 Bias;
			public Vector3 LightDirection;
			public float Width, Height, Depth;
			/// <summary>The window's own box in its view space (Matrix4x4.Ortho's arguments).</summary>
			public float Left, Right, Bottom, Top, Near, Far;
		}

		/// <summary>
		/// Where URP's slice reads its window this frame: cache atlas uv = slice NDC xy × <see cref="Scale"/> + <see cref="Offset"/>;
		/// URP raw depth = cache raw depth × <see cref="DepthScale"/> + <see cref="DepthOffset"/>.
		/// </summary>
		public struct Mapping
		{
			public Vector2 Scale, Offset;
			public float DepthScale, DepthOffset;
		}

		/// <summary>An orthographic projection's box (Matrix4x4.Ortho's arguments), or false when it is not a plain one.</summary>
		public static bool TryOrthoBox(Matrix4x4 p, out float left, out float right, out float bottom, out float top, out float near, out float far)
		{
			left = right = bottom = top = near = far = 0f;
			if (p.m30 != 0f || p.m31 != 0f || p.m32 != 0f || p.m33 != 1f || p.m00 == 0f || p.m11 == 0f || p.m22 == 0f
				|| p.m01 != 0f || p.m02 != 0f || p.m10 != 0f || p.m12 != 0f || p.m20 != 0f || p.m21 != 0f)
			{
				return false;
			}
			float width = 2f / p.m00, height = 2f / p.m11, depth = -2f / p.m22;
			float cx = -p.m03 * width * 0.5f, cy = -p.m13 * height * 0.5f, cz = -p.m23 * depth * 0.5f;
			left = cx - width * 0.5f;
			right = cx + width * 0.5f;
			bottom = cy - height * 0.5f;
			top = cy + height * 0.5f;
			near = cz - depth * 0.5f;
			far = cz + depth * 0.5f;
			return true;
		}

		/// <summary>
		/// A slice's window at a refresh: its view as it is, its projection widened by <paramref name="margin"/> texels on each
		/// side and by a quarter of its depth range at each end, in the tile at (<paramref name="tileX"/>, <paramref name="tileY"/>).
		/// False when the slice's projection is not a plain orthographic one (then nothing is cached).
		/// </summary>
		public static bool TryWindowFor(in CascadeKey slice, int margin, int tileX, int tileY, out Window window)
		{
			window = default;
			if (slice.Resolution <= 0 || !TryOrthoBox(slice.Projection, out float l, out float r, out float b, out float t, out float n, out float f))
			{
				return false;
			}
			float mx = margin * (r - l) / slice.Resolution, my = margin * (t - b) / slice.Resolution, mz = 0.25f * (f - n);
			window = new Window
			{
				View = slice.View,
				TileX = tileX,
				TileY = tileY,
				Size = slice.Resolution + 2 * margin,
				Resolution = slice.Resolution,
				Bias = slice.Bias,
				LightDirection = slice.LightDirection,
				Width = r - l,
				Height = t - b,
				Depth = f - n,
				Left = l - mx,
				Right = r + mx,
				Bottom = b - my,
				Top = t + my,
				Near = n - mz,
				Far = f + mz,
			};
			window.Projection = Matrix4x4.Ortho(window.Left, window.Right, window.Bottom, window.Top, window.Near, window.Far);
			return true;
		}

		/// <summary>
		/// A window that is URP's slice itself (no margin, its own view and projection) in the tile at
		/// (<paramref name="tileX"/>, <paramref name="tileY"/>): what a frame the margin windows cannot serve draws, merged by
		/// <see cref="TryMap"/> as any window. False when the slice's projection is not a plain orthographic one.
		/// </summary>
		public static bool TryExactWindowFor(in CascadeKey slice, int tileX, int tileY, out Window window)
		{
			window = default;
			if (slice.Resolution <= 0 || !TryOrthoBox(slice.Projection, out float l, out float r, out float b, out float t, out float n, out float f))
			{
				return false;
			}
			window = new Window
			{
				View = slice.View,
				Projection = slice.Projection,
				TileX = tileX,
				TileY = tileY,
				Size = slice.Resolution,
				Resolution = slice.Resolution,
				Bias = slice.Bias,
				LightDirection = slice.LightDirection,
				Width = r - l,
				Height = t - b,
				Depth = f - n,
				Left = l,
				Right = r,
				Bottom = b,
				Top = t,
				Near = n,
				Far = f,
			};
			return true;
		}


		/// <summary>
		/// How URP's <paramref name="slice"/> reads <paramref name="window"/> in a cache atlas of the given size, or false when the
		/// window must be redrawn: a different slice shape (resolution, bias, light, rotation, texel size, depth range), a shift
		/// that is not whole texels, or the slice outside the window's tile or depth range. A pure function (tests).
		/// </summary>
		/// <remarks>
		/// Measured through the GPU's matrices (renderIntoTexture), as URP's own shadow coordinates are, so every platform's
		/// depth range and y flip are its own: three points of the slice's NDC give the xy map, the slice's near and far points
		/// the depth map.
		/// </remarks>
		/// <summary>A point in double precision.</summary>
		public struct double3
		{
			public double x, y, z;
			public double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
			public static double3 operator -(double3 a, double3 b) => new double3(a.x - b.x, a.y - b.y, a.z - b.z);
		}

		/// <summary>A 4×4 matrix in double precision (TryMap's maths: world translations of kilometres, cascades of metres).</summary>
		public struct D4
		{
			private double[] m;

			public static D4 From(Matrix4x4 f)
			{
				var d = new D4 { m = new double[16] };
				for (int r = 0; r < 4; r++)
				{
					for (int c = 0; c < 4; c++)
					{
						d.m[r * 4 + c] = f[r, c];
					}
				}
				return d;
			}

			public static D4 operator *(D4 a, D4 b)
			{
				var d = new D4 { m = new double[16] };
				for (int r = 0; r < 4; r++)
				{
					for (int c = 0; c < 4; c++)
					{
						double sum = 0.0;
						for (int k = 0; k < 4; k++)
						{
							sum += a.m[r * 4 + k] * b.m[k * 4 + c];
						}
						d.m[r * 4 + c] = sum;
					}
				}
				return d;
			}

			/// <summary>The point transformed and divided by w.</summary>
			public double3 Point(double x, double y, double z)
			{
				double px = m[0] * x + m[1] * y + m[2] * z + m[3];
				double py = m[4] * x + m[5] * y + m[6] * z + m[7];
				double pz = m[8] * x + m[9] * y + m[10] * z + m[11];
				double pw = m[12] * x + m[13] * y + m[14] * z + m[15];
				return new double3(px / pw, py / pw, pz / pw);
			}

			public double3 Point(double3 p) => Point(p.x, p.y, p.z);

			/// <summary>The inverse (Gauss–Jordan with partial pivoting; the identity's zero for a singular matrix).</summary>
			public D4 Inverse()
			{
				var a = (double[])m.Clone();
				var inv = new double[16];
				for (int i = 0; i < 4; i++)
				{
					inv[i * 4 + i] = 1.0;
				}
				for (int col = 0; col < 4; col++)
				{
					int pivot = col;
					for (int r = col + 1; r < 4; r++)
					{
						if (Math.Abs(a[r * 4 + col]) > Math.Abs(a[pivot * 4 + col]))
						{
							pivot = r;
						}
					}
					if (Math.Abs(a[pivot * 4 + col]) < 1e-300)
					{
						return new D4 { m = new double[16] };
					}
					if (pivot != col)
					{
						for (int k = 0; k < 4; k++)
						{
							(a[col * 4 + k], a[pivot * 4 + k]) = (a[pivot * 4 + k], a[col * 4 + k]);
							(inv[col * 4 + k], inv[pivot * 4 + k]) = (inv[pivot * 4 + k], inv[col * 4 + k]);
						}
					}
					double scale = 1.0 / a[col * 4 + col];
					for (int k = 0; k < 4; k++)
					{
						a[col * 4 + k] *= scale;
						inv[col * 4 + k] *= scale;
					}
					for (int r = 0; r < 4; r++)
					{
						if (r == col)
						{
							continue;
						}
						double factor = a[r * 4 + col];
						if (factor == 0.0)
						{
							continue;
						}
						for (int k = 0; k < 4; k++)
						{
							a[r * 4 + k] -= factor * a[col * 4 + k];
							inv[r * 4 + k] -= factor * inv[col * 4 + k];
						}
					}
				}
				return new D4 { m = inv };
			}
		}

		/// <summary>Diagnosis: why TryMap last refused, and how often each reason has (<see cref="MapFailureNames"/>; the browser probe logs them).</summary>
		public static readonly string[] MapFailureNames = { "window size or resolution", "slice size", "view rotation", "axes not aligned", "texel scale", "tile outside window", "texel centres off", "depth range outside", "flat depth", "bias", "light direction", "not a plain ortho projection" };
		public static readonly int[] MapFailures = new int[12];
		public static int MapSuccesses;

		private static bool Fail(int reason)
		{
			MapFailures[reason]++;
			return false;
		}

		/// <summary>How far URP's texel centres may lie from the window's, in texels, for its texels to be copied one for one.</summary>
		public const double TexelCentreTolerance = 0.1;

		public static bool TryMap(in Window window, in CascadeKey slice, int atlasWidth, int atlasHeight, out Mapping map)
		{
			map = default;
			if (window.Size <= 0 || slice.Resolution != window.Resolution)
			{
				return Fail(0);
			}
			if (!slice.Bias.Equals(window.Bias))
			{
				return Fail(9);
			}
			if (!slice.LightDirection.Equals(window.LightDirection))
			{
				return Fail(10);
			}
			if (!TryOrthoBox(slice.Projection, out float l, out float r, out float b, out float t, out float n, out float f))
			{
				return Fail(11);
			}
			const float shape = 1e-4f;
			if (Mathf.Abs((r - l) - window.Width) > shape * window.Width || Mathf.Abs((t - b) - window.Height) > shape * window.Height
				|| Mathf.Abs((f - n) - window.Depth) > shape * window.Depth)
			{
				return Fail(1);
			}
			for (int row = 0; row < 3; row++)
			{
				for (int col = 0; col < 3; col++)
				{
					if (Mathf.Abs(slice.View[row, col] - window.View[row, col]) > 1e-5f)
					{
						return Fail(2);
					}
				}
			}
			/* In double precision (2026-11-05): the views carry world translations of thousands of metres and the near cascade is
			 * metres across, so float products and an inverse put a fresh window's texel centres a few hundredths of a texel off
			 * and TryMap refused the very slice it was built from (browser, settlement close: ~1 frame in 6). */
			D4 worldToSlice = D4.From(GL.GetGPUProjectionMatrix(slice.Projection, true)) * D4.From(slice.View);
			D4 sliceToWorld = worldToSlice.Inverse();
			D4 worldToCache = D4.From(GL.GetGPUProjectionMatrix(window.Projection, true)) * D4.From(window.View);
			D4 sliceToCache = worldToCache * sliceToWorld;
			double3 a0 = sliceToCache.Point(0.0, 0.0, 0.5);
			double3 ax = sliceToCache.Point(1.0, 0.0, 0.5) - a0;
			double3 ay = sliceToCache.Point(0.0, 1.0, 0.5) - a0;
			if (Math.Abs(ax.y) > shape || Math.Abs(ay.x) > shape)
			{
				return Fail(3);
			}
			// One slice texel (2 / resolution in NDC) is one cache texel (2 / size), each way.
			double size = window.Size;
			if (Math.Abs(Math.Abs(ax.x) * size / window.Resolution - 1.0) > 1e-3 || Math.Abs(Math.Abs(ay.y) * size / window.Resolution - 1.0) > 1e-3)
			{
				return Fail(4);
			}
			// The slice's tile within the window's (its corners), and its texel centres on the window's texel centres.
			for (int corner = 0; corner < 4; corner++)
			{
				double sx = (corner & 1) == 0 ? -1.0 : 1.0, sy = (corner & 2) == 0 ? -1.0 : 1.0;
				double u = ((a0.x + ax.x * sx) * 0.5 + 0.5) * size, v = ((a0.y + ay.y * sy) * 0.5 + 0.5) * size;
				if (u < -1e-2 || u > size + 1e-2 || v < -1e-2 || v > size + 1e-2)
				{
					return Fail(5);
				}
			}
			double firstX = ((a0.x + ax.x * (1.0 / window.Resolution - 1.0)) * 0.5 + 0.5) * size;
			double firstY = ((a0.y + ay.y * (1.0 / window.Resolution - 1.0)) * 0.5 + 0.5) * size;
			// A tenth of a texel: URP's slices are float matrices, and kilometres from the origin a cascade metres across moved
			// by whole texels lands up to ~0.03 of one off its grid (a float's step at 2 km is 0.12 mm); the copy reads the
			// nearest texel, so a shadow edge moves by at most this much.
			if (Math.Abs(firstX - Math.Floor(firstX) - 0.5) > TexelCentreTolerance || Math.Abs(firstY - Math.Floor(firstY) - 0.5) > TexelCentreTolerance)
			{
				return Fail(6);
			}
			// Depth: the slice's near and far points, in both maps; the slice's range inside the window's.
			D4 viewToWorld = D4.From(slice.View).Inverse();
			D4 windowView = D4.From(window.View);
			double3 nearPoint = viewToWorld.Point(0.0, 0.0, -n), farPoint = viewToWorld.Point(0.0, 0.0, -f);
			double nearInWindow = -windowView.Point(nearPoint).z, farInWindow = -windowView.Point(farPoint).z;
			double slack = 1e-4 * window.Depth;
			if (nearInWindow < window.Near - slack || farInWindow > window.Far + slack)
			{
				return Fail(7);
			}
			double sliceNear = worldToSlice.Point(nearPoint).z, sliceFar = worldToSlice.Point(farPoint).z;
			double cacheNear = worldToCache.Point(nearPoint).z, cacheFar = worldToCache.Point(farPoint).z;
			if (Math.Abs(cacheNear - cacheFar) < 1e-8)
			{
				return Fail(8);
			}
			double depthScale = (sliceNear - sliceFar) / (cacheNear - cacheFar);
			map.DepthScale = (float)depthScale;
			map.DepthOffset = (float)(sliceNear - depthScale * cacheNear);
			map.Scale = new Vector2((float)(ax.x * 0.5 * size / atlasWidth), (float)(ay.y * 0.5 * size / atlasHeight));
			map.Offset = new Vector2((float)(((a0.x * 0.5 + 0.5) * size + window.TileX) / atlasWidth), (float)(((a0.y * 0.5 + 0.5) * size + window.TileY) / atlasHeight));
			MapSuccesses++;
			return true;
		}

		/// <summary>
		/// The cache cull's planes for a window (TerrainGpuRenderer.RecordShadowCacheCull): its four sides, which run along the
		/// light, then two that pass everything; inside where dot(n, p) + w ≥ 0.
		/// </summary>
		public static void WindowPlanes(in Window window, Vector4[] planes)
		{
			Matrix4x4 v = window.View;
			planes[0] = new Vector4(v.m00, v.m01, v.m02, v.m03 - window.Left);
			planes[1] = new Vector4(-v.m00, -v.m01, -v.m02, window.Right - v.m03);
			planes[2] = new Vector4(v.m10, v.m11, v.m12, v.m13 - window.Bottom);
			planes[3] = new Vector4(-v.m10, -v.m11, -v.m12, window.Top - v.m13);
			planes[4] = planes[5] = new Vector4(0f, 0f, 0f, 1f);
		}

		/// <summary>The cache atlas for <paramref name="count"/> cascades of <paramref name="size"/>-texel tiles: two columns, as URP's.</summary>
		public static Vector2Int AtlasSize(int count, int size) => new Vector2Int(count == 1 ? size : 2 * size, count <= 2 ? size : 2 * size);

		/// <summary>A cascade's tile in that atlas.</summary>
		public static Vector2Int TileOf(int cascade, int count, int size) => count == 1 ? Vector2Int.zero : new Vector2Int(cascade % 2 * size, cascade / 2 * size);

		// ── State ──────────────────────────────────────────────────────

		private static Material mergeMaterial;
		private static RTHandle cache;
		private static int cacheWidth, cacheHeight, cachedCount = -1, cachedSize, cachedContent = int.MinValue;
		private static readonly Window[] windows = new Window[MaxCascades];
		private static readonly bool[] windowValid = new bool[MaxCascades];
		private static readonly CascadeKey[] currentKeys = new CascadeKey[MaxCascades];
		private static readonly Mapping[] mappings = new Mapping[MaxCascades];
		private static readonly Vector4[] cullPlanes = new Vector4[6];
		/// <summary>Whether this frame's cache is complete for the camera recording now (the merge pass reads it).</summary>
		private static bool mergeReady;
		private static bool matricesLogged;

		/// <summary>URP's main light shadow map's depth format as the merge pass last saw it, and the format the cache was made in.</summary>
		private static GraphicsFormat shadowFormat = GraphicsFormat.None, cachedFormat = GraphicsFormat.None;
		private static int mergeCascades;

		private static readonly int CacheId = Shader.PropertyToID("_FishShadowCache");
		private static readonly int CacheMapId = Shader.PropertyToID("_FishShadowCacheMap");
		private static readonly int CacheDepthMapId = Shader.PropertyToID("_FishShadowCacheDepthMap");
		private static readonly int ZTestId = Shader.PropertyToID("_FishShadowCacheZTest");
		private static readonly int ClearDepthId = Shader.PropertyToID("_FishShadowCacheClearDepth");
		private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
		private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
		private static readonly int LightPositionId = Shader.PropertyToID("_LightPosition");
		private static readonly int CameraPositionId = Shader.PropertyToID("_WorldSpaceCameraPos");
		private static readonly GlobalKeyword CastingPunctual = GlobalKeyword.Create("_CASTING_PUNCTUAL_LIGHT_SHADOW");

		private static Material MergeMaterial()
		{
			if (mergeMaterial != null)
			{
				return mergeMaterial;
			}
			Shader shader = MergeShader != null ? MergeShader : Shader.Find(MergeShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			mergeMaterial = CoreUtils.CreateEngineMaterial(shader);
			mergeMaterial.SetFloat(ZTestId, (float)MergeDepthTest(false));
			return mergeMaterial;
		}

		/// <summary>
		/// The merge's depth test: keep the nearer depth to the light, written as on a forward depth buffer (LessEqual), since
		/// Unity itself reverses a material's ZTest where the depth buffer is reversed (the casters' own "ZTest LEqual" reaches
		/// the GPU as greater-equal there). Passing GreaterEqual under a reversed buffer came out as less-equal, which kept the
		/// farther depth: no cached caster ever won, and an empty cache tile erased the live casters. The diagnostic overwrite
		/// copies the cache whatever it holds.
		/// </summary>
		public static CompareFunction MergeDepthTest(bool overwrite) => overwrite ? CompareFunction.Always : CompareFunction.LessEqual;

		/// <summary>The cache pass (before URP's shadows) and the merge pass (after them); enqueued by FishCloudsFeature.</summary>
		public static readonly ScriptableRenderPass CachePass = new CacheRenderPass();
		public static readonly ScriptableRenderPass MergePass = new MergeRenderPass();

		/// <summary>Releases the cache (and forgets every window), e.g. when switched off.</summary>
		public static void Release()
		{
			cache?.Release();
			cache = null;
			cacheWidth = cacheHeight = cachedSize = 0;
			cachedCount = -1;
			Array.Clear(windowValid, 0, windowValid.Length);
			mergeReady = false;
		}

		private sealed class CullPassData
		{
			public Camera Camera;
			public Vector3 Anchor;
			public readonly Vector4[] Planes = new Vector4[6];
			public Vector4 Light;
		}

		private sealed class CachePassData
		{
			public readonly Window[] Windows = new Window[MaxCascades];
			public int Mask, Count;
			public Vector3 CameraPosition;
			public Vector4 LightPosition;
			public Material Clear;
			public float ClearDepth;
		}

		private sealed class CacheRenderPass : ScriptableRenderPass
		{
			public CacheRenderPass()
			{
				renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				mergeReady = false;
				if (DebugSkipPass)
				{
					return;
				}
				UniversalCameraData camera = frameData.Get<UniversalCameraData>();
				if (!AppliesTo(camera.camera))
				{
					if (!Enabled && cache != null)
					{
						Release();
					}
					return;
				}
				UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
				UniversalLightData lights = frameData.Get<UniversalLightData>();
				UniversalShadowData shadows = frameData.Get<UniversalShadowData>();
				int light = lights.mainLightIndex;
				int count = shadows.mainLightShadowCascadesCount;
				if (!shadows.supportsMainLightShadows || camera.maxShadowDistance <= 0f || light < 0 || light >= lights.visibleLights.Length)
				{
					Skip(SkipNoShadows);
					return;
				}
				if (count < 1 || count > MaxCascades)
				{
					Skip(SkipNoCascade);
					return;
				}
				VisibleLight visible = lights.visibleLights[light];
				if (visible.lightType != LightType.Directional || visible.light == null || visible.light.shadows == LightShadows.None)
				{
					Skip(SkipNotDirectional);
					return;
				}
				// UniversalRenderPipeline.InitializeMainLightShadowResolution's sizes, and ShadowCulling's slices.
				int width = shadows.mainLightShadowmapWidth;
				int height = count == 2 ? shadows.mainLightShadowmapHeight >> 1 : shadows.mainLightShadowmapHeight;
				int resolution = ShadowUtils.GetMaxTileResolutionInAtlas(width, height, count);
				CullingResults cull = rendering.cullResults;
				Vector3 lightDirection = -visible.localToWorldMatrix.GetColumn(2);
				for (int i = 0; i < count; i++)
				{
					if (!ShadowUtils.ExtractDirectionalLightMatrix(ref cull, shadows, light, i, width, height, resolution, visible.light.shadowNearPlane, out _, out ShadowSliceData slice))
					{
						Skip(SkipSlice);
						return;
					}
					currentKeys[i] = new CascadeKey
					{
						View = slice.viewMatrix,
						Projection = slice.projectionMatrix,
						OffsetX = slice.offsetX,
						OffsetY = slice.offsetY,
						Resolution = slice.resolution,
						Bias = ShadowUtils.GetShadowBias(ref visible, light, shadows, slice.projectionMatrix, slice.resolution),
						LightDirection = lightDirection,
					};
				}
				if (!matricesLogged)
				{
					// Diagnosis: what this platform's slice and its GPU projection look like (the browser's mapping fails).
					matricesLogged = true;
					Debug.Log($"[Shadow cache] {SystemInfo.graphicsDeviceType}: slice 0 projection {currentKeys[0].Projection.ToString("F6")}; GPU {GL.GetGPUProjectionMatrix(currentKeys[0].Projection, true).ToString("F6")}; view {currentKeys[0].View.ToString("F4")}; resolution {resolution}, reversed z {SystemInfo.usesReversedZBuffer}");
				}
				int margin = resolution / MarginDivisor;
				int size = resolution + 2 * margin;
				Vector2Int atlas = AtlasSize(count, size);
				int content = Content();
				/* The cache in the depth format URP's own map is in on this device (seen by the merge pass, a frame late; 16-bit until
				 * then): a caster's depth bias is in units of its target's depth format, and the cache's casters are drawn with URP's
				 * bias, so a cache in another format kept another bias. In the browser that left the cached structures' grazing edges
				 * (roof eaves, wall corners) shaded unlike URP's own map (2026-11-05). */
				GraphicsFormat wantedFormat = GraphicsFormatUtility.IsDepthFormat(shadowFormat) ? shadowFormat : GraphicsFormat.None;
				bool atlasChanged = cache == null || cacheWidth != atlas.x || cacheHeight != atlas.y || cachedCount != count || cachedSize != size
					|| cachedFormat != wantedFormat;
				if (atlasChanged)
				{
					cache?.Release();
					cache = wantedFormat != GraphicsFormat.None
						? RTHandles.Alloc(atlas.x, atlas.y, wantedFormat, dimension: TextureDimension.Tex2D, filterMode: FilterMode.Point, name: "Fish Main Light Shadow Cache")
						: RTHandles.Alloc(atlas.x, atlas.y, depthBufferBits: DepthBits.Depth16, dimension: TextureDimension.Tex2D,
							filterMode: FilterMode.Point, name: "Fish Main Light Shadow Cache");
					cachedFormat = wantedFormat;
					cacheWidth = atlas.x;
					cacheHeight = atlas.y;
					cachedCount = count;
					cachedSize = size;
					Array.Clear(windowValid, 0, windowValid.Length);
				}
				bool contentChanged = content != cachedContent;
				// Each cascade read from its window where URP's slice still lies inside it, else redrawn over a new one.
				int mask = 0, widest = -1;
				bool refused = false;
				for (int i = 0; i < count; i++)
				{
					if (DebugForceExact)
					{
						refused = true;
						break;
					}
					if (!atlasChanged && !contentChanged && windowValid[i] && TryMap(in windows[i], in currentKeys[i], cacheWidth, cacheHeight, out mappings[i]))
					{
						continue;
					}
					Vector2Int tile = TileOf(i, count, size);
					if (!TryWindowFor(in currentKeys[i], margin, tile.x, tile.y, out windows[i]) || !TryMap(in windows[i], in currentKeys[i], cacheWidth, cacheHeight, out mappings[i]))
					{
						refused = true;
						break;
					}
					windowValid[i] = true;
					mask |= 1 << i;
					widest = i;
				}
				if (refused)
				{
					/* A slice this cache cannot read through a margin window this frame. The batch group has already left the static
					 * casters out of URP's culling (decided before this pass knows), so every cascade is drawn over a window that IS
					 * URP's slice (no margin, its own projection) and merged one texel to one texel at depth unchanged: until
					 * 2026-11-05 a refused frame had no structure shadows at all. */
					Skip(SkipRefused);
					mask = 0;
					for (int i = 0; i < count; i++)
					{
						Vector2Int tile = TileOf(i, count, size);
						if (!TryExactWindowFor(in currentKeys[i], tile.x, tile.y, out windows[i]))
						{
							Skip(SkipExactFailed);
							Array.Clear(windowValid, 0, windowValid.Length);
							return;
						}
						if (!TryMap(in windows[i], in currentKeys[i], cacheWidth, cacheHeight, out mappings[i]))
						{
							Skip(SkipExactFailed);
							Array.Clear(windowValid, 0, windowValid.Length);
							return;
						}
						windowValid[i] = true;
						mask |= 1 << i;
					}
					widest = count - 1;
				}
				mergeCascades = count;
				mergeReady = true;
				if (mask == 0)
				{
					return;
				}
				// The casters for the widest window redrawn (the farther cascades hold the nearer ones), culled for it from here.
				if (!DebugSkipCull)
				{
					AddCasterCull(renderGraph, camera, in windows[widest]);
				}
				TextureHandle target = renderGraph.ImportTexture(cache);
				using (var builder = renderGraph.AddRasterRenderPass<CachePassData>("Fish Main Light Shadow Cache", out CachePassData data))
				{
					Array.Copy(windows, data.Windows, count);
					data.Mask = mask;
					data.Count = count;
					data.CameraPosition = camera.worldSpaceCameraPos;
					data.LightPosition = visible.localToWorldMatrix.GetColumn(3);
					data.Clear = MergeMaterial();
					data.ClearDepth = ClearDepth();
					builder.SetRenderAttachmentDepth(target, AccessFlags.ReadWrite);
					builder.AllowPassCulling(false);
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc(static (CachePassData d, RasterGraphContext context) => DrawCache(d, context.cmd));
				}
				for (int i = 0; i < count; i++)
				{
					if ((mask & (1 << i)) != 0)
					{
						RefreshedCascades++;
					}
				}
				cachedContent = content;
				RefreshFrames++;
			}
		}

		/// <summary>
		/// The static casters culled for <paramref name="window"/> from the camera (TerrainGpuRenderer.RecordShadowCacheCull), into
		/// the cache's own visible list and args, which the cache's redraw or the direct draw then reads.
		/// </summary>
		private static void AddCasterCull(RenderGraph renderGraph, UniversalCameraData camera, in Window window)
		{
			using (var builder = renderGraph.AddUnsafePass<CullPassData>("Fish Main Light Shadow Cache Cull", out CullPassData cullData))
			{
				cullData.Camera = camera.camera;
				cullData.Anchor = camera.worldSpaceCameraPos;
				WindowPlanes(in window, cullData.Planes);
				float reach = camera.maxShadowDistance + Mathf.Max(window.Right - window.Left, window.Top - window.Bottom);
				Vector3 travel = -new Vector3(window.View.m20, window.View.m21, window.View.m22).normalized;
				cullData.Light = new Vector4(travel.x, travel.y, travel.z, reach);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc(static (CullPassData d, UnsafeGraphContext context) =>
				{
					CommandBuffer native = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
					foreach (TerrainGpuRenderer renderer in TerrainGpuRenderer.Live)
					{
						renderer.RecordShadowCacheCull(native, d.Camera, d.Anchor, d.Planes, d.Light);
					}
				});
			}
		}

		/// <summary>The raw depth the cache's tiles clear to: the far plane, or the diagnostic's.</summary>
		private static float ClearDepth() => DebugClearDepth >= 0f ? DebugClearDepth : SystemInfo.usesReversedZBuffer ? 0f : 1f;

		/// <summary>The static content's version over every live renderer serving the cache.</summary>
		private static int Content()
		{
			int hash = 17;
			foreach (TerrainGpuRenderer renderer in TerrainGpuRenderer.Live)
			{
				hash = hash * 31 + (renderer.ServesShadowCache ? renderer.ShadowContentVersion : -1);
			}
			return hash;
		}

		private static void DrawCache(CachePassData d, RasterCommandBuffer cmd)
		{
			// As URP's main-light shadow pass: the camera position, then per window its tile, matrices, depth bias and caster constants.
			cmd.SetGlobalVector(CameraPositionId, d.CameraPosition);
			cmd.SetKeyword(CastingPunctual, false);
			int draws = 0;
			for (int i = 0; i < d.Count; i++)
			{
				if ((d.Mask & (1 << i)) == 0)
				{
					continue;
				}
				Window w = d.Windows[i];
				cmd.SetViewport(new Rect(w.TileX, w.TileY, w.Size, w.Size));
				// The tile cleared to the far plane (the shader's own clip-space triangle, depth test off).
				cmd.SetGlobalDepthBias(0f, 0f);
				cmd.SetGlobalFloat(ClearDepthId, d.ClearDepth);
				cmd.DrawProcedural(Matrix4x4.identity, d.Clear, 1, MeshTopology.Triangles, 3);
				cmd.SetGlobalDepthBias(1.0f, 2.5f);
				cmd.SetViewProjectionMatrices(w.View, w.Projection);
				cmd.SetGlobalVector(ShadowBiasId, w.Bias);
				cmd.SetGlobalVector(LightDirectionId, new Vector4(w.LightDirection.x, w.LightDirection.y, w.LightDirection.z, 0f));
				cmd.SetGlobalVector(LightPositionId, new Vector4(d.LightPosition.x, d.LightPosition.y, d.LightPosition.z, 1f));
				foreach (TerrainGpuRenderer renderer in TerrainGpuRenderer.Live)
				{
					draws += renderer.RecordStaticShadowCasters(cmd);
				}
			}
			cmd.SetGlobalDepthBias(0f, 0f);
			LastStaticDraws = draws;
		}

		private sealed class MergePassData
		{
			public bool NoDraw, NoState;
			public readonly CascadeKey[] Keys = new CascadeKey[MaxCascades];
			public readonly Mapping[] Maps = new Mapping[MaxCascades];
			public int Count;
			public float ClearDepth;
			public TextureHandle Cache;
			public Material Merge;
		}

		private sealed class MergeRenderPass : ScriptableRenderPass
		{
			public MergeRenderPass()
			{
				renderPassEvent = RenderPassEvent.AfterRenderingShadows;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (!AppliesTo(frameData.Get<UniversalCameraData>().camera) || DebugSkipMerge)
				{
					return;
				}
				if (!mergeReady || cache == null)
				{
					Skip(SkipNotReady);
					lastFrameUnmerged = true;
					return;
				}
				mergeReady = false;
				UniversalResourceData resources = frameData.Get<UniversalResourceData>();
				TextureHandle shadowMap = resources.mainShadowsTexture;
				// URP's own map, not the 1×1 empty one it binds when it draws no shadows.
				if (!shadowMap.IsValid() || renderGraph.GetTextureDesc(shadowMap).width <= 1)
				{
					Skip(SkipNoShadowMap);
					return;
				}
				// Its depth format, which the cache takes from the next frame (see the cache pass).
				GraphicsFormat urpFormat = renderGraph.GetTextureDesc(shadowMap).format;
				if (urpFormat != shadowFormat)
				{
					Debug.Log($"[Shadow cache] {SystemInfo.graphicsDeviceType}: URP's main light shadow map is {urpFormat}; the cache was {(cache != null && cache.rt != null ? cache.rt.depthStencilFormat.ToString() : "none")}, now matches it.");
					shadowFormat = urpFormat;
				}
				using (var builder = renderGraph.AddRasterRenderPass<MergePassData>("Fish Main Light Shadow Cache Merge", out MergePassData data))
				{
					Array.Copy(currentKeys, data.Keys, mergeCascades);
					Array.Copy(mappings, data.Maps, mergeCascades);
					data.Count = mergeCascades;
					data.ClearDepth = ClearDepth();
					data.Cache = renderGraph.ImportTexture(cache);
					data.Merge = MergeMaterial();
					data.Merge.SetFloat(ZTestId, (float)MergeDepthTest(DebugOverwrite));
					builder.UseTexture(data.Cache, AccessFlags.Read);
					builder.SetRenderAttachmentDepth(shadowMap, AccessFlags.ReadWrite);
					builder.AllowPassCulling(false);
					builder.AllowGlobalStateModification(true);
					data.NoDraw = DebugMergeNoDraw;
					data.NoState = DebugMergeNoState;
					builder.SetRenderFunc(static (MergePassData d, RasterGraphContext context) =>
					{
						RasterCommandBuffer cmd = context.cmd;
						if (!d.NoState)
						{
							cmd.SetGlobalDepthBias(0f, 0f);
						}
						cmd.SetGlobalTexture(CacheId, d.Cache);
						cmd.SetGlobalFloat(ClearDepthId, d.ClearDepth);
						for (int i = 0; i < d.Count; i++)
						{
							CascadeKey key = d.Keys[i];
							Mapping map = d.Maps[i];
							// URP's tile; each texel reads its window's texel and its depth remapped to URP's range.
							if (!d.NoState)
							{
								cmd.SetViewport(new Rect(key.OffsetX, key.OffsetY, key.Resolution, key.Resolution));
							}
							cmd.SetGlobalVector(CacheMapId, new Vector4(map.Scale.x, map.Scale.y, map.Offset.x, map.Offset.y));
							cmd.SetGlobalVector(CacheDepthMapId, new Vector4(map.DepthScale, map.DepthOffset, 0f, 0f));
							if (!d.NoDraw)
							{
								cmd.DrawProcedural(Matrix4x4.identity, d.Merge, 0, MeshTopology.Triangles, 3);
							}
						}
					});
				}
				MergedFrames++;
				lastFrameUnmerged = false;
			}
		}
	}
}
