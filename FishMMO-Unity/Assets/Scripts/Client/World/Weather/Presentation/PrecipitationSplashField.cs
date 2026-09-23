using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Rings bursting where the rain lands (P5's ground collision and splashes).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Ground collision without a simulation.</b> <see cref="SkyOcclusionMap"/> is already a
	/// top-down heightfield of the highest surface around the camera, built from downward raycasts
	/// so that rain stops at roofs — and "the highest surface" is exactly where a falling drop ends
	/// up. So a splash needs no physics step and no particle to trace: the vertex shader reads the
	/// landing height out of a texture it can already sample, on every target including WebGL2.
	/// </para>
	/// <para>
	/// It also gets roofs right for nothing. A drop over a barn lands on the barn, because that is
	/// what the heightfield says is up there — no special case, and no splashes on the floor
	/// underneath it.
	/// </para>
	/// <para>
	/// <b>Additive to <see cref="PrecipitationField"/>, not a replacement.</b> The falling particles
	/// are tuned — drop size in heavy rain, streak length, the lean and turbulence that stopped them
	/// marching in lines — and rewriting that onto compute would put all of it at risk to gain
	/// nothing. This adds the part that genuinely did not exist.
	/// </para>
	/// </remarks>
	public sealed class PrecipitationSplashField
	{
		private static readonly int OriginId = Shader.PropertyToID("_FishSplashOrigin");
		private static readonly int ParamsId = Shader.PropertyToID("_FishSplashParams");
		private static readonly int ColorId = Shader.PropertyToID("_FishSplashColor");

		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private Mesh mesh;
		private int meshCount;

		/// <summary>How far out splashes are scattered, in metres.</summary>
		public float Radius = 22f;

		/// <summary>How big one splash gets at its widest, in metres.</summary>
		public float Size = 0.22f;

		/// <summary>Seconds from landing to gone.</summary>
		public float Lifetime = 0.45f;

		public void Dispose()
		{
			if (mesh != null)
			{
				Object.DestroyImmediate(mesh);
				mesh = null;
			}
			meshCount = 0;
		}

		/// <summary>
		/// Draws the splashes for this frame's weather.
		/// </summary>
		/// <param name="occlusionValid">
		/// Whether the height map has been built. Without it every splash would land at the same
		/// height, which on a slope is a sheet of rings hanging in the air.
		/// </param>
		public void Render(in WeatherFrame frame, Camera camera, WeatherTierSettings tier, WeatherRenderProfile profile,
			float time, bool occlusionValid, WeatherSubstance substance = null)
		{
			if (camera == null || profile == null || tier == null || profile.SplashMaterial == null || !occlusionValid)
			{
				return;
			}

			/* Liquid only. Snow settles, hail bounces and ash drifts — none of them throw up a ring
			 * of water, and splashing ash would read as rain falling on a volcano. */
			float rain = frame[WeatherChannel.Precipitation] * frame[WeatherChannel.RainWeight];
			if (rain <= 0.02f)
			{
				return;
			}
			// A substance that is not liquid does not splash either, whatever channel it rides on.
			if (substance != null && substance.Cover != WeatherCoverKind.Wet)
			{
				return;
			}

			int count = Mathf.Max(64, tier.Particles / 8);
			if (mesh == null || meshCount != count)
			{
				Dispose();
				mesh = BuildMesh(count);
				meshCount = count;
			}

			Vector3 origin = camera.transform.position;
			block.Clear();
			block.SetVector(OriginId, new Vector4(origin.x, origin.y, origin.z, time));
			block.SetVector(ParamsId, new Vector4(Radius, Mathf.Clamp01(rain * 1.4f), Size * Mathf.Lerp(0.7f, 1.4f, frame[WeatherChannel.DropSize]), Lifetime));

			Color tint = substance != null ? substance.Tint : new Color(0.82f, 0.88f, 0.96f);
			if (SkySystem.Instance != null)
			{
				// Lit by this world's sky, like the drops that made them.
				tint = SkySystem.Instance.InAir(tint);
			}
			block.SetColor(ColorId, new Color(tint.r, tint.g, tint.b, 0.5f));

			var rp = new RenderParams(profile.SplashMaterial)
			{
				camera = camera,
				matProps = block,
				worldBounds = new Bounds(origin, Vector3.one * Radius * 2.5f),
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		/// <summary>
		/// One flat quad per splash, carrying its index. The vertex shader does the placing.
		/// </summary>
		/// <remarks>
		/// Built once and reused. The quads lie in the XZ plane rather than facing the camera: a
		/// splash is ON the ground, and a billboard would stand it up like a card the moment anyone
		/// looked from a low angle.
		/// </remarks>
		private static Mesh BuildMesh(int count)
		{
			var vertices = new Vector3[count * 4];
			var uvs = new Vector2[count * 4];
			var data = new Vector4[count * 4];
			var indices = new int[count * 6];

			for (int i = 0; i < count; i++)
			{
				int v = i * 4;
				vertices[v + 0] = new Vector3(-1f, 0f, -1f);
				vertices[v + 1] = new Vector3(1f, 0f, -1f);
				vertices[v + 2] = new Vector3(1f, 0f, 1f);
				vertices[v + 3] = new Vector3(-1f, 0f, 1f);
				uvs[v + 0] = new Vector2(0f, 0f);
				uvs[v + 1] = new Vector2(1f, 0f);
				uvs[v + 2] = new Vector2(1f, 1f);
				uvs[v + 3] = new Vector2(0f, 1f);
				for (int c = 0; c < 4; c++)
				{
					data[v + c] = new Vector4(i, 0f, 0f, 0f);
				}
				int t = i * 6;
				indices[t + 0] = v + 0; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
				indices[t + 3] = v + 0; indices[t + 4] = v + 3; indices[t + 5] = v + 2;
			}

			var built = new Mesh
			{
				name = "FishPrecipitationSplash",
				// Well past 65k vertices at the top tier, so 32-bit indices are not optional.
				indexFormat = IndexFormat.UInt32,
				hideFlags = HideFlags.HideAndDontSave,
			};
			built.SetVertices(vertices);
			built.SetUVs(0, uvs);
			built.SetUVs(1, data);
			built.SetIndices(indices, MeshTopology.Triangles, 0, false);
			// The vertex shader moves every quad, so a bounds computed from the source positions
			// would cull the lot the moment the camera looked away from the origin.
			built.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
			return built;
		}
	}
}
