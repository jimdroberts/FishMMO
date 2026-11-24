using System;
using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// No <see cref="Interactable"/> subclass declares an <c>Awake</c> or an <c>OnDestroy</c> of its own.
	/// </summary>
	/// <remarks>
	/// Unity calls only the most derived <c>Awake</c>, and a subclass's private one silently replaces
	/// <see cref="Interactable"/>'s: the base never caches its transform or squares its range, so <c>InRange</c>
	/// refuses every player and the server drops the interaction at a Debug-level log line. That was every housing
	/// deed post in the world (the Housing module's PlotFoundation). A subclass uses <see cref="Interactable.OnAwake"/>
	/// and overrides <c>OnDestroy</c> (protected virtual) instead. Reflection over every loaded assembly, so the
	/// modules' interactables are covered wherever they live; FishNet's generated <c>Awake</c> is told apart from a
	/// written one (<see cref="DeclaresOwnAwake"/>).
	/// </remarks>
	[TestFixture]
	public class InteractableUnityMessageTests
	{
		private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		[Test]
		public void NoInteractable_DeclaresItsOwnAwake_OrHidesOnDestroy()
		{
			var offenders = new List<string>();
			int checkedTypes = 0;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException ex)
				{
					types = Array.FindAll(ex.Types, t => t != null);
				}
				foreach (Type type in types)
				{
					if (type == typeof(Interactable) || !typeof(Interactable).IsAssignableFrom(type))
					{
						continue;
					}
					checkedTypes++;
					if (DeclaresOwnAwake(type))
					{
						offenders.Add($"{type.FullName} declares Awake (use OnAwake)");
					}
					MethodInfo destroy = type.GetMethod("OnDestroy", Declared, null, Type.EmptyTypes, null);
					if (destroy != null && destroy.GetBaseDefinition().DeclaringType != typeof(Interactable))
					{
						offenders.Add($"{type.FullName} declares its own OnDestroy (override Interactable.OnDestroy)");
					}
				}
			}

			Assert.That(checkedTypes, Is.GreaterThan(0), "No Interactable subclass was loaded; the scan proved nothing.");
			Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
		}

		/// <summary>
		/// Whether the type's own source declares an <c>Awake</c>.
		/// </summary>
		/// <remarks>
		/// Not simply "has a declared <c>Awake</c>": FishNet's IL post-processor gives every NetworkBehaviour it processes a
		/// generated <c>public virtual Awake</c> that calls the base one, so in an editor-compiled assembly every interactable
		/// has one. A processed type (it declares <c>NetworkInitialize___Early</c>) shows a user-written <c>Awake</c> by the
		/// copy the processor moves its body into, <c>Awake_UserLogic_&lt;type&gt;_&lt;module&gt;</c>; an unprocessed one (an
		/// assembly compiled outside Unity) by the declared <c>Awake</c> itself.
		/// </remarks>
		internal static bool DeclaresOwnAwake(Type type)
		{
			MethodInfo[] methods = type.GetMethods(Declared);
			bool processed = Array.Exists(methods, m => m.Name == "NetworkInitialize___Early");
			if (!processed)
			{
				return type.GetMethod("Awake", Declared, null, Type.EmptyTypes, null) != null;
			}
			string userLogic = "Awake_UserLogic_" + type.FullName + "_";
			return Array.Exists(methods, m => m.Name.StartsWith(userLogic, StringComparison.Ordinal));
		}

		[Test]
		public void TheGeneratedAwakeIsNotMistakenForAUserOne()
		{
			// The base interactable writes its own Awake; every subclass in the core leaves it alone.
			Assert.IsTrue(DeclaresOwnAwake(typeof(Interactable)), "Interactable's own Awake is seen, processed or not");
			Assert.IsFalse(DeclaresOwnAwake(typeof(Teleporter)), "a subclass with only the generated Awake is not an offender");
		}
	}
}
