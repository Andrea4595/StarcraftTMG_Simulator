using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>미션 설정 화면의 배치구역/지형/미션 목표 배치를 JSON 파일로
    /// 저장·불러온다 — MissionData와 같은 스키마(코너 원점 mm)를 그대로 쓴다.
    /// 로스터 JSON은 외부(로스터 앱)가 만드는 포맷이라 읽기만 하면 됐지만,
    /// 이건 이 프로젝트가 직접 쓰고 읽는 포맷이라 쓰기도 필요하다 — 여기서도
    /// Newtonsoft 같은 패키지 의존성을 새로 만들지 않는다는 기존 방침(로스터
    /// 임포트의 MiniJson.cs 참고)을 그대로 따라 손으로 최소 JSON을 쓴다.</summary>
    public static class MissionPresetIO
    {
        public static void Save(string path, string mapPreset,
                List<DeploymentZoneData> zones, List<MissionObjectiveData> objectives, List<TerrainPieceData> terrain)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"map_preset\": \"{Escape(mapPreset)}\",\n");

            sb.Append("  \"deployment_zones\": [\n");
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                sb.Append("    {\"edge\": \"").Append(Escape(z.Edge))
                        .Append("\", \"player\": \"").Append(Escape(z.Player))
                        .Append("\", \"start_along\": ").Append(F(z.StartAlong))
                        .Append(", \"end_along\": ").Append(F(z.EndAlong))
                        .Append(i < zones.Count - 1 ? "},\n" : "}\n");
            }
            sb.Append("  ],\n");

            sb.Append("  \"mission_objectives\": [\n");
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                sb.Append("    {\"number\": ").Append(o.Number)
                        .Append(", \"x\": ").Append(F(o.Position.x))
                        .Append(", \"y\": ").Append(F(o.Position.y))
                        .Append(i < objectives.Count - 1 ? "},\n" : "}\n");
            }
            sb.Append("  ],\n");

            sb.Append("  \"terrain_pieces\": [\n");
            for (int i = 0; i < terrain.Count; i++)
            {
                var t = terrain[i];
                sb.Append("    {\"module_id\": \"").Append(Escape(t.ModuleId))
                        .Append("\", \"x\": ").Append(F(t.Position.x))
                        .Append(", \"y\": ").Append(F(t.Position.y))
                        .Append(", \"rotation_deg\": ").Append(F(t.RotationDeg))
                        .Append(i < terrain.Count - 1 ? "},\n" : "}\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");

            File.WriteAllText(path, sb.ToString());
        }

        public static bool TryLoad(string jsonText, out string mapPreset,
                out List<DeploymentZoneData> zones, out List<MissionObjectiveData> objectives, out List<TerrainPieceData> terrain,
                out string error)
        {
            zones = new List<DeploymentZoneData>();
            objectives = new List<MissionObjectiveData>();
            terrain = new List<TerrainPieceData>();
            mapPreset = GameConstants.DefaultMapSizePreset;
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
                error = "프리셋 파일 형식이 올바르지 않습니다.";
                return false;
            }

            if (root.TryGetValue("map_preset", out var mp) && mp is string mpStr && GameConstants.MapSizePresets.ContainsKey(mpStr))
            {
                mapPreset = mpStr;
            }

            if (root.TryGetValue("deployment_zones", out var zonesRaw) && zonesRaw is List<object> zoneList)
            {
                foreach (var raw in zoneList)
                {
                    if (!(raw is Dictionary<string, object> z))
                    {
                        continue;
                    }
                    zones.Add(new DeploymentZoneData
                    {
                        Edge = GetString(z, "edge"),
                        Player = GetString(z, "player"),
                        StartAlong = GetFloat(z, "start_along"),
                        EndAlong = GetFloat(z, "end_along"),
                    });
                }
            }

            if (root.TryGetValue("mission_objectives", out var objRaw) && objRaw is List<object> objList)
            {
                foreach (var raw in objList)
                {
                    if (!(raw is Dictionary<string, object> o))
                    {
                        continue;
                    }
                    objectives.Add(new MissionObjectiveData
                    {
                        Number = (int)GetFloat(o, "number"),
                        Position = new Vector2(GetFloat(o, "x"), GetFloat(o, "y")),
                    });
                }
            }

            if (root.TryGetValue("terrain_pieces", out var terrainRaw) && terrainRaw is List<object> terrainList)
            {
                foreach (var raw in terrainList)
                {
                    if (!(raw is Dictionary<string, object> t))
                    {
                        continue;
                    }
                    terrain.Add(new TerrainPieceData
                    {
                        ModuleId = GetString(t, "module_id"),
                        Position = new Vector2(GetFloat(t, "x"), GetFloat(t, "y")),
                        RotationDeg = GetFloat(t, "rotation_deg"),
                    });
                }
            }

            return true;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v is string s ? s : "";
        }

        private static float GetFloat(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var v) && v is double d ? (float)d : 0f;
        }

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
