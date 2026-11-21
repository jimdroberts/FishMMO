using System;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// How a pet uses the heals and buffs it knows: who it looks after, and when. See <see cref="PetSupport"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>On by default, and inert without the spells.</b> Support is decided by what the pet knows,
	/// not by its archetype: a melee pet that learns a heal from its summon spell heals. A pet that
	/// knows no heal or buff never finds anything to cast, so every pet archetype carries these
	/// settings at their defaults without changing how it behaves.
	/// </para>
	/// <para>
	/// The owner always comes first: an owner who needs a heal or is missing a buff is served
	/// before anyone else, wherever the owner is. Nothing here can change that order; these only
	/// tune how wounded "needs a heal" is and how far the pet looks for others.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AIPetSupportSettings
	{
		[Tooltip("Whether pets with this archetype heal and buff at all. Off: heals and buffs are only cast on the pet itself, as part of its attack rotation.")]
		public bool Enabled = true;

		[Tooltip("Heal someone whose health is below this fraction of their maximum.")]
		[Range(0f, 1f)] public float HealThreshold = 0.75f;

		[Tooltip("Whether the pet heals and buffs itself, after its owner.")]
		public bool SupportSelf = true;

		[Tooltip("Whether the pet heals and buffs friends other than its owner: its owner's party and guild, its owner's other pets, and anyone its owner is allied with.")]
		public bool SupportAllies = true;

		[Tooltip("How far around the pet it looks for friends other than its owner. The owner is looked after at any distance.")]
		[Min(0f)] public float AllyScanRadius = 20f;

		[Tooltip("Seconds between looks for friends. Health moves slowly enough that this can be coarse.")]
		[Min(0.1f)] public float AllyScanInterval = 0.5f;

		[Tooltip("Physics layers friends are on. Nothing selected: the layers the pet's current state sweeps for enemies on, which are the character layers.")]
		public LayerMask AllyLayers;
	}
}
