using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for System.Type, providing high-performance instance creation via 
	/// compiled expression trees and thread-safe delegate caching.
	/// </summary>
	public static class TypeExtensions
	{
		/// <summary>
		/// Caches compiled delegates for default constructors. 
		/// ConcurrentDictionary provides lock-free reads for high-frequency access.
		/// </summary>
		private static readonly ConcurrentDictionary<Type, Func<object?>> ConstructorCache =
			new ConcurrentDictionary<Type, Func<object?>>();

		/// <summary>
		/// Creates an instance of the specified type and casts it to T.
		/// </summary>
		/// <typeparam name="T">The type to cast the instance to.</typeparam>
		/// <param name="type">The type to instantiate.</param>
		/// <returns>A new instance cast to T, or null if creation fails.</returns>
		public static T? CreateInstance<T>(this Type? type) where T : class
		{
			var del = type.GetDefaultConstructorDelegate();
			return del?.Invoke() as T;
		}

		/// <summary>
		/// Creates an instance of the specified type.
		/// </summary>
		/// <param name="type">The type to instantiate.</param>
		/// <returns>A new instance of the type.</returns>
		public static object? CreateInstance(this Type? type)
		{
			var del = type.GetDefaultConstructorDelegate();
			return del?.Invoke();
		}

		/// <summary>
		/// Gets or creates a compiled delegate for the parameterless constructor of the specified type.
		/// </summary>
		/// <param name="type">The type to instantiate.</param>
		/// <returns>A delegate that creates an instance, or null if the type cannot be instantiated.</returns>
		public static Func<object?>? GetDefaultConstructorDelegate(this Type? type)
		{
			if (type == null) return null;

			// GetOrAdd is thread-safe and more efficient than manual locking for this use case
			return ConstructorCache.GetOrAdd(type, t =>
			{
				if (t.IsAbstract || t.IsInterface)
				{
					return () => null;
				}

				try
				{
					// Compile the "new T()" call into a reusable delegate
					NewExpression newExp;
					if (t.IsValueType)
					{
						newExp = Expression.New(t);
					}
					else
					{
						var constructorInfo = t.GetConstructor(Type.EmptyTypes);
						if (constructorInfo == null)
						{
							return () => null;
						}

						newExp = Expression.New(constructorInfo);
					}

					// Box the result to object so the delegate is universal
					Expression<Func<object?>> lambda = Expression.Lambda<Func<object?>>(
						Expression.Convert(newExp, typeof(object))
					);

					return lambda.Compile();
				}
				catch
				{
					// Return a null-returning delegate if no parameterless constructor exists
					return () => null;
				}
			});
		}
	}
}