using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Drives <see cref="ClientSettings.Pump"/>, and forces the owed write out when the player is
	/// about to stop looking at the game.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a component of its own.</b> The debounce used to be pumped from
	/// <c>UITKControl.Update</c>, which every panel runs. That works only for as long as at least
	/// one panel is alive and enabled, and it made a guarantee about the player's settings depend
	/// on an unrelated subsystem: a scene with no panels, or a transfer that unloads the one
	/// holding them, silently stops the clock on a write that is already owed. Nothing reports it,
	/// and the setting is simply gone next launch. It also cost forty-odd redundant calls per frame
	/// — one per registered panel — to read a single bool.</para>
	///
	/// <para><b>Created on demand and never destroyed.</b> The object appears the first time a
	/// write is owed and lives in the <c>DontDestroyOnLoad</c> scene, so scene loads, world
	/// transfers and quit-to-login cannot take it with them.
	/// <c>HideFlags.HideAndDontSave</c> keeps it out of the hierarchy view and out of scene saves,
	/// following <see cref="ClientAudioFocusWatcher"/>, which exists for the same reason.</para>
	///
	/// <para><b>Losing focus flushes.</b> A debounce is a bet that the player will still be here in
	/// three quarters of a second, and the moment they alt-tab is the moment that bet stops being
	/// safe. It matters most in a browser: <c>OnApplicationQuit</c> does not run when a tab is
	/// closed, so on WebGL a flush on focus loss is the last chance a pending change gets to reach
	/// IndexedDB. <see cref="ClientSettings.Flush"/> is a single bool read when nothing is owed, so
	/// doing this on every focus change costs nothing.</para>
	/// </remarks>
	internal sealed class ClientSettingsPump : MonoBehaviour
	{
		/// <summary>The single live pump, or null before one has been created.</summary>
		private static ClientSettingsPump instance;

		/// <summary>
		/// Creates the pump if it does not already exist.
		/// </summary>
		internal static void Install()
		{
			/* Unity's == comparison, which reports a destroyed object as null. That matters in the
			 * editor with domain reload disabled: the static survives into the next play session
			 * pointing at a GameObject that no longer exists, and a reference-equality check would
			 * take that as "already installed" and never create a live pump. */
			if (instance != null)
			{
				return;
			}

			/* Isolated. This is reached from ClientSettings.RequestSave, which is on the boot path
			 * — ClientSettings.EnsureLoaded writes a default file on first launch — and an
			 * exception escaping here would surface as "could not load the client configuration"
			 * from that method's own handler, which is both wrong and alarming. Without a pump the
			 * debounce still discharges through the explicit Flush calls on panel close and on
			 * quit; it just stops being automatic. */
			try
			{
				GameObject host = new GameObject(nameof(ClientSettingsPump))
				{
					hideFlags = HideFlags.HideAndDontSave,
				};
				DontDestroyOnLoad(host);
				instance = host.AddComponent<ClientSettingsPump>();
			}
			catch (System.Exception ex)
			{
				FishMMO.Logging.Log.Warning("ClientSettingsPump",
					$"Could not start the settings write pump; settings will be written on close instead: {ex.Message}");
			}
		}

		/// <summary>Flushes the owed write once its quiet period has elapsed.</summary>
		private void Update()
		{
			ClientSettings.Pump();
		}

		/// <summary>Writes out anything owed when the window loses focus.</summary>
		/// <param name="hasFocus">True when the client's window gained focus.</param>
		private void OnApplicationFocus(bool hasFocus)
		{
			if (!hasFocus)
			{
				ClientSettings.Flush();
			}
		}

		/// <summary>
		/// Treats a pause as a loss of focus.
		/// </summary>
		/// <remarks>
		/// Mobile and some standalone configurations raise <c>OnApplicationPause</c> without a
		/// matching focus message, and a backgrounded application is one the operating system may
		/// terminate without further warning.
		/// </remarks>
		/// <param name="paused">True when the application was backgrounded.</param>
		private void OnApplicationPause(bool paused)
		{
			if (paused)
			{
				ClientSettings.Flush();
			}
		}

		/// <summary>Writes out anything owed on the way down.</summary>
		private void OnApplicationQuit()
		{
			ClientSettings.Flush();
		}

		/// <summary>Clears the singleton so a later install can recreate it.</summary>
		private void OnDestroy()
		{
			/* Last chance: a pump being destroyed while a write is owed is the exact case this
			 * class exists to prevent, and it happens on the way out of a play-mode session. */
			ClientSettings.Flush();

			if (ReferenceEquals(instance, this))
			{
				instance = null;
			}
		}
	}
}
