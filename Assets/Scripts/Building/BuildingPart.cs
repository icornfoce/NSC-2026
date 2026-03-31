using UnityEngine;
using BuildingSimulation.Data;

namespace BuildingSimulation.Building
{
    /// <summary>
    /// Runtime component attached to every placed building piece.
    /// Tracks its data, current scale, material, cost, joints, ambient sound, and effects.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public class BuildingPart : MonoBehaviour
    {
        [Header("Data (assigned at spawn)")]
        [SerializeField] private BuildingPartData partData;
        [SerializeField] private BuildingMaterialData materialData;

        [Header("Runtime")]
        [SerializeField] private Vector3 currentScale = Vector3.one;

        private Rigidbody _rb;
        private Renderer[] _renderers;
        private AudioSource _ambientSource;
        private GameObject _persistentEffect;

        public BuildingPartData PartData => partData;
        public BuildingMaterialData MaterialData => materialData;
        public Vector3 CurrentScale => currentScale;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _renderers = GetComponentsInChildren<Renderer>();
        }

        /// <summary>
        /// Initialize this part after instantiation.
        /// </summary>
        public void Initialize(BuildingPartData data, BuildingMaterialData material, Vector3 scale)
        {
            partData = data;
            materialData = material;
            SetScale(scale);
            ApplyMaterial();
            ApplyPhysics();
            SetupAmbientSound();
            SetupPersistentEffect();
        }

        /// <summary>
        /// Set the scale, clamped to the part's min/max.
        /// </summary>
        public void SetScale(Vector3 newScale)
        {
            if (partData == null) return;

            currentScale = new Vector3(
                Mathf.Clamp(newScale.x, partData.minScale.x, partData.maxScale.x),
                Mathf.Clamp(newScale.y, partData.minScale.y, partData.maxScale.y),
                Mathf.Clamp(newScale.z, partData.minScale.z, partData.maxScale.z)
            );
            transform.localScale = currentScale;
        }

        /// <summary>
        /// Calculate the final cost of this piece:
        /// baseCost × materialCostMultiplier × volumeScale
        /// </summary>
        public float GetCost()
        {
            if (partData == null) return 0f;
            float volumeScale = currentScale.x * currentScale.y * currentScale.z;
            float matMul = materialData != null ? materialData.costMultiplier : 1f;
            return partData.baseCost * matMul * volumeScale;
        }

        /// <summary>
        /// Calculate mass from material density × volume.
        /// </summary>
        public float GetMass()
        {
            if (materialData == null) return 10f;
            float volume = currentScale.x * currentScale.y * currentScale.z;
            return materialData.density * volume;
        }

        /// <summary>
        /// Set gravity; called by SimulationManager.
        /// </summary>
        public void SetGravity(bool useGravity)
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.useGravity = useGravity;
            _rb.isKinematic = !useGravity;
        }

        /// <summary>
        /// Change the material of this part at runtime.
        /// </summary>
        public void UpdateMaterial(BuildingMaterialData newMaterial)
        {
            if (newMaterial == null) return;
            materialData = newMaterial;
            ApplyMaterial();
            
            // Update mass if Rigidbody exists
            if (_rb != null) _rb.mass = GetMass();
        }

        private void ApplyMaterial()
        {
            if (materialData == null || _renderers == null || _renderers.Length == 0) return;
 
            // Apply color tint to all renderers
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", materialData.materialColor);
            
            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(mpb);
                }
            }
 
            // Apply physics material to all colliders
            if (materialData.physicsMaterial != null)
            {
                foreach (var col in GetComponentsInChildren<Collider>())
                {
                    col.sharedMaterial = materialData.physicsMaterial;
                }
            }
        }

        private void ApplyPhysics()
        {
            if (_rb == null) return;
            _rb.mass = GetMass();
            _rb.useGravity = false;   // Gravity off until simulation starts
            _rb.isKinematic = true;    // Kinematic until simulation starts
        }

        // ─── Sound ────────────────────────────────────────────────────

        private void SetupAmbientSound()
        {
            if (partData == null || partData.ambientSound == null) return;

            if (SoundManager.Instance != null)
            {
                _ambientSource = SoundManager.Instance.PlayLoop(
                    partData.ambientSound, transform, partData.ambientVolume);
            }
        }

        // ─── Effect ───────────────────────────────────────────────────

        private void SetupPersistentEffect()
        {
            if (partData == null || partData.persistentEffectPrefab == null) return;

            if (EffectManager.Instance != null)
            {
                _persistentEffect = EffectManager.Instance.SpawnPersistentEffect(
                    partData.persistentEffectPrefab, transform);
            }
        }

        private void OnDestroy()
        {
            // Cleanup ambient sound
            if (_ambientSource != null && SoundManager.Instance != null)
            {
                SoundManager.Instance.StopLoop(_ambientSource);
            }

            // Cleanup persistent effect
            if (_persistentEffect != null && EffectManager.Instance != null)
            {
                EffectManager.Instance.StopEffect(_persistentEffect);
            }
        }
    }
}
