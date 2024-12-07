using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
    [CustomEditor(typeof(ParticleAdjuster))]
    public class ParticleSystemEditorExtension : Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw default inspector elements
            DrawDefaultInspector();

            // Get reference to the target object (ParticleSystem)
            ParticleAdjuster particleAdjuster = (ParticleAdjuster)target;

            // Add a button to the Inspector UI
            if (GUILayout.Button("Scale Particle System to Fit Cube"))
            {
                // Scale the particle system to fit within the 1x1x1 cube
                ScaleParticleSystem(particleAdjuster);
            }
        }

        private void ScaleParticleSystem(ParticleAdjuster particleAdjuster)
        {
            if (particleAdjuster == null || particleAdjuster.ParticleSystem == null)
            {
                Debug.LogWarning("Particle system is missing.");
                return;
            }

            var particleSystem = particleAdjuster.ParticleSystem;
            var main = particleSystem.main;
            var shape = particleSystem.shape;

            // Cube half size (distance from the center to any face of the 1x1x1 cube)
            const float CubeSize = 1.0f;
            const float CubeHalfSize = 0.5f;

            // The maximum speed of particles should ensure they stay inside the cube
            float maxStartSpeed = shape.shapeType == ParticleSystemShapeType.Cone ? CubeSize : CubeHalfSize / main.startLifetime.constantMax;

            // Set the start speed to a value that ensures particles stay inside the cube
            main.startSpeed = maxStartSpeed;

            // Set the particle size to fit within the cube (adjust size based on cube size)
            if (shape.shapeType == ParticleSystemShapeType.Cone)
            {
                shape.radius = CubeHalfSize;
            }

            // Adjust the start lifetime of particles so they don't travel outside the cube
            main.startLifetime = shape.shapeType == ParticleSystemShapeType.Cone ? CubeSize : CubeHalfSize / maxStartSpeed;  // Ensure particles don't exceed cube boundaries
        }
    }
}