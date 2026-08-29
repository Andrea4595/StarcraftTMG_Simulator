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
    /// 고른 쪽으로 붙는다. "squad_tiers"(남은 모델 수 구간별 서플라이
    /// 단계표, pts 포함)는 읽는다 — 스코어보드의 팀별 서플라이 표시와 유닛
    /// 상세 패널에 쓰인다. "squad_tier_index"(로스터 작성 시 고른 단계)는
    /// 게임 로직에는 안 쓴다(인게임 실제 서플라이는 항상 남은 모델 수로
    /// 그때그때 다시 찾는다) — 상세 패널 표시용으로만 읽는다.
    /// "tactical_cards"는 name/count만 읽는다(gas_cost/resource/slots/
    /// abilities는 무시) — 예비대 패널의 택티컬 카드 목록에 쓰인다.
    /// "supply_override"(int|null, 메딕류 능력의 서플라이 "대입"값 — 단계표에
    /// 더하는 게 아니라 통째로 대체)와 "specialists"({"en","ko"} 이름 객체
    /// 리스트)도 읽는다 — 어느 모델이 어느 전문가인지는 여기서 정하지 않고
    /// 배치 시점에 순서대로 배정한다(PendingUnitDef.Specialists 참고).
    /// 그 밖의 나머지 필드(unit_type/stat의 shld·eva·arm·hp·siz/tags/
    /// abilities 전체/specialists의 이중언어 원본)는 게임 로직에는 안
    /// 쓰지만 ParseUnitDetail()이 유닛 상세 패널 표시용으로 통째로
    /// 구조화해서 읽는다(PendingUnitDef.Detail 참고). "resource_label"은
    /// 여전히 완전히 무시한다.
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
                        SupplyOverride = GetNullableInt(unitData, "supply_override"),
                        Specialists = ParseSpecialists(GetList(unitData, "specialists")),
                        Detail = ParseUnitDetail(unitData),
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

        /// <summary>"specialists": [{"en":"AGG-12","ko":"AGG-12"}, ...] — 유닛
        /// "name"과 같은 {"en","ko"}(또는 구형 평문자열) 스키마라 ParseRosterName을
        /// 그대로 재사용한다(ko 우선, 없으면 en). 어느 모델이 어느 전문가인지는
        /// 여기서 정하지 않는다 — 리스트 순서 그대로 PendingUnitDef.Specialists에
        /// 담기고, 배치 시점에 시뮬레이터가 순서대로 배정한다.</summary>
        private static List<string> ParseSpecialists(List<object> raw)
        {
            var result = new List<string>();
            if (raw == null)
            {
                return result;
            }
            foreach (var item in raw)
            {
                string name = ParseRosterName(item);
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>유닛 상세 패널을 위해 이 유닛의 로스터 JSON에서 게임 로직이
        /// 안 쓰는 나머지 필드를 전부 구조화해서 읽는다. 실패해도(필드가 아예
        /// 없어도) 예외를 던지지 않고 빈 값으로 채운 RosterUnitDetail을
        /// 돌려준다 — 이 정보는 순수 표시용이라 못 읽어도 임포트 자체를
        /// 막을 이유가 없다.</summary>
        private static RosterUnitDetail ParseUnitDetail(Dictionary<string, object> unitData)
        {
            var (nameEn, nameKo) = ParseBilingualPair(unitData.TryGetValue("name", out var nameRaw) ? nameRaw : null);
            var stat = GetDict(unitData, "stat");
            return new RosterUnitDetail
            {
                NameEn = nameEn,
                NameKo = nameKo,
                UnitType = GetString(unitData, "unit_type", ""),
                Shield = GetDisplayString(stat, "shld"),
                Evasion = GetString(stat, "eva", ""),
                Armor = GetString(stat, "arm", ""),
                Hp = GetDisplayString(stat, "hp"),
                Size = GetDisplayString(stat, "siz"),
                Tags = ParseTags(GetList(unitData, "tags")),
                Abilities = ParseAbilities(GetList(unitData, "abilities")),
                SquadTierIndex = GetNullableInt(unitData, "squad_tier_index"),
                Specialists = ParseSpecialistEntries(GetList(unitData, "specialists")),
            };
        }

        private static List<RosterTag> ParseTags(List<object> raw)
        {
            var result = new List<RosterTag>();
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
                var (en, ko) = ParseBilingualPair(t.TryGetValue("name", out var n) ? n : null);
                if (!string.IsNullOrEmpty(en) || !string.IsNullOrEmpty(ko))
                {
                    result.Add(new RosterTag { NameEn = en, NameKo = ko });
                }
            }
            return result;
        }

        private static List<RosterSpecialistEntry> ParseSpecialistEntries(List<object> raw)
        {
            var result = new List<RosterSpecialistEntry>();
            if (raw == null)
            {
                return result;
            }
            foreach (var item in raw)
            {
                var (en, ko) = ParseBilingualPair(item);
                if (!string.IsNullOrEmpty(en) || !string.IsNullOrEmpty(ko))
                {
                    result.Add(new RosterSpecialistEntry { NameEn = en, NameKo = ko });
                }
            }
            return result;
        }

        /// <summary>"abilities" 배열 — kind가 "rule"이면 "rule"({"en","ko"}) +
        /// type/cost를, "weapon"이면 "stat"(사거리/명중/데미지/키워드 등)을
        /// 읽는다. 둘 다 없는 미확인 kind가 와도 이름/phase 정도는 남긴다.</summary>
        private static List<RosterAbilityEntry> ParseAbilities(List<object> raw)
        {
            var result = new List<RosterAbilityEntry>();
            if (raw == null)
            {
                return result;
            }
            foreach (var item in raw)
            {
                if (!(item is Dictionary<string, object> a))
                {
                    continue;
                }
                var (nameEn, nameKo) = ParseBilingualPair(a.TryGetValue("name", out var n) ? n : null);
                var entry = new RosterAbilityEntry
                {
                    Kind = GetString(a, "kind", ""),
                    Id = GetString(a, "id", ""),
                    NameEn = nameEn,
                    NameKo = nameKo,
                    Phase = GetString(a, "phase", ""),
                    IsUpgrade = GetBool(a, "is_upgrade", false),
                    Type = GetString(a, "type", ""),
                    Cost = (int)GetFloat(a, "cost", 0f),
                };
                if (a.TryGetValue("rule", out var ruleRaw))
                {
                    var (ruleEn, ruleKo) = ParseBilingualPair(ruleRaw);
                    entry.RuleEn = ruleEn;
                    entry.RuleKo = ruleKo;
                }
                if (a.TryGetValue("stat", out var statRaw) && statRaw is Dictionary<string, object> stat)
                {
                    entry.Weapon = ParseWeaponStat(stat);
                }
                result.Add(entry);
            }
            return result;
        }

        private static RosterWeaponStat ParseWeaponStat(Dictionary<string, object> stat)
        {
            var w = new RosterWeaponStat
            {
                Range = GetDisplayString(stat, "rng"),
                Target = ParseTraitName(stat.TryGetValue("tgt", out var tgtRaw) ? tgtRaw : null),
                Roa = GetDisplayString(stat, "roa"),
                Hit = GetString(stat, "hit", ""),
                SurgeDie = GetString(stat, "sDie", ""),
                Damage = GetDisplayString(stat, "dmg"),
            };
            if (GetList(stat, "surge") is List<object> surgeList)
            {
                foreach (var s in surgeList)
                {
                    var trait = ParseTraitName(s);
                    if (trait != null)
                    {
                        w.Surge.Add(trait);
                    }
                }
            }
            if (GetList(stat, "keyword") is List<object> kwList)
            {
                foreach (var kwRaw in kwList)
                {
                    if (!(kwRaw is Dictionary<string, object> kw))
                    {
                        continue;
                    }
                    var (nameEn, nameKo) = ParseBilingualPair(kw.TryGetValue("name", out var kn) ? kn : null);
                    string suffixEn = "", suffixKo = "";
                    if (kw.TryGetValue("suffix", out var sfx))
                    {
                        (suffixEn, suffixKo) = ParseBilingualPair(sfx);
                    }
                    w.Keywords.Add(new RosterKeyword { NameEn = nameEn, NameKo = nameKo, SuffixEn = suffixEn, SuffixKo = suffixKo });
                }
            }
            return w;
        }

        /// <summary>무기 stat의 "tgt"/"surge" 항목 — tags/keyword와 같은
        /// {"name":{"en","ko"}} 모양이라 그 "name" 값을 꺼내 RosterTag로
        /// 만든다. 예전 평문자열 스키마(구형 로스터 대비)와 "name" 래퍼 없이
        /// {"en","ko"}만 바로 온 경우도 관대하게 받아준다 — 정보가 아예 없으면
        /// null(호출부가 걸러서 목록에 안 넣는다).</summary>
        private static RosterTag ParseTraitName(object raw)
        {
            if (raw is string s)
            {
                return string.IsNullOrEmpty(s) ? null : new RosterTag { NameEn = s, NameKo = s };
            }
            if (raw is Dictionary<string, object> dict)
            {
                object nameRaw = dict.TryGetValue("name", out var n) ? n : raw;
                var (en, ko) = ParseBilingualPair(nameRaw);
                if (string.IsNullOrEmpty(en) && string.IsNullOrEmpty(ko))
                {
                    return null;
                }
                return new RosterTag { NameEn = en, NameKo = ko };
            }
            return null;
        }

        /// <summary>ParseRosterName처럼 하나만 고르지 않고 en/ko 둘 다 돌려준다 —
        /// 유닛 상세 패널은 "모든 정보"를 보여줘야 하므로 둘 다 필요하다.</summary>
        private static (string En, string Ko) ParseBilingualPair(object raw)
        {
            if (raw is Dictionary<string, object> dict)
            {
                return (GetString(dict, "en", ""), GetString(dict, "ko", ""));
            }
            if (raw is string s)
            {
                return (s, s);
            }
            return ("", "");
        }

        /// <summary>rng/roa/hp/siz/dmg처럼 로스터 JSON에서 숫자([12])와 문자열
        /// (["E"])이 섞여 나오는 필드를 표시용 문자열 하나로 통일한다. 정수면
        /// 소수점 없이, 아니면 소수 둘째 자리까지.</summary>
        private static string GetDisplayString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var v) || v == null)
            {
                return "";
            }
            if (v is double d)
            {
                float f = (float)d;
                return Mathf.Approximately(f, Mathf.Floor(f)) ? ((int)d).ToString() : d.ToString("0.##");
            }
            if (v is bool b)
            {
                return b ? "true" : "false";
            }
            return v is string s ? s : v.ToString();
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
                    Pts = (int)GetFloat(t, "pts", 0f),
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

        /// <summary>"supply_override"처럼 "값이 있으면 그 숫자, 없거나 null이면
        /// 그런 능력 자체가 없음"을 뜻하는 optional 정수 필드용 — GetFloat과
        /// 달리 fallback 없이 존재/부재 자체를 구분해야 하는 필드에 쓴다.</summary>
        private static int? GetNullableInt(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var v) && v is double d ? (int)d : (int?)null;
        }
    }
}
