using System;
using System.Runtime.InteropServices;

namespace FishNet.Transporting.WebTransport.WebGL
{
	/// <summary>
	/// DllImport declarations for the WebTransport JavaScript bridge (WebGL).
	/// When running in a browser, the browser's native WebTransport API handles
	/// QUIC/HTTP3. This class calls into WebTransport.jslib.
	///
	/// Pattern mirrors Bayou's SimpleWebJSLib.cs.
	/// </summary>
#if UNITY_WEBGL && !UNITY_EDITOR
	internal static class WebTransportJSLib
	{
		/// <summary>
		/// Connect to a WebTransport server.
		/// </summary>
		/// <param name="url">Full URL, e.g. "https://game.fishmmo.com/wt/7770"</param>
		/// <param name="onOpen">Called when session is ready.</param>
		/// <param name="onClose">Called when session closes.</param>
		/// <param name="onStream">Called when reliable stream data arrives.</param>
		/// <param name="onDatagram">Called when unreliable datagram arrives.</param>
		/// <param name="onError">Called on error.</param>
		/// <returns>Transport index (handle) or -1 on failure.</returns>
		[DllImport("__Internal")]
		internal static extern int WTConnect(
			string url,
			Action<int> onOpen,
			Action<int> onClose,
			Action<int, IntPtr, int> onStream,
			Action<int, IntPtr, int> onDatagram,
			Action<int> onError);

		/// <summary>
		/// Send reliable data via a new bidirectional stream.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern bool WTSendStream(int index, byte[] data, int length);

		/// <summary>
		/// Send unreliable data via datagram.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern bool WTSendDatagram(int index, byte[] data, int length);

		/// <summary>
		/// Close the WebTransport session.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern void WTDisconnect(int index);

		/// <summary>
		/// Returns true if the session is in the 'connected' state.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern bool WTIsConnected(int index);
	}
#else
	/// <summary>
	/// Stub implementations for non-WebGL platforms.
	/// Logs a warning and returns a failure sentinel instead of throwing.
	/// </summary>
	internal static class WebTransportJSLib
	{
		internal static int WTConnect(
			string url,
			Action<int> onOpen,
			Action<int> onClose,
			Action<int, IntPtr, int> onStream,
			Action<int, IntPtr, int> onDatagram,
			Action<int> onError)
		{
			UnityEngine.Debug.LogWarning("[WebTransport] WebTransportJSLib is only available in WebGL builds.");
			return -1;
		}

		internal static bool WTSendStream(int index, byte[] data, int length)
		{
			UnityEngine.Debug.LogWarning("[WebTransport] WebTransportJSLib is only available in WebGL builds.");
			return false;
		}

		internal static bool WTSendDatagram(int index, byte[] data, int length)
		{
			UnityEngine.Debug.LogWarning("[WebTransport] WebTransportJSLib is only available in WebGL builds.");
			return false;
		}

		internal static void WTDisconnect(int index)
		{
			UnityEngine.Debug.LogWarning("[WebTransport] WebTransportJSLib is only available in WebGL builds.");
		}

		internal static bool WTIsConnected(int index)
		{
			UnityEngine.Debug.LogWarning("[WebTransport] WebTransportJSLib is only available in WebGL builds.");
			return false;
		}
	}
#endif
}