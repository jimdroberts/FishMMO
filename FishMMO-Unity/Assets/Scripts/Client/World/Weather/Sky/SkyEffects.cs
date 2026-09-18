using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Lightning: schedules strikes from the weather, flashes the sky and the sun light, draws
	/// the bolts and asks for thunder once the sound would arrive.
	/// </summary>
	public sealed class LightningPresenter
	{
		public const float BoltSeconds = 0.25f;
		public const float SpeedOfSound = 343f;
		private static readonly int BoltColorId = Shader.PropertyToID("_BoltColor");

		private readonly List<LightningStrike> strikes = new List<LightningStrike>();
		private readonly List<LightningStrike> fresh = new List<LightningStrike>();
		private readonly List<(double at, WeatherAudioCue cue, float volume)> thunder = new List<(double, WeatherAudioCue, float)>();
		private readonly List<Vector3> trunk = new List<Vector3>();
		private readonly List<List<Vector3>> branches = new List<List<Vector3>>();
		private readonly List<Vector3> vertices = new List<Vector3>();
		private readonly List<Vector2> uvs = new List<Vector2>();
		private readonly List<Color> colors = new List<Color>();
		private readonly List<int> indices = new List<int>();
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private Mesh mesh;
		private double lastTime = double.NaN;

		/// <summary>0..1 flash brightness now.</summary>
		public float Flash { get; private set; }

		public int ActiveBolts { get; private set; }

		/// <summary>Strikes seen since the presenter started.</summary>
		public int TotalStrikes { get; private set; }

		public void Reset()
		{
			strikes.Clear();
			thunder.Clear();
			lastTime = double.NaN;
			Flash = 0f;
		}

		public void Update(WeatherTimeline timeline, uint tick, double now, Vector3 viewer, Camera camera, Material material)
		{
			if (double.IsNaN(lastTime) || now < lastTime || now - lastTime > 5.0)
			{
				lastTime = now;
			}
			fresh.Clear();
			SkySchedule.Lightning(timeline, tick, lastTime, now, viewer, fresh);
			lastTime = now;
			foreach (LightningStrike strike in fresh)
			{
				strikes.Add(strike);
				TotalStrikes++;
				float distance = Vector3.Distance(new Vector3(strike.Ground.x, viewer.y, strike.Ground.z), viewer);
				WeatherAudioCue cue = distance < 1200f ? WeatherAudioCue.ThunderNear : WeatherAudioCue.ThunderFar;
				float volume = Mathf.Clamp01(1.2f - distance / 5000f) * strike.Intensity;
				thunder.Add((strike.Time + distance / SpeedOfSound, cue, volume));
			}
			for (int i = thunder.Count - 1; i >= 0; i--)
			{
				if (now >= thunder[i].at)
				{
					WeatherClient.RaiseAudioCue(thunder[i].cue, thunder[i].volume);
					thunder.RemoveAt(i);
				}
			}

			float flash = 0f;
			vertices.Clear();
			uvs.Clear();
			colors.Clear();
			indices.Clear();
			ActiveBolts = 0;
			for (int i = strikes.Count - 1; i >= 0; i--)
			{
				LightningStrike strike = strikes[i];
				double age = now - strike.Time;
				if (age > 1.5)
				{
					strikes.RemoveAt(i);
					continue;
				}
				if (age < 0.0)
				{
					continue;
				}
				// A double flicker, fading quickly; farther strikes flash less.
				float distance = Vector3.Distance(strike.Ground, viewer);
				float nearness = Mathf.Clamp01(1.3f - distance / 4000f);
				float pulse = Mathf.Exp(-(float)age * 7f) + 0.6f * Mathf.Exp(-Mathf.Abs((float)age - 0.12f) * 25f);
				flash = Mathf.Max(flash, pulse * nearness * strike.Intensity);
				if (age < BoltSeconds && camera != null)
				{
					float alpha = (1f - (float)age / BoltSeconds) * strike.Intensity;
					SkySchedule.BoltPath(strike, trunk, branches);
					float width = Mathf.Lerp(2f, 8f, Mathf.Clamp01(distance / 3000f));
					AddRibbon(trunk, width, alpha, camera.transform.position);
					foreach (List<Vector3> branch in branches)
					{
						AddRibbon(branch, width * 0.5f, alpha * 0.7f, camera.transform.position);
					}
					ActiveBolts++;
				}
			}
			Flash = Mathf.Clamp01(flash);

			if (mesh == null)
			{
				mesh = new Mesh { name = "Lightning", hideFlags = HideFlags.DontSave };
				mesh.MarkDynamic();
			}
			mesh.Clear();
			if (vertices.Count > 0)
			{
				mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
				mesh.SetVertices(vertices);
				mesh.SetUVs(0, uvs);
				mesh.SetColors(colors);
				mesh.SetTriangles(indices, 0, false);
				mesh.RecalculateBounds();
			}
		}

		private void AddRibbon(List<Vector3> points, float width, float alpha, Vector3 cameraPosition)
		{
			for (int i = 0; i < points.Count - 1; i++)
			{
				Vector3 a = points[i], b = points[i + 1];
				Vector3 toCamera = (cameraPosition - (a + b) * 0.5f).normalized;
				Vector3 side = Vector3.Cross(b - a, toCamera).normalized * width * 0.5f;
				int start = vertices.Count;
				vertices.Add(a - side);
				vertices.Add(a + side);
				vertices.Add(b + side);
				vertices.Add(b - side);
				uvs.Add(new Vector2(0f, 0f));
				uvs.Add(new Vector2(1f, 0f));
				uvs.Add(new Vector2(1f, 1f));
				uvs.Add(new Vector2(0f, 1f));
				var color = new Color(1f, 1f, 1f, alpha);
				colors.Add(color);
				colors.Add(color);
				colors.Add(color);
				colors.Add(color);
				indices.Add(start);
				indices.Add(start + 1);
				indices.Add(start + 2);
				indices.Add(start);
				indices.Add(start + 2);
				indices.Add(start + 3);
			}
		}

		public void Draw(Camera camera, Material material)
		{
			if (mesh == null || mesh.vertexCount == 0 || material == null)
			{
				return;
			}
			block.SetColor(BoltColorId, new Color(0.8f, 0.85f, 1f, 3f));
			var rp = new RenderParams(material) { camera = camera, matProps = block, worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		public void Dispose()
		{
			if (mesh != null)
			{
				if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
				mesh = null;
			}
		}
	}

	/// <summary>Distant precipitation: a soft curtain under each of the nearest cells beyond the particle field.</summary>
	public sealed class CurtainPresenter
	{
		public const float NearFieldMeters = 120f;
		private static readonly int ColorId = Shader.PropertyToID("_CurtainColor");
		private static readonly int ParamsId = Shader.PropertyToID("_CurtainParams");
		private Mesh cylinder;
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private readonly List<(StormCell cell, float distance)> nearest = new List<(StormCell, float)>();

		public int Drawn { get; private set; }

		public static Mesh BuildCylinder(int segments = 24)
		{
			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var indices = new List<int>();
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;
				float a = t * Mathf.PI * 2f;
				var n = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
				vertices.Add(n * 0.5f);
				vertices.Add(n * 0.5f + Vector3.up);
				normals.Add(n);
				normals.Add(n);
				uvs.Add(new Vector2(t, 0f));
				uvs.Add(new Vector2(t, 1f));
				if (i < segments)
				{
					int b = i * 2;
					indices.Add(b); indices.Add(b + 1); indices.Add(b + 3);
					indices.Add(b); indices.Add(b + 3); indices.Add(b + 2);
				}
			}
			var mesh = new Mesh { name = "Curtain", hideFlags = HideFlags.DontSave };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		public void Draw(WeatherTimeline timeline, uint tick, Camera camera, Material material, WeatherRenderProfile profile, int limit, float time)
		{
			Drawn = 0;
			if (timeline == null || camera == null || material == null || limit <= 0 || timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			if (cylinder == null)
			{
				cylinder = BuildCylinder();
			}
			Vector3 viewer = camera.transform.position;
			nearest.Clear();
			foreach (StormCell cell in timeline.Cells)
			{
				Vector2 centre = cell.CentreAt(tick, timeline.TickDelta);
				float distance = Vector2.Distance(centre, new Vector2(viewer.x, viewer.z));
				if (distance - cell.RadiusMeters < NearFieldMeters && distance < cell.RadiusMeters)
				{
					// The camera is under this one: the particle field shows it.
					continue;
				}
				nearest.Add((cell, distance));
			}
			nearest.Sort((a, b) => a.distance.CompareTo(b.distance));
			for (int i = 0; i < nearest.Count && Drawn < limit; i++)
			{
				StormCell cell = nearest[i].cell;
				WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
				if (preset == null)
				{
					continue;
				}
				WeatherFrame frame = preset.Evaluate();
				float amount = frame[WeatherChannel.Precipitation] * cell.EnvelopeAt(tick) * cell.PeakIntensity;
				if (amount < 0.05f)
				{
					continue;
				}
				PrecipitationKind kind = frame.DominantPrecipitation;
				PrecipitationLook look = profile.LookOf(kind == PrecipitationKind.Snow ? WeatherChannel.SnowWeight
					: kind == PrecipitationKind.Hail ? WeatherChannel.HailWeight
					: kind == PrecipitationKind.Ash ? WeatherChannel.AshWeight
					: kind == PrecipitationKind.Sand ? WeatherChannel.SandWeight
					: WeatherChannel.RainWeight);
				Vector2 centre = cell.CentreAt(tick, timeline.TickDelta);
				float radius = cell.RadiusMeters * 1.3f;
				float height = 1300f;
				Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(centre.x, viewer.y - 30f, centre.y), Quaternion.identity, new Vector3(radius, height, radius));
				Color color = look.FogColor;
				color.a = Mathf.Clamp01(amount * (kind == PrecipitationKind.Sand ? 0.75f : 0.45f));
				block.SetColor(ColorId, color);
				block.SetVector(ParamsId, new Vector4(kind == PrecipitationKind.Snow ? 0.08f : 0.6f, time, kind == PrecipitationKind.Rain ? 40f : 16f, kind == PrecipitationKind.Snow ? 1f : 0f));
				var rp = new RenderParams(material) { camera = camera, matProps = block, worldBounds = new Bounds(matrix.GetColumn(3), new Vector3(radius * 2f, height * 2f, radius * 2f)), shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
				Graphics.RenderMesh(rp, cylinder, 0, matrix);
				Drawn++;
			}
		}

		public void Dispose()
		{
			if (cylinder != null)
			{
				if (Application.isPlaying) Object.Destroy(cylinder); else Object.DestroyImmediate(cylinder);
				cylinder = null;
			}
		}
	}

	/// <summary>Moving cloud shadows: a cookie on the sun light, redrawn when the cloud cover changes.</summary>
	public sealed class CloudShadowPresenter
	{
		private static readonly int CookieParamsId = Shader.PropertyToID("_CookieParams");
		private static readonly int ShadowAreaId = Shader.PropertyToID("_FishCloudShadowArea");
		private static readonly int ShadowOriginId = Shader.PropertyToID("_FishCloudShadowOrigin");
		private static readonly int ShadowRightId = Shader.PropertyToID("_FishCloudShadowRight");
		private static readonly int ShadowUpId = Shader.PropertyToID("_FishCloudShadowUp");
		private RenderTexture cookie;
		private float drawnCover = -1f;
		private Light boundLight;
		private Vector2 offset;

		/// <summary>
		/// Redraws the shadow the clouds throw on the world, by marching the same volume the sky
		/// draws — from each patch of ground toward the light — into a cookie on the sun.
		/// </summary>
		/// <remarks>
		/// The cookie is a window on the world, not a tiling pattern: it covers a square around the
		/// camera and moves with it, so the shadow under a cloud is the shadow of that cloud.
		/// </remarks>
		public void Update(Light light, Material cloudMaterial, Vector3 viewer, float areaMeters, float strength, int steps, bool enabled)
		{
			if (light == null || cloudMaterial == null || !enabled || !SkySystem.CloudsReady)
			{
				Clear(light);
				return;
			}
			if (cookie == null)
			{
				cookie = new RenderTexture(256, 256, 0, RenderTextureFormat.R8) { name = "Cloud Shadows", wrapMode = TextureWrapMode.Clamp, useMipMap = false, hideFlags = HideFlags.DontSave };
				cookie.Create();
			}
			// The window is in the light's own plane, because that is the plane the light reads a
			// cookie in. Snapped to a texel, so the shadow does not crawl as the camera moves.
			Transform lightTransform = light.transform;
			Vector3 local = lightTransform.InverseTransformPoint(viewer);
			float texel = areaMeters / cookie.width;
			local.x = Mathf.Round(local.x / texel) * texel;
			local.y = Mathf.Round(local.y / texel) * texel;
			Vector3 centre = lightTransform.TransformPoint(new Vector3(local.x, local.y, 0f));
			cloudMaterial.SetVector(ShadowAreaId, new Vector4(0f, 0f, areaMeters, steps));
			cloudMaterial.SetVector(ShadowOriginId, centre);
			cloudMaterial.SetVector(ShadowRightId, lightTransform.right);
			cloudMaterial.SetVector(ShadowUpId, lightTransform.up);
			Graphics.Blit(null, cookie, cloudMaterial, 3);

			if (boundLight != light)
			{
				Clear(boundLight);
				boundLight = light;
			}
			light.cookie = cookie;
			var data = light.GetComponent<UniversalAdditionalLightData>();
			if (data == null)
			{
				data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
			}
			data.lightCookieSize = new Vector2(areaMeters, areaMeters);
			// The cookie follows the camera: the window's middle sits where the viewer stands, in
			// the light's plane, so the offset is that point measured in windows.
			data.lightCookieOffset = new Vector2(-local.x / areaMeters, -local.y / areaMeters);
			drawnCover = strength;
		}

		public void Clear(Light light)
		{
			if (light != null && light.cookie == cookie && cookie != null)
			{
				light.cookie = null;
			}
			drawnCover = -1f;
		}

		public void Dispose()
		{
			Clear(boundLight);
			if (cookie != null)
			{
				cookie.Release();
				if (Application.isPlaying) Object.Destroy(cookie); else Object.DestroyImmediate(cookie);
				cookie = null;
			}
		}
	}
}
