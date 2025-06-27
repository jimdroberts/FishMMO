using UnityEngine;
using System.Collections.Generic;
using System;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace FishMMO.Shared
{
    public class AbilityObject : MonoBehaviour
    {
        public static Action<PetAbilityTemplate, IPlayerCharacter> OnPetSummon;

        internal int ContainerID;
        internal int ID;
        public Ability Ability;
        public IPlayerCharacter Caster;
        public Rigidbody CachedRigidBody;
        public int HitCount;
        public float RemainingLifeTime;

        public System.Random RNG;

        public Transform Transform { get; private set; }

        private void Awake()
        {
            Transform = transform;
            CachedRigidBody = GetComponent<Rigidbody>();
            if (CachedRigidBody != null)
            {
                CachedRigidBody.isKinematic = true; // Still handle physics setup
            }
        }

        void Update()
        {
            // Update remaining lifetime
            if (Ability.LifeTime > 0.0f)
            {
                RemainingLifeTime -= Time.deltaTime;
            }

            // Dispatch Tick Event
            if (Ability?.Template?.OnTickTriggers != null)
            {
                TickEventData tickEvent = new TickEventData(Caster, Transform, Time.deltaTime);
                foreach (var trigger in Ability.OnTickTriggers)
                {
                    trigger.Execute(tickEvent);
                }
            }

            // If lifetime reaches 0, trigger destruction directly (or via a trigger for more control)
            // For simplicity, let's keep it direct for now as a fallback if no trigger handles it
            if (Ability.LifeTime > 0.0f && RemainingLifeTime < 0.0f)
            {
                DestroyAbilityObjectInternal();
                return;
            }
            else if (Ability.LifeTime <= 0.0f) // Immediately destroy if lifetime is 0
            {
                DestroyAbilityObjectInternal();
                return;
            }
        }

        void OnCollisionEnter(Collision other)
        {
            ICharacter hitCharacter = other.gameObject.GetComponent<ICharacter>();

            // Dispatch Collision Event
            if (Ability?.OnHitTriggers != null)
            {
                AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(Caster, hitCharacter, this, other);
                foreach (var trigger in Ability.OnHitTriggers)
                {
                    trigger.Execute(collisionEvent);
                }
            }

            // Check if object should be destroyed after hits.
            // This can also be moved to an action/condition if you want more control.
            // For now, keeping it here as a hard check.
            if (HitCount < 1)
            {
                DestroyAbilityObjectInternal();
            }
        }

        // Renamed to avoid confusion with public Destroy() from MonoBehaviour
        internal void DestroyAbilityObjectInternal()
        {
            // Log.Debug("Destroyed " + gameObject.name);
            if (Ability != null)
            {
                // Dispatch OnDestroy Event if needed
                if (Ability.OnDestroyTriggers != null)
                {
                    // You might need a specific EventData for destruction
                    // For example, AbilityDestroyEventData with `AbilityObject` reference
                    // For now, just pass a generic EventData if no specific data is needed
                    EventData destroyEvent = new EventData(Caster); // Or a new AbilityDestroyEventData(Caster, this);
                    foreach (var trigger in Ability.OnDestroyTriggers)
                    {
                        trigger.Execute(destroyEvent);
                    }
                }

                Ability.RemoveAbilityObject(ContainerID, ID);
                Ability = null;
            }
            Caster = null;
            Destroy(gameObject);
            gameObject.SetActive(false); // Destroy takes a frame, deactivate immediately
        }

        /// <summary>
        /// Handles primary spawn functionality for all ability objects. Returns true if successful.
        /// </summary>
        public static void Spawn(Ability ability, IPlayerCharacter caster, Transform abilitySpawner, TargetInfo targetInfo, int seed)
        {
            AbilityTemplate template = ability.Template;
            if (template == null)
            {
                return;
            }

            if (template.RequiresTarget && targetInfo.Target == null)
            {
                return;
            }

            PetAbilityTemplate petAbilityTemplate = template as PetAbilityTemplate;
            if (petAbilityTemplate != null)
            {
                OnPetSummon?.Invoke(petAbilityTemplate, caster);
                return;
            }

            // Self target abilities don't spawn ability objects and are instead applied immediately
            // This could be a "SpawnSelfAbilityAction"
            if (ability.Template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
            {
                // Here, you would ideally dispatch an event that a "SelfTargetAbilityTrigger" could listen to
                // For demonstration, let's keep the direct action for now, but in a full ECA, this would be a trigger.
                // You would need a 'SelfTargetHitEventData' or similar.
                // ApplyHitEvents(ability, caster, caster, null);
                // Instead, dispatch an event for self-target abilities.
                AbilityCollisionEventData selfTargetEvent = new AbilityCollisionEventData(caster, caster, null, null);
                foreach (var trigger in ability.Template.OnHitTriggers) // Assuming OnHitTriggers can apply to self-targets
                {
                    trigger.Execute(selfTargetEvent);
                }
                return;
            }

            if (template.AbilityObjectPrefab == null)
            {
                return;
            }

            GameObject go = Instantiate(template.AbilityObjectPrefab);
            SceneManager.MoveGameObjectToScene(go, caster.GameObject.scene);
            go.SetActive(false);

            AbilityObject abilityObject = go.GetComponent<AbilityObject>();
            if (abilityObject == null)
            {
                abilityObject = go.AddComponent<AbilityObject>();
            }
            abilityObject.ID = 0;
            abilityObject.Ability = ability;
            abilityObject.Caster = caster;
            abilityObject.HitCount = template.HitCount;
            abilityObject.RemainingLifeTime = ability.LifeTime;
            abilityObject.RNG = new System.Random(seed);

            if (ability.Objects == null)
            {
                ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>();
            }

            Dictionary<int, AbilityObject> spawnedAbilityObjects = new Dictionary<int, AbilityObject>();
            int containerID;
            do
            {
                containerID = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            } while (ability.Objects.ContainsKey(containerID));

            ability.Objects.Add(containerID, spawnedAbilityObjects);
            abilityObject.ContainerID = containerID;
            spawnedAbilityObjects[abilityObject.ID] = abilityObject; // Add the initial object to the map

            RefWrapper<int> nextChildID = new RefWrapper<int>(0); // Start ID for child objects

            // Dispatch Pre-Spawn Events
            if (ability.Template.OnPreSpawnTriggers != null)
            {
                AbilitySpawnEventData preSpawnEvent = new AbilitySpawnEventData(caster, ability, abilitySpawner, targetInfo, seed, abilityObject, nextChildID, spawnedAbilityObjects);
                foreach (var trigger in ability.Template.OnPreSpawnTriggers)
                {
                    trigger.Execute(preSpawnEvent);
                }
            }

            // Dispatch Spawn Events
            if (ability.Template.OnSpawnTriggers != null)
            {
                AbilitySpawnEventData spawnEvent = new AbilitySpawnEventData(caster, ability, abilitySpawner, targetInfo, seed, abilityObject, nextChildID, spawnedAbilityObjects);
                foreach (var trigger in ability.Template.OnSpawnTriggers)
                {
                    trigger.Execute(spawnEvent);
                }
            }

            // Finalize activation of all spawned objects (initial and children)
            foreach (AbilityObject obj in spawnedAbilityObjects.Values)
            {
                obj.gameObject.SetActive(true);
            }
        }
    }
}