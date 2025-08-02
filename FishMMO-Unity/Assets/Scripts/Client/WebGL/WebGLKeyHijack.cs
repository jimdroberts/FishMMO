#if UNITY_WEBGL
using System;
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// This class will hijack web browser key listeners which will help WebGL builds stay contained.
	/// This is not intended to be used as a malicious tool and is solely for preventing certain keys from executing
	/// browser functions while the WebGL game is focused.
	/// </summary>
	public class WebGLKeyHijack : MonoBehaviour
	{
		/// <summary>
		/// Array of key codes to hijack in the browser. Prevents default browser actions for these keys.
		/// </summary>
		public int[] HijackKeyCodes;

#if UNITY_WEBGL
		/// <summary>
		/// Calls the browser-side function to quit the WebGL client.
		/// </summary>
		[DllImport("__Internal")]
		private static extern void ClientWebGLQuit();

		/// <summary>
		/// Adds a browser-side key listener to hijack specified key codes.
		/// </summary>
		/// <param name="keyCodesPtr">Pointer to the array of key codes.</param>
		/// <param name="keyCodesLength">Number of key codes in the array.</param>
		[DllImport("__Internal")]
		private static extern void AddHijackKeysListener(IntPtr keyCodesPtr, int keyCodesLength);
#endif

		/// <summary>
		/// Called when the script instance is being loaded. Sets up key hijacking for WebGL builds.
		/// </summary>
		void Awake()
		{
#if UNITY_WEBGL
			if (HijackKeyCodes != null && HijackKeyCodes.Length > 0)
			{
				// Allocate memory and copy keyCodes array to it
				GCHandle handle = GCHandle.Alloc(HijackKeyCodes, GCHandleType.Pinned);
				IntPtr pointer = handle.AddrOfPinnedObject();

				AddHijackKeysListener(pointer, HijackKeyCodes.Length);

				// Release memory
				handle.Free();
			}
#endif
		}

		/// <summary>
		/// Quits the WebGL client by calling the browser-side quit function.
		/// </summary>
		public void ClientQuit()
		{
#if UNITY_WEBGL
			ClientWebGLQuit();
#endif
		}
	}
}