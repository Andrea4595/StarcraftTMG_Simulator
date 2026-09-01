using System.Collections.Generic;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 게임 불러오기(2026-08-31 신설) ────────────────────────────
        // Start()가 이미 지형/배치구역/미션 목표 마커(MapData 기반, 정적
        // 상태는 GameFlowBootstrap이 씬을 짓기 전에 GameSaveIO.
        // ApplyLoadedStaticState로 이미 채워뒀다 — 이 파일이 실제로 하는
        // 일은 그 이후의 "라이브" 상태(보드 위 유닛/마커/예비대/택티컬
        // 카드/미션 마커 순환 상태)뿐이다. RestoreBoardSnapshot(BoardManager.
        // UndoRedo.cs)과 거의 같은 순서를 따르지만, Unit을 참조로 재사용하는
        // 대신(디스크에서 막 읽어온 값이라 재사용할 원본이 없다) 전부 새로
        // 만든다.

        private void ApplyLoadedLiveState(Dictionary<string, object> root)
        {
            _rosterLoadedTeams.Clear();
            foreach (var raw in GameSaveIO.GetList(root, "roster_loaded_teams"))
            {
                if (raw is string team)
                {
                    _rosterLoadedTeams.Add(team);
                }
            }

            foreach (var raw in GameSaveIO.GetList(root, "units"))
            {
                if (raw is Dictionary<string, object> u)
                {
                    CreateUnitFromTree(u);
                }
            }

            foreach (var raw in GameSaveIO.GetList(root, "pending_units"))
            {
                if (raw is Dictionary<string, object> p)
                {
                    _pendingUnits.Add(ParsePendingUnitDefTree(p));
                }
            }

            foreach (var raw in GameSaveIO.GetList(root, "pending_tokens"))
            {
                if (!(raw is Dictionary<string, object> t))
                {
                    continue;
                }
                _pendingRosterTokens.Add(new PendingTokenDef
                {
                    Name = GameSaveIO.GetString(t, "name"),
                    Team = GameSaveIO.GetString(t, "team", "neutral"),
                    SizeMm = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(t, "size_mm")),
                    IsDisplacement = GameSaveIO.GetBool(t, "is_displacement"),
                    Ranges = GameSaveIO.TreeToRanges(GameSaveIO.GetList(t, "ranges")),
                });
            }

            foreach (var raw in GameSaveIO.GetList(root, "tactical_cards"))
            {
                if (raw is Dictionary<string, object> c)
                {
                    _pendingTacticalCards.Add(ParseTacticalCardDefTree(c));
                }
            }

            if (markerLayer != null)
            {
                foreach (var raw in GameSaveIO.GetList(root, "markers"))
                {
                    if (!(raw is Dictionary<string, object> m))
                    {
                        continue;
                    }
                    string kind = GameSaveIO.GetString(m, "kind");
                    string state = GameSaveIO.GetString(m, "state");
                    MarkerBase marker = CreateMarkerObject(kind, markerLayer);
                    switch (kind)
                    {
                        case "activation":
                            ((ActivationMarker)marker).SetState(state);
                            marker.RightClicked += OnActivationMarkerRightClicked;
                            break;
                        case "capture":
                            ((CaptureMarker)marker).SetColorState(state);
                            marker.RightClicked += OnCaptureMarkerRightClicked;
                            break;
                        default:
                            marker.RightClicked += OnIconMarkerRightClicked;
                            break;
                    }
                    marker.Center = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "center"));
                    marker.DragRequested += OnMarkerDragRequested;

                    // 2026-09-02 추가 — 게임 도중 멀티 시작으로 받은 상태라면
                    // 이 마커가 호스트 쪽에서 이미 발급받은 네트워크 id를
                    // 갖고 있을 수 있다(BoardManager.MidGameHandoff.cs가 보내기
                    // 전에 미리 채워둠). 그걸 그대로 이어받아야 이후 이동/삭제/
                    // 상태변경 방송이 양쪽에서 같은 id로 짝지어진다. 옛 저장
                    // 파일이나 순수 솔로 저장은 이 필드가 없어 -1(기본값)로
                    // 남고, 그러면 등록하지 않는다(일반 배치와 동일하게 취급).
                    int networkMarkerId = GameSaveIO.GetInt(m, "network_marker_id", -1);
                    if (networkMarkerId >= 0)
                    {
                        marker.NetworkMarkerId = networkMarkerId;
                        _networkedMarkersById[networkMarkerId] = marker;
                    }
                }
            }

            // 미션 목표 마커의 우클릭 순환 상태 — 위치/토큰은 이미 Start()의
            // BuildMissionObjectiveVisuals()가 MapData 기준으로 다 지어놨으니
            // (GameSaveIO.ApplyLoadedStaticState가 GameFlowBootstrap에서 미리
            // MapData를 채워뒀다), 여기서는 번호로 짝지어 상태만 되돌린다.
            var ringStates = new Dictionary<int, string>();
            foreach (var raw in GameSaveIO.GetList(root, "mission_objective_states"))
            {
                if (raw is Dictionary<string, object> s)
                {
                    ringStates[GameSaveIO.GetInt(s, "number")] = GameSaveIO.GetString(s, "ring_state");
                }
            }
            if (ringStates.Count > 0)
            {
                for (int i = 0; i < baseLayer.childCount; i++)
                {
                    var piece = baseLayer.GetChild(i).GetComponent<MissionObjectivePiece>();
                    if (piece != null && ringStates.TryGetValue(piece.Number, out var state))
                    {
                        piece.SetRingColorState(state);
                    }
                }
            }

            RefreshPendingList();
            RefreshRosterTokenList();
            RefreshTacticalCardList();
            RefreshRangeOverlays();

            // 리플레이 저장 파일(BuildFullStateTree(includeUndoHistory: true)
            // 로 쓰인 것)에만 있다(2026-09-04 추가) — 평범한 저장 파일에는
            // 이 키 자체가 없으므로 GameSaveIO.GetDict가 null을 돌려주고,
            // ApplySeededUndoHistory는 안에서 GameSaveIO.GetList(null도
            // 안전하게 빈 리스트로 처리)만 쓰므로 null을 넘겨도 그냥 두
            // 스택을 비운 채로 둔다(Start()에서 어차피 처음부터 비어있으므로
            // 사실상 아무 일도 안 일어남) — 굳이 키가 있는지부터 확인할
            // 필요 없이 항상 호출해도 안전하다.
            ApplySeededUndoHistory(GameSaveIO.GetDict(root, "undo_history"));
        }

        /// <summary>예비대 정의 하나를 트리에서 만든다 — 게임 불러오기와
        /// 예비대 목록 동기화(BoardManager.PendingUnitSync.cs) 둘 다 쓴다.</summary>
        private static PendingUnitDef ParsePendingUnitDefTree(Dictionary<string, object> p)
        {
            var damages = new List<int>();
            foreach (var d in GameSaveIO.GetList(p, "damages"))
            {
                if (d is double dd)
                {
                    damages.Add((int)dd);
                }
            }
            var specialists = new List<string>();
            foreach (var s in GameSaveIO.GetList(p, "specialists"))
            {
                if (s is string ss)
                {
                    specialists.Add(ss);
                }
            }
            return new PendingUnitDef
            {
                Name = GameSaveIO.GetString(p, "name"),
                Team = GameSaveIO.GetString(p, "team", "neutral"),
                ModelCount = GameSaveIO.GetInt(p, "model_count", 1),
                SizeMm = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(p, "size_mm")),
                FillColor = GameSaveIO.TreeToColor(GameSaveIO.GetDict(p, "fill_color")),
                MoveInch = GameSaveIO.GetFloat(p, "move_inch", GameConstants.DefaultMoveInch),
                CoherencyInch = GameSaveIO.GetFloat(p, "coherency_inch", GameConstants.DefaultCoherencyInch),
                CanMove = GameSaveIO.GetBool(p, "can_move", true),
                IsDisplacement = GameSaveIO.GetBool(p, "is_displacement"),
                SupplyTiers = GameSaveIO.TreeToSupplyTiers(GameSaveIO.GetList(p, "supply_tiers")),
                Damages = damages,
                Ranges = GameSaveIO.TreeToRanges(GameSaveIO.GetList(p, "ranges")),
                SupplyOverride = GameSaveIO.GetNullableInt(p, "supply_override"),
                Specialists = specialists,
                Detail = GameSaveIO.DetailFromTree(p.TryGetValue("detail", out var pDetailRaw) ? pDetailRaw : null),
            };
        }

        /// <summary>택티컬 카드 정의 하나를 트리에서 만든다 — 게임 불러오기와
        /// 택티컬 카드 목록 동기화(BoardManager.TacticalCardSync.cs) 둘 다
        /// 쓴다(2026-09-02, ParsePendingUnitDefTree와 같은 이유로 추출).</summary>
        private static TacticalCardDef ParseTacticalCardDefTree(Dictionary<string, object> c)
        {
            var abilities = new List<RosterAbilityEntry>();
            foreach (var rawAbility in GameSaveIO.GetList(c, "abilities"))
            {
                if (rawAbility is Dictionary<string, object> a)
                {
                    abilities.Add(GameSaveIO.TreeToAbility(a));
                }
            }
            return new TacticalCardDef
            {
                Name = GameSaveIO.GetString(c, "name"),
                Team = GameSaveIO.GetString(c, "team", "neutral"),
                Count = GameSaveIO.GetInt(c, "count", 1),
                Remaining = GameSaveIO.GetInt(c, "remaining", 1),
                ResourceAbbr = GameSaveIO.GetString(c, "resource_abbr"),
                ResourceAmount = GameSaveIO.GetInt(c, "resource_amount"),
                Abilities = abilities,
            };
        }
    }
}
