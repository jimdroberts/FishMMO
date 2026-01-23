using System;
using System.Collections.Concurrent;
using System.Linq;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Service registry for Npgsql-based database services.
	/// Explicitly registers and owns the lifecycle of all Npgsql services.
	/// Thread-safe singleton-style registry (constructed once and then used concurrently).
	/// </summary>
	/// <remarks>
	/// Uses ConcurrentDictionary for lock-free reads, providing excellent performance
	/// under high concurrency. Thread-safe for all operations.
	/// </remarks>
	public sealed class NpgsqlServiceRegistry : IDatabaseServiceRegistry
	{
		private readonly ConcurrentDictionary<Type, object> services;

		/// <inheritdoc/>
		public int ServiceCount => services.Count;

		/// <summary>
		/// Initializes a new instance of the <see cref="NpgsqlServiceRegistry"/> class.
		/// Services must be registered explicitly by the composition root.
		/// </summary>
		public NpgsqlServiceRegistry()
		{
			services = new ConcurrentDictionary<Type, object>();
		}

		/// <inheritdoc/>
		public bool TryGet<TService>(out TService service) where TService : class
		{
			if (services.TryGetValue(typeof(TService), out var serviceInstance))
			{
				service = (serviceInstance as TService)!;
				return service != null;
			}

			service = null!;
			return false;
		}

		/// <inheritdoc/>
		public bool TryGet(Type serviceType, out object service)
		{
			if (serviceType == null)
				throw new ArgumentNullException(nameof(serviceType));

			return services.TryGetValue(serviceType, out service);
		}

		/// <inheritdoc/>
		public bool IsRegistered<TService>() where TService : class
		{
			return services.ContainsKey(typeof(TService));
		}

		/// <inheritdoc/>
		public bool IsRegistered(Type serviceType)
		{
			if (serviceType == null)
				throw new ArgumentNullException(nameof(serviceType));

			return services.ContainsKey(serviceType);
		}

		/// <inheritdoc/>
		public Type[] GetRegisteredServiceTypes()
		{
			return services.Keys.ToArray();
		}

		internal void Register<TService>(TService serviceInstance) where TService : class
		{
			if (serviceInstance == null)
				throw new ArgumentNullException(nameof(serviceInstance));

			if (!services.TryAdd(typeof(TService), serviceInstance))
			{
				throw new InvalidOperationException($"Service already registered: {typeof(TService).FullName}");
			}
		}
	}
}