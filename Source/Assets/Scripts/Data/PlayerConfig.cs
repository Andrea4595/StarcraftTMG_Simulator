using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>실행 파일 옆 config.json에 로컬 설정(현재는 닉네임 하나)을
    /// 저장/불러온다 — AppPaths.ExeDirectory() 기준, GameSaveIO와 같은
    /// MiniJson 트리 왕복 방식.</summary>
    public static class PlayerConfig
    {
        private static string ConfigPath => Path.Combine(AppPaths.ExeDirectory(), "config.json");

        public static string LoadNickname()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return "";
                }
                if (MiniJson.Parse(File.ReadAllText(ConfigPath)) is Dictionary<string, object> dict
                        && dict.TryGetValue("nickname", out var v) && v is string s)
                {
                    return s;
                }
            }
            catch (Exception)
            {
                // 손상된 설정 파일은 무시하고 빈 닉네임으로 시작한다.
            }
            return "";
        }

        public static void SaveNickname(string nickname)
        {
            try
            {
                var dict = new Dictionary<string, object> { ["nickname"] = nickname ?? "" };
                File.WriteAllText(ConfigPath, MiniJson.Write(dict));
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerConfig] 닉네임 저장 실패: {e.Message}");
            }
        }
    }
}
