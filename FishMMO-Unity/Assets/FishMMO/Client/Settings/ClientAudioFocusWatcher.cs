using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Reports window focus changes to <see cref="ClientAudioSettings"/> so the "mute when
	/// unfocused" setting has something to act on.
	/// </summary>
	/// <remarks>
	/// A component and not a static hook because <c>OnApplicationFocus</c> is a Unity message and
	/// only reaches a <see cref="MonoBehaviour"/>. It creates its own hidden, scene-independent
	/// GameObject rather than expecting to be placed in a scene: the client loads five scenes
	/// across its lifetime and a watcher that lived in one of them would stop reporting the moment
	/// that scene unloaded — which is exactly when the player has alt-tabbed away and the setting
	/// is supposed to be doing something.
	/// <para>
	/// <c>HideFlags.HideAndDontSave</c> keeps it out of the hierarchy view and out of scene saves,
	/// so it cannot be accidentally serialised into a scene by a developer who saves while playing.
	/// </para>
	/// </remarks>
	internal sealed class ClientAudioFocusWatcher : MonoBehaviour
	{
		/// <summary>The single live watcher, or null before one has been created.</summary>
		private static ClientAudioFocusWatcher instance;

		/// <summary>
		/// Creates the watcher if it does not already exist.
		/// </summary>
		internal static void Install()
		{
			/* The == comparison is Unity's, which reports a destroyed object as null. That matters
			 * in the editor with domain reload disabled: the static survives into the next play
			 * session pointing at a GameObject that no longer exists, and a reference-equality
			 * check would take that as "already installed" and never create a live watcher. */
			if (instance != null)
			{
				return;
			}

			/* Isolated, for the reason ClientSettingsPump.Install documents. This runs from
			 * ClientAudioSettings.ApplySaved, and ApplySaved is reached lazily from GetVolume —
			 * which the options panel calls while building its audio row. An exception escaping
			 * here therefore does not just cost the unfocused-mute feature: it propagates out of
			 * UITKOptions.OnStarting and leaves the settings panel half-built, which is exactly
			 * the failure the frame-rate dropdown used to cause. Muting on focus loss is a
			 * convenience; it must never be able to take the settings screen down with it.
			 *
			 * DontDestroyOnLoad is the concrete way this throws: it is play-mode only, so any
			 * editor tooling that drives the panel outside play mode lands here. */
			try
			{
				GameObject host = new GameObject(nameof(ClientAudioFocusWatcher))
				{
					hideFlags = HideFlags.HideAndDontSave,
				};
				DontDestroyOnLoad(host);
				instance = host.AddComponent<ClientAudioFocusWatcher>();

				/* Seeded from the live value rather than assumed focused. A client launched behind
				 * another window, or one whose settings are applied during a long load, would
				 * otherwise report focus it does not have until the player next alt-tabbed twice. */
				ClientAudioSettings.SetWindowFocused(Application.isFocused);
			}
			catch (System.Exception ex)
			{
				FishMMO.Logging.Log.Warning("ClientAudioFocusWatcher",
					$"Could not install the focus watcher; audio will not mute when unfocused: {ex.Message}");
			}
		}

		/// <summary>Reports a focus change.</summary>
		/// <param name="hasFocus">True when the client's window gained focus.</param>
		private void OnApplicationFocus(bool hasFocus)
		{
			ClientAudioSettings.SetWindowFocused(hasFocus);
		}

		/// <summary>
		/// Treats a pause as a loss of focus.
		/// </summary>
		/// <remarks>
		/// Mobile and some standalone configurations raise <c>OnApplicationPause</c> without a
		/// matching focus message. Without this the client keeps playing at full volume in the
		/// background on exactly the platforms where that is least acceptable.
		/// </remarks>
		/// <param name="paused">True when the application was backgrounded.</param>
		private void OnApplicationPause(bool paused)
		{
			ClientAudioSettings.SetWindowFocused(!paused);
		}

		/// <summary>Clears the singleton so a later install can recreate it.</summary>
		private void OnDestroy()
		{
			if (ReferenceEquals(instance, this))
			{
				instance = null;
			}
		}
	}
}
