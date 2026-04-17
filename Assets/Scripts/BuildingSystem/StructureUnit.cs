using UnityEngine;
using System.Collections.Generic;
using Simulation.Data;

namespace Simulation.Building
{
    public class StructureUnit : MonoBehaviour
    {
        [SerializeField] private StructureData data;
        [SerializeField] private MaterialData currentMaterial;
        
        private float _currentHP;
        private float _rotation;

        // Highlight
        private List<Renderer> _renderers = new List<Renderer>();
        private List<Color> _originalColors = new List<Color>();
        private bool _isHighlighted;

        [Header("Highlight")]
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 0.5f, 1f);

        public StructureData Data => data;
        public MaterialData CurrentMaterial => currentMaterial;
        public float CurrentHP => _currentHP;
        public float Rotation => _rotation;

        public void Initialize(StructureData structureData, MaterialData materialData, float rotation = 0f)
        {
            data = structureData;
            currentMaterial = materialData;

            // HP = Base + Modifier
            float maxHP = data.baseHP + (currentMaterial != null ? currentMaterial.hpModifier : 0f);
            _currentHP = maxHP;

            _rotation = rotation;
            CacheRenderers();
            ApplyMaterial();

            var stress = GetComponent<Simulation.Physics.StructuralStress>();
            if (stress == null)
            {
                stress = gameObject.AddComponent<Simulation.Physics.StructuralStress>();
            }
            stress.InitializeStress(maxHP, data.maxStress, data.forceTransferPercent);

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            
            if (data != null && data.baseMass > 0)
            {
                rb.mass = data.baseMass;
            }
            else
            {
                rb.mass = 10f; // Default mass if not specified
            }
            
            bool isSimulating = Simulation.Physics.SimulationManager.Instance != null && Simulation.Physics.SimulationManager.Instance.IsSimulating;
            rb.isKinematic = !isSimulating;
        }

        private void CacheRenderers()
        {
            _renderers.Clear();
            _originalColors.Clear();

            foreach (var rend in GetComponentsInChildren<Renderer>())
            {
                _renderers.Add(rend);
                _originalColors.Add(rend.material.color);
            }
        }

        public void ApplyMaterial()
        {
            if (currentMaterial == null || currentMaterial.material == null) return;

            foreach (var rend in _renderers)
            {
                if (rend == null) continue;
                rend.material = currentMaterial.material;
            }

            // Update original colors for highlight system
            _originalColors.Clear();
            foreach (var rend in _renderers)
            {
                _originalColors.Add(rend.material.color);
            }
        }

        public void ChangeMaterial(MaterialData newMaterial)
        {
            currentMaterial = newMaterial;
            ApplyMaterial();
        }

        public void SetRotation(float newRotation)
        {
            _rotation = newRotation;
        }

        /// <summary>
        /// Highlight this structure (e.g. when hovered in idle mode).
        /// </summary>
        public void SetHighlight(bool highlighted)
        {
            if (_isHighlighted == highlighted) return;
            _isHighlighted = highlighted;

            if (_renderers.Count == 0) CacheRenderers();

            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] == null) continue;

                if (highlighted)
                {
                    _renderers[i].material.color = highlightColor;
                }
                else if (i < _originalColors.Count)
                {
                    _renderers[i].material.color = _originalColors[i];
                }
            }
        }

        public void TakeDamage(float amount)
        {
            _currentHP -= amount;
            if (_currentHP <= 0)
            {
                DestroyStructure();
            }
        }

        public void DestroyStructure()
        {
            if (currentMaterial != null)
            {
                if (currentMaterial.breakSound != null) AudioSource.PlayClipAtPoint(currentMaterial.breakSound, transform.position);
                if (currentMaterial.breakVFX != null) Instantiate(currentMaterial.breakVFX, transform.position, Quaternion.identity);
            }

            Destroy(gameObject);
        }
    }
}
