using System;
using System.Runtime.InteropServices;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Tells the client when the browser page stops being visible, which on a web build is the
	/// last reliable chance to write anything to disk.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this exists.</b> A WebGL build gets no <c>OnApplicationQuit</c> when the tab
	/// is closed, and the frame loop stops the moment the page is hidden, so a save that is waiting
	/// on a debounce in <c>Update</c> never comes due. The explored map lost every chunk walked since
	/// its last write that way. <see cref="PageHidden"/> is raised synchronously from the page's
	/// <c>visibilitychange</c>/<c>pagehide</c> handler, and the handler persists the IndexedDB mount
	/// straight after it returns — see <c>FishMMORegisterPageHidden</c> in WebGL.jslib.</para>
	///
	/// <para><b>Fires more than once per close, and on every tab switch.</b> Handlers must be cheap
	/// and idempotent: flush what is dirty, do nothing when nothing is.</para>
	///
	/// <para>Everywhere else this never fires. A desktop build has OnApplicationQuit and an editor
	/// has neither a page nor IndexedDB, so subscribing needs no platform guard.</para>
	/// </remarks>
	public static class WebGLPageLifecycle
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		/// <summary>The callback shape the jslib calls: no arguments, no return.</summary>
		private delegate void PageHiddenCallback();

		/// <summary>Registers <paramref name="callback"/> for visibilitychange/pagehide. See WebGL.jslib.</summary>
		[DllImport("__Internal")]
		private static extern void FishMMORegisterPageHidden(PageHiddenCallback callback);

		/// <summary>Whether the page listener has been installed. Installed once per page load.</summary>
		private static bool registered;
#endif

		private static event Action pageHidden;

		/// <summary>
		/// Raised when the page is hidden or being unloaded. Runs outside the frame loop.
		/// </summary>
		public static event Action PageHidden
		{
			add
			{
				pageHidden -= value;
				pageHidden += value;
				EnsureRegistered();
			}
			remove
			{
				pageHidden -= value;
			}
		}

		/// <summary>
		/// Installs the page listener on first use.
		/// </summary>
		private static void EnsureRegistered()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			if (registered)
			{
				return;
			}
			registered = true;

			try
			{
				FishMMORegisterPageHidden(OnPageHiddenNative);
			}
			catch (Exception ex)
			{
				/* A build whose jslib was excluded. Saves still happen on their own timers; only the
				 * last few seconds before a tab close are at risk. */
				Log.Warning("WebGLPageLifecycle", $"Could not register for page-hide notifications: {ex.Message}");
			}
#endif
		}

#if UNITY_WEBGL && !UNITY_EDITOR
		/// <summary>
		/// Entry point the browser calls. Static because IL2CPP cannot hand an instance method or a
		/// lambda to native code.
		/// </summary>
		[AOT.MonoPInvokeCallback(typeof(PageHiddenCallback))]
		private static void OnPageHiddenNative()
		{
			Action handlers = pageHidden;
			if (handlers == null)
			{
				return;
			}

			/* Each handler isolated. This runs from a browser event, outside Unity's player loop:
			 * an exception escaping here aborts the remaining handlers and surfaces as a wasm
			 * trap in the console rather than as a logged error. */
			foreach (Action handler in handlers.GetInvocationList())
			{
				try
				{
					handler();
				}
				catch (Exception ex)
				{
					Log.Error("WebGLPageLifecycle", "A page-hide handler threw.", ex);
				}
			}
		}
#endif
	}
}
