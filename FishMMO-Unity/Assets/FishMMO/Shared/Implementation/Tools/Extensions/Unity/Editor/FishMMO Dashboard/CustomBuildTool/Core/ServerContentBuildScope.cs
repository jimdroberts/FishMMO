#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Carries the requested content target while SBP temporarily uses the Player subtarget.
	/// Always dispose the scope, including when content or player building throws.
	/// </summary>
	public sealed class ServerContentBuildScope : IDisposable
	{
		private readonly bool previous;
		private readonly Func<IBuildParameters, IBuildResults, ReturnCode> previousScriptsCallback;
		private readonly Func<IBuildParameters, IDependencyData, IWriteData, ReturnCode> previousPackingCallback;
		private readonly BuildCallbacks callbacks;
		private readonly ServerRenderPipelineScope renderPipelines;
		private bool disposed;

		public static bool IsActive { get; private set; }

		public static bool ShouldStripRendering => IsActive ||
			(BuildPipeline.isBuildingPlayer &&
			 EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server);

		public ServerContentBuildScope(bool server)
		{
			previous = IsActive;
			IsActive = server;
			if (server)
			{
				try
				{
					renderPipelines = new ServerRenderPipelineScope();
					callbacks = ContentPipeline.BuildCallbacks;
					previousScriptsCallback = callbacks.PostScriptsCallbacks;
					previousPackingCallback = callbacks.PostPackingCallback;
					callbacks.PostScriptsCallbacks = ConfigureCache;
					callbacks.PostPackingCallback = StripPackedRendering;
				}
				catch
				{
					renderPipelines?.Dispose();
					IsActive = previous;
					throw;
				}
			}
		}

		private ReturnCode ConfigureCache(IBuildParameters parameters, IBuildResults results)
		{
			ReturnCode result = previousScriptsCallback?.Invoke(parameters, results) ?? ReturnCode.Success;
			// SBP's cache key has no Server/Player distinction under this workaround.
			// Bypass reads AND writes so server scene/shader stripping cannot reuse client
			// content or leave stripped cached bundles for the next client build.
			if (IsActive) parameters.UseCache = false;
			return result;
		}

		private ReturnCode StripPackedRendering(IBuildParameters parameters, IDependencyData dependencies, IWriteData writeData)
		{
			ReturnCode result = previousPackingCallback?.Invoke(parameters, dependencies, writeData) ?? ReturnCode.Success;
			if (result >= ReturnCode.Success && IsActive) ServerContentPacking.StripRendering(dependencies, writeData);
			return result;
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			try
			{
				if (callbacks != null)
				{
					callbacks.PostScriptsCallbacks = previousScriptsCallback;
					callbacks.PostPackingCallback = previousPackingCallback;
				}
				renderPipelines?.Dispose();
			}
			finally { IsActive = previous; }
		}
	}
}
#endif
