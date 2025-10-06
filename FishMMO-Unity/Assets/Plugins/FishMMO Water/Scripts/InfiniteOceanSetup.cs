using UnityEngine;

namespace FishMMO.Water
{
    /// <summary>
    /// Helper script to automatically set up infinite ocean water
    /// Attach this to a GameObject to create an infinite ocean setup
    /// </summary>
    public class InfiniteOceanSetup : MonoBehaviour
    {
        [Header("Ocean Setup")]
        [Tooltip("Material to use for the ocean (should use FishMMO/RealisticWater shader)")]
        public Material oceanMaterial;
        
        [Tooltip("Water surface level (Y position)")]
        public float waterLevel = 0f;
        
        [Tooltip("Size of the initial water plane (will extend to infinity)")]
        public float planeSize = 100f;
        
        [Header("Camera Setup")]
        [Tooltip("Camera to configure for infinite ocean")]
        public Camera targetCamera;
        
        [Tooltip("Far clip plane distance for infinite ocean")]
        public float farClipDistance = 2000f;
        
        [Header("Auto Setup")]
        [Tooltip("Automatically configure material settings")]
        public bool autoConfigureMaterial = true;
        
        void Start()
        {
            SetupInfiniteOcean();
        }
        
        [ContextMenu("Setup Infinite Ocean")]
        public void SetupInfiniteOcean()
        {
            // Create water plane if not exists
            CreateWaterPlane();
            
            // Configure camera
            ConfigureCamera();
            
            // Configure material
            if (autoConfigureMaterial && oceanMaterial != null)
            {
                ConfigureMaterial();
            }
            
            Debug.Log("Infinite Ocean setup complete!");
        }
        
        void CreateWaterPlane()
        {
            // Check if water plane already exists
            Transform waterPlane = transform.Find("InfiniteOceanPlane");
            if (waterPlane == null)
            {
                // Create new water plane
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name = "InfiniteOceanPlane";
                plane.transform.SetParent(transform);
                
                // Position and scale
                plane.transform.position = new Vector3(0, waterLevel, 0);
                plane.transform.localScale = Vector3.one * (planeSize / 10f); // Plane is 10x10 by default
                
                // Assign material
                if (oceanMaterial != null)
                {
                    Renderer renderer = plane.GetComponent<Renderer>();
                    renderer.material = oceanMaterial;
                }
                
                // Remove collider (not needed for visual water)
                Collider col = plane.GetComponent<Collider>();
                if (col != null)
                {
                    DestroyImmediate(col);
                }
                
                Debug.Log($"Created infinite ocean plane at Y={waterLevel}");
            }
        }
        
        void ConfigureCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
            
            if (targetCamera != null)
            {
                targetCamera.farClipPlane = farClipDistance;
                Debug.Log($"Configured camera far clip plane to {farClipDistance}");
            }
        }
        
        void ConfigureMaterial()
        {
            if (oceanMaterial != null)
            {
                // Enable infinite ocean
                oceanMaterial.SetFloat("_EnableInfiniteOcean", 1.0f);
                
                // Set recommended infinite ocean values
                oceanMaterial.SetFloat("_FarOceanFadeDistance", 0.7f);
                oceanMaterial.SetFloat("_HorizonBlend", 0.9f);
                oceanMaterial.SetFloat("_DistanceWaveReduction", 0.8f);
                
                // Set far ocean color (darker atmospheric blue)
                oceanMaterial.SetColor("_FarOceanColor", new Color(0.02f, 0.2f, 0.35f, 1f));
                
                // Configure shoreline smoothing for better visual quality
                oceanMaterial.SetFloat("_FoamSmoothness", 0.4f);
                oceanMaterial.SetFloat("_ShorelineSmoothing", 0.3f);
                
                Debug.Log("Configured material for infinite ocean with shoreline smoothing");
            }
        }
        
        void OnValidate()
        {
            // Update water plane position when water level changes in editor
            if (Application.isPlaying) return;
            
            Transform waterPlane = transform.Find("InfiniteOceanPlane");
            if (waterPlane != null)
            {
                Vector3 pos = waterPlane.position;
                pos.y = waterLevel;
                waterPlane.position = pos;
            }
        }
        
        void OnDrawGizmosSelected()
        {
            // Draw water level indicator
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position + Vector3.up * waterLevel, 
                               new Vector3(planeSize, 0.1f, planeSize));
            
            // Draw far clip distance indicator
            if (targetCamera != null)
            {
                Gizmos.color = Color.blue;
                Vector3 cameraPos = targetCamera.transform.position;
                Gizmos.DrawWireSphere(cameraPos, farClipDistance);
            }
        }
    }
}