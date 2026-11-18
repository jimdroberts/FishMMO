using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's baked prop collision: the <see cref="ScenePropCollisionSet"/>s its generator wrote, streamed into
	/// physics near characters and the camera by <see cref="PropColliderStreamer"/>. One per scene, at its root;
	/// kept by server and client alike.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ScenePropColliders : MonoBehaviour
	{
		/// <summary>The name of the object carrying this, at the scene's root.</summary>
		public const string ObjectName = "Prop Colliders";

		[Tooltip("The scene's baked collision sets, one per source.")]
		public List<ScenePropCollisionSet> Sets = new List<ScenePropCollisionSet>();

		/// <summary>Replaces the set from <paramref name="set"/>'s source (or adds it), dropping missing entries.</summary>
		public void Put(ScenePropCollisionSet set)
		{
			Sets.RemoveAll(s => s == null || (set != null && s.Source == set.Source));
			if (set != null)
			{
				Sets.Add(set);
			}
		}

		private void OnEnable() => PropColliderStreamer.Add(this);

		private void OnDisable() => PropColliderStreamer.Remove(this);
	}
}
