using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Installs the browser-side pointer-lock guard (PointerLockGuard.jslib) before the first scene loads.
	/// </summary>
	/// <remarks>
	/// The game locks the cursor on its own (<see cref="PlayerInputController.MouseMode"/>), not only from a click,
	/// and a browser refuses a pointer lock that is not requested inside a user gesture. Unity's runtime ignored the
	/// refusal: the console showed "Uncaught (in promise) NotAllowedError: A user gesture is required to request
	/// Pointer Lock" and the cursor stayed free. The guard handles the refusal and takes the lock on the player's next
	/// mouse press, unless the game has freed the cursor by then. Nothing outside a WebGL player.
	/// </remarks>
	public static class WebGLPointerLockGuard
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern void FishMMOInstallPointerLockGuard();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Install()
		{
			FishMMOInstallPointerLockGuard();
		}
#endif
	}
}
