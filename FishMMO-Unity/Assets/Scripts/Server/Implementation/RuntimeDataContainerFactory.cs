using System;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Factory for creating RuntimeDataContainer instances via reflection.
	/// Validates container types and instantiates them using parameterless constructors.
	/// </summary>
	public class RuntimeDataContainerFactory : IRuntimeDataContainerFactory
	{
		/// <summary>
		/// Creates an instance of the specified RuntimeDataContainer type using reflection.
		/// </summary>
		/// <param name="containerType">The concrete container type to instantiate.</param>
		/// <returns>The created container instance.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the type is not a valid container type.</exception>
		public IRuntimeDataContainer CreateContainer(Type containerType)
		{
			if (!IsValidContainerType(containerType))
			{
				throw new InvalidOperationException(
					$"Type {containerType?.Name ?? "null"} is not a valid RuntimeDataContainer. " +
					"Container types must be non-abstract classes that implement IRuntimeDataContainer " +
					"and have a public parameterless constructor.");
			}

			return (IRuntimeDataContainer)Activator.CreateInstance(containerType);
		}

		/// <summary>
		/// Validates that a type is a valid concrete RuntimeDataContainer.
		/// </summary>
		/// <param name="type">The type to validate.</param>
		/// <returns>True if the type is a valid container type, false otherwise.</returns>
		public bool IsValidContainerType(Type type)
		{
			if (type == null)
				return false;

			if (type.IsAbstract || type.IsInterface)
				return false;

			if (!typeof(IRuntimeDataContainer).IsAssignableFrom(type))
				return false;

			// Check for parameterless constructor
			var constructor = type.GetConstructor(Type.EmptyTypes);
			if (constructor == null)
				return false;

			return true;
		}
	}
}
