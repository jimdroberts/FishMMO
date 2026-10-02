using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Shows the scene boundary as a glass barrier that breaks where the local player pushes into
	/// it, so the edge of the playable area is never a surprise. Driven from <c>Client.Update</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same boxes the server enforces.</b> The scene server checks
	/// <c>SceneBoundaryDetails.ContainsPoint</c>: axis-aligned boxes, inside any one of them is
	/// inside. This reads the scene's <see cref="SceneBoundary"/> components — what that cache is
	/// built from — and measures the same boxes, so the glass stands exactly where the server will
	/// stop the player. A face shared with a neighbouring box is not an edge, and shows nothing.
	/// </para>
	/// <para>
	/// <b>One break, set entirely by distance.</b> Each frame the shader is told where on the face
	/// the player's chest is, how hard they are pressing (from how close they are), and how much of
	/// the invisible force a metre or two short of the boundary is on. The fracture pattern itself
	/// is fixed to the face, so walking closer breaks it further, backing off eases it back, and
	/// walking along the wall carries the break through the pattern. Nothing is timed, so nothing
	/// restarts. The look is <c>FishMMO/Boundary Glass</c>; see <see cref="RequestSceneTextures"/>
	/// for what its refraction costs and when.
	/// </para>
	/// <para>
	/// <b>Every face, the ceiling included.</b> A flier meets the top of the box the same way a
	/// walker meets a side, and sees the same glass overhead.
	/// </para>
	/// <para>
	/// Presentation only: it never moves the player or blocks anything. Client-only by assembly:
	/// <c>FishMMO.Client</c> is not compiled into the server.
	/// </para>
	/// </remarks>
	public sealed class ClientBoundaryWarning
	{
		/// <summary>The shader the panes are drawn with; its material lives in the client addressables so builds include it.</summary>
		public const string ShaderName = "FishMMO/Boundary Glass";

		/// <summary>
		/// The tuned material: its address in the client's static addressables, and its path for
		/// the editor. Its values are the glass's look; the shader's own defaults are only the
		/// fallback if it cannot be loaded.
		/// </summary>
		public const string MaterialAddress = "Boundary Glass";
		public const string MaterialEditorPath = "Assets/Prefabs/Client/Materials/Boundary/Boundary Glass.mat";

		/// <summary>How close to a face the glass starts to show, in metres.</summary>
		/// <remarks>
		/// The break is the size of a body pressing into it, which says nothing to anyone further
		/// off than this.
		/// </remarks>
		public const float WarningDistance = 12f;

		/// <summary>
		/// Where the invisible force starts to push back, in metres short of the boundary: shards
		/// begin to break loose here.
		/// </summary>
		public const float ForceEngageDistance = 2f;

		/// <summary>Where the force is fully on and the glass has given way, in metres short of the boundary.</summary>
		public const float ForceFullDistance = 1f;

		/// <summary>How high above the player's feet they press into a wall: chest height.</summary>
		public const float ImpactHeight = 1.2f;

		/// <summary>How wide the pressure reaches across the face, in metres, from first contact to full press.</summary>
		public const float PressureRadiusNear = 1.3f;
		public const float PressureRadiusFar = 0.7f;

		/// <summary>The most faces shown at once: a corner and the ceiling.</summary>
		private const int MaximumPanes = 3;

		/// <summary>Players can switch it off; it is on by default.</summary>
		public static bool Enabled = true;

		private static readonly int PressureId = Shader.PropertyToID("_Pressure");
		private static readonly int ForceId = Shader.PropertyToID("_Force");
		private static readonly int CentreId = Shader.PropertyToID("_Centre");
		private static readonly int RadiusId = Shader.PropertyToID("_Radius");
		private static readonly int AxisUId = Shader.PropertyToID("_AxisU");
		private static readonly int AxisVId = Shader.PropertyToID("_AxisV");

		private IPlayerCharacter localCharacter;
		private readonly List<Bounds> boxes = new List<Bounds>();
		private int boxesSceneHandle = -1;
		private string boxesSceneName;

		private GameObject root;
		private Material material;
		private Material tuned;
		private bool tunedSettled;
		private AsyncOperationHandle<Material> tunedHandle;
		private Mesh quad;
		private readonly List<MeshRenderer> panes = new List<MeshRenderer>();
		private MaterialPropertyBlock block;
		private readonly List<Face> faces = new List<Face>(6);

		/// <summary>The camera whose scene textures were turned on for the glass, and what it had before.</summary>
		private UniversalAdditionalCameraData texturedCamera;
		private CameraOverrideOption previousColour;
		private CameraOverrideOption previousDepth;

		private struct Face
		{
			public Vector3 Point;
			public Vector3 Normal;
			public float Distance;
		}

		public void Initialize()
		{
			IPlayerCharacter.OnStartLocalClient += OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient += OnStopLocalClient;
			LoadTunedMaterial();
		}

		/// <summary>
		/// Loads the tuned material: straight off disk in the editor, through the client's
		/// addressables in a player, asynchronously, since a WebGL player cannot wait on a load.
		/// </summary>
		private void LoadTunedMaterial()
		{
#if UNITY_EDITOR
			tuned = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialEditorPath);
			tunedSettled = true;
#else
			tunedHandle = Addressables.LoadAssetAsync<Material>(MaterialAddress);
			tunedHandle.Completed += operation =>
			{
				tuned = operation.Status == AsyncOperationStatus.Succeeded ? operation.Result : null;
				tunedSettled = true;
			};
#endif
		}

		public void Shutdown()
		{
			IPlayerCharacter.OnStartLocalClient -= OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient -= OnStopLocalClient;
			RequestSceneTextures(false);
			localCharacter = null;
			if (root != null)
			{
				Object.Destroy(root);
			}
			if (material != null)
			{
				Object.Destroy(material);
			}
			if (tunedHandle.IsValid())
			{
				Addressables.Release(tunedHandle);
			}
			tuned = null;
			tunedSettled = false;
			if (quad != null)
			{
				Object.Destroy(quad);
			}
			root = null;
			material = null;
			quad = null;
			panes.Clear();
		}

		private void OnStartLocalClient(IPlayerCharacter character)
		{
			localCharacter = character;
			boxesSceneHandle = -1;
		}

		private void OnStopLocalClient(IPlayerCharacter character)
		{
			if (ReferenceEquals(localCharacter, character))
			{
				localCharacter = null;
				HideFrom(0);
			}
		}

		public void Tick()
		{
			if (!Enabled || localCharacter == null || localCharacter.Transform == null)
			{
				HideFrom(0);
				RequestSceneTextures(false);
				return;
			}

			RefreshBoxes();
			FindFaces(localCharacter.Transform.position);
			if (faces.Count == 0 || !EnsurePanes())
			{
				HideFrom(0);
				RequestSceneTextures(false);
				return;
			}

			int shown = 0;
			for (int i = 0; i < faces.Count && shown < MaximumPanes; i++, shown++)
			{
				Show(panes[shown], faces[i]);
			}
			HideFrom(shown);
			RequestSceneTextures(shown > 0);
		}

		/// <summary>
		/// Turns the main camera's opaque and depth textures on while glass is showing, and puts
		/// its own settings back when it is not.
		/// </summary>
		/// <remarks>
		/// The glass refracts the world through it, which needs the camera's copy of the opaque
		/// scene, and checks depth so nothing in front of the pane or under the sea is bent. The
		/// Balanced and Performant pipeline assets leave the opaque copy off, and a copy every
		/// frame for a pane that shows only at the edge of the map would be waste; so it is asked
		/// for per camera, only while a pane is up.
		/// </remarks>
		private void RequestSceneTextures(bool wanted)
		{
			if (wanted)
			{
				Camera camera = Camera.main;
				UniversalAdditionalCameraData data = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
				if (data == texturedCamera)
				{
					return;
				}
				RequestSceneTextures(false);
				if (data == null)
				{
					return;
				}
				texturedCamera = data;
				previousColour = data.requiresColorOption;
				previousDepth = data.requiresDepthOption;
				data.requiresColorOption = CameraOverrideOption.On;
				data.requiresDepthOption = CameraOverrideOption.On;
				return;
			}
			if (texturedCamera != null)
			{
				texturedCamera.requiresColorOption = previousColour;
				texturedCamera.requiresDepthOption = previousDepth;
			}
			texturedCamera = null;
		}

		// ── The boxes ─────────────────────────────────────────────────

		/// <summary>Reads the boundaries of the scene the player is standing in, once per scene.</summary>
		private void RefreshBoxes()
		{
			string sceneName = localCharacter.CurrentSceneName();
			Scene scene = string.IsNullOrEmpty(sceneName) ? default : SceneManager.GetSceneByName(sceneName);
			if (!scene.IsValid())
			{
				scene = localCharacter.Transform.gameObject.scene;
			}
			if (!scene.IsValid() || (scene.handle == boxesSceneHandle && sceneName == boxesSceneName))
			{
				return;
			}
			boxesSceneHandle = scene.handle;
			boxesSceneName = sceneName;
			boxes.Clear();
			foreach (GameObject sceneRoot in scene.GetRootGameObjects())
			{
				foreach (SceneBoundary boundary in sceneRoot.GetComponentsInChildren<SceneBoundary>(true))
				{
					boxes.Add(new Bounds(boundary.GetBoundaryOffset(), boundary.GetBoundarySize()));
				}
			}
		}

		/// <summary>
		/// The faces of the player's box within warning distance, nearest first, leaving out any
		/// face that opens into another box.
		/// </summary>
		private void FindFaces(Vector3 position)
		{
			faces.Clear();
			if (boxes.Count == 0)
			{
				return;
			}

			int containing = -1;
			float nearestOutside = float.MaxValue;
			int nearestBox = -1;
			for (int i = 0; i < boxes.Count; i++)
			{
				if (Contains(boxes[i], position))
				{
					containing = i;
					break;
				}
				float d = Mathf.Sqrt(boxes[i].SqrDistance(position));
				if (d < nearestOutside)
				{
					nearestOutside = d;
					nearestBox = i;
				}
			}

			// Already outside: full glass on the nearest box, until the server brings them back.
			bool outside = containing < 0;
			Bounds box = boxes[outside ? nearestBox : containing];
			Vector3 min = box.min, max = box.max;

			// Walls are pressed at chest height, not at the feet.
			float chest = Mathf.Clamp(position.y + ImpactHeight, min.y, max.y);
			AddFace(new Vector3(1f, 0f, 0f), max.x - position.x, new Vector3(max.x, chest, position.z), outside);
			AddFace(new Vector3(-1f, 0f, 0f), position.x - min.x, new Vector3(min.x, chest, position.z), outside);
			AddFace(new Vector3(0f, 0f, 1f), max.z - position.z, new Vector3(position.x, chest, max.z), outside);
			AddFace(new Vector3(0f, 0f, -1f), position.z - min.z, new Vector3(position.x, chest, min.z), outside);
			AddFace(new Vector3(0f, 1f, 0f), max.y - position.y, new Vector3(position.x, max.y, position.z), outside);
			AddFace(new Vector3(0f, -1f, 0f), position.y - min.y, new Vector3(position.x, min.y, position.z), outside);

			faces.Sort((a, b) => a.Distance.CompareTo(b.Distance));
		}

		private void AddFace(Vector3 normal, float distance, Vector3 point, bool outside)
		{
			if (!outside && distance > WarningDistance)
			{
				return;
			}
			if (outside && distance > 0f)
			{
				// Only the faces the player has crossed.
				return;
			}
			// A face that opens into another box is not the edge of the playable area.
			Vector3 beyond = point + normal * 0.5f;
			for (int i = 0; i < boxes.Count; i++)
			{
				if (Contains(boxes[i], beyond))
				{
					return;
				}
			}
			faces.Add(new Face { Point = point, Normal = normal, Distance = Mathf.Max(0f, distance) });
		}

		/// <summary>The server's test, inclusive on every face (<c>SceneBoundaryDetails.ContainsPoint</c>).</summary>
		private static bool Contains(Bounds box, Vector3 point)
		{
			Vector3 min = box.min, max = box.max;
			return point.x >= min.x && point.x <= max.x
				&& point.z >= min.z && point.z <= max.z
				&& point.y >= min.y && point.y <= max.y;
		}

		// ── The panes ─────────────────────────────────────────────────

		private bool EnsurePanes()
		{
			if (material == null)
			{
				if (!tunedSettled)
				{
					// Still loading: show nothing for a frame or two rather than the untuned look.
					return false;
				}
				if (tuned != null)
				{
					material = new Material(tuned) { name = "Boundary Glass", hideFlags = HideFlags.DontSave };
				}
				else
				{
					Shader shader = Shader.Find(ShaderName);
					if (shader == null)
					{
						return false;
					}
					material = new Material(shader) { name = "Boundary Glass", hideFlags = HideFlags.DontSave };
				}
			}
			if (quad == null)
			{
				quad = BuildQuad();
			}
			if (root == null)
			{
				root = new GameObject("Boundary Warning") { hideFlags = HideFlags.DontSave };
				Object.DontDestroyOnLoad(root);
				panes.Clear();
			}
			while (panes.Count < MaximumPanes)
			{
				var pane = new GameObject($"Pane {panes.Count}");
				pane.transform.SetParent(root.transform, false);
				pane.AddComponent<MeshFilter>().sharedMesh = quad;
				MeshRenderer renderer = pane.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				renderer.lightProbeUsage = LightProbeUsage.Off;
				renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
				pane.SetActive(false);
				panes.Add(renderer);
			}
			block ??= new MaterialPropertyBlock();
			return true;
		}

		/// <summary>
		/// Lays a pane on its face in front of the player and tells the shader how hard they are
		/// pressing. Everything here is a function of where the player is now.
		/// </summary>
		private void Show(MeshRenderer pane, Face face)
		{
			// Pressure builds from the edge of the warning distance in to the force line, mostly up
			// close: cubed, so at 6 m a few coarse cracks show, at 3 m it is about half, and the
			// finest fracture starts as the force engages. The force itself engages a metre or two
			// short of the boundary, where the glass gives way.
			float closeness = 1f - Mathf.Clamp01((face.Distance - ForceFullDistance) / (WarningDistance - ForceFullDistance));
			float pressure = closeness * closeness * closeness;
			float force = 1f - Mathf.Clamp01((face.Distance - ForceFullDistance) / (ForceEngageDistance - ForceFullDistance));
			force = force * force * (3f - 2f * force);
			// About a body wide, spreading a little as they press harder.
			float radius = Mathf.Lerp(PressureRadiusFar, PressureRadiusNear, pressure);

			// The face's own axes: U along the ground for walls, V up; for floor and ceiling, X and Z.
			Vector3 axisU, axisV;
			if (Mathf.Abs(face.Normal.y) > 0.5f)
			{
				axisU = Vector3.right;
				axisV = Vector3.forward;
			}
			else
			{
				axisU = Vector3.Cross(Vector3.up, face.Normal).normalized;
				axisV = Vector3.up;
			}

			Transform t = pane.transform;
			t.position = face.Point;
			t.rotation = Quaternion.LookRotation(face.Normal, Mathf.Abs(face.Normal.y) > 0.5f ? Vector3.forward : Vector3.up);
			// Wide enough for the pressure to have faded to nothing inside it.
			float size = radius * 3f;
			t.localScale = new Vector3(size, size, 1f);

			pane.GetPropertyBlock(block);
			block.SetFloat(PressureId, pressure);
			block.SetFloat(ForceId, force);
			block.SetVector(CentreId, face.Point);
			block.SetFloat(RadiusId, radius);
			block.SetVector(AxisUId, axisU);
			block.SetVector(AxisVId, axisV);
			pane.SetPropertyBlock(block);
			if (!pane.gameObject.activeSelf)
			{
				pane.gameObject.SetActive(true);
			}
		}

		private void HideFrom(int index)
		{
			for (int i = index; i < panes.Count; i++)
			{
				if (panes[i] != null && panes[i].gameObject.activeSelf)
				{
					panes[i].gameObject.SetActive(false);
				}
			}
		}

		/// <summary>A unit quad in X/Y facing +Z, which LookRotation turns onto the face.</summary>
		private static Mesh BuildQuad()
		{
			var mesh = new Mesh { name = "Boundary pane", hideFlags = HideFlags.DontSave };
			mesh.vertices = new[]
			{
				new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
				new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
			};
			mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
			mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
