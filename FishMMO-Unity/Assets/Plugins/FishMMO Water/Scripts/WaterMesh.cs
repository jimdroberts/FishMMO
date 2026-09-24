using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// Builds the disc of geometry the ocean is drawn on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Radial, camera-centred, and spaced so a ring is about as wide on screen wherever it is.</b>
	/// A flat grid big enough to reach the horizon is either millions of vertices or a handful of
	/// enormous triangles with no waves on them. Rings whose radius grows geometrically put the
	/// vertices where they are visible: metre-scale near the viewer, hundreds of metres at the
	/// horizon, with a roughly constant cost per pixel.
	/// </para>
	/// <para>
	/// This replaces a vertex shader that took each vertex of a 100 m Unity plane, normalised the
	/// vector from the camera to it, pushed that to the far clip plane and then put the original Y
	/// back. That is not a projection of anything: the Y snap changes the vector's length after it
	/// was normalised, so each vertex lands at its own arbitrary XZ scale and the plane folds
	/// through itself. It also re-derived every vertex from the camera every frame, in the shadow
	/// pass too, with a different camera.
	/// </para>
	/// </remarks>
	public static class WaterMesh
	{
		/// <summary>
		/// Builds the ocean disc, centred on the origin and lying in the XZ plane.
		/// </summary>
		/// <param name="innerRadius">Radius of the solid centre, in metres. A few metres is plenty.</param>
		/// <param name="outerRadius">How far the sea reaches, in metres. Past the far clip plane is wasted.</param>
		/// <param name="rings">Rings of vertices. More is smoother wave motion at middle distance.</param>
		/// <param name="segments">Vertices around each ring. More is a rounder horizon.</param>
		/// <param name="waveHeadroom">The tallest wave, in metres, so the bounds still contain the sea.</param>
		public static Mesh Build(float innerRadius, float outerRadius, int rings, int segments, float waveHeadroom)
		{
			innerRadius = Mathf.Max(0.5f, innerRadius);
			outerRadius = Mathf.Max(innerRadius * 2f, outerRadius);
			rings = Mathf.Clamp(rings, 2, 512);
			segments = Mathf.Clamp(segments, 3, 1024);

			int vertexCount = 1 + rings * segments;
			var vertices = new Vector3[vertexCount];
			// Centre, then ring by ring outward.
			vertices[0] = Vector3.zero;

			float growth = Mathf.Pow(outerRadius / innerRadius, 1f / (rings - 1));
			float radius = innerRadius;
			for (int ring = 0; ring < rings; ring++)
			{
				for (int segment = 0; segment < segments; segment++)
				{
					float angle = segment * Mathf.PI * 2f / segments;
					vertices[1 + ring * segments + segment] =
						new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
				}
				radius *= growth;
			}

			int triangleCount = segments + (rings - 1) * segments * 2;
			var triangles = new int[triangleCount * 3];
			int t = 0;

			// The fan filling the middle.
			for (int segment = 0; segment < segments; segment++)
			{
				triangles[t++] = 0;
				triangles[t++] = 1 + segment;
				triangles[t++] = 1 + (segment + 1) % segments;
			}

			// A quad between each pair of rings.
			for (int ring = 0; ring < rings - 1; ring++)
			{
				int inner = 1 + ring * segments;
				int outer = 1 + (ring + 1) * segments;
				for (int segment = 0; segment < segments; segment++)
				{
					int next = (segment + 1) % segments;
					triangles[t++] = inner + segment;
					triangles[t++] = outer + segment;
					triangles[t++] = outer + next;

					triangles[t++] = inner + segment;
					triangles[t++] = outer + next;
					triangles[t++] = inner + next;
				}
			}

			var mesh = new Mesh
			{
				name = "FishMMO Ocean",
				// A disc this size passes 65,535 vertices at any useful density.
				indexFormat = vertexCount > 65000
					? UnityEngine.Rendering.IndexFormat.UInt32
					: UnityEngine.Rendering.IndexFormat.UInt16,
				hideFlags = HideFlags.HideAndDontSave,
			};
			mesh.vertices = vertices;
			mesh.triangles = triangles;

			/* Set by hand, never recalculated.
			 *
			 * The vertices all lie flat; the waves happen in the vertex shader, which Unity's
			 * culling knows nothing about. Bounds recalculated from the mesh would be a
			 * zero-height slab, and the ocean would vanish the moment the camera looked along it
			 * from just above the water — the case where it fills the screen. */
			mesh.bounds = new Bounds(Vector3.zero,
				new Vector3(outerRadius * 2f, Mathf.Max(2f, waveHeadroom * 4f), outerRadius * 2f));

			// Normals are computed in the shader from the wave derivatives, and there is no UV:
			// everything is addressed in world XZ so the mesh may be any size without stretching.
			mesh.normals = null;
			return mesh;
		}
	}
}
