using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>Handles a signal of type <typeparamref name="T"/>.</summary>
	public delegate void SignalHandler<T>(World world, in T signal) where T : struct;

	/// <summary>
	/// Facts delivered at once, to every module that listens, at the moment they are published: the other half of
	/// a world's events. <see cref="EventStream{T}"/> carries what systems read on the next pass through a
	/// phase; a signal is for a fact whose consequence must not wait for a tick, such as progress that has to be
	/// in place before the character it belongs to is saved and leaves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Neither side names the other: the publisher states a fact, listeners decide what it means. A listener
	/// that throws is reported to <see cref="HandlerFailed"/> and the rest still run, so one module's fault
	/// does not undo another's work.
	/// </para>
	/// <para>
	/// Not thread-safe, like the world. A listener may publish further signals; they are delivered at once,
	/// inside the publish that caused them.
	/// </para>
	/// </remarks>
	public sealed class Signals
	{
		private readonly Dictionary<Type, object> handlers = new Dictionary<Type, object>();

		/// <summary>Told when a listener throws, with the signal's type and the exception. Null to ignore.</summary>
		public Action<Type, Exception> HandlerFailed { get; set; }

		/// <summary>How many listeners have thrown since the world began.</summary>
		public long Failures { get; private set; }

		/// <summary>Listens for signals of type <typeparamref name="T"/>.</summary>
		public void Subscribe<T>(SignalHandler<T> handler) where T : struct
		{
			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}
			List<SignalHandler<T>> list = ListOf<T>();
			list.Add(handler);
		}

		/// <summary>Stops listening.</summary>
		public bool Unsubscribe<T>(SignalHandler<T> handler) where T : struct => ListOf<T>().Remove(handler);

		/// <summary>Whether anything listens for <typeparamref name="T"/>.</summary>
		public bool HasListeners<T>() where T : struct =>
			handlers.TryGetValue(typeof(T), out object list) && ((List<SignalHandler<T>>)list).Count > 0;

		/// <summary>Delivers <paramref name="signal"/> to every listener now, in the order they subscribed.</summary>
		public void Publish<T>(World world, in T signal) where T : struct
		{
			if (!handlers.TryGetValue(typeof(T), out object found))
			{
				return;
			}
			var list = (List<SignalHandler<T>>)found;
			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					list[i](world, signal);
				}
				catch (Exception ex)
				{
					Failures++;
					HandlerFailed?.Invoke(typeof(T), ex);
				}
			}
		}

		private List<SignalHandler<T>> ListOf<T>() where T : struct
		{
			if (!handlers.TryGetValue(typeof(T), out object list))
			{
				list = new List<SignalHandler<T>>();
				handlers[typeof(T)] = list;
			}
			return (List<SignalHandler<T>>)list;
		}
	}

	/// <summary>Publishing signals on a <see cref="World"/>.</summary>
	public static class SignalWorld
	{
		/// <summary>Delivers <paramref name="signal"/> to every listener now (<see cref="Signals.Publish{T}"/>).</summary>
		public static void Publish<T>(this World world, in T signal) where T : struct => world.Resource<Signals>().Publish(world, signal);
	}
}
