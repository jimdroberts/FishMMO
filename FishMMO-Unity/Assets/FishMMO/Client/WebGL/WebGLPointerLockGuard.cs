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
	/// <para>
	/// The guard also reports a lock the <b>browser</b> ended — Escape, which no page can cancel, or a switch of tab
	/// or window. Unity's runtime says nothing about it, so the game kept believing the cursor was captured while
	/// the player was looking at a free one. <see cref="ConsumeBrowserRelease"/> hands that to the world's input
	/// controller, which frees the cursor to match.
	/// </para>
	/// </remarks>
	public static class WebGLPointerLockGuard
	{
		/// <summary>Set from the browser event, cleared when the input controller acts on it.</summary>
		private static bool browserReleased;

		/// <summary>
		/// True once after the browser ended a pointer lock the game still wanted; clears itself.
		/// </summary>
		/// <remarks>
		/// Polled from the player loop rather than acted on in the callback: the callback runs from a browser event,
		/// outside Unity's frame, and freeing the cursor raises <see cref="PlayerInputController.OnToggleMouseMode"/>,
		/// whose UI subscribers belong inside it. Always false outside a WebGL player.
		/// </remarks>
		public static bool ConsumeBrowserRelease()
		{
			if (!browserReleased)
			{
				return false;
			}
			browserReleased = false;
			return true;
		}

#if UNITY_WEBGL && !UNITY_EDITOR
		private delegate void LockLostCallback();

		[DllImport("__Internal")]
		private static extern void FishMMOInstallPointerLockGuard(LockLostCallback lostCallback);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Install()
		{
			FishMMOInstallPointerLockGuard(OnLockLostNative);
		}

		/// <summary>
		/// Entry point the browser calls. Static because IL2CPP cannot hand an instance method or a lambda to
		/// native code.
		/// </summary>
		[AOT.MonoPInvokeCallback(typeof(LockLostCallback))]
		private static void OnLockLostNative()
		{
			browserReleased = true;
		}
#endif
	}
}
