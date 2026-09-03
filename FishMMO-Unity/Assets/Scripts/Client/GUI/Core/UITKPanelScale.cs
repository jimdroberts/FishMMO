using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Scales the whole interface by adjusting the shared <see cref="PanelSettings"/> reference
	/// resolution.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the reference resolution and not <c>PanelSettings.scale</c>.</b> That property
	/// only has an effect under <c>PanelScaleMode.ConstantPixelSize</c>; the project's panel is
	/// authored <c>ScaleWithScreenSize</c> against a 1200x800 reference matched on width, so
	/// setting <c>scale</c> does nothing at all. Under that mode the reference resolution <em>is</em>
	/// the scale knob: a smaller reference means each authored point covers more of the screen, so
	/// dividing it by the multiplier makes the interface larger.</para>
	///
	/// <para><b>Panel positions move with it.</b> Stored positions are in panel points, and
	/// changing the reference resolution changes how many points fit on screen — so a panel the
	/// player pushed against the right edge is off-screen at a larger scale.
	/// <see cref="UITKControl"/> re-clamps a restored position into the visible area on every
	/// layout, which is what keeps that from stranding a window; nothing here has to move them.</para>
	///
	/// <para><b>Restored in the editor.</b> <see cref="PanelSettings"/> is a project asset, and a
	/// value written into it at play time stays written — a developer who ran the client once with
	/// a non-default UI scale would find the asset modified in source control. The authored
	/// reference resolution is captured before the first change and put back when play mode ends.</para>
	/// </remarks>
	public static class UITKPanelScale
	{
		/// <summary>The panel asset being scaled, discovered from the first panel to come up.</summary>
		private static PanelSettings panelSettings;

		/// <summary>The reference resolution authored in the asset, captured before any change.</summary>
		private static Vector2Int authoredReferenceResolution;

		/// <summary>True once <see cref="authoredReferenceResolution"/> holds the authored value.</summary>
		private static bool hasAuthoredResolution;

		/// <summary>The multiplier currently in force.</summary>
		private static float currentScale = 1.0f;

		/// <summary>
		/// Records the panel asset a control draws through, and applies the pending scale to it.
		/// </summary>
		/// <param name="settings">The control's <see cref="PanelSettings"/>. May be null.</param>
		/// <remarks>
		/// Called from every panel as it starts. The client applies its settings during boot,
		/// before any <see cref="UIDocument"/> exists, so the scale has nowhere to go at the moment
		/// it is read — the first panel to appear is what supplies the target.
		/// </remarks>
		public static void Register(PanelSettings settings)
		{
			if (settings == null || ReferenceEquals(settings, panelSettings))
			{
				return;
			}

			panelSettings = settings;

			if (!hasAuthoredResolution)
			{
				authoredReferenceResolution = settings.referenceResolution;
				hasAuthoredResolution = true;

#if UNITY_EDITOR
				UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
			}

			ApplyToPanel();
		}

		/// <summary>
		/// Sets the interface scale multiplier.
		/// </summary>
		/// <param name="scale">
		/// Multiplier of the authored size, clamped between
		/// <see cref="ClientSettings.MinimumUIScale"/> and <see cref="ClientSettings.MaximumUIScale"/>.
		/// </param>
		public static void Apply(float scale)
		{
			currentScale = float.IsNaN(scale)
				? 1.0f
				: Mathf.Clamp(scale, ClientSettings.MinimumUIScale, ClientSettings.MaximumUIScale);

			ApplyToPanel();
		}

		/// <summary>The multiplier currently in force.</summary>
		public static float Current => currentScale;

		/// <summary>Writes the current multiplier into the panel asset.</summary>
		private static void ApplyToPanel()
		{
			if (panelSettings == null || !hasAuthoredResolution)
			{
				/* Both silent conditions, and the interface scale simply does nothing when either
				 * holds: the setting is written and read back correctly, the slider moves, and no
				 * panel changes size. Register supplies both, from the first panel to come up, so a
				 * UIDocument with no PanelSettings assigned leaves the scale with nowhere to go. */
				FishMMO.Logging.Log.Warning("UITKPanelScale",
					$"Interface scale not applied: panelSettings={(panelSettings != null ? panelSettings.name : "<null>")}, " +
					$"hasAuthoredResolution={hasAuthoredResolution}.");
				return;
			}

			/* Rounded, and floored at one. A reference resolution of zero divides by zero inside
			 * UI Toolkit's scaling and produces a panel with no visible content at all. */
			Vector2Int applied = new Vector2Int(
				Mathf.Max(1, Mathf.RoundToInt(authoredReferenceResolution.x / currentScale)),
				Mathf.Max(1, Mathf.RoundToInt(authoredReferenceResolution.y / currentScale)));

			panelSettings.referenceResolution = applied;

			/* Reports what actually landed in the asset. The write can be correct and still have no
			 * visible effect if the panel is not authored ScaleWithScreenSize, so the scale mode is
			 * named here too rather than assumed. */
			FishMMO.Logging.Log.Debug("UITKPanelScale",
				$"Interface scale {currentScale:0.00}x -> reference resolution {applied.x}x{applied.y} " +
				$"(authored {authoredReferenceResolution.x}x{authoredReferenceResolution.y}, " +
				$"scaleMode {panelSettings.scaleMode}).");
		}

#if UNITY_EDITOR
		/// <summary>Puts the authored reference resolution back when play mode ends.</summary>
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode)
			{
				return;
			}

			if (panelSettings != null && hasAuthoredResolution)
			{
				panelSettings.referenceResolution = authoredReferenceResolution;
			}

			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

			// The statics survive a play-mode cycle when domain reload is disabled.
			panelSettings = null;
			hasAuthoredResolution = false;
			currentScale = 1.0f;
		}
#endif
	}
}
