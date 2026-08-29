using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 로스터 JSON 임포트 / 토큰 ────────────────────────────────────

        private const float TokenColorSaturationFactor = 0.45f;
        private const float TokenColorValueFactor = 0.9f;

        private void ImportRoster(string team)
        {
            if (rosterFileDialog == null)
            {
                return;
            }
            _rosterImportTeam = team;
            rosterFileDialog.Open();
        }

        private void OnRosterFileSelected(string path)
        {
            string jsonText;
            try
            {
                jsonText = System.IO.File.ReadAllText(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"로스터 파일을 열 수 없습니다: {path} ({e.Message})");
                return;
            }

            if (!RosterImporter.TryImport(jsonText, _rosterImportTeam, out var units, out var tokens, out var tacticalCards, out var error))
            {
                Debug.LogWarning($"로스터 파일 형식이 올바르지 않습니다: {path} — {error}");
                return;
            }

            _pendingUnits.AddRange(units);
            _pendingRosterTokens.AddRange(tokens);
            _pendingTacticalCards.AddRange(tacticalCards);
            // 임포트 자체가 "로드됨" 확정 신호 — 방금 불러온 파일이 유닛/토큰을
            // 하나도 안 줬어도(형식은 맞았지만 내용이 빈 롤스터), 버튼을 다시
            // 안 보여준다. RefreshPendingList/RefreshRosterTokenList가 끝에서
            // 부르는 RefreshPanelLayout()이 이 집합을 보고 버튼/목록을 정리한다.
            _rosterLoadedTeams.Add(_rosterImportTeam);
            RefreshPendingList();
            RefreshRosterTokenList();
            RefreshTacticalCardList();
        }

        /// <summary>토큰 정의는 유닛과 달리 목록에서 지우지 않는다 — 몇 번이든
        /// 다시 배치 가능해야 하므로. 실제 배치는 지도 배경 클릭에서 시작한다
        /// (HandlePendingRosterTokenInput → BeginRosterTokenPlacement).</summary>
        private void StartRosterTokenPlacement(int index)
        {
            if (_unitMoveActive || _pendingDeploymentDef != null || index < 0 || index >= _pendingRosterTokens.Count)
            {
                return;
            }
            _pendingRosterTokenDef = _pendingRosterTokens[index];
            var fillColor = GameConstants.TeamColors.TryGetValue(_pendingRosterTokenDef.Team, out var c) ? c : GameConstants.TeamColors["neutral"];
            ShowBasePlacementPreview(_pendingRosterTokenDef.SizeMm,
                    GameConstants.Muted(fillColor, TokenColorSaturationFactor, TokenColorValueFactor),
                    _pendingRosterTokenDef.IsDisplacement);
        }

        private void HandlePendingRosterTokenInput()
        {
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
            {
                // 빈 곳 우클릭 — 배치 취소. 트랜잭션은 실제로 지도를 클릭할 때
                // (BeginRosterTokenPlacement)까지 시작하지 않으므로 버릴 것도 없다.
                _pendingRosterTokenDef = null;
                ClearPlacementPreview();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                if (TryGetLocalMouse(out var clickPoint))
                {
                    BeginRosterTokenPlacement(clickPoint);
                }
                return;
            }

            if (TryGetLocalMouse(out var mouseLocal))
            {
                UpdatePlacementPreviewPosition(mouseLocal);
            }
        }

        /// <summary>배치 직후 바로 일반 드래그로 이어지므로(아래 _draggingPiece),
        /// 커밋은 EndPieceDrag()의 드래그 종료 지점에서 자연히 이루어진다.
        /// _unitMoveActive를 켜지 않으므로 코헤런시/리딩-팔로워 흐름은 전혀
        /// 타지 않는다 — 이후로는 일반 베이스 드래그와 완전히 동일하게
        /// 처리된다(변위 베이스 관통, 놓을 때 겹친 변위 베이스 밀어내기 포함).</summary>
        private void BeginRosterTokenPlacement(Vector2 clickPoint)
        {
            BeginUndoTransaction();
            ClearPlacementPreview();

            var def = _pendingRosterTokenDef;
            _pendingRosterTokenDef = null;

            // 같은 이름+팀의 토큰이 이미 지도 위에 있으면 새 Unit을 만들지 않고
            // 그 Unit에 모델만 추가한다 — "동일한 다른 토큰과 한 유닛으로 취급".
            string key = $"{def.Team}|{def.Name}";
            if (!_rosterTokenUnits.TryGetValue(key, out var unit))
            {
                unit = new Unit { UnitName = def.Name, Team = def.Team, IsToken = true };
                _rosterTokenUnits[key] = unit;

                // 이 토큰이 처음 배치되는 순간(=새 Unit)에만 로스터에 미리 등록된
                // 범위를 "범위 표시"에 넣는다 — 같은 토큰을 또 배치해도 같은
                // Unit에 모델만 늘어날 뿐이므로 여기서 또 등록하면 중복된다.
                if (def.Ranges.Count > 0)
                {
                    _unitRanges[unit] = new List<RangeSpec>(def.Ranges);
                    RefreshRangeOverlays();
                }
            }

            var fillColor = GameConstants.TeamColors.TryGetValue(def.Team, out var c) ? c : GameConstants.TeamColors["neutral"];
            var piece = CreatePieceObject(unit, def.SizeMm, GameConstants.Muted(fillColor, TokenColorSaturationFactor, TokenColorValueFactor), def.IsDisplacement);
            unit.Models.Add(piece);
            piece.Center = ResolvePosition(piece, clickPoint, true);
            piece.Refresh();

            _draggingPiece = piece;
            TryGetLocalMouse(out var mouseLocal);
            _dragOffset = piece.Center - mouseLocal;
            piece.transform.SetAsLastSibling();
        }

        /// <summary>지도 배경 클릭으로 실제 리딩 모델을 만들고, 곧바로 일반
        /// 유닛-이동의 "리딩 모델 드래그" 상태로 들어간다 — 이후로는
        /// EndPieceDrag()/FinishLeadingMove()가 일반 유닛 이동과 동일하게
        /// 처리한다(변위 베이스 밀어내기 포함).</summary>
        private void BeginDeploymentDrag(Vector2 clickPoint)
        {
            // 고스트 미리보기 단계에서 휠로 돌려놨을 수 있는 회전을 실제
            // 리딩 모델로 이어받는다 — 지우기 전에 먼저 읽어둔다.
            float previewRotation = _placementPreview != null ? _placementPreview.RotationDegrees : 0f;
            ClearPlacementPreview();
            // 배치구역 밴드(guideline.BandPolylines)는 여기서 지우지 않는다 —
            // Shift 스냅(ResolveLeadingDragCenter)이 리딩 모델을 드래그하는
            // 동안 이 밴드 경계를 계속 참조해야 한다. 팔로워 단계로 넘어가면
            // UpdateUnitMoveGuideline()이 코헤런시 링으로 자연히 덮어쓰고,
            // 이동이 완전히 끝나면 EndUnitMove()의 guideline.ClearBand()가
            // 정리한다.

            var def = _pendingDeploymentDef;
            _pendingDeploymentDef = null;

            var unit = new Unit
            {
                UnitName = def.Name,
                Team = def.Team,
                CoherencyInch = def.CoherencyInch,
                MoveInch = def.MoveInch,
                CanMove = def.CanMove,
            };
            unit.SupplyTiers.AddRange(def.SupplyTiers);
            unit.SupplyOverride = def.SupplyOverride;
            unit.Detail = def.Detail;

            var leading = CreatePieceObject(unit, def.SizeMm, def.FillColor, def.IsDisplacement);
            leading.RotationDegrees = previewRotation;
            // "유닛 되돌리기"로 되돌아온 유닛은 데미지 기록을 유지한 채 재배치된다.
            if (def.Damages.Count > 0)
            {
                leading.Damage = def.Damages[0];
            }
            // 로스터 "specialists"의 0번째는 리더 모델이 받는다(그 뒤 순서는
            // SpawnDeploymentFollowers가 팔로워에게 이어서 배정).
            if (def.Specialists.Count > 0)
            {
                leading.Memo = def.Specialists[0];
            }
            unit.Models.Add(leading);

            // 로스터에 미리 등록된 범위 표시가 있으면(또는 "유닛 되돌리기"로
            // 유지해온 것이면) 배치와 동시에 다시 등록한다 — 다이얼 메뉴로
            // 하나하나 추가한 것과 동일하게 취급된다.
            if (def.Ranges.Count > 0)
            {
                _unitRanges[unit] = new List<RangeSpec>(def.Ranges);
            }

            // 나머지 모델은 아직 만들지 않는다 — 리딩 모델이 실제로 놓이기 전까지는
            // 클릭 지점에 겹쳐서 충돌 해소를 방해하게 된다.
            _pendingFollowerCount = Mathf.Max(def.ModelCount - 1, 0);
            _deploymentDefSnapshot = def;

            _unitMoveActive = true;
            _unitMoveIsDeployment = true;
            _unitMoveLeading = leading;
            _unitMoveUnit = unit;
            _unitMovePhase = "leading";
            _unitMoveOriginalPositions.Clear();

            // 배치구역/이동거리 밴드는 참고용으로만 보여주고, 실제 배치 위치는
            // 자유롭게 아무 데나 놓을 수 있다 — 충돌 회피와 지도 경계만 지킨다.
            // 리딩 모델 이동이므로 변위 베이스는 통과할 수 있다.
            //
            // 배치는 "누른 채로 드래그"가 아니라 클릭 한 번으로 끝나는 경우가
            // 많다 — 마우스를 바로 떼면 Update()의 _draggingPiece 드래그-갱신
            // 블록(스냅 계산이 있는 곳)이 GetMouseButtonUp 체크에 걸려 한 번도
            // 실행되지 못한 채 EndPieceDrag()로 바로 넘어간다. 그래서 Shift
            // 스냅은 여기, 최초 배치 지점에도 똑같이 적용해야 한다 — 위의
            // _unitMoveActive/_unitMoveIsDeployment/_unitMoveLeading/_unitMovePhase가
            // 이미 다 설정된 뒤라 ResolveLeadingDragCenter가 정상 동작한다.
            var initialCenter = ResolveLeadingDragCenter(clickPoint);
            leading.Center = ResolvePosition(leading, initialCenter, true);
            leading.Refresh();

            _draggingPiece = leading;
            TryGetLocalMouse(out var mouseLocal);
            _dragOffset = leading.Center - mouseLocal;
            leading.transform.SetAsLastSibling();
        }

        private void ShowBasePlacementPreview(Vector2 sizeMm, Color fillColor, bool isDisplacement)
        {
            ClearPlacementPreview();
            var go = new GameObject("PlacementPreview", typeof(RectTransform));
            go.transform.SetParent(baseLayer, false);
            var preview = go.AddComponent<Base>();
            preview.SizeMm = sizeMm;
            var mutedColor = fillColor;
            mutedColor.a *= 0.5f;
            preview.FillColor = mutedColor;
            preview.IsDisplacement = isDisplacement;
            preview.raycastTarget = false;
            preview.Refresh();
            _placementPreview = preview;
        }

        /// <summary>고스트 미리보기는(실제 배치와 달리) ResolvePosition을 안
        /// 거친다 — 충돌 회피는 일부러 안 한다(그냥 위치를 미리 보여주는
        /// 용도). 하지만 지도 경계는 항상 지켜야 한다 — 예전엔 이 클램프
        /// 자체가 아예 없어서, 배치 밴드 스냅(Shift)이 지도 경계 근처의 밴드
        /// 가장자리로 고스트를 밀어줄 때 지도 밖으로 나가는 버그가 있었다
        /// (사용자가 실제로 겪음). EllipseMath.ClampToMapBounds를 재사용한다
        /// — ResolvePosition의 경계 클램프와 같은 함수.</summary>
        private void UpdatePlacementPreviewPosition(Vector2 localMouse)
        {
            if (_placementPreview != null)
            {
                _placementPreview.Center = EllipseMath.ClampToMapBounds(localMouse, _placementPreview.SizeMm, _placementPreview.RotationRadians, mapSizeMm);
            }
        }

        private void ClearPlacementPreview()
        {
            if (_placementPreview != null)
            {
                Destroy(_placementPreview.gameObject);
                _placementPreview = null;
            }
        }

        /// <summary>배치 밴드를 참고용으로 보여준다 — MissionData에 이 팀의
        /// 배치구역이 있으면 실제 구역(들)을 "약통" 모양(양 끝이 둥근) 폴리곤
        /// 으로, 없으면 지도 전체 가장자리 안쪽 테두리를 폴백으로 보여준다.
        /// 실제 배치 위치는 이 밴드에 제약받지 않는다 — 충돌 회피와 지도
        /// 경계만 지키면 어디든 놓을 수 있다(딥 스트라이크 등 예외를 일일이
        /// 모델링하는 대신 플레이어가 규칙에 맞게 직접 배치하도록 맡긴다).</summary>
        private void ShowDeploymentBand(PendingUnitDef def)
        {
            float radius = EllipseMath.BoundingRadius(def.SizeMm);
            float moveMm = def.MoveInch * GameConstants.MmPerInch;

            var segments = TeamZoneSegments(def.Team);
            var polylines = new List<Vector2[]>();

            if (segments.Count == 0)
            {
                polylines.Add(FallbackEdgeBandPolyline(radius, moveMm));
            }
            else
            {
                float visualDepth = radius * 2f + moveMm;
                foreach (var zone in segments)
                {
                    var capsule = BuildCapsulePolygon(zone.Edge, zone.StartAlong, zone.EndAlong, visualDepth);
                    var closed = new Vector2[capsule.Length + 1];
                    capsule.CopyTo(closed, 0);
                    closed[capsule.Length] = capsule[0];
                    polylines.Add(closed);
                }
            }

            guideline.BandPolylines = polylines;
        }

        private void ClearDeploymentBand()
        {
            guideline.BandPolylines = new List<Vector2[]>();
        }

        private static List<DeploymentZoneData> TeamZoneSegments(string team)
        {
            var result = new List<DeploymentZoneData>();
            if (!MapData.HasData)
            {
                return result;
            }
            foreach (var zone in MapData.DeploymentZones)
            {
                if (zone.Player == team)
                {
                    result.Add(zone);
                }
            }
            return result;
        }

        private Vector2[] FallbackEdgeBandPolyline(float radius, float moveMm)
        {
            float inset = radius + moveMm + radius;
            var p = new Vector2(inset, inset);
            var s = new Vector2(Mathf.Max(mapSizeMm.x - inset * 2f, 0f), Mathf.Max(mapSizeMm.y - inset * 2f, 0f));
            return new[]
            {
                CornerToCenterMm(p),
                CornerToCenterMm(p + new Vector2(s.x, 0f)),
                CornerToCenterMm(p + s),
                CornerToCenterMm(p + new Vector2(0f, s.y)),
                CornerToCenterMm(p),
            };
        }

        /// <summary>구간 [a,b]에서 depth만큼 보드 안쪽으로 뻗은 "약통" 모양(양
        /// 끝은 컴퍼스로 그린 것처럼 둥글게) 외곽선. 지도 가장자리 쪽은 닫지
        /// 않아도 된다 — 밴드를 그릴 때 마지막 점을 첫 점과 이어서 자연히
        /// 가장자리를 따라 닫히게 한다(호출부에서 처리). 여러 구역을 실제
        /// 다각형 하나로 합치진 않지만(Godot판의 Geometry2D.merge_polygons에
        /// 대응하는 Unity 다각형 불리언 유틸이 없음), GuidelineOverlay가 겹치는
        /// 구간의 윤곽선을 그리지 않아서 시각적으로는 병합된 것처럼 보인다
        /// (RangeOverlay의 범위 겹침 처리와 같은 트릭).</summary>
        private Vector2[] BuildCapsulePolygon(string edge, float a, float b, float depth)
        {
            const int steps = 16;
            var points = new Vector2[(steps + 1) * 2];
            int idx = 0;
            for (int i = 0; i <= steps; i++)
            {
                float t = 180f - 90f * i / (float)steps;
                float rad = t * Mathf.Deg2Rad;
                points[idx++] = CornerToCenterMm(ClampToMap(LocalToWorld(edge, new Vector2(a + depth * Mathf.Cos(rad), depth * Mathf.Sin(rad)))));
            }
            for (int i = 0; i <= steps; i++)
            {
                float t = 90f - 90f * i / (float)steps;
                float rad = t * Mathf.Deg2Rad;
                points[idx++] = CornerToCenterMm(ClampToMap(LocalToWorld(edge, new Vector2(b + depth * Mathf.Cos(rad), depth * Mathf.Sin(rad)))));
            }
            return points;
        }

        /// <summary>p = (along, depth-into-board)를 지도 로컬 mm 좌표(모서리-원점,
        /// [0,mapSize] 범위)로 변환한다 — 가장자리를 기준으로 한 계산이라
        /// 모서리-원점 쪽이 자연스럽다. 실제 렌더링(baseLayer/guideline)은
        /// 중심-원점이므로, 이 함수의 결과는 항상 CornerToCenterMm()을 거쳐야
        /// 한다(호출부인 BuildCapsulePolygon에서 처리).</summary>
        private Vector2 LocalToWorld(string edge, Vector2 p)
        {
            switch (edge)
            {
                case "left":
                    return new Vector2(p.y, p.x);
                case "right":
                    return new Vector2(mapSizeMm.x - p.y, p.x);
                case "top":
                    return new Vector2(p.x, p.y);
                case "bottom":
                    return new Vector2(p.x, mapSizeMm.y - p.y);
                default:
                    return Vector2.zero;
            }
        }

        private Vector2 ClampToMap(Vector2 p)
        {
            return new Vector2(Mathf.Clamp(p.x, 0f, mapSizeMm.x), Mathf.Clamp(p.y, 0f, mapSizeMm.y));
        }

        /// <summary>모서리-원점([0,mapSize]) mm 좌표를 baseLayer/guideline이 쓰는
        /// 중심-원점([-mapSize/2,+mapSize/2]) 좌표로 바꾼다 — 배치 밴드(약통
        /// 모양 배치구역, 폴백 가장자리 밴드) 계산은 지도 가장자리를 기준으로
        /// 하는 게 자연스러워 내부적으로 모서리-원점을 쓰지만, 실제로 그리는
        /// 대상(GuidelineOverlay)은 중심-원점이라 결과를 넘기기 전에 항상 이
        /// 변환을 거쳐야 한다.</summary>
        private Vector2 CornerToCenterMm(Vector2 cornerPoint)
        {
            return cornerPoint - mapSizeMm / 2f;
        }

    }
}
