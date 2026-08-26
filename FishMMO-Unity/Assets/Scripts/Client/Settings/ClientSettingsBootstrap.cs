using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Loads the client's configuration and applies the player's settings during boot.
	/// </summary>
	/// <remarks>
	/// <para><b>The problem this solves.</b> Nothing loaded the settings file at client start-up.
	/// <c>Configuration.GlobalSettings</c> was created lazily by whichever of two places asked for
	/// it first — the launcher, or the options panel — and the options panel ships closed, so in a
	/// client launched past the launcher neither ran. Every setting that is only ever applied by
	/// the options panel's <c>OnStarting</c> was therefore not in effect until the player opened
	/// the menu, and every setting read from the store by something else came back as a default:
	/// keybinding overrides were skipped (<c>LoadBindingOverrides</c> returns early on a null
	/// store), panel positions were not restored, and the theme was built from nothing.</para>
	///
	/// <para><b>Two phases, and why.</b> The store is loaded at
	/// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>, which precedes every scene's
	/// <c>Awake</c> — so the first panel to register already has settings to read. The settings are
	/// <em>applied</em> from a hook the bootstrap system raises, immediately after it installs the
	/// boot-time frame rate and VSync defaults. Applying them earlier than that would work exactly
	/// once and then be overwritten by those two lines.</para>
	///
	/// <para><b>Input is created here too.</b> <c>PlayerControls</c> used to be constructed only on
	/// world entry, which meant the Key Bindings tab could not show a single binding until the
	/// player was in the world, and a saved override was not loaded until then either. Creating the
	/// asset at boot costs nothing — it is data until an action map is enabled — and makes the
	/// bindings inspectable and editable from the login screen, which is where a player who has
	/// just installed the game will look for them.</para>
	/// </remarks>
	public static class ClientSettingsBootstrap
	{
#if !UNITY_SERVER
		/// <summary>Log channel for boot-phase settings messages.</summary>
		private const string LogChannel = "ClientSettings";

		/// <summary>True once the settings have been applied, so a second call is a no-op.</summary>
		private static bool applied;
#endif

		/// <summary>
		/// Loads the configuration store and arms the apply hook.
		/// </summary>
		/// <remarks>
		/// Runs before any scene loads. Deliberately does not apply anything itself: see the class
		/// remarks for why the apply half has to wait for the bootstrap system.
		/// </remarks>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Initialize()
		{
#if UNITY_SERVER
			// A headless server has no player settings, no display and no audio.
			return;
#else
			/* Reset explicitly. With "Enter Play Mode Options" configured to skip domain reload —
			 * which this project supports — statics survive between play-mode sessions, and an
			 * `applied` left true from the previous run would skip the whole of boot. */
			applied = false;

			/* Before anything writes to QualitySettings, and that ordering is the point: the very
			 * first write is MainBootstrapSystem forcing vSyncCount to zero during the first
			 * scene's Awake, which is after this and before any of ours. In the editor those
			 * writes land in the checked-in QualitySettings asset, so the authored values have to
			 * be recorded here to be restorable on play-mode exit. A no-op in a build. */
			ClientDisplaySettings.CaptureAuthoredQuality();

			ClientSettings.EnsureLoaded();

			/* Subscribed before unsubscribed for the same reason: without a domain reload the
			 * previous session's subscription is still attached, and a second one would apply
			 * everything twice. */
			MainBootstrapSystem.OnApplyClientBootSettings -= Apply;
			MainBootstrapSystem.OnApplyClientBootSettings += Apply;

			/* The apply half is NOT called here. The bootstrap system installs a boot-time frame
			 * rate and VSync default during the first scene's Awake, which is after this runs and
			 * would overwrite anything applied now. ApplyAfterFirstScene below is the backstop for
			 * scenes that have no bootstrap system to raise the hook — the UI validation and unit
			 * test scenes among them — and it runs late enough not to be overwritten. */
#endif
		}

#if !UNITY_SERVER
		/// <summary>
		/// Applies the settings once the first scene is up, for builds and scenes with no
		/// bootstrap system to raise the hook.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void ApplyAfterFirstScene()
		{
			Apply();
		}

		/// <summary>
		/// Applies every saved setting, exactly once per session.
		/// </summary>
		private static void Apply()
		{
			if (applied)
			{
				return;
			}
			applied = true;

			ClientSettings.ApplyAll();

			/* Installed explicitly, at a point where creating a GameObject is unambiguously safe.
			 * ClientSettings.RequestSave installs it on demand as well, which covers scenes that
			 * come up without a bootstrap system — but that path can first run from
			 * RuntimeInitializeLoadType.BeforeSceneLoad, where the first scene does not exist yet.
			 * Doing it here means the normal client never depends on that. */
			ClientSettingsPump.Install();

			/* Created here rather than on world entry so the bindings exist — and the player's
			 * saved overrides are in force — from the login screen onwards. This creates the asset
			 * WITHOUT enabling an action map; PlayerInputController.InitializeControls does that on
			 * world entry. So the Key Bindings tab has something to list from the moment the client
			 * starts, and nothing in the world becomes live early. */
			try
			{
				PlayerInputController.EnsureControlsCreated();
			}
			catch (System.Exception ex)
			{
				Log.Error(LogChannel, "Creating the input bindings during boot failed.", ex);
			}

			Log.Debug(LogChannel, "Client settings applied.");
		}
#endif
	}
}
