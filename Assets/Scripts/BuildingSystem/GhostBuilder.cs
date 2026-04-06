using UnityEngine;
using System.Collections.Generic;

namespace Simulation.Building
{
    /// <summary>
    /// Manages the ghost (preview) object for the building system.
    /// Handles creation, color feedback (green/red), rotation, and cleanup.
    /// </summary>
    public class GhostBuilder : MonoBehaviour
    {
        [Header("Ghost Colors")]
        [SerializeField] private Color validColor = new Color(0f, 1f, 0f, 0.5f);
        [SerializeField] private Color invalidColor = new Color(1f, 0f, 0f, 0.5f);

        private GameObject _ghostObject;
        private List<Renderer> _ghostRenderers = new List<Renderer>();
        private List<Material> _ghostMaterials = new List<Material>();
        private float _currentRotation = 0f;
        private bool _isValid = true;

        public bool HasGhost => _ghostObject != null;
        public float CurrentRotation => _currentRotation;
        public GameObject GhostObject => _ghostObject;

        /// <summary>
        /// Create a ghost preview from a prefab.
        /// Disables colliders and Rigidbodies, applies transparent material.
        /// </summary>
        public void CreateGhost(GameObject prefab)
        {
            DestroyGhost();

            _ghostObject = Instantiate(prefab);
            _ghostObject.name = "Ghost_Preview";
            _currentRotation = 0f;

            // Disable all colliders
            foreach (var col in _ghostObject.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            // Disable all Rigidbodies
            foreach (var rb in _ghostObject.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
            }

            // Remove StructureUnit if present on prefab
            var existingUnit = _ghostObject.GetComponent<StructureUnit>();
            if (existingUnit != null) Destroy(existingUnit);

            // Collect renderers and create transparent ghost materials
            _ghostRenderers.Clear();
            _ghostMaterials.Clear();

            foreach (var rend in _ghostObject.GetComponentsInChildren<Renderer>())
            {
                _ghostRenderers.Add(rend);
                
                // Use temp materials so we don't modify the prefab's materials
                Material[] sharedMaterials = rend.sharedMaterials;
                Material[] ghostMats = new Material[sharedMaterials.Length];

                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    ghostMats[i] = new Material(sharedMaterials[i]);
                    
                    // Switch to transparent rendering (Standard Shader fallback)
                    if (ghostMats[i].HasProperty("_Mode")) ghostMats[i].SetFloat("_Mode", 3);
                    
                    ghostMats[i].SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    ghostMats[i].SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    ghostMats[i].SetInt("_ZWrite", 0);
                    ghostMats[i].DisableKeyword("_ALPHATEST_ON");
                    ghostMats[i].EnableKeyword("_ALPHABLEND_ON");
                    ghostMats[i].DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    ghostMats[i].renderQueue = 3000;
                    
                    _ghostMaterials.Add(ghostMats[i]);
                }
                rend.materials = ghostMats;
            }

            SetValid(true);
        }

        /// <summary>
        /// Destroy the current ghost object.
        /// </summary>
        public void DestroyGhost()
        {
            if (_ghostObject != null)
            {
                Destroy(_ghostObject);
                _ghostObject = null;
            }
            _ghostRenderers.Clear();
            _ghostMaterials.Clear();
            _currentRotation = 0f;
        }

        /// <summary>
        /// Update the ghost position (already snapped to grid).
        /// </summary>
        public void UpdatePosition(Vector3 snappedPosition)
        {
            if (_ghostObject == null) return;
            _ghostObject.transform.position = snappedPosition;
        }

        /// <summary>
        /// Rotate the ghost by 90 degrees clockwise.
        /// </summary>
        public void Rotate()
        {
            SetRotation((_currentRotation + 90f) % 360f);
        }

        /// <summary>
        /// Set rotation to a specific angle.
        /// </summary>
        public void SetRotation(float angle)
        {
            _currentRotation = angle;
            if (_ghostObject != null)
            {
                _ghostObject.transform.rotation = Quaternion.Euler(0f, _currentRotation, 0f);
            }
        }

        /// <summary>
        /// Set validity state — changes ghost color to green (valid) or red (invalid).
        /// </summary>
        public void SetValid(bool isValid)
        {
            _isValid = isValid;
            Color targetColor = isValid ? validColor : invalidColor;

            foreach (var mat in _ghostMaterials)
            {
                mat.color = targetColor;
            }
        }

        public bool IsValid => _isValid;
    }
}
