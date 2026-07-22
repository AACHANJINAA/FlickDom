using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(FlickMovement))]
[RequireComponent(typeof(FlickVisuals))]
public class FlickMain : MonoBehaviour
{
    private FlickMovement movement;
    private FlickVisuals visuals;

    private Vector3 mouseStartPos;
    private bool isDragging = false;
    private Vector3 initialCubePos;

    void Start()
    {
        // 큐브에 붙어있는 나머지 두 컴포넌트를 찾아서 가져옵니다.
        if (GetComponent<FlickDom.Gameplay.TurnBasedFlickPiece>() != null)
        {
            enabled = false;
            return;
        }

        movement = GetComponent<FlickMovement>();
        visuals = GetComponent<FlickVisuals>();
    }

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
                
                // 시각 효과 담당 컴포넌트에게 궤적과 노란빛을 켜라고 지시
                visuals.SetHighlight(true);
                visuals.ShowTrajectory(true);
            }
        }
        // 2. 마우스 왼쪽 버튼을 떼는 순간 (발사!)
        else if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
        {
            Vector3 mouseEndPos = GetMousePositionOnBoard();
            isDragging = false;
            
            // 시각 효과 끄기 지시
            visuals.SetHighlight(false);
            visuals.ShowTrajectory(false);
            
            // 이동(물리) 담당 컴포넌트에게 당긴 만큼의 벡터를 전달하여 발사 지시
            Vector3 dragVector = mouseStartPos - mouseEndPos;
            movement.Flick(dragVector);
        }
        // 3. 드래그 중일 때 (마우스 이동에 맞춰 큐브 이동 및 궤적 업데이트)
        else if (isDragging)
        {
            Vector3 currentMousePos = GetMousePositionOnBoard();
            Vector3 pullVector = currentMousePos - mouseStartPos;
            pullVector.y = 0; // 높이는 변경하지 않음

            // 드래그 거리 제한을 이동 컴포넌트의 설정값에서 가져와 체크
            if (pullVector.magnitude > movement.maxDragDistance)
            {
                pullVector = pullVector.normalized * movement.maxDragDistance;
            }

            // 큐브 위치 강제 업데이트 (마우스를 따라감)
            transform.position = initialCubePos + pullVector;

            // 시각 효과 담당 컴포넌트에게 궤적 업데이트 지시
            Vector3 forceDirection = -pullVector;
            visuals.UpdateTrajectory(initialCubePos, forceDirection);
        }
    }

    // 마우스의 화면 좌표를 보드판(3D 월드) 위 좌표로 변환
    private Vector3 GetMousePositionOnBoard()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane boardPlane = new Plane(Vector3.up, Vector3.zero);
        if (boardPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }
        return Vector3.zero;
    }

    // 마우스가 현재 이 큐브 위에 있는지 레이캐스트로 확인
    private bool IsMouseOverCube()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (hit.collider.gameObject == this.gameObject)
            {
                return true;
            }
        }
        return false;
    }
}
