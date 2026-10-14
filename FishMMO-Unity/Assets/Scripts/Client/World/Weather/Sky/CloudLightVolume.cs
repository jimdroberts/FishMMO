using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The cloud's density on a coarse grid round the camera, for the light march to read instead of the whole field.
	/// </summary>
	/// <remarks>
	/// The light march from every cloud sample steps toward the sun in six segments, out to almost three kilometres,
	/// and each segment asked the field for its density: the air, storm, terrain, meso and tower maps and the
	/// shape noise, six times a sample. In a storm, with towers filling the view, that was most of a frame. The
	/// segments past the first two are hundreds of metres long and want the cloud's average there, which is what a
	/// texel of this grid holds (drawn at a footprint of a texel, the field eases to its mean). The first two stay
	/// live: they light a cloud's edge, and are finer than the grid.
	///
	/// Drawn by the clouds' own CloudLightVolume pass from FishCloudDensity, so it is the same cloud. Slices of the
	/// volume are tiled into one 2D texture, a band of them a frame, into a back buffer that is swapped in whole
	/// once every slice is drawn: a cloud drifts a few metres in that time against texels of three hundred.
	/// </remarks>
	public sealed class CloudLightVolume
	{
		/// <summary>The volume's GPU time, as a marker the profiler's GPU module and ScenePerfProbe see (a string sample has no GPU timing).</summary>
		private static readonly CustomSampler GpuSample = CustomSampler.Create("Cloud Light Volume", true);

		/// <summary>Off: every light segment asks the field again (the probe's A/B).</summary>
		public static bool Enabled = true;
		/// <summary>Texels along a side of each slice.</summary>
		public const int Texels = 160;
		/// <summary>Slices through the cloud shell.</summary>
		public const int Slices = 48;
		/// <summary>Slices along a row of the texture.</summary>
		public const int TilesAcross = 8;
		/// <summary>
		/// Slices drawn a frame: the whole volume every 24 frames. At 8 a frame (every 6) the build cost 0.55 ms of GPU,
		/// more than the light march saved in an overcast (ScenePerfProbe 2026-10-07); a cloud drifts a few metres in
		/// 24 frames against texels of three hundred.
		/// </summary>
		public const int SlicesPerFrame = 2;

		/// <summary>
		/// Seconds a whole rebuild takes once the volume is up: slices are drawn as time owes them, not a band a frame,
		/// which at 150–300 fps rebuilt it every 0.1–0.2 s against texels three hundred metres wide that a cloud takes
		/// minutes to cross. The first build still draws <see cref="SlicesPerFrame"/> a frame, so it is up as soon as before.
		/// 0: a band a frame always, as before (the probe's A/B).
		/// </summary>
		public static float BuildSeconds = 2f;
		private float owed;
		/// <summary>The grid reaches this far past the camera's far plane: the light march's own reach, and some.</summary>
		public const float MarginMetres = 3500f;
		/// <summary>And no farther than this, however far the camera sees.</summary>
		public const float MaxReachMetres = 30000f;

		private static readonly int VolumeId = Shader.PropertyToID("_FishCloudLightVolume");
		private static readonly int VolumeAId = Shader.PropertyToID("_FishCloudLightVolumeA");
		private static readonly int VolumeBId = Shader.PropertyToID("_FishCloudLightVolumeB");
		private static readonly int VolumeCId = Shader.PropertyToID("_FishCloudLightVolumeC");
		private static readonly int BuildAId = Shader.PropertyToID("_FishCloudLightBuildA");
		private static readonly int BuildBId = Shader.PropertyToID("_FishCloudLightBuildB");

		private static int Rows => (Slices + TilesAcross - 1) / TilesAcross;

		private RenderTexture front;
		private RenderTexture back;
		private CommandBuffer commands;
		private int part = -1;
		private Vector4 buildA;
		private Vector4 buildB;
		private bool published;

		// The pass's index, looked up by name once for a material and its shader (FindPass compares strings).
		private Material passOf;
		private Shader passShader;
		private int passCount = -1;
		private int passIndex = -1;

		private int PassOf(Material cloudMaterial)
		{
			if (cloudMaterial == null)
			{
				return -1;
			}
			Shader shader = cloudMaterial.shader;
			int count = cloudMaterial.passCount;
			if (cloudMaterial != passOf || shader != passShader || count != passCount)
			{
				passOf = cloudMaterial;
				passShader = shader;
				passCount = count;
				passIndex = cloudMaterial.FindPass("CloudLightVolume");
			}
			return passIndex;
		}

		/// <summary>
		/// Draws the next band of slices round the viewer, and swaps the volume in when the last is drawn.
		/// </summary>
		/// <param name="far">The camera's far plane: the march goes no farther, and the grid covers it.</param>
		/// <param name="bottom">The cloud shell's floor (m).</param>
		/// <param name="top">Its top (m).</param>
		public void Update(Material cloudMaterial, Vector3 viewer, float far, float bottom, float top, bool enabled)
		{
			int pass = PassOf(cloudMaterial);
			if (!Enabled || !enabled || pass < 0 || !(top > bottom))
			{
				Clear();
				return;
			}
			if (front == null)
			{
				GraphicsFormat format = GraphicsFormat.R16_SFloat;
				if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
				{
					format = GraphicsFormat.R16G16B16A16_SFloat;
				}
				int width = TilesAcross * Texels, height = Rows * Texels;
				front = Create(width, height, format, "Cloud Light Volume");
				back = Create(width, height, format, "Cloud Light Volume (building)");
				commands = new CommandBuffer { name = "Cloud Light Volume" };
				part = -1;
			}
			if (part < 0)
			{
				// A new build: the grid round where the viewer is now, snapped to a texel so a still camera keeps
				// its grid, and through the shell as it stands now.
				float half = Mathf.Min(Mathf.Max(far, 1000f), MaxReachMetres) + MarginMetres;
				float texel = 2f * half / Texels;
				float x = Mathf.Round(viewer.x / texel) * texel - half;
				float z = Mathf.Round(viewer.z / texel) * texel - half;
				buildA = new Vector4(x, z, texel, Texels);
				buildB = new Vector4(bottom, (top - bottom) / Slices, Slices, TilesAcross);
				part = 0;
				owed = 0f;
			}
			// `part` is the next slice. Owed by time; the first build a band a frame, as it always was.
			owed += BuildSeconds > 0f ? Slices * Mathf.Min(Time.unscaledDeltaTime, 0.25f) / BuildSeconds : 0f;
			int count = Mathf.Min(Slices - part, BuildSeconds <= 0f ? SlicesPerFrame : published ? (int)owed : Mathf.Max(SlicesPerFrame, (int)owed));
			if (count <= 0)
			{
				return;
			}
			owed = Mathf.Max(0f, owed - count);
			commands.Clear();
			commands.BeginSample(GpuSample);
			commands.SetRenderTarget(back);
			commands.SetGlobalVector(BuildAId, buildA);
			commands.SetGlobalVector(BuildBId, buildB);
			// This frame's slices, a tile each; the pass works out each pixel's slice from where it is.
			for (int k = 0; k < count; k++)
			{
				int slice = part + k;
				if (slice >= Slices)
				{
					break;
				}
				commands.SetViewport(new Rect(slice % TilesAcross * Texels, slice / TilesAcross * Texels, Texels, Texels));
				commands.DrawProcedural(Matrix4x4.identity, cloudMaterial, pass, MeshTopology.Triangles, 3);
			}
			commands.EndSample(GpuSample);
			Graphics.ExecuteCommandBuffer(commands);
			part += count;
			if (part >= Slices)
			{
				(front, back) = (back, front);
				Shader.SetGlobalTexture(VolumeId, front);
				Shader.SetGlobalVector(VolumeAId, buildA);
				Shader.SetGlobalVector(VolumeBId, buildB);
				Shader.SetGlobalVector(VolumeCId, new Vector4(1f, 0f, 0f, 0f));
				published = true;
				part = -1;
			}
		}

		/// <summary>No volume: the light march asks the field all the way, as it did.</summary>
		public void Clear()
		{
			if (published || part >= 0)
			{
				Shader.SetGlobalVector(VolumeCId, Vector4.zero);
				published = false;
			}
			part = -1;
		}

		public void Dispose()
		{
			Clear();
			Release(ref front);
			Release(ref back);
			commands?.Release();
			commands = null;
		}

		private static RenderTexture Create(int width, int height, GraphicsFormat format, string name)
		{
			var texture = new RenderTexture(width, height, 0, format)
			{
				name = name,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				useMipMap = false,
				hideFlags = HideFlags.DontSave,
			};
			texture.Create();
			return texture;
		}

		private static void Release(ref RenderTexture texture)
		{
			if (texture == null)
			{
				return;
			}
			texture.Release();
			if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
			texture = null;
		}
	}
}
