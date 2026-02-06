using UnityEngine;

namespace Com.MyCompany.MyGame
{
    public class CameraFollow : MonoBehaviour
    {
        private void Awake()
        {
            // Auto-disable if Cinemachine is present to avoid conflicts
            if (FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>() != null)
            {
                Debug.LogWarning("[CameraFollow] Disabling self because CinemachineCamera was found.");
                enabled = false;
                return;
            }
        }

        [SerializeField] private float smoothSpeed = 0.125f;
        [SerializeField] private Vector3 offset = new Vector3(0, 10, -10); // Default isometric-style offset

        private Transform target;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 desiredPosition = target.position + offset;
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            transform.position = smoothedPosition;

            transform.LookAt(target);
        }
    }
}
