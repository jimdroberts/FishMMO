#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Collections.Generic;
using System;
using FishMMO.Logging;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using UnityEngine;

using FishMMO.Shared.CustomBuildTool;
using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Re-bakes every world scene's server-only spawn table before any addressables build, and
	/// refuses to build while a spawner would reach the shared scene bundle.
	/// </summary>
	/// <remarks>
	/// World scenes are one bundle for clients and servers alike. A spawner that is not tagged
	/// <c>EditorOnly</c>, or still carries a network object, ships its configuration to every
	/// player, so it stops the build rather than a warning nobody reads. Client builds bake too:
	/// the scene bundle is shared, and the tables must match the scenes it was built from.
	/// </remarks>
	/// <exception cref="InvalidOperationException">A spawner would ship to clients.</exception>
	internal sealed class SpawnTableBuildStep : BuildStep
	{
		public override string Name => "Bake spawn tables";
		public override BuildStepPhase Phase => BuildStepPhase.PrepareContent;
		public override int Order => 200;

		public override void Run(BuildStepContext context)
		{
			List<string> problems = new List<string>();
			List<string> blocking = new List<string>();

			Log.Debug("BuildLogger", "Baking spawn tables...");
			int baked = SpawnTableBaker.BakeAll(problems, blocking);

			for (int i = 0; i < problems.Count; ++i)
			{
				Log.Warning("BuildLogger", problems[i]);
			}

			if (blocking.Count > 0)
			{
				throw new InvalidOperationException($"{blocking.Count} spawner(s) would ship to clients; fix them before building:\n  " + string.Join("\n  ", blocking));
			}

			Log.Debug("BuildLogger", $"Baked {baked} spawner(s).");

			/* The tables and their catalogue register themselves as they are baked; the brain
			 * catalogue is written by other tools, so it is checked here. Server group only — no
			 * client build includes it. */
			AIBrainCatalogue brains = AIBrainCatalogueEditorUtility.Find();
			if (brains != null)
			{
				ServerAddressables.Register(AssetDatabase.GetAssetPath(brains));
			}
		
		}
	}
}
#endif
