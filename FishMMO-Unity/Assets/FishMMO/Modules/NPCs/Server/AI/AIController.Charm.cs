using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// A charmed NPC (the Mesmerize ability, <see cref="INPCCharm"/>): for as long as the charm lasts it fights for the
	/// character who cast it, the way a pet fights for its owner, and then goes back to being itself.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Whose side.</b> <see cref="NPC.CharmedBy"/> is set, so <see cref="NPC.CompanionOwner"/> names the charmer and
	/// the harm rule, kill credit and loot judge the NPC as the charmer's: the charmer and their party cannot hurt it, it
	/// cannot hurt them, and its old friends may now hurt it. Threat is linked as a pet's is
	/// (<see cref="AggressionDispatcher.LinkPet"/>): whatever hits the charmer or the NPC is the NPC's enemy.
	/// </para>
	/// <para>
	/// <b>What it does.</b> The generic enemy sweep is off (it would find the charmer's own side by faction). Out of a
	/// fight it keeps at the charmer's heel (<see cref="Home"/> is the charmer). It takes up whatever the charmer strikes
	/// (<see cref="OnCharmMasterStruck"/>) and whatever strikes the charmer or itself (the threat link).
	/// </para>
	/// <para>
	/// <b>Ending.</b> The buff's removal ends it (<see cref="EndCharm"/>), and so does the charmer dying, leaving the
	/// scene or falling more than <see cref="CharmBreakDistance"/> behind. The fight is forgotten — threat, target, the
	/// link — and the NPC is itself again: its own sweep finds whatever it is hostile to, its charmer included.
	/// </para>
	/// </remarks>
	public partial class AIController : INPCCharm
	{
		/// <summary>How close a charmed NPC keeps to its charmer out of a fight, metres.</summary>
		public const float CharmFollowDistance = 3f;

		/// <summary>A charmer further than this (metres) has left its charm behind, and the charm breaks.</summary>
		public const float CharmBreakDistance = 60f;

		private ICharacter charmMaster;

		/// <inheritdoc />
		public ICharacter CharmMaster => charmMaster;

		/// <summary>True while a charm holds this NPC.</summary>
		public bool IsCharmed => charmMaster != null;

		/// <summary>
		/// Whether <paramref name="target"/> may be charmed by <paramref name="master"/>: a charmable NPC (authored
		/// <see cref="NPC.IsCharmable"/>) that is not somebody's companion already, is not the master, and is alive.
		/// Pure over what it is handed, so the rule can be pinned without a scene.
		/// </summary>
		public static bool MayCharm(ICharacter target, ICharacter master, bool alive)
		{
			if (target == null || master == null || ReferenceEquals(target, master) || !alive)
			{
				return false;
			}
			if (!(target is NPC npc) || !npc.IsCharmable)
			{
				return false;
			}
			// Somebody's pet, or held by another charmer: not ours to take. Recharmed by the same master is fine (a refresh).
			ICharacter owner = npc.CompanionOwner;
			return owner == null || ReferenceEquals(owner, master);
		}

		/// <inheritdoc />
		public bool BeginCharm(ICharacter master)
		{
			if (!IsRunning || Character == null || Character is IAICompanion)
			{
				return false;
			}
			bool alive = Character.TryGet(out ICharacterDamageController damage) && damage.IsAlive;
			if (!MayCharm(Character, master, alive))
			{
				return false;
			}
			if (ReferenceEquals(charmMaster, master))
			{
				return true; // a refresh: nothing changes but the buff's clock
			}

			charmMaster = master;
			if (Character is NPC npc)
			{
				npc.CharmedBy = master;
			}

			// Whatever it was fighting (most likely its charmer) is forgotten.
			ForgetFight();
			AggressionDispatcher.LinkPet(Character, master);
			ICharacterDamageController.OnDamaged += OnCharmMasterStruck;
			TransitionToIdleState();
			Log.Debug("AIController", $"{gameObject.name} is charmed by character {master.ID}.");
			return true;
		}

		/// <inheritdoc />
		public void EndCharm(ICharacter master)
		{
			if (charmMaster == null || (master != null && !ReferenceEquals(master, charmMaster)))
			{
				return;
			}
			ICharacter was = charmMaster;
			ReleaseCharm();
			if (IsRunning)
			{
				ForgetFight();
				TransitionToRandomMovementState();
			}
			Log.Debug("AIController", $"{gameObject.name} is no longer charmed by character {was.ID}.");
		}

		/// <summary>Drops every tie the charm made, without touching the fight state (a corpse, a pool reset).</summary>
		private void ReleaseCharm()
		{
			if (charmMaster == null)
			{
				return;
			}
			ICharacterDamageController.OnDamaged -= OnCharmMasterStruck;
			AggressionDispatcher.UnlinkPet(Character);
			if (Character is NPC npc)
			{
				npc.CharmedBy = null;
			}
			charmMaster = null;
		}

		/// <summary>Threat, target and look are cleared, and the agent stops where it is.</summary>
		private void ForgetFight()
		{
			AggressionState?.Clear();
			Target = null;
			LookTarget = null;
			leashEvade = false;
		}

		/// <summary>
		/// Once per brain tick while charmed: breaks a charm whose charmer is gone, and out of a fight keeps the NPC at its
		/// charmer's heel.
		/// </summary>
		private void TickCharm()
		{
			if (charmMaster == null)
			{
				return;
			}
			Transform self = Character != null ? Character.Transform : null;
			Transform master = charmMaster.Transform;
			bool masterAlive = charmMaster.TryGet(out ICharacterDamageController damage) && damage.IsAlive;
			if (self == null || master == null || !masterAlive || master.gameObject.scene != self.gameObject.scene ||
				(master.position - self.position).sqrMagnitude > CharmBreakDistance * CharmBreakDistance)
			{
				EndCharm(charmMaster);
				return;
			}
			if (IsInCombatState)
			{
				return;
			}
			if ((master.position - self.position).sqrMagnitude > CharmFollowDistance * CharmFollowDistance)
			{
				Resume();
				TryMoveTo(master.position);
			}
			else
			{
				Stop();
			}
		}

		/// <summary>
		/// The charmer struck someone: the NPC takes it up, as a pet set on its owner's target does. Anyone the charmer may
		/// not harm (the harm rule already refused that hit) is left alone.
		/// </summary>
		private void OnCharmMasterStruck(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (charmMaster == null || !ReferenceEquals(attacker, charmMaster) || defender == null ||
				ReferenceEquals(defender, Character) || AggressionState == null)
			{
				return;
			}
			if (!defender.TryGet(out ICharacterDamageController defenderDamage) || !defenderDamage.IsAlive ||
				!HarmRule.Allows(Character, defender))
			{
				return;
			}
			// Threat as if the defender had struck the NPC: the NPC's own combat entry answers it.
			AggressionState.HandleDamaged(defender, amount > 0 ? amount : 1);
		}
	}
}
