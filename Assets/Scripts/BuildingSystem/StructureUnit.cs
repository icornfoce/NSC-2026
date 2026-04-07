using UnityEngine;
using System.Collections.Generic;
using Simulation.Data;

namespace Simulation.Building
{
    public class StructureUnit : MonoBehaviour
    {
        [SerializeField] private StructureData data;
        private float _currentHP;
        private float _rotation;

        // Highlight
        private List<Renderer> _renderers = new List<Renderer>();
        private List<Color> _originalColors = new List<Color>();
        private bool _isHighlighted;

        [Header("Highlight")]
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 0.5f, 1f);

        public StructureData Data => data;
        public float CurrentHP => _currentHP;
        public float Rotation => _rotation;

        public void Initialize(StructureData structureData, float rotation = 0f)
        {
            data = structureData;
            _currentHP = data.baseHP;
            _rotation = rotation;
            CacheRenderers();

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
            if (data.breakVFX != null)
            {
                Instantiate(data.breakVFX, transform.position, Quaternion.identity);
            }

            Destroy(gameObject);
        }
    }
}
