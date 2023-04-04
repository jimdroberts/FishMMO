using System;
using System.Collections.Generic;
using System.Linq.Expressions;
public static class TypeExtensions
{
	internal static readonly object Lock = new object();
	internal static Dictionary<Type, Func<object>> Constructors = new Dictionary<Type, Func<object>>();

	/// <summary>
	/// Creates an instance of Type and casts it to T.
	/// </summary>
	public static T CreateInstance<T>(this Type type) where T : class
	{
		return type.GetDefaultConstructorDelegate()() as T;
	}

	/// <summary>
	/// Creates an instance of Type.
	/// </summary>
	public static object CreateInstance(this Type type)
	{
		return type.GetDefaultConstructorDelegate()();
	}

	/// <summary>
	/// Thread safe function to get the default constructor of Type. Returns null if the Type is not a class or value type.
	/// </summary>
	public static Func<object> GetDefaultConstructorDelegate(this Type type)
	{
		if (type.IsValueType || type.IsClass)
		{
			lock (Lock)
			{
				Func<object> constructor;
				if (Constructors.TryGetValue(type, out constructor))
				{
					NewExpression expression = Expression.New(type);
					Expression<Func<object>> lambda = Expression.Lambda<Func<object>>(expression);
					constructor = lambda.Compile();
					Constructors.Add(type, constructor);
				}
				return constructor;
			}
		}
		return null;
	}
}