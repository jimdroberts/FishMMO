using System;
using System.Runtime.InteropServices;

namespace FishNet.Transporting.WebTransport.WebGL
{
	/// <summary>
	/// Delegate types for the WebTransport JavaScript bridge (WebGL).
	/// These are defined as named delegate types (not System.Action) so that
	/// IL2CPP can marshal them to JS via [AOT.MonoPInvokeCallback].
	/// </summary>

	/// <summary>Callback receiving only the session index (open, close, error).</summary>
	public delegate void WTIndexCallback(int index);

	/// <summary>Callback receiving session index + data pointer + length (stream, datagram).</summary>
	public delegate void WTDataCallback(int index, IntPtr dataPtr, int length);

#if UNITY_WEBGL && !UNITY_EDITOR
	internal static class WebTransportJSLib
	{
		/// <summary>
		/// Connect to a WebTransport server.
		/// All callbacks must be static methods decorated with
		/// [AOT.MonoPInvokeCallback] for IL2CPP compatibility.
		/// </summary>
		/// <param name="url">Full URL, e.g. "https://game.fishmmo.com/wt/7770"</param>
		/// <returns>Transport index (handle) or -1 on failure.</returns>
		[DllImport("__Internal")]
		internal static extern int WTConnect(
			string url,
			WTIndexCallback onOpen,
			WTIndexCallback onClose,
			WTDataCallback onStream,
			WTDataCallback onDatagram,
			WTIndexCallback onError);

		/// <summary>
		/// Send reliable data via a new bidirectional stream.
		/// IMPORTANT: Returns true when the data is QUEUED for send, NOT when it has been
		/// delivered. The underlying browser WebTransport API uses JS Promises which may
		/// reject asynchronously. The caller should not interpret a true return as
		/// guaranteed delivery.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern bool WTSendStream(int index, byte[] data, int length);

		/// <summary>
		/// Send unreliable data via datagram.
		/// IMPORTANT: Returns true when the data is QUEUED for send, NOT when it has been
		/// delivered. The underlying browser WebTransport API uses JS Promises which may
		/// reject asynchronously. The caller should not interpret a true return as
		/// guaranteed delivery.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern bool WTSendDatagram(int index, byte[] data, int length);

		/// <summary>Close the WebTransport session.</summary>
		[DllImport("__Internal")]
		internal static extern void WTDisconnect(int index);

		/// <summary>
		/// Returns true if the session is in the 'connected' state.
		/// NOTE: Reserved/deprecated — not currently called from managed code.
		/// Callers should track connection state locally instead.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern bool WTIsConnected(int index);

		/// <summary>
		/// Reserved for future stream congestion control.
		/// Currently a no-op in the jslib implementation — streams are created
		/// on demand without threshold enforcement.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern void WTSetStreamThreshold(int index, int threshold);

		/// <summary>
		/// Retrieves the last error message stored for the given session index.
		/// Returns a pointer to a UTF-8 string allocated on the WASM heap,
		/// or IntPtr.Zero if no error is available.
		/// The caller MUST free the returned pointer via <see cref="WASMFree"/>.
		/// </summary>
		[DllImport("__Internal")]
		internal static extern IntPtr WTGetLastErrorMessage(int index);

		/// <summary>
		/// Frees a pointer previously allocated on the WASM heap.
		/// Maps to Emscripten's built-in _free() via EntryPoint.
		/// Required to release memory returned by <see cref="WTGetLastErrorMessage"/>.
		/// </summary>
		[DllImport("__Internal", EntryPoint = "_free")]
		internal static extern void WASMFree(IntPtr ptr);
	}
#else
	/// <summary>
	/// Stub implementations for non-WebGL platforms.
	/// </summary>
	internal static class WebTransportJSLib
	{
		internal static int WTConnect(
			string url,
			WTIndexCallback onOpen,
			WTIndexCallback onClose,
			WTDataCallback onStream,
			WTDataCallback onDatagram,
			WTIndexCallback onError)
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

		/// <summary>
		/// Reserved for future stream congestion control.
		/// No-op on non-WebGL platforms.
		/// </summary>
		internal static void WTSetStreamThreshold(int index, int threshold)
		{
			// Reserved — no-op on non-WebGL platforms.
		}

		/// <summary>Stub — returns IntPtr.Zero on non-WebGL platforms.</summary>
		internal static IntPtr WTGetLastErrorMessage(int index) => IntPtr.Zero;

		/// <summary>Stub — no-op on non-WebGL platforms.</summary>
		internal static void WASMFree(IntPtr ptr) { /* no-op */ }
	}
#endif
}
