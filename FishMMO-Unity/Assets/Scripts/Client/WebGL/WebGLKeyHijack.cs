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
		public int[] HijackKeyCodes;

#if UNITY_WEBGL
		[DllImport("__Internal")]
		private static extern void ClientWebGLQuit();

		[DllImport("__Internal")]
		private static extern void AddHijackKeysListener(IntPtr keyCodesPtr, int keyCodesLength);
#endif

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

		public void ClientQuit()
		{
#if UNITY_WEBGL
			ClientWebGLQuit();
#endif
		}
	}
}