using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace FishMMO.Shared
{
	/// <summary>
	/// A faster alternative to Activator.CreateInstance
	/// </summary>
	public static class FastActivator<TResult> where TResult : class
	{
		internal static object CreateDelegate(Type type, Type delegateType, Type[] argTypes)
		{
			ConstructorInfo? ctor = type.GetConstructor(argTypes);
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
			internal static readonly ActivatorDelegate Constructor = (ActivatorDelegate)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate), new Type[] { });
		}

		public static TResult CreateInstance<TArg>(TArg arg)
		{
			return FastActivatorImpl<TArg>.Constructor(arg);
		}

		internal static class FastActivatorImpl<TArg>
		{
			internal static readonly ActivatorDelegate<TArg> Constructor = (ActivatorDelegate<TArg>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg>), new Type[] { typeof(TArg), });
		}

		public static TResult CreateInstance<TArg1, TArg2>(TArg1 arg1, TArg2 arg2)
		{
			return FastActivatorImpl<TArg1, TArg2>.Constructor(arg1, arg2);
		}

		internal static class FastActivatorImpl<TArg1, TArg2>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2> Constructor = (ActivatorDelegate<TArg1, TArg2>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2>), new Type[] { typeof(TArg1), typeof(TArg2), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3>(TArg1 arg1, TArg2 arg2, TArg3 arg3)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3>.Constructor(arg1, arg2, arg3);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4>.Constructor(arg1, arg2, arg3, arg4);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5>.Constructor(arg1, arg2, arg3, arg4, arg5);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13) });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14) });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14, TArg15 arg15)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14), typeof(TArg15) });
		}

		public static TResult CreateInstance<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6, TArg7 arg7, TArg8 arg8, TArg9 arg9, TArg10 arg10, TArg11 arg11, TArg12 arg12, TArg13 arg13, TArg14 arg14, TArg15 arg15, TArg16 arg16)
		{
			return FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>.Constructor(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
		}

		internal static class FastActivatorImpl<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>
		{
			internal static readonly ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16> Constructor = (ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>)CreateDelegate(typeof(TResult), typeof(ActivatorDelegate<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9, TArg10, TArg11, TArg12, TArg13, TArg14, TArg15, TArg16>), new Type[] { typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5), typeof(TArg6), typeof(TArg7), typeof(TArg8), typeof(TArg9), typeof(TArg10), typeof(TArg11), typeof(TArg12), typeof(TArg13), typeof(TArg14), typeof(TArg15), typeof(TArg16) });
		}
	}
}