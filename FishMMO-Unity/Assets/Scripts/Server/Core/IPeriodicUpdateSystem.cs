using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Provides periodic callback registration and dispatch functionality.
	/// Allows components to register callbacks that execute at specified intervals.
	/// </summary>
	public interface IPeriodicUpdateSystem
	{
		/// <summary>
		/// Registers a callback to be invoked periodically at the specified interval.
		/// </summary>
		/// <param name="interval">Time in seconds between callback invocations.</param>
		/// <param name="callback">The callback to invoke. Receives delta time since last invocation.</param>
		void RegisterPeriodicCallback(float interval, Action<float> callback);

		/// <summary>
		/// Unregisters a previously registered periodic callback.
		/// </summary>
		/// <param name="callback">The callback to unregister.</param>
		void UnregisterPeriodicCallback(Action<float> callback);

		/// <summary>
		/// Updates the interval for an existing periodic callback.
		/// </summary>
		/// <param name="callback">The callback whose interval to update.</param>
		/// <param name="newInterval">The new interval in seconds.</param>
		void UpdateCallbackInterval(Action<float> callback, float newInterval);
	}
}