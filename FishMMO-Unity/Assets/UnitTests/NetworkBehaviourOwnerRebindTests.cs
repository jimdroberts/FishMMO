using System.Reflection;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that FishNet's editor validation rebinds a NetworkBehaviour whose cached owner
	/// points at a NetworkObject outside its own hierarchy.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Stock FishNet returns the cached <c>_addedNetworkObject</c> whenever it is non-null and
	/// never touches <c>_networkObjectCache</c> in the editor at all. Paste Component Values,
	/// <c>EditorUtility.CopySerialized</c> and SerializedObject migrations copy both hidden fields
	/// verbatim between prefabs, so one paste left three NPC prefabs owned by the orc mage for
	/// five months (PR #212). The tagged edit in <c>NetworkBehaviour.TryAddNetworkObject</c>
	/// rejects an owner that is not on this transform's ancestor chain and keeps the runtime
	/// cache on the discovered owner. These tests plant the fault the way a paste does, bypassing
	/// validation, and then invoke OnValidate exactly as Unity would.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NetworkBehaviourOwnerRebindTests
	{
		private static readonly FieldInfo AddedField =
			typeof(NetworkBehaviour).GetField("_addedNetworkObject", BindingFlags.NonPublic | BindingFlags.Instance);
		private static readonly FieldInfo CacheField =
			typeof(NetworkBehaviour).GetField("_networkObjectCache", BindingFlags.NonPublic | BindingFlags.Instance);
		private static readonly MethodInfo OnValidate =
			typeof(NetworkBehaviour).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

		private GameObject own;
		private GameObject other;
		private NetworkObject ownNob;
		private NetworkObject otherNob;

		[SetUp]
		public void SetUp()
		{
			LogAssert.IsNotNull(AddedField, "_addedNetworkObject must exist on NetworkBehaviour");
			LogAssert.IsNotNull(CacheField, "_networkObjectCache must exist on NetworkBehaviour");
			LogAssert.IsNotNull(OnValidate, "OnValidate must exist on NetworkBehaviour");

			other = new GameObject("other asset");
			otherNob = other.AddComponent<NetworkObject>();
			own = new GameObject("own asset");
			ownNob = own.AddComponent<NetworkObject>();
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(own);
			Object.DestroyImmediate(other);
		}

		/// <summary>Writes both hidden fields directly, which is what a paste does.</summary>
		private static void Plant(NetworkBehaviour nb, NetworkObject owner)
		{
			AddedField.SetValue(nb, owner);
			CacheField.SetValue(nb, owner);
		}

		private static NetworkObject Added(NetworkBehaviour nb) => (NetworkObject)AddedField.GetValue(nb);

		[Test]
		public void OnValidateRebindsAnOwnerFromAnotherHierarchy()
		{
			NetworkBehaviour nb = own.AddComponent<EmptyNetworkBehaviour>();
			Plant(nb, otherNob);
			LogAssert.AreSame(otherNob, nb.NetworkObject, "precondition: the fault is planted");

			OnValidate.Invoke(nb, null);

			LogAssert.AreSame(ownNob, Added(nb), "_addedNetworkObject rebinds to the owner in this hierarchy");
			LogAssert.AreSame(ownNob, nb.NetworkObject, "_networkObjectCache follows it");
		}

		[Test]
		public void OnValidateRebindsAChildBehaviourToItsAncestor()
		{
			GameObject child = new GameObject("limb");
			child.transform.SetParent(own.transform);
			NetworkBehaviour nb = child.AddComponent<EmptyNetworkBehaviour>();
			Plant(nb, otherNob);

			OnValidate.Invoke(nb, null);

			LogAssert.AreSame(ownNob, Added(nb), "an ancestor's NetworkObject is the owner");
			LogAssert.AreSame(ownNob, nb.NetworkObject, "cache matches");
		}

		[Test]
		public void OnValidateKeepsAHealthyOwner()
		{
			NetworkBehaviour nb = own.AddComponent<EmptyNetworkBehaviour>();
			Plant(nb, ownNob);

			OnValidate.Invoke(nb, null);

			LogAssert.AreSame(ownNob, Added(nb), "a local owner is untouched");
			LogAssert.AreSame(ownNob, nb.NetworkObject, "cache untouched");
		}

		[Test]
		public void OnValidateFillsAMissingCacheFromTheOwner()
		{
			NetworkBehaviour nb = own.AddComponent<EmptyNetworkBehaviour>();
			AddedField.SetValue(nb, ownNob);
			CacheField.SetValue(nb, null);

			OnValidate.Invoke(nb, null);

			LogAssert.AreSame(ownNob, nb.NetworkObject, "a null cache is filled from the discovered owner");
		}
	}
}
