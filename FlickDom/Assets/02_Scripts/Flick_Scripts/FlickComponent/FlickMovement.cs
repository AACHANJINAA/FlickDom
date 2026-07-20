using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FlickMovement : MonoBehaviour
{
    [Header("튕기기 설정")]
    public float forceMultiplier = 10f; // 튕기는 힘 조절
    public float maxDragDistance = 3f;  // 최대 드래그 허용 거리

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // 외부(FlickInput)에서 드래그 벡터를 전달받아 실제로 힘을 가하는 함수
    public void Flick(Vector3 dragVector)
    {
        // Y축(위아래)으로는 힘이 들어가지 않도록 0으로 막아줍니다.
        dragVector.y = 0;

        // 드래그 거리를 제한합니다 (안전장치)
        if (dragVector.magnitude > maxDragDistance)
        {
            dragVector = dragVector.normalized * maxDragDistance;
        }

        // Main에서 이미 당긴 반대 방향(발사될 방향)으로 계산해서 넘겨주었으므로, 그대로 힘을 가합니다.
        rb.AddForce(dragVector * forceMultiplier, ForceMode.Impulse);
    }
}
