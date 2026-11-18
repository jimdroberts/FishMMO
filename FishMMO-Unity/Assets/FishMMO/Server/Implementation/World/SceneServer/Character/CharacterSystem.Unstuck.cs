using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/unstuck</c> — a player's own way out of geometry they cannot walk out of.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same gate as every other voluntary move.</b> <see cref="CharacterStateValidation.CanActOrMove"/>
	/// refuses the dead, the incapacitated, anyone mid-teleport and anyone in combat. Without the
	/// combat check this is a free escape from every fight; without the rest it is a way out of a
	/// stun or a corpse walking itself home.
	/// </para>
	/// <para>
	/// <b>The nearest safe place in the scene they are in.</b> The nearest authored respawn position,
	/// or the bind point when that is nearer still. Never a transfer, and never "home" from across the
	/// map: either would make this a free, ungated recall.
	/// </para>
	/// <para>
	/// <b>Not inside an instance.</b> Instanced content already has <c>/leaveinstance</c>, with its
	/// own rules about who may leave and when, and an instance's respawn positions are authored for
	/// its own purposes (an arena keys them by team).
	/// </para>
	/// <para>
	/// A cooldown, because the move is otherwise a short-range teleport on demand. Only a move that
	/// happened starts it, so a refused attempt can be retried as soon as the reason clears.
	/// </para>
	/// </remarks>
	public partial class CharacterSystem
	{
		[Tooltip("Seconds a player must wait between uses of /unstuck. 0 disables the cooldown.")]
		[SerializeField][Min(0f)] private float selfUnstuckCooldownSeconds = 300.0f;

		/// <summary>The words that run <see cref="OnUnstuckCommand"/>.</summary>
		private static readonly string[] UnstuckCommandWords = { "/unstuck", "/stuck" };

		/// <summary>How many cooldown entries are kept before the lapsed ones are swept.</summary>
		private const int MaxUnstuckCooldowns = 512;

		/// <summary>
		/// When each character may next use <c>/unstuck</c>, by character id, in
		/// <see cref="MonotonicClock"/> seconds: a cooldown is a local duration.
		/// </summary>
		private readonly Dictionary<long, double> nextUnstuckAt = new Dictionary<long, double>();

		/// <summary>Handles <c>/unstuck</c> and <c>/stuck</c>.</summary>
		/// <returns>Always <c>true</c>: the command is consumed either way, never echoed to chat.</returns>
		private bool OnUnstuckCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null)
			{
				return true;
			}

			NetworkConnection conn = character.Owner;
			if (conn == null || !conn.IsActive)
			{
				return true;
			}

			if (character.IsInInstance())
			{
				SendSystemMessage(conn, "Use /leaveinstance to leave instanced content.");
				return true;
			}

			if (!CharacterStateValidation.CanActOrMove(character))
			{
				SendSystemMessage(conn,
					character.IsFlagged(CharacterFlags.IsDead) ? "The dead cannot use /unstuck." :
					character.IsFlagged(CharacterFlags.IsInCombat) ? "You cannot use /unstuck in combat." :
					"You cannot use /unstuck right now.");
				return true;
			}

			double now = MonotonicClock.NowSeconds;
			if (nextUnstuckAt.TryGetValue(character.ID, out double next) && next > now)
			{
				TimeSpan wait = TimeSpan.FromSeconds(next - now);
				SendSystemMessage(conn, $"You can use /unstuck again in {(int)wait.TotalMinutes}m {wait.Seconds}s.");
				return true;
			}

			if (!TryResolveUnstuckDestination(character, out Vector3 position, out Quaternion rotation) ||
				character.Motor == null)
			{
				SendSystemMessage(conn, "There is nowhere safe to move you to here. Use /helpme to ask staff.");
				return true;
			}

			// Through the motor, never the transform: the prediction system reconciles against the
			// motor, and a transform write is corrected straight back on the next tick.
			character.Motor.SetPositionAndRotationAndVelocity(position, rotation, Vector3.zero);

			if (selfUnstuckCooldownSeconds > 0f)
			{
				SweepUnstuckCooldowns(now);
				nextUnstuckAt[character.ID] = now + selfUnstuckCooldownSeconds;
			}

			Log.Debug("CharacterSystem", $"{character.CharacterName} used /unstuck in '{character.CurrentSceneName()}' and was moved to {position}.");
			SendSystemMessage(conn, "You have been moved to safety.");
			return true;
		}

		/// <summary>
		/// The bind point when the character stands in the bind scene, otherwise the nearest respawn
		/// position authored for the scene they are in.
		/// </summary>
		private bool TryResolveUnstuckDestination(IPlayerCharacter character, out Vector3 position, out Quaternion rotation)
		{
			position = default;
			rotation = Quaternion.identity;

			if (character.Transform == null)
			{
				return false;
			}

			string scene = character.CurrentSceneName();
			if (string.IsNullOrEmpty(scene))
			{
				return false;
			}

			Vector3 from = character.Transform.position;
			bool hasRespawn = Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
				sceneServerSystem.WorldSceneDetailsCache != null &&
				sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(scene, out WorldSceneDetails details) &&
				RescuePoints.TryFind(details, RescuePointKind.Respawn, null, from, out RescuePoint nearest)
				? SetOut(nearest, out position, out rotation)
				: false;

			/* The NEAREST safe place, never simply "home". A bind point anywhere in a large open-world
			 * scene would make this a free recall across the whole map every few minutes; the bind point
			 * is used only when it is itself closer than the nearest respawn position, so getting unstuck
			 * never moves a player further than the nearest authored safe spot would. The bind point
			 * carries no facing, so the character keeps its own. */
			if (!string.IsNullOrEmpty(character.BindScene) &&
				string.Equals(character.BindScene, scene, StringComparison.Ordinal) &&
				(!hasRespawn || (character.BindPosition - from).sqrMagnitude < (position - from).sqrMagnitude))
			{
				position = character.BindPosition;
				rotation = character.Transform.rotation;
				return true;
			}

			return hasRespawn;
		}

		/// <summary>Unpacks a rescue point into a position and facing, for use inside a condition.</summary>
		private static bool SetOut(RescuePoint point, out Vector3 position, out Quaternion rotation)
		{
			position = point.Position;
			rotation = point.Rotation;
			return true;
		}

		/// <summary>Drops lapsed cooldowns once there are many, so the map cannot grow for the life of the server.</summary>
		private void SweepUnstuckCooldowns(double now)
		{
			if (nextUnstuckAt.Count < MaxUnstuckCooldowns)
			{
				return;
			}

			List<long> lapsed = new List<long>();
			foreach (KeyValuePair<long, double> pair in nextUnstuckAt)
			{
				if (pair.Value <= now)
				{
					lapsed.Add(pair.Key);
				}
			}
			for (int i = 0; i < lapsed.Count; ++i)
			{
				nextUnstuckAt.Remove(lapsed[i]);
			}
		}
	}
}
