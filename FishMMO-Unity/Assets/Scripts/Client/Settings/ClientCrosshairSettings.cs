using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The crosshair's appearance: whether it is drawn, what shape it is, how large, and how
	/// strongly.
	/// </summary>
	/// <remarks>
	/// <para><b>Colour is deliberately absent.</b> It is already a themed colour —
	/// <see cref="UITKTheme.ColorNames"/> carries <c>Crosshair</c>, and the UI tab's colour list
	/// edits it — so exposing a second control for the same value would give a player two places
	/// to set one colour and no rule about which wins.</para>
	///
	/// <para><b>Why the settings live here rather than on the panel.</b>
	/// <see cref="UITKCrosshair"/> is a panel: it does not exist until the HUD is built, and its
	/// <c>OnStarting</c> re-runs whenever the visual tree is replaced. A panel that owned the
	/// defaults would be the only thing that knew them, and every consumer would have to wait for
	/// it. These are plain reads off <see cref="ClientSettings"/>, so the options panel and the
	/// crosshair can both answer "what size is it" without either of them existing yet.</para>
	/// </remarks>
	public static class ClientCrosshairSettings
	{
		/// <summary>
		/// The shapes a player may pick.
		/// </summary>
		/// <remarks>
		/// Stored as the ORDINAL, so members must only ever be appended — an inserted value would
		/// silently re-map every stored choice. Each shape is a USS class on the crosshair icon
		/// rather than a separate texture: only one crosshair sprite ships, and a dot and a ring
		/// are a border radius, not artwork.
		/// </remarks>
		public enum CrosshairStyle
		{
			/// <summary>The shipped sprite: a small plus.</summary>
			Cross = 0,

			/// <summary>A filled circle.</summary>
			Dot = 1,

			/// <summary>An unfilled ring, which leaves the target itself unobscured.</summary>
			Circle = 2,
		}

		/// <summary>USS class applied for <see cref="CrosshairStyle.Cross"/>.</summary>
		public const string CrossClass = "crosshair-icon--cross";
		/// <summary>USS class applied for <see cref="CrosshairStyle.Dot"/>.</summary>
		public const string DotClass = "crosshair-icon--dot";
		/// <summary>USS class applied for <see cref="CrosshairStyle.Circle"/>.</summary>
		public const string CircleClass = "crosshair-icon--circle";

		/// <summary>Every style class, so a rebuild can clear the ones that do not apply.</summary>
		public static readonly string[] StyleClasses = { CrossClass, DotClass, CircleClass };

		/// <summary>Labels shown in the options panel, in <see cref="CrosshairStyle"/> order.</summary>
		public static readonly string[] StyleLabels = { "Cross", "Dot", "Circle" };

		/// <summary>Whether a fresh install draws a crosshair.</summary>
		public const bool DefaultEnabled = true;

		/// <summary>The shape a fresh install uses.</summary>
		public const CrosshairStyle DefaultStyle = CrosshairStyle.Cross;

		/// <summary>
		/// Edge length a fresh install uses, in panel points.
		/// </summary>
		/// <remarks>
		/// Eight, which is what <c>UICrosshair.uss</c> authors and what the uGUI crosshair drew
		/// before the UI Toolkit port. The default is repeated here rather than left to the
		/// stylesheet because this value is also what the options slider shows before the player
		/// has ever moved it, and a slider that starts somewhere other than the crosshair's actual
		/// size is a control that lies about the state it is editing.
		/// </remarks>
		public const float DefaultSize = 8.0f;

		/// <summary>Smallest crosshair offered, in panel points.</summary>
		/// <remarks>Four: below that the shape is indistinguishable from a stuck pixel.</remarks>
		public const float MinimumSize = 4.0f;

		/// <summary>
		/// Largest crosshair offered, in panel points.
		/// </summary>
		/// <remarks>
		/// Thirty-two. A crosshair is a sighting mark, and past this it stops marking a point and
		/// starts covering the thing being aimed at — which is the failure the 8px retune in
		/// UICrosshair.uss already corrected once.
		/// </remarks>
		public const float MaximumSize = 32.0f;

		/// <summary>Opacity a fresh install uses.</summary>
		public const float DefaultOpacity = 1.0f;

		/// <summary>
		/// Faintest crosshair offered.
		/// </summary>
		/// <remarks>
		/// Not zero. Zero is "no crosshair", which the enable toggle already says, and a slider
		/// that can silently reproduce a toggle's off state gives the player two ways to reach one
		/// result and no way to tell which one they are in.
		/// </remarks>
		public const float MinimumOpacity = 0.1f;

		/// <summary>Strongest crosshair offered.</summary>
		public const float MaximumOpacity = 1.0f;

		/// <summary>
		/// Raised when any crosshair setting changes.
		/// </summary>
		/// <remarks>
		/// The crosshair panel subscribes; the options panel raises. Neither holds a reference to
		/// the other, which matters because the options panel can be open in a scene where the HUD
		/// is not loaded at all.
		/// </remarks>
		public static event Action OnChanged;

		/// <summary>Whether the crosshair is drawn at all.</summary>
		public static bool Enabled => ClientSettings.GetBool(ClientSettings.CrosshairEnabledKey, DefaultEnabled);

		/// <summary>The chosen shape, clamped to a style this build knows.</summary>
		public static CrosshairStyle Style
		{
			get
			{
				int stored = ClientSettings.GetInt(ClientSettings.CrosshairStyleKey, (int)DefaultStyle);
				if (stored < 0 || stored >= StyleLabels.Length)
				{
					return DefaultStyle;
				}
				return (CrosshairStyle)stored;
			}
		}

		/// <summary>The chosen edge length, in panel points.</summary>
		public static float Size => ClientSettings.GetFloat(
			ClientSettings.CrosshairSizeKey, DefaultSize, MinimumSize, MaximumSize);

		/// <summary>The chosen opacity.</summary>
		public static float Opacity => ClientSettings.GetFloat(
			ClientSettings.CrosshairOpacityKey, DefaultOpacity, MinimumOpacity, MaximumOpacity);

		/// <summary>Writes whether the crosshair is drawn and notifies the panel.</summary>
		public static void SetEnabled(bool value)
		{
			ClientSettings.Set(ClientSettings.CrosshairEnabledKey, value);
			Raise();
		}

		/// <summary>Writes the shape and notifies the panel.</summary>
		public static void SetStyle(CrosshairStyle value)
		{
			int ordinal = (int)value;
			if (ordinal < 0 || ordinal >= StyleLabels.Length)
			{
				ordinal = (int)DefaultStyle;
			}

			ClientSettings.Set(ClientSettings.CrosshairStyleKey, ordinal);
			Raise();
		}

		/// <summary>Writes the edge length and notifies the panel.</summary>
		public static void SetSize(float value)
		{
			ClientSettings.Set(ClientSettings.CrosshairSizeKey, Clamp(value, DefaultSize, MinimumSize, MaximumSize));
			Raise();
		}

		/// <summary>Writes the opacity and notifies the panel.</summary>
		public static void SetOpacity(float value)
		{
			ClientSettings.Set(ClientSettings.CrosshairOpacityKey, Clamp(value, DefaultOpacity, MinimumOpacity, MaximumOpacity));
			Raise();
		}

		/// <summary>Clamps a value, rejecting the non-finite ones a hand-edited file can carry.</summary>
		/// <remarks>
		/// NaN compares false against every bound, so <c>Mathf.Clamp</c> passes it straight
		/// through — and a NaN reaching a width or an opacity takes the element out of layout.
		/// </remarks>
		private static float Clamp(float value, float fallback, float minimum, float maximum)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				value = fallback;
			}
			return Mathf.Clamp(value, minimum, maximum);
		}

		/// <summary>Notifies subscribers, reporting rather than propagating a handler's failure.</summary>
		private static void Raise()
		{
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientCrosshairSettings", "A crosshair-settings subscriber threw.", ex);
			}
		}
	}
}
