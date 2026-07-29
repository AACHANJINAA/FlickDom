using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class TokenSetup : MonoBehaviour
{
    [Tooltip("여기에 생성한 TokenData (Wood, Iron, Rubber) 중 하나를 넣어주세요.")]
    public TokenData tokenData;

    // FlickVisuals가 Start()에서 머티리얼을 캐싱하기 전에, 먼저 머티리얼과 물리 속성을 세팅해야 하므로 Awake()를 사용합니다.
    void Awake()
    {
        if (tokenData != null)
        {
            ApplyTokenData();
        }
    }

    public void ApplyTokenData()
    {
        if (tokenData == null)
        {
            Debug.LogWarning("[TokenSetup] TokenData가 비어있습니다. 에디터에서 할당해주세요!");
            return;
        }

        // 1. 물리 속성 (질량, 저항) 적용
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.mass = tokenData.mass;
        rb.linearDamping = tokenData.drag;

        // 2. 유니티에서 직접 만든 물리 재질(PhysicMaterial) 에셋 적용
        if (tokenData.physicMaterial != null)
        {
            Collider col = GetComponent<Collider>();
            col.material = tokenData.physicMaterial;
        }

        // 3. 시각적 재질 (색상, 질감) 적용
        if (tokenData.renderMaterial != null)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer currentRenderer = renderers[i];
                if (currentRenderer == null)
                {
                    continue;
                }

                currentRenderer.sharedMaterial = tokenData.renderMaterial;
            }
        }
    }
}
