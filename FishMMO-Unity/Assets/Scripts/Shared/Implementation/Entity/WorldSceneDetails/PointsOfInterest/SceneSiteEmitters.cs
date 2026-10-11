using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's baked emitters (<see cref="ScenePointsOfInterest.Emitters"/>: chimneys, fires, forges, lanterns), offered
	/// to the client's site lights and hearth smoke while the scene is loaded.
	/// </summary>
	/// <remarks>Client only: the object carries <see cref="ClientOnlyObject"/>, so server builds strip it.</remarks>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ClientOnlyObject))]
	public sealed class SceneSiteEmitters : MonoBehaviour
	{
		public const string ObjectName = "Site Emitters";

		[Tooltip("The scene's points of interest, whose Emitters this offers.")]
		public ScenePointsOfInterest Points;

		private static readonly List<SceneSiteEmitters> active = new List<SceneSiteEmitters>();

		/// <summary>The enabled scenes' emitter sets.</summary>
		public static IReadOnlyList<SceneSiteEmitters> Active => active;

		/// <summary>Bumped whenever a set comes or goes, so a reader can cache what it found.</summary>
		public static int Version { get; private set; }

		/// <summary>This scene's emitters (never null).</summary>
		public IReadOnlyList<SceneEmitter> Emitters => Points != null && Points.Emitters != null ? Points.Emitters : (IReadOnlyList<SceneEmitter>)System.Array.Empty<SceneEmitter>();

		private void OnEnable()
		{
			if (!active.Contains(this))
			{
				active.Add(this);
				Version++;
			}
		}

		private void OnDisable()
		{
			if (active.Remove(this))
			{
				Version++;
			}
		}

		/// <summary>
		/// The point of interest whose ground covers a place (within its radius and <paramref name="margin"/> more,
		/// horizontally), the nearest centre where several do; false outside every one. Sites on the sea or lake floor are
		/// left out: their ground is under the water.
		/// </summary>
		public static bool SiteAt(Vector3 at, float margin, out PointOfInterestKindInfo site)
		{
			site = default;
			float best = float.MaxValue;
			bool found = false;
			foreach (SceneSiteEmitters set in active)
			{
				if (set == null || set.Points == null || set.Points.Points == null)
				{
					continue;
				}
				foreach (PointOfInterestRecord point in set.Points.Points)
				{
					float dx = point.Position.x - at.x, dz = point.Position.z - at.z;
					float d2 = dx * dx + dz * dz;
					float reach = point.Radius + margin;
					if (d2 > reach * reach || d2 >= best)
					{
						continue;
					}
					PointOfInterestKindInfo info = point.Info;
					if (info.Has(PointOfInterestTraits.Underwater))
					{
						continue;
					}
					best = d2;
					site = info;
					found = true;
				}
			}
			return found;
		}

		/// <summary>Every enabled scene's emitters of some kinds within a distance of a point, nearest first, at most <paramref name="max"/>.</summary>
		public static void Near(Vector3 at, float reach, System.Func<SceneEmitterKind, bool> kinds, int max, List<SceneEmitter> into)
		{
			into.Clear();
			float r2 = reach * reach;
			foreach (SceneSiteEmitters set in active)
			{
				if (set == null)
				{
					continue;
				}
				foreach (SceneEmitter e in set.Emitters)
				{
					if (kinds != null && !kinds(e.Kind))
					{
						continue;
					}
					Vector3 d = e.Position - at;
					if (d.sqrMagnitude <= r2)
					{
						into.Add(e);
					}
				}
			}
			into.Sort((a, b) => (a.Position - at).sqrMagnitude.CompareTo((b.Position - at).sqrMagnitude));
			if (into.Count > max)
			{
				into.RemoveRange(max, into.Count - max);
			}
		}
	}
}
