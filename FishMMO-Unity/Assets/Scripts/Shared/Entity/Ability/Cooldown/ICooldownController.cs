using System;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for a cooldown controller, which manages ability cooldowns for a character.
	/// </summary>
	public interface ICooldownController : ICharacterBehaviour
	{
		/// <summary>
		/// Event invoked when a cooldown is added.
		/// </summary>
		static Action<long, CooldownInstance> OnAddCooldown;

		/// <summary>
		/// Event invoked when a cooldown is updated.
		/// </summary>
		static Action<long, CooldownInstance> OnUpdateCooldown;

		/// <summary>
		/// Event invoked when a cooldown is removed.
		/// </summary>
		static Action<long> OnRemoveCooldown;

		/// <summary>
		/// Reads cooldown data from a network reader.
		/// </summary>
		/// <param name="reader">The network reader.</param>
		void Read(Reader reader);

		/// <summary>
		/// Writes cooldown data to a network writer.
		/// </summary>
		/// <param name="writer">The network writer.</param>
		void Write(Writer writer);

		/// <summary>
		/// Updates cooldowns by the given delta time.
		/// </summary>
		/// <param name="deltaTime">Time to subtract from each cooldown.</param>
		void OnTick(float deltaTime);

		/// <summary>
		/// Checks if an ability is on cooldown.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <returns>True if on cooldown, otherwise false.</returns>
		bool IsOnCooldown(long id);

		/// <summary>
		/// Tries to get the remaining cooldown time for an ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="cooldown">Remaining cooldown time.</param>
		/// <returns>True if found, otherwise false.</returns>
		bool TryGetCooldown(long id, out float cooldown);

		/// <summary>
		/// Adds a cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="cooldown">Cooldown instance.</param>
		void AddCooldown(long id, CooldownInstance cooldown);

		/// <summary>
		/// Removes the cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		void RemoveCooldown(long id);

		/// <summary>
		/// Clears all cooldowns.
		/// </summary>
		void Clear();
	}
}