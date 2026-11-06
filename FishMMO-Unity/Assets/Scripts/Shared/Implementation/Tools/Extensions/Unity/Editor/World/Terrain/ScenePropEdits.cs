using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A scene's hand edits to its generated props (structures, cliffs, boulders, trees): a prop moved, turned, scaled or
	/// removed by a developer, kept beside the scene's terrain ("&lt;Scene&gt; Prop Edits.asset", text, so it diffs) and laid
	/// over every bake again. Generated props are data, not objects (<see cref="ScenePropSet"/>), and every POI regenerate,
	/// repaint or cut writes their sets anew; an edit names the prop it changes by what the generator gave it (its source,
	/// its prefab and where it stood), so the same generation finds it again.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Applied where each prop is made: a POI piece in <see cref="PointOfInterestSiteContext.AddProp"/> (while
	/// <see cref="Active"/> is the scene's), so its doors, rooms, the ground dug under it and what it gives off follow it; every
	/// other source in <see cref="ScenePropBaker.Write"/>. Edited in the Scene view with <see cref="ScenePropEditTool"/>.
	/// </para>
	/// <para>
	/// An edit no bake matched (its prop no longer generated where it was: the template or the site moved) is kept and
	/// shown as unmatched in its inspector, never silently dropped.
	/// </para>
	/// </remarks>
	public sealed class ScenePropEdits : ScriptableObject
	{
		/// <summary>How far a generated prop may stand from an edit's origin and still be the prop it names, metres.</summary>
		public const float MatchMetres = 0.05f;

		/// <summary>One prop's edit.</summary>
		[Serializable]
		public struct Edit
		{
			/// <summary>The set it comes from (ScenePropSet.Source: POI, Cliffs, Boulders, Scatter).</summary>
			public string Source;
			/// <summary>Its prefab, and that prefab's asset GUID (what matching compares: a renamed prefab is the same prefab).</summary>
			public GameObject Prefab;
			public string PrefabGuid;
			/// <summary>Where the generator put it.</summary>
			public Vector3 Origin;
			public Quaternion OriginRotation;
			public Vector3 OriginScale;
			/// <summary>Taken out: the generator's prop is not laid at all.</summary>
			public bool Removed;
			/// <summary>Where the developer put it (when not removed).</summary>
			public Vector3 Position;
			public Quaternion Rotation;
			public Vector3 Scale;
			/// <summary>Whether the last bake of its source found its prop (false: unmatched, see the inspector).</summary>
			public bool Matched;
		}

		public List<Edit> Edits = new List<Edit>();

		/// <summary>The edits of the scene a POI generation is laying (set by PointOfInterestStage.Place for its length), else null.</summary>
		public static ScenePropEdits Active;

		/// <summary>The asset's path for a scene (beside its terrain), or null for a scene with no folder yet.</summary>
		public static string PathFor(Scene scene) => ScenePropBaker.AssetPath(scene, "Prop Edits");

		/// <summary>The scene's edits, made (as an asset beside its terrain) when <paramref name="create"/> and it has none.</summary>
		public static ScenePropEdits For(Scene scene, bool create)
		{
			string path = PathFor(scene);
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			var edits = AssetDatabase.LoadAssetAtPath<ScenePropEdits>(path);
			if (edits == null && create)
			{
				edits = CreateInstance<ScenePropEdits>();
				edits.name = $"{scene.name} Prop Edits";
				AssetDatabase.CreateAsset(edits, path);
			}
			return edits;
		}

		/// <summary>A prefab's asset GUID ("" for none or a scene object).</summary>
		public static string GuidOf(GameObject prefab)
		{
			if (prefab == null)
			{
				return string.Empty;
			}
			string path = AssetDatabase.GetAssetPath(prefab);
			return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
		}

		/// <summary>
		/// Lays this scene's edit (if any) over a generated prop: true with its placement changed (or as it was), false
		/// when the edit removes it. Marks the edit matched.
		/// </summary>
		public bool Apply(string source, GameObject prefab, ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
		{
			if (Edits.Count == 0)
			{
				return true;
			}
			int at = ScenePropEditMath.FindByOrigin(Edits, source, GuidOf(prefab), position);
			if (at < 0)
			{
				return true;
			}
			Edit e = Edits[at];
			e.Matched = true;
			Edits[at] = e;
			if (e.Removed)
			{
				return false;
			}
			position = e.Position;
			rotation = e.Rotation;
			scale = e.Scale;
			return true;
		}

		/// <summary>
		/// Lays the edits of <paramref name="source"/> over a set about to be written: props moved or dropped in place.
		/// Every edit of the source is first marked unmatched, then matched as its prop is found. Returns the edits applied.
		/// </summary>
		public int ApplyTo(string source, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props)
		{
			if (props == null || Edits.Count == 0)
			{
				return 0;
			}
			BeginBake(source);
			var guids = new string[prototypes != null ? prototypes.Count : 0];
			for (int p = 0; p < guids.Length; p++)
			{
				guids[p] = GuidOf(prototypes[p].Prefab);
			}
			int applied = 0;
			for (int i = props.Count - 1; i >= 0; i--)
			{
				ScenePropSet.Prop prop = props[i];
				if (prop.Prototype < 0 || prop.Prototype >= guids.Length)
				{
					continue;
				}
				int at = ScenePropEditMath.FindByOrigin(Edits, source, guids[prop.Prototype], prop.Position);
				if (at < 0)
				{
					continue;
				}
				Edit e = Edits[at];
				e.Matched = true;
				Edits[at] = e;
				applied++;
				if (e.Removed)
				{
					props.RemoveAt(i);
					continue;
				}
				prop.Position = e.Position;
				prop.Rotation = e.Rotation;
				prop.Scale = e.Scale;
				props[i] = prop;
			}
			EditorUtility.SetDirty(this);
			return applied;
		}

		/// <summary>Marks every edit of <paramref name="source"/> unmatched before a bake lays it again.</summary>
		public void BeginBake(string source)
		{
			for (int i = 0; i < Edits.Count; i++)
			{
				if (Edits[i].Source == source)
				{
					Edit e = Edits[i];
					e.Matched = false;
					Edits[i] = e;
				}
			}
		}

		/// <summary>The edits of a source no bake has matched since it was last laid.</summary>
		public int Unmatched(string source)
		{
			int n = 0;
			foreach (Edit e in Edits)
			{
				n += e.Source == source && !e.Matched ? 1 : 0;
			}
			return n;
		}
	}

	/// <summary>The pure parts of <see cref="ScenePropEdits"/>: which edit names a prop.</summary>
	public static class ScenePropEditMath
	{
		/// <summary>The edit of the prop the generator put at <paramref name="origin"/> (its source and prefab), or −1.</summary>
		public static int FindByOrigin(IReadOnlyList<ScenePropEdits.Edit> edits, string source, string prefabGuid, Vector3 origin) =>
			Find(edits, source, prefabGuid, origin, true);

		/// <summary>The edit that put a prop where it stands now (<paramref name="position"/>), or −1: a prop already edited.</summary>
		public static int FindByPlacement(IReadOnlyList<ScenePropEdits.Edit> edits, string source, string prefabGuid, Vector3 position) =>
			Find(edits, source, prefabGuid, position, false);

		private static int Find(IReadOnlyList<ScenePropEdits.Edit> edits, string source, string prefabGuid, Vector3 at, bool byOrigin)
		{
			int best = -1;
			float bestDistance = ScenePropEdits.MatchMetres * ScenePropEdits.MatchMetres;
			for (int i = 0; i < edits.Count; i++)
			{
				ScenePropEdits.Edit e = edits[i];
				if (e.Source != source || e.PrefabGuid != prefabGuid || (!byOrigin && e.Removed))
				{
					continue;
				}
				float d = ((byOrigin ? e.Origin : e.Position) - at).sqrMagnitude;
				if (d <= bestDistance)
				{
					bestDistance = d;
					best = i;
				}
			}
			return best;
		}

		/// <summary>
		/// The distance along a ray to where it enters a box given in a prop's own space, or +∞ when it misses: the ray
		/// taken into that space by <paramref name="worldToLocal"/>, the box's slabs met, the hit taken back to the world.
		/// </summary>
		public static float RayBoxDistance(Vector3 origin, Vector3 direction, Matrix4x4 worldToLocal, Matrix4x4 localToWorld, Bounds local)
		{
			Vector3 o = worldToLocal.MultiplyPoint3x4(origin);
			Vector3 d = worldToLocal.MultiplyVector(direction);
			float tMin = 0f, tMax = float.PositiveInfinity;
			Vector3 min = local.min, max = local.max;
			for (int axis = 0; axis < 3; axis++)
			{
				float oa = o[axis], da = d[axis];
				if (Mathf.Abs(da) < 1e-9f)
				{
					if (oa < min[axis] || oa > max[axis])
					{
						return float.PositiveInfinity;
					}
					continue;
				}
				float t1 = (min[axis] - oa) / da, t2 = (max[axis] - oa) / da;
				if (t1 > t2)
				{
					(t1, t2) = (t2, t1);
				}
				tMin = Mathf.Max(tMin, t1);
				tMax = Mathf.Min(tMax, t2);
				if (tMin > tMax)
				{
					return float.PositiveInfinity;
				}
			}
			return Vector3.Distance(origin, localToWorld.MultiplyPoint3x4(o + d * tMin));
		}

		/// <summary>The distance along a ray to a triangle (Möller–Trumbore, both faces), or +∞.</summary>
		public static float RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 e1 = b - a, e2 = c - a;
			Vector3 p = Vector3.Cross(direction, e2);
			float det = Vector3.Dot(e1, p);
			if (Mathf.Abs(det) < 1e-12f)
			{
				return float.PositiveInfinity;
			}
			float inv = 1f / det;
			Vector3 s = origin - a;
			float u = Vector3.Dot(s, p) * inv;
			if (u < 0f || u > 1f)
			{
				return float.PositiveInfinity;
			}
			Vector3 q = Vector3.Cross(s, e1);
			float v = Vector3.Dot(direction, q) * inv;
			if (v < 0f || u + v > 1f)
			{
				return float.PositiveInfinity;
			}
			float t = Vector3.Dot(e2, q) * inv;
			return t >= 0f ? t : float.PositiveInfinity;
		}
	}
}
