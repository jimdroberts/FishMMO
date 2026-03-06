using System;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for ability controllers, managing ability activation and state.
	/// Inherits from IAbilityKnowledgeController for known ability/event management.
	/// Consumers that only need to query or modify known abilities should depend on
	/// IAbilityKnowledgeController instead for better interface segregation.
	/// </summary>
	public interface IAbilityController : IAbilityKnowledgeController
	{
		/// <summary>
		/// Event triggered to check if manipulation is allowed.
		/// </summary>
		event Func<bool> OnCanManipulate;

		/// <summary>
		/// Event triggered to update ability UI or state.
		/// </summary>
		event Action<string, float, float> OnUpdate;
		/// <summary>
		/// Event triggered when the current ability is interrupted.
		/// </summary>
		event Action OnInterrupt;
		/// <summary>
		/// Event triggered when the current ability is cancelled.
		/// </summary>
		event Action OnCancel;
		/// <summary>
		/// Event triggered to reset the UI.
		/// </summary>
		event Action OnReset;

		/// <summary>
		/// True if an ability is currently activating.
		/// </summary>
		bool IsActivating { get; }
		/// <summary>
		/// True if an ability is queued for activation.
		/// </summary>
		bool AbilityQueued { get; }

		/// <summary>
		/// Interrupts the current ability, optionally specifying the attacker.
		/// </summary>
		/// <param name="attacker">The character causing the interruption.</param>
		void Interrupt(ICharacter attacker);
		/// <summary>
		/// Activates an ability by reference ID and held state.
		/// </summary>
		/// <param name="referenceID">The ability reference ID.</param>
		/// <param name="isHeld">Whether the activation key is held.</param>
		void Activate(long referenceID, bool isHeld);
		/// <summary>
		/// Queues a consumable item for activation through the replicate pipeline.
		/// </summary>
		/// <param name="item">The consumable item to activate.</param>
		void ActivateConsumable(Item item);
		/// <summary>
		/// Returns true if manipulation is allowed.
		/// </summary>
		bool CanManipulate();
		/// <summary>
		/// Gets the current ability type.
		/// </summary>
		AbilityType GetCurrentAbilityType();
		/// <summary>
		/// Returns true if the current ability type is aerial.
		/// </summary>
		bool IsCurrentAbilityTypeAerial();
		/// <summary>
		/// Gets the activation attribute template for the given ability.
		/// </summary>
		/// <param name="ability">The ability to check.</param>
		CharacterAttributeTemplate GetActivationAttributeTemplate(Ability ability);
		/// <summary>
		/// Calculates the speed reduction for the given attribute.
		/// </summary>
		/// <param name="attribute">The attribute to check.</param>
		float CalculateSpeedReduction(CharacterAttributeTemplate attribute);

		/// <summary>
		/// Returns true if the ability requires held input (channeled or charged).
		/// AI should pass the result as the isHeld parameter when calling Activate().
		/// </summary>
		/// <param name="abilityID">The ability ID to check.</param>
		bool RequiresHeld(long abilityID);

		/// <summary>
		/// Releases the held state for the current ability. For charged abilities this
		/// triggers the release (fire). For channeled abilities this stops the channel early.
		/// </summary>
		void Release();

		/// <summary>
		/// The remaining activation time for the current ability, or 0 if no ability is active.
		/// </summary>
		float RemainingActivationTime { get; }
	}
}