using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Hosts the modules' render features (<see cref="ModuleRenderFeatures"/>) on a URP renderer: the World Simulation module's clouds, height fog, froxel fog and heat shimmer stood on the renderers
	/// themselves, so a project without the module had renderers with missing features. This core feature stands where they
	/// stood and draws whatever modules are installed, in their registered order; with none, it draws nothing.
	/// </summary>
	[DisallowMultipleRendererFeature("Fish Module Passes")]
	public class FishModulePassesFeature : ScriptableRendererFeature
	{
		[Tooltip("The renderer's quality tier: modules leave some of their work out of the Performant renderer.")]
		public RendererTier Tier = RendererTier.HighFidelity;

		private readonly List<ModuleRenderFeature> features = new List<ModuleRenderFeature>();
		private readonly List<string> pending = new List<string>();
		private int builtVersion = -1;

		/// <summary>The module features this host draws now, for diagnostics and the renderer checks.</summary>
		public IReadOnlyList<ModuleRenderFeature> Features => features;

		/// <summary>The first hosted feature of a type, or null.</summary>
		public T Find<T>() where T : ModuleRenderFeature
		{
			for (int i = 0; i < features.Count; i++)
			{
				if (features[i] is T found)
				{
					return found;
				}
			}
			return null;
		}

		public override void Create()
		{
			Build();
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			// A module registered, or its settings changed, since the last build, or a pending one's settings have loaded: make
			// them again (a pending one is only asked, not built, until it can be).
			if (builtVersion != ModuleRenderFeatures.Version || (pending.Count > 0 && AnyPendingReady()))
			{
				Build();
			}
			for (int i = 0; i < features.Count; i++)
			{
				if (features[i].isActive)
				{
					features[i].AddRenderPasses(renderer, ref renderingData);
				}
			}
		}

		private bool AnyPendingReady()
		{
			IReadOnlyList<ModuleRenderFeatures.Entry> entries = ModuleRenderFeatures.Entries;
			for (int i = 0; i < entries.Count; i++)
			{
				if (pending.Contains(entries[i].Id))
				{
					ModuleRenderFeature probe = entries[i].Create();
					if (probe != null)
					{
						probe.Dispose();
						return true;
					}
				}
			}
			return false;
		}

		private void Build()
		{
			Release();
			builtVersion = ModuleRenderFeatures.Version;
			IReadOnlyList<ModuleRenderFeatures.Entry> entries = ModuleRenderFeatures.Entries;
			for (int i = 0; i < entries.Count; i++)
			{
				ModuleRenderFeature feature = entries[i].Create();
				if (feature == null)
				{
					// Its settings are not loaded yet (the module's profile comes with the client's addressables): tried again.
					pending.Add(entries[i].Id);
					continue;
				}
				feature.host = this;
				feature.Create();
				features.Add(feature);
			}
		}

		private void Release()
		{
			for (int i = 0; i < features.Count; i++)
			{
				features[i].Dispose();
				features[i].host = null;
			}
			features.Clear();
			pending.Clear();
		}

		protected override void Dispose(bool disposing)
		{
			Release();
			builtVersion = -1;
		}
	}
}
