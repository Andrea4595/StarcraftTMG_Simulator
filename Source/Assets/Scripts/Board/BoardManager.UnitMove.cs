using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 유닛 이동 워크플로우 ────────────────────────────────────────

        /// <summary>유닛 이동(리딩 모델)/팔로워 배치 중, draggedPiece(지금
        /// 드래그하고 있는 그 모델)의 인게이지 거리(1") 안에 있는 적 모델을
        /// 붉게 반짝이도록 표시한다(사용자 요청 — 룰북상 적 인게이지 거리
        /// 안으로 그냥 이동해 들어갈 수 없다는 걸 드래그하는 동안 바로
        /// 보여준다). draggedPiece가 null이면(드래그 중이 아니면) 그냥
        /// 전부 끈다. 매 프레임 다시 계산한다 — 드래그하는 동안 다른 적
        /// 모델은 안 움직이지만, draggedPiece 자신은 계속 움직이므로.</summary>
        private void UpdateEngageWarning(Base draggedPiece)
        {
            foreach (var model in _engageWarningHighlighted)
            {
                if (model)
                {
                    model.EngageWarning = false;
                }
            }
            _engageWarningHighlighted.Clear();

            if (draggedPiece == null || draggedPiece.Unit == null)
            {
                return;
            }

            foreach (var piece in _pieces)
            {
                if (piece == null || piece == draggedPiece || piece.Unit == null)
                {
                    continue;
                }
                if (piece.Unit.Team == draggedPiece.Unit.Team || piece.Unit.IsToken)
                {
                    continue;
                }
                float dist = EllipseMath.EllipseToEllipseDistance(
                        draggedPiece.Center, draggedPiece.SizeMm, draggedPiece.RotationRadians,
                        piece.Center, piece.SizeMm, piece.RotationRadians);
                if (dist <= EngageDistanceMm)
                {
                    piece.EngageWarning = true;
                    _engageWarningHighlighted.Add(piece);
                }
            }
        }

        public void StartUnitMove(Base leading)
        {
            if (leading == null || leading.Unit == null || _unitMoveActive)
            {
                return;
            }

            // 리딩 모델 배치부터 완료(또는 배치 중 취소)까지 전체를 되돌리기 한
            // 단계로 묶는다 — CompleteUnitMove()에서 커밋, CancelUnitMove()에서는
            // 폐기(원래 상태로 이미 되돌렸으므로 별도 되돌리기 단계가 필요 없음).
            BeginUndoTransaction($"{DescribeUnit(leading.Unit)} 이동", leading.Unit?.Team);

            _unitMoveActive = true;
            _unitMoveLeading = leading;
            _unitMoveUnit = leading.Unit;
            _unitMovePhase = "leading";
            _unitMoveStartPoint = leading.Center;

            _unitMoveOriginalPositions.Clear();
            foreach (var model in _unitMoveUnit.Models)
            {
                _unitMoveOriginalPositions[model] = model.Center;
            }

            // 리딩 모델은 이제 한 번의 드래그로 끝나지 않는다 — 뗄 때마다
            // (CommitLeadingWaypoint) 그 지점이 웨이포인트로 쌓이고, 다시
            // 집어서 계속 옮길 수 있다. "이동 확정" 버튼(팔로워 단계 전용이던
            // 패널을 여기서부터 띄운다)을 눌러야 FinishLeadingMove로 넘어간다
            // (사용자 요청, 2026-09-08 백로그 — 이동에도 확정 기능).
            _unitMoveWaypoints.Clear();
            _unitMoveLastAnchor = _unitMoveStartPoint;
            _menuTarget = null;
            UpdateLeadingMoveBoundary();
            UpdateUnitMoveDistanceLabel();
            // 이동 확정 패널(경고 라벨 전용)은 팔로워 단계에서만 필요하다 —
            // 리딩 단계의 확정은 이제 리딩 모델 위에 뜨는 체크 아이콘이
            // 맡는다(사용자 요청, 2026-09-09).
            ShowUnitMoveConfirmIcon(true);
            BroadcastUnitMoveGuidelineIfNetworked(true);
        }

        /// <summary>리딩 모델을 뗄 때마다(드래그 종료) 불린다 — 그 지점을
        /// 웨이포인트로 확정하고, 리딩 단계 자체는 계속 이어간다(다시 집어서
        /// 옮길 수 있음). "이동 확정" 버튼을 눌러야 FinishLeadingMove로
        /// 넘어간다.</summary>
        private void CommitLeadingWaypoint()
        {
            _unitMoveWaypoints.Add(_unitMoveLeading.Center);
            _unitMoveWaypointGhosts.Add(BuildWaypointGhost(_unitMoveLeading, _unitMoveLeading.Center));
            _unitMoveLastAnchor = _unitMoveLeading.Center;
            UpdateLeadingMoveBoundary();
            UpdateUnitMoveDistanceLabel();
            BroadcastUnitMoveGuidelineIfNetworked(true);
        }

        /// <summary>웨이포인트 지점에 남기는 반투명 고스트 — 배치 미리보기
        /// (BoardManager.Roster.cs의 ShowBasePlacementPreview)와 같은 패턴으로
        /// 만든다: template과 같은 크기/색(반투명)/회전, raycastTarget 꺼서
        /// 클릭/드래그 대상이 되지 않는다. 로컬 웨이포인트(template=
        /// _unitMoveLeading)와 상대 웨이포인트(template=상대 유닛의 리딩
        /// 모델) 둘 다 이 하나를 공유해서 만든다.</summary>
        private Base BuildWaypointGhost(Base template, Vector2 center)
        {
            var go = new GameObject("UnitMoveWaypointGhost", typeof(RectTransform));
            go.transform.SetParent(baseLayer, false);
            var ghost = go.AddComponent<Base>();
            ghost.SizeMm = template.SizeMm;
            var mutedColor = template.FillColor;
            mutedColor.a *= 0.5f;
            ghost.FillColor = mutedColor;
            ghost.IsDisplacement = template.IsDisplacement;
            ghost.RotationDegrees = template.RotationDegrees;
            ghost.raycastTarget = false;
            ghost.Center = center;
            ghost.Refresh();
            return ghost;
        }

        private void ClearWaypointGhosts()
        {
            foreach (var ghost in _unitMoveWaypointGhosts)
            {
                if (ghost != null)
                {
                    Destroy(ghost.gameObject);
                }
            }
            _unitMoveWaypointGhosts.Clear();
        }

        /// <summary>우클릭 처리 — 리딩 단계에서 웨이포인트가 하나라도 있으면
        /// 마지막 한 점만 되돌리고(리딩 모델도 그 이전 지점으로 되돌아감),
        /// 더 되돌릴 웨이포인트가 없으면(=아직 시작점 그대로) 이동 전체를
        /// 취소한다. 팔로워 단계에서는(웨이포인트 개념이 없다) 예전과 같이
        /// 항상 전체 취소.</summary>
        internal void HandleUnitMoveRightClick()
        {
            if (_unitMovePhase == "leading" && _unitMoveWaypoints.Count > 0)
            {
                if (_draggingPiece == _unitMoveLeading)
                {
                    _draggingPiece = null;
                }
                _unitMoveWaypoints.RemoveAt(_unitMoveWaypoints.Count - 1);
                var lastGhost = _unitMoveWaypointGhosts[_unitMoveWaypointGhosts.Count - 1];
                _unitMoveWaypointGhosts.RemoveAt(_unitMoveWaypointGhosts.Count - 1);
                if (lastGhost != null)
                {
                    Destroy(lastGhost.gameObject);
                }
                _unitMoveLastAnchor = _unitMoveWaypoints.Count > 0
                        ? _unitMoveWaypoints[_unitMoveWaypoints.Count - 1]
                        : _unitMoveStartPoint;
                _unitMoveLeading.Center = _unitMoveLastAnchor;
                UpdateLeadingMoveBoundary();
                UpdateUnitMoveDistanceLabel();
                BroadcastUnitMoveGuidelineIfNetworked(true);
            }
            else
            {
                CancelUnitMove();
            }
        }

        /// <summary>지금까지 확정된 웨이포인트 구간들의 총 길이만 잰다 — 지금
        /// 드래그 중인 마지막 구간은 포함하지 않는다(호출부가 필요하면 따로
        /// 더한다). 측정 기준은 "시작 지점 베이스의 끄트머리(진행 방향
        /// 쪽)로부터 도착 지점 베이스의 가장 먼 곳(같은 방향 쪽)까지"
        /// (사용자 지정) — 리딩 모델은 두 지점에서 크기/회전이 똑같으므로,
        /// 이 정의는 대수적으로 정확히 두 중심점 사이의 직선 거리와 같다
        /// (양쪽 다 "+ 같은 방향으로 반지름만큼" 만큼 밀려나 있어 서로
        /// 상쇄됨). 그래서 그냥 Vector2.Distance면 충분하다 — 굳이 반지름을
        /// 계산해 더했다 뺄 필요가 없다.</summary>
        private float ComputeUsedMoveMm()
        {
            return ComputeUsedMoveMm(_unitMoveStartPoint, _unitMoveWaypoints);
        }

        /// <summary>로컬(위)과 상대(ApplyRemoteUnitMoveGuideline) 둘 다 공유하는
        /// 순수 계산 — 시작점부터 웨이포인트를 순서대로 잇는 구간 길이의 합.</summary>
        private static float ComputeUsedMoveMm(Vector2 startPoint, IReadOnlyList<Vector2> waypoints)
        {
            float usedMm = 0f;
            Vector2 prev = startPoint;
            foreach (var wp in waypoints)
            {
                usedMm += Vector2.Distance(prev, wp);
                prev = wp;
            }
            return usedMm;
        }

        /// <summary>anchor를 중심으로, 남은 이동력(remainingMm, 음수면 0으로
        /// 취급)만큼 밀어낸 테두리 폴리곤을 만든다 — 로컬 이동력 링
        /// (UpdateLeadingMoveBoundary)과 상대 이동력 링(ApplyRemoteUnitMoveGuideline)
        /// 둘 다 이 하나를 공유한다.</summary>
        private static Vector2[] BuildMoveBoundaryRing(Vector2 anchor, Vector2 sizeMm, float rotationRadians, float remainingMm)
        {
            var boundary = EllipseMath.EllipseOffsetPolygonAt(anchor, sizeMm, rotationRadians, Mathf.Max(0f, remainingMm));
            var closed = new Vector2[boundary.Length + 1];
            boundary.CopyTo(closed, 0);
            closed[boundary.Length] = boundary[0];
            return closed;
        }

        /// <summary>남은 이동력만큼 _unitMoveLastAnchor(마지막 확정 웨이포인트,
        /// 없으면 시작점)를 중심으로 한 테두리를 그린다 — 웨이포인트를 찍을
        /// 때마다 다시 계산해야 한다(이미 쓴 만큼 남은 반경이 줄어듦). 이
        /// 테두리는 어디까지나 참고용 시각 요소이고, 실제 제한(clamp)은
        /// Shift를 누르고 있을 때만 ClampTowardCenter가 건다(사용자 요청 —
        /// 기본은 자유이동, 이 게임 다른 배치/이동 스냅과 같은 정책).</summary>
        private void UpdateLeadingMoveBoundary()
        {
            float totalMoveMm = EffectiveMoveInch(_unitMoveUnit) * GameConstants.MmPerInch;
            float remainingMm = totalMoveMm - ComputeUsedMoveMm();
            guideline.RadiusMm = 0f;
            guideline.BandPolylines = new List<Vector2[]> { BuildMoveBoundaryRing(_unitMoveLastAnchor, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, remainingMm) };
        }

        /// <summary>확정된 웨이포인트들 + 지금 드래그 중인 마지막 구간을 잇는
        /// 선을 그린다. 각 구간은 "시작 지점의 진행방향 쪽 끄트머리부터
        /// 도착 지점의 같은 방향 가장 먼 곳까지"(사용자 지정, ComputeUsedMoveMm
        /// 참고) 그린다 — 그래서 선이 도착 지점의 고스트 위로 겹쳐 지나간다
        /// (의도된 모습, 고스트가 반투명이라 선이 그 위로 보임).</summary>
        private void UpdateUnitMovePathVisual()
        {
            var segments = new List<Vector2[]>();
            Vector2 prev = _unitMoveStartPoint;
            foreach (var wp in _unitMoveWaypoints)
            {
                segments.Add(PathSegmentEndpoints(prev, wp, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians));
                prev = wp;
            }
            segments.Add(PathSegmentEndpoints(prev, _unitMoveLeading.Center, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians));
            guideline.PathSegments = segments;
        }

        /// <summary>로컬 웨이포인트 경로선(위)과 상대 웨이포인트 경로선
        /// (ApplyRemoteUnitMoveGuideline) 둘 다 공유하는 순수 계산 — sizeMm/
        /// rotationRadians를 인자로 받으므로 어느 쪽 리딩 모델이든 쓸 수
        /// 있다.</summary>
        private static Vector2[] PathSegmentEndpoints(Vector2 a, Vector2 b, Vector2 sizeMm, float rotationRadians)
        {
            Vector2 offset = b - a;
            float dist = offset.magnitude;
            if (dist < 0.0001f)
            {
                return new[] { a, b };
            }
            Vector2 dir = offset / dist;
            float radius = EllipseMath.EllipseRadiusInDirection(sizeMm, rotationRadians, dir);
            return new[] { a + dir * radius, b + dir * radius };
        }

        /// <summary>상대가 자신의 리딩 모델 웨이포인트 경로를 갱신할 때마다
        /// BoardNetworkSync를 거쳐 온다(BroadcastUnitMoveGuidelineIfNetworked
        /// 참고) — 로컬 게임 상태는 전혀 안 바꾸고, "구경용" 고스트+경로선만
        /// 갱신한다. active=false면 그 시각 요소를 지운다(리딩 단계 종료/
        /// 취소).</summary>
        internal void ApplyRemoteUnitMoveGuideline(int networkUnitId, int leadingModelIndex, Vector2 startPoint, Vector2[] waypoints, bool active)
        {
            if (!active)
            {
                ClearRemoteUnitMoveGuideline();
                return;
            }
            if (!_networkedUnits.TryGet(networkUnitId, out var unit)
                    || leadingModelIndex < 0 || leadingModelIndex >= unit.Models.Count)
            {
                return;
            }
            var leading = unit.Models[leadingModelIndex];

            // 다른 유닛의 이동으로 넘어왔으면(상대가 이 유닛 이동을 확정/취소
            // 하지 않고 바로 다른 유닛을 골랐다는 뜻은 사실 없다 — 한 번에
            // 하나만 이동 가능 — 하지만 방어적으로 대비) 이전 고스트부터 정리.
            if (_remoteUnitMoveNetworkId != networkUnitId)
            {
                ClearRemoteUnitMoveGhostsOnly();
                _remoteUnitMoveNetworkId = networkUnitId;
            }

            // 고스트 개수를 웨이포인트 개수에 맞춘다 — 모자라면 만들고
            // (매번 통째로 다시 만들지 않는다, 부드러운 트윈 등은 필요
            // 없지만 매 방송마다 파괴+재생성하면 깜빡임만 생김), 넘치면
            // 지운다(상대가 우클릭으로 마지막 점을 되돌린 경우 자연히 여기로
            // 온다).
            while (_remoteUnitMoveGhosts.Count < waypoints.Length)
            {
                _remoteUnitMoveGhosts.Add(BuildWaypointGhost(leading, waypoints[_remoteUnitMoveGhosts.Count]));
            }
            while (_remoteUnitMoveGhosts.Count > waypoints.Length)
            {
                var last = _remoteUnitMoveGhosts[_remoteUnitMoveGhosts.Count - 1];
                _remoteUnitMoveGhosts.RemoveAt(_remoteUnitMoveGhosts.Count - 1);
                if (last != null)
                {
                    Destroy(last.gameObject);
                }
            }
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (_remoteUnitMoveGhosts[i] != null)
                {
                    _remoteUnitMoveGhosts[i].Center = waypoints[i];
                }
            }

            var segments = new List<Vector2[]>();
            Vector2 prev = startPoint;
            foreach (var wp in waypoints)
            {
                segments.Add(PathSegmentEndpoints(prev, wp, leading.SizeMm, leading.RotationRadians));
                prev = wp;
            }
            guideline.RemotePathSegments = segments;

            // 남은 이동력 링도 로컬과 같은 방식으로 — unit.MoveInch는 이미
            // 기존 유닛 동기화로 알고 있으므로 새로 보낼 값이 없다(웨이포인트
            // 목록만으로 여기서 그대로 다시 계산 가능).
            Vector2 anchor = waypoints.Length > 0 ? waypoints[waypoints.Length - 1] : startPoint;
            float totalMoveMm = EffectiveMoveInch(unit) * GameConstants.MmPerInch;
            float remainingMm = totalMoveMm - ComputeUsedMoveMm(startPoint, waypoints);
            guideline.RemoteBandPolylines = new List<Vector2[]> { BuildMoveBoundaryRing(anchor, leading.SizeMm, leading.RotationRadians, remainingMm) };
        }

        private void ClearRemoteUnitMoveGhostsOnly()
        {
            foreach (var ghost in _remoteUnitMoveGhosts)
            {
                if (ghost != null)
                {
                    Destroy(ghost.gameObject);
                }
            }
            _remoteUnitMoveGhosts.Clear();
        }

        private void ClearRemoteUnitMoveGuideline()
        {
            ClearRemoteUnitMoveGhostsOnly();
            _remoteUnitMoveNetworkId = -1;
            guideline.ClearRemoteBand();
        }

        private float EffectiveMoveInch(Unit unit)
        {
            // 1모델 유닛은 결속(coherency) 이동 보너스를 받으면 안 된다(사용자
            // 지적, 2026-09-01 백로그 → 2026-09-02 수정) — 예전엔 오히려
            // 1모델일 때만 보너스를 더해줬는데(다모델 유닛은 안 받음), 실제
            // 규칙은 정반대라 항상 MoveInch만 반환한다.
            return unit.MoveInch;
        }

        private void UpdateUnitMoveDistanceLabel()
        {
            if (!(_unitMoveActive && _unitMovePhase == "leading"))
            {
                return;
            }
            // 배치(신규 유닛 최초 배치)는 웨이포인트 개념이 없다 — 배치는
            // StartUnitMove를 안 거치고 BeginDeploymentDrag가 상태를 직접
            // 세팅하므로 _unitMoveStartPoint/_unitMoveLastAnchor가 채워지지
            // 않는다. 그 값들을 써서 거리 라벨이나 경로 선을 그리면 엉뚱한
            // 값이 나오므로 배치 중에는 아예 건드리지 않는다.
            if (_unitMoveIsDeployment)
            {
                return;
            }
            // 총 거리는 지금까지 확정된 웨이포인트 구간 합 + 지금 드래그 중인
            // (또는 마지막으로 놓인) 마지막 구간 — 라벨은 그 합계 하나만
            // 마지막 구간 위에 표시한다(사용자 요청).
            float distMm = ComputeUsedMoveMm() + Vector2.Distance(_unitMoveLastAnchor, _unitMoveLeading.Center);
            guideline.LabelText = $"{distMm / GameConstants.MmPerInch:F1}\"";
            guideline.LabelPos = _unitMoveLeading.Center + new Vector2(0f, _unitMoveLeading.BoundingRadius + 14f);
            UpdateUnitMovePathVisual();
            UpdateUnitMoveConfirmIconPosition();
        }

        private void FinishLeadingMove()
        {
            _unitMovePhase = "followers";
            // 리딩 단계가 끝났으니 지금까지의 웨이포인트 고스트도 정리한다 —
            // 최종 위치만 남으면 된다(배치는 애초에 고스트가 안 생기므로
            // 여기선 아무 일도 안 함). 상대에게도 "이제 그만 지워도 된다"고
            // 알린다 — 이후 최종 위치는 기존 유닛 방송(BroadcastUnitIfNetworked)
            // 경로로 따로 전달된다.
            ClearWaypointGhosts();
            BroadcastUnitMoveGuidelineIfNetworked(false);
            if (_unitMoveIsDeployment && _pendingFollowerCount > 0)
            {
                SpawnDeploymentFollowers();
            }
            AutoPlaceFollowers();
            UpdateUnitMoveGuideline();
            UpdateUnitMoveWarning();
            if (_unitMoveUnit.Models.Count <= 1)
            {
                CompleteUnitMove();
                return;
            }
            // 팔로워 단계로 들어가는 시점(리딩 모델 확정 + 팔로워 자동 배치
            // 직후)에 한 번 공유하고, 이후 팔로워를 하나씩 드래그해서 옮길
            // 때마다(Update()의 _draggingFollower 마우스업 처리) 또 공유한다
            // — 사용자 요청(2026-08-30): 전체 배치가 다 끝날 때까지 기다리지
            // 말고 팔로워 단계 중간중간도 상대에게 보여달라.
            BroadcastUnitIfNetworked(_unitMoveUnit);
            // 패널(경고 라벨 전용)은 더 이상 여기서 무조건 켜지 않는다 —
            // UpdateUnitMoveWarning()이 경고가 실제로 있을 때만 켠다(사용자
            // 보고, 2026-09-09: "팔로워 옮길 때 화면 하단에 예전 버튼
            // 흔적이 남아있어, 백그라운드 박스가 작게 보여" — 경고가 없어도
            // 패딩만 있는 빈 상자가 계속 떠 있던 게 원인).
            // 배치(deployment)는 여기가 확정 아이콘을 처음 보여주는 시점이다
            // (StartUnitMove를 안 거치므로) — 재이동은 이미 리딩 단계부터
            // 떠 있었지만, 다시 불러도 안전하다(그냥 같은 위치로 재갱신).
            ShowUnitMoveConfirmIcon(true);
            UpdateUnitMoveConfirmIconPosition();
        }

        /// <summary>배치의 리딩 모델이 실제로 놓인 뒤에야 나머지 모델을 만든다 —
        /// 그 전에 만들면 클릭 지점에 겹쳐서 충돌 해소를 방해하게 된다.
        /// "유닛 되돌리기"로 되돌아온 유닛이면 damages[0]은 리딩 모델에 이미
        /// 쓰였으므로, 팔로워는 그 뒤 순서대로 이어서 가져간다.</summary>
        private void SpawnDeploymentFollowers()
        {
            var damages = _deploymentDefSnapshot != null ? _deploymentDefSnapshot.Damages : null;
            var specialists = _deploymentDefSnapshot != null ? _deploymentDefSnapshot.Specialists : null;
            for (int i = 0; i < _pendingFollowerCount; i++)
            {
                var follower = CreatePieceObject(_unitMoveUnit, _unitMoveLeading.SizeMm, _unitMoveLeading.FillColor, _unitMoveLeading.IsDisplacement);
                int modelIndex = i + 1; // 0번은 리더가 이미 가져갔다(BeginDeploymentDrag).
                if (damages != null && modelIndex < damages.Count)
                {
                    follower.Damage = damages[modelIndex];
                }
                if (specialists != null && modelIndex < specialists.Count)
                {
                    follower.Memo = specialists[modelIndex];
                }
                follower.Center = _unitMoveLeading.Center;
                follower.Refresh();
                _unitMoveUnit.Models.Add(follower);
            }
            _pendingFollowerCount = 0;
        }

        private void AutoPlaceFollowers()
        {
            var followers = new List<Base>();
            foreach (var model in _unitMoveUnit.Models)
            {
                if (model != _unitMoveLeading)
                {
                    followers.Add(model);
                }
            }
            if (followers.Count == 0)
            {
                return;
            }

            float boundary = CoherencyBoundaryRadiusMm();
            Vector2 leadingCenter = _unitMoveLeading.Center;
            for (int i = 0; i < followers.Count; i++)
            {
                var follower = followers[i];
                float ringRadius = (boundary - follower.BoundingRadius) * FollowerRingFraction;
                float angle = (Mathf.PI * 2f / followers.Count) * i - Mathf.PI / 2f;
                var desired = leadingCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
                follower.Center = ResolvePosition(follower, desired, false);
            }
        }

        private float CoherencyBoundaryRadiusMm()
        {
            return _unitMoveLeading.BoundingRadius + _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch;
        }

        private void UpdateUnitMoveGuideline()
        {
            float coherencyMm = _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch;
            var boundary = EllipseMath.EllipseOffsetPolygonAt(_unitMoveLeading.Center, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, coherencyMm);
            var closed = new Vector2[boundary.Length + 1];
            boundary.CopyTo(closed, 0);
            closed[boundary.Length] = boundary[0];

            guideline.RadiusMm = 0f;
            guideline.BandPolylines = new List<Vector2[]> { closed };
            guideline.LabelText = "";
            // 팔로워 단계로 넘어오면 리딩 단계의 웨이포인트 경로 선은 더 이상
            // 필요 없다.
            guideline.PathSegments = new List<Vector2[]>();
        }

        private void UpdateUnitMoveWarning()
        {
            bool anyOut = false;
            foreach (var model in _unitMoveUnit.Models)
            {
                if (model == _unitMoveLeading)
                {
                    continue;
                }
                bool outOfCoherency = !IsFollowerWithinCoherency(model);
                // 코헤런시를 벗어난 모델은 하얗게 반짝인다(Base.CoherencyWarning) —
                // 전부 검사해야 하므로(첫 번째에서 break하지 않는다) 글로벌 경고
                // 문구뿐 아니라 어떤 모델이 문제인지도 바로 보인다.
                model.CoherencyWarning = outOfCoherency;
                if (outOfCoherency)
                {
                    anyOut = true;
                }
            }
            if (_unitMoveWarningLabel != null)
            {
                _unitMoveWarningLabel.gameObject.SetActive(anyOut);
            }
            // 패널(경고 라벨을 담는 배경 상자) 자체도 경고가 실제로 있을
            // 때만 보인다 — 예전엔 팔로워 단계 내내 항상 켜져 있었는데
            // ("이동 확정" 버튼이 거기 있었으므로), 버튼이 빠진 지금은
            // 경고 없이 계속 켜두면 패딩만 있는 빈 상자가 화면 하단에
            // 남는다(사용자 보고, 2026-09-09).
            if (_unitMovePanel != null)
            {
                _unitMovePanel.gameObject.SetActive(anyOut);
            }
        }

        private bool IsFollowerWithinCoherency(Base follower)
        {
            float coherencyMm = _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch + CoherencyEpsilonMm;
            var boundary = EllipseMath.EllipseOffsetPolygonAt(_unitMoveLeading.Center, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, coherencyMm);
            var followerPoly = EllipseMath.EllipsePolygonAt(follower.Center, follower.SizeMm, follower.RotationRadians);
            foreach (var p in followerPoly)
            {
                if (!EllipseMath.PointInConvexPolygon(p, boundary))
                {
                    return false;
                }
            }
            return true;
        }

        private Vector2 ResolveFollowerPosition(Base piece, Vector2 desiredCenter)
        {
            // 코헤런시 경계 근처로 드래그하면 스냅되도록 돕는다 — 기본은
            // 자유배치, Shift를 누르고 있을 때만 스냅된다(사용자 요청).
            // 리딩 모델 위치를 고정 중심으로 삼는 ClampTowardCenter를 쓴다 —
            // "가장 가까운 경계 위 점"을 매 프레임 다시 찾는 방식은 desired가
            // 조금만 움직여도 그 최근접점이 완전히 다른 곳으로 튈 수 있어
            // 불안정했다(사용자가 실제로 겪은 떨림/진동 버그 두 번, 자세한
            // 경위는 ClampTowardCenter 및 EllipseMath.MaxRadialDistanceFullyInside
            // 참고).
            bool useSnap = SnapEnabled;
            var pos = desiredCenter;
            for (int i = 0; i < EllipseMath.CollisionIterations; i++)
            {
                var before = pos;
                if (useSnap)
                {
                    pos = ClampTowardCenter(pos, _unitMoveLeading.Center, piece.SizeMm, piece.RotationRadians);
                }
                pos = ResolvePosition(piece, pos, false);
                if (Vector2.Distance(pos, before) < 0.01f)
                {
                    break;
                }
            }
            return pos;
        }

        /// <summary>Shift를 누르고 있는 동안만 스냅이 켜진다 — 배치/이동/
        /// 코헤런시 배치 모두 이 값을 공유한다(사용자 요청).</summary>
        private static bool SnapEnabled => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /// <summary>
        /// 고정된 기준점 center를 두고, desired가 center로부터 너무 멀어지지
        /// (guideline.BandPolylines 경계를 넘지) 않도록 제한한다 — 코헤런시
        /// 팔로워(center=리딩 모델 위치)와 리딩 모델의 자유 이동(center=이동
        /// 시작점) 둘 다 "고정된 한 점 기준으로 이 방향으로 얼마나 멀어질
        /// 수 있는가"라는 같은 형태의 문제라서 이 함수 하나를 공유한다.
        ///
        /// 실제 한계 거리는 EllipseMath.MaxRadialDistanceFullyInside(타원
        /// 테두리 전체가 경계 안에 완전히 들어가는지 직접 검증하는 이분
        /// 탐색)가 계산한다 — 예전엔 지지함수로 dir 방향 한 점만 검증했는데,
        /// 리딩과 팔로워(또는 리딩의 시작 회전과 지금 회전)가 서로 다르게
        /// 회전돼 있으면 실제로 가장 많이 튀어나오는 방향이 dir이 아닐 수
        /// 있어서, 그 경우 코헤런시 라인을 실제로 이탈하는 버그가 있었다
        /// (사용자가 실제로 겪음 — Base.CoherencyWarning용 IsFollowerWithinCoherency
        /// 는 애초에 테두리 전체를 검사하는 더 엄격한 방식이라 이 문제가
        /// 없었다). 이분 탐색의 상한은 RayDistanceToPolylines(반직선이 경계에
        /// 닿는 raw 거리 — 실제 한계는 타원 크기만큼 항상 이보다 작거나 같다)로
        /// 잡는다. 여기서는 그 위에 Shift/문턱/하드클램프 같은 "이 프로젝트의
        /// 정책"만 얹는다.
        ///
        /// 배치(SnapToGuidelineBoundary)는 이 함수를 못 쓴다 — 배치구역 밴드는
        /// 뚜렷한 중심점이 없어서(지도 가장자리를 따라 늘어선 모양) "고정
        /// 기준점에서의 방향"이라는 전제 자체가 성립하지 않는다.
        ///
        /// 왜 반직선 방식인가(vs. "가장 가까운 경계 위 점"): 처음엔 팔로워도
        /// 배치와 같은 ClosestPointOnPolylines 기반으로 짰는데, 그 함수는
        /// "desired에서 가장 가까운 경계 위 점"을 매번 새로 찾는다 — desired가
        /// 가만히 있어도 이전 위치 기준 접선 이동으로 매 프레임 값이 바뀌는
        /// 구조라 대각선 방향에서 두 좌표를 오가며 진동했다(사용자가 실측).
        /// 이 함수는 그 반대다 — center가 고정이고 dir=Normalize(desired-center)도
        /// desired만의 함수이므로, desired가 고정이면 결과도 항상 고정된다
        /// (진동이 구조적으로 불가능).</summary>
        private Vector2 ClampTowardCenter(Vector2 desired, Vector2 center, Vector2 sizeMm, float rotationRadians)
        {
            if (guideline.BandPolylines == null || guideline.BandPolylines.Count == 0)
            {
                return desired;
            }
            Vector2 offset = desired - center;
            float dist = offset.magnitude;
            if (dist < 0.01f)
            {
                return desired;
            }
            Vector2 dir = offset / dist;
            float? rayHit = EllipseMath.RayDistanceToPolylines(center, dir, guideline.BandPolylines);
            if (rayHit == null)
            {
                return desired;
            }
            float maxDist = EllipseMath.MaxRadialDistanceFullyInside(center, dir, guideline.BandPolylines[0], sizeMm, rotationRadians, rayHit.Value);
            if (dist <= maxDist)
            {
                return maxDist - dist <= FollowerSnapThresholdMm ? center + dir * maxDist : desired;
            }
            return center + dir * maxDist;
        }

        /// <summary>리딩 모델을 드래그하는 동안(배치/이동 공통) Shift를 누르고
        /// 있으면 가이드라인 경계에 스냅한다. 이동은 고정 중심(이동 시작점)이
        /// 있으므로 ClampTowardCenter를, 배치는 뚜렷한 중심점이 없으므로
        /// SnapToGuidelineBoundary(가장 가까운 변 기준)를 쓴다 — 서로 다른
        /// 기하 구조라 억지로 하나로 합치지 않는다. 기본은(Shift 없이) 완전
        /// 자유배치(사용자 요청) — 이 밴드/곡선은 참고용일 뿐 원래도
        /// 강제되지 않았다.</summary>
        private Vector2 ResolveLeadingDragCenter(Vector2 desired)
        {
            if (!SnapEnabled || !_unitMoveActive || _unitMovePhase != "leading")
            {
                return desired;
            }
            if (_unitMoveIsDeployment)
            {
                return SnapToGuidelineBoundary(desired, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians);
            }
            // 웨이포인트 도입 이후: 고정 기준점은 시작점이 아니라 "마지막으로
            // 확정된 웨이포인트"다 — 이미 쓴 이동력만큼 guideline.BandPolylines
            // 반경도 줄어들어 있다(UpdateLeadingMoveBoundary).
            return ClampTowardCenter(desired, _unitMoveLastAnchor, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians);
        }

        /// <summary>guideline.BandPolylines 경계 근처에서 (sizeMm/rotationRadians로
        /// 주어진) 타원의 테두리를 선에 붙여주고, 경계 밖으로는 아예 못 나가게
        /// 막는다 — 뚜렷한 고정 중심점이 없는 경우 전용(배치구역 밴드). 리딩
        /// 모델을 실제로 드래그하는 중(위 ResolveLeadingDragCenter의 배치
        /// 분기)과, 아직 클릭 전 고스트 미리보기가 마우스를 따라다니는 중
        /// (HandlePendingDeploymentInput) 둘 다에서 공유해서 쓴다(사용자 요청
        /// — 미리보기 단계에서도 가이드라인처럼 스냅/차단이 보여야 자연스럽다).
        ///
        /// EllipseMath.ClosestPointOnPolylines가 돌려주는 closest/inward(가장
        /// 가까운 변 하나의 정확한 안쪽 법선)로 문턱 판정용 radius/signedDist를
        /// 구하되, 실제로 스냅/클램프하는 지점은 그 변 하나만으로 계산하지
        /// 않는다 — 변 하나의 지지함수만 쓰면, 둥근 모서리(짧은 변 여러 개가
        /// 이어진 곳)를 애매한 각도(45도 근처 등)로 접근할 때 타원이 실제로는
        /// "가장 가까운" 변이 아닌 이웃한 변을 살짝 넘어가는데도 통과시켜버리는
        /// 버그가 있었다(사용자가 실제로 겪음 — 코헤런시 스냅에서 먼저 고친
        /// 것과 같은 종류의 문제). 대신 PushToFullyInsideBand로 "타원 테두리
        /// 전체가 밴드(여러 구역이면 그 중 하나에라도) 완전히 들어가는" 지점을
        /// 직접 검증하며 이분 탐색으로 찾는다. 배치는 클릭 한 번짜리 짧은
        /// 상호작용이라 팔로워처럼 "desired가 가만히 있어도 진동"하는 문제는
        /// 보고되지 않았다. SnapEnabled 자체도 여기서 다시 확인한다 — 호출부
        /// (미리보기)는 Shift 여부를 미리 안 걸러주므로.</summary>
        private Vector2 SnapToGuidelineBoundary(Vector2 desired, Vector2 sizeMm, float rotationRadians)
        {
            if (!SnapEnabled || guideline.BandPolylines == null || guideline.BandPolylines.Count == 0)
            {
                return desired;
            }
            var closest = EllipseMath.ClosestPointOnPolylines(desired, guideline.BandPolylines, out Vector2 inward, out _);
            if (inward == Vector2.zero)
            {
                return desired;
            }
            float radius = EllipseMath.EllipseSupportInDirection(sizeMm, rotationRadians, inward);
            float signedDist = Vector2.Dot(desired - closest, inward);
            if (signedDist >= radius)
            {
                return signedDist - radius <= FollowerSnapThresholdMm ? PushToFullyInsideBand(closest, inward, sizeMm, rotationRadians, radius) : desired;
            }
            return PushToFullyInsideBand(closest, inward, sizeMm, rotationRadians, radius);
        }

        /// <summary>closest(가장 가까운 변 위의 점)에서 inward 방향으로, 타원
        /// (sizeMm/rotationRadians) 전체가 guideline.BandPolylines 중 하나에라도
        /// 완전히 들어가는 가장 가까운 지점까지 밀어낸다 — singleSegmentRadius
        /// (그 변 하나만의 지지함수, 둥근 모서리가 아니면 대개 정답과 같다)를
        /// 이분 탐색의 하한으로 쓰고, 같은 방향으로 밴드 반대편까지의 실제
        /// 거리(RayDistanceToPolylines)를 상한으로 쓴다 — 상한까지 밀었는데도
        /// 안 들어가는 예외적인 경우엔(반직선이 아무 것도 못 만나는 등) 그냥
        /// singleSegmentRadius 지점을 쓴다(예전 동작으로 안전하게 되돌아감).
        /// 반직선은 closest에서 바로 안 쏜다 — closest 자체가 그 변 위의
        /// 점이라, 그대로 쏘면 자기 자신이 속한 변과 t≈0에서 "만나서" 상한이
        /// 항상 하한과 같아지는 버그가 있었다(사용자가 실제로 겪음). 대신
        /// inward 쪽으로 살짝(RayOriginOffsetMm) 물러난 지점에서 쏜다.</summary>
        private Vector2 PushToFullyInsideBand(Vector2 closest, Vector2 inward, Vector2 sizeMm, float rotationRadians, float singleSegmentRadius)
        {
            Vector2 rayOrigin = closest + inward * RayOriginOffsetMm;
            float? farHit = EllipseMath.RayDistanceToPolylines(rayOrigin, inward, guideline.BandPolylines);
            float upperBound = farHit.HasValue ? Mathf.Max(farHit.Value + RayOriginOffsetMm, singleSegmentRadius) : singleSegmentRadius;
            float pushDist = EllipseMath.MinPushDistanceFullyInside(closest, inward, guideline.BandPolylines, sizeMm, rotationRadians, singleSegmentRadius, upperBound);
            return closest + inward * pushDist;
        }

        private void OnUnitMoveCompletePressed()
        {
            if (!_unitMoveActive)
            {
                return;
            }
            // 리딩 단계에서 누르면 지금까지 찍은 웨이포인트 경로를 마무리하고
            // 팔로워 단계(또는 1모델 유닛이면 바로 완료)로 넘어간다. 팔로워
            // 단계에서 누르면 예전처럼 전체 이동을 확정한다.
            if (_unitMovePhase == "leading")
            {
                FinishLeadingMove();
            }
            else if (_unitMovePhase == "followers")
            {
                CompleteUnitMove();
            }
        }

        /// <summary>코헤런시 밖에 남은 팔로워가 있어도 모델을 지우지 않는다 —
        /// 이동 중 경고 라벨(_unitMoveWarningLabel)로 알려주는 것으로 충분하다
        /// (사용자 요청으로 자동 제거를 뺐다).</summary>
        private void CompleteUnitMove()
        {
            CommitUndoTransaction();
            // 유닛 이동 워크플로우(배치든 재이동이든) 전체가 여기서 확정된다
            // — 되돌리기 가능한 유일한 시점이라, 중간 드래그 과정 대신 이
            // "완료됨" 순간의 최종 상태 하나만 상대에게 방송한다(마커와
            // 같은 원칙 — BoardManager.UnitSync.cs 참고).
            BroadcastUnitIfNetworked(_unitMoveUnit);
            EndUnitMove();
        }

        internal void CancelUnitMove()
        {
            if (_unitMoveIsDeployment)
            {
                // 배치 중 취소: 아직 게임에 존재한 적 없는 유닛이므로 되돌릴 위치가
                // 없다. 만든 모델을 전부 지우고, 정의를 예비대 목록에 되돌려놓는다.
                foreach (var model in _unitMoveUnit.Models.ToArray())
                {
                    _pieces.Remove(model);
                    Destroy(model.gameObject);
                }
                _unitMoveUnit.Models.Clear();
                // 팔로워 단계 도중이었다면(2026-08-30 기능) 이미 상대에게
                // 중간 상태가 방송돼있을 수 있다 — 그 쪽에도 지우라고
                // 알려준다. 아직 한 번도 방송된 적 없으면(NetworkUnitId
                // 미배정) 상대는 이 유닛을 아예 모르므로 알릴 것도 없다.
                BroadcastDeleteUnitIfNetworked(_unitMoveUnit);
                if (_deploymentDefSnapshot != null)
                {
                    AddPendingUnitDef(_deploymentDefSnapshot);
                }
            }
            else
            {
                foreach (var kv in _unitMoveOriginalPositions)
                {
                    if (kv.Key != null)
                    {
                        kv.Key.Center = kv.Value;
                    }
                }
                // 재이동 취소 — 되돌린 원래 위치를 다시 방송한다. 상대는 이미
                // 받았던 중간 상태에서 이 원래 위치로(같은 부드러운 트윈
                // 경로로) 되돌아간다.
                BroadcastUnitIfNetworked(_unitMoveUnit);
            }
            _draggingPiece = null;
            _draggingFollower = null;
            // 취소는 원래 상태로 되돌렸을 뿐 새로운 변화가 없으므로, 열어둔
            // 되돌리기 트랜잭션은 커밋하지 않고 그냥 버린다.
            DiscardUndoTransaction();
            // 리딩 단계 도중 취소됐을 수 있다 — 상대 화면에 남아있을 수 있는
            // 웨이포인트 고스트/경로선을 지우라고 알린다(배치 취소는 이
            // 안에서 자동으로 무시됨 — BroadcastUnitMoveGuidelineIfNetworked
            // 참고).
            BroadcastUnitMoveGuidelineIfNetworked(false);
            EndUnitMove();
        }

        private void EndUnitMove()
        {
            // 반짝임은 이동 상호작용 중에만 보여주는 실시간 경고다 — 이동이
            // 끝나면(완료든 취소든) 꺼둔다(_unitMoveUnit이 null이 되기 전에).
            if (_unitMoveUnit != null)
            {
                foreach (var model in _unitMoveUnit.Models)
                {
                    model.CoherencyWarning = false;
                }
            }
            _unitMoveActive = false;
            _unitMoveLeading = null;
            _unitMoveUnit = null;
            _unitMovePhase = "";
            _unitMoveWaypoints.Clear();
            _unitMoveLastAnchor = default;
            // 리딩 단계 도중 취소된 경우(FinishLeadingMove를 거치지 않음)를
            // 대비한 안전망 — 이미 비어 있으면 아무 일도 안 한다.
            ClearWaypointGhosts();
            _unitMoveOriginalPositions.Clear();
            _unitMoveIsDeployment = false;
            _deploymentDefSnapshot = null;
            _pendingFollowerCount = 0;
            _displacementAnchor = null;
            _displacementQueue.Clear();
            _displacementResumeLeadingFinish = false;
            ShowUnitMovePanel(false);
            ShowUnitMoveConfirmIcon(false);
            guideline.ClearBand();
        }

        private void ShowUnitMovePanel(bool show)
        {
            // 경고 라벨의 표시 여부는 UpdateUnitMoveWarning()이 전담한다 — 여기서
            // 손대면 FinishLeadingMove()가 먼저 켜둔 경고를 지워버리게 된다.
            if (_unitMovePanel != null)
            {
                _unitMovePanel.gameObject.SetActive(show);
            }
            if (!show && _unitMoveWarningLabel != null)
            {
                _unitMoveWarningLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>고정 화면 UI(패널 등)를 매달 Canvas를 찾는다. mapArea는
        /// 패닝/줌으로 움직이므로 그 부모(=Canvas)를 써야 패널이 지도를
        /// 따라 움직이지 않는다.</summary>
        private Transform GetCanvasParent()
        {
            if (mapArea != null)
            {
                return mapArea.parent;
            }
            return baseLayer != null ? baseLayer.parent : transform;
        }

        private void BuildUnitMovePanel()
        {
            var canvasParent = GetCanvasParent();

            var panelGo = new GameObject("UnitMovePanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasParent, false);
            // 화면 중앙 아래(마커바보다 위)에 둔다 — 예전엔 우측 상단이었는데,
            // B 로스터 패널이 그 자리로 옮겨오면서 서로 겹쳐 "완료" 버튼이
            // 안 보이는 문제가 있었다.
            _unitMovePanel = (RectTransform)panelGo.transform;
            _unitMovePanel.anchorMin = new Vector2(0.5f, 0f);
            _unitMovePanel.anchorMax = new Vector2(0.5f, 0f);
            _unitMovePanel.pivot = new Vector2(0.5f, 0f);
            _unitMovePanel.anchoredPosition = new Vector2(0f, 70f);
            _unitMovePanel.sizeDelta = new Vector2(260f, 0f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            // "이동 확정" 버튼이 없어진 뒤로 이 패널엔 경고 라벨 하나만
            // 남는다(2026-09-09, 아래 확정 체크 아이콘으로 대체됨) — 고정
            // 높이 대신 내용에 맞춰 자동으로 크기를 잡는다.
            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var warningGo = new GameObject("Warning", typeof(RectTransform));
            warningGo.transform.SetParent(panelGo.transform, false);
            _unitMoveWarningLabel = warningGo.AddComponent<TextMeshProUGUI>();
            _unitMoveWarningLabel.text = "코헤런시를 벗어나는 모델이 있습니다.";
            _unitMoveWarningLabel.fontSize = 13f;
            _unitMoveWarningLabel.color = new Color(1f, 0.3f, 0.3f, 1f);
            _unitMoveWarningLabel.textWrappingMode = TextWrappingModes.Normal;
            warningGo.SetActive(false);

            panelGo.SetActive(false);
        }

        /// <summary>"이동 확정" 역할을 하는 체크 아이콘(ComfirmDial.png) —
        /// 예전엔 화면 하단 고정 패널의 버튼이었는데, 사용자 요청(2026-09-09)
        /// 으로 이동시킨(리딩) 모델 바로 위에 뜨는 보드 공간 아이콘으로
        /// 바뀌었다. 클릭 동작은 그대로 OnUnitMoveCompletePressed를 재사용
        /// (리딩 단계면 FinishLeadingMove, 팔로워 단계면 CompleteUnitMove) —
        /// 어느 UI가 눌렀는지만 바뀌었을 뿐 로직은 그대로다.</summary>
        private void BuildUnitMoveConfirmIcon()
        {
            var go = new GameObject("UnitMoveConfirmIcon", typeof(RectTransform));
            go.transform.SetParent(baseLayer, false);
            _unitMoveConfirmIcon = (RectTransform)go.transform;
            _unitMoveConfirmIcon.sizeDelta = new Vector2(UnitMoveConfirmIconSizeMm, UnitMoveConfirmIconSizeMm);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/ComfirmDial");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnUnitMoveCompletePressed);

            go.SetActive(false);
        }

        private void ShowUnitMoveConfirmIcon(bool show)
        {
            if (_unitMoveConfirmIcon == null)
            {
                return;
            }
            _unitMoveConfirmIcon.gameObject.SetActive(show);
            if (show)
            {
                // baseLayer의 다른 조각(유닛)들보다 위에 그려져야 클릭도
                // 막히지 않는다 — 이 아이콘은 Start() 때 한 번만 만들어져서
                // 그 뒤에 생기는 유닛 조각들보다 항상 형제 순서가 앞선다.
                _unitMoveConfirmIcon.SetAsLastSibling();
            }
        }

        /// <summary>리딩 모델 테두리(리딩 단계) 또는 코헤런시 가이드라인
        /// (팔로워 단계, CoherencyBoundaryRadiusMm — 사용자 요청, 2026-09-09:
        /// "팔로워 옮길 땐 코헤런시 가이드라인 위에 뜨게") 바로 위에 아이콘을
        /// 띄운다 — 리딩 단계에서 드래그/웨이포인트로 위치가 바뀔 때마다
        /// (UpdateUnitMoveDistanceLabel 경유) 다시 불린다. 팔로워 단계에서는
        /// 리딩 모델이 더 이상 움직이지 않으므로(코헤런시 링 반경도 고정)
        /// FinishLeadingMove에서 한 번만 다시 불러주면 충분하다.</summary>
        private void UpdateUnitMoveConfirmIconPosition()
        {
            if (_unitMoveConfirmIcon == null || _unitMoveLeading == null)
            {
                return;
            }
            bool isFollowerPhase = _unitMovePhase == "followers";
            float radius = isFollowerPhase ? CoherencyBoundaryRadiusMm() : _unitMoveLeading.BoundingRadius;
            float margin = isFollowerPhase ? UnitMoveConfirmIconFollowerMarginMm : UnitMoveConfirmIconMarginMm;
            _unitMoveConfirmIcon.anchoredPosition = _unitMoveLeading.Center
                    + new Vector2(0f, radius + margin);
        }

    }
}
