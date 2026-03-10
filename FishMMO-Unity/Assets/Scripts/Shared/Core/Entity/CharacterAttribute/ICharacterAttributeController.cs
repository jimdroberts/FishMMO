using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for character attribute controllers, providing access and management for all character attributes and resources.
	/// Used to read, modify, and synchronize attribute and resource values for a character.
	/// </summary>
	public interface ICharacterAttributeController : ICharacterBehaviour
	{
		/// <summary>
		/// Dictionary of all non-resource character attributes, keyed by template ID.
		/// </summary>
		Dictionary<int, CharacterAttribute> Attributes { get; }

		/// <summary>
		/// Dictionary of all resource character attributes (e.g., health, mana), keyed by template ID.
		/// </summary>
		Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get; }

		/// <summary>
		/// Sets the value and optional modifier of a non-resource attribute by template ID.
		/// </summary>
		/// <param name="id">Template ID of the attribute.</param>
		/// <param name="value">New value to set.</param>
		/// <param name="modifier">Optional modifier value to set.</param>
		void SetAttribute(int id, int value, int? modifier = null);

		/// <summary>
		/// Sets the value, current value, and optional modifier of a resource attribute by template ID.
		/// </summary>
		/// <param name="id">Template ID of the resource attribute.</param>
		/// <param name="value">New value to set.</param>
		/// <param name="currentValue">New current value to set.</param>
		/// <param name="modifier">Optional modifier value to set.</param>
		void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null);

		/// <summary>
		/// Tries to get a non-resource attribute by template reference.
		/// </summary>
		/// <param name="template">Attribute template to look up.</param>
		/// <param name="attribute">Out: found attribute instance.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute);

		/// <summary>
		/// Tries to get a non-resource attribute by template ID.
		/// </summary>
		/// <param name="id">Template ID to look up.</param>
		/// <param name="attribute">Out: found attribute instance.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetAttribute(int id, out CharacterAttribute attribute);

		/// <summary>
		/// Tries to get the health resource attribute for the character.
		/// </summary>
		/// <param name="health">Out: found health resource attribute.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetHealthAttribute(out CharacterResourceAttribute health);

		/// <summary>
		/// Tries to get the mana resource attribute for the character.
		/// </summary>
		/// <param name="mana">Out: found mana resource attribute.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetManaAttribute(out CharacterResourceAttribute mana);

		/// <summary>
		/// Tries to get the stamina resource attribute for the character.
		/// </summary>
		/// <param name="stamina">Out: found stamina resource attribute.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina);

		/// <summary>
		/// Tries to get a resource attribute by template reference.
		/// </summary>
		/// <param name="template">Resource attribute template to look up.</param>
		/// <param name="attribute">Out: found resource attribute instance.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute);

		/// <summary>
		/// Tries to get a resource attribute by template ID.
		/// </summary>
		/// <param name="id">Template ID to look up.</param>
		/// <param name="attribute">Out: found resource attribute instance.</param>
		/// <returns>True if found, false otherwise.</returns>
		bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute);

		/// <summary>
		/// Adds a new non-resource attribute instance to the controller.
		/// </summary>
		/// <param name="instance">Attribute instance to add.</param>
		void AddAttribute(CharacterAttribute instance);

		/// <summary>
		/// Regenerates resource attributes (e.g., health, mana) over time.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last regeneration tick.</param>
		void Regenerate(float deltaTime);

		/// <summary>
		/// Applies a resource state (health, mana, stamina, regen delta) to the controller.
		/// </summary>
		/// <param name="resourceState">Resource state to apply.</param>
		void ApplyResourceState(CharacterAttributeResourceState resourceState);

		/// <summary>
		/// Gets the current resource state (health, mana, stamina, regen delta) for the controller.
		/// </summary>
		/// <returns>Current resource state struct.</returns>
		CharacterAttributeResourceState GetResourceState();

		/// <summary>
		/// True while the attribute graph is propagating value changes.
		/// Notifications are deferred until propagation completes.
		/// </summary>
		bool IsPropagating { get; }

		/// <summary>
		/// Begins a propagation batch. While active, <see cref="EnqueueNotification"/>
		/// collects changed attributes instead of firing events immediately.
		/// Must be paired with <see cref="EndPropagation"/>.
		/// </summary>
		void BeginPropagation();

		/// <summary>
		/// Ends a propagation batch. If this is the outermost batch (depth returns to zero),
		/// fires all deferred <see cref="CharacterAttribute.OnAttributeUpdated"/> events.
		/// </summary>
		void EndPropagation();

		/// <summary>
		/// Enqueues an attribute for deferred notification during propagation.
		/// If not propagating, callers should invoke the event directly instead.
		/// </summary>
		/// <param name="attribute">The attribute to notify after propagation completes.</param>
		void EnqueueNotification(CharacterAttribute attribute);
	}
}