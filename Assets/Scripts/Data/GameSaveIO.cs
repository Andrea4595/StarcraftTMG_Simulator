using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 진행 중인 게임 저장/불러오기(2026-08-31 신설) — 사용자 요청: "'되돌리기'
    /// 히스토리까지 저장해줄 필요는 없고, 현재 예비대 및 택티컬 현황, 지도에
    /// 배치된 모든 요소들과 각 요소들의 상태"를 저장. 이 파일은 그 JSON의
    /// 공용 읽기/쓰기 헬퍼(제네릭 트리 왕복 + Vector2/Color/RangeSpec/
    /// SupplyTier/RosterUnitDetail 변환)와, 씬 로드 전에 먼저 채워둬야 하는
    /// "정적" 부분(MapData/MissionSettingsData/MatchState/GameConstants.
    /// TeamColors)의 적용을 담당한다. 실제 보드 위 라이브 상태(유닛/마커/
    /// 예비대/택티컬 카드)는 BoardManager.Save.cs/BoardManager.Load.cs가
    /// 담당 — BoardManager만 그 실물(baseLayer 자식들 등)에 접근할 수 있어서
    /// 여기서는 다룰 수 없다.
    ///
    /// 이 프로젝트의 기존 IO 클래스(MapPresetIO 등)는 필드 수가 적어 손으로
    /// 문자열을 이어붙였지만, 이 스키마는 그러기엔 너무 크고 깊어서
    /// MiniJson.Write(제네릭 Dictionary/List 트리 → JSON 문자열)를 새로
    /// 만들어 그 위에 얹었다 — 트리를 조립하기만 하면 직렬화는 한 번에
    /// 끝난다.
    /// </summary>
    public static class GameSaveIO
    {
        // ── 파일 I/O ────────────────────────────────────────────────

        public static void WriteToFile(string path, Dictionary<string, object> root)
        {
            File.WriteAllText(path, MiniJson.Write(root));
        }

        public static bool TryReadFile(string path, out Dictionary<string, object> root, out string error)
        {
            root = null;
            error = null;
            string jsonText;
            try
            {
                jsonText = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            try
            {
                if (!(MiniJson.Parse(jsonText) is Dictionary<string, object> dict))
                {
                    error = "저장 파일 형식이 올바르지 않습니다.";
                    return false;
                }
                root = dict;
                return true;
            }
            catch (Exception e)
            {
                error = $"JSON 파싱 실패: {e.Message}";
                return false;
            }
        }

        /// <summary>저장 파일은 스크린샷/프리셋과 같은 AppPaths.ExeDirectory()
        /// 계산을 쓴다(실행 위치 기준, 에디터/빌드 양쪽에서 동일하게 동작).
        /// 프리셋(Deployments/Missions)과 별개인 전용 Saves/ 폴더.</summary>
        public static string ResolveSavesDirectory()
        {
            string dir = Path.Combine(AppPaths.ExeDirectory(), "Saves");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ── 씬 로드 전에 먼저 채워야 하는 "정적" 부분 ───────────────────
        // MapData/MissionSettingsData/MatchState/GameConstants.TeamColors는
        // 전부 static 클래스라 GameBoard 씬이 빌드되기 전에 채워두면,
        // ScoreboardPanel/PhaseBar/BoardManager.Start()의 지형·배치구역·
        // 미션마커 생성 코드가 전부 자기 build 시점에 이 값을 그대로 읽어서
        // 별도 "다시 그리기" 호출 없이 처음부터 정확하게 그려진다 — 이미
        // Selection→TerrainSetup 흐름이 정확히 같은 방식으로 동작하므로,
        // 불러오기도 같은 지점(GameFlowBootstrap.BuildGameBoard 맨 앞)에
        // 끼워 넣었다.

        public static void ApplyLoadedStaticState(Dictionary<string, object> root)
        {
            var map = GetDict(root, "map");
            MapData.Clear();
            MapData.HasData = true;
            MapData.MapPreset = GetString(map, "map_preset", GameConstants.DefaultMapSizePreset);
            foreach (var raw in GetList(map, "deployment_zones"))
            {
                if (raw is Dictionary<string, object> z)
                {
                    MapData.DeploymentZones.Add(new DeploymentZoneData
                    {
                        Edge = GetString(z, "edge"),
                        Player = GetString(z, "player"),
                        StartAlong = GetFloat(z, "start_along"),
                        EndAlong = GetFloat(z, "end_along"),
                    });
                }
            }
            foreach (var raw in GetList(map, "mission_objectives"))
            {
                if (raw is Dictionary<string, object> o)
                {
                    MapData.MissionObjectives.Add(new MissionObjectiveData
                    {
                        Number = GetInt(o, "number"),
                        Position = new Vector2(GetFloat(o, "x"), GetFloat(o, "y")),
                    });
                }
            }
            foreach (var raw in GetList(map, "terrain_pieces"))
            {
                if (raw is Dictionary<string, object> t)
                {
                    MapData.TerrainPieces.Add(new TerrainPieceData
                    {
                        ModuleId = GetString(t, "module_id"),
                        Position = new Vector2(GetFloat(t, "x"), GetFloat(t, "y")),
                        RotationDeg = GetFloat(t, "rotation_deg"),
                    });
                }
            }

            var mission = GetDict(root, "mission");
            MissionSettingsData.Clear();
            MissionSettingsData.HasData = true;
            MissionSettingsData.MissionName = GetString(mission, "mission_name");
            MissionSettingsData.MissionParameters = GetString(mission, "mission_parameters");
            MissionSettingsData.ScoringConditions = GetString(mission, "scoring_conditions");
            MissionSettingsData.AdditionalConditions = GetString(mission, "additional_conditions");
            MissionSettingsData.BaseSupply = GetInt(mission, "base_supply");
            MissionSettingsData.SupplyPerRound = GetInt(mission, "supply_per_round");
            MissionSettingsData.RoundLength = GetInt(mission, "round_length", 5);
            MissionSettingsData.EngagementScale = GetString(mission, "engagement_scale", MissionSettingsData.EngagementScaleStandard);

            var matchState = GetDict(root, "match_state");
            MatchState.RoundNumber = GetInt(matchState, "round_number", 1);
            MatchState.PhaseIndex = GetInt(matchState, "phase_index", 0);
            MatchState.Supply = GetInt(matchState, "supply");
            MatchState.MissionVp["A"] = GetInt(matchState, "mission_vp_a");
            MatchState.MissionVp["B"] = GetInt(matchState, "mission_vp_b");
            MatchState.KillVp["A"] = GetInt(matchState, "kill_vp_a");
            MatchState.KillVp["B"] = GetInt(matchState, "kill_vp_b");

            var teamColors = GetDict(root, "team_colors");
            if (teamColors != null)
            {
                foreach (var key in new[] { "A", "B", "neutral" })
                {
                    var c = GetDict(teamColors, key);
                    if (c != null)
                    {
                        GameConstants.TeamColors[key] = TreeToColor(c);
                    }
                }
            }
        }

        // ── Vector2 / Color ─────────────────────────────────────────

        public static Dictionary<string, object> Vec2ToTree(Vector2 v)
        {
            return new Dictionary<string, object> { { "x", (double)v.x }, { "y", (double)v.y } };
        }

        public static Vector2 TreeToVec2(Dictionary<string, object> t)
        {
            return new Vector2(GetFloat(t, "x"), GetFloat(t, "y"));
        }

        public static Dictionary<string, object> ColorToTree(Color c)
        {
            return new Dictionary<string, object>
            {
                { "r", (double)c.r }, { "g", (double)c.g }, { "b", (double)c.b }, { "a", (double)c.a },
            };
        }

        public static Color TreeToColor(Dictionary<string, object> t)
        {
            return new Color(GetFloat(t, "r"), GetFloat(t, "g"), GetFloat(t, "b"), GetFloat(t, "a", 1f));
        }

        // ── RangeSpec / SupplyTier ──────────────────────────────────

        public static List<object> RangesToTree(List<RangeSpec> ranges)
        {
            var list = new List<object>();
            foreach (var r in ranges)
            {
                list.Add(new Dictionary<string, object> { { "inch", (double)r.Inch }, { "always_show", r.AlwaysShow } });
            }
            return list;
        }

        public static List<RangeSpec> TreeToRanges(List<object> raw)
        {
            var list = new List<RangeSpec>();
            foreach (var item in raw)
            {
                if (item is Dictionary<string, object> d)
                {
                    list.Add(new RangeSpec { Inch = GetFloat(d, "inch"), AlwaysShow = GetBool(d, "always_show") });
                }
            }
            return list;
        }

        public static List<object> SupplyTiersToTree(List<SupplyTier> tiers)
        {
            var list = new List<object>();
            foreach (var t in tiers)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "model_min", t.ModelMin }, { "model_max", t.ModelMax }, { "supply", t.Supply }, { "pts", t.Pts },
                });
            }
            return list;
        }

        public static List<SupplyTier> TreeToSupplyTiers(List<object> raw)
        {
            var list = new List<SupplyTier>();
            foreach (var item in raw)
            {
                if (item is Dictionary<string, object> d)
                {
                    list.Add(new SupplyTier { ModelMin = GetInt(d, "model_min"), ModelMax = GetInt(d, "model_max"), Supply = GetInt(d, "supply"), Pts = GetInt(d, "pts") });
                }
            }
            return list;
        }

        // ── RosterUnitDetail(+중첩 타입) — 유닛 상세 패널 표시용 원본
        // 로스터 정보 전체. 게임 로직에는 안 쓰이지만, 불러온 뒤에도 상세
        // 패널이 정상 동작해야 한다는 사용자 지정(전부 유지)에 따라
        // 왕복시킨다.

        public static object DetailToTree(RosterUnitDetail d)
        {
            if (d == null)
            {
                return null;
            }
            var tags = new List<object>();
            foreach (var tag in d.Tags)
            {
                tags.Add(TagToTree(tag));
            }
            var abilities = new List<object>();
            foreach (var a in d.Abilities)
            {
                abilities.Add(AbilityToTree(a));
            }
            var specialists = new List<object>();
            foreach (var s in d.Specialists)
            {
                specialists.Add(new Dictionary<string, object> { { "name_en", s.NameEn }, { "name_ko", s.NameKo } });
            }
            return new Dictionary<string, object>
            {
                { "name_en", d.NameEn }, { "name_ko", d.NameKo }, { "unit_type", d.UnitType },
                { "shield", d.Shield }, { "evasion", d.Evasion }, { "armor", d.Armor }, { "hp", d.Hp }, { "size", d.Size },
                { "tags", tags }, { "abilities", abilities },
                { "squad_tier_index", d.SquadTierIndex },
                { "specialists", specialists },
            };
        }

        public static RosterUnitDetail DetailFromTree(object raw)
        {
            if (!(raw is Dictionary<string, object> d))
            {
                return null;
            }
            var detail = new RosterUnitDetail
            {
                NameEn = GetString(d, "name_en"),
                NameKo = GetString(d, "name_ko"),
                UnitType = GetString(d, "unit_type"),
                Shield = GetString(d, "shield"),
                Evasion = GetString(d, "evasion"),
                Armor = GetString(d, "armor"),
                Hp = GetString(d, "hp"),
                Size = GetString(d, "size"),
                SquadTierIndex = GetNullableInt(d, "squad_tier_index"),
            };
            foreach (var raw2 in GetList(d, "tags"))
            {
                if (raw2 is Dictionary<string, object> t)
                {
                    detail.Tags.Add(TreeToTag(t));
                }
            }
            foreach (var raw2 in GetList(d, "abilities"))
            {
                if (raw2 is Dictionary<string, object> a)
                {
                    detail.Abilities.Add(TreeToAbility(a));
                }
            }
            foreach (var raw2 in GetList(d, "specialists"))
            {
                if (raw2 is Dictionary<string, object> s)
                {
                    detail.Specialists.Add(new RosterSpecialistEntry { NameEn = GetString(s, "name_en"), NameKo = GetString(s, "name_ko") });
                }
            }
            return detail;
        }

        public static Dictionary<string, object> TagToTree(RosterTag t)
        {
            if (t == null)
            {
                return null;
            }
            return new Dictionary<string, object> { { "name_en", t.NameEn }, { "name_ko", t.NameKo } };
        }

        public static RosterTag TreeToTag(Dictionary<string, object> t)
        {
            return new RosterTag { NameEn = GetString(t, "name_en"), NameKo = GetString(t, "name_ko") };
        }

        public static Dictionary<string, object> AbilityToTree(RosterAbilityEntry a)
        {
            var tree = new Dictionary<string, object>
            {
                { "kind", a.Kind }, { "id", a.Id }, { "name_en", a.NameEn }, { "name_ko", a.NameKo },
                { "phase", a.Phase }, { "is_upgrade", a.IsUpgrade },
                { "type", a.Type }, { "cost", a.Cost }, { "rule_en", a.RuleEn }, { "rule_ko", a.RuleKo },
            };
            if (a.Weapon != null)
            {
                var surge = new List<object>();
                foreach (var s in a.Weapon.Surge)
                {
                    surge.Add(TagToTree(s));
                }
                var keywords = new List<object>();
                foreach (var kw in a.Weapon.Keywords)
                {
                    keywords.Add(new Dictionary<string, object>
                    {
                        { "name_en", kw.NameEn }, { "name_ko", kw.NameKo }, { "suffix_en", kw.SuffixEn }, { "suffix_ko", kw.SuffixKo },
                    });
                }
                tree["weapon"] = new Dictionary<string, object>
                {
                    { "range", a.Weapon.Range }, { "target", TagToTree(a.Weapon.Target) },
                    { "roa", a.Weapon.Roa }, { "hit", a.Weapon.Hit },
                    { "surge", surge }, { "surge_die", a.Weapon.SurgeDie }, { "damage", a.Weapon.Damage },
                    { "keywords", keywords },
                };
            }
            return tree;
        }

        public static RosterAbilityEntry TreeToAbility(Dictionary<string, object> a)
        {
            var entry = new RosterAbilityEntry
            {
                Kind = GetString(a, "kind"),
                Id = GetString(a, "id"),
                NameEn = GetString(a, "name_en"),
                NameKo = GetString(a, "name_ko"),
                Phase = GetString(a, "phase"),
                IsUpgrade = GetBool(a, "is_upgrade"),
                Type = GetString(a, "type"),
                Cost = GetInt(a, "cost"),
                RuleEn = GetString(a, "rule_en"),
                RuleKo = GetString(a, "rule_ko"),
            };
            var weaponTree = GetDict(a, "weapon");
            if (weaponTree != null)
            {
                var weapon = new RosterWeaponStat
                {
                    Range = GetString(weaponTree, "range"),
                    Target = GetDict(weaponTree, "target") is Dictionary<string, object> tt ? TreeToTag(tt) : null,
                    Roa = GetString(weaponTree, "roa"),
                    Hit = GetString(weaponTree, "hit"),
                    SurgeDie = GetString(weaponTree, "surge_die"),
                    Damage = GetString(weaponTree, "damage"),
                };
                foreach (var raw in GetList(weaponTree, "surge"))
                {
                    if (raw is Dictionary<string, object> s)
                    {
                        weapon.Surge.Add(TreeToTag(s));
                    }
                }
                foreach (var raw in GetList(weaponTree, "keywords"))
                {
                    if (raw is Dictionary<string, object> kw)
                    {
                        weapon.Keywords.Add(new RosterKeyword
                        {
                            NameEn = GetString(kw, "name_en"), NameKo = GetString(kw, "name_ko"),
                            SuffixEn = GetString(kw, "suffix_en"), SuffixKo = GetString(kw, "suffix_ko"),
                        });
                    }
                }
                entry.Weapon = weapon;
            }
            return entry;
        }

        // ── 제네릭 트리 읽기 헬퍼 — MapPresetIO 등 기존 IO 클래스와 같은
        // 이름/스타일이지만, 이 파일과 BoardManager.Save.cs/Load.cs가
        // 공유해야 해서 public으로 뺐다.

        public static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            return (dict != null && dict.TryGetValue(key, out var v) ? v as List<object> : null) ?? new List<object>();
        }

        public static string GetString(Dictionary<string, object> dict, string key, string fallback = "")
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is string s ? s : fallback;
        }

        public static float GetFloat(Dictionary<string, object> dict, string key, float fallback = 0f)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;
        }

        public static int GetInt(Dictionary<string, object> dict, string key, int fallback = 0)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is double d ? (int)d : fallback;
        }

        public static bool GetBool(Dictionary<string, object> dict, string key, bool fallback = false)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is bool b ? b : fallback;
        }

        public static int? GetNullableInt(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is double d ? (int)d : (int?)null;
        }
    }
}
