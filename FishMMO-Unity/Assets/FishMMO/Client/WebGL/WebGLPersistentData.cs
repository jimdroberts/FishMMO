using System.Runtime.InteropServices;

namespace FishMMO.Client
{
	/// <summary>
	/// Pushes files written under <c>Application.persistentDataPath</c> into the browser's
	/// IndexedDB, so they survive the page being closed.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this exists.</b> On WebGL <c>persistentDataPath</c> is an Emscripten IDBFS
	/// mount, which is an in-memory filesystem with IndexedDB behind it. A write reaches memory
	/// immediately and IndexedDB only when the mount is persisted. Unity persists automatically on
	/// file close, but only when the page passes <c>autoSyncPersistentDataPath: true</c> to
	/// <c>createUnityInstance()</c> — this project ships the stock PWA template, which does not.
	/// Without an explicit sync the settings file is written, is read back correctly for the rest
	/// of the session, and has vanished by the next visit. That is indistinguishable, from the
	/// player's side, from settings that are never saved at all.</para>
	///
	/// <para><b>Everywhere else this is nothing.</b> On a desktop build the method compiles to an
	/// empty body, so callers need no platform guard of their own — which is the point. A guard at
	/// each call site is a guard somebody forgets to add to the next one.</para>
	/// </remarks>
	public static class WebGLPersistentData
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		/// <summary>Queues an IndexedDB persist for the persistent-data mount. See WebGL.jslib.</summary>
		[DllImport("__Internal")]
		private static extern void FishMMOSyncPersistentData();
#endif

		/// <summary>
		/// Requests that pending writes under <c>Application.persistentDataPath</c> be persisted.
		/// </summary>
		/// <remarks>
		/// Asynchronous on the browser side and safe to call after every save: the underlying
		/// queue coalesces requests and never runs two syncs at once. Failures are reported to the
		/// browser console and never thrown — a browser with IndexedDB unavailable must lose
		/// settings between sessions rather than fail the save that is holding them for this one.
		/// </remarks>
		public static void Sync()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			try
			{
				FishMMOSyncPersistentData();
			}
			catch (System.Exception ex)
			{
				/* A missing symbol is the realistic failure — a build whose jslib was excluded.
				 * The save itself already succeeded, so this costs persistence across sessions
				 * and nothing else. */
				FishMMO.Logging.Log.Warning("WebGLPersistentData",
					$"Could not persist settings to IndexedDB: {ex.Message}");
			}
#endif
		}
	}
}
