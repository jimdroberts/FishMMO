using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks a scene object that exists only to be looked at: it, its children and every asset
	/// only they reference are removed from server builds.
	/// </summary>
	/// <remarks>
	/// <para>
	/// World scenes ship in both players, because the scene server needs their terrain, colliders
	/// and boundaries. Anything placed in a scene therefore reaches the dedicated server unless
	/// something takes it out, and the server runs headless: a mesh or texture there is memory and
	/// load time spent on pixels nobody will draw. <c>ClientOnlySceneStripper</c> deletes every
	/// object carrying this while a server build processes its scenes, before the build collects
	/// what the scene depends on, so the assets never enter the server at all.
	/// </para>
	/// <para>
	/// Nothing marked with this may carry anything the server needs: no colliders players stand on,
	/// no boundaries, no gameplay. Under a server player it also destroys itself on load, as a
	/// backstop for a server built some other way.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class ClientOnlyObject : MonoBehaviour
	{
#if UNITY_SERVER
		protected virtual void Awake()
		{
			Destroy(gameObject);
		}
#endif
	}
}
