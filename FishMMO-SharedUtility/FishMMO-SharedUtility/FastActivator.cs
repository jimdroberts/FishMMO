using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace FishMMO.Shared
{
	/// <summary>
	/// A faster alternative to Activator.CreateInstance
	///
	/// The 17 constructor overloads (0 through 16 arguments) are intentionally
	/// copy-pasted rather than unified behind IReadOnlyList&lt;object?&gt;. This
	/// eliminates boxing/allocation of value-type arguments and avoids the
	/// per-call array or list allocation that a params-style API would require,
	/// keeping construction as close to raw &#x22;new&#x22; as possible.
	/// </summary>
	public static class FastActivator<TResult> where TResult : class
	{
		internal static object CreateDelegate(Type type, Type delegateType, Type[] argTypes)
		{
#pragma warning disable CS8632
			ConstructorInfo? ctor = type.GetConstructor(argTypes);
#pragma warning restore CS8632
			if (ctor == null)
			{
				throw new MissingMethodException($"Type {type.Name} does not have any matching public constructors.");
			}

			// Create parameters for the lambda expression
			ParameterExpression[] parameters = argTypes.Select(Expression.Parameter).ToArray();

			// Create the constructor call expression
			NewExpression newExp = Expression.New(ctor, parameters);

			// Create the lambda expression
			LambdaExpression lambda = Expression.Lambda(delegateType, newExp, parameters);

			// Compile the lambda expression into a delegate
			return lambda.Compile();
		}

		internal delegate TResult ActivatorDelegate();
		internal delegate TResult ActivatorDelegate<TArg>(TArg arg);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2>(TArg1 arg1, TArg2 arg2);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3>(TArg1 arg1, TArg2 arg2, TArg3 arg3);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14, TArg15 arg15);
		internal delegate TResult ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14, TArg15 arg15, TArg16 arg16);

		public static TResult CreateInstance()
		{
			return FastActivatorImpl.Constructor();
		}

		internal static class FastActivatorImpl
		{
			internal static readonly ActivatorDelegate Constructor = BuildConstructor();
			private static ActivatorDelegate BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(Type.EmptyTypes);
				if (ctor == null) return () => default;
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate), new Type[] { });
					}
					catch
					{
						// Expression.Compile() not supported on this platform (IL2CPP AOT, etc.), fall back to Activator.CreateInstance
					}
				}
				return () => (TResult?)Activator.CreateInstance(typeof(TResult));
			}
		}

		public static TResult CreateInstance<TArg>(TArg arg)
		{
			return FastActivatorImpl<TArg>.Constructor(arg);
		}

		internal static class FastActivatorImpl<TArg>
		{
			internal static readonly ActivatorDelegate<TArg> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg>), new Type[] { typeof(TArg), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg a) => (TResult?)ctor.Invoke(new object?[] { a });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2>(TArg1 arg1, TArg2 arg2)
		{
			return FastActivatorImpl<TArg1, TArg2>.Constructor(arg1, arg2);
		}

		internal static class FastActivatorImpl<TArg1, TArg2>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2>), new Type[] { typeof(TArg1), typeof(TArg2), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2) => (TResult?)ctor.Invoke(new object?[] { a1, a2 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3>(TArg1 arg1, TArg2 arg2, TArg3 arg3)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3>.Constructor(arg1, arg2, arg3);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4>.Constructor(arg1, arg2, arg3, arg4);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5>.Constructor(arg1, arg2, arg3, arg4, arg5);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10, TArg11 a11) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10, TArg11 a11, TArg12 a12) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13) });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10, TArg11 a11, TArg12 a12, TArg13 a13) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14) });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10, TArg11 a11, TArg12 a12, TArg13 a13, TArg14 a14) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14, TArg15 arg15)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14), typeof(TArg15) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14), typeof(TArg15) });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10, TArg11 a11, TArg12 a12, TArg13 a13, TArg14 a14, TArg15 a15) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15 });
			}
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14, TArg15 arg15, TArg16 arg16)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16> Constructor = BuildConstructor();
			private static ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16> BuildConstructor()
			{
				var ctor = typeof(TResult).GetConstructor(new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14), typeof(TArg15), typeof(TArg16) });
				if (ctor == null)
					throw new MissingMethodException($"Type {typeof(TResult).Name} does not have any matching public constructors.");
				if (TypeExtensions.UseExpressionCompilation)
				{
					try
					{
						return (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14), typeof(TArg15), typeof(TArg16) });
					}
					catch
					{
						// Expression.Compile() not supported, fall back to ConstructorInfo.Invoke
					}
				}
				return (TArg1 a1, TArg2 a2, TArg3 a3, TArg4 a4, TArg5 a5, TArg6 a6, TArg7 a7, TArg8 a8, TArg9 a9, TArg10 a10, TArg11 a11, TArg12 a12, TArg13 a13, TArg14 a14, TArg15 a15, TArg16 a16) => (TResult?)ctor.Invoke(new object?[] { a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16 });
			}
		}
	}
}
