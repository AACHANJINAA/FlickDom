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

    private LineRenderer trajectoryLine;

    private Material cubeMaterial;
    private Color originalEmissionColor;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            // 머티리얼 복사본을 가져오고 Emission 키워드를 활성화합니다.
            cubeMaterial = renderer.material;
            cubeMaterial.EnableKeyword("_EMISSION");
            originalEmissionColor = cubeMaterial.GetColor("_EmissionColor");
        }

        // 궤적(선)을 그리기 위한 LineRenderer 컴포넌트를 동적으로 추가/세팅합니다.
        trajectoryLine = GetComponent<LineRenderer>();
        if (trajectoryLine == null)
        {
            trajectoryLine = gameObject.AddComponent<LineRenderer>();
        }
        trajectoryLine.positionCount = 2; // 선의 양 끝점 (시작과 끝)
        trajectoryLine.enabled = false;   // 평소엔 끄기
        trajectoryLine.startWidth = 0.2f; // 시작 두께
        trajectoryLine.endWidth = 0.05f;  // 끝 두께 (뾰족하게)
        
        // 렌더러 머티리얼을 생성해서 넣어주고 그라데이션 색상(노랑->빨강)을 설정합니다.
        trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
        trajectoryLine.startColor = Color.yellow;
        trajectoryLine.endColor = Color.red;
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
                SetHighlight(true); // 노란빛 켜기
                trajectoryLine.enabled = true; // 궤적 선 표시 시작
            }
        }
        // 2. 마우스 왼쪽 버튼을 떼는 순간 (발사!)
        else if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
        {
            mouseEndPos = GetMousePositionOnBoard();
            isDragging = false;
            SetHighlight(false); // 노란빛 끄기
            trajectoryLine.enabled = false; // 궤적 선 끄기
            
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

            // 궤적(선) 업데이트: 당기는 방향의 반대 방향으로 날아갈 것이므로 역전(-pullVector)시킴
            Vector3 forceDirection = -pullVector;
            trajectoryLine.SetPosition(0, initialCubePos); // 선의 시작점 (큐브 원래 위치)
            trajectoryLine.SetPosition(1, initialCubePos + forceDirection); // 선의 끝점 (날아갈 방향)
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

    // 객체 발광(노란빛)을 켜고 끄는 함수
    private void SetHighlight(bool isHighlighted)
    {
        if (cubeMaterial != null)
        {
            if (isHighlighted)
            {
                // 원래 텍스처(나무 결 등)를 가리지 않도록 밝기를 은은하게(0.4배) 줄임
                cubeMaterial.SetColor("_EmissionColor", Color.yellow * 0.05f);
            }
            else
            {
                // 원래 색상으로 복구
                cubeMaterial.SetColor("_EmissionColor", originalEmissionColor);
            }
        }
    }
}