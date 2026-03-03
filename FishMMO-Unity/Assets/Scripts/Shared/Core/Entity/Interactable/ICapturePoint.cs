using System;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for capture point interactables.
	/// Exposes state, ownership, and capture logic needed by the interaction handler.
	/// </summary>
	public interface ICapturePoint : IInteractable
	{
		/// <summary>
		/// Fired when a player captures this point. Parameters: (capturePoint, captorCharacterID).
		/// </summary>
		static Action<CapturePoint, long> OnCaptured;

		/// <summary>
		/// Fired when the capture state changes. Parameters: (capturePoint, newState).
		/// </summary>
		static Action<CapturePoint, ObjectiveState> OnStateChanged;

		/// <summary>
		/// The capture point template defining interactions to capture, point value, and other settings.
		/// </summary>
		CapturePointTemplate Template { get; }

		/// <summary>
		/// Achievement template to increment when a player captures this point.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }

		/// <summary>
		/// The character ID of the current owner of this capture point.
		/// </summary>
		long OwnerCharacterID { get; }

		/// <summary>
		/// The current capture progress toward the next ownership change.
		/// </summary>
		int CaptureProgress { get; }

		/// <summary>
		/// The current objective state of this capture point.
		/// </summary>
		ObjectiveState State { get; }

		/// <summary>
		/// Applies a capture interaction from the specified character. Returns true if the point was fully captured.
		/// </summary>
		/// <param name="characterID">The ID of the capturing character.</param>
		/// <returns>True if the capture point was fully captured; otherwise, false.</returns>
		bool ApplyCapture(long characterID);
	}
}