using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>The renderer a module pass is drawn for: the pipeline asset's quality tier.</summary>
	public enum RendererTier
	{
		HighFidelity = 0,
		Balanced = 1,
		Performant = 2,
	}

	/// <summary>
	/// A module's render feature, hosted by the core's <see cref="FishModulePassesFeature"/> on every renderer (Phase 4 of the
	/// core and modules plan, 2026-11-18): the weather's clouds, height fog, froxel fog and heat shimmer. Written as a URP
	/// renderer feature is (Create, AddRenderPasses, Dispose), but plain C#: its settings live in the module's profile, so the
	/// core renderers carry no module script and a build without the module loads them cleanly.
	/// </summary>
	[Serializable]
	public abstract class ModuleRenderFeature : IDisposable
	{
		/// <summary>The renderer feature hosting this instance; null for the profile's template.</summary>
		[NonSerialized] internal FishModulePassesFeature host;

		/// <summary>The tier of the renderer it draws for (HighFidelity for the profile's template).</summary>
		public RendererTier Tier => host != null ? host.Tier : RendererTier.HighFidelity;

		/// <summary>Whether it draws on its tier; the Performant renderer leaves some out.</summary>
		protected virtual bool ActiveOnTier => true;

		/// <summary>Whether the hosting feature is on and this one draws on its tier (as a renderer feature's isActive).</summary>
		public bool isActive => host != null && host.isActive && ActiveOnTier;

		/// <summary>Its name, for logs.</summary>
		public string name => GetType().Name;

		/// <summary>Called when its host is created: make the passes here.</summary>
		public virtual void Create()
		{
		}

		/// <summary>Called for every camera its host's renderer draws: enqueue the passes here.</summary>
		public abstract void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData);

		/// <summary>Releases what <see cref="Create"/> made.</summary>
		protected virtual void Dispose(bool disposing)
		{
		}

		public void Dispose()
		{
			Dispose(true);
		}

		/// <summary>
		/// A fresh instance of <paramref name="template"/>'s type carrying its serialized settings (public fields and
		/// [SerializeField] ones, not [NonSerialized] state), for one host; null for a null template.
		/// </summary>
		public static T Instantiate<T>(T template) where T : ModuleRenderFeature
		{
			if (template == null)
			{
				return null;
			}
			var copy = (T)Activator.CreateInstance(template.GetType());
			for (Type type = template.GetType(); type != null && type != typeof(ModuleRenderFeature); type = type.BaseType)
			{
				foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					if (field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
					{
						continue;
					}
					if (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
					{
						field.SetValue(copy, field.GetValue(template));
					}
				}
			}
			return copy;
		}
	}

	/// <summary>The module render features, by id, in their drawing order (<see cref="ModuleRenderFeature"/>).</summary>
	public static class ModuleRenderFeatures
	{
		/// <summary>One registered feature: its id, order and how to make one (null until the module's settings are loaded).</summary>
		public readonly struct Entry
		{
			public readonly string Id;
			public readonly int Order;
			public readonly Func<ModuleRenderFeature> Create;

			public Entry(string id, int order, Func<ModuleRenderFeature> create)
			{
				Id = id;
				Order = order;
				Create = create;
			}
		}

		private static readonly List<Entry> entries = new List<Entry>();

		/// <summary>Bumped whenever the registrations or a module's settings change: the hosts remake their features.</summary>
		public static int Version { get; private set; }

		/// <summary>The registered features, by order.</summary>
		public static IReadOnlyList<Entry> Entries => entries;

		/// <summary>Registers a feature; registering an id again replaces it (a module's installer runs on every domain load).</summary>
		public static void Register(string id, int order, Func<ModuleRenderFeature> create)
		{
			if (string.IsNullOrEmpty(id) || create == null)
			{
				return;
			}
			entries.RemoveAll(e => e.Id == id);
			entries.Add(new Entry(id, order, create));
			entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
			Version++;
		}

		/// <summary>Removes a feature by id.</summary>
		public static void Unregister(string id)
		{
			if (entries.RemoveAll(e => e.Id == id) > 0)
			{
				Version++;
			}
		}

		/// <summary>Tells the hosts a module's settings changed (an edit to its profile): they remake their features.</summary>
		public static void Changed()
		{
			Version++;
		}
	}
}
