using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the two send-side decisions that shape what an observer receives from a
	/// NetworkTransform: the owner is not sent updates it would discard, and the viewer-cap
	/// interval and the distance-LOD interval compose by <b>max</b> rather than stacking.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Owner exclusion.</b> A server-authoritative NetworkTransform with SendToOwner off used
	/// to send its ObserversRpc to the owner every tick; the owner's handler returned on its first
	/// line. That is ~630 B/s per moving player for nothing. The fix is a virtual on
	/// NetworkBehaviour that NetworkTransform overrides, consulted where FishNet builds the RPC
	/// exclusion list (FISHMMO EDIT in <c>NetworkBehaviour.SendObserversRpc</c>). Half of it is
	/// asserted through the live property, the other half at source level because the send path
	/// needs a spawned object.
	/// </para>
	/// <para>
	/// <b>Composition.</b> <c>NetworkTransformDistanceLod</c> used to set the transform's own
	/// interval while <c>ObserverStreamingEntry</c> gated per observer, so an observer beyond the
	/// cap only heard from an object when both modulo gates coincided — for intervals 3 and 4 that
	/// is once in twelve ticks, and for an unlucky phase never. Now both are per observer and the
	/// entry takes the larger, so the observer hears exactly once per max(N, M) ticks.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObserverSendShapingTests
	{
		private static NetworkConnection Connection(int clientId) => new NetworkConnection { ClientId = clientId };

		private static void SetPrivate(object target, string field, object value)
		{
			FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(f, $"{target.GetType().Name}.{field} must exist.");
			f.SetValue(target, value);
		}

		[Test]
		public void NetworkTransform_ExcludesItsOwner_OnlyWhenServerAuthoritativeWithoutSendToOwner()
		{
			GameObject go = new GameObject("OwnerExclusionProbe");
			try
			{
				FishNet.Component.Transforming.NetworkTransform nt =
					go.AddComponent<FishNet.Component.Transforming.NetworkTransform>();

				// The authored configuration on every player prefab.
				SetPrivate(nt, "_clientAuthoritative", false);
				SetPrivate(nt, "_sendToOwner", false);
				LogAssert.IsTrue(nt.ExcludeOwnerFromUnbufferedObserversRpcs,
					"A server-authoritative transform that does not send to its owner must exclude the owner " +
					"at the send, not discard on receipt.");

				// The owner explicitly wants updates (a spectator camera on its own character).
				SetPrivate(nt, "_sendToOwner", true);
				LogAssert.IsFalse(nt.ExcludeOwnerFromUnbufferedObserversRpcs,
					"SendToOwner on must keep the owner in the send.");

				// Client authoritative: the owner is the source and is excluded by the RPC itself,
				// but this hook must not claim it (the relay path has its own rules).
				SetPrivate(nt, "_clientAuthoritative", true);
				SetPrivate(nt, "_sendToOwner", false);
				LogAssert.IsFalse(nt.ExcludeOwnerFromUnbufferedObserversRpcs,
					"A client-authoritative transform must not use the server-authoritative owner exclusion.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void OtherBehaviours_DoNotExcludeTheOwner()
		{
			GameObject go = new GameObject("DefaultExclusionProbe");
			try
			{
				NetworkBehaviour other = go.AddComponent<NetworkTransformDistanceLod>();
				LogAssert.IsFalse(other.ExcludeOwnerFromUnbufferedObserversRpcs,
					"The default must be false: only a behaviour whose owner provably discards the RPC may opt in, " +
					"or the owner silently stops receiving things it needs.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void SendObserversRpc_ConsultsTheOwnerExclusion_ForUnbufferedSendsOnly()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(),
				"Assets/Plugins/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.RPCs.cs");
			LogAssert.IsTrue(File.Exists(path), $"Vendored FishNet file not found at {path}.");

			string source = File.ReadAllText(path);
			int send = source.IndexOf("internal void SendObserversRpc(", StringComparison.Ordinal);
			LogAssert.IsTrue(send >= 0, "SendObserversRpc must exist.");
			int next = source.IndexOf("internal void SendTargetRpc(", send, StringComparison.Ordinal);
			string body = source.Substring(send, next > send ? next - send : source.Length - send);

			LogAssert.IsTrue(body.Contains("!bufferLast && !excludeOwner && ExcludeOwnerFromUnbufferedObserversRpcs && Owner.IsValid"),
				"SendObserversRpc must add the owner to the exclusion list when the behaviour opts in, " +
				"guarded on !bufferLast (the owner needs buffered interval / SendToOwner changes) and " +
				"Owner.IsValid (an owner that disconnected mid-tick).");
			int exclusion = body.IndexOf("ExcludeOwnerFromUnbufferedObserversRpcs", StringComparison.Ordinal);
			int sendToClients = body.IndexOf("SendToClients(", StringComparison.Ordinal);
			LogAssert.IsTrue(exclusion >= 0 && sendToClients > exclusion,
				"The owner exclusion must be added before the packet is handed to the transport.");
		}

		[Test]
		public void Entry_ComposesCapAndDistanceIntervals_ByMax_NeverByStacking()
		{
			GameObject go = new GameObject("ComposeProbe");
			try
			{
				NetworkObject nob = go.AddComponent<NetworkObject>();
				NetworkTransformDistanceLod lod = go.AddComponent<NetworkTransformDistanceLod>();
				ObserverStreamingEntry entry = new ObserverStreamingEntry(nob, new MockCharacter(1), lod);
				LogAssert.IsTrue(entry.HasDistanceLod, "The entry must see the LOD on its own object.");

				NetworkConnection viewer = Connection(7);

				/* Both directions, because the composition is a max and a max has to be proved from
				 * either side. The numbers moved when the far band was capped at the interpolation
				 * buffer's width (see NetworkTransformLodBufferTests): the coarsest band is 2 now,
				 * so the cap has to be set BELOW it to let the distance side win. */
				lod.BandObserver(viewer.ClientId, 100f * 100f); // coarsest band
				byte distanceInterval = lod.GetInterval(viewer);
				LogAssert.IsTrue(distanceInterval > 1, "The 100 m observer must be throttled by distance at all.");

				// Cap unlimited, distance throttled: distance wins.
				entry.SetInterval(viewer, 1);
				LogAssert.AreEqual(distanceInterval, entry.GetEffectiveInterval(viewer),
					"With no cap in force the distance interval must decide.");

				// Cap coarser than distance: the cap wins, and the two must not multiply.
				entry.SetInterval(viewer, 8);
				LogAssert.AreEqual(8, entry.GetEffectiveInterval(viewer), "The larger interval wins in the other direction too.");
				LogAssert.IsTrue(entry.GetEffectiveInterval(viewer) < 8 * distanceInterval,
					"Composing by multiplication is the failure this test exists to catch.");

				// Under the old design (NT interval N × per-observer gate M) an observer heard once
				// in N×M ticks at best. Count what it hears now over a long window.
				const int window = 240;
				int heard = 0;
				byte effective = entry.GetEffectiveInterval(viewer);
				for (uint tick = 0; tick < window; tick++)
				{
					if (ObserverStreamingPolicy.ShouldSendThisTick(tick, effective, viewer.ClientId))
					{
						heard++;
					}
				}
				TestContext.WriteLine($"MEASURE composed interval {effective}: {heard} sends in {window} ticks " +
					$"(stacked 8×6 would have been {window / 48})");
				LogAssert.AreEqual(window / effective, heard,
					"An observer limited by both policies must hear exactly once per max(N, M) ticks.");

				// A near, in-cap observer is untouched by either.
				NetworkConnection near = Connection(8);
				lod.BandObserver(near.ClientId, 3f * 3f);
				LogAssert.AreEqual(1, entry.GetEffectiveInterval(near), "An in-cap, near observer stays at full rate.");
				LogAssert.IsTrue(entry.ShouldSend(nob, near, Channel.Unreliable), "Full rate means every send.");

				// Reliable is never shaped, whatever the intervals say.
				LogAssert.IsTrue(entry.ShouldSend(nob, viewer, Channel.Reliable),
					"The reliable settle after a stop must reach the most-limited observer too.");
			}
			finally
			{
				ObserverStreamingRegistry.Clear();
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Entry_WithoutADistanceLod_UsesTheCapIntervalAlone()
		{
			GameObject go = new GameObject("NoLodProbe");
			try
			{
				NetworkObject nob = go.AddComponent<NetworkObject>();
				ObserverStreamingEntry entry = new ObserverStreamingEntry(nob, new MockCharacter(2), null);
				LogAssert.IsFalse(entry.HasDistanceLod, "No LOD was attached.");

				NetworkConnection viewer = Connection(9);
				LogAssert.AreEqual(1, entry.GetEffectiveInterval(viewer), "Nothing limits an unlisted observer.");
				entry.SetInterval(viewer, 4);
				LogAssert.AreEqual(4, entry.GetEffectiveInterval(viewer), "The cap interval applies on its own.");
				entry.SetInterval(viewer, 1);
				LogAssert.AreEqual(1, entry.GetEffectiveInterval(viewer), "An interval of 1 removes the limit.");
			}
			finally
			{
				ObserverStreamingRegistry.Clear();
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
