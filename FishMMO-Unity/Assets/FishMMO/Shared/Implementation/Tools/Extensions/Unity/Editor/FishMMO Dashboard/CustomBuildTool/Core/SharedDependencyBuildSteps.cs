#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Collections.Generic;
using System;
using FishMMO.Logging;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using UnityEngine;

using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Stores once, in a build-time group, every asset two or more bundles of this build would each
	/// copy (<see cref="FishMMO.Shared.CustomBuildTool.Addressables.SharedDependencyBuildGroup"/>).
	/// Removed again by <see cref="RemoveSharedDependencyGroup"/> whether or not the build succeeds.
	/// </summary>
	internal sealed class SharedDependencyRegistrationStep : BuildStep
	{
		public override string Name => "Register shared dependencies";
		public override BuildStepPhase Phase => BuildStepPhase.RegisterContent;
		public override int Order => 1000;

		public override void Run(BuildStepContext context)
		{
			CustomBuildType customBuildType = context.BuildType;
			var log = new List<string>();
			FishMMO.Shared.CustomBuildTool.Addressables.SharedDependencyBuildGroup.Register(customBuildType == CustomBuildType.Server, log);
			foreach (string line in log)
			{
				Log.Info("BuildLogger", line);
			}
		
		}
	}

	/// <summary>Removes the shared dependencies' build-time group, leaving the project as it was found.</summary>
	internal sealed class SharedDependencyCleanupStep : BuildStep
	{
		public override string Name => "Remove the shared dependency build group";
		public override BuildStepPhase Phase => BuildStepPhase.Cleanup;
		public override int Order => 150;

		public override void Run(BuildStepContext context)
		{
			try
			{
				FishMMO.Shared.CustomBuildTool.Addressables.SharedDependencyBuildGroup.RemoveGroup();
			}
			catch (Exception ex)
			{
				Log.Warning("BuildLogger", $"Removing the '{FishMMO.Shared.CustomBuildTool.Addressables.SharedDependencyBuildGroup.GroupName}' addressable group failed; remove it by hand in the Addressables Groups window: {ex.Message}");
			}
		
		}
	}
}
#endif
