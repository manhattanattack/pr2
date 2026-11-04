using UnityEngine;

public class VisualProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float speed;

    public void Setup(Vector3 dir, float moveSpeed)
    {
        direction = dir.normalized;
        speed = moveSpeed;
        if (direction.sqrMagnitude > 0.001f) transform.forward = direction;
    }

    private void Update()
    {
        transform.position += direction * speed * Time.deltaTime;
    }
}
