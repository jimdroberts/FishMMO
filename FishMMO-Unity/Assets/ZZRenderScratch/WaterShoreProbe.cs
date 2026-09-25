using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Water;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// A close-up of the waterline, stepped through one swash cycle.
	/// </summary>
	/// <remarks>
	/// The full ocean suite photographs one instant from far away, which is exactly the wrong
	/// instrument for a shoreline: a swash is a CYCLE, and a single frame of it says nothing about
	/// whether the water runs up and drains or simply sits there. Six frames across one period,
	/// from close enough to see the lip, answers in one launch what the big suite could not answer
	/// in five.
	/// </remarks>
	public static class WaterShoreProbe
	{
		private const string OutputDirectory = "/home/jim/Dev/FishMMO-Dev/WaterRenders/shore";
		private const int Width = 1280;
		private const int Height = 720;
		private const float Period = 9f;

		private static readonly StringBuilder Report = new StringBuilder();

		public static void Run()
		{
			bool depthWas = false, opaqueWas = false;
			UniversalRenderPipelineAsset pipeline = null;
			try
			{
				Directory.CreateDirectory(OutputDirectory);
				pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset
					?? QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
				depthWas = pipeline.supportsCameraDepthTexture;
				opaqueWas = pipeline.supportsCameraOpaqueTexture;
				pipeline.supportsCameraDepthTexture = true;

				Build(out Camera camera, out WaterSurface water, out WaterShore shore, out Light sun);
				sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

				// Close enough that a foam lip is more than a pixel, and low enough to see along
				// the beach the way a player standing on it would.
				camera.transform.position = new Vector3(238f, 2.0f, -6f);
				camera.transform.LookAt(new Vector3(268f, -0.6f, 2f));
				camera.fieldOfView = 55f;


				for (int frame = 0; frame < 6; frame++)
				{
					float t = frame / 6f * Period;
					water.SetClock(11.0 + t);
					shore.SetClock(t);
					Capture(camera, $"cycle-{frame}", $"t={t:0.0}s of {Period:0}s");
				}

				/* The foam at the waterline from a few metres, standing on the beach: close enough to
				 * tell lace from a painted band. It was a solid band, every pixel of the lip 45-75%
				 * white, before the lip was thresholded against the mottle (2026-09-25). */
				// Eye height on the sand (about 0.5 m up at x 244), looking down the gentle 1:37
				// beach at the lower swash, which runs up to about x 245 from the waterline at 267.
				camera.transform.position = new Vector3(244f, 2.3f, -3f);
				camera.transform.LookAt(new Vector3(256f, 0.1f, 1f));
				camera.fieldOfView = 50f;
				for (int frame = 0; frame < 3; frame++)
				{
					float t = (0.12f + frame * 0.2f) * Period;
					water.SetClock(11.0 + t);
					shore.SetClock(t);
					Capture(camera, $"foam-close-{frame}", $"the lip from the beach, t={t:0.0}s");
				}

				/* Which term is the white band at the water's edge? Three systems can each draw white
				 * there — the shore pass's foam, its sheen (the sheet reflecting the sky at a glancing
				 * angle), and the sea's own surf foam — and a finished frame cannot tell them apart.
				 * One off at a time, at the same instant as foam-close-1. */
				float instant = (0.12f + 0.2f) * Period;
				void Moment()
				{
					water.SetClock(11.0 + instant);
					shore.SetClock(instant);
				}
				float sheen = shore.Material.GetFloat("_Sheen");
				shore.Material.SetFloat("_Sheen", 0f);
				Moment();
				Capture(camera, "terms-no-sheen", "the shore's sheen off");
				shore.Material.SetFloat("_Sheen", sheen);

				float edgeFoam = shore.Material.GetFloat("_EdgeFoam"), residual = shore.Material.GetFloat("_Residual");
				shore.Material.SetFloat("_EdgeFoam", 0f);
				shore.Material.SetFloat("_Residual", 0f);
				Moment();
				Capture(camera, "terms-no-shore-foam", "the shore's lip and stranded foam off");
				shore.Material.SetFloat("_EdgeFoam", edgeFoam);
				shore.Material.SetFloat("_Residual", residual);

				float surf = water.Material.GetFloat("_SurfStrength");
				water.Material.SetFloat("_SurfStrength", 0f);
				Moment();
				Capture(camera, "terms-no-surf", "the sea's surf foam off");
				water.Material.SetFloat("_SurfStrength", surf);

				Finish(0);
			}
			catch (Exception ex)
			{
				Report.AppendLine("EXCEPTION: " + ex);
				Finish(3);
			}
			finally
			{
				if (pipeline != null)
				{
					pipeline.supportsCameraDepthTexture = depthWas;
					pipeline.supportsCameraOpaqueTexture = opaqueWas;
				}
			}
		}

		private static void Build(out Camera camera, out WaterSurface water, out WaterShore shore, out Light sun)
		{
			RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
			RenderSettings.ambientMode = AmbientMode.Skybox;
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();

			var sunHost = new GameObject("Sun");
			sun = sunHost.AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.6f;
			sun.color = new Color(1f, 0.96f, 0.88f);
			sun.shadows = LightShadows.Soft;

			WaterRenderProbe.BuildSeabedForProbe();

			var waterHost = new GameObject("Water");
			waterHost.AddComponent<MeshFilter>();
			waterHost.AddComponent<MeshRenderer>();
			waterHost.transform.position = Vector3.zero;
			water = waterHost.AddComponent<WaterSurface>();
			Material authored = AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Plugins/FishMMO Water/Materials/OceanWater.mat");
			water.Material = new Material(authored) { name = "ProbeOcean" };
			water.Spectrum = AssetDatabase.LoadAssetAtPath<ComputeShader>(
				"Assets/Plugins/FishMMO Water/Shaders/FishWaterFFT.compute");
			water.OuterRadius = 9000f;
			water.ReportQuality = false;
			water.Rebuild();
			waterHost.AddComponent<WaterShoreField>().Build();
			shore = waterHost.AddComponent<WaterShore>();
			var environment = waterHost.AddComponent<WaterEnvironment>();
			environment.DriveGravity = false;
			environment.DriveColor = false;
			environment.DriveTide = false;
			environment.DriveStorms = false;
			environment.DriveCloudShadow = false;
			// A fresh breeze blowing onshore across the default 20 km of open water.
			environment.WindOverride = 8f;
			environment.HeadingOverride = 270f;
			environment.Apply();
			Report.AppendLine($"sea state: Hs {environment.SignificantHeight:0.00} m, Tp {environment.PeakPeriod:0.0} s, " +
				$"fetch {environment.FetchMetres / 1000f:0} km, FFT wind {water.WindSpeed:0.0} m/s");

			var cameraHost = new GameObject("Probe Camera");
			camera = cameraHost.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.Skybox;
			camera.nearClipPlane = 0.05f;
			camera.farClipPlane = 12000f;
			camera.allowHDR = true;
		}

		private static void Capture(Camera camera, string name, string note)
		{
			var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 1 };
			camera.targetTexture = texture;
			camera.Render();
			camera.Render();

			var readable = new Texture2D(Width, Height, TextureFormat.RGB24, false);
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = texture;
			readable.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			readable.Apply();
			RenderTexture.active = active;
			camera.targetTexture = null;

			File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), readable.EncodeToPNG());

			Color[] pixels = readable.GetPixels();
			int white = 0;
			float sum = 0f;
			for (int i = 0; i < pixels.Length; i++)
			{
				float v = pixels[i].grayscale;
				sum += v;
				if (v > 0.80f)
				{
					white++;
				}
			}
			Report.AppendLine($"{name,-18} {note,-42} mean {sum / pixels.Length:0.000} " +
				$"white {100f * white / pixels.Length,5:0.00}% | " +
				$"reach {Shader.GetGlobalFloat("_FishWaterSwashReach"):0.00} " +
				$"period {Shader.GetGlobalFloat("_FishWaterSwashPeriod"):0.00} " +
				$"skew {Shader.GetGlobalFloat("_FishWaterSwashSkew"):0.00} " +
				$"t {Shader.GetGlobalFloat("_FishWaterShoreTime"):0.00}");

			UnityEngine.Object.DestroyImmediate(readable);
			texture.Release();
			UnityEngine.Object.DestroyImmediate(texture);
		}

		private static void Finish(int code)
		{
			Debug.Log("[WaterShoreProbe]\n" + Report);
			File.WriteAllText(Path.Combine(OutputDirectory, "report.txt"), Report.ToString());
			EditorApplication.Exit(code);
		}
	}
}
