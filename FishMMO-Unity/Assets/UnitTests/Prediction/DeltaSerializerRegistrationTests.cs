using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Fails the build when a prediction type has no delta serializer pair.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Issue #159 was a type with no delta serializer, and on the 4.6.12 fork the only thing that
	/// reported it was a per-tick log line — 14,442 of them in four minutes, each with a stack
	/// trace. FishNet's delta prediction (<c>FISHNET_DELTA_PREDICTION</c>) instead writes a type
	/// without BOTH a delta writer and a delta reader with its regular serializer, silently and
	/// correctly — which is exactly why an omission now costs bandwidth with nothing to say so.
	/// This fixture is what reports it: the omission fails here, before it ships, and names the type.
	/// </para>
	/// <para>
	/// Discovered by reflection rather than from a hand-maintained list, because a list would have
	/// to be updated by the same person who forgot the serializer. Adding a <c>[Replicate]</c> or
	/// <c>[Reconcile]</c> method is enough to be covered.
	/// </para>
	/// <para>
	/// Keyed on those attributes rather than on the <see cref="IReplicateData"/> interface, because
	/// implementing the interface is not what puts a type on the wire. <c>KCCInputReplicateData</c>
	/// and <c>AbilityActivationReplicateData</c> both implement it and neither is ever replicated —
	/// they are input DTOs that get folded into <c>CharacterReplicateData</c> before it is sent, so
	/// they never reach a delta writer. Asserting on the interface flags both as missing and would
	/// have been answered by writing two serializers nothing calls.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class DeltaSerializerRegistrationTests
	{
		/// <summary>
		/// Runs every <c>RegisterSerializers</c> hook in the FishMMO assemblies.
		/// </summary>
		/// <remarks>
		/// They are <c>[RuntimeInitializeOnLoadMethod]</c>, which does not fire in EditMode tests —
		/// without this the registries are empty and every assertion below would pass vacuously.
		/// </remarks>
		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			int invoked = 0;
			foreach (Type type in FishMMOTypes())
			{
				MethodInfo register = type.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

				if (register == null || register.GetParameters().Length != 0)
				{
					continue;
				}

				register.Invoke(null, null);
				invoked++;
			}

			LogAssert.IsTrue(invoked > 0,
				"No RegisterSerializers hooks were found; this fixture would pass vacuously.");
		}

		[Test]
		public void EveryReplicateAndReconcileType_HasADeltaWriterAndReader()
		{
			List<Type> predictionTypes = WireTypes().ToList();

			LogAssert.IsTrue(predictionTypes.Count > 0,
				"No [Replicate]/[Reconcile] methods were discovered; the reflection filter is wrong.");

			List<string> missing = new List<string>();
			foreach (Type type in predictionTypes)
			{
				if (RegistryValue(typeof(GenericDeltaWriter<>), type, "Write") == null)
				{
					missing.Add($"{type.FullName}: no delta WRITER registered");
				}

				if (RegistryValue(typeof(GenericDeltaReader<>), type, "Read") == null)
				{
					missing.Add($"{type.FullName}: no delta READER registered");
				}
			}

			LogAssert.IsTrue(missing.Count == 0,
				"Every [Replicate]/[Reconcile] payload type needs a delta writer AND reader, or FishNet "
				+ "sends it with the full serializer on every tick (issue #159 was the same omission). Missing:\n  "
				+ string.Join("\n  ", missing)
				+ $"\nChecked {predictionTypes.Count} type(s).");
		}

		/// <summary>Used only by the registration-order test below, so its static registry starts empty.</summary>
		private struct DeltaFirstProbe
		{
			public int Value;
		}

		/// <summary>
		/// A custom delta serializer must survive a custom regular serializer registered after it.
		/// </summary>
		/// <remarks>
		/// <c>GenericWriter&lt;T&gt;.SetWrite</c> / <c>GenericReader&lt;T&gt;.SetRead</c> tested
		/// <c>HasCustomSerializer</c> without the negation, so registering the regular serializer
		/// second nulled the CUSTOM delta serializer instead of a generated one, and the delta's
		/// <c>HasCustomSerializer</c> then refused it a second time. Every FishMMO hook registers
		/// regular-then-delta, which is all that masked it, and <c>[RuntimeInitializeOnLoadMethod]</c>
		/// callbacks have no defined order across classes. Fixed in FishNet by feat/delta-prediction-beta
		/// (PR #1095, commit 5); this pins it so a FishNet update that loses the fix fails here.
		/// </remarks>
		[Test]
		public void CustomDeltaSerializer_SurvivesARegularSerializerRegisteredAfterIt()
		{
			Func<Writer, DeltaFirstProbe, DeltaFirstProbe, DeltaSerializerOption, bool> deltaWrite = (writer, prev, next, option) => true;
			Func<Reader, DeltaFirstProbe, DeltaFirstProbe> deltaRead = (reader, prev) => prev;

			GenericDeltaWriter<DeltaFirstProbe>.SetWrite(deltaWrite);
			GenericDeltaReader<DeltaFirstProbe>.SetRead(deltaRead);
			GenericWriter<DeltaFirstProbe>.SetWrite((writer, value) => writer.WriteInt32(value.Value));
			GenericReader<DeltaFirstProbe>.SetRead(reader => new DeltaFirstProbe { Value = reader.ReadInt32() });

			LogAssert.IsTrue(GenericDeltaWriter<DeltaFirstProbe>.Write == deltaWrite,
				"Registering the regular writer after the custom delta writer discarded the delta writer.");
			LogAssert.IsTrue(GenericDeltaReader<DeltaFirstProbe>.Read == deltaRead,
				"Registering the regular reader after the custom delta reader discarded the delta reader.");
		}

		/// <summary>
		/// The distinct payload types of every <c>[Replicate]</c> and <c>[Reconcile]</c> method —
		/// that is, exactly the types FishNet asks a delta serializer for.
		/// </summary>
		private static IEnumerable<Type> WireTypes()
		{
			HashSet<Type> seen = new HashSet<Type>();

			foreach (Type type in FishMMOTypes())
			{
				MethodInfo[] methods;
				try
				{
					methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static
						| BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				}
				catch (TypeLoadException)
				{
					continue;
				}

				foreach (MethodInfo method in methods)
				{
					bool isPredictionMethod =
						method.GetCustomAttribute<ReplicateAttribute>() != null
						|| method.GetCustomAttribute<ReconcileAttribute>() != null;

					if (!isPredictionMethod)
					{
						continue;
					}

					ParameterInfo[] parameters = method.GetParameters();
					LogAssert.IsTrue(parameters.Length > 0,
						$"{type.FullName}.{method.Name} is marked prediction but takes no payload.");

					// The payload is always the first parameter; the rest are state and channel.
					if (seen.Add(parameters[0].ParameterType))
					{
						yield return parameters[0].ParameterType;
					}
				}
			}
		}

		/// <summary>Reads a static member off a closed generic registry such as GenericDeltaWriter&lt;T&gt;.</summary>
		private static object RegistryValue(Type openRegistry, Type argument, string memberName)
		{
			Type closed = openRegistry.MakeGenericType(argument);

			PropertyInfo property = closed.GetProperty(memberName,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null)
			{
				return property.GetValue(null);
			}

			FieldInfo field = closed.GetField(memberName,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, $"{openRegistry.Name} has no member '{memberName}'.");
			return field.GetValue(null);
		}

		/// <summary>Every type in the FishMMO-authored assemblies.</summary>
		private static IEnumerable<Type> FishMMOTypes()
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				string name = assembly.GetName().Name;
				if (!name.StartsWith("FishMMO", StringComparison.Ordinal))
				{
					continue;
				}

				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException ex)
				{
					// A partially loadable assembly still tells us about the types that did load.
					types = ex.Types.Where(t => t != null).ToArray();
				}

				foreach (Type type in types)
				{
					yield return type;
				}
			}
		}
	}
}
