using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The look of a housing plot: its deed post, the staked rope round an empty lot, a builder's
	/// scaffold while it is a building site, a stone footing once it is a home, and a leaning post
	/// over bare stakes when it has been abandoned. Client only; built from the plot's own size.
	/// </summary>
	/// <remarks>
	/// <para>Nothing here collides. The plot volume is a box the server tests positions against
	/// (<see cref="PlotFoundation.Bounds"/>); a collider that size would stop anyone walking onto the
	/// lot and swallow every aim ray pointed past it. The only collider is the post's, on the
	/// foundation object itself, so the post can be targeted and interacted with.</para>
	///
	/// <para>Built procedurally rather than from a prefab per size because lots come in several
	/// sizes and the rope and footing follow the edge. One mesh per state, made on first use.</para>
	/// </remarks>
	[RequireComponent(typeof(PlotFoundation))]
	public class PlotFoundationVisual : MonoBehaviour
	{
		/// <summary>Post, stakes and scaffold timber.</summary>
		public Material Wood;

		/// <summary>The rope between the stakes.</summary>
		public Material Rope;

		/// <summary>The footing of a finished house.</summary>
		public Material Stone;

		/// <summary>The deed board on the post.</summary>
		public Material Board;

#if !UNITY_SERVER
		private PlotFoundation foundation;
		private MeshFilter filter;
		private MeshRenderer meshRenderer;
		private readonly Dictionary<PlotState, Mesh> meshes = new Dictionary<PlotState, Mesh>();

		private void Awake()
		{
			foundation = GetComponent<PlotFoundation>();
			Transform child = transform.Find("Plot Visual");
			if (child == null)
			{
				child = new GameObject("Plot Visual").transform;
				child.SetParent(transform, false);
			}
			child.localPosition = Vector3.zero;
			child.localRotation = Quaternion.identity;
			child.localScale = Vector3.one;
			filter = child.GetComponent<MeshFilter>();
			if (filter == null)
			{
				filter = child.gameObject.AddComponent<MeshFilter>();
			}
			meshRenderer = child.GetComponent<MeshRenderer>();
			if (meshRenderer == null)
			{
				meshRenderer = child.gameObject.AddComponent<MeshRenderer>();
			}
			meshRenderer.sharedMaterials = new[] { Wood, Rope, Stone, Board };
			foundation.OnStateChanged += Show;
			Show(foundation.State);
		}

		private void OnDestroy()
		{
			if (foundation != null)
			{
				foundation.OnStateChanged -= Show;
			}
			foreach (Mesh mesh in meshes.Values)
			{
				if (mesh != null)
				{
					Destroy(mesh);
				}
			}
			meshes.Clear();
		}

		private void Show(PlotState state)
		{
			if (filter == null)
			{
				return;
			}
			if (!meshes.TryGetValue(state, out Mesh mesh) || mesh == null)
			{
				mesh = PlotFoundationMesh.Build(state, foundation.Dimensions, foundation.CentreOffset, transform.rotation);
				meshes[state] = mesh;
			}
			filter.sharedMesh = mesh;
		}
#endif
	}

	/// <summary>
	/// Builds a plot's look as one mesh with four submeshes: wood, rope, stone, board.
	/// </summary>
	/// <remarks>
	/// Pure apart from the <see cref="Mesh"/> it fills, so the layout is the same on every client.
	/// Positions are local to the post; the lot's edges are found from the plot's world-axis centre
	/// offset turned into the post's frame, because the post is turned to face its street while the
	/// plot volume is not.
	/// </remarks>
	public static class PlotFoundationMesh
	{
		private const int WoodMat = 0, RopeMat = 1, StoneMat = 2, BoardMat = 3;

		/// <summary>The lot's corners inset this far, metres, so the stakes stand inside it.</summary>
		public const float StakeInset = 0.4f;

		public static Mesh Build(PlotState state, Vector2 size, Vector3 centreOffset, Quaternion postRotation)
		{
			var b = new Builder();
			Quaternion toLocal = Quaternion.Inverse(postRotation);
			// The lot's floor at the post's height: the cut levels the lot to it, and stakes on a dip read as sunk rather than floating.
			Vector3 centre = toLocal * new Vector3(centreOffset.x, 0f, centreOffset.z);
			float hx = Mathf.Max(0.5f, size.x * 0.5f - StakeInset), hz = Mathf.Max(0.5f, size.y * 0.5f - StakeInset);
			// The four corners of the lot in world axes, then into the post's frame.
			var corners = new Vector3[4];
			corners[0] = centre + toLocal * new Vector3(-hx, 0f, -hz);
			corners[1] = centre + toLocal * new Vector3(hx, 0f, -hz);
			corners[2] = centre + toLocal * new Vector3(hx, 0f, hz);
			corners[3] = centre + toLocal * new Vector3(-hx, 0f, hz);

			bool abandoned = state == PlotState.Abandoned;
			// The deed post: a squared timber with a cross-arm and a hanging board.
			float lean = abandoned ? 12f : 0f;
			Quaternion postTilt = Quaternion.Euler(lean, 0f, lean * 0.4f);
			b.Box(WoodMat, postTilt * new Vector3(0f, 1.15f, 0f), new Vector3(0.18f, 2.3f, 0.18f), postTilt);
			b.Box(WoodMat, postTilt * new Vector3(0.38f, 2.1f, 0f), new Vector3(0.9f, 0.12f, 0.12f), postTilt);
			if (!abandoned)
			{
				b.Box(BoardMat, postTilt * new Vector3(0.48f, 1.72f, 0f), new Vector3(0.7f, 0.46f, 0.05f), postTilt);
				b.Box(RopeMat, postTilt * new Vector3(0.24f, 1.99f, 0f), new Vector3(0.02f, 0.14f, 0.02f), postTilt);
				b.Box(RopeMat, postTilt * new Vector3(0.72f, 1.99f, 0f), new Vector3(0.02f, 0.14f, 0.02f), postTilt);
			}

			switch (state)
			{
				case PlotState.Empty:
					Stakes(b, corners, 0.9f);
					Ropes(b, corners, 0.72f);
					break;
				case PlotState.Building:
					Stakes(b, corners, 0.9f);
					Scaffold(b, corners, centre, toLocal, hx, hz);
					break;
				case PlotState.Occupied:
					Footing(b, corners);
					break;
				default:
					Stakes(b, corners, 0.6f);
					break;
			}
			return b.ToMesh(state.ToString());
		}

		private static void Stakes(Builder b, Vector3[] corners, float tall)
		{
			foreach (Vector3 c in corners)
			{
				b.Box(WoodMat, c + new Vector3(0f, tall * 0.5f, 0f), new Vector3(0.1f, tall, 0.1f), Quaternion.identity);
			}
		}

		private static void Ropes(Builder b, Vector3[] corners, float y)
		{
			for (int i = 0; i < 4; i++)
			{
				Vector3 a = corners[i], c = corners[(i + 1) % 4];
				b.Beam(RopeMat, a + Vector3.up * y, c + Vector3.up * y, 0.035f);
			}
		}

		private static void Scaffold(Builder b, Vector3[] corners, Vector3 centre, Quaternion toLocal, float hx, float hz)
		{
			// Poles stand a little in from the stakes, ledgers ring them at two lifts: a builder's frame.
			float px = Mathf.Max(0.5f, hx - 1.5f), pz = Mathf.Max(0.5f, hz - 1.5f);
			var poles = new Vector3[4];
			poles[0] = centre + toLocal * new Vector3(-px, 0f, -pz);
			poles[1] = centre + toLocal * new Vector3(px, 0f, -pz);
			poles[2] = centre + toLocal * new Vector3(px, 0f, pz);
			poles[3] = centre + toLocal * new Vector3(-px, 0f, pz);
			foreach (Vector3 p in poles)
			{
				b.Box(WoodMat, p + new Vector3(0f, 2.4f, 0f), new Vector3(0.14f, 4.8f, 0.14f), Quaternion.identity);
			}
			for (int i = 0; i < 4; i++)
			{
				Vector3 a = poles[i], c = poles[(i + 1) % 4];
				b.Beam(WoodMat, a + Vector3.up * 2.2f, c + Vector3.up * 2.2f, 0.1f);
				b.Beam(WoodMat, a + Vector3.up * 4.4f, c + Vector3.up * 4.4f, 0.1f);
			}
			// A stack of sawn timber by the post side.
			Vector3 pile = Vector3.Lerp(poles[0], poles[1], 0.5f) + (centre - Vector3.Lerp(poles[0], poles[1], 0.5f)).normalized * 2f;
			for (int i = 0; i < 3; i++)
			{
				b.Box(WoodMat, pile + new Vector3(0f, 0.1f + i * 0.18f, 0f), new Vector3(3f, 0.16f, 0.9f - i * 0.2f), Quaternion.identity);
			}
		}

		private static void Footing(Builder b, Vector3[] corners)
		{
			for (int i = 0; i < 4; i++)
			{
				Vector3 a = corners[i], c = corners[(i + 1) % 4];
				b.Beam(StoneMat, a + Vector3.up * 0.15f, c + Vector3.up * 0.15f, 0.5f, 0.3f);
			}
			foreach (Vector3 corner in corners)
			{
				b.Box(StoneMat, corner + new Vector3(0f, 0.25f, 0f), new Vector3(0.7f, 0.5f, 0.7f), Quaternion.identity);
			}
		}

		/// <summary>Accumulates flat-shaded boxes into per-material index lists.</summary>
		private sealed class Builder
		{
			private readonly List<Vector3> vertices = new List<Vector3>();
			private readonly List<Vector3> normals = new List<Vector3>();
			private readonly List<int>[] indices = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

			private static readonly Vector3[] Faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

			public void Box(int material, Vector3 centre, Vector3 size, Quaternion rotation)
			{
				Vector3 h = size * 0.5f;
				foreach (Vector3 n in Faces)
				{
					// Two axes spanning the face.
					Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
					Vector3 v = Vector3.Cross(n, u);
					Vector3 fc = Vector3.Scale(n, h);
					Vector3 fu = Vector3.Scale(u, h), fv = Vector3.Scale(v, h);
					int start = vertices.Count;
					vertices.Add(centre + rotation * (fc - fu - fv));
					vertices.Add(centre + rotation * (fc + fu - fv));
					vertices.Add(centre + rotation * (fc + fu + fv));
					vertices.Add(centre + rotation * (fc - fu + fv));
					Vector3 wn = rotation * n;
					for (int i = 0; i < 4; i++)
					{
						normals.Add(wn);
					}
					List<int> list = indices[material];
					// Wound so the face points along n (Unity: clockwise seen from outside).
					list.Add(start); list.Add(start + 1); list.Add(start + 2);
					list.Add(start); list.Add(start + 2); list.Add(start + 3);
				}
			}

			/// <summary>A square-section beam from a to b.</summary>
			public void Beam(int material, Vector3 a, Vector3 b, float thick, float tall = -1f)
			{
				Vector3 d = b - a;
				float length = d.magnitude;
				if (length < 0.01f)
				{
					return;
				}
				Quaternion rotation = Quaternion.LookRotation(d / length, Vector3.up);
				Box(material, (a + b) * 0.5f, new Vector3(thick, tall > 0f ? tall : thick, length), rotation);
			}

			public Mesh ToMesh(string name)
			{
				var mesh = new Mesh { name = "Plot " + name };
				mesh.SetVertices(vertices);
				mesh.SetNormals(normals);
				mesh.subMeshCount = indices.Length;
				for (int i = 0; i < indices.Length; i++)
				{
					mesh.SetTriangles(indices[i], i, false);
				}
				mesh.RecalculateBounds();
				return mesh;
			}
		}
	}
}
