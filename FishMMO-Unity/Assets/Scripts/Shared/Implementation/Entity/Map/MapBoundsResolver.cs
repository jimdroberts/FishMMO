using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Works out the world-space rectangle a scene's map covers.
	/// </summary>
	/// <remarks>
	/// <para>Three sources, most authoritative first: bounds authored on the
	/// <see cref="WorldMapDefinition"/>, the scene boundaries already baked into
	/// <see cref="WorldSceneDetails"/>, and — only in the editor, only while the scene is open —
	/// the live <c>IBoundary</c> components and terrains.</para>
	///
	/// <para>The middle source is why a scene needs no map authoring at all to get a working
	/// world map: every world scene is already required to have an <c>IBoundary</c> (the cache
	/// rebuild refuses a scene without one), and those boundaries are already harvested into the
	/// cache that the client loads. Deriving from them means the map matches the area the player
	/// is actually allowed to be in, which is a better default than any number typed by hand.</para>
	/// </remarks>
	public static class MapBoundsResolver
	{
		/// <summary>
		/// Padding, as a fraction of the derived extent, added around bounds that were derived
		/// rather than authored.
		/// </summary>
		/// <remarks>
		/// A boundary is the edge of the playable area, and a map cropped exactly to it puts the
		/// player marker hard against the frame at the edge of the zone with nothing beyond it to
		/// give the position context. Authored bounds are left alone — somebody chose those.
		/// </remarks>
		private const float DerivedPadding = 0.04f;

		/// <summary>
		/// The rectangle the map covers, on the XZ plane.
		/// </summary>
		/// <param name="definition">The scene's map definition. May be null.</param>
		/// <param name="details">The scene's baked details, used when no bounds are authored. May be null.</param>
		/// <returns>The map rectangle, or <see cref="Rect.zero"/> when nothing could be derived.</returns>
		public static Rect Resolve(WorldMapDefinition definition, WorldSceneDetails details)
		{
			/* HasBounds, not HasAuthoredBounds. Bounds derived by the cache rebuild are written
			 * into the definition and are exactly as usable at runtime as typed ones; the
			 * authored/derived distinction only decides whether the next rebuild may overwrite
			 * them. Testing the wrong one here means every scene that has not had its bounds typed
			 * in by hand falls through to re-deriving them from the boundary dictionary on the
			 * client, which is the same answer arrived at more slowly. */
			if (definition != null && definition.HasBounds)
			{
				return definition.MapRect;
			}

			Rect fromBoundaries = FromSceneBoundaries(details);
			if (fromBoundaries.width > 0.0f && fromBoundaries.height > 0.0f)
			{
				return Pad(fromBoundaries, DerivedPadding);
			}

			return Rect.zero;
		}

		/// <summary>
		/// The union of a scene's baked boundaries, on the XZ plane.
		/// </summary>
		/// <param name="details">The scene's baked details. May be null.</param>
		/// <returns>The union rectangle, or <see cref="Rect.zero"/> when there are no boundaries.</returns>
		public static Rect FromSceneBoundaries(WorldSceneDetails details)
		{
			if (details == null || details.Boundaries == null || details.Boundaries.Count < 1)
			{
				return Rect.zero;
			}

			bool any = false;
			float minX = float.MaxValue;
			float minZ = float.MaxValue;
			float maxX = float.MinValue;
			float maxZ = float.MinValue;

			foreach (SceneBoundaryDetails boundary in details.Boundaries.Values)
			{
				if (boundary == null)
				{
					continue;
				}

				/* Absolute size. A boundary authored with a negative extent describes the same box
				 * as its positive twin, and Unity does not stop anyone entering one — taking the
				 * signed value straight through produces an inverted rect whose min exceeds its
				 * max, and every containment and normalisation downstream then quietly answers
				 * false for the whole scene. */
				float halfX = Mathf.Abs(boundary.BoundarySize.x) * 0.5f;
				float halfZ = Mathf.Abs(boundary.BoundarySize.z) * 0.5f;
				if (halfX <= 0.0f || halfZ <= 0.0f)
				{
					continue;
				}

				minX = Mathf.Min(minX, boundary.BoundaryOrigin.x - halfX);
				maxX = Mathf.Max(maxX, boundary.BoundaryOrigin.x + halfX);
				minZ = Mathf.Min(minZ, boundary.BoundaryOrigin.z - halfZ);
				maxZ = Mathf.Max(maxZ, boundary.BoundaryOrigin.z + halfZ);
				any = true;
			}

			return any ? new Rect(minX, minZ, maxX - minX, maxZ - minZ) : Rect.zero;
		}

		/// <summary>
		/// The union of every <c>IBoundary</c> and <c>Terrain</c> in the currently loaded scenes.
		/// </summary>
		/// <returns>The union rectangle, or <see cref="Rect.zero"/> when nothing was found.</returns>
		/// <remarks>
		/// Used by the map baker, which has the scene open and has not yet written a definition to
		/// derive from. Terrains are included as well as boundaries because a scene's terrain is
		/// what the bake camera will actually photograph — a boundary that crops tighter than the
		/// terrain would leave the map showing ground the rectangle claims is off the map.
		/// </remarks>
		public static Rect FromOpenScene()
		{
			bool any = false;
			float minX = float.MaxValue;
			float minZ = float.MaxValue;
			float maxX = float.MinValue;
			float maxZ = float.MinValue;

			IBoundary[] boundaries = Object.FindObjectsByType<IBoundary>(FindObjectsSortMode.None);
			for (int i = 0; i < boundaries.Length; ++i)
			{
				IBoundary boundary = boundaries[i];
				if (boundary == null)
				{
					continue;
				}

				Vector3 origin = boundary.GetBoundaryOffset();
				Vector3 size = boundary.GetBoundarySize();
				float halfX = Mathf.Abs(size.x) * 0.5f;
				float halfZ = Mathf.Abs(size.z) * 0.5f;
				if (halfX <= 0.0f || halfZ <= 0.0f)
				{
					continue;
				}

				minX = Mathf.Min(minX, origin.x - halfX);
				maxX = Mathf.Max(maxX, origin.x + halfX);
				minZ = Mathf.Min(minZ, origin.z - halfZ);
				maxZ = Mathf.Max(maxZ, origin.z + halfZ);
				any = true;
			}

			Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
			for (int i = 0; i < terrains.Length; ++i)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}

				Vector3 position = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;

				minX = Mathf.Min(minX, position.x);
				maxX = Mathf.Max(maxX, position.x + size.x);
				minZ = Mathf.Min(minZ, position.z);
				maxZ = Mathf.Max(maxZ, position.z + size.z);
				any = true;
			}

			return any ? Pad(new Rect(minX, minZ, maxX - minX, maxZ - minZ), DerivedPadding) : Rect.zero;
		}

		/// <summary>
		/// Grows a rectangle outwards by a fraction of its own size.
		/// </summary>
		/// <param name="rect">The rectangle to pad.</param>
		/// <param name="fraction">Fraction of each axis to add on each side.</param>
		/// <returns>The padded rectangle.</returns>
		public static Rect Pad(Rect rect, float fraction)
		{
			if (fraction <= 0.0f)
			{
				return rect;
			}

			float padX = rect.width * fraction;
			float padZ = rect.height * fraction;
			return new Rect(rect.xMin - padX, rect.yMin - padZ, rect.width + (padX * 2.0f), rect.height + (padZ * 2.0f));
		}

		/// <summary>
		/// Converts a world position into normalised map coordinates.
		/// </summary>
		/// <param name="rect">The map rectangle, on the XZ plane.</param>
		/// <param name="worldPosition">The world position to convert.</param>
		/// <returns>
		/// X and Y in 0..1 across the rectangle, with Y running from the rectangle's minimum Z at
		/// zero. Values outside 0..1 mean the position is off the map; they are not clamped,
		/// because the caller usually needs to know by how much in order to clamp a marker to the
		/// correct edge.
		/// </returns>
		public static Vector2 WorldToNormalized(Rect rect, Vector3 worldPosition)
		{
			if (rect.width <= 0.0f || rect.height <= 0.0f)
			{
				return new Vector2(0.5f, 0.5f);
			}

			return new Vector2((worldPosition.x - rect.xMin) / rect.width,
							   (worldPosition.z - rect.yMin) / rect.height);
		}

		/// <summary>
		/// Converts normalised map coordinates back into a world position on the XZ plane.
		/// </summary>
		/// <param name="rect">The map rectangle, on the XZ plane.</param>
		/// <param name="normalized">Normalised coordinates, X and Y in 0..1.</param>
		/// <returns>The world position, with Y left at zero.</returns>
		public static Vector3 NormalizedToWorld(Rect rect, Vector2 normalized)
		{
			return new Vector3(rect.xMin + (normalized.x * rect.width),
							   0.0f,
							   rect.yMin + (normalized.y * rect.height));
		}
	}
}
