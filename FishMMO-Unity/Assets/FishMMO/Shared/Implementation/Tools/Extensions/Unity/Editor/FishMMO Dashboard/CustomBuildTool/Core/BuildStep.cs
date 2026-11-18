#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using FishMMO.Logging;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>When in a build a <see cref="BuildStep"/> runs.</summary>
	public enum BuildStepPhase
	{
		/// <summary>Before any addressable is registered: generate and bake content (art, tables, maps). A failure stops the build.</summary>
		PrepareContent = 0,
		/// <summary>After content is ready, before the addressables are built: register or reorganise addressable entries. A failure stops the build.</summary>
		RegisterContent = 1,
		/// <summary>After the build, whether it succeeded or not: undo what the earlier phases changed in the project. A failure is logged, and the next step still runs.</summary>
		Cleanup = 2,
	}

	/// <summary>What a <see cref="BuildStep"/> is told about the build it runs in.</summary>
	public sealed class BuildStepContext
	{
		/// <summary>Client or server.</summary>
		public CustomBuildType BuildType;

		/// <summary>The platform being built.</summary>
		public BuildTarget Target;

		/// <summary>The addressable group name parts this build leaves out.</summary>
		public string[] ExcludedAddressableGroups;

		/// <summary>Whether this is a client build.</summary>
		public bool IsClient => BuildType == CustomBuildType.Client;
	}

	/// <summary>
	/// One step the build runs around the addressables and player build: a bake before, a registration, or a
	/// cleanup after. Found by <see cref="BuildSteps"/> through the TypeCache, so a module adds its own steps
	/// and the build tool never names a feature.
	/// </summary>
	/// <remarks>
	/// A concrete step needs a public parameterless constructor. Steps run in <see cref="Phase"/> order, then
	/// <see cref="Order"/>, then <see cref="Name"/>; a step that only applies to one kind of build returns
	/// early from <see cref="Run"/>. A step that wants a failure to be a warning rather than stop the build
	/// catches it itself, as the bakes do.
	/// </remarks>
	public abstract class BuildStep
	{
		/// <summary>The name logged when the step runs.</summary>
		public abstract string Name { get; }

		/// <summary>When the step runs.</summary>
		public abstract BuildStepPhase Phase { get; }

		/// <summary>Position within its phase; lower runs first.</summary>
		public virtual int Order => 0;

		/// <summary>Does the step's work.</summary>
		public abstract void Run(BuildStepContext context);
	}

	/// <summary>Finds every <see cref="BuildStep"/> and runs them a phase at a time.</summary>
	public static class BuildSteps
	{
		private static List<BuildStep> steps;

		/// <summary>Every step, in the order they run.</summary>
		public static IReadOnlyList<BuildStep> All
		{
			get
			{
				if (steps == null)
				{
					steps = new List<BuildStep>();
					foreach (Type type in TypeCache.GetTypesDerivedFrom<BuildStep>())
					{
						if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
						{
							continue;
						}
						steps.Add((BuildStep)Activator.CreateInstance(type));
					}
					steps.Sort((a, b) =>
					{
						int cmp = a.Phase.CompareTo(b.Phase);
						if (cmp != 0) return cmp;
						cmp = a.Order.CompareTo(b.Order);
						return cmp != 0 ? cmp : string.CompareOrdinal(a.Name, b.Name);
					});
				}
				return steps;
			}
		}

		/// <summary>
		/// Runs every step of <paramref name="phase"/> in order. In <see cref="BuildStepPhase.Cleanup"/> a failing
		/// step is logged and the rest still run; in the other phases a failure propagates and stops the build.
		/// </summary>
		public static void Run(BuildStepPhase phase, BuildStepContext context)
		{
			foreach (BuildStep step in All)
			{
				if (step.Phase != phase)
				{
					continue;
				}
				if (phase != BuildStepPhase.Cleanup)
				{
					Log.Debug("BuildLogger", $"Build step: {step.Name}");
					step.Run(context);
					continue;
				}
				try
				{
					step.Run(context);
				}
				catch (Exception ex)
				{
					Log.Warning("BuildLogger", $"Cleanup step '{step.Name}' failed; the project may need tidying by hand: {ex.Message}");
				}
			}
		}
	}
}
#endif
