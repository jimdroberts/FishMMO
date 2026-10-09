using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Water
{
	/// <summary>
	/// The rock round one fall, for the water to meet as it is traced down (<see cref="InlandWaterRenderer"/>): the terrain
	/// read once into a grid, the triangles of every baked prop's collision mesh in reach, and any live collider besides.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>From the baked data, not from physics.</b> A scene's props are collided only where the
	/// <see cref="PropColliderStreamer"/> has put chunks of them into physics: in play, within 128 m of the camera, a few
	/// frames after the scene loads. The falls are built as the scene loads, so asking physics found no prop at all and
	/// no curtain ever parted round a rock in a real scene. The <see cref="ScenePropCollisionSet"/>s hold the same
	/// collision meshes and where each prop stands, in the editor as in play and at any distance.
	/// </para>
	/// <para>
	/// <b>The terrain once.</b> Asking every terrain for its height at every test cost about half a million lookups a
	/// fall; the ground under a fall is read once into a quarter-metre grid and sampled bilinearly from that.
	/// </para>
	/// </remarks>
	internal sealed class FallRock
	{
		/// <summary>The terrain grid's spacing, metres.</summary>
		private const float GridMetres = 0.25f;
		/// <summary>The prop triangles' bucket size, metres.</summary>
		private const float BucketMetres = 2f;

		private readonly Vector3 min;
		private readonly int columns, rows;
		private readonly float[] heights;
		private readonly bool hasGround;

		private readonly List<Vector3> triangles = new List<Vector3>();
		private readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
		private readonly PhysicsScene physics;
		private readonly bool liveColliders;
		private static readonly Collider[] overlaps = new Collider[16];

		/// <summary>How many prop triangles it holds: for logs and tests.</summary>
		public int TriangleCount => triangles.Count / 3;

		/// <summary>Reads the rock in <paramref name="bounds"/> from <paramref name="scene"/>.</summary>
		public FallRock(Scene scene, Bounds bounds)
		{
			min = bounds.min;
			columns = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / GridMetres) + 1);
			rows = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / GridMetres) + 1);
			heights = new float[columns * rows];
			for (int z = 0; z < rows; z++)
			{
				for (int x = 0; x < columns; x++)
				{
					float h = TerrainHeight(scene, min.x + x * GridMetres, min.z + z * GridMetres);
					heights[z * columns + x] = h;
					hasGround |= !float.IsNegativeInfinity(h);
				}
			}
			GatherProps(scene, bounds);
			physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
			liveColliders = LiveCollidersIn(bounds);
		}

		/// <summary>The highest terrain of <paramref name="scene"/> at (x, z); negative infinity off every terrain.</summary>
		private static float TerrainHeight(Scene scene, float x, float z)
		{
			float best = float.NegativeInfinity;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null || terrain.gameObject.scene != scene)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
				if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z)
				{
					continue;
				}
				best = Mathf.Max(best, terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y);
			}
			return best;
		}

		/// <summary>The ground's height at (x, z) from the grid; negative infinity outside it or off the terrain.</summary>
		public float Ground(float x, float z)
		{
			if (!hasGround)
			{
				return float.NegativeInfinity;
			}
			float gx = (x - min.x) / GridMetres, gz = (z - min.z) / GridMetres;
			if (gx < 0f || gz < 0f || gx > columns - 1 || gz > rows - 1)
			{
				return float.NegativeInfinity;
			}
			int x0 = Mathf.Min(columns - 2, (int)gx), z0 = Mathf.Min(rows - 2, (int)gz);
			float tx = gx - x0, tz = gz - z0;
			float a = heights[z0 * columns + x0], b = heights[z0 * columns + x0 + 1];
			float c = heights[(z0 + 1) * columns + x0], d = heights[(z0 + 1) * columns + x0 + 1];
			if (float.IsNegativeInfinity(a) || float.IsNegativeInfinity(b) || float.IsNegativeInfinity(c) || float.IsNegativeInfinity(d))
			{
				return Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
			}
			return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
		}

		/// <summary>The ground's normal at (x, z), from its slope across a metre.</summary>
		public Vector3 GroundNormal(float x, float z)
		{
			const float e = 0.5f;
			float hx0 = Ground(x - e, z), hx1 = Ground(x + e, z), hz0 = Ground(x, z - e), hz1 = Ground(x, z + e);
			if (float.IsNegativeInfinity(hx0) || float.IsNegativeInfinity(hx1) || float.IsNegativeInfinity(hz0) || float.IsNegativeInfinity(hz1))
			{
				return Vector3.up;
			}
			return new Vector3(hx0 - hx1, 2f * e, hz0 - hz1).normalized;
		}

		private static long Key(int x, int y, int z) => ((long)(x & 0xFFFFF) << 40) | ((long)(y & 0xFFFFF) << 20) | (long)(z & 0xFFFFF);

		/// <summary>Every baked prop's collision triangles that reach into <paramref name="bounds"/>, bucketed.</summary>
		private void GatherProps(Scene scene, Bounds bounds)
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (ScenePropColliders owner in root.GetComponentsInChildren<ScenePropColliders>(true))
				{
					foreach (ScenePropCollisionSet set in owner.Sets)
					{
						if (set == null || set.Prototypes == null || set.Instances == null)
						{
							continue;
						}
						foreach (ScenePropCollisionSet.Instance instance in set.Instances)
						{
							if (instance.Prototype < 0 || instance.Prototype >= set.Prototypes.Length)
							{
								continue;
							}
							Mesh mesh = set.Prototypes[instance.Prototype].Mesh;
							if (mesh == null || !mesh.isReadable)
							{
								continue;
							}
							Matrix4x4 matrix = Matrix4x4.TRS(instance.Position, instance.Rotation, instance.Scale);
							Bounds world = TransformBounds(mesh.bounds, matrix);
							if (!world.Intersects(bounds))
							{
								continue;
							}
							AddMesh(mesh, matrix, bounds);
						}
					}
				}
			}
		}

		private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
		{
			Vector3 centre = matrix.MultiplyPoint3x4(local.center);
			Vector3 e = local.extents;
			Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0f, 0f)), y = matrix.MultiplyVector(new Vector3(0f, e.y, 0f)), z = matrix.MultiplyVector(new Vector3(0f, 0f, e.z));
			var extents = new Vector3(
				Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
				Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
				Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
			return new Bounds(centre, 2f * extents);
		}

		private readonly List<Vector3> meshVertices = new List<Vector3>();
		private readonly List<int> meshIndices = new List<int>();

		private void AddMesh(Mesh mesh, Matrix4x4 matrix, Bounds bounds)
		{
			mesh.GetVertices(meshVertices);
			for (int s = 0; s < mesh.subMeshCount; s++)
			{
				if (mesh.GetTopology(s) != MeshTopology.Triangles)
				{
					continue;
				}
				mesh.GetTriangles(meshIndices, s);
				for (int i = 0; i + 2 < meshIndices.Count; i += 3)
				{
					Vector3 a = matrix.MultiplyPoint3x4(meshVertices[meshIndices[i]]);
					Vector3 b = matrix.MultiplyPoint3x4(meshVertices[meshIndices[i + 1]]);
					Vector3 c = matrix.MultiplyPoint3x4(meshVertices[meshIndices[i + 2]]);
					var box = new Bounds(a, Vector3.zero);
					box.Encapsulate(b);
					box.Encapsulate(c);
					if (!box.Intersects(bounds))
					{
						continue;
					}
					int index = triangles.Count;
					triangles.Add(a);
					triangles.Add(b);
					triangles.Add(c);
					Vector3Int lo = Bucket(box.min), hi = Bucket(box.max);
					for (int x = lo.x; x <= hi.x; x++)
					{
						for (int y = lo.y; y <= hi.y; y++)
						{
							for (int z = lo.z; z <= hi.z; z++)
							{
								long key = Key(x, y, z);
								if (!buckets.TryGetValue(key, out List<int> list))
								{
									buckets[key] = list = new List<int>();
								}
								list.Add(index);
							}
						}
					}
				}
			}
		}

		private static Vector3Int Bucket(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / BucketMetres), Mathf.FloorToInt(p.y / BucketMetres), Mathf.FloorToInt(p.z / BucketMetres));

		/// <summary>Whether any live collider but terrain stands in <paramref name="bounds"/>: a hand-placed rock, a test's.</summary>
		private bool LiveCollidersIn(Bounds bounds)
		{
			int found = physics.OverlapBox(bounds.center, bounds.extents, overlaps, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < found; i++)
			{
				if (overlaps[i] != null && !(overlaps[i] is TerrainCollider) && !overlaps[i].isTrigger)
				{
					return true;
				}
			}
			return false;
		}

		private readonly HashSet<int> visited = new HashSet<int>();

		/// <summary>
		/// The prop rock nearest <paramref name="point"/> within <paramref name="radius"/>: its closest point and the normal
		/// out of it there. False where no prop is that near.
		/// </summary>
		public bool PropContact(Vector3 point, float radius, out Vector3 closest, out Vector3 normal)
		{
			closest = default;
			normal = Vector3.up;
			float best = radius * radius;
			bool found = false;
			if (triangles.Count > 0)
			{
				visited.Clear();
				Vector3Int lo = Bucket(point - Vector3.one * radius), hi = Bucket(point + Vector3.one * radius);
				for (int x = lo.x; x <= hi.x; x++)
				{
					for (int y = lo.y; y <= hi.y; y++)
					{
						for (int z = lo.z; z <= hi.z; z++)
						{
							if (!buckets.TryGetValue(Key(x, y, z), out List<int> list))
							{
								continue;
							}
							foreach (int t in list)
							{
								if (!visited.Add(t))
								{
									continue;
								}
								Vector3 a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
								Vector3 q = ClosestOnTriangle(point, a, b, c);
								float d2 = (point - q).sqrMagnitude;
								if (d2 < best)
								{
									best = d2;
									closest = q;
									Vector3 away = point - q;
									Vector3 face = Vector3.Cross(b - a, c - a);
									face = face.sqrMagnitude > 1e-12f ? face.normalized : Vector3.up;
									/* Out of the rock: from the surface to the water, unless the water has already sunk behind
									 * the surface (a step taken into the rock), when the way out is the face's own outward side. */
									normal = away.sqrMagnitude > 1e-8f && Vector3.Dot(away, face) >= 0f ? away.normalized : face;
									found = true;
								}
							}
						}
					}
				}
			}
			return found;
		}

		/// <summary>
		/// A live collider struck moving from <paramref name="from"/> to <paramref name="to"/> with a sphere of <paramref name="radius"/>:
		/// where, and its normal. Only when one stands near the fall.
		/// </summary>
		public bool LiveContact(Vector3 from, Vector3 to, float radius, out Vector3 point, out Vector3 normal)
		{
			point = default;
			normal = Vector3.up;
			if (!liveColliders)
			{
				return false;
			}
			Vector3 move = to - from;
			float distance = move.magnitude;
			if (distance < 1e-5f)
			{
				return false;
			}
			if (physics.SphereCast(from, radius, move / distance, out RaycastHit hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
				&& !(hit.collider is TerrainCollider) && hit.distance > 0f)
			{
				point = from + move / distance * hit.distance;
				normal = hit.normal;
				return true;
			}
			return false;
		}

		/// <summary>The point of triangle (a, b, c) nearest <paramref name="p"/> (Ericson, Real-Time Collision Detection 5.1.5).</summary>
		public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 ab = b - a, ac = c - a, ap = p - a;
			float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
			if (d1 <= 0f && d2 <= 0f)
			{
				return a;
			}
			Vector3 bp = p - b;
			float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
			if (d3 >= 0f && d4 <= d3)
			{
				return b;
			}
			float vc = d1 * d4 - d3 * d2;
			if (vc <= 0f && d1 >= 0f && d3 <= 0f)
			{
				return a + ab * (d1 / (d1 - d3));
			}
			Vector3 cp = p - c;
			float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
			if (d6 >= 0f && d5 <= d6)
			{
				return c;
			}
			float vb = d5 * d2 - d1 * d6;
			if (vb <= 0f && d2 >= 0f && d6 <= 0f)
			{
				return a + ac * (d2 / (d2 - d6));
			}
			float va = d3 * d6 - d5 * d4;
			if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
			{
				return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
			}
			float denominator = 1f / (va + vb + vc);
			return a + ab * (vb * denominator) + ac * (vc * denominator);
		}
	}
}
