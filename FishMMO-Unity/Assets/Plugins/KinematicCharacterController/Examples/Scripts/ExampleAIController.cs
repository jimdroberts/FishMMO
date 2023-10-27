using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KinematicCharacterController.Examples
{
    public class ExampleAIController : MonoBehaviour
    {
        public float MovementPeriod = 1f;
        public List<ExampleCharacterController> Characters = new List<ExampleCharacterController>();

        private bool _stepHandling;
        private bool _ledgeHandling;
        private bool _intHandling;
        private bool _safeMove;

        private void Update()
        {
            AICharacterInputs inputs = new AICharacterInputs();

            // Simulate an input on all controlled characters
            inputs.MoveVector = Mathf.Sin(Time.time * MovementPeriod) * Vector3.forward;
            inputs.LookVector = Vector3.Slerp(-Vector3.forward, Vector3.forward, inputs.MoveVector.z).normalized;
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].SetInputs(ref inputs);
            }
        }
    }
}