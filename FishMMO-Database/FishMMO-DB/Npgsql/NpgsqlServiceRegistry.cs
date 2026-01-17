using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Service registry for Npgsql-based database services.
	/// Automatically discovers and instantiates all services in the FishMMO.Database.Npgsql.Services namespace.
	/// Thread-safe singleton pattern with lazy initialization.
	/// Follows Single Responsibility Principle: solely responsible for service lifecycle management.
	/// </summary>
	public sealed class NpgsqlServiceRegistry : IDatabaseServiceRegistry
	{
		private readonly Dictionary<Type, object> services;
		private readonly object lockObject = new object();

		/// <inheritdoc/>
		public int ServiceCount
		{
			get
			{
				lock (lockObject)
				{
					return services.Count;
				}
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="NpgsqlServiceRegistry"/> class.
		/// Automatically discovers and registers all services in the Services namespace.
		/// </summary>
		/// <param name="dbContextFactory">The database context factory to inject into services.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		/// <exception cref="InvalidOperationException">Thrown when service instantiation fails.</exception>
		public NpgsqlServiceRegistry(INpgsqlDbContextFactory dbContextFactory)
		{
			if (dbContextFactory == null)
				throw new ArgumentNullException(nameof(dbContextFactory));

			services = new Dictionary<Type, object>();

			// Discover and register all services
			DiscoverAndRegisterServices(dbContextFactory);
		}

		/// <inheritdoc/>
		public bool TryGet<TService>(out TService service) where TService : class
		{
			lock (lockObject)
			{
				if (services.TryGetValue(typeof(TService), out var serviceInstance))
				{
					service = (serviceInstance as TService)!;
					return service != null;
				}

				service = null!;
				return false;
			}
		}

		/// <inheritdoc/>
		public bool TryGet(Type serviceType, out object service)
		{
			if (serviceType == null)
				throw new ArgumentNullException(nameof(serviceType));

			lock (lockObject)
			{
				return services.TryGetValue(serviceType, out service);
			}
		}

		/// <inheritdoc/>
		public bool IsRegistered<TService>() where TService : class
		{
			lock (lockObject)
			{
				return services.ContainsKey(typeof(TService));
			}
		}

		/// <inheritdoc/>
		public bool IsRegistered(Type serviceType)
		{
			if (serviceType == null)
				throw new ArgumentNullException(nameof(serviceType));

			lock (lockObject)
			{
				return services.ContainsKey(serviceType);
			}
		}

		/// <inheritdoc/>
		public Type[] GetRegisteredServiceTypes()
		{
			lock (lockObject)
			{
				return services.Keys.ToArray();
			}
		}

		/// <summary>
		/// Discovers all service interfaces and their implementations in the Services namespace.
		/// Automatically instantiates each service with the provided factory.
		/// </summary>
		/// <param name="dbContextFactory">The database context factory to inject into services.</param>
		/// <exception cref="InvalidOperationException">Thrown when service instantiation fails.</exception>
		private void DiscoverAndRegisterServices(INpgsqlDbContextFactory dbContextFactory)
		{
			var assembly = Assembly.GetExecutingAssembly();
			var serviceNamespace = "FishMMO.Database.Npgsql.Services";

			// Find all interfaces in the Services.Interfaces namespace that start with 'I'
			var interfaceTypes = assembly.GetTypes()
				.Where(t => t.IsInterface &&
							t.Namespace != null &&
							t.Namespace.StartsWith(serviceNamespace) &&
							t.Name.StartsWith("I") &&
							t.Name.EndsWith("Service"))
				.ToList();

			// Find all concrete service implementations
			var implementationTypes = assembly.GetTypes()
				.Where(t => t.IsClass &&
							!t.IsAbstract &&
							t.Namespace != null &&
							t.Namespace.StartsWith(serviceNamespace))
				.ToList();

			// Match interfaces to implementations and instantiate
			foreach (var interfaceType in interfaceTypes)
			{
				var implementationType = implementationTypes
					.FirstOrDefault(t => interfaceType.IsAssignableFrom(t));

				if (implementationType == null)
					continue;

				// Instantiate the service with the factory
				try
				{
					var serviceInstance = Activator.CreateInstance(implementationType, dbContextFactory);
					if (serviceInstance != null)
					{
						services[interfaceType] = serviceInstance;
					}
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException(
						$"Failed to instantiate service {implementationType.Name} for interface {interfaceType.Name}. " +
						$"Ensure the service has a constructor that accepts INpgsqlDbContextFactory. " +
						$"Inner exception: {ex.Message}",
						ex);
				}
			}
		}
	}
}