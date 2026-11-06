using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's baked props: the <see cref="ScenePropSet"/>s its generator wrote (scatter, cliffs, boulders),
	/// which the client draws instanced. One per scene, on a client-only object at its root.
	/// </summary>
	/// <remarks>
	/// Under a <see cref="ClientOnlyObject"/>, so server builds drop it and the sets and prefabs it references.
	/// The props' collision is not here: each source bakes its own merged chunk colliders, which both the server
	/// and the client keep.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class SceneProps : MonoBehaviour
	{
		/// <summary>The name of the object carrying this, at the scene's root.</summary>
		public const string ObjectName = "Props";

		[Tooltip("The scene's baked prop sets, one per source.")]
		public List<ScenePropSet> Sets = new List<ScenePropSet>();

		/// <summary>
		/// Raised when a set's props were changed in place (a developer's hand edit, ScenePropEditTool): whatever draws or
		/// collides the sets reads them again. A rebake replaces the asset and needs no call.
		/// </summary>
		public static event System.Action SetsEdited;

		/// <summary>Tells the props' renderers a set changed in place (<see cref="SetsEdited"/>).</summary>
		public static void NotifySetsEdited() => SetsEdited?.Invoke();

		/// <summary>Replaces the set from <paramref name="set"/>'s source (or adds it), dropping missing entries.</summary>
		public void Put(ScenePropSet set)
		{
			Sets.RemoveAll(s => s == null || (set != null && s.Source == set.Source));
			if (set != null)
			{
				Sets.Add(set);
			}
		}
	}
}
