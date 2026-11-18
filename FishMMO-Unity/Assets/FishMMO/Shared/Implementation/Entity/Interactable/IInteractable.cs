using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for interactable objects in the scene, providing interaction logic and UI display properties.
	/// Used for NPCs, objects, and other entities that players can interact with.
	/// </summary>
	public interface IInteractable : ISceneObject
	{
		/// <summary>
		/// The transform of the interactable object in the scene.
		/// </summary>
		Transform Transform { get; }

		/// <summary>
		/// The name of the interactable object.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// The display title for the interactable, shown in the UI.
		/// </summary>
		string Title { get; }

		/// <summary>
		/// The color of the title displayed for the interactable in the UI.
		/// </summary>
		Color TitleColor { get; }

		/// <summary>
		/// Returns true if the specified transform is within interaction range of this object.
		/// </summary>
		/// <param name="transform">The transform to check range against.</param>
		/// <returns>True if in range, false otherwise.</returns>
		bool InRange(Transform transform);

		/// <summary>
		/// Returns true if the specified player character may interact with this object.
		/// </summary>
		/// <remarks>
		/// A pure question with no side effects. It used to stamp the character's interact
		/// rate-limit as part of answering, which made it unusable as a check: the scene server,
		/// the quest system and the client's input handler all call it, and each call silently
		/// consumed a limiter meant for one of the others. A quest accepted within the limiter's
		/// window of the interaction that opened the dialogue was refused by a debounce nobody
		/// intended to spend, and the refusal path sends nothing back — so the player saw a dead
		/// keypress. Spend the limiter explicitly with
		/// <see cref="TryConsumeInteractRateLimit"/>.
		/// </remarks>
		/// <param name="character">The player character attempting to interact.</param>
		/// <returns>True if interaction is allowed, false otherwise.</returns>
		bool CanInteract(IPlayerCharacter character);

		/// <summary>
		/// Minimum milliseconds between interactions with this object from one character.
		/// </summary>
		double InteractRateLimit { get; }

		/// <summary>
		/// Spends the character's interact rate-limit, returning false when it is still cooling.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="CanInteract"/> so a caller that only needs to validate — the
		/// quest system, which already holds its own ingress guard — does not consume a budget
		/// belonging to the interaction path.
		/// </remarks>
		/// <param name="character">The interacting character.</param>
		/// <returns>True when the limiter was free and has now been spent.</returns>
		bool TryConsumeInteractRateLimit(IPlayerCharacter character);

		/// <summary>
		/// ECA triggers invoked server-side when a player successfully interacts with this object.
		/// Configure these in the Inspector to define per-object interaction behaviour without code.
		/// </summary>
		List<Trigger> OnInteractTriggers { get; }

		/// <summary>
		/// Fires all <see cref="OnInteractTriggers"/> with the provided event data.
		/// Called by <see cref="FishMMO.Server.Implementation.World.SceneServer.Interactable.InteractableSystem"/>
		/// after server-side validation succeeds.
		/// </summary>
		/// <param name="eventData">Context for this interaction (initiator, interactable reference, etc.).</param>
		/// <returns>True when at least one trigger was fired.</returns>
		bool ExecuteOnInteract(EventData eventData);
	}
}