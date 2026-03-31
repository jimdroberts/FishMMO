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
		/// Returns true if the specified player character can interact with this object.
		/// </summary>
		/// <param name="character">The player character attempting to interact.</param>
		/// <returns>True if interaction is allowed, false otherwise.</returns>
		bool CanInteract(IPlayerCharacter character);

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
		void ExecuteOnInteract(EventData eventData);
	}
}