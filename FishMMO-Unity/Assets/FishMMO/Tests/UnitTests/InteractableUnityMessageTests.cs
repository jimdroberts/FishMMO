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
	/// modules' interactables are covered wherever they live.
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
					if (type.GetMethod("Awake", Declared, null, Type.EmptyTypes, null) != null)
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
	}
}
