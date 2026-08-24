using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages registration and tracking of scene objects with unique IDs in FishMMO.
	/// </summary>
	public class SceneObject
	{
		/// <summary>
		/// Dictionary of all registered scene objects, keyed by their unique ID.
		/// </summary>
		public readonly static Dictionary<long, ISceneObject> Objects = new Dictionary<long, ISceneObject>();

		/// <summary>
		/// The most recently assigned scene object ID. Counts DOWN from zero.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Negative on purpose, so scene object IDs and character IDs occupy disjoint ranges.
		/// </para>
		/// <para>
		/// They used to share one: this counter issued 1, 2, 3… to every NPC, interactable and
		/// platform in a scene, while a player's ID is its database primary key — which also
		/// starts at 1. On any server with fewer characters than scene objects, which is every
		/// server early in its life, low-numbered players collided with scene objects outright.
		/// Three places key a single dictionary by "character ID" without knowing which kind they
		/// hold, and each broke in its own way:
		/// </para>
		/// <list type="bullet">
		/// <item><description><c>AggressionController</c>'s threat table. Every death in a scene
		/// is broadcast to every NPC in combat, which drops the victim's ID from its table — so
		/// killing the NPC that happened to draw scene ID 7 wiped player 7's threat off every mob
		/// fighting them, scene-wide, as if they had vanished.</description></item>
		/// <item><description><c>BaseCharacter.ClientCharacters</c>, the client's lookup from ID
		/// to character. A colliding NPC and player evict one another, so faction and archetype
		/// broadcasts resolve to the wrong body or to none.</description></item>
		/// <item><description><c>AICombatSlots</c>' attacker and target rings, where a player and
		/// an NPC would contend for one ring entry.</description></item>
		/// </list>
		/// <para>
		/// Counting down is the whole fix, and it is safe because these IDs are never persisted:
		/// they are handed out fresh at every scene load and travel the wire as signed 64-bit
		/// values. A pet's database row carries its own key and is unaffected.
		/// </para>
		/// </remarks>
		private static long currentID = 0;

		/// <summary>
		/// Registers a scene object, assigning a unique ID if not a client object.
		/// </summary>
		/// <param name="sceneObject">The scene object to register.</param>
		/// <param name="asClient">If true, do not assign an ID (server will assign).</param>
		public static void Register(ISceneObject sceneObject, bool asClient = false)
		{
			if (sceneObject == null)
			{
				return;
			}
			// If this is a client, we don't want to assign an ID, as it will be assigned by the server.
			if (!asClient)
			{
				/* Already holding a live registration: keep the ID rather than issuing a new one.
				 *
				 * Server registration happens in OnStartServer, which runs again every time a
				 * pooled object is respawned. Handing out a fresh ID there would strand the old
				 * dictionary entry and, worse, change the ID out from under any client still
				 * holding it — so a re-registration must be a no-op. */
				if (IsRegistered(sceneObject))
				{
					return;
				}

				// Assign a unique ID not already in use. Decrementing keeps every scene object ID
				// negative and therefore disjoint from any character ID — see currentID.
				do
				{
					sceneObject.ID = --currentID;
				}
				while (Objects.ContainsKey(sceneObject.ID));
			}
			//Log.Debug($"Registering {sceneObject.GameObject.name}:{sceneObject.ID} | {asClient}");

			// Add to dictionary
			Objects[sceneObject.ID] = sceneObject;
		}

		/// <summary>
		/// Unregisters a scene object, removing it from the dictionary.
		/// </summary>
		/// <param name="sceneObject">The scene object to unregister.</param>
		public static void Unregister(ISceneObject sceneObject)
		{
			/* Identity-checked. Removing by ID alone will evict whoever currently holds that ID,
			 * which is not necessarily this object: an unregistered object still has ID 0, and a
			 * pooled object that was re-registered under a new ID would otherwise have its
			 * successor's entry torn out by its own late teardown. */
			if (sceneObject == null)
			{
				return;
			}
			if (Objects.TryGetValue(sceneObject.ID, out ISceneObject existing) &&
				ReferenceEquals(existing, sceneObject))
			{
				Objects.Remove(sceneObject.ID);
			}
		}

		/// <summary>
		/// True when this exact object is the one currently registered under its own ID.
		/// </summary>
		/// <param name="sceneObject">The object to test.</param>
		/// <returns>True if registered.</returns>
		public static bool IsRegistered(ISceneObject sceneObject)
		{
			return sceneObject != null &&
				   Objects.TryGetValue(sceneObject.ID, out ISceneObject existing) &&
				   ReferenceEquals(existing, sceneObject);
		}
	}
}