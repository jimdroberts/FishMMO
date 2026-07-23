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
		/// When false, uses Activator.CreateInstance/ConstructorInfo.Invoke instead of
		/// Expression.Compile(). Set this to false from the Unity host when running on
		/// IL2CPP (which does not support System.Reflection.Emit) or on AOT-restricted
		/// platforms where runtime code generation is unavailable.
		/// Defaults to true (Expression.Compile() path) for maximum throughput on JIT-capable
		/// runtimes.
		/// </summary>
		internal static bool UseExpressionCompilation { get; set; } = true;

		/// <summary>
		/// Caches compiled delegates for default constructors.
		/// ConcurrentDictionary provides lock-free reads for high-frequency access.
		/// </summary>
		private static readonly ConcurrentDictionary<Type, Func<object?>> constructorCache =
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
			return constructorCache.GetOrAdd(type, t =>
			{
				if (t.IsAbstract || t.IsInterface)
				{
					return () => null;
				}

				// Use the safe Activator.CreateInstance/ConstructorInfo.Invoke path when
				// expression compilation is disabled (e.g., IL2CPP, AOT platforms where
				// Expression.Compile() would throw PlatformNotSupportedException).
				if (!UseExpressionCompilation)
				{
					try
					{
						if (t.IsValueType)
						{
							return () => Activator.CreateInstance(t);
						}

						var constructorInfo = t.GetConstructor(Type.EmptyTypes);
						if (constructorInfo == null)
						{
							return () => null;
						}

						return () => constructorInfo.Invoke(null);
					}
					catch
					{
						return () => null;
					}
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
				catch (MissingMethodException)
				{
					// Return a null-returning delegate if no parameterless constructor exists
					return () => null;
				}
				catch (ArgumentException)
				{
					// Return a null-returning delegate if constructor arguments are invalid
					return () => null;
				}
			});
		}
	}
}
