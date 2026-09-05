using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 블라스트 템플릿이 깔려있는 동안 반복 재생되는 펄스 원 FX의 타이밍/모양을
    /// 담는 ScriptableObject 자산(2026-09-06 신설, 사용자 요청) —
    /// EmoteScaleCurveSettings와 같은 이유로 별도 자산으로 뺐다(BoardManager는
    /// 씬에 저장 안 되고 매번 코드로 AddComponent되는 컴포넌트라 평범한
    /// [SerializeField]는 편집한 값이 저장될 자리가 없다). 이 자산
    /// (Resources/BlastFxSettings.asset) 자체를 선택해 진짜 커브 에디터로
    /// 다듬을 수 있다 — BoardManager.BlastTemplate.cs가 Resources.Load로
    /// 읽어간다(자산이 없으면 코드 안 기본값으로 대체, 방어적으로).
    /// </summary>
    public class BlastFxSettings : ScriptableObject
    {
        /// <summary>펄스 원 하나가 새로 생성되는 간격(초).</summary>
        public float spawnIntervalSeconds = 0.3f;

        /// <summary>펄스 원 하나가 생성된 뒤 사라지기까지 걸리는 시간(초) —
        /// spawnIntervalSeconds와 다른 값을 주면(예: 이 값이 더 크면) 여러
        /// 펄스가 겹쳐서 동시에 재생된다.</summary>
        public float lifetimeSeconds = 0.3f;

        /// <summary>펄스 원이 가장 커졌을 때(sizeCurve 값이 1일 때) 블라스트
        /// 템플릿 자체 크기(5")의 몇 배가 되는지 — 매 프레임 실제로 읽힌다
        /// (사용자 확인, 2026-09-06: sizeCurve는 절대 크기가 아니라 이
        /// 배율과 템플릿 원래 크기 사이를 보간하는 0~1 진행값이다).</summary>
        public float maxScaleMultiplier = 1.1f;

        /// <summary>펄스 원의 크기를 진행도(0~1, 생애주기 기준)에 따라 결정하는
        /// "0(=템플릿 원래 크기 5") ~ 1(=5" × maxScaleMultiplier)" 사이의
        /// 정규화된 보간값 커브 — 커브 자체가 절대 인치 값을 담는 게
        /// 아니다. 기본값은 단순 선형(0→1)이며, 실제 "확 커졌다 멈추는"
        /// 모양은 이 자산을 선택해 커브 에디터에서 직접 다듬는다.</summary>
        public AnimationCurve sizeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>펄스 원의 알파값을 진행도(0~1)에 따라 직접 담는 커브 —
        /// 기본값은 단순 선형(0→1)이며, 실제 "옅어졌다 사라지는" 모양(예:
        /// 0→0.5→0)은 이 자산을 선택해 커브 에디터에서 직접 다듬는다.</summary>
        public AnimationCurve alphaCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }
}
