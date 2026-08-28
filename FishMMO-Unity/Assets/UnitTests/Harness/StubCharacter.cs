using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// A minimal <see cref="ICharacter"/> for exercising a single <c>CharacterBehaviour</c> without
	/// standing up a networked character.
	/// </summary>
	/// <remarks>
	/// Only the members a behaviour under test actually reaches are implemented — the behaviour
	/// lookup, the flags, and registration. Everything else throws, deliberately: a test that
	/// wanders into unimplemented territory should say so loudly rather than quietly observing a
	/// default that means nothing.
	/// </remarks>
	internal sealed class StubCharacter : ICharacter
	{
		/// <summary>Behaviour handed out by <see cref="TryGet{T}"/>, or null to fail the lookup.</summary>
		public ICharacterBehaviour Behaviour;

		/// <summary>How many times a behaviour lookup was attempted.</summary>
		public int TryGetCalls;

		public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
		{
			TryGetCalls++;
			control = Behaviour as T;
			return control != null;
		}

		public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
		public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }

		public long ID { get; set; }

		/// <summary>
		/// Character flags. Left at zero — "not loaded" — unless a test sets it, which is the
		/// state resource clamping treats as "the final value is not authoritative yet".
		/// </summary>
		public int Flags { get; set; }

		public Collider Collider { get; set; }
		public WorldLabel CharacterNameLabel { get; set; }
		public WorldLabel CharacterGuildLabel { get; set; }

		public string Name => "Stub";
		public Transform Transform => null;
		public GameObject GameObject => null;
		public NetworkConnection Owner => null;
		public NetworkObject NetworkObject => null;
		public PredictionManager PredictionManager => null;
		public HashSet<NetworkConnection> Observers => null;
		public bool IsTeleporting => false;
		public bool IsSpawned => false;
		public Transform MeshRoot => null;

		public void EnableFlags(CharacterFlags flags) { }
		public void DisableFlags(CharacterFlags flags) { }
		public bool IsFlagged(CharacterFlags flags) => false;

		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) => throw new NotImplementedException();
		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) => throw new NotImplementedException();
		public void Invoke(List<Trigger> triggers, EventData eventData) => throw new NotImplementedException();
	}
}
