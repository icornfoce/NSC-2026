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
        public bool IsHighlighted => _isHighlighted;

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
            if (stress != null)
            {
                // Final limit = Structure base + Material modifier
                float compLimit = data.baseMaxCompression + (currentMaterial != null ? currentMaterial.compressionModifier : 0f);
                float tenLimit  = data.baseMaxTension     + (currentMaterial != null ? currentMaterial.tensionModifier : 0f);
                stress.InitializeStress(maxHP, compLimit, tenLimit);
            }

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                bool isSimulating = Simulation.Physics.SimulationManager.Instance != null && Simulation.Physics.SimulationManager.Instance.IsSimulating;
                rb.isKinematic = !isSimulating;
            }
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

            var stress = GetComponent<Simulation.Physics.StructuralStress>();

            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] == null) continue;

                if (highlighted)
                {
                    // ผสมสี Highlight เข้าไป (หรือใช้สี Highlight ไปเลย)
                    _renderers[i].material.color = highlightColor;
                }
                else
                {
                    // เมื่อเอา Highlight ออก ให้กลับไปเป็นสีที่ควรจะเป็น
                    if (stress != null && Simulation.Physics.StructuralStress.ShowHPVisualsGlobal)
                    {
                        // ถ้าเปิดระบบ Stress อยู่ ให้ Stress เป็นคนจัดการสีใหม่
                        stress.RefreshVisual();
                    }
                    else if (i < _originalColors.Count)
                    {
                        // ถ้าไม่มี Stress ให้กลับไปเป็นสีเดิมของวัสดุ
                        _renderers[i].material.color = _originalColors[i];
                    }
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
