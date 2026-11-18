using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A tree's trunk as the ambient life climbs it: its axis and radius up the lower trunk, measured from the tree's own
	/// collision mesh, placed where the tree stands. A squirrel clinging to the bark sits on this surface, which is the
	/// surface the trunk's collider has, rather than on a guess round the crown's middle (2026-10-11: "when they jump
	/// onto trees they miss the colliders").
	/// </summary>
	public sealed class AmbientTrunk
	{
		/// <summary>The trunk's axis and radius at heights up a prototype, in its own space (before the tree's scale).</summary>
		public sealed class Profile
		{
			/// <summary>Heights of the samples, prototype space, rising.</summary>
			public float[] Heights;
			/// <summary>The axis (x, z) at each height, prototype space.</summary>
			public Vector2[] Centres;
			/// <summary>The bark's distance from the axis at each height, prototype space.</summary>
			public float[] Radii;

			/// <summary>A straight trunk on the prototype's origin: for trees with no readable collision mesh.</summary>
			public static Profile Plain(float height, float radius)
			{
				float top = Mathf.Max(1f, height * 0.5f);
				return new Profile
				{
					Heights = new[] { 0f, top },
					Centres = new[] { Vector2.zero, Vector2.zero },
					Radii = new[] { radius, radius * 0.75f },
				};
			}
		}

		/// <summary>Where the tree stands (its trunk's foot), world space.</summary>
		public Vector3 Base;
		public Quaternion Rotation = Quaternion.identity;
		public Vector3 Scale = Vector3.one;
		public Profile Shape;

		/// <summary>Metres up from the foot the measured trunk reaches (where it is known to be trunk), world space.</summary>
		public float Top => Shape.Heights[Shape.Heights.Length - 1] * Scale.y;

		/// <summary>The trunk's axis and radius <paramref name="up"/> metres above its foot, world space.</summary>
		public void At(float up, out Vector3 axis, out float radius)
		{
			float h = Mathf.Clamp(up / Mathf.Max(0.01f, Scale.y), Shape.Heights[0], Shape.Heights[Shape.Heights.Length - 1]);
			int i = 0;
			while (i < Shape.Heights.Length - 2 && Shape.Heights[i + 1] < h)
			{
				i++;
			}
			int j = Mathf.Min(i + 1, Shape.Heights.Length - 1);
			float span = Shape.Heights[j] - Shape.Heights[i];
			float u = span > 1e-4f ? Mathf.Clamp01((h - Shape.Heights[i]) / span) : 0f;
			Vector2 c = Vector2.Lerp(Shape.Centres[i], Shape.Centres[j], u);
			float r = Mathf.Lerp(Shape.Radii[i], Shape.Radii[j], u);
			axis = Base + Rotation * Vector3.Scale(Scale, new Vector3(c.x, h, c.y));
			radius = r * 0.5f * (Mathf.Abs(Scale.x) + Mathf.Abs(Scale.z));
		}

		/// <summary>
		/// The bark <paramref name="up"/> metres above the foot on the side facing <paramref name="outward"/> (horizontal),
		/// the way up the trunk there (its lean) and the bark's outward normal.
		/// </summary>
		public Vector3 Surface(float up, Vector3 outward, out Vector3 along, out Vector3 normal)
		{
			At(up, out Vector3 axis, out float radius);
			At(up + 0.4f, out Vector3 above, out _);
			along = above - axis;
			along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.up;
			Vector3 side = Vector3.ProjectOnPlane(new Vector3(outward.x, 0f, outward.z), along);
			side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.forward;
			normal = side;
			return axis + side * (radius + 0.01f);
		}

		// ── Measuring a prototype ─────────────────────────────────────

		private static readonly Dictionary<Mesh, Profile> measured = new Dictionary<Mesh, Profile>();
		private static readonly List<Vector3> scratchVertices = new List<Vector3>();
		private static readonly List<int> scratchTriangles = new List<int>();
		private static readonly List<Vector2> slab = new List<Vector2>();
		private static readonly List<float> distances = new List<float>();

		/// <summary>Spacing of the samples up the trunk, metres (prototype space).</summary>
		public const float SampleMetres = 0.5f;
		/// <summary>The highest a trunk is measured to, metres (prototype space): what a squirrel climbs.</summary>
		public const float MaxMeasured = 10f;

		/// <summary>Forgets every measured prototype (scenes unloaded, play mode left).</summary>
		public static void ClearCache() => measured.Clear();

		/// <summary>
		/// The trunk of a tree prefab, from its collision mesh: at each height, the outline where the plane cuts the mesh,
		/// kept to the part nearest the trunk below (limbs crossing the plane further out are left out); its centre is the
		/// axis and its median distance the radius. A prefab with no readable collision mesh gets a straight trunk on its
		/// origin, its radius a fortieth of its height.
		/// </summary>
		public static Profile Measure(GameObject prefab, float height)
		{
			if (prefab == null)
			{
				return Profile.Plain(height, Mathf.Clamp(height * 0.025f, 0.12f, 0.5f));
			}
			MeshCollider collider = prefab.GetComponentInChildren<MeshCollider>(true);
			Mesh mesh = collider != null ? collider.sharedMesh : null;
			if (mesh == null || !mesh.isReadable)
			{
				return Profile.Plain(height, Mathf.Clamp(height * 0.025f, 0.12f, 0.5f));
			}
			if (measured.TryGetValue(mesh, out Profile known))
			{
				return known;
			}
			Matrix4x4 local = Matrix4x4.identity;
			for (Transform t = collider.transform; t != null && t != prefab.transform; t = t.parent)
			{
				local = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * local;
			}
			scratchVertices.Clear();
			mesh.GetVertices(scratchVertices);
			for (int i = 0; i < scratchVertices.Count; i++)
			{
				scratchVertices[i] = local.MultiplyPoint3x4(scratchVertices[i]);
			}
			Profile profile = Measure(scratchVertices, Triangles(mesh), Mathf.Min(MaxMeasured, Mathf.Max(1f, height * 0.6f)));
			if (profile == null)
			{
				profile = Profile.Plain(height, Mathf.Clamp(height * 0.025f, 0.12f, 0.5f));
			}
			measured[mesh] = profile;
			return profile;
		}

		private static List<int> Triangles(Mesh mesh)
		{
			scratchTriangles.Clear();
			var one = new List<int>();
			for (int s = 0; s < mesh.subMeshCount; s++)
			{
				mesh.GetTriangles(one, s);
				scratchTriangles.AddRange(one);
			}
			return scratchTriangles;
		}

		/// <summary>The trunk of a triangle mesh standing on its origin, sampled to <paramref name="top"/>; null when no trunk is found at its foot.</summary>
		public static Profile Measure(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles, float top)
		{
			var heights = new List<float>();
			var centres = new List<Vector2>();
			var radii = new List<float>();
			Vector2 previous = Vector2.zero;
			float previousRadius = 1.5f;
			for (float h = SampleMetres * 0.5f; h <= top + 1e-3f; h += SampleMetres)
			{
				slab.Clear();
				for (int t = 0; t + 2 < triangles.Count; t += 3)
				{
					Cut(vertices[triangles[t]], vertices[triangles[t + 1]], h, slab);
					Cut(vertices[triangles[t + 1]], vertices[triangles[t + 2]], h, slab);
					Cut(vertices[triangles[t + 2]], vertices[triangles[t]], h, slab);
				}
				// The trunk's part of the cut: what lies near the axis below. A trunk's radius changes little in half a metre and
				// a limb leaving it is further out, so the band is the radius below and a little more (at the foot, wide open).
				float keep = heights.Count == 0 ? Mathf.Max(0.4f, previousRadius * 2.5f) : previousRadius * 1.5f + 0.12f;
				Vector2 sum = Vector2.zero;
				int n = 0;
				foreach (Vector2 p in slab)
				{
					if ((p - previous).sqrMagnitude <= keep * keep)
					{
						sum += p;
						n++;
					}
				}
				if (n < 3)
				{
					break; // above the trunk (or below a mesh that does not reach the ground)
				}
				Vector2 centre = sum / n;
				distances.Clear();
				foreach (Vector2 p in slab)
				{
					if ((p - previous).sqrMagnitude <= keep * keep)
					{
						distances.Add((p - centre).magnitude);
					}
				}
				distances.Sort();
				float radius = Mathf.Max(0.04f, distances[distances.Count / 2]);
				// Where the trunk forks the cut holds two stems: its outline widens past any trunk's, and it ends there.
				if (heights.Count > 0 && radius > previousRadius * 1.8f + 0.05f)
				{
					break;
				}
				heights.Add(h);
				centres.Add(centre);
				radii.Add(radius);
				previous = centre;
				previousRadius = radius;
			}
			if (heights.Count < 2)
			{
				return null;
			}
			return new Profile { Heights = heights.ToArray(), Centres = centres.ToArray(), Radii = radii.ToArray() };
		}

		/// <summary>Where an edge crosses the plane y = <paramref name="h"/>, added as (x, z).</summary>
		private static void Cut(Vector3 a, Vector3 b, float h, List<Vector2> into)
		{
			if ((a.y - h) * (b.y - h) >= 0f || Mathf.Abs(b.y - a.y) < 1e-6f)
			{
				return;
			}
			float u = (h - a.y) / (b.y - a.y);
			into.Add(new Vector2(a.x + (b.x - a.x) * u, a.z + (b.z - a.z) * u));
		}
	}
}
