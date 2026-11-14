using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/gm invis</c> and <c>/gm fly</c>: how a game master is present in the world. Hidden, the character is withheld from
	/// every client but its own and staff (<see cref="GameMasterInvisibility"/>), who draw it half see-through
	/// (<see cref="MovementModeBits.Hidden"/>). Flying, it moves the way the camera looks
	/// with no gravity, ground or collisions (<see cref="KCCController.SetFlying"/>), predicted by its own client like any
	/// other movement because the flag rides the reconcile.
	/// </summary>
	public partial class SceneServerSystem
	{
		private ICharacterSystem<NetworkConnection, Scene> presenceCharacterSystem;

		/// <summary>The presence rows of the <c>/gm</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildGameMasterPresenceCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "invis", Aliases = new[] { "invisible", "hide" }, Category = "Presence",
					Summary = "Hides you from players (staff still see you), or shows you again. on/off, or toggles.",
					Arguments = "state:Text?",
					Run = ToggleInvisibility,
				},
				new OperatorCommand
				{
					Name = "fly", Aliases = new[] { "noclip" }, Category = "Presence",
					Summary = "Flies through anything: look to steer, jump rises, crouch sinks, sprint for speed. on/off, or toggles.",
					Arguments = "state:Text?",
					Run = ToggleFlight,
				},
			};
		}

		private void InitializeGameMasterPresence(ICharacterSystem<NetworkConnection, Scene> characterSystem)
		{
			presenceCharacterSystem = characterSystem;
			if (presenceCharacterSystem != null)
			{
				presenceCharacterSystem.OnDespawnCharacter += CharacterSystem_OnPresenceCharacterDespawned;
			}
		}

		private void DeinitializeGameMasterPresence()
		{
			if (presenceCharacterSystem != null)
			{
				presenceCharacterSystem.OnDespawnCharacter -= CharacterSystem_OnPresenceCharacterDespawned;
				presenceCharacterSystem = null;
			}
			GameMasterInvisibility.Clear();
		}

		/// <summary>A leaving character takes its hidden state and flight with it: pooled characters are reused by other players.</summary>
		private void CharacterSystem_OnPresenceCharacterDespawned(NetworkConnection connection, IPlayerCharacter character)
		{
			if (character?.NetworkObject != null)
			{
				GameMasterInvisibility.SetHidden(character.NetworkObject, false);
				if (character.NetworkObject.TryGetComponent(out KCCController controller))
				{
					controller.SetFlying(false);
				}
			}
		}

		/// <summary>Parses "on", "off" or nothing (toggle) against the current value. False when the word is neither.</summary>
		private static bool TryParseToggle(string arguments, bool current, out bool value)
		{
			string state = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (string.IsNullOrEmpty(state))
			{
				value = !current;
				return true;
			}
			if (state.Equals("on", StringComparison.OrdinalIgnoreCase))
			{
				value = true;
				return true;
			}
			if (state.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				value = false;
				return true;
			}
			value = current;
			return false;
		}

		/// <summary>Starts or stops the caller's flight: "on", "off", or nothing to toggle.</summary>
		private void ToggleFlight(IPlayerCharacter character, string arguments)
		{
			NetworkObject nob = character?.NetworkObject;
			if (nob == null || !nob.IsSpawned || !nob.TryGetComponent(out KCCController controller))
			{
				Reply(character, "Your character cannot fly: it has no movement controller.");
				return;
			}
			if (!TryParseToggle(arguments, controller.IsFlying, out bool fly))
			{
				ReplyUsage(character, gameMasterCommands, "fly");
				return;
			}
			if (fly == controller.IsFlying)
			{
				Reply(character, fly ? "You are already flying." : "You are not flying.");
				return;
			}
			controller.SetFlying(fly);
			Reply(character, fly
				? "Flying: look to steer, jump rises, crouch sinks, sprint for speed; you pass through everything. /gm fly again to land."
				: $"Flight off: you fall from where you are. Inside terrain? /gm unstuck {character.CharacterName}");
			_ = Log.Info("SceneServerSystem", $"{character.CharacterName} {(fly ? "started" : "stopped")} flying.");
		}

		/// <summary>Hides or shows the caller: "on", "off", or nothing to toggle.</summary>
		private void ToggleInvisibility(IPlayerCharacter character, string arguments)
		{
			NetworkObject nob = character?.NetworkObject;
			if (nob == null || !nob.IsSpawned)
			{
				return;
			}
			if (!TryParseToggle(arguments, GameMasterInvisibility.IsHidden(nob), out bool hide))
			{
				ReplyUsage(character, gameMasterCommands, "invis");
				return;
			}

			if (!GameMasterInvisibility.SetHidden(nob, hide))
			{
				Reply(character, hide ? "You are already hidden from players." : "You are already visible.");
				return;
			}
			// Now rather than on the next timed sweep, so nobody sees the character for a moment longer than asked.
			nob.NetworkManager?.ServerManager.Objects.RebuildObservers(nob);
			Reply(character, hide
				? "You are hidden: players no longer see you; staff still do, faded as you see yourself. /gm invis again to show yourself."
				: "You are visible to everyone again.");
			_ = Log.Info("SceneServerSystem", $"{character.CharacterName} is now {(hide ? "hidden from" : "visible to")} players.");
		}
	}
}
