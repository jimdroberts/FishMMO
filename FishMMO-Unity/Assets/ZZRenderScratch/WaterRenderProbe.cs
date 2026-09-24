using System;
using System.Collections.Generic;
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
	/// Throwaway harness: builds a beach and an ocean from nothing and photographs it, so the water
	/// can be looked at rather than reasoned about.
	/// </summary>
	/// <remarks>
	/// The sea bed is a mesh rather than a Unity terrain so the shot depends on no project asset:
	/// a beach at +6 m running down through a shelf to −45 m, which is the range the depth
	/// absorption, the shore foam and the soft edge all have to behave over.
	/// </remarks>
	public static class WaterRenderProbe
	{
		private const string OutputDirectory = "/home/jim/Dev/FishMMO-Dev/WaterRenders";
		private const int Width = 1280;
		private const int Height = 720;

		private sealed class Shot
		{
			public string Name;
			public Vector3 Position;
			public Vector3 LookAt;
			public float Wind;
			public float Choppiness;
			public float SunPitch;
			public float SunYaw;
			public float Clock = 12f;
			public float FieldOfView = 60f;
			public bool Refraction;
			public bool Depth = true;
			/// <summary>Material overrides, for isolating which term a frame is actually showing.</summary>
			public Action<Material> Override;
			/// <summary>Shore overrides, for isolating the beach pass.</summary>
			public Action<WaterShore> Shore;
		}

		private static readonly StringBuilder Report = new StringBuilder();
		private static Material authoredValues;
		private static WaterShore shoreline;

		public static void Run()
		{
			bool depthWas = false, opaqueWas = false;
			UniversalRenderPipelineAsset pipeline = null;
			try
			{
				Directory.CreateDirectory(OutputDirectory);

				pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset
					?? QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
				if (pipeline == null)
				{
					Report.AppendLine("FAIL: no URP asset is active; nothing can be rendered.");
					Finish(2);
					return;
				}
				Report.AppendLine($"pipeline: {pipeline.name}  hdr={pipeline.supportsHDR}  msaa={pipeline.msaaSampleCount}");
				depthWas = pipeline.supportsCameraDepthTexture;
				opaqueWas = pipeline.supportsCameraOpaqueTexture;
				Report.AppendLine($"as shipped: depth={depthWas} opaque={opaqueWas}  (both forced ON for the probe)");
				pipeline.supportsCameraDepthTexture = true;
				pipeline.supportsCameraOpaqueTexture = true;

				Shader shader = Shader.Find("FishMMO/Water/Ocean");
				if (shader == null)
				{
					Report.AppendLine("FAIL: Shader.Find(\"FishMMO/Water/Ocean\") returned null — it did not compile.");
					Finish(3);
					return;
				}
				Report.AppendLine($"shader: {shader.name}  supported={shader.isSupported}  passes={shader.passCount}");
				if (!shader.isSupported)
				{
					Report.AppendLine("FAIL: the shader compiled but is not supported on this device.");
				}

				Build(shader, out Camera camera, out WaterSurface water, out Light sun);

				foreach (Shot shot in Shots())
				{
					Capture(shot, camera, water, sun);
				}
				Finish(0);
			}
			catch (Exception ex)
			{
				Report.AppendLine("EXCEPTION: " + ex);
				Finish(4);
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

		private static IEnumerable<Shot> Shots()
		{
			// Sun roughly ahead of the camera for the glint shots, behind for the scatter one.
			yield return new Shot
			{
				Name = "01-shore-breeze", Position = new Vector3(215f, 5f, -30f), LookAt = new Vector3(600f, -6f, 30f),
				Wind = 6f, Choppiness = 0.5f, SunPitch = 28f, SunYaw = 0f,
			};
			yield return new Shot
			{
				Name = "02-shore-gale", Position = new Vector3(215f, 5f, -30f), LookAt = new Vector3(600f, -6f, 30f),
				Wind = 20f, Choppiness = 0.75f, SunPitch = 28f, SunYaw = 0f,
			};
			yield return new Shot
			{
				Name = "03-open-sea-gale", Position = new Vector3(900f, 6f, 0f), LookAt = new Vector3(1900f, 2f, 200f),
				Wind = 20f, Choppiness = 0.75f, SunPitch = 22f, SunYaw = 20f,
			};
			yield return new Shot
			{
				Name = "04-open-sea-calm", Position = new Vector3(900f, 6f, 0f), LookAt = new Vector3(1900f, 2f, 200f),
				Wind = 4f, Choppiness = 0.4f, SunPitch = 22f, SunYaw = 20f,
			};
			yield return new Shot
			{
				Name = "05-backlit-swell", Position = new Vector3(900f, 3f, 0f), LookAt = new Vector3(1900f, 4f, 0f),
				Wind = 14f, Choppiness = 0.7f, SunPitch = 12f, SunYaw = 0f,
			};
			yield return new Shot
			{
				Name = "06-overhead-shallows", Position = new Vector3(300f, 60f, 0f), LookAt = new Vector3(480f, 0f, 0f),
				Wind = 8f, Choppiness = 0.5f, SunPitch = 55f, SunYaw = 40f, FieldOfView = 50f,
			};
			yield return new Shot
			{
				Name = "07-underwater", Position = new Vector3(470f, -4f, 0f), LookAt = new Vector3(440f, -8f, 18f),
				Wind = 8f, Choppiness = 0.5f, SunPitch = 55f, SunYaw = 0f,
			};
			yield return new Shot
			{
				Name = "08-underwater-up", Position = new Vector3(470f, -6f, 0f), LookAt = new Vector3(480f, 6f, 6f),
				Wind = 8f, Choppiness = 0.5f, SunPitch = 60f, SunYaw = 0f,
			};
			/* Diagnostics. The open-sea frames come back nearly black and the arithmetic says they
			 * should not, so rather than reason about it these two say where the body colour
			 * actually lands: white deep water, and then the reflection removed entirely. */
			yield return new Shot
			{
				Name = "D1-white-body", Position = new Vector3(900f, 6f, 0f), LookAt = new Vector3(1900f, 2f, 200f),
				Wind = 20f, Choppiness = 0.75f, SunPitch = 22f, SunYaw = 20f,
				Override = m =>
				{
					m.SetColor("_DeepColor", Color.white);
					m.SetColor("_ShallowColor", Color.magenta);
				},
			};
			yield return new Shot
			{
				Name = "D2-no-glint", Position = new Vector3(900f, 6f, 0f), LookAt = new Vector3(1900f, 2f, 200f),
				Wind = 20f, Choppiness = 0.75f, SunPitch = 22f, SunYaw = 20f,
				Override = m =>
				{
					m.SetFloat("_SpecularStrength", 0f);
					m.SetColor("_FoamColor", Color.red);
					m.SetFloat("_WhitecapThreshold", 0.70f);
				},
			};
			// A viewpoint that is not inside the waves: at 20 m/s the swell is 9 m tall, so a
			// camera at 6 m is standing in it.
			yield return new Shot
			{
				Name = "11-gale-from-above", Position = new Vector3(900f, 45f, 0f), LookAt = new Vector3(1500f, 0f, 120f),
				Wind = 20f, Choppiness = 0.75f, SunPitch = 35f, SunYaw = 20f,
			};

			yield return new Shot
			{
				Name = "12-surf-line", Position = new Vector3(250f, 14f, -60f), LookAt = new Vector3(430f, -1f, 20f),
				Wind = 10f, Choppiness = 0.6f, SunPitch = 40f, SunYaw = -30f, FieldOfView = 55f,
			};
			yield return new Shot
			{
				Name = "13-swash", Position = new Vector3(232f, 2.2f, -10f), LookAt = new Vector3(300f, -1f, 6f),
				Wind = 8f, Choppiness = 0.55f, SunPitch = 45f, SunYaw = -40f, FieldOfView = 55f,
			};

			/* Two conclusive tests for the missing shore foam, using material overrides only.
			 * D3 paints the sediment band magenta and widens it to 50 m: if the near shore goes
			 * magenta the depth lookup is sound and the foam FORMULA is at fault; if it stays
			 * green the field is not reaching the shader at all.
			 * D4 widens the foam band to 50 m of depth: if foam then appears, 1.1 m was simply too
			 * narrow a band to see. */
			yield return new Shot
			{
				Name = "D3-depth-probe", Position = new Vector3(232f, 2.2f, -10f), LookAt = new Vector3(300f, -1f, 6f),
				Wind = 8f, Choppiness = 0.55f, SunPitch = 45f, SunYaw = -40f, FieldOfView = 55f,
				Override = m =>
				{
					m.SetColor("_ShoreColor", Color.magenta);
					m.SetFloat("_ShoreDepth", 50f);
				},
			};
			yield return new Shot
			{
				Name = "D4-wide-foam", Position = new Vector3(232f, 2.2f, -10f), LookAt = new Vector3(300f, -1f, 6f),
				Wind = 8f, Choppiness = 0.55f, SunPitch = 45f, SunYaw = -40f, FieldOfView = 55f,
				Override = m => m.SetFloat("_FoamDistance", 50f),
			};

			yield return new Shot
			{
				Name = "D5-shore-band", Position = new Vector3(232f, 6f, -10f), LookAt = new Vector3(320f, -1f, 6f),
				Wind = 8f, Choppiness = 0.55f, SunPitch = 45f, SunYaw = -40f, FieldOfView = 60f,
				Shore = s => { s.DriveFromSea = false; s.Reach = 22f; },
			};

			// What a player on the shipped Balanced tier actually sees: no opaque copy.
			yield return new Shot
			{
				Name = "09-shore-with-refraction", Refraction = true, Position = new Vector3(215f, 5f, -30f), LookAt = new Vector3(600f, -6f, 30f),
				Wind = 6f, Choppiness = 0.5f, SunPitch = 28f, SunYaw = 0f,
			};
			// And on Performant: no depth either.
			yield return new Shot
			{
				Name = "10-shore-no-depth", Position = new Vector3(215f, 5f, -30f), LookAt = new Vector3(600f, -6f, 30f),
				Wind = 6f, Choppiness = 0.5f, SunPitch = 28f, SunYaw = 0f, Depth = false,
			};
		}

		private static void Build(Shader shader, out Camera camera, out WaterSurface water, out Light sun)
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

			BuildSeabed();

			var waterHost = new GameObject("Water");
			waterHost.AddComponent<MeshFilter>();
			waterHost.AddComponent<MeshRenderer>();
			waterHost.transform.position = Vector3.zero;   // sea level y = 0
			water = waterHost.AddComponent<WaterSurface>();
			/* The real material, instanced. Building one from the shader alone gives a material
			 * with no normal map and no foam mask, which is exactly the configuration the shipped
			 * one is not — and the probe exists to photograph what ships. */
			Material authored = AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Plugins/FishMMO Water/Materials/OceanWater.mat");
			authoredValues = authored;
			water.Material = authored != null
				? new Material(authored) { name = "ProbeOcean" }
				: new Material(shader) { name = "ProbeOcean" };
			water.Spectrum = AssetDatabase.LoadAssetAtPath<ComputeShader>(
				"Assets/Plugins/FishMMO Water/Shaders/FishWaterFFT.compute");
			water.OuterRadius = 14000f;
			water.Rings = 150;
			water.Segments = 220;
			water.ReportQuality = false;
			water.UnderwaterVisibility = 30f;
			water.Rebuild();
			// The depth field the surf and the shoaling read. Built from the terrain above.
			WaterShoreField shore = waterHost.AddComponent<WaterShoreField>();
			shore.Build();
			// The shoreline itself: swash, edge foam and wet sand, as its own projected pass.
			shoreline = waterHost.AddComponent<WaterShore>();

			// What the distance field actually holds — the shore pass is driven entirely by it.
			Texture2D fieldTexture = Shader.GetGlobalTexture("_FishWaterShore") as Texture2D;
			if (fieldTexture != null)
			{
				Color[] texels = fieldTexture.GetPixels();
				float depthLow = float.MaxValue, depthHigh = float.MinValue;
				float distLow = float.MaxValue, distHigh = float.MinValue;
				for (int i = 0; i < texels.Length; i++)
				{
					depthLow = Mathf.Min(depthLow, texels[i].r);
					depthHigh = Mathf.Max(depthHigh, texels[i].r);
					distLow = Mathf.Min(distLow, texels[i].g);
					distHigh = Mathf.Max(distHigh, texels[i].g);
				}
				Report.AppendLine($"shore field: depth {depthLow:0.0} .. {depthHigh:0.0} m | " +
					$"distance {distLow:0.0} .. {distHigh:0.0} m");
			}
			else
			{
				Report.AppendLine("shore field: NOT READABLE from the global slot");
			}
			Vector4 rect = Shader.GetGlobalVector("_FishWaterShoreRect");
			Texture bound = Shader.GetGlobalTexture("_FishWaterShore");
			Report.AppendLine($"shore pass: component={(shoreline != null)} " +
				$"material={(shoreline != null && shoreline.Material != null ? shoreline.Material.shader.name : "NULL")} " +
				$"shaderFound={(Shader.Find("FishMMO/Water/Shore") != null)}");
			Report.AppendLine($"shore field: rect={rect} texture={(bound != null ? bound.width + "px" : "NONE")} " +
				$"terrains={Terrain.activeTerrains.Length}");

			var cameraHost = new GameObject("Probe Camera");
			camera = cameraHost.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.Skybox;
			camera.nearClipPlane = 0.2f;
			camera.farClipPlane = 15000f;
			camera.allowHDR = true;
		}

		/// <summary>A beach running from +14 m down through a shelf to −45 m, as a real Unity terrain.</summary>
		/// <remarks>
		/// A terrain rather than a mesh, because WaterShoreField reads Terrain.activeTerrains to
		/// build the depth field the surf needs — and because that is what a generated scene has.
		/// </remarks>
		/// <summary>Shared with the shore probe, which needs the same beach.</summary>
		public static void BuildSeabedForProbe() => BuildSeabed();

		private static void BuildSeabed()
		{
			const int Resolution = 513;
			const float Size = 2600f;
			const float Base = -60f;
			const float Height = 90f;

			var data = new TerrainData
			{
				heightmapResolution = Resolution,
				size = new Vector3(Size, Height, Size),
			};
			var heights = new float[Resolution, Resolution];
			for (int z = 0; z < Resolution; z++)
			{
				float wz = z / (float)(Resolution - 1) * Size - Size * 0.5f;
				for (int x = 0; x < Resolution; x++)
				{
					float wx = x / (float)(Resolution - 1) * Size - 200f;
					heights[z, x] = Mathf.Clamp01((Depth(wx, wz) - Base) / Height);
				}
			}
			data.SetHeights(0, 0, heights);

			var host = new GameObject("Seabed");
			host.transform.position = new Vector3(-200f, Base, -Size * 0.5f);
			Terrain terrain = host.AddComponent<Terrain>();
			terrain.terrainData = data;
			host.AddComponent<TerrainCollider>().terrainData = data;

			Material sand = AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Prefabs/Client/Materials/Ground/Weather Terrain.mat");
			if (sand == null)
			{
				// A script-created terrain gets the built-in material, which URP draws magenta.
				sand = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "Sand" };
			}
			terrain.materialTemplate = sand;

			/* Real sand, not Texture2D.whiteTexture. A pure white beach blows out to nothing under
			 * a sunlit sky and takes the whole shoreline with it — the water at the waterline
			 * cannot be judged against a surface that is already clipped. */
			var grains = new Texture2D(64, 64, TextureFormat.RGB24, true) { name = "Sand" };
			var pixels = new Color[64 * 64];
			for (int i = 0; i < pixels.Length; i++)
			{
				float grain = Mathf.PerlinNoise(i % 64 * 0.35f, i / 64 * 0.35f) * 0.16f - 0.08f;
				pixels[i] = new Color(0.68f + grain, 0.60f + grain, 0.46f + grain);
			}
			grains.SetPixels(pixels);
			grains.Apply(true, false);

			var layer = new TerrainLayer
			{
				diffuseTexture = grains,
				tileSize = new Vector2(12f, 12f),
				specular = Color.black,
				smoothness = 0.05f,
				metallic = 0f,
				name = "Sand",
			};
			data.terrainLayers = new[] { layer };
			data.alphamapResolution = 64;
			var splat = new float[64, 64, 1];
			for (int z = 0; z < 64; z++)
			{
				for (int x = 0; x < 64; x++)
				{
					splat[z, x, 0] = 1f;
				}
			}
			data.SetAlphamaps(0, 0, splat);

			// Something standing in the shallows, so refraction has an object to bend and the
			// in-front-of-the-water rejection has something to reject.
			var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			post.name = "Post";
			post.transform.position = new Vector3(330f, 0.5f, -14f);
			post.transform.localScale = new Vector3(2.5f, 6f, 2.5f);
			var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Post" };
			wood.SetColor("_BaseColor", new Color(0.30f, 0.20f, 0.13f));
			post.GetComponent<MeshRenderer>().sharedMaterial = wood;

			var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			rock.name = "Rock";
			rock.transform.position = new Vector3(420f, -1.5f, 26f);
			rock.transform.localScale = new Vector3(18f, 9f, 14f);
			var stone = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Rock" };
			stone.SetColor("_BaseColor", new Color(0.34f, 0.33f, 0.31f));
			rock.GetComponent<MeshRenderer>().sharedMaterial = stone;
		}

		/// <summary>The beach profile, in metres, as a function of world XZ.</summary>
		private static float Depth(float x, float z)
		{
			float height;
			if (x < 120f)
			{
				height = Mathf.Lerp(14f, 3f, Mathf.InverseLerp(-200f, 120f, x));
			}
			else if (x < 420f)
			{
				// The beach: through the water line at about x = 250.
				height = Mathf.Lerp(3f, -6f, Mathf.InverseLerp(120f, 420f, x));
			}
			else if (x < 1000f)
			{
				height = Mathf.Lerp(-6f, -30f, Mathf.InverseLerp(420f, 1000f, x));
			}
			else
			{
				height = Mathf.Lerp(-30f, -45f, Mathf.InverseLerp(1000f, 2400f, x));
			}
			// Gentle relief so the shoreline is a coast and not a ruled line.
			height += Mathf.Sin(z * 0.006f) * 7f * Mathf.InverseLerp(120f, 900f, x);
			height += Mathf.Sin(z * 0.021f + x * 0.004f) * 1.6f;
			return height;
		}

		private static void Capture(Shot shot, Camera camera, WaterSurface water, Light sun)
		{
			water.WindSpeed = shot.Wind;
			water.Choppiness = shot.Choppiness;
			water.FFTChoppiness = Mathf.Lerp(1.0f, 1.9f, shot.Choppiness);
			water.Refraction = shot.Refraction;
			water.DepthEffects = shot.Depth;
			water.Rebuild();
			water.SetClock(shot.Clock);

			// Reset to the authored values, then apply this shot's overrides.
			Material material = water.Material;
			if (authoredValues != null)
			{
				material.CopyPropertiesFromMaterial(authoredValues);
			}
			shot.Override?.Invoke(material);
			if (shoreline != null)
			{
				shoreline.DriveFromSea = true;
				shoreline.Reach = 14f;
				shot.Shore?.Invoke(shoreline);
			}

			sun.transform.rotation = Quaternion.Euler(shot.SunPitch, shot.SunYaw, 0f);

			camera.transform.position = shot.Position;
			camera.transform.LookAt(shot.LookAt);
			camera.fieldOfView = shot.FieldOfView;

			var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR)
			{
				antiAliasing = 1,
			};
			camera.targetTexture = texture;
			// Twice: the first pass populates the opaque and depth copies the second reads.
			camera.Render();
			camera.Render();

			var readable = new Texture2D(Width, Height, TextureFormat.RGB24, false);
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = texture;
			readable.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			readable.Apply();
			RenderTexture.active = active;
			camera.targetTexture = null;

			string path = Path.Combine(OutputDirectory, shot.Name + ".png");
			File.WriteAllBytes(path, readable.EncodeToPNG());

			// Enough of a measurement to tell a black frame from a rendered one without opening it.
			Color[] pixels = readable.GetPixels();
			float sum = 0f, min = 1f, max = 0f;
			for (int i = 0; i < pixels.Length; i++)
			{
				float v = pixels[i].grayscale;
				sum += v;
				min = Mathf.Min(min, v);
				max = Mathf.Max(max, v);
			}
			float mean = sum / pixels.Length;
			int white = 0;
			for (int i = 0; i < pixels.Length; i++)
			{
				if (pixels[i].grayscale > 0.82f)
				{
					white++;
				}
			}
			Report.AppendLine($"{shot.Name,-26} wind {shot.Wind,4:0} chop {shot.Choppiness:0.00} " +
				$"refr={(shot.Refraction ? "on " : "off")} depth={(shot.Depth ? "on " : "off")} " +
				$"| mean {mean:0.000} min {min:0.000} max {max:0.000} bright {100f * white / pixels.Length,5:0.0}%");

			UnityEngine.Object.DestroyImmediate(readable);
			texture.Release();
			UnityEngine.Object.DestroyImmediate(texture);
		}

		private static void Finish(int code)
		{
			string text = Report.ToString();
			Debug.Log("[WaterRenderProbe]\n" + text);
			try
			{
				File.WriteAllText(Path.Combine(OutputDirectory, "report.txt"), text);
			}
			catch
			{
				// The log has it either way.
			}
			EditorApplication.Exit(code);
		}
	}
}
