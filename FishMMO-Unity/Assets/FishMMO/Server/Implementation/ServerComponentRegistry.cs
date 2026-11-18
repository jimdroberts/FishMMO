using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Generic base implementation for server component registries.
	/// Handles registration, lookup, and lifecycle management of server components.
	/// Can be extended for specific component types (behaviours, data containers, etc.).
	/// </summary>
	/// <typeparam name="TNetworkManager">The network manager type.</typeparam>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	/// <typeparam name="TServerComponent">The base component type being managed.</typeparam>
	public abstract class ServerComponentRegistry<TNetworkManager, TConnection, TServerComponent> :
		IServerComponentRegistry<TNetworkManager, TConnection, TServerComponent>
		where TServerComponent : IServerComponent
	{
		/// <summary>
		/// Dictionary mapping component interface types and concrete types to their instances.
		/// </summary>
		protected Dictionary<Type, TServerComponent> components = new Dictionary<Type, TServerComponent>();

		/// <summary>
		/// Name of the registry for logging purposes. Override in derived classes.
		/// </summary>
		protected abstract string RegistryName { get; }

		/// <summary>
		/// Registers a component instance for global access.
		/// The component will be registered under every interface it implements that derives from TServerComponent,
		/// as well as under its concrete type.
		/// </summary>
		public virtual void Register<T>(T component) where T : class, TServerComponent
		{
			if (component == null)
				return;

			Type concreteType = component.GetType();

			// Register under concrete type if not already present
			if (!components.ContainsKey(concreteType))
			{
				components.Add(concreteType, component);
				Log.Debug(RegistryName, $"Registered {concreteType.Name}");
			}

			// Register under all TServerComponent interfaces implemented by this component
			var interfaces = concreteType.GetInterfaces();
			foreach (var iface in interfaces)
			{
				if (iface == typeof(TServerComponent))
				{
					continue;
				}
				if (!typeof(TServerComponent).IsAssignableFrom(iface))
				{
					continue;
				}
				if (!components.ContainsKey(iface))
				{
					components.Add(iface, component);
					Log.Debug(RegistryName, $"Registered interface {iface.Name} => {concreteType.Name}");
				}
			}
		}

		/// <summary>
		/// Unregisters a component instance from global access.
		/// </summary>
		public virtual void Unregister<T>(T component) where T : class, TServerComponent
		{
			if (component == null)
				return;

			Type concreteType = component.GetType();

			// Remove concrete type
			if (components.ContainsKey(concreteType))
			{
				components.Remove(concreteType);
				Log.Debug(RegistryName, $"Unregistered {concreteType.Name}");
			}

			// Remove any interface registrations pointing to this component
			var interfaces = concreteType.GetInterfaces();
			foreach (var iface in interfaces)
			{
				if (iface == typeof(TServerComponent))
				{
					continue;
				}
				if (!typeof(TServerComponent).IsAssignableFrom(iface))
				{
					continue;
				}
				if (components.TryGetValue(iface, out TServerComponent existing) && ReferenceEquals(existing, component))
				{
					components.Remove(iface);
					Log.Debug(RegistryName, $"Unregistered interface {iface.Name} => {concreteType.Name}");
				}
			}
		}

		/// <summary>
		/// Attempts to get a registered component of type T.
		/// </summary>
		public virtual bool TryGet<T>(out T component) where T : class, TServerComponent
		{
			Type type = typeof(T);

			if (components.TryGetValue(type, out TServerComponent result))
			{
				if ((component = result as T) != null)
				{
					return true;
				}
			}
			component = null;
			return false;
		}

		/// <summary>
		/// Gets a registered component of type T, or null if not found.
		/// </summary>
		public virtual T Get<T>() where T : class, TServerComponent
		{
			Type type = typeof(T);
			if (components.TryGetValue(type, out TServerComponent result))
			{
				return result as T;
			}
			return null;
		}

		/// <summary>
		/// Initializes all registered components. Must be implemented by derived classes
		/// to handle component-specific initialization logic.
		/// </summary>
		public abstract void InitializeAll(IServer server);

		/// <summary>
		/// Deinitializes all registered components. Must be implemented by derived classes
		/// to handle component-specific deinitialization logic.
		/// </summary>
		public abstract void DeinitializeAll();
	}
}