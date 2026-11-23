using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Ability crafting on a scene server: a player at an ability crafter combines a known ability template with ability
	/// events into a new ability, paid for in currency.
	/// </summary>
	/// <remarks>
	/// The Ability Crafting module. This was a partial of <c>InteractableSystem</c>; it is its own
	/// system now, with its own request guard, so a game without crafting leaves the module out. Ability crafters placed
	/// in the world stay in the core and their ECA action opens the window; abilities themselves (learning, the grant
	/// funnel <c>AbilitySystem.TryGrantAbility</c>, prediction) are the core's.
	/// </remarks>
	[CreateAssetMenu(fileName = "AbilityCraftingSystem", menuName = "FishMMO/Server/SceneServer/Ability Crafting System", order = 1)]
	[RequiresDataContainer(typeof(AbilityCraftingSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class AbilityCraftingSystem : ServerBehaviour
	{
		[Header("Request Protection")]
		[Tooltip("Minimum milliseconds between two craft requests from one connection.")]
		[SerializeField] private int interactionDebounceMilliseconds = 1000;
		[Tooltip("Seconds between sweeps of stale request-guard entries.")]
		[SerializeField] private float debounceSweepIntervalSeconds = 5.0f;
		[Tooltip("Seconds before a request-guard entry is stale.")]
		[SerializeField] private float debounceEntryTtlSeconds = 30.0f;
		[Tooltip("Most stale request-guard entries removed per sweep.")]
		[SerializeField] private int debounceSweepMaxRemovals = 128;

		/// <summary>The world scenes, to validate the scene a request is made in (the same asset the interactable system uses).</summary>
		[Header("World")]
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>The currency attribute crafting is paid in.</summary>
		[Header("Economy")]
		[SerializeField] private CharacterAttributeTemplate currencyTemplate;

		/// <summary>Most crafted abilities a character may know.</summary>
		[SerializeField] [Min(1)] private int maxAbilityCount = 25;

		/// <summary>Most ability events one craft request may name; a defence against oversized payloads.</summary>
		[SerializeField] [Min(1)] private int maxAbilityCraftEvents = 32;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("AbilityCraftingSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("AbilityCraftingSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IAbilityCraftingSystemRuntimeData>(out _))
			{
				Log.Error("AbilityCraftingSystem", "InitializeOnce: its runtime data is missing");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			Server.NetworkWrapper.RegisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived, true);

			interactionDebounceMilliseconds = Mathf.Max(0, interactionDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);

			Log.Debug("AbilityCraftingSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}
			if (Server.DataContainerRegistry.TryGet<IAbilityCraftingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
			Server.NetworkWrapper.UnregisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived);
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<IAbilityCraftingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(debounceSweepIntervalSeconds, debounceEntryTtlSeconds, debounceSweepMaxRemovals);
			}
		}

		/// <summary>Acquires the request guard for the shared operation (0) at the system's debounce.</summary>
		private bool TryBeginIngressGuard(int connectionId, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IAbilityCraftingSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, 0, interactionDebounceMilliseconds, out guardKey);
		}

		/// <summary>Releases the request guard.</summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IAbilityCraftingSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// The scene object with <paramref name="sceneObjectID"/>, when it exists, is live (not pooled) and stands in the
		/// character's scene (the same check the interactable system makes: a pooled object keeps its id).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateSceneObject(long sceneObjectID, SceneHandle characterSceneHandle, out ISceneObject sceneObject)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out sceneObject))
			{
				Log.Debug("AbilityCraftingSystem", $"Missing SceneObject ID:{sceneObjectID}");
				return false;
			}
			GameObject gameObject = sceneObject.GameObject;
			if (gameObject == null || !gameObject.activeInHierarchy)
			{
				Log.Debug("AbilityCraftingSystem", $"SceneObject ID:{sceneObjectID} is despawned.");
				sceneObject = null;
				return false;
			}
			if (gameObject.scene.handle != characterSceneHandle)
			{
				Log.Debug("AbilityCraftingSystem", "Object scene mismatch.");
				sceneObject = null;
				return false;
			}
			return true;
		}
	}
}
