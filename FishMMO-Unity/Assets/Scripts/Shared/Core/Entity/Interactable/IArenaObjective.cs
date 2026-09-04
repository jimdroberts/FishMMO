using System;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What kind of arena objective a scene object is.
	/// </summary>
	public enum ArenaObjectiveKind : byte
	{
		/// <summary>A team's flag stand: the enemy takes the flag from it, the owner captures at it.</summary>
		FlagStand = 0,
		/// <summary>A control point held by whichever team last captured it.</summary>
		ControlPoint = 1,
	}

	/// <summary>
	/// An interactable inside an arena scene that the match's mode scores: a flag stand for
	/// Capture the Flag, a control point for King of the Hill.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The objective itself holds no match state. It is a place in the scene with an identity and,
	/// for a flag stand, a team; what has happened to it — who carries the flag, who holds the
	/// point, how far a capture has progressed — lives in the match coordinator on the hosting
	/// scene server and is sent to clients with the match state. A scene handle is reused after
	/// unload and an instance of an arena scene may be loaded several times on one server, so the
	/// coordinator keys objective state by (scene handle, objective id).
	/// </para>
	/// <para>
	/// Interacting runs the objective's ECA interaction, whose action raises
	/// <see cref="OnServerInteracted"/>; the coordinator decides what, if anything, it means for the
	/// match the player is in. The same pattern <c>IDialogueInteractable</c> uses to reach the
	/// server without the shared assembly knowing about it.
	/// </para>
	/// </remarks>
	public interface IArenaObjective : IInteractable
	{
		/// <summary>Raised on the server when a player interacts with an objective.</summary>
		static Action<IPlayerCharacter, IArenaObjective> OnServerInteracted;

		/// <summary>Flag stand or control point.</summary>
		ArenaObjectiveKind Kind { get; }

		/// <summary>The team a flag stand belongs to (0-based). Ignored for a control point.</summary>
		int Team { get; }
	}
}
