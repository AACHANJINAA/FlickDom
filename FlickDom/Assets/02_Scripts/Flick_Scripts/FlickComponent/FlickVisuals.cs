using UnityEngine;

public class FlickVisuals : MonoBehaviour
{
    private LineRenderer trajectoryLine;
    private Material cubeMaterial;
    private Color originalEmissionColor;

    void Start()
    {
        // 발광 머티리얼 설정
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            cubeMaterial = renderer.material;
            cubeMaterial.EnableKeyword("_EMISSION");
            originalEmissionColor = cubeMaterial.GetColor("_EmissionColor");
        }

        // 궤적(선) LineRenderer 설정
        trajectoryLine = GetComponent<LineRenderer>();
        if (trajectoryLine == null)
        {
            trajectoryLine = gameObject.AddComponent<LineRenderer>();
        }
        trajectoryLine.positionCount = 2;
        trajectoryLine.enabled = false;
        
        // 화살표 궤적 모양 세팅
        Keyframe[] keys = new Keyframe[4];
        keys[0] = new Keyframe(0.0f, 0.2f);
        keys[1] = new Keyframe(0.75f, 0.2f);
        keys[2] = new Keyframe(0.751f, 0.6f);
        keys[3] = new Keyframe(1.0f, 0.0f);
        trajectoryLine.widthCurve = new AnimationCurve(keys);
        trajectoryLine.widthMultiplier = 1f;
        
        trajectoryLine.material = new Material(Shader.Find("Sprites/Default"));
        trajectoryLine.startColor = Color.yellow;
        trajectoryLine.endColor = Color.red;
    }

    // 마우스 오버 / 드래그 시 큐브 발광 효과 켜고 끄기
    public void SetHighlight(bool isHighlighted)
    {
        if (cubeMaterial != null)
        {
            if (isHighlighted)
                cubeMaterial.SetColor("_EmissionColor", Color.yellow * 0.4f);
            else
                cubeMaterial.SetColor("_EmissionColor", originalEmissionColor);
        }
    }

    // 궤적 표시 켜기/끄기
    public void ShowTrajectory(bool show)
    {
        if (trajectoryLine != null)
        {
            trajectoryLine.enabled = show;
        }
    }

    // 궤적 선의 시작점과 끝점 업데이트
    public void UpdateTrajectory(Vector3 startPos, Vector3 forceDirection)
    {
        if (trajectoryLine != null)
        {
            trajectoryLine.SetPosition(0, startPos);
            trajectoryLine.SetPosition(1, startPos + forceDirection);
        }
    }
}
