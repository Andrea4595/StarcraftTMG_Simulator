using System.Collections.Generic;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 게임 저장(2026-08-31 신설) ────────────────────────────────
        // 지도/배치구역/미션 목표/지형은 MapData를, 미션 파라미터는
        // MissionSettingsData를 그대로 다시 쓴다 — Selection→TerrainSetup이
        // 이미 채워둔 값을 그대로 반영하면 되므로 baseLayer를 다시 스캔할
        // 필요가 없다(그 값들은 게임 중 바뀌지 않는다).
        //
        // "되돌리기" 히스토리는 2026-08-31엔 저장하지 않기로 했었다가(사용자
        // 지정), 리플레이 기능 준비 중(2026-09-04) 다시 검토해서 뒤집었다 —
        // "불러왔을 때 되돌리기가 남아있지 않을 이유가 없다, 일부러 지울
        // 기획적 이유가 없으면 굳이 날릴 필요 없다"(사용자 판단). 그래서
        // SaveGame은 이제 항상 히스토리를 포함해서 저장한다. 게임 도중 멀티
        // 시작(BoardManager.MidGameHandoff.cs)은 여전히 includeUndoHistory:
        // false를 쓴다 — 그쪽은 undo_history를 그 트리 밖 별도 키로 담아
        // 보내고(BuildUndoHistoryTree), 합류 이전 항목은 지우지 않는 대신
        // Locked로 표시해 조작만 막는다(LockExistingUndoHistoryForMidGameJoin,
        // 유닛 중복 버그 재발 방지 — 2026-09-04, 예전엔 통째로 지웠었다).

        public void SaveGame(string path)
        {
            GameSaveIO.WriteToFile(path, BuildFullStateTree(includeUndoHistory: true));
        }

        /// <summary>SaveGame이 파일로 쓰는 것과 완전히 같은 트리 — 2026-09-02,
        /// 게임 도중 멀티 시작(BoardManager.MidGameHandoff.cs)이 상대에게
        /// 넘겨줄 스냅샷을 만드는 데도 재사용한다(파일로 안 쓰고 네트워크로
        /// 보낼 뿐, 스키마와 그걸 불러오는 경로(GameSaveIO.ApplyLoadedStaticState
        /// + BoardManager.Load.cs의 ApplyLoadedLiveState)는 저장 파일
        /// 불러오기와 완전히 동일). includeUndoHistory=true면 되돌리기 스택
        /// 전체(BuildUndoHistoryTree, 원래 게임 도중 멀티 시작 전용이었던
        /// 것)를 "undo_history" 키로 트리 안에 함께 담는다 — SaveGame은
        /// 항상 true로 부르고(2026-09-04부터), 게임 도중 멀티 시작 전송만
        /// 여전히 기본값(false, 별도 이유는 위 클래스 상단 주석 참고)을
        /// 쓴다.</summary>
        public Dictionary<string, object> BuildFullStateTree(bool includeUndoHistory = false)
        {
            var tree = new Dictionary<string, object>
            {
                { "map", BuildMapTree() },
                { "mission", BuildMissionTree() },
                { "match_state", BuildMatchStateTree() },
                { "team_colors", BuildTeamColorsTree() },
                { "roster_loaded_teams", BuildRosterLoadedTeamsTree() },
                { "units", BuildUnitsTree() },
                { "pending_units", BuildPendingUnitsTree() },
                { "pending_tokens", BuildPendingTokensTree() },
                { "tactical_cards", BuildTacticalCardsTree() },
                { "markers", BuildMarkersTree() },
                { "mission_objective_states", BuildMissionObjectiveStatesTree() },
            };
            if (includeUndoHistory)
            {
                // Locked는 저장 데이터에 넣지 않는다 — 그 잠금은 "지금 이 멀티
                // 세션이 살아있는 동안만" 의미 있는 런타임 플래그일 뿐이라
                // (UndoRedoService.BuildUndoHistoryTree 참고), 저장 파일을
                // 나중에 불러오면 항상 풀린 상태로 시작해야 한다(사용자 지적,
                // 2026-09-09).
                tree["undo_history"] = BuildUndoHistoryTree(includeLocked: false);
            }
            return tree;
        }

        private static Dictionary<string, object> BuildMapTree()
        {
            var zones = new List<object>();
            foreach (var z in MapData.DeploymentZones)
            {
                zones.Add(new Dictionary<string, object>
                {
                    { "edge", z.Edge }, { "player", z.Player }, { "start_along", (double)z.StartAlong }, { "end_along", (double)z.EndAlong },
                });
            }
            var objectives = new List<object>();
            foreach (var o in MapData.MissionObjectives)
            {
                objectives.Add(new Dictionary<string, object> { { "number", o.Number }, { "x", (double)o.Position.x }, { "y", (double)o.Position.y } });
            }
            var terrain = new List<object>();
            foreach (var t in MapData.TerrainPieces)
            {
                terrain.Add(new Dictionary<string, object>
                {
                    { "module_id", t.ModuleId }, { "x", (double)t.Position.x }, { "y", (double)t.Position.y }, { "rotation_deg", (double)t.RotationDeg },
                });
            }
            return new Dictionary<string, object>
            {
                { "map_preset", MapData.MapPreset }, { "deployment_zones", zones }, { "mission_objectives", objectives }, { "terrain_pieces", terrain },
            };
        }

        private static Dictionary<string, object> BuildMissionTree()
        {
            return new Dictionary<string, object>
            {
                { "mission_name", MissionSettingsData.MissionName },
                { "mission_parameters", MissionSettingsData.MissionParameters },
                { "scoring_conditions", MissionSettingsData.ScoringConditions },
                { "additional_conditions", MissionSettingsData.AdditionalConditions },
                { "base_supply", MissionSettingsData.BaseSupply },
                { "supply_per_round", MissionSettingsData.SupplyPerRound },
                { "round_length", MissionSettingsData.RoundLength },
                { "engagement_scale", MissionSettingsData.EngagementScale },
            };
        }

        private static Dictionary<string, object> BuildMatchStateTree()
        {
            return new Dictionary<string, object>
            {
                { "round_number", MatchState.RoundNumber },
                { "phase_index", MatchState.PhaseIndex },
                { "supply", MatchState.Supply },
                { "mission_vp_a", MatchState.MissionVp["A"] },
                { "mission_vp_b", MatchState.MissionVp["B"] },
                { "kill_vp_a", MatchState.KillVp["A"] },
                { "kill_vp_b", MatchState.KillVp["B"] },
            };
        }

        private static Dictionary<string, object> BuildTeamColorsTree()
        {
            var tree = new Dictionary<string, object>();
            foreach (var kv in GameConstants.TeamColors)
            {
                tree[kv.Key] = GameSaveIO.ColorToTree(kv.Value);
            }
            return tree;
        }

        private List<object> BuildRosterLoadedTeamsTree()
        {
            var list = new List<object>();
            foreach (var team in _rosterLoadedTeams)
            {
                list.Add(team);
            }
            return list;
        }

        /// <summary>baseLayer 위 모든 Base 조각을 Unit별로 묶어서 직렬화한다 —
        /// CaptureBoardSnapshot(BoardManager.UndoRedo.cs)과 같은 순회 방식.
        /// 미션 목표 마커/배치구역 표시(DeploymentZonePiece)는 Base가 아니라
        /// 별도 컴포넌트라 이 순회에 안 걸린다(의도된 동작 — 그건 MapData에서
        /// 이미 저장됨).</summary>
        private List<object> BuildUnitsTree()
        {
            var units = new List<object>();
            var seen = new HashSet<Unit>();
            for (int i = 0; i < baseLayer.childCount; i++)
            {
                var piece = baseLayer.GetChild(i).GetComponent<Base>();
                if (piece == null || piece.Unit == null)
                {
                    continue;
                }
                var unit = piece.Unit;
                if (seen.Add(unit))
                {
                    units.Add(BuildUnitTree(unit));
                }
            }
            return units;
        }

        /// <summary>유닛 하나를 트리로 직렬화한다 — BuildUnitsTree(전체 저장)와
        /// BoardNetworkSync 유닛 동기화(BoardManager.UnitSync.cs) 양쪽이 쓴다.</summary>
        private Dictionary<string, object> BuildUnitTree(Unit unit)
        {
            var models = new List<object>();
            foreach (var m in unit.Models)
            {
                models.Add(new Dictionary<string, object>
                {
                    { "center", GameSaveIO.Vec2ToTree(m.Center) },
                    { "rotation_degrees", (double)m.RotationDegrees },
                    { "size_mm", GameSaveIO.Vec2ToTree(m.SizeMm) },
                    { "fill_color", GameSaveIO.ColorToTree(m.FillColor) },
                    { "damage", m.Damage },
                    { "is_displacement", m.IsDisplacement },
                    { "memo", m.Memo },
                });
            }
            var ranges = _unitRanges.TryGetValue(unit, out var unitRanges) ? unitRanges : new List<RangeSpec>();
            return new Dictionary<string, object>
            {
                { "network_unit_id", unit.NetworkUnitId },
                { "unit_name", unit.UnitName }, { "team", unit.Team },
                { "coherency_inch", (double)unit.CoherencyInch }, { "move_inch", (double)unit.MoveInch },
                { "is_token", unit.IsToken }, { "can_move", unit.CanMove },
                { "supply_override", unit.SupplyOverride },
                { "supply_tiers", GameSaveIO.SupplyTiersToTree(unit.SupplyTiers) },
                { "ranges", GameSaveIO.RangesToTree(ranges) },
                { "detail", GameSaveIO.DetailToTree(unit.Detail) },
                { "models", models },
            };
        }

        private List<object> BuildPendingUnitsTree()
        {
            var list = new List<object>();
            foreach (var def in _pendingUnits)
            {
                var damages = new List<object>();
                foreach (var d in def.Damages)
                {
                    damages.Add(d);
                }
                var specialists = new List<object>();
                foreach (var s in def.Specialists)
                {
                    specialists.Add(s);
                }
                list.Add(new Dictionary<string, object>
                {
                    { "name", def.Name }, { "team", def.Team }, { "model_count", def.ModelCount },
                    { "size_mm", GameSaveIO.Vec2ToTree(def.SizeMm) }, { "fill_color", GameSaveIO.ColorToTree(def.FillColor) },
                    { "move_inch", (double)def.MoveInch }, { "coherency_inch", (double)def.CoherencyInch },
                    { "can_move", def.CanMove }, { "is_displacement", def.IsDisplacement },
                    { "supply_tiers", GameSaveIO.SupplyTiersToTree(def.SupplyTiers) },
                    { "damages", damages }, { "ranges", GameSaveIO.RangesToTree(def.Ranges) },
                    { "supply_override", def.SupplyOverride }, { "specialists", specialists },
                    { "detail", GameSaveIO.DetailToTree(def.Detail) },
                });
            }
            return list;
        }

        private List<object> BuildPendingTokensTree()
        {
            var list = new List<object>();
            foreach (var def in _pendingRosterTokens)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "name", def.Name }, { "team", def.Team }, { "size_mm", GameSaveIO.Vec2ToTree(def.SizeMm) },
                    { "is_displacement", def.IsDisplacement }, { "ranges", GameSaveIO.RangesToTree(def.Ranges) },
                });
            }
            return list;
        }

        private List<object> BuildTacticalCardsTree()
        {
            var list = new List<object>();
            foreach (var def in _pendingTacticalCards)
            {
                var abilities = new List<object>();
                foreach (var a in def.Abilities)
                {
                    abilities.Add(GameSaveIO.AbilityToTree(a));
                }
                list.Add(new Dictionary<string, object>
                {
                    { "name", def.Name }, { "team", def.Team }, { "count", def.Count }, { "remaining", def.Remaining },
                    { "resource_abbr", def.ResourceAbbr }, { "resource_amount", def.ResourceAmount },
                    { "abilities", abilities },
                });
            }
            return list;
        }

        /// <summary>미션 목표 마커(baseLayer 위 MissionObjectivePiece)의 우클릭
        /// 순환 상태(비활성/활성/A/B) — 위치는 "map" 섹션(MapData 그대로)에
        /// 이미 저장되므로, 여기서는 라이브 상태만 번호로 짝지어 따로
        /// 저장한다.</summary>
        private List<object> BuildMissionObjectiveStatesTree()
        {
            var list = new List<object>();
            for (int i = 0; i < baseLayer.childCount; i++)
            {
                var piece = baseLayer.GetChild(i).GetComponent<MissionObjectivePiece>();
                if (piece == null)
                {
                    continue;
                }
                list.Add(new Dictionary<string, object> { { "number", piece.Number }, { "ring_state", piece.RingColorState } });
            }
            return list;
        }

        /// <summary>배치 미리보기(반투명 고스트)는 실제 마커가 아니므로 뺀다
        /// (CaptureBoardSnapshot과 같은 이유). 미션 목표 마커/점령 링(우클릭
        /// 상태)은 baseLayer에 있는 MissionObjectivePiece라 여기 안 걸리고,
        /// "map" 섹션(위치)과 별도로 라이브 상태만 아래 BuildMissionObjectiveRingStatesTree로
        /// 따로 저장한다.</summary>
        private List<object> BuildMarkersTree()
        {
            var list = new List<object>();
            foreach (var markerGo in EnumerateRealMarkers())
            {
                // network_marker_id는 2026-09-02 추가 — 파일 저장 자체엔 필요
                // 없지만(솔로 불러오기는 새로 놓는 것과 동일), 게임 도중 멀티
                // 시작(BoardManager.MidGameHandoff.cs)이 이 값을 몰라야 하는
                // 파일 저장과 알아야 하는 네트워크 전송 양쪽에 같은 트리를
                // 재사용하므로 여기 같이 실어 보낸다 — -1(미배정)이어도 안전
                // (network_unit_id와 같은 이유로 harmless).
                if (markerGo.TryGetComponent<ActivationMarker>(out var act))
                {
                    list.Add(new Dictionary<string, object> { { "kind", "activation" }, { "center", GameSaveIO.Vec2ToTree(act.Center) }, { "state", act.State }, { "network_marker_id", act.NetworkMarkerId } });
                }
                else if (markerGo.TryGetComponent<CaptureMarker>(out var cap))
                {
                    list.Add(new Dictionary<string, object> { { "kind", "capture" }, { "center", GameSaveIO.Vec2ToTree(cap.Center) }, { "state", cap.ColorState }, { "network_marker_id", cap.NetworkMarkerId } });
                }
                else if (markerGo.TryGetComponent<IconMarker>(out var icon))
                {
                    list.Add(new Dictionary<string, object> { { "kind", icon.Kind }, { "center", GameSaveIO.Vec2ToTree(icon.Center) }, { "state", "" }, { "network_marker_id", icon.NetworkMarkerId } });
                }
            }
            return list;
        }
    }
}
