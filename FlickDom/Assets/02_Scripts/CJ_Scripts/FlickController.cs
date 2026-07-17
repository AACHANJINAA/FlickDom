using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class FlickController : MonoBehaviour
{
    [Header("튕기기 설정")]
    public float forceMultiplier = 10f; // 튕기는 힘 조절
    public float maxDragDistance = 3f;  // 최대 드래그 허용 거리 (너무 세게 튕기지 않도록)

    private Rigidbody rb;
    private Vector3 mouseStartPos;
    private Vector3 mouseEndPos;
    private bool isDragging = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (Mouse.current == null) return;

        // 1. 마우스 왼쪽 버튼을 누르는 순간 (드래그 시작)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // 현재 마우스가 위치한 바닥(Plane)의 3D 좌표를 구합니다.
            mouseStartPos = GetMousePositionOnBoard();
            isDragging = true;
        }

        // 2. 마우스 왼쪽 버튼을 떼는 순간 (발사!)
        if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
        {
            mouseEndPos = GetMousePositionOnBoard();
            isDragging = false;
            
            Flick(); // 튕기기 실행
        }
    }

    private void Flick()
    {
        // 당긴 방향의 반대 방향으로 힘을 가하기 위해 (시작점 - 끝점)을 계산합니다.
        // (당구 큐대나 앵그리버드처럼 뒤로 당겨서 쏘는 방식)
        Vector3 forceVector = mouseStartPos - mouseEndPos;
        
        // Y축(위아래)으로는 힘이 들어가지 않도록 0으로 막아줍니다.
        forceVector.y = 0;

        // 드래그 거리를 제한합니다 (너무 멀리 당겨서 초고속으로 날아가는 것 방지)
        if (forceVector.magnitude > maxDragDistance)
        {
            forceVector = forceVector.normalized * maxDragDistance;
        }

        // 오브젝트에 물리적인 힘(Impulse)을 가합니다.
        rb.AddForce(forceVector * forceMultiplier, ForceMode.Impulse);
    }

    // 마우스의 2D 화면 좌표를 3D 보드판 위 좌표로 변환해주는 함수
    private Vector3 GetMousePositionOnBoard()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        // 바닥(Y=0)을 기준으로 평면을 만듭니다.
        Plane boardPlane = new Plane(Vector3.up, Vector3.zero);
        
        if (boardPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }
        return Vector3.zero;
    }
}