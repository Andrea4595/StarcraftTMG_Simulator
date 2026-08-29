using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>미션 셋업 화면(텍스트 조건들 + 서플라이/라운드 공식 + 전투 규모)의
    /// 상태를 JSON 파일로 저장·불러온다 — MapPresetIO와 같은 이유로 외부 패키지
    /// 의존성 없이 손으로 최소 JSON을 쓴다(MiniJson.cs 재사용). 저장 위치는
    /// MapPresetIO의 Deployments/와 별개인 Missions/ 폴더.</summary>
    public static class MissionSettingsPresetIO
    {
        public static void Save(string path, string missionParameters, string scoringConditions,
                string additionalConditions, int baseSupply, int supplyPerRound, int roundLength, string engagementScale)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"mission_parameters\": \"{Escape(missionParameters)}\",\n");
            sb.Append($"  \"scoring_conditions\": \"{Escape(scoringConditions)}\",\n");
            sb.Append($"  \"additional_conditions\": \"{Escape(additionalConditions)}\",\n");
            sb.Append($"  \"base_supply\": {baseSupply},\n");
            sb.Append($"  \"supply_per_round\": {supplyPerRound},\n");
            sb.Append($"  \"round_length\": {roundLength},\n");
            sb.Append($"  \"engagement_scale\": \"{Escape(engagementScale)}\"\n");
            sb.Append("}\n");

            File.WriteAllText(path, sb.ToString());
        }

        public static bool TryLoad(string jsonText, out string missionParameters, out string scoringConditions,
                out string additionalConditions, out int baseSupply, out int supplyPerRound, out int roundLength,
                out string engagementScale, out string error)
        {
            missionParameters = "";
            scoringConditions = "";
            additionalConditions = "";
            baseSupply = 0;
            supplyPerRound = 0;
            roundLength = 5;
            engagementScale = MissionSettingsData.EngagementScaleStandard;
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

            if (!(parsed is Dictionary<string, object> root))
            {
                error = "미션 프리셋 파일 형식이 올바르지 않습니다.";
                return false;
            }

            missionParameters = GetString(root, "mission_parameters");
            scoringConditions = GetString(root, "scoring_conditions");
            additionalConditions = GetString(root, "additional_conditions");
            baseSupply = GetInt(root, "base_supply");
            supplyPerRound = GetInt(root, "supply_per_round");
            roundLength = GetInt(root, "round_length", 5);
            string scale = GetString(root, "engagement_scale");
            engagementScale = scale == MissionSettingsData.EngagementScaleSkirmish
                    ? MissionSettingsData.EngagementScaleSkirmish
                    : MissionSettingsData.EngagementScaleStandard;

            return true;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v is string s ? s : "";
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int fallback = 0)
        {
            return dict.TryGetValue(key, out var v) && v is double d ? (int)d : fallback;
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
