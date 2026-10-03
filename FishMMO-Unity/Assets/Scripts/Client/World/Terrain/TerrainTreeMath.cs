using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The arithmetic of the instanced terrain tree renderer, free of scene objects so it can be tested:
	/// where an instance stands, which level of detail a camera sees it at, which chunk it is bucketed in,
	/// whether a chunk can be seen (or can throw a shadow that can), and how a draw splits into batches.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Placement is Unity's.</b> A terrain tree stands at
	/// <c>terrain.GetPosition() + Vector3.Scale(instance.position, terrainData.size)</c>, turned about
	/// world up by <c>instance.rotation</c> radians and scaled (<c>widthScale</c>, <c>heightScale</c>,
	/// <c>widthScale</c>). Height included: the scatter writes <c>position.y</c> with snapping off and the
	/// TerrainCollider builds each tree's collider from the same numbers, so what is drawn here is exactly
	/// where the server's colliders are.
	/// </para>
	/// <para>
	/// <b>Levels of detail are Unity's LODGroup rule.</b> The screen-relative height of an object of
	/// world size <c>s</c> at distance <c>d</c> under a perspective camera of vertical field of view
	/// <c>fov</c> is <c>s / (2·d·tan(fov/2))</c> (orthographic: <c>s / (2·orthographicSize)</c>), times
	/// the LOD bias; the first level whose transition height it reaches is drawn, none when it is below the
	/// last. The size is the group's size times the instance's larger scale, measured from the group's
	/// reference point.
	/// </para>
	/// </remarks>
	public static class TerrainTreeMath
	{
		/// <summary>The most instances one instanced draw takes (the classic constant-buffer limit).</summary>
		public const int MaxInstancesPerBatch = 1023;

		/// <summary>The side of a bucketing chunk, metres.</summary>
		public const float DefaultChunkMetres = 64f;

		// ── Placement ─────────────────────────────────────────────────

		/// <summary>An instance's world position: the terrain's corner plus its normalised position scaled to the terrain's size.</summary>
		public static Vector3 WorldPosition(Vector3 terrainPosition, Vector3 terrainSize, Vector3 normalizedPosition)
		{
			return terrainPosition + Vector3.Scale(normalizedPosition, terrainSize);
		}

		/// <summary>An instance's rotation: <paramref name="radians"/> about world up.</summary>
		public static Quaternion Rotation(float radians)
		{
			return Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.up);
		}

		/// <summary>An instance's scale: width on x and z, height on y.</summary>
		public static Vector3 Scale(float widthScale, float heightScale)
		{
			return new Vector3(widthScale, heightScale, widthScale);
		}

		/// <summary>The object-to-world matrix of a terrain tree instance (the prototype's root).</summary>
		public static Matrix4x4 InstanceMatrix(Vector3 terrainPosition, Vector3 terrainSize, Vector3 normalizedPosition, float rotation, float widthScale, float heightScale)
		{
			return Matrix4x4.TRS(WorldPosition(terrainPosition, terrainSize, normalizedPosition), Rotation(rotation), Scale(widthScale, heightScale));
		}

		/// <summary>The size a LODGroup measures an instance by: the group's size times the larger of its scales.</summary>
		public static float WorldSize(float groupSize, float widthScale, float heightScale)
		{
			return groupSize * Mathf.Max(Mathf.Abs(widthScale), Mathf.Abs(heightScale));
		}

		/// <summary>The world axis-aligned box around a local box under a matrix.</summary>
		public static Bounds TransformBounds(Matrix4x4 m, Bounds local)
		{
			Vector3 centre = m.MultiplyPoint3x4(local.center);
			Vector3 e = local.extents;
			Vector3 extents = new Vector3(
				Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
				Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
				Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
			return new Bounds(centre, extents * 2f);
		}

		// ── Levels of detail ──────────────────────────────────────────

		/// <summary>
		/// What a world size and distance are multiplied by to give the biased screen-relative height:
		/// <c>lodBias / (2·tan(fov/2))</c> for a perspective camera; for an orthographic one the height does
		/// not depend on distance, <c>lodBias / (2·orthographicSize)</c>, and <paramref name="orthographic"/>
		/// tells the caller not to divide by distance.
		/// </summary>
		public static float ScreenFactor(float verticalFovDegrees, bool orthographic, float orthographicSize, float lodBias)
		{
			if (orthographic)
			{
				return lodBias / Mathf.Max(1e-5f, 2f * orthographicSize);
			}
			float tan = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(verticalFovDegrees, 1e-3f, 179f) * 0.5f);
			return lodBias / Mathf.Max(1e-6f, 2f * tan);
		}

		/// <summary>The biased screen-relative height of a world size at a distance (see <see cref="ScreenFactor"/>).</summary>
		public static float RelativeHeight(float worldSize, float distance, float screenFactor, bool orthographic)
		{
			return orthographic ? worldSize * screenFactor : worldSize * screenFactor / Mathf.Max(1e-4f, distance);
		}

		/// <summary>
		/// The level drawn at a biased screen-relative height: the first whose transition height it reaches,
		/// never finer than <paramref name="maximumLodLevel"/> (QualitySettings.maximumLODLevel); −1 when it is
		/// below the last (culled). Transition heights descend, as a LODGroup's do.
		/// </summary>
		public static int SelectLod(float relativeHeight, float[] transitionHeights, int maximumLodLevel)
		{
			int count = transitionHeights != null ? transitionHeights.Length : 0;
			for (int i = 0; i < count; i++)
			{
				if (relativeHeight >= transitionHeights[i])
				{
					return Mathf.Min(Mathf.Max(i, maximumLodLevel), count - 1);
				}
			}
			return -1;
		}

		// ── Cross-fade bands ──────────────────────────────────────────

		/// <summary>
		/// The default cross-fade band: this share of a transition height, above it. 0.1 puts a tree's
		/// LOD0→LOD1 fade (0.25) over 0.25–0.275, ~26.7–29.4 m for an 8.5 m tree at 60°, and the band before
		/// a level is culled likewise over the last tenth of its range in distance terms (≈ 9%).
		/// </summary>
		public const float DefaultFadeBandShare = 0.1f;

		/// <summary>
		/// The cross-fade band of level <paramref name="level"/>'s lower transition, in screen-relative
		/// height: Unity's <see cref="LOD.fadeTransitionWidth"/> (a share of the level's own range, up to the
		/// level above, or 1 for LOD0) when the prefab sets one, else <paramref name="share"/> of the
		/// transition height. Never wider than the level's range, so a band never reaches into the level above.
		/// </summary>
		public static float FadeWidth(float[] transitions, int level, float lodFadeTransitionWidth, float share = DefaultFadeBandShare)
		{
			float t = transitions[level];
			float upper = level == 0 ? Mathf.Max(1f, t) : transitions[level - 1];
			float range = Mathf.Max(0f, upper - t);
			float width = lodFadeTransitionWidth > 0f ? lodFadeTransitionWidth * range : share * t;
			return Mathf.Clamp(width, 0f, range);
		}

		/// <summary>
		/// The level(s) drawn at a biased screen-relative height, with the per-instance cross-fade
		/// (<c>_FishLodFade</c>, FishLodFade.hlsl). Outside every band: one level, fade 0 (fully drawn), no
		/// partner. Inside the band <c>[T, T + W)</c> above level L's lower transition T: L with −f and
		/// L + 1 with +f (the same magnitude, negated — they partition the dither pattern), where
		/// <c>f = 1 − (rh − T) / W</c> is the incoming share, in (0, 1). For the last level the partner is
		/// "culled": the level fades out alone (−f) and there is no partner draw. Exactly at T the next level
		/// is drawn whole (or nothing, after the last). A fade of 0 is never returned for a level that should
		/// be hidden: that level is not returned at all. Returns the primary level, −1 when culled.
		/// </summary>
		public static int SelectLodFaded(float relativeHeight, float[] transitions, float[] widths, int maximumLodLevel,
			out float fade, out int partner, out float partnerFade)
		{
			fade = 0f;
			partner = -1;
			partnerFade = 0f;
			int lod = SelectLod(relativeHeight, transitions, maximumLodLevel);
			if (lod < 0)
			{
				return -1;
			}
			float t = transitions[lod];
			float w = widths != null && lod < widths.Length ? widths[lod] : 0f;
			if (w <= 0f || relativeHeight >= t + w)
			{
				return lod;
			}
			int next = lod + 1 < transitions.Length ? lod + 1 : -1;
			// From the transition up, so exactly at it f is exactly 1 (t + w − rh would round either side of it).
			float f = 1f - (relativeHeight - t) / w;
			if (f >= 1f)
			{
				return next;
			}
			if (!(f > 0f))
			{
				return lod;
			}
			fade = -f;
			if (next >= 0)
			{
				partner = next;
				partnerFade = f;
			}
			return lod;
		}

		// ── Chunks ────────────────────────────────────────────────────

		/// <summary>How many chunks a terrain side is cut into: at least one, each no longer than <paramref name="chunkMetres"/>.</summary>
		public static int ChunkCount(float sideMetres, float chunkMetres)
		{
			if (sideMetres <= 0f || chunkMetres <= 0f)
			{
				return 1;
			}
			return Mathf.Max(1, Mathf.CeilToInt(sideMetres / chunkMetres - 1e-4f));
		}

		/// <summary>The chunk a normalised position falls in, row-major (z rows of <paramref name="chunksX"/>); the far edge belongs to the last chunk.</summary>
		public static int ChunkIndex(float normalizedX, float normalizedZ, int chunksX, int chunksZ)
		{
			int x = Mathf.Clamp(Mathf.FloorToInt(normalizedX * chunksX), 0, chunksX - 1);
			int z = Mathf.Clamp(Mathf.FloorToInt(normalizedZ * chunksZ), 0, chunksZ - 1);
			return z * chunksX + x;
		}

		/// <summary>
		/// Orders instances by chunk, then by prototype, then by their original index (a stable counting
		/// sort): <paramref name="order"/>[k] is the instance drawn k-th. Returns, per chunk, where its
		/// instances start in that order (<paramref name="chunkStart"/>, length chunks + 1).
		/// </summary>
		public static void SortByChunkThenPrototype(int[] chunkOf, int[] prototypeOf, int chunkCount, int prototypeCount, int[] order, int[] chunkStart)
		{
			int n = chunkOf.Length;
			int keys = chunkCount * Mathf.Max(1, prototypeCount);
			var start = new int[keys + 1];
			for (int i = 0; i < n; i++)
			{
				start[chunkOf[i] * prototypeCount + prototypeOf[i] + 1]++;
			}
			for (int k = 0; k < keys; k++)
			{
				start[k + 1] += start[k];
			}
			for (int c = 0; c <= chunkCount; c++)
			{
				chunkStart[c] = start[Mathf.Min(c * prototypeCount, keys)];
			}
			var next = (int[])start.Clone();
			for (int i = 0; i < n; i++)
			{
				order[next[chunkOf[i] * prototypeCount + prototypeOf[i]]++] = i;
			}
		}

		// ── Culling ───────────────────────────────────────────────────

		/// <summary>True when a box is at least partly inside every plane (the usual positive-vertex test; conservative).</summary>
		public static bool IntersectsFrustum(Plane[] planes, Bounds bounds)
		{
			Vector3 c = bounds.center, e = bounds.extents;
			for (int i = 0; i < planes.Length; i++)
			{
				Vector3 n = planes[i].normal;
				float reach = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
				if (Vector3.Dot(n, c) + planes[i].distance + reach < 0f)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The nearest distance from a point to a box (0 inside).</summary>
		public static float MinDistance(Bounds bounds, Vector3 point)
		{
			return Mathf.Sqrt(bounds.SqrDistance(point));
		}

		/// <summary>The farthest distance from a point to a box: to its farthest corner.</summary>
		public static float MaxDistance(Bounds bounds, Vector3 point)
		{
			Vector3 min = bounds.min, max = bounds.max;
			float dx = Mathf.Max(Mathf.Abs(point.x - min.x), Mathf.Abs(point.x - max.x));
			float dy = Mathf.Max(Mathf.Abs(point.y - min.y), Mathf.Abs(point.y - max.y));
			float dz = Mathf.Max(Mathf.Abs(point.z - min.z), Mathf.Abs(point.z - max.z));
			return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
		}

		/// <summary>
		/// A box swept along the light's direction of travel far enough to cover the shadow its contents
		/// throw on ground at its own base: its height over the light's elevation, capped at
		/// <paramref name="maxLength"/>. A chunk off screen whose swept box is on screen can shadow what is seen.
		/// </summary>
		public static Bounds ShadowSweep(Bounds bounds, Vector3 lightDirection, float maxLength)
		{
			Vector3 d = lightDirection.sqrMagnitude > 1e-8f ? lightDirection.normalized : Vector3.down;
			float down = Mathf.Max(-d.y, 0.1f);
			float length = Mathf.Min(maxLength, bounds.size.y / down);
			Bounds swept = bounds;
			swept.Encapsulate(bounds.min + d * length);
			swept.Encapsulate(bounds.max + d * length);
			return swept;
		}

		// ── Batches ───────────────────────────────────────────────────

		/// <summary>How many instanced draws <paramref name="count"/> instances take at <paramref name="perBatch"/> a draw.</summary>
		public static int BatchCount(int count, int perBatch = MaxInstancesPerBatch)
		{
			return count <= 0 ? 0 : (count + perBatch - 1) / perBatch;
		}

		/// <summary>The first instance and the size of batch <paramref name="batch"/> of <paramref name="count"/>.</summary>
		public static void Batch(int count, int batch, out int start, out int size, int perBatch = MaxInstancesPerBatch)
		{
			start = batch * perBatch;
			size = Mathf.Clamp(count - start, 0, perBatch);
		}
	}
}
