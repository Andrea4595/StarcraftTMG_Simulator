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
            BeginUndoTransaction($"{DescribeUnit(leading.Unit)} 이동");

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

            // "베이스 테두리로부터 이동거리만큼"을 나타내야 하므로, 리딩 모델의
            // 시작 시점 타원 테두리를 실제 법선 방향으로 밀어낸 곡선을 그린다
            // (EllipseOffsetPolygonAt — 코헤런시 링과 같은 기법). 원형 베이스일
            // 때는 이 곡선이 그냥 원과 같아 보이지만, 타원형 베이스는 방향마다
            // 진짜 테두리로부터의 거리가 달라야 정확하다(사용자 지적 — 예전엔
            // 이걸 반지름 하나로만 근사한 원을 그렸었다). 모델이 하나뿐인
            // 유닛은 코헤런시만큼 이동력이 늘어난다(팔로워를 코헤런시 안에
            // 배치할 필요가 없으므로).
            float moveMm = EffectiveMoveInch(_unitMoveUnit) * GameConstants.MmPerInch;
            var boundary = EllipseMath.EllipseOffsetPolygonAt(_unitMoveStartPoint, leading.SizeMm, leading.RotationRadians, moveMm);
            var closed = new Vector2[boundary.Length + 1];
            boundary.CopyTo(closed, 0);
            closed[boundary.Length] = boundary[0];
            guideline.RadiusMm = 0f;
            guideline.BandPolylines = new List<Vector2[]> { closed };
            _menuTarget = null;
            UpdateUnitMoveDistanceLabel();
        }

        private float EffectiveMoveInch(Unit unit)
        {
            if (unit.Models.Count <= 1)
            {
                return unit.MoveInch + unit.CoherencyInch;
            }
            return unit.MoveInch;
        }

        private void UpdateUnitMoveDistanceLabel()
        {
            if (!(_unitMoveActive && _unitMovePhase == "leading"))
            {
                return;
            }
            float distMm = Vector2.Distance(_unitMoveLeading.Center, _unitMoveStartPoint);
            guideline.LabelText = $"{distMm / GameConstants.MmPerInch:F1}\"";
            guideline.LabelPos = _unitMoveLeading.Center + new Vector2(0f, _unitMoveLeading.BoundingRadius + 14f);
        }

        private void FinishLeadingMove()
        {
            _unitMovePhase = "followers";
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
            ShowUnitMovePanel(true);
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
            return ClampTowardCenter(desired, _unitMoveStartPoint, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians);
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
            if (!_unitMoveActive || _unitMovePhase != "followers")
            {
                return;
            }
            CompleteUnitMove();
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

        private void CancelUnitMove()
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
            _unitMoveOriginalPositions.Clear();
            _unitMoveIsDeployment = false;
            _deploymentDefSnapshot = null;
            _pendingFollowerCount = 0;
            _displacementAnchor = null;
            _displacementQueue.Clear();
            _displacementResumeLeadingFinish = false;
            ShowUnitMovePanel(false);
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
            _unitMovePanel.sizeDelta = new Vector2(260f, 90f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var warningGo = new GameObject("Warning", typeof(RectTransform));
            warningGo.transform.SetParent(panelGo.transform, false);
            _unitMoveWarningLabel = warningGo.AddComponent<TextMeshProUGUI>();
            _unitMoveWarningLabel.text = "코헤런시를 벗어나는 모델이 있습니다.";
            _unitMoveWarningLabel.fontSize = 13f;
            _unitMoveWarningLabel.color = new Color(1f, 0.3f, 0.3f, 1f);
            _unitMoveWarningLabel.textWrappingMode = TextWrappingModes.Normal;
            warningGo.SetActive(false);

            var btnGo = new GameObject("CompleteButton", typeof(RectTransform));
            btnGo.transform.SetParent(panelGo.transform, false);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 32f;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(OnUnitMoveCompletePressed);
            var btnLabelGo = new GameObject("Label", typeof(RectTransform));
            btnLabelGo.transform.SetParent(btnGo.transform, false);
            var btnLabelRect = (RectTransform)btnLabelGo.transform;
            btnLabelRect.anchorMin = Vector2.zero;
            btnLabelRect.anchorMax = Vector2.one;
            btnLabelRect.offsetMin = Vector2.zero;
            btnLabelRect.offsetMax = Vector2.zero;
            var btnLabel = btnLabelGo.AddComponent<TextMeshProUGUI>();
            btnLabel.text = "이동 확정";
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.fontSize = 16f;
            btnLabel.color = Color.white;
            btnLabel.raycastTarget = false;

            panelGo.SetActive(false);
        }

    }
}
