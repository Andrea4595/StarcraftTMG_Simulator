using System;
using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 로스터 앱이 내보낸 JSON을 파싱해서 예비대 유닛/토큰 정의로 바꾼다.
    /// Godot판 GameBoard.gd의 _on_roster_file_selected/_parse_roster_name/
    /// _parse_roster_speed/_parse_roster_ranges 포팅 — MiniJson으로 판 JSON
    /// 트리(Dictionary/List)를 그대로 순회한다. 팀은 파일에 없고 불러올 때
    /// 고른 쪽으로 붙는다. tags/abilities 등 시뮬레이터가 안 쓰는 나머지
    /// 텍스트 필드는 그냥 무시한다(Godot판과 동일). "squad_tiers"(남은 모델
    /// 수 구간별 서플라이 단계표)는 읽는다(pts는 여전히 무시) — 스코어보드의
    /// 팀별 서플라이 표시에 쓰인다. "squad_tier_index"(로스터 작성 시 고른
    /// 단계)는 안 읽는다 — 인게임 현재 서플라이는 항상 남은 모델 수로
    /// 그때그때 다시 찾으므로 필요 없다. "tactical_cards"는 name/count만
    /// 읽는다(gas_cost/resource/slots/abilities는 무시) — 예비대 패널의
    /// 택티컬 카드 목록에 쓰인다.
    /// </summary>
    public static class RosterImporter
    {
        public static bool TryImport(string jsonText, string team,
                out List<PendingUnitDef> units, out List<PendingTokenDef> tokens,
                out List<TacticalCardDef> tacticalCards, out string error)
        {
            units = new List<PendingUnitDef>();
            tokens = new List<PendingTokenDef>();
            tacticalCards = new List<TacticalCardDef>();
            error = null;

            object parsed;
            try
            {
                parsed = MiniJson.Parse(jsonText);
            }
            catch (Exception e)
            {
                error = $"JSON 파싱 실패: {e.Message}";
                return false;
            }

            if (!(parsed is Dictionary<string, object> root) || !root.ContainsKey("units"))
            {
                error = "로스터 파일 형식이 올바르지 않습니다(\"units\" 없음).";
                return false;
            }

            var fillColor = GameConstants.TeamColors.TryGetValue(team, out var c) ? c : GameConstants.TeamColors["neutral"];

            if (root["units"] is List<object> unitList)
            {
                foreach (var raw in unitList)
                {
                    if (!(raw is Dictionary<string, object> unitData) || !unitData.ContainsKey("name"))
                    {
                        continue;
                    }
                    string unitName = ParseRosterName(unitData["name"]);
                    if (string.IsNullOrEmpty(unitName))
                    {
                        continue;
                    }
                    var baseMm = GetDict(unitData, "base_mm");
                    float width = GetFloat(baseMm, "width", 32f);
                    float height = GetFloat(baseMm, "height", width);
                    var speed = ParseRosterSpeed(unitData);

                    units.Add(new PendingUnitDef
                    {
                        Name = unitName,
                        Team = team,
                        ModelCount = Mathf.Max((int)GetFloat(unitData, "model_count", 1f), 1),
                        SizeMm = new Vector2(Mathf.Max(width, 1f), Mathf.Max(height, 1f)),
                        FillColor = fillColor,
                        MoveInch = speed.MoveInch,
                        CoherencyInch = speed.CoherencyInch,
                        CanMove = speed.CanMove,
                        IsDisplacement = GetBool(unitData, "is_displacement", false),
                        SupplyTiers = ParseSupplyTiers(GetList(unitData, "squad_tiers")),
                        Ranges = ParseRosterRanges(GetList(unitData, "ranges")),
                    });
                }
            }

            if (root.TryGetValue("tokens", out var tokensRaw) && tokensRaw is List<object> tokenList)
            {
                foreach (var raw in tokenList)
                {
                    if (!(raw is Dictionary<string, object> tokenData) || !tokenData.ContainsKey("name"))
                    {
                        continue;
                    }
                    string tokenName = ParseRosterName(tokenData["name"]);
                    if (string.IsNullOrEmpty(tokenName))
                    {
                        continue;
                    }
                    var baseMm = GetDict(tokenData, "base_mm");
                    float width = GetFloat(baseMm, "width", 32f);
                    float height = GetFloat(baseMm, "height", width);

                    tokens.Add(new PendingTokenDef
                    {
                        Name = tokenName,
                        Team = team,
                        SizeMm = new Vector2(Mathf.Max(width, 1f), Mathf.Max(height, 1f)),
                        IsDisplacement = GetBool(tokenData, "is_displacement", false),
                        Ranges = ParseRosterRanges(GetList(tokenData, "ranges")),
                    });
                }
            }

            if (root.TryGetValue("tactical_cards", out var cardsRaw) && cardsRaw is List<object> cardList)
            {
                foreach (var raw in cardList)
                {
                    if (!(raw is Dictionary<string, object> cardData) || !cardData.ContainsKey("name"))
                    {
                        continue;
                    }
                    string cardName = ParseRosterName(cardData["name"]);
                    if (string.IsNullOrEmpty(cardName))
                    {
                        continue;
                    }
                    int count = Mathf.Max((int)GetFloat(cardData, "count", 1f), 1);
                    tacticalCards.Add(new TacticalCardDef
                    {
                        Name = cardName,
                        Team = team,
                        Count = count,
                        Remaining = count,
                    });
                }
            }

            return true;
        }

        /// <summary>신형({"en","ko"}) / 구형(문자열) 이름을 둘 다 받아 게임에서 쓸
        /// 단일 한글 이름 문자열로 바꾼다. ko가 없으면 en으로, 그것도 없으면 빈
        /// 문자열을 돌려준다(호출부에서 빈 이름은 건너뛴다).</summary>
        private static string ParseRosterName(object raw)
        {
            if (raw is Dictionary<string, object> dict)
            {
                string ko = GetString(dict, "ko", "");
                return !string.IsNullOrEmpty(ko) ? ko : GetString(dict, "en", "");
            }
            if (raw is string s)
            {
                return s;
            }
            return "";
        }

        private struct RosterSpeed
        {
            public float MoveInch;
            public float CoherencyInch;
            public bool CanMove;
        }

        /// <summary>신형 스키마("stat": {"spd": ...})와 구형 평탄화 필드(move_inch/
        /// coherency_inch)를 둘 다 받는다. spd가 null이면 그 유닛은 애초에 이동
        /// 스탯이 없는 것(수정탑 등)으로 보고 CanMove를 끈다.</summary>
        private static RosterSpeed ParseRosterSpeed(Dictionary<string, object> unitData)
        {
            if (unitData.TryGetValue("stat", out var statRaw) && statRaw is Dictionary<string, object> stat)
            {
                if (!stat.TryGetValue("spd", out var spdRaw) || spdRaw == null)
                {
                    return new RosterSpeed { MoveInch = 0f, CoherencyInch = 0f, CanMove = false };
                }
                if (spdRaw is Dictionary<string, object> spd)
                {
                    return new RosterSpeed
                    {
                        MoveInch = GetFloat(spd, "move", GameConstants.DefaultMoveInch),
                        CoherencyInch = GetFloat(spd, "cohesion", GameConstants.DefaultCoherencyInch),
                        CanMove = true,
                    };
                }
            }

            return new RosterSpeed
            {
                MoveInch = GetFloat(unitData, "move_inch", GameConstants.DefaultMoveInch),
                CoherencyInch = GetFloat(unitData, "coherency_inch", GameConstants.DefaultCoherencyInch),
                CanMove = true,
            };
        }

        private static List<RangeSpec> ParseRosterRanges(List<object> raw)
        {
            var result = new List<RangeSpec>();
            if (raw == null)
            {
                return result;
            }
            foreach (var item in raw)
            {
                if (!(item is Dictionary<string, object> r) || !r.ContainsKey("inch"))
                {
                    continue;
                }
                float inch = GetFloat(r, "inch", 0f);
                if (inch <= 0f)
                {
                    continue;
                }
                result.Add(new RangeSpec { Inch = inch, AlwaysShow = GetBool(r, "always_show", true) });
            }
            return result;
        }

        private static List<SupplyTier> ParseSupplyTiers(List<object> raw)
        {
            var result = new List<SupplyTier>();
            if (raw == null)
            {
                return result;
            }
            foreach (var item in raw)
            {
                if (!(item is Dictionary<string, object> t))
                {
                    continue;
                }
                result.Add(new SupplyTier
                {
                    ModelMin = (int)GetFloat(t, "model_min", 0f),
                    ModelMax = (int)GetFloat(t, "model_max", int.MaxValue),
                    Supply = (int)GetFloat(t, "supply", 0f),
                });
            }
            return result;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        }

        private static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) ? v as List<object> : null;
        }

        private static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is string s ? s : fallback;
        }

        private static float GetFloat(Dictionary<string, object> dict, string key, float fallback)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is bool b ? b : fallback;
        }
    }
}
