using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 되돌리기(ctrl+z) / 다시 실행(ctrl+shift+z) ───────────────────

        private void HandleUndoRedoInput()
        {
            if (!Input.GetKeyDown(KeyCode.Z) || !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                return;
            }
            if (IsTextFieldFocused())
            {
                // 이름/데미지/범위 입력 필드에 포커스가 있으면 그 필드 자체의
                // 실행취소(ctrl+z)로 남겨둔다 — 게임판 되돌리기가 가로채지 않는다.
                return;
            }
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
            {
                Redo();
            }
            else
            {
                Undo();
            }
        }

        private void BeginUndoTransaction()
        {
            if (_undoPendingActive)
            {
                return;
            }
            _undoPendingSnapshot = CaptureBoardSnapshot();
            _undoPendingActive = true;
        }

        private void CommitUndoTransaction()
        {
            if (!_undoPendingActive)
            {
                return;
            }
            _undoStack.Add(_undoPendingSnapshot);
            _redoStack.Clear();
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
        }

        private void DiscardUndoTransaction()
        {
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
        }

        /// <summary>진행 중인 트랜잭션이 있으면(드래그/유닛 이동/변위 배치 등) 그
        /// 중간 상태를 되돌리기로 덮어써서 망가뜨리면 안 되므로 무시한다.
        /// 다이얼로그나 다이얼 메뉴가 떠 있을 때도 마찬가지 — 그 뒤에서 보드가
        /// 바뀌면 열려 있는 창이 가리키는 대상(_menuTarget 등)이 붕 뜨게 된다.
        /// Godot판은 이 목록에 메모 다이얼로그를 빼먹었는데(아마 실수), Unity의
        /// UnityEngine.Object는 파괴된 오브젝트를 == null로 안전하게 취급해서
        /// 위험이 적긴 하지만 굳이 같은 구멍을 재현할 이유가 없어 포함시켰다.</summary>
        private bool IsUndoBlocked()
        {
            if (_undoPendingActive)
            {
                return true;
            }
            if (IsDialogVisible(damageDialog) || IsDialogVisible(memoDialog)
                    || IsDialogVisible(rangeInputDialog) || (radialMenu != null && radialMenu.gameObject.activeSelf)
                    || (diceRollDialog != null && diceRollDialog.gameObject.activeSelf))
            {
                return true;
            }
            return false;
        }

        private static bool IsDialogVisible(InputDialog dialog)
        {
            return dialog != null && dialog.gameObject.activeSelf;
        }

        private static bool IsDialogVisible(RangeInputDialog dialog)
        {
            return dialog != null && dialog.gameObject.activeSelf;
        }

        private void Undo()
        {
            if (IsUndoBlocked() || _undoStack.Count == 0)
            {
                return;
            }
            var current = CaptureBoardSnapshot();
            var previous = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(current);
            RestoreBoardSnapshot(previous);
        }

        private void Redo()
        {
            if (IsUndoBlocked() || _redoStack.Count == 0)
            {
                return;
            }
            var current = CaptureBoardSnapshot();
            var next = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(current);
            RestoreBoardSnapshot(next);
        }

        private BoardSnapshot CaptureBoardSnapshot()
        {
            var snapshot = new BoardSnapshot();
            var unitRefIds = new Dictionary<Unit, int>();

            for (int i = 0; i < baseLayer.childCount; i++)
            {
                var piece = baseLayer.GetChild(i).GetComponent<Base>();
                if (piece == null || piece.Unit == null)
                {
                    continue;
                }
                var unit = piece.Unit;
                if (!unitRefIds.TryGetValue(unit, out int uidx))
                {
                    uidx = snapshot.Units.Count;
                    unitRefIds[unit] = uidx;
                    var unitSnap = new UnitSnapshot
                    {
                        UnitName = unit.UnitName,
                        Team = unit.Team,
                        CoherencyInch = unit.CoherencyInch,
                        MoveInch = unit.MoveInch,
                        IsToken = unit.IsToken,
                        CanMove = unit.CanMove,
                        SupplyOverride = unit.SupplyOverride,
                        Detail = unit.Detail,
                    };
                    unitSnap.SupplyTiers.AddRange(unit.SupplyTiers);
                    snapshot.Units.Add(unitSnap);
                }
                snapshot.Units[uidx].Models.Add(new ModelSnapshot
                {
                    Center = piece.Center,
                    RotationDegrees = piece.RotationDegrees,
                    SizeMm = piece.SizeMm,
                    FillColor = piece.FillColor,
                    Damage = piece.Damage,
                    IsDisplacement = piece.IsDisplacement,
                    Memo = piece.Memo,
                });
            }

            foreach (var kv in _unitRanges)
            {
                if (!unitRefIds.TryGetValue(kv.Key, out int uidx))
                {
                    continue; // 보드에 모델이 하나도 없는 유닛(방금 마지막 모델이 지워짐) — 스냅샷에서 뺀다.
                }
                snapshot.Ranges.Add(new RangeSnapshot { UnitRef = uidx, Ranges = new List<RangeSpec>(kv.Value) });
            }

            foreach (var def in _pendingUnits)
            {
                snapshot.PendingUnits.Add(ClonePendingUnitDef(def));
            }

            foreach (var def in _pendingRosterTokens)
            {
                snapshot.PendingRosterTokens.Add(ClonePendingTokenDef(def));
            }

            if (markerLayer != null)
            {
                for (int i = 0; i < markerLayer.childCount; i++)
                {
                    var markerGo = markerLayer.GetChild(i).gameObject;
                    // 배치 미리보기(반투명 고스트)는 실제 마커가 아니므로 스냅샷에서 뺀다.
                    if (_markerPlacementPreview != null && markerGo == _markerPlacementPreview.gameObject)
                    {
                        continue;
                    }
                    if (markerGo.TryGetComponent<ActivationMarker>(out var act))
                    {
                        snapshot.Markers.Add(new MarkerSnapshot { Kind = "activation", Center = act.Center, State = act.State });
                    }
                    else if (markerGo.TryGetComponent<CaptureMarker>(out var cap))
                    {
                        snapshot.Markers.Add(new MarkerSnapshot { Kind = "capture", Center = cap.Center, State = cap.ColorState });
                    }
                    else if (markerGo.TryGetComponent<IconMarker>(out var icon))
                    {
                        snapshot.Markers.Add(new MarkerSnapshot { Kind = icon.Kind, Center = icon.Center, State = "" });
                    }
                }
            }

            return snapshot;
        }

        private void RestoreBoardSnapshot(BoardSnapshot snapshot)
        {
            for (int i = baseLayer.childCount - 1; i >= 0; i--)
            {
                Destroy(baseLayer.GetChild(i).gameObject);
            }
            _pieces.Clear();

            if (markerLayer != null)
            {
                for (int i = markerLayer.childCount - 1; i >= 0; i--)
                {
                    var child = markerLayer.GetChild(i);
                    if (_markerPlacementPreview != null && child.gameObject == _markerPlacementPreview.gameObject)
                    {
                        continue; // 배치 미리보기는 스냅샷 대상이 아니었으니 복원 때도 안 지운다.
                    }
                    Destroy(child.gameObject);
                }
            }

            // 지워진 베이스/유닛을 참조하던 값들을 전부 정리 — 복원 뒤에도 남아있으면
            // 파괴된 인스턴스를 가리키는 참조가 된다.
            _hoveredBase = null;
            _hoveredUnit = null;
            _menuTarget = null;
            _selectedUnitForDetailByTeam.Clear();
            _rangeTargetUnit = null;
            _rangeDeleteTargetUnit = null;
            _unitRanges.Clear();
            _rosterTokenUnits.Clear();
            _draggingPiece = null;
            _draggingFollower = null;
            _draggingMarker = null;

            var restoredUnits = new List<Unit>();
            foreach (var unitSnap in snapshot.Units)
            {
                var unit = new Unit
                {
                    UnitName = unitSnap.UnitName,
                    Team = unitSnap.Team,
                    CoherencyInch = unitSnap.CoherencyInch,
                    MoveInch = unitSnap.MoveInch,
                    IsToken = unitSnap.IsToken,
                    CanMove = unitSnap.CanMove,
                    SupplyOverride = unitSnap.SupplyOverride,
                    Detail = unitSnap.Detail,
                };
                unit.SupplyTiers.AddRange(unitSnap.SupplyTiers);
                restoredUnits.Add(unit);
                if (unit.IsToken)
                {
                    _rosterTokenUnits[$"{unit.Team}|{unit.UnitName}"] = unit;
                }

                foreach (var modelSnap in unitSnap.Models)
                {
                    var piece = CreatePieceObject(unit, modelSnap.SizeMm, modelSnap.FillColor, modelSnap.IsDisplacement);
                    piece.Damage = modelSnap.Damage;
                    piece.Memo = modelSnap.Memo;
                    piece.Center = modelSnap.Center;
                    piece.RotationDegrees = modelSnap.RotationDegrees;
                    piece.Refresh();
                    unit.Models.Add(piece);
                }
            }

            foreach (var rangeSnap in snapshot.Ranges)
            {
                var unit = restoredUnits[rangeSnap.UnitRef];
                _unitRanges[unit] = new List<RangeSpec>(rangeSnap.Ranges);
            }

            _pendingUnits.Clear();
            foreach (var def in snapshot.PendingUnits)
            {
                _pendingUnits.Add(ClonePendingUnitDef(def));
            }
            RefreshPendingList();

            _pendingRosterTokens.Clear();
            foreach (var def in snapshot.PendingRosterTokens)
            {
                _pendingRosterTokens.Add(ClonePendingTokenDef(def));
            }
            RefreshRosterTokenList();

            if (markerLayer != null)
            {
                foreach (var markerSnap in snapshot.Markers)
                {
                    MarkerBase marker = CreateMarkerObject(markerSnap.Kind, markerLayer);
                    switch (markerSnap.Kind)
                    {
                        case "activation":
                            ((ActivationMarker)marker).SetState(markerSnap.State);
                            marker.RightClicked += OnActivationMarkerRightClicked;
                            break;
                        case "capture":
                            ((CaptureMarker)marker).SetColorState(markerSnap.State);
                            marker.RightClicked += OnCaptureMarkerRightClicked;
                            break;
                        default:
                            marker.RightClicked += OnIconMarkerRightClicked;
                            break;
                    }
                    marker.Center = markerSnap.Center;
                    marker.DragRequested += OnMarkerDragRequested;
                }
            }

            RefreshRangeOverlays();
        }

        private static PendingUnitDef ClonePendingUnitDef(PendingUnitDef def)
        {
            return new PendingUnitDef
            {
                Name = def.Name,
                Team = def.Team,
                ModelCount = def.ModelCount,
                SizeMm = def.SizeMm,
                FillColor = def.FillColor,
                MoveInch = def.MoveInch,
                CoherencyInch = def.CoherencyInch,
                CanMove = def.CanMove,
                IsDisplacement = def.IsDisplacement,
                SupplyTiers = new List<SupplyTier>(def.SupplyTiers),
                Damages = new List<int>(def.Damages),
                Ranges = new List<RangeSpec>(def.Ranges),
                SupplyOverride = def.SupplyOverride,
                Specialists = new List<string>(def.Specialists),
                Detail = def.Detail,
            };
        }

        private static PendingTokenDef ClonePendingTokenDef(PendingTokenDef def)
        {
            return new PendingTokenDef
            {
                Name = def.Name,
                Team = def.Team,
                SizeMm = def.SizeMm,
                IsDisplacement = def.IsDisplacement,
                Ranges = new List<RangeSpec>(def.Ranges),
            };
        }

        private class UnitSnapshot
        {
            public string UnitName;
            public string Team;
            public float CoherencyInch;
            public float MoveInch;
            public bool IsToken;
            public bool CanMove;
            public int? SupplyOverride;
            public RosterUnitDetail Detail;
            public readonly List<SupplyTier> SupplyTiers = new List<SupplyTier>();
            public readonly List<ModelSnapshot> Models = new List<ModelSnapshot>();
        }

        private class ModelSnapshot
        {
            public Vector2 Center;
            public float RotationDegrees;
            public Vector2 SizeMm;
            public Color FillColor;
            public int Damage;
            public bool IsDisplacement;
            public string Memo;
        }

        private class RangeSnapshot
        {
            public int UnitRef;
            public List<RangeSpec> Ranges;
        }

        private class MarkerSnapshot
        {
            public string Kind; // "activation" / "capture" / 그 외(아이콘 kind 문자열)
            public Vector2 Center;
            public string State; // activation: State 문자열, capture: ColorState 문자열, icon: 안 씀("")
        }

        private class BoardSnapshot
        {
            public readonly List<UnitSnapshot> Units = new List<UnitSnapshot>();
            public readonly List<RangeSnapshot> Ranges = new List<RangeSnapshot>();
            public readonly List<PendingUnitDef> PendingUnits = new List<PendingUnitDef>();
            public readonly List<PendingTokenDef> PendingRosterTokens = new List<PendingTokenDef>();
            public readonly List<MarkerSnapshot> Markers = new List<MarkerSnapshot>();
        }
    }
}
