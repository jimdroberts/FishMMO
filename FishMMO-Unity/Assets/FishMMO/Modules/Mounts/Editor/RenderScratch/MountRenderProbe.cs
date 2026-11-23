using System;
using System.IO;
using System.Reflection;
using System.Text;
using FishMMO.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness (issue #109): seats the TestHuman on every mount and photographs it, so the saddle heights and the
	/// ride pose can be looked at rather than reasoned about.
	/// </summary>
	/// <remarks>
	/// The rider is posed by the game's own <see cref="CharacterBody"/> (the Ride clip, lifted to the mount's saddle by
	/// <see cref="CharacterBody.Place"/>), and the mount is the body <see cref="MountController"/> draws: the same mesh, the
	/// same material and the same gait and wing values. Output: <c>&lt;checkout&gt;/WorldRenders/Mounts/</c>.
	/// Batch: <c>FishMMO.RenderScratch.MountRenderProbe.Run</c>.
	/// </remarks>
	public static class MountRenderProbe
	{
		private static readonly string OutputDirectory = ProbeOutput.Path("WorldRenders", "Mounts");
		private const string MountFolder = "Assets/FishMMO/Modules/Mounts/Templates/Mounts";
		private const string RiderPrefab = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.prefab";
		private const int Width = 1200;
		private const int Height = 900;
		private const float Dt = 1f / 30f;

		private static readonly int GaitId = Shader.PropertyToID("_WildGait");
		private static readonly int PoseId = Shader.PropertyToID("_WildPose");
		private static readonly int FlightId = Shader.PropertyToID("_WildFlight");
		private static readonly StringBuilder Report = new StringBuilder();

		private enum Action { Standing, Running, Flying }

		public static void Run()
		{
			try
			{
				Directory.CreateDirectory(OutputDirectory);
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				Camera camera = Stage();
				GameObject rider = AssetDatabase.LoadAssetAtPath<GameObject>(RiderPrefab);
				if (rider == null)
				{
					Report.AppendLine($"FAIL: no rider at {RiderPrefab}");
					Finish(2);
					return;
				}
				string[] guids = AssetDatabase.FindAssets("t:MountBuffTemplate", new[] { MountFolder });
				Array.Sort(guids, (a, b) => Price(a).CompareTo(Price(b)));
				foreach (string guid in guids)
				{
					MountBuffTemplate mount = AssetDatabase.LoadAssetAtPath<MountBuffTemplate>(AssetDatabase.GUIDToAssetPath(guid));
					Shoot(camera, rider, mount, Action.Standing);
					Shoot(camera, rider, mount, mount.Flies ? Action.Flying : Action.Running);
				}
				Finish(0);
			}
			catch (Exception e)
			{
				Report.AppendLine("FAIL: " + e);
				Finish(1);
			}
		}

		/// <summary>The Stablemaster's order, cheapest first (MountContentBuilder's roster); anything else after.</summary>
		private static readonly string[] Order = { "Cow", "Llama", "Horse", "Giant Frog", "Dragon" };

		private static int Price(string mountGuid)
		{
			string name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(mountGuid));
			int at = Array.IndexOf(Order, name);
			return at >= 0 ? at : Order.Length;
		}

		/// <summary>A meadow under an afternoon sun, and the camera.</summary>
		private static Camera Stage()
		{
			var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
			ground.name = "Ground";
			ground.transform.localScale = new Vector3(40f, 1f, 40f);
			var grass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			grass.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.44f));
			grass.SetFloat("_Smoothness", 0.1f);
			ground.GetComponent<MeshRenderer>().sharedMaterial = grass;

			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.25f;
			sun.color = new Color(1f, 0.95f, 0.86f);
			sun.shadows = LightShadows.Soft;
			// Over the camera's shoulder (the camera looks from -x, +z), so the near side is lit.
			sun.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
			RenderSettings.sun = sun;
			RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
			RenderSettings.ambientEquatorColor = new Color(0.5f, 0.54f, 0.5f);
			RenderSettings.ambientGroundColor = new Color(0.28f, 0.3f, 0.22f);
			RenderSettings.fog = false;
			RenderSettings.ambientIntensity = 1f;
			// A new scene's ambient probe is empty until it is rebuilt: the shaded sides drew black.
			DynamicGI.UpdateEnvironment();

			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.63f, 0.76f, 0.9f);
			camera.fieldOfView = 30f;
			camera.nearClipPlane = 0.05f;
			camera.farClipPlane = 500f;
			return camera;
		}

		private static void Shoot(Camera camera, GameObject riderPrefab, MountBuffTemplate mount, Action action)
		{
			float up = action == Action.Flying ? 3.5f : 0f;
			var root = new GameObject("Rider " + mount.name);
			root.transform.position = new Vector3(0f, up, 0f);
			var meshRoot = new GameObject("MeshRoot").transform;
			meshRoot.SetParent(root.transform, false);
			var rider = (GameObject)PrefabUtility.InstantiatePrefab(riderPrefab, meshRoot);
			CharacterModel model = rider.GetComponentInChildren<CharacterModel>(true);
			Animator animator = model != null ? model.GetComponent<Animator>() : null;
			CharacterBody body = CharacterBody.Create(animator, model != null ? model.Animations : null, "MountRenderProbe");
			if (animator != null)
			{
				// Unseen until the camera renders, and a culled animator is not evaluated: the rider stood in its T-pose.
				animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}
			if (body == null)
			{
				Report.AppendLine($"{mount.name}: FAIL, the rider has no body");
				UnityEngine.Object.DestroyImmediate(root);
				return;
			}
			foreach (SkinnedMeshRenderer skin in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				skin.updateWhenOffscreen = true;
			}

			// The mount, as MountController.ShowMount draws it.
			Mesh mesh = WildlifeBodyMesh.Build(mount.Body, mount.name);
			var mountBody = new GameObject("MountBody");
			mountBody.transform.SetParent(meshRoot, false);
			mountBody.transform.localPosition = new Vector3(0f, 0f, mount.SaddleBack);
			mountBody.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = mountBody.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = mount.Material;

			// And its legs and wings, as MountController.Animate sets them.
			WildlifeBodySpec spec = mount.Body;
			float speed = action == Action.Running ? mount.GaitRunSpeed : action == Action.Flying ? 0f : 0f;
			WildlifeGait gait = WildlifeGait.Evaluate(spec, mount.GaitRunSpeed, speed, 1f);
			float phase = action == Action.Running ? Mathf.PI * 0.5f : 0f;
			float airborne = action == Action.Flying ? 1f : 0f;
			// Wings spread almost level (angle = 0.15 + 0.85 sin phase): raised, the near one hid the rider.
			WildlifeFlight flight = WildlifeFlight.Evaluate(spec, mount.FlySpeed, 8f, Mathf.PI * 2f - 0.1f, 0f, 0f);
			float wingAngle = Mathf.Lerp(-0.35f, flight.WingAngle, airborne);
			float legSwing = spec.WingSpan > 0f ? gait.LegSwing * Mathf.Lerp(0.6f, 0.1f, airborne) : gait.LegSwing;
			var block = new MaterialPropertyBlock();
			block.SetVector(GaitId, new Vector4(phase, legSwing, gait.Gallop, gait.Bob));
			block.SetVector(PoseId, new Vector4(0f, 0f, 0f, spec.Width * 0.5f));
			block.SetVector(FlightId, new Vector4(wingAngle, 0f, 0f, 0f));
			renderer.SetPropertyBlock(block);

			// The rider astride: four seconds of the game's own frames, so every blend and the lift have settled.
			body.SetRiding(true, mount.SaddleHeight);
			PlayableGraph graph = (PlayableGraph)typeof(CharacterBody).GetField("graph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(body);
			Vector3 velocity = action == Action.Running ? Vector3.forward * mount.GaitRunSpeed : Vector3.zero;
			var motion = new CharacterMotion { Velocity = velocity, Grounded = action != Action.Flying, StillWater = float.NaN };
			for (int i = 0; i < 120; i++)
			{
				body.Pose(in motion, Dt);
				graph.Evaluate(Dt);
				body.Place(Dt);
			}

			// Framed from the near side and a little ahead, the whole of both in shot.
			// The mount's mesh and the rider's joints (a skinned mesh's bounds are loose).
			// (The body mesh's own bounds are padded for its legs' and wings' vertex motion: framed on its vertices.)
			Vector3[] vertices = mesh.vertices;
			var bounds = new Bounds(mountBody.transform.TransformPoint(vertices[0]), Vector3.zero);
			foreach (Vector3 v in vertices)
			{
				bounds.Encapsulate(mountBody.transform.TransformPoint(v));
			}
			foreach (HumanBodyBones bone in new[] { HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
			{
				Transform joint = animator.GetBoneTransform(bone);
				if (joint != null)
				{
					bounds.Encapsulate(joint.position + Vector3.up * (bone == HumanBodyBones.Head ? 0.15f : 0f));
				}
			}
			Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
			float hipOverSaddle = hips != null ? hips.position.y - up - mount.SaddleHeight : float.NaN;
			float radius = bounds.extents.magnitude;
			float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.9f;
			Vector3 from = new Vector3(-0.82f, action == Action.Flying ? 0.5f : 0.22f, 0.55f).normalized;
			camera.transform.position = bounds.center + from * distance;
			camera.transform.LookAt(bounds.center);

			string name = $"{mount.name.Replace(' ', '_')}_{action.ToString().ToLowerInvariant()}";
			Save(camera, name);
			Report.AppendLine($"{name,-24} saddle {mount.SaddleHeight:0.00} m  lift {body.Lift:0.00} m  hips-saddle {hipOverSaddle:+0.00;-0.00} m  " +
				$"size {bounds.size.x:0.0}x{bounds.size.y:0.0}x{bounds.size.z:0.0}");

			body.Dispose();
			UnityEngine.Object.DestroyImmediate(root);
			UnityEngine.Object.DestroyImmediate(mesh);
		}

		private static void Save(Camera camera, string name)
		{
			var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 8 };
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
			UnityEngine.Object.DestroyImmediate(readable);
			texture.Release();
			UnityEngine.Object.DestroyImmediate(texture);
		}

		private static void Finish(int code)
		{
			string text = Report.ToString();
			Debug.Log("[MountRenderProbe]\n" + text);
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
