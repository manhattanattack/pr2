using UnityEngine;
using TMPro;

public class NetworkPlayer : MonoBehaviour
{
    [Header("Ссылки на узлы")]
    [SerializeField] private Transform headPivot;
    [SerializeField] private Transform bodyMesh;
    [SerializeField] private TextMeshProUGUI nicknameText;

    [Header("Сглаживание сети")]
    [SerializeField] private float smoothSpeed = 15f;

    private Vector3 targetPosition;
    private float targetYaw;
    private float targetPitch;
    private bool targetCrouch;

    public void Initialize(string nickname, Vector3 startPos)
    {
        if (nicknameText != null)
        {
            nicknameText.text = nickname;
        }

        transform.position = startPos;
        targetPosition = startPos;
    }

    public void UpdateNetworkState(Vector3 pos, float yaw, float pitch, bool crouch)
    {
        targetPosition = pos;
        targetYaw = yaw;
        targetPitch = pitch;
        targetCrouch = crouch;
    }

    private void Update()
    {
        // Плавная интерполяция координат
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), Time.deltaTime * smoothSpeed);

        // Наклон головы с кубиком-лицом
        if (headPivot != null)
        {
            headPivot.localRotation = Quaternion.Slerp(headPivot.localRotation, Quaternion.Euler(targetPitch, 0f, 0f), Time.deltaTime * smoothSpeed);
        }

        // Отражение приседания через масштаб модели туловища
        if (bodyMesh)
        {
            float targetScaleY = targetCrouch ? 0.5f : 1f;
            Vector3 s = bodyMesh.localScale;
            s.y = Mathf.Lerp(s.y, targetScaleY, Time.deltaTime * smoothSpeed);
            bodyMesh.localScale = s;
        }
    }
}