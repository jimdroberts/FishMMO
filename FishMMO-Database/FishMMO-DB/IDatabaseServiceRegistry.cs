using System;

namespace FishMMO.Database
{
	/// <summary>
	/// Defines a contract for database service registries.
	/// Enables automatic service discovery, registration, and retrieval for database services.
	/// Follows Interface Segregation Principle and Open/Closed Principle for extensibility.
	/// Can be implemented for different database types (Npgsql, Redis, MongoDB, etc.).
	/// </summary>
	public interface IDatabaseServiceRegistry
	{
		/// <summary>
		/// Gets the number of registered services in the registry.
		/// </summary>
		int ServiceCount { get; }

		/// <summary>
		/// Attempts to retrieve a service by its interface type.
		/// Type-safe generic method for compile-time type checking.
		/// </summary>
		/// <typeparam name="TService">The service interface type to retrieve.</typeparam>
		/// <param name="service">When this method returns, contains the service instance if found; otherwise, null.</param>
		/// <returns>True if the service was found; otherwise, false.</returns>
		bool TryGet<TService>(out TService service) where TService : class;

		/// <summary>
		/// Attempts to retrieve a service by its interface type.
		/// Runtime type resolution for dynamic service lookup.
		/// </summary>
		/// <param name="serviceType">The service interface type to retrieve.</param>
		/// <param name="service">When this method returns, contains the service instance if found; otherwise, null.</param>
		/// <returns>True if the service was found; otherwise, false.</returns>
		bool TryGet(Type serviceType, out object service);

		/// <summary>
		/// Determines whether a service is registered for the specified interface type.
		/// </summary>
		/// <typeparam name="TService">The service interface type to check.</typeparam>
		/// <returns>True if the service is registered; otherwise, false.</returns>
		bool IsRegistered<TService>() where TService : class;

		/// <summary>
		/// Determines whether a service is registered for the specified interface type.
		/// </summary>
		/// <param name="serviceType">The service interface type to check.</param>
		/// <returns>True if the service is registered; otherwise, false.</returns>
		bool IsRegistered(Type serviceType);

		/// <summary>
		/// Gets an array of all registered service interface types.
		/// </summary>
		/// <returns>An array containing all registered service types.</returns>
		Type[] GetRegisteredServiceTypes();
	}
}