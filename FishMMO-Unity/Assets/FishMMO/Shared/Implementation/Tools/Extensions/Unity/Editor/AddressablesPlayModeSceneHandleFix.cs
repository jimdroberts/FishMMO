using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Editor-only workaround for the "Attempting to use an invalid operation handle"
	/// exception Addressables throws out of its own Play Mode teardown.
	/// </summary>
	/// <remarks>
	/// <para><b>The bug.</b> <c>AddressablesImpl</c> tracks a loaded Addressable scene in two
	/// places: <c>OnSceneHandleCompleted</c> adds the handle to <c>m_SceneInstances</c> and
	/// also registers it in <c>m_resultToHandle</c>. <c>AddressablesImpl.Dispose()</c> then
	/// releases every handle in <c>m_resultToHandle</c>, and afterwards every handle in
	/// <c>m_SceneInstances</c> — with no <c>IsValid()</c> guard on either pass. The first pass
	/// drops a scene handle's reference count to zero, which destroys the operation and bumps
	/// its version; the second pass releases that same, now-stale, handle and
	/// <c>AsyncOperationHandle.InternalOp</c> throws. Any Addressable scene still loaded when
	/// Play Mode exits reproduces it, and the whole stack sits inside the Addressables package.
	/// </para>
	/// <para><b>Why our own shutdown cannot cover it.</b> <c>Addressables.Initialize()</c>
	/// subscribes <c>PlayModeStateChangedCleanup</c> from both <c>[InitializeOnLoadMethod]</c>
	/// and <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c>, long before any
	/// MonoBehaviour runs. <c>EditorApplication.playModeStateChanged</c> dispatches in
	/// subscription order, so <c>Dispose()</c> runs <i>before</i>
	/// <see cref="AddressableLoadProcessor.ReleaseAllAssets"/> ever sees ExitingPlayMode — by
	/// which point every handle it inspects is already invalid and its scene-unload loop is a
	/// no-op. Unloading scenes there cannot help anyway: scene unload is asynchronous and Play
	/// Mode exit gives it no frame to finish in.</para>
	/// <para><b>The ordering this relies on.</b> Subscribing from
	/// <c>[InitializeOnLoadMethod]</c> — which runs at domain load — puts this handler ahead of
	/// Addressables', because Addressables re-appends itself to the tail of the delegate at
	/// SubsystemRegistration every time Play Mode starts. That holds whether or not Enter Play
	/// Mode Options has domain reload disabled: with it disabled this subscription simply
	/// survives from the previous domain load, and Addressables still re-appends.</para>
	/// <para><b>What it does.</b> Empties <c>m_SceneInstances</c> before <c>Dispose()</c> runs.
	/// The scene handles are still reachable through <c>m_resultToHandle</c>, so Dispose's
	/// first pass releases each of them exactly once — correct teardown, minus the second,
	/// duplicate release. Nothing is skipped and nothing leaks.</para>
	/// <para>Editor-only and entirely reflective, so it adds no production code path. If a
	/// future Addressables version renames or removes either field the workaround reports it
	/// once and stands down, leaving the original (noisy, but harmless) teardown exception.
	/// </para>
	/// </remarks>
	internal static class AddressablesPlayModeSceneHandleFix
	{
		/// <summary>Private static <c>AddressablesImpl</c> backing field on <c>Addressables</c>.</summary>
		private const string ImplFieldName = "s_AddressablesImpl";
		/// <summary>Internal <c>HashSet&lt;AsyncOperationHandle&gt;</c> of live scene handles.</summary>
		private const string SceneInstancesFieldName = "m_SceneInstances";

		/// <summary>Resolved once per domain; null when the reflection lookup failed.</summary>
		private static FieldInfo implField;
		private static FieldInfo sceneInstancesField;

		/// <summary>True once the reflection lookup has been attempted for this domain.</summary>
		private static bool resolveAttempted;
		/// <summary>Guards the "package changed" warning so it is reported once, not per exit.</summary>
		private static bool warnedUnavailable;

		[InitializeOnLoadMethod]
		private static void Install()
		{
			// Detach first: with domain reload disabled this method runs again on top of a
			// subscription that is still attached from the previous session.
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			if (change != PlayModeStateChange.ExitingPlayMode)
			{
				return;
			}
			ClearSceneInstanceTracking();
		}

		/// <summary>
		/// Drops Addressables' duplicate scene-handle registry so its teardown releases each
		/// scene handle once instead of twice.
		/// </summary>
		private static void ClearSceneInstanceTracking()
		{
			if (!TryResolveFields())
			{
				return;
			}

			// Read the backing field rather than the Instance property: the property lazily
			// constructs a fresh AddressablesImpl when none exists, which would spin up a new
			// one during teardown purely to look at it.
			object impl = implField.GetValue(null);
			if (impl == null)
			{
				return;
			}

			if (sceneInstancesField.GetValue(impl) is HashSet<AsyncOperationHandle> sceneInstances &&
				sceneInstances.Count > 0)
			{
				sceneInstances.Clear();
			}
		}

		/// <summary>
		/// Resolves the two Addressables internals this depends on, once per domain.
		/// </summary>
		/// <returns>False when the package no longer exposes them, in which case the
		/// workaround permanently stands down for this domain.</returns>
		private static bool TryResolveFields()
		{
			if (!resolveAttempted)
			{
				resolveAttempted = true;

				implField = typeof(Addressables).GetField(ImplFieldName, BindingFlags.NonPublic | BindingFlags.Static);
				if (implField != null)
				{
					sceneInstancesField = implField.FieldType.GetField(SceneInstancesFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
				}
			}

			if (implField != null && sceneInstancesField != null)
			{
				return true;
			}

			if (!warnedUnavailable)
			{
				warnedUnavailable = true;
				Debug.LogWarning(
					"[AddressablesPlayModeSceneHandleFix] Could not find " +
					$"Addressables.{ImplFieldName} / AddressablesImpl.{SceneInstancesFieldName}. The Addressables package " +
					"has likely changed its internals. Exiting Play Mode with an Addressable scene loaded may log " +
					"'Attempting to use an invalid operation handle' from AddressablesImpl.Dispose; that exception is " +
					"harmless editor teardown noise.");
			}
			return false;
		}
	}
}