using UnityEngine;

namespace Simulation.Character
{
    /// <summary>
    /// สคริปต์สำหรับแปะที่ Prefab คนที่ให้ผู้เล่นวาง (Target Marker)
    /// จะทำให้ตัวมันใสๆ (Transparent) เมื่อถูกวางลงในฉาก
    /// </summary>
    public class PersonTarget : MonoBehaviour
    {
        [Header("Transparency")]
        [SerializeField] private float alpha = 0.15f;
        [SerializeField] private float fadeSpeed = 5f;

        private Material[] _materials;
        private float _currentAlpha;

        private void Start()
        {
            _currentAlpha = alpha;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            System.Collections.Generic.List<Material> mats = new System.Collections.Generic.List<Material>();

            foreach (var r in renderers)
            {
                if (r.material != null)
                {
                    // เปลี่ยน Rendering Mode เป็น Transparent (เฉพาะ Standard Shader)
                    r.material.SetFloat("_Mode", 3);
                    r.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    r.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    r.material.SetInt("_ZWrite", 0);
                    r.material.DisableKeyword("_ALPHATEST_ON");
                    r.material.EnableKeyword("_ALPHABLEND_ON");
                    r.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    r.material.renderQueue = 3000;

                    Color c = r.material.color;
                    c.a = _currentAlpha;
                    r.material.color = c;

                    mats.Add(r.material);
                }
            }
            _materials = mats.ToArray();

            // ปิด Collider ไม่ให้เป็นส่วนหนึ่งของฟิสิกส์สิ่งก่อสร้าง
            Collider[] cols = GetComponentsInChildren<Collider>();
            foreach (var col in cols)
            {
                col.isTrigger = true; 
            }
        }

        private void Update()
        {
            // หาเป้าหมายความทึบตามสถานะของการจำลอง
            bool isSimulating = Simulation.Physics.SimulationManager.Instance != null && Simulation.Physics.SimulationManager.Instance.IsSimulating;
            float targetAlpha = isSimulating ? 0f : alpha;

            // ค่อยๆ Fade สี
            if (Mathf.Abs(_currentAlpha - targetAlpha) > 0.001f)
            {
                _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
                
                foreach (var mat in _materials)
                {
                    if (mat != null)
                    {
                        Color c = mat.color;
                        c.a = _currentAlpha;
                        mat.color = c;
                    }
                }
            }
        }
    }
}
