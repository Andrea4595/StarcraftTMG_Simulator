using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 맵 이모트의 X/Y 스케일 애니메이션 커브를 담는 ScriptableObject 자산
    /// (2026-09-03 신설) — BoardManager도 씬에 저장 안 되고 매번 코드로
    /// AddComponent되는 컴포넌트라, 평범한 [SerializeField] AnimationCurve는
    /// 인스펙터에서 편집해도 저장될 자리가 없다(RolloffCurveSettings와 같은
    /// 이유). 이 자산(Resources/EmoteScaleCurveSettings.asset) 자체를 선택해
    /// 진짜 커브 에디터로 다듬을 수 있게 한다 — BoardManager.Emote.cs가
    /// Resources.Load로 읽어간다(자산이 없으면 코드 안 기본값으로 대체,
    /// 방어적으로).
    /// </summary>
    public class EmoteScaleCurveSettings : ScriptableObject
    {
        public AnimationCurve scaleCurveX = AnimationCurve.Constant(0f, 1f, 1f);
        public AnimationCurve scaleCurveY = AnimationCurve.Constant(0f, 1f, 1f);
    }
}
