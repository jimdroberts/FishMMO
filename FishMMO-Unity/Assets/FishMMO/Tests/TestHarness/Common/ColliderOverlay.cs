using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// Draws physics colliders and arbitrary debug shapes as coloured wireframes that render in the
	/// GAME view (and so in a capture), unlike Gizmos which only the editor scene view shows.
	/// </summary>
	/// <remarks>
	/// One line-topology mesh is rebuilt every <c>LateUpdate</c> from (a) every enabled collider in the
	/// loaded scenes, coloured by <see cref="Classify"/>, and (b) whatever the <see cref="ExtraShapes"/>
	/// callbacks submit for the frame (rewound poses, sweeps, hit markers). The material ignores depth so
	/// a hitbox inside a body still shows. Harness-only: nothing in the game references it.
	/// </remarks>
	public sealed class ColliderOverlay : MonoBehaviour
	{
		/// <summary>Colour for a collider, or null to skip it. Defaults to <see cref="DefaultClassify"/>.</summary>
		public Func<Collider, Color?> Classify;

		/// <summary>Per-frame extra shapes (called after the colliders are added).</summary>
		public event Action<ColliderOverlay> ExtraShapes;

		/// <summary>Colliders whose bounds are wider than this are skipped (arena floors, seas).</summary>
		public float MaxColliderExtent = 18f;

		/// <summary>Draw colliders at all (the extra shapes are always drawn).</summary>
		public bool DrawColliders = true;

		public static readonly Color CharacterColor = new Color(0.2f, 1f, 0.3f, 1f);
		public static readonly Color AttackColor = new Color(1f, 0.55f, 0.1f, 1f);
		public static readonly Color TriggerColor = new Color(0.2f, 0.85f, 1f, 0.9f);
		public static readonly Color StaticColor = new Color(0.75f, 0.75f, 0.8f, 0.55f);
		public static readonly Color RewindColor = new Color(1f, 0.95f, 0.15f, 1f);
		public static readonly Color SweepColor = new Color(1f, 0.25f, 0.9f, 1f);
		public static readonly Color HitColor = new Color(1f, 0.15f, 0.15f, 1f);

		private const int CircleSegments = 20;

		private readonly List<Vector3> vertices = new List<Vector3>(8192);
		private readonly List<Color> colors = new List<Color>(8192);
		private readonly List<int> indices = new List<int>(8192);
		private readonly List<Collider> scratch = new List<Collider>(256);
		private Mesh mesh;
		private Material material;

		/// <summary>Lines drawn this frame (for the report).</summary>
		public int LastLineCount { get; private set; }

		/// <summary>Colliders drawn this frame, by colour bucket name.</summary>
		public readonly Dictionary<string, int> LastCounts = new Dictionary<string, int>();

		private void Awake()
		{
			mesh = new Mesh { name = "ColliderOverlay" };
			mesh.MarkDynamic();
			mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			Shader shader = Shader.Find("Hidden/Internal-Colored");
			material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
			material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
			material.SetInt("_ZWrite", 0);
			material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
			material.renderQueue = 4000;

			GameObject holder = new GameObject("ColliderOverlay (mesh)");
			holder.transform.SetParent(transform, false);
			holder.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		private void OnDestroy()
		{
			if (mesh != null) Destroy(mesh);
			if (material != null) Destroy(material);
		}

		private void LateUpdate()
		{
			vertices.Clear();
			colors.Clear();
			indices.Clear();
			LastCounts.Clear();

			if (DrawColliders)
			{
				Collider[] all = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
				Func<Collider, Color?> classify = Classify ?? DefaultClassify;
				foreach (Collider collider in all)
				{
					if (collider == null || collider.bounds.extents.magnitude > MaxColliderExtent)
					{
						continue;
					}
					Color? color = classify(collider);
					if (color == null)
					{
						continue;
					}
					AddCollider(collider, color.Value);
					string bucket = BucketName(color.Value);
					LastCounts.TryGetValue(bucket, out int n);
					LastCounts[bucket] = n + 1;
				}
			}

			ExtraShapes?.Invoke(this);

			mesh.Clear();
			mesh.SetVertices(vertices);
			mesh.SetColors(colors);
			mesh.SetIndices(indices, MeshTopology.Lines, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
			LastLineCount = indices.Count / 2;
		}

		private static string BucketName(Color c)
		{
			if (c == CharacterColor) return "character";
			if (c == AttackColor) return "attack";
			if (c == TriggerColor) return "trigger";
			if (c == StaticColor) return "static";
			return "other";
		}

		/// <summary>Characters green, ability objects orange, triggers cyan, everything else grey.</summary>
		public static Color? DefaultClassify(Collider collider)
		{
			if (!collider.enabled)
			{
				return null;
			}
			if (collider.GetComponentInParent<FishMMO.Shared.BaseCharacter>() != null)
			{
				return CharacterColor;
			}
			if (collider.GetComponentInParent<FishMMO.Shared.AbilityObject>() != null)
			{
				return AttackColor;
			}
			return collider.isTrigger ? TriggerColor : StaticColor;
		}

		// ── Shapes ──────────────────────────────────────────────────────────────────

		public void AddLine(Vector3 a, Vector3 b, Color color)
		{
			int i = vertices.Count;
			vertices.Add(a);
			vertices.Add(b);
			colors.Add(color);
			colors.Add(color);
			indices.Add(i);
			indices.Add(i + 1);
		}

		public void AddCircle(Vector3 centre, Vector3 normal, float radius, Color color, float fromDeg = 0f, float toDeg = 360f)
		{
			Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			Vector3 v = Vector3.Cross(normal, u).normalized;
			AddArc(centre, u, v, radius, color, fromDeg, toDeg);
		}

		private void AddArc(Vector3 centre, Vector3 u, Vector3 v, float radius, Color color, float fromDeg, float toDeg)
		{
			int segments = Mathf.Max(4, Mathf.CeilToInt(CircleSegments * Mathf.Abs(toDeg - fromDeg) / 360f));
			Vector3 previous = Vector3.zero;
			for (int s = 0; s <= segments; ++s)
			{
				float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, s / (float)segments);
				Vector3 point = centre + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
				if (s > 0)
				{
					AddLine(previous, point, color);
				}
				previous = point;
			}
		}

		public void AddSphere(Vector3 centre, float radius, Color color)
		{
			AddCircle(centre, Vector3.up, radius, color);
			AddCircle(centre, Vector3.right, radius, color);
			AddCircle(centre, Vector3.forward, radius, color);
		}

		/// <summary>A capsule between two sphere centres.</summary>
		public void AddCapsule(Vector3 bottom, Vector3 top, float radius, Color color)
		{
			Vector3 axis = top - bottom;
			Vector3 up = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
			Vector3 u = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			Vector3 v = Vector3.Cross(up, u).normalized;
			AddArc(bottom, u, v, radius, color, 0f, 360f);
			AddArc(top, u, v, radius, color, 0f, 360f);
			AddLine(bottom + u * radius, top + u * radius, color);
			AddLine(bottom - u * radius, top - u * radius, color);
			AddLine(bottom + v * radius, top + v * radius, color);
			AddLine(bottom - v * radius, top - v * radius, color);
			// Hemispherical caps on two planes.
			AddArc(top, u, up, radius, color, 0f, 180f);
			AddArc(top, v, up, radius, color, 0f, 180f);
			AddArc(bottom, u, -up, radius, color, 0f, 180f);
			AddArc(bottom, v, -up, radius, color, 0f, 180f);
		}

		/// <summary>An oriented box from a centre, rotation and half extents.</summary>
		public void AddBox(Vector3 centre, Quaternion rotation, Vector3 halfExtents, Color color)
		{
			Vector3[] c = new Vector3[8];
			for (int i = 0; i < 8; ++i)
			{
				Vector3 corner = new Vector3(
					(i & 1) == 0 ? -halfExtents.x : halfExtents.x,
					(i & 2) == 0 ? -halfExtents.y : halfExtents.y,
					(i & 4) == 0 ? -halfExtents.z : halfExtents.z);
				c[i] = centre + rotation * corner;
			}
			int[] edges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
			for (int e = 0; e < edges.Length; e += 2)
			{
				AddLine(c[edges[e]], c[edges[e + 1]], color);
			}
		}

		/// <summary>Draws a collider at its live pose.</summary>
		public void AddCollider(Collider collider, Color color)
		{
			Transform t = collider.transform;
			AddColliderAt(collider, t.position, t.rotation, t.lossyScale, color);
		}

		/// <summary>
		/// Draws <paramref name="collider"/> as if its ROOT stood at <paramref name="rootPosition"/> /
		/// <paramref name="rootRotation"/> — how a rewound pose is shown with the collider's real shape.
		/// </summary>
		public void AddColliderAtRootPose(Collider collider, Transform root, Vector3 rootPosition, Quaternion rootRotation, Color color)
		{
			Transform t = collider.transform;
			Quaternion delta = rootRotation * Quaternion.Inverse(root.rotation);
			Vector3 position = rootPosition + delta * (t.position - root.position);
			AddColliderAt(collider, position, delta * t.rotation, t.lossyScale, color);
		}

		private void AddColliderAt(Collider collider, Vector3 position, Quaternion rotation, Vector3 scale, Color color)
		{
			switch (collider)
			{
				case CapsuleCollider capsule:
				{
					Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
					float axisScale = Mathf.Abs(capsule.direction == 0 ? scale.x : capsule.direction == 1 ? scale.y : scale.z);
					float radiusScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
						: capsule.direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
						: Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
					float radius = capsule.radius * radiusScale;
					float half = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
					Vector3 centre = position + rotation * Vector3.Scale(capsule.center, scale);
					Vector3 worldAxis = rotation * axis;
					AddCapsule(centre - worldAxis * half, centre + worldAxis * half, radius, color);
					break;
				}
				case CharacterController controller:
				{
					float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
					float half = Mathf.Max(0f, controller.height * Mathf.Abs(scale.y) * 0.5f - radius);
					Vector3 centre = position + rotation * Vector3.Scale(controller.center, scale);
					Vector3 worldAxis = rotation * Vector3.up;
					AddCapsule(centre - worldAxis * half, centre + worldAxis * half, radius, color);
					break;
				}
				case SphereCollider sphere:
				{
					float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
					Vector3 centre = position + rotation * Vector3.Scale(sphere.center, scale);
					AddSphere(centre, radius, color);
					break;
				}
				case BoxCollider box:
				{
					Vector3 centre = position + rotation * Vector3.Scale(box.center, scale);
					AddBox(centre, rotation, Vector3.Scale(box.size, scale) * 0.5f, color);
					break;
				}
				default:
				{
					Bounds b = collider.bounds;
					AddBox(b.center, Quaternion.identity, b.extents, color);
					break;
				}
			}
		}
	}
}
