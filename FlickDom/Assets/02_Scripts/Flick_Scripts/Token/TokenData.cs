using UnityEngine;

public enum MaterialType { Wood, Iron, Rubber }

[CreateAssetMenu(fileName = "NewTokenData", menuName = "FlickDom/Token Data")]
public class TokenData : ScriptableObject
{
    public MaterialType materialType;
    
    [Header("시각 효과 (Visuals)")]
    [Tooltip("이 재질을 가진 토큰에 씌울 머티리얼 (색상, 질감 등)")]
    public Material renderMaterial;
    
    [Header("물리 속성 (Physics)")]
    [Tooltip("질량: 무거울수록 남을 잘 밀어내고, 내 몸은 덜 밀립니다.")]
    public float mass = 1f;
    
    [Tooltip("공기 저항: 굴러가다 멈추는 속도. 높을수록 금방 멈춥니다.")]
    public float drag = 1f;
    
    [Tooltip("유니티에서 직접 만든 Physic Material 에셋(PM_Iron, PM_Rubber 등)을 여기에 끌어다 넣으세요.")]
    public PhysicsMaterial physicMaterial;
}
