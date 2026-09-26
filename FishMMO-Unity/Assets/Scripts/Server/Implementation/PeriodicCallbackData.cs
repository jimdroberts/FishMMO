using System;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Internal data structure for tracking periodic callback state.
	/// </summary>
	public class PeriodicCallbackData
	{
		/// <summary>
		/// The interval in seconds between callback invocations.
		/// </summary>
		public float Interval;

		/// <summary>
		/// Time remaining in seconds until the next callback invocation.
		/// </summary>
		public float TimeRemaining;

		/// <summary>
		/// Real time in seconds since the callback last ran (or was registered). Passed to the
		/// callback, then reset.
		/// </summary>
		public float Elapsed;

		/// <summary>
		/// Fault log, created the first time the callback throws.
		/// </summary>
		public FishMMO.Server.Core.RepeatingFaultLog Faults;

		/// <summary>
		/// The callback to invoke when the interval elapses.
		/// </summary>
		public Action<float> Callback;

		/// <summary>
		/// Cached display name for logging, avoiding repeated reflection on
		/// <c>Callback.Method.DeclaringType</c> and <c>Callback.Method.Name</c>.
		/// </summary>
		public readonly string CallbackName;

		/// <summary>
		/// Initializes a new instance of PeriodicCallbackData.
		/// </summary>
		/// <param name="interval">The interval in seconds.</param>
		/// <param name="callback">The callback to invoke.</param>
		public PeriodicCallbackData(float interval, Action<float> callback)
		{
			Interval = interval;
			TimeRemaining = interval;
			Callback = callback;
			CallbackName = $"{callback.Method.DeclaringType?.Name}.{callback.Method.Name}";
		}
	}
}