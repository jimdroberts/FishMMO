using System.Reflection;
using FishNet.Serializing;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// Writes a value and reads it back through FishNet's generic serializers, after registering the ones its codegen
	/// produced (2026-11-20: lifted out of the Commerce module's knowledge wire tests so every module's wire test shares it).
	/// </summary>
	public static class WireRoundTrip
	{
		/// <summary>True once the generated serializers have been registered for this process.</summary>
		private static bool serializersReady;
		private static string serializerFailure;

		/// <summary>Why the serializers could not be registered, or null.</summary>
		public static string SerializerFailure => serializerFailure;

		/// <summary>
		/// Registers the serializers FishNet's codegen produced, which in a build happens through
		/// <c>[RuntimeInitializeOnLoadMethod]</c> and under the test runner never happens at all.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Without this every <c>Writer.Write&lt;T&gt;</c> in a wire test returns default: the generic
		/// delegate behind it is null, exactly as it is in the play session the tests below describe.
		/// That is why this fixture reads the generated <c>InitializeOnce</c> rather than registering
		/// the hand-written methods itself — registering them by hand would assume the very thing
		/// under test, which is whether FishNet finds and uses them.
		/// </para>
		/// <para>
		/// FishNet emits one such class per assembly it processes, so all of them are run: the
		/// built-ins live in FishNet's own, and every game type lives in its own assembly's copy.
		/// </para>
		/// </remarks>
		public static void EnsureSerializers()
		{
			if (serializersReady || serializerFailure != null)
			{
				return;
			}

			int found = 0;
			try
			{
				foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
				{
					System.Type[] types;
					try
					{
						types = assembly.GetTypes();
					}
					catch (ReflectionTypeLoadException ex)
					{
						types = System.Array.FindAll(ex.Types, t => t != null);
					}

					foreach (System.Type type in types)
					{
						if (type.Name != "GeneratedWriters___Internal" &&
							type.Name != "GeneratedReaders___Internal")
						{
							continue;
						}

						MethodInfo initialize = type.GetMethod("InitializeOnce",
							BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
						if (initialize == null)
						{
							continue;
						}

						initialize.Invoke(null, null);
						++found;
					}
				}
			}
			catch (System.Exception ex)
			{
				serializerFailure = ex.ToString();
				return;
			}

			if (found == 0)
			{
				serializerFailure =
					"no generated serializer class was found in any loaded assembly — FishNet's weaver " +
					"did not run, so nothing in a wire test can be round-tripped and the tests below " +
					"would fail for a reason that has nothing to do with the game";
				return;
			}

			serializersReady = true;
		}

		/// <summary>Writes and reads a value through FishNet's generic serializers.</summary>
		public static T RoundTrip<T>(T value)
		{
			EnsureSerializers();
			LogAssert.IsNull(serializerFailure, serializerFailure);

			Writer writer = new Writer();
			writer.Write(value);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			return reader.Read<T>();
		}
	}
}
