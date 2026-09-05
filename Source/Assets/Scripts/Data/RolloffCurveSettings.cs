using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 롤 오프 모달의 크기 팝 애니메이션 커브를 담는 유일한 ScriptableObject
    /// 자산(2026-08-31 신설) — 이 프로젝트는 지금까지 모든 UI를 코드로 직접
    /// 짓고(프리팹/에셋 없이), `RolloffDialog`도 씬에 저장되지 않고
    /// `GameFlowBootstrap`이 매번 새로 `AddComponent`하는 컴포넌트라, 평범한
    /// `[SerializeField] AnimationCurve`는 인스펙터에서 편집해도 저장될 자리가
    /// 없었다(사용자 지적). 그 커브 값 하나만 별도 자산(Resources/
    /// RolloffCurveSettings.asset)으로 빼서, 그 자산 자체를 인스펙터에서
    /// 선택해 진짜 커브 에디터로 다듬을 수 있게 한다 — RolloffDialog.Awake()가
    /// Resources.Load로 이 값을 읽어간다(자산이 없으면 코드 안 기본값으로
    /// 대체, 방어적으로).
    /// </summary>
    public class RolloffCurveSettings : ScriptableObject
    {
        public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}
