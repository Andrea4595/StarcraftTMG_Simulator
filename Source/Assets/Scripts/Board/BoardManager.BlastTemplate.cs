using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 블라스트 템플릿(지름 5" 타격 범위) ──────────────────────────
        // 사용자 요청(2026-09-06): 마커바에서 배치하는 원형 타격 범위 마커.
        // 마커 자체(위치/이동/삭제)는 일반 아이콘 마커와 완전히 같은 경로를
        // 타므로(BoardManager.Markers.cs의 "blast" kind 분기 — 되돌리기/저장/
        // 멀티플레이어 동기화가 전부 그대로 딸려온다) 여기서는 "지금 이
        // 범위 안에 어떤 유닛이 들어와 있는가"만 매 프레임 다시 계산해서
        // Base.BlastPrimary/BlastSecondary로 반영한다. 이 판정 자체는
        // 저장/동기화 대상이 아니다 — 마커 위치 + 유닛 위치(둘 다 이미
        // 동기화됨)로부터 각 클라이언트가 로컬로 그대로 다시 계산할 수
        // 있는 파생 상태이기 때문(BoardManager.Range.cs의 RefreshRangeOverlays와
        // 같은 성격).
        private const float BlastTemplateRadiusMm = GameConstants.MmPerInch * 5f / 2f;

        private readonly List<Base> _blastHighlightedPrimary = new List<Base>();
        private readonly List<Base> _blastHighlightedSecondary = new List<Base>();

        /// <summary>BoardInputController.RunFrame이 매 프레임 부른다(배치/드래그
        /// 여부와 무관하게 — 정지해 있는 블라스트 템플릿도 다른 유닛이 그
        /// 범위로 걸어 들어오면 강조가 갱신돼야 하므로). 배치 미리보기(고스트)도
        /// 포함한다 — 실제로 놓기 전부터 무엇이 걸리는지 보여주는 게 이
        /// 기능의 목적이라(사용자 설명: "범위 안에 있는 유닛들을 강조").</summary>
        internal void RefreshBlastTemplateHighlights()
        {
            foreach (var b in _blastHighlightedPrimary)
            {
                if (b)
                {
                    b.BlastPrimary = false;
                }
            }
            foreach (var b in _blastHighlightedSecondary)
            {
                if (b)
                {
                    b.BlastSecondary = false;
                }
            }
            _blastHighlightedPrimary.Clear();
            _blastHighlightedSecondary.Clear();

            if (baseLayer == null)
            {
                return;
            }

            var templateCenters = new List<Vector2>();
            foreach (var markerGo in EnumerateRealMarkers())
            {
                if (markerGo.TryGetComponent<IconMarker>(out var icon) && icon.Kind == "blast")
                {
                    templateCenters.Add(icon.Center);
                }
            }
            if (_markerPlacementPreview is IconMarker previewIcon && previewIcon.Kind == "blast")
            {
                templateCenters.Add(previewIcon.Center);
            }
            if (templateCenters.Count == 0)
            {
                return;
            }

            // 1단계: 원의 중심이 정확히 그 모델의 타원 안에 들어가는 모델을
            // "주 목표"로, 그 모델이 속한 유닛을 "주 목표 유닛"으로 삼는다
            // (스냅된 자리라면 항상 여기 걸린다 — ResolveBlastTemplateCenter/
            // FindBaseAtPoint 참고).
            var primaries = new HashSet<Base>();
            var primaryUnits = new HashSet<Unit>();
            foreach (var center in templateCenters)
            {
                var primary = FindBaseAtPoint(center);
                if (primary != null)
                {
                    primaries.Add(primary);
                    if (primary.Unit != null)
                    {
                        primaryUnits.Add(primary.Unit);
                    }
                }
            }
            foreach (var primary in primaries)
            {
                primary.BlastPrimary = true;
                _blastHighlightedPrimary.Add(primary);
            }

            // 2단계: 주 목표 자신을 뺀 나머지 모델 중, 베이스 일부라도 범위
            // 원과 겹치는 모델을 찾는다. EllipseOffsetPolygonAt(반경만큼
            // 바깥으로 부풀린 타원)에 원의 중심점이 들어있는지로 판정하면
            // "타원 테두리까지의 거리 <= 반경"과 같은 뜻이 된다 —
            // RangeOverlay가 사거리 다각형을 그릴 때 쓰는 것과 같은 근사.
            // 겹치는 모델이 주 목표와 같은 유닛 소속이면 주 목표와 똑같이
            // 흰색(BlastPrimary)으로, 다른 유닛이면 주황색(BlastSecondary)으로
            // 표시한다(사용자 요청, 2026-09-06).
            for (int i = 0; i < baseLayer.childCount; i++)
            {
                if (!baseLayer.GetChild(i).TryGetComponent<Base>(out var model) || primaries.Contains(model))
                {
                    continue;
                }
                var offsetPolygon = EllipseMath.EllipseOffsetPolygonAt(
                        model.Center, model.SizeMm, model.RotationRadians, BlastTemplateRadiusMm);
                bool inRange = false;
                foreach (var center in templateCenters)
                {
                    if (EllipseMath.PointInConvexPolygon(center, offsetPolygon))
                    {
                        inRange = true;
                        break;
                    }
                }
                if (!inRange)
                {
                    continue;
                }
                if (model.Unit != null && primaryUnits.Contains(model.Unit))
                {
                    model.BlastPrimary = true;
                    _blastHighlightedPrimary.Add(model);
                }
                else
                {
                    model.BlastSecondary = true;
                    _blastHighlightedSecondary.Add(model);
                }
            }
        }
    }
}
