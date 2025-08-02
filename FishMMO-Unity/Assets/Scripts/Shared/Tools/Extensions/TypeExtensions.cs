using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for System.Type, providing fast instance creation and constructor caching.
	/// </summary>
	public static class TypeExtensions
	{
		/// <summary>
		/// Lock object for thread-safe access to the constructor cache.
		/// </summary>
		internal static readonly object Lock = new object();

		/// <summary>
		/// Caches compiled delegates for default constructors by type.
		/// </summary>
		internal static Dictionary<Type, Func<object>> Constructors = new Dictionary<Type, Func<object>>();

		/// <summary>
		/// Creates an instance of the specified type and casts it to T.
		/// </summary>
		/// <typeparam name="T">The type to cast the instance to.</typeparam>
		/// <param name="type">The type to instantiate.</param>
		/// <returns>A new instance of type T, or null if creation fails.</returns>
		public static T CreateInstance<T>(this Type type) where T : class
		{
			return type.GetDefaultConstructorDelegate()() as T;
		}

		/// <summary>
		/// Creates an instance of the specified type.
		/// </summary>
		/// <param name="type">The type to instantiate.</param>
		/// <returns>A new instance of the type, or null if creation fails.</returns>
		public static object CreateInstance(this Type type)
		{
			return type.GetDefaultConstructorDelegate()();
		}

		/// <summary>
		/// Gets a compiled delegate for the default constructor of the specified type.
		/// Thread-safe. Returns null if the type is not a class or value type.
		/// </summary>
		/// <param name="type">The type to get the constructor for.</param>
		/// <returns>A delegate that creates a new instance of the type, or null if not available.</returns>
		public static Func<object> GetDefaultConstructorDelegate(this Type type)
		{
			if (type.IsValueType || type.IsClass)
			{
				lock (Lock)
				{
					Func<object> constructor;
					// If the constructor is already cached, return it
					if (Constructors.TryGetValue(type, out constructor))
					{
						return constructor;
					}
					// Otherwise, compile and cache the constructor delegate
					NewExpression expression = Expression.New(type);
					Expression<Func<object>> lambda = Expression.Lambda<Func<object>>(expression);
					constructor = lambda.Compile();
					Constructors.Add(type, constructor);
					return constructor;
				}
			}
			return null;
		}
	}
}