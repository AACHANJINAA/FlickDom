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

    private Vector3 initialCubePos;

    void Update()
    {
        if (Mouse.current == null) return;

        // 1. 마우스 왼쪽 버튼을 누르는 순간 (드래그 시작)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (IsMouseOverCube())
            {
                mouseStartPos = GetMousePositionOnBoard();
                initialCubePos = transform.position;
                isDragging = true;
            }
        }
        // 2. 마우스 왼쪽 버튼을 떼는 순간 (발사!)
        else if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
        {
            mouseEndPos = GetMousePositionOnBoard();
            isDragging = false;
            
            Flick(); // 튕기기 실행
        }
        // 3. 드래그 중일 때 큐브가 마우스를 따라가도록 이동
        else if (isDragging)
        {
            Vector3 currentMousePos = GetMousePositionOnBoard();
            Vector3 pullVector = currentMousePos - mouseStartPos;
            pullVector.y = 0; // 높이는 변경하지 않음

            // 드래그 거리 제한
            if (pullVector.magnitude > maxDragDistance)
            {
                pullVector = pullVector.normalized * maxDragDistance;
            }

            // 큐브 위치 업데이트
            transform.position = initialCubePos + pullVector;
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

    // 마우스가 현재 이 큐브 위에 있는지 확인하는 함수
    private bool IsMouseOverCube()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // 부딪힌 객체가 자기 자신(이 스크립트가 붙은 큐브)인지 확인
            if (hit.collider.gameObject == this.gameObject)
            {
                return true;
            }
        }
        return false;
    }
}