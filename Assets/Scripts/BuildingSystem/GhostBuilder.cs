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
        private List<GameObject> _ghostInstances = new List<GameObject>();
        private List<Renderer> _ghostRenderers = new List<Renderer>();
        private List<Material> _ghostMaterials = new List<Material>();
        private float _currentRotation = 0f;
        private bool _isValid = true;

        public bool HasGhost => _ghostObject != null;
        public float CurrentRotation => _currentRotation;
        public GameObject GhostObject => _ghostObject;

        /// <summary>
        /// Create a ghost preview from a prefab.
        /// This serves as the template for multiple instances.
        /// </summary>
        public void CreateGhost(GameObject prefab)
        {
            DestroyGhost();

            // Instantiate template at origin, hidden
            _ghostObject = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _ghostObject.name = "Ghost_Template";
            _ghostObject.SetActive(false);
            _currentRotation = 0f;

            SetupGhost(_ghostObject);
            
            // Add the first instance immediately
            AddInstance(Vector3.zero);
            SetValid(true);
        }

        private void SetupGhost(GameObject obj)
        {
            // Disable all colliders
            foreach (var col in obj.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            // Disable all Rigidbodies
            foreach (var rb in obj.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
            }

            // Remove logic components
            Destroy(obj.GetComponent<StructureUnit>());
            Destroy(obj.GetComponent<Simulation.Physics.StructuralStress>());
            Destroy(obj.GetComponent<Simulation.Character.PersonTarget>());
            Destroy(obj.GetComponent<Simulation.Character.PersonSpawner>());
            Destroy(obj.GetComponent<Simulation.Character.PersonAI>());
            Destroy(obj.GetComponent<UnityEngine.AI.NavMeshAgent>());
            foreach (var j in obj.GetComponentsInChildren<Joint>()) Destroy(j);
        }

        private void AddInstance(Vector3 pos)
        {
            GameObject inst = Instantiate(_ghostObject, pos, Quaternion.Euler(0f, _currentRotation, 0f));
            inst.name = "Ghost_Instance";
            inst.SetActive(true);
            _ghostInstances.Add(inst);

            foreach (var rend in inst.GetComponentsInChildren<Renderer>())
            {
                _ghostRenderers.Add(rend);
                foreach (var mat in rend.materials)
                {
                    // Apply transparency settings to new materials
                    SetupTransparentMaterial(mat);
                    _ghostMaterials.Add(mat);
                }
            }
        }

        private void SetupTransparentMaterial(Material mat)
        {
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        /// <summary>
        /// Syncs ghost instances to a list of positions.
        /// </summary>
        public void UpdateGhosts(List<Vector3> positions, float rotation, bool isValid)
        {
            if (_ghostObject == null) return;
            _currentRotation = rotation;

            // Adjust instance count
            while (_ghostInstances.Count < positions.Count)
            {
                AddInstance(Vector3.zero);
            }
            while (_ghostInstances.Count > positions.Count)
            {
                GameObject last = _ghostInstances[_ghostInstances.Count - 1];
                _ghostInstances.RemoveAt(_ghostInstances.Count - 1);
                
                // Cleanup materials and renderers for this instance
                Renderer[] rends = last.GetComponentsInChildren<Renderer>();
                foreach(var r in rends) _ghostRenderers.Remove(r);
                // Note: material cleanup is harder without tracking which mat belongs to which instance, 
                // but SetValid will handle it as long as we clear the list and rebuild if needed.
                // For performance, we'll just clear and rebuild the mat list in SetValid.
                
                Destroy(last);
            }

            // Update positions and rotations
            for (int i = 0; i < positions.Count; i++)
            {
                _ghostInstances[i].transform.position = positions[i];
                _ghostInstances[i].transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            }

            SetValid(isValid);
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
            foreach (var inst in _ghostInstances)
            {
                if (inst != null) inst.transform.rotation = Quaternion.Euler(0f, _currentRotation, 0f);
            }
        }

        /// <summary>
        /// Update the ghost position (compatibility for single placement).
        /// </summary>
        public void UpdatePosition(Vector3 snappedPosition)
        {
            List<Vector3> pos = new List<Vector3> { snappedPosition };
            UpdateGhosts(pos, _currentRotation, _isValid);
        }

        /// <summary>
        /// Destroy the current ghost object and all instances.
        /// </summary>
        public void DestroyGhost()
        {
            if (_ghostObject != null) Destroy(_ghostObject);
            foreach (var inst in _ghostInstances) if (inst != null) Destroy(inst);
            
            _ghostInstances.Clear();
            _ghostObject = null;
            _ghostRenderers.Clear();
            _ghostMaterials.Clear();
            _currentRotation = 0f;
        }

        /// <summary>
        /// Set validity state — changes all ghost instances' color.
        /// </summary>
        public void SetValid(bool isValid)
        {
            _isValid = isValid;
            Color targetColor = isValid ? validColor : invalidColor;

            // Re-collect materials if list is empty or inconsistent
            if (_ghostMaterials.Count == 0 && _ghostRenderers.Count > 0)
            {
                foreach(var rend in _ghostRenderers)
                {
                    if (rend == null) continue;
                    foreach(var mat in rend.materials) _ghostMaterials.Add(mat);
                }
            }

            for (int i = _ghostMaterials.Count - 1; i >= 0; i--)
            {
                if (_ghostMaterials[i] == null) { _ghostMaterials.RemoveAt(i); continue; }
                _ghostMaterials[i].color = targetColor;
            }
        }

        public bool IsValid => _isValid;
    }
}
