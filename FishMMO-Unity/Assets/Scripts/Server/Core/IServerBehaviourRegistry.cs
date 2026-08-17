using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Engine-agnostic interface for the server behaviour registry.
	/// Defines the contract for registering, retrieving, and managing server-side behaviours.
	/// Extends IServerComponentRegistry for unified component management.
	/// </summary>
	/// <typeparam name="TNetworkManager">The network manager type.</typeparam>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	/// <typeparam name="TBehaviour">The base type of server behaviour to be managed.</typeparam>
	public interface IServerBehaviourRegistry<TNetworkManager, TConnection, TBehaviour> :
		IServerComponentRegistry<TNetworkManager, TConnection, TBehaviour>
		where TBehaviour : IServerBehaviour
	{
		// Inherits all methods from IServerComponentRegistry:
		// - void Register<T>(T behaviour)
		// - void Unregister<T>(T behaviour)
		// - bool TryGet<T>(out T control)
		// - T Get<T>()
		// - void InitializeAll(IServer<TNetworkManager, TConnection, TBehaviour> server)
		// - void DeinitializeAll()

		/// <summary>
		/// Initializes all registered behaviours without blocking the caller's thread, one at a
		/// time in registration order so a behaviour may depend on state published by an earlier
		/// one. This is the entry point the server startup chain uses: behaviour initialization
		/// performs I/O, and the Unity main thread must stay free to drain the continuations that
		/// I/O depends on.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="cancellationToken">Cancelled when the server shuts down mid-startup.</param>
		/// <returns>
		/// The behaviours that failed, with their status. Empty when every behaviour initialized.
		/// </returns>
		Task<IReadOnlyList<(string Name, ServerComponentInitializationStatus Status)>> InitializeAllAsync(
			IServer server,
			CancellationToken cancellationToken);
	}
}