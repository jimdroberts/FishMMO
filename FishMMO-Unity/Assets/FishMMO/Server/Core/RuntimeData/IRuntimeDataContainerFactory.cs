using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Engine-agnostic factory for creating RuntimeDataContainer instances.
	/// Enables testability and decouples container creation from discovery.
	/// </summary>
	public interface IRuntimeDataContainerFactory
	{
		/// <summary>
		/// Creates an instance of the specified RuntimeDataContainer type.
		/// </summary>
		/// <param name="containerType">The concrete container type to instantiate.</param>
		/// <returns>The created container instance.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the type is not a valid container type.</exception>
		IRuntimeDataContainer CreateContainer(Type containerType);

		/// <summary>
		/// Validates that a type is a valid concrete RuntimeDataContainer.
		/// A valid container type must be:
		/// - Non-null
		/// - Not abstract
		/// - Not an interface
		/// - Assignable to IRuntimeDataContainer
		/// - Have a parameterless constructor
		/// </summary>
		/// <param name="type">The type to validate.</param>
		/// <returns>True if the type is a valid container type, false otherwise.</returns>
		bool IsValidContainerType(Type type);
	}
}