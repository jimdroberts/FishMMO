using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws what is falling around the camera: one pre-built field of quads per quality budget,
	/// drawn once per kind that is falling, animated entirely in the vertex shader.
	/// </summary>
	public sealed class PrecipitationField
	{
		/// <summary>Kinds drawn at once, strongest first.</summary>
		public const int MaxKindsAtOnce = 3;

		private static readonly int OriginId = Shader.PropertyToID("_PrecipOrigin");
		private static readonly int BoxId = Shader.PropertyToID("_PrecipBox");
		private static readonly int FallId = Shader.PropertyToID("_PrecipFall");
		private static readonly int ShapeId = Shader.PropertyToID("_PrecipShape");
		private static readonly int FlutterId = Shader.PropertyToID("_PrecipFlutter");
		private static readonly int ColorId = Shader.PropertyToID("_PrecipColor");
		private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

		private static readonly WeatherChannel[] Kinds =
		{
			WeatherChannel.RainWeight, WeatherChannel.SnowWeight, WeatherChannel.HailWeight, WeatherChannel.AshWeight, WeatherChannel.SandWeight,
		};

		private Mesh mesh;
		private int meshParticles;
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private readonly List<(WeatherChannel kind, float amount)> drawn = new List<(WeatherChannel, float)>(MaxKindsAtOnce);

		/// <summary>The kinds drawn last frame and how much of each.</summary>
		public IReadOnlyList<(WeatherChannel kind, float amount)> Drawn => drawn;

		/// <summary>
		/// A field of <paramref name="particles"/> quads. Every corner of a quad shares its
		/// position in the unit box; the shader places and shapes it.
		/// </summary>
		public static Mesh BuildMesh(int particles, int seed = 1234)
		{
			particles = Mathf.Max(1, particles);
			var random = new System.Random(seed);
			var positions = new Vector3[particles * 4];
			var corners = new Vector2[particles * 4];
			var randoms = new List<Vector4>(particles * 4);
			var indices = new int[particles * 6];
			for (int i = 0; i < particles; i++)
			{
				var p = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				// The show threshold is an even ramp so density scales linearly with the share shown.
				var r = new Vector4((i + 0.5f) / particles, (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				int v = i * 4;
				for (int c = 0; c < 4; c++)
				{
					positions[v + c] = p;
					randoms.Add(r);
				}
				corners[v] = new Vector2(0f, 0f);
				corners[v + 1] = new Vector2(1f, 0f);
				corners[v + 2] = new Vector2(1f, 1f);
				corners[v + 3] = new Vector2(0f, 1f);
				int t = i * 6;
				indices[t] = v;
				indices[t + 1] = v + 2;
				indices[t + 2] = v + 1;
				indices[t + 3] = v;
				indices[t + 4] = v + 3;
				indices[t + 5] = v + 2;
			}
			// Shuffle the thresholds so the visible share is spread through the box, not in index order.
			for (int i = particles - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				float a = randoms[i * 4].x, b = randoms[j * 4].x;
				for (int c = 0; c < 4; c++)
				{
					Vector4 ri = randoms[i * 4 + c];
					Vector4 rj = randoms[j * 4 + c];
					ri.x = b;
					rj.x = a;
					randoms[i * 4 + c] = ri;
					randoms[j * 4 + c] = rj;
				}
			}
			var mesh = new Mesh
			{
				name = $"Precipitation Field ({particles})",
				indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
				hideFlags = HideFlags.DontSave,
			};
			mesh.SetVertices(positions);
			mesh.SetUVs(0, corners);
			mesh.SetUVs(1, randoms);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
			return mesh;
		}

		/// <summary>The kinds worth drawing, strongest first, with each one's visible share.</summary>
		public static void Choose(in WeatherFrame frame, List<(WeatherChannel kind, float amount)> into)
		{
			into.Clear();
			float p = frame[WeatherChannel.Precipitation];
			if (p <= 0.005f)
			{
				return;
			}
			foreach (WeatherChannel kind in Kinds)
			{
				float amount = p * frame[kind];
				if (amount > 0.01f)
				{
					into.Add((kind, Mathf.Clamp01(amount)));
				}
			}
			into.Sort((a, b) => b.amount.CompareTo(a.amount));
			if (into.Count > MaxKindsAtOnce)
			{
				into.RemoveRange(MaxKindsAtOnce, into.Count - MaxKindsAtOnce);
			}
		}

		/// <summary>The fall velocity of a kind: down at its speed, pushed by the wind.</summary>
		public static Vector3 FallVelocity(in WeatherFrame frame, PrecipitationLook look, float gust)
		{
			float drop = frame[WeatherChannel.DropSize];
			float fall = Mathf.Lerp(look.FallSpeed.x, look.FallSpeed.y, drop);
			Vector2 dir = WeatherShaderGlobals.WindDirection(frame[WeatherChannel.WindHeading]);
			float wind = frame[WeatherChannel.WindSpeed] * 30f * (1f + gust) * look.WindResponse;
			return new Vector3(dir.x * wind, -fall, dir.y * wind);
		}

		public void Render(in WeatherFrame frame, Camera camera, WeatherTierSettings tier, WeatherRenderProfile profile, float time)
		{
			Choose(frame, drawn);
			if (drawn.Count == 0 || camera == null || profile.PrecipitationMaterial == null)
			{
				return;
			}
			if (mesh == null || meshParticles != tier.Particles)
			{
				Dispose();
				mesh = BuildMesh(tier.Particles);
				meshParticles = tier.Particles;
			}

			Vector3 origin = camera.transform.position;
			float gust = frame[WeatherChannel.WindGust] * (0.5f + 0.5f * Mathf.Sin(time * 0.7f) * Mathf.Sin(time * 0.23f + 1f));
			var rp = new RenderParams(profile.PrecipitationMaterial)
			{
				camera = camera,
				matProps = block,
				worldBounds = new Bounds(origin, Vector3.one * tier.BoxSize * 2f),
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			foreach ((WeatherChannel kind, float amount) in drawn)
			{
				PrecipitationLook look = profile.LookOf(kind);
				float drop = frame[WeatherChannel.DropSize];
				Vector3 fall = FallVelocity(frame, look, gust);
				block.Clear();
				if (profile.PrecipitationAtlas != null)
				{
					block.SetTexture(MainTexId, profile.PrecipitationAtlas);
				}
				block.SetVector(OriginId, new Vector4(origin.x, origin.y, origin.z, time));
				block.SetVector(BoxId, new Vector4(tier.BoxSize, tier.BoxSize * 0.75f, tier.BoxSize, 0.6f));
				block.SetVector(FallId, fall);
				// Heavier rain reads as longer streaks: stretch follows the fall speed.
				float stretch = look.Stretch > 1.01f ? look.Stretch * Mathf.Lerp(0.6f, 1f, drop) : 1f;
				block.SetVector(ShapeId, new Vector4(amount, Mathf.Lerp(look.Size.x, look.Size.y, drop), stretch, look.AtlasRow));
				block.SetVector(FlutterId, new Vector4(look.Sway, look.SwayFrequency, look.Alpha, look.Brightness));
				block.SetColor(ColorId, look.Tint);
				Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
			}
		}

		public void Dispose()
		{
			if (mesh != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(mesh);
				}
				else
				{
					Object.DestroyImmediate(mesh);
				}
				mesh = null;
			}
		}
	}
}
