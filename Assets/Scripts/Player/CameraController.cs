using UnityEngine;

namespace Simulation.Camera
{
    public class CameraController : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField] private float orbitSensitivity = 0.3f;
        [SerializeField] private float minVerticalAngle = -80f;
        [SerializeField] private float maxVerticalAngle = 80f;

        [Header("Zoom")]
        [SerializeField] private float zoomSensitivity = 2f;
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 100f;
        [SerializeField] private float zoomSmoothing = 10f;

        [Header("Pan")]
        [SerializeField] private float panSensitivity = 0.01f;

        [Header("Initial")]
        [SerializeField] private Vector3 pivotPoint = Vector3.zero;
        [SerializeField] private float initialDistance = 20f;
        [SerializeField] private float initialYaw = 45f;
        [SerializeField] private float initialPitch = 30f;

        private float _yaw;
        private float _pitch;
        private float _currentDistance;
        private float _targetDistance;

        private void Start()
        {
            _yaw = initialYaw;
            _pitch = initialPitch;
            _currentDistance = initialDistance;
            _targetDistance = initialDistance;
            UpdateCameraPosition();
        }

        private void LateUpdate()
        {
            HandleOrbit();
            HandleZoom();
            HandlePan();
            UpdateCameraPosition();
        }

        private void HandleOrbit()
        {
            if (!Input.GetMouseButton(1)) return; // Right Mouse Button

            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            _yaw += dx * orbitSensitivity * 100f * Time.deltaTime;
            _pitch -= dy * orbitSensitivity * 100f * Time.deltaTime;
            _pitch = Mathf.Clamp(_pitch, minVerticalAngle, maxVerticalAngle);
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            _targetDistance -= scroll * zoomSensitivity * _currentDistance * 0.3f;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, zoomSmoothing * Time.deltaTime);
        }

        private void HandlePan()
        {
            if (!Input.GetMouseButton(2)) return; // Middle Mouse Button

            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            Vector3 right = transform.right;
            Vector3 up = transform.up;

            pivotPoint -= (right * dx + up * dy) * panSensitivity * _currentDistance;
        }

        private void UpdateCameraPosition()
        {
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, zoomSmoothing * Time.deltaTime);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);

            transform.position = pivotPoint + offset;
            transform.LookAt(pivotPoint);
        }

        public void FocusOn(Vector3 point)
        {
            pivotPoint = point;
        }
    }
}
