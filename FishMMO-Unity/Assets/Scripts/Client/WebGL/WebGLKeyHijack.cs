using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// This class will hijack web browser key listeners which will help WebGL builds stay contained.
	/// </summary>
	public class WebGLKeyHijack : MonoBehaviour
	{
		/// <summary>
		/// Array of key codes to hijack in the browser. Prevents default browser actions for these keys.
		/// </summary>
		public int[] HijackKeyCodes;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ClientWebGLQuit();

        [DllImport("__Internal")]
        private static extern void AddHijackKeysListener(IntPtr keyCodesPtr, int keyCodesLength);
#endif

		/// <summary>
		/// Called when the script instance is being loaded. Sets up key hijacking for WebGL builds.
		/// </summary>
		void Awake()
		{
			if (HijackKeyCodes == null || HijackKeyCodes.Length == 0)
			{
				return;
			}

#if UNITY_WEBGL && !UNITY_EDITOR
            // Allocate memory and copy keyCodes array to it
            GCHandle handle = GCHandle.Alloc(HijackKeyCodes, GCHandleType.Pinned);
            IntPtr pointer = handle.AddrOfPinnedObject();

            AddHijackKeysListener(pointer, HijackKeyCodes.Length);

            // Release memory
            handle.Free();
#endif
		}

		/// <summary>
		/// Quits the WebGL client by calling the browser-side quit function.
		/// </summary>
		public void ClientQuit()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
            ClientWebGLQuit();
#else
			Debug.Log("[WebGLKeyHijack] ClientQuit simulation called in Editor.");
#endif
		}
	}
}