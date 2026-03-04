using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for AI state management — querying and transitioning between AI states.
	/// Separated from IAIController to satisfy ISP: consumers that only need state transitions
	/// can depend on this smaller contract.
	/// </summary>
	public interface IAIStateMachine : ICharacterBehaviour
	{
		/// <summary>
		/// The current AI state.
		/// </summary>
		BaseAIState CurrentState { get; }

		/// <summary>
		/// Changes the AI state, optionally providing targets for attacking states.
		/// </summary>
		/// <param name="newState">The new state to transition to.</param>
		/// <param name="targets">Optional list of targets for attacking states.</param>
		void ChangeState(BaseAIState newState, List<ICharacter> targets = null);

		/// <summary>
		/// Transitions to the idle state.
		/// </summary>
		void TransitionToIdleState();

		/// <summary>
		/// Transitions to a random movement state from the available movement states.
		/// </summary>
		void TransitionToRandomMovementState();
	}
}