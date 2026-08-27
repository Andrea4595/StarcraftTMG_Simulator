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

        public void StartUnitMove(Base leading)
        {
            if (leading == null || leading.Unit == null || _unitMoveActive)
            {
                return;
            }

            // 리딩 모델 배치부터 완료(또는 배치 중 취소)까지 전체를 되돌리기 한
            // 단계로 묶는다 — CompleteUnitMove()에서 커밋, CancelUnitMove()에서는
            // 폐기(원래 상태로 이미 되돌렸으므로 별도 되돌리기 단계가 필요 없음).
            BeginUndoTransaction();

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

            // 원은 "베이스 테두리로부터 이동거리만큼"을 나타내야 하므로 리딩 모델
            // 반지름만큼 더해서 그린다. 모델이 하나뿐인 유닛은 코헤런시만큼
            // 이동력이 늘어난다(팔로워를 코헤런시 안에 배치할 필요가 없으므로).
            guideline.CenterMm = _unitMoveStartPoint;
            guideline.RadiusMm = leading.BoundingRadius + EffectiveMoveInch(_unitMoveUnit) * GameConstants.MmPerInch;
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
            ShowUnitMovePanel(true);
        }

        /// <summary>배치의 리딩 모델이 실제로 놓인 뒤에야 나머지 모델을 만든다 —
        /// 그 전에 만들면 클릭 지점에 겹쳐서 충돌 해소를 방해하게 된다.
        /// "유닛 되돌리기"로 되돌아온 유닛이면 damages[0]은 리딩 모델에 이미
        /// 쓰였으므로, 팔로워는 그 뒤 순서대로 이어서 가져간다.</summary>
        private void SpawnDeploymentFollowers()
        {
            var damages = _deploymentDefSnapshot != null ? _deploymentDefSnapshot.Damages : null;
            for (int i = 0; i < _pendingFollowerCount; i++)
            {
                var follower = CreatePieceObject(_unitMoveUnit, _unitMoveLeading.SizeMm, _unitMoveLeading.FillColor, _unitMoveLeading.IsDisplacement);
                int damageIndex = i + 1;
                if (damages != null && damageIndex < damages.Count)
                {
                    follower.Damage = damages[damageIndex];
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

        private Vector2 ResolveFollowerPosition(Base piece, Vector2 desiredCenter, Vector2 leadingCenter)
        {
            // 코헤런시 경계 근처로 드래그하면 스냅되도록 돕는다 — 리딩·팔로워가
            // 둘 다 원형일 때만(타원이 끼면 방향/회전에 따라 경계가 계속
            // 달라져서 스냅이 오히려 어긋나 보인다).
            bool useSnap = IsCircular(_unitMoveLeading.SizeMm) && IsCircular(piece.SizeMm);
            var pos = desiredCenter;
            for (int i = 0; i < EllipseMath.CollisionIterations; i++)
            {
                var before = pos;
                if (useSnap)
                {
                    float maxCenterDistance = DirectionalMaxFollowerDistance(piece, leadingCenter, pos);
                    pos = SnapToCoherencyBoundary(pos, leadingCenter, maxCenterDistance);
                }
                pos = ResolvePosition(piece, pos, false);
                if (Vector2.Distance(pos, before) < 0.01f)
                {
                    break;
                }
            }
            return pos;
        }

        private static bool IsCircular(Vector2 sizeMm)
        {
            return Mathf.Approximately(sizeMm.x, sizeMm.y);
        }

        /// <summary>이 방향으로 팔로워를 얼마나 멀리 놓을 수 있는지 — "팔로워
        /// 베이스 전체가 리딩 테두리로부터 코헤런시 이내"라는 규칙을 그대로
        /// 따른다: 팔로워의 바깥쪽 끝(centerDistance + followerEdge)이 리딩의
        /// 코헤런시 경계(leadingEdge + coherencyMm)를 넘지 않아야 하므로
        /// centerDistance = leadingEdge + coherencyMm - followerEdge. (두 베이스의
        /// 마주보는 면 사이 간격이 코헤런시가 되는 지점이 아니라, 노란 경계선
        /// 자체에 팔로워의 먼 쪽 끝이 닿는 지점 — 더하면 항상 그 경계 바깥으로
        /// 스냅된다.)</summary>
        private float DirectionalMaxFollowerDistance(Base follower, Vector2 leadingCenter, Vector2 atPoint)
        {
            var direction = atPoint - leadingCenter;
            direction = direction.sqrMagnitude < 0.0001f ? Vector2.right : direction.normalized;
            float coherencyMm = _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch;
            float leadingEdge = EllipseMath.EllipseRadiusInDirection(_unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, direction);
            float followerEdge = EllipseMath.EllipseRadiusInDirection(follower.SizeMm, follower.RotationRadians, direction);
            return leadingEdge + coherencyMm - followerEdge;
        }

        private static Vector2 SnapToCoherencyBoundary(Vector2 pos, Vector2 leadingCenter, float maxCenterDistance)
        {
            var offset = pos - leadingCenter;
            float dist = offset.magnitude;
            if (dist > 0.01f)
            {
                if (dist >= maxCenterDistance && dist - maxCenterDistance <= FollowerOutwardSnapThresholdMm)
                {
                    pos = leadingCenter + offset.normalized * maxCenterDistance;
                }
                else if (dist < maxCenterDistance && maxCenterDistance - dist <= FollowerSnapThresholdMm)
                {
                    pos = leadingCenter + offset.normalized * maxCenterDistance;
                }
            }
            return pos;
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
                if (_deploymentDefSnapshot != null)
                {
                    _pendingUnits.Add(_deploymentDefSnapshot);
                    RefreshPendingList();
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
            _unitMoveWarningLabel.enableWordWrapping = true;
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
