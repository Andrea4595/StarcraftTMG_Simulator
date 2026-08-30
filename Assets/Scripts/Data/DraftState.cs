using System;
using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>멀티플레이어 미션/배치 드래프트 흐름(CardPrep → CardDraft)의
    /// 핸드오프 저장소 — MapData/MissionSettingsData처럼 씬 전환 내내 유지되는
    /// static 상태다. CardPrep에서 각자 고른 배치 2장+미션 2장의 "원본 JSON
    /// 텍스트"를 담아둔다(상대 컴퓨터엔 그 프리셋 파일이 없을 수 있으므로,
    /// 로스터/유닛 동기화와 같은 이유로 파싱 결과가 아니라 원본 텍스트를
    /// 주고받는다 — BoardNetworkSync.RequestImportRoster 참고).
    ///
    /// 2026-08-31 재구성: 상대를 기다리지 않고 "준비 완료"를 누르는 즉시
    /// CardDraft로 넘어가도록 바뀌면서(사용자 지정), 상대 4장이 아직 도착하지
    /// 않았을 수 있는 상태에서도 화면을 지어야 한다 — 그래서 Pool의 각
    /// 카드는 IsPlaceholder 플래그를 가진다. 상대 데이터는 CardPrep에 있을
    /// 때 미리 도착할 수도, CardDraft에 있을 때 방송으로 도착할 수도 있는데
    /// (ApplyRemoteCardPrepJson이 이 두 경우를 대칭적으로 처리) 어느 쪽이든
    /// 카드 id(팀+카테고리+슬롯으로 결정적)는 항상 같으므로, 이미 만들어진
    /// DraftCard 객체를 그 자리에서 그대로 채워 넣는다(새로 만들어 바꿔치기
    /// 하지 않는다) — CardDraftController가 그 객체 참조를 클릭 클로저 등에
    /// 계속 들고 있어도 어긋나지 않는다.</summary>
    public static class DraftState
    {
        public struct PresetEntry
        {
            public string DisplayName;
            public string JsonText;
        }

        // 이 기기에서 고른 4장(배치 2 + 미션 2)의 원본 텍스트.
        public static readonly List<PresetEntry> LocalDeployment = new List<PresetEntry>();
        public static readonly List<PresetEntry> LocalMission = new List<PresetEntry>();
        public static bool LocalReady;

        // 상대에게서 방송받은 4장 — 아직 안 왔으면 비어있다.
        public static readonly List<PresetEntry> RemoteDeployment = new List<PresetEntry>();
        public static readonly List<PresetEntry> RemoteMission = new List<PresetEntry>();
        public static bool RemoteReady;

        /// <summary>CardDraft 화면에 표시되는 카드 한 장 — Id는 소유
        /// 팀+카테고리+슬롯번호로 결정적으로 정해지므로(예: "A_deployment_0")
        /// 네트워크로 id 자체를 따로 주고받을 필요가 없다.</summary>
        public class DraftCard
        {
            public string Id;
            public string Category; // "mission" 또는 "deployment"
            public string OwnerTeam;
            public string DisplayName;

            /// <summary>상대 카드 자리인데 아직 상대의 실제 데이터가 도착하지
            /// 않은 상태 — 화면에는 어두운 회색 자리표시자로만 보여야 하고,
            /// 좌/우클릭(선택/밴) 모두 무시해야 한다.</summary>
            public bool IsPlaceholder;

            // category == "deployment"일 때만 채워짐.
            public string MapPreset;
            public List<DeploymentZoneData> Zones;
            public List<MissionObjectiveData> Objectives;

            // category == "mission"일 때만 채워짐.
            public string MissionParameters;
            public string ScoringConditions;
            public string AdditionalConditions;
            public int BaseSupply;
            public int SupplyPerRound;
            public int RoundLength;
            public string EngagementScale;
        }

        public static readonly List<DraftCard> Pool = new List<DraftCard>();
        public static readonly HashSet<string> BannedIds = new HashSet<string>();
        public static string SelectedMissionId;
        public static string SelectedDeploymentId;

        public static void Clear()
        {
            LocalDeployment.Clear();
            LocalMission.Clear();
            LocalReady = false;
            RemoteDeployment.Clear();
            RemoteMission.Clear();
            RemoteReady = false;
            Pool.Clear();
            BannedIds.Clear();
            SelectedMissionId = null;
            SelectedDeploymentId = null;
        }

        /// <summary>CardPrep에서 "준비 완료"를 누르는 즉시(상대를 기다리지
        /// 않고) 부른다 — 내 4장은 바로 실제 카드로 채우고, 상대 4장 자리는
        /// 이미 RemoteReady라면(상대가 먼저 끝내서 데이터가 벌써 와있었다면)
        /// 마찬가지로 바로 실제 카드로, 아직이면 자리표시자로 채운다.
        ///
        /// 팀 A를 항상 먼저, 팀 B를 항상 나중에 추가한다(2026-08-31 수정 —
        /// 사용자 지정: 호스트든 클라이언트든 상관없이 화면에서 항상 A가
        /// 왼쪽, B가 오른쪽에 보여야 한다). 이전엔 "내 카드 먼저"였는데,
        /// 그러면 클라이언트(B) 화면에서는 B가 왼쪽에 보여 호스트(A) 화면과
        /// 좌우가 뒤집혀 보이는 문제가 있었다.</summary>
        public static void BuildLocalPool()
        {
            Pool.Clear();
            string localTeam = NetworkTeam.LocalTeam();
            AddTeamCards(NetworkTeam.Host, isLocal: localTeam == NetworkTeam.Host);
            AddTeamCards(NetworkTeam.Client, isLocal: localTeam == NetworkTeam.Client);
        }

        private static void AddTeamCards(string team, bool isLocal)
        {
            var deployment = isLocal ? LocalDeployment : RemoteDeployment;
            var mission = isLocal ? LocalMission : RemoteMission;
            if (isLocal || RemoteReady)
            {
                AddRealCards(team, "deployment", deployment);
                AddRealCards(team, "mission", mission);
            }
            else
            {
                AddPlaceholderCards(team, "deployment");
                AddPlaceholderCards(team, "mission");
            }
        }

        private static void AddPlaceholderCards(string team, string category)
        {
            for (int i = 0; i < 2; i++)
            {
                Pool.Add(new DraftCard
                {
                    Id = $"{team}_{category}_{i}",
                    Category = category,
                    OwnerTeam = team,
                    DisplayName = "???",
                    IsPlaceholder = true,
                });
            }
        }

        private static void AddRealCards(string team, string category, List<PresetEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var card = new DraftCard { Id = $"{team}_{category}_{i}", Category = category, OwnerTeam = team };
                ApplyEntryToCard(card, category, entries[i]);
                Pool.Add(card);
            }
        }

        private static void ApplyEntryToCard(DraftCard card, string category, PresetEntry entry)
        {
            card.DisplayName = entry.DisplayName;
            card.IsPlaceholder = false;
            if (category == "deployment")
            {
                if (MapPresetIO.TryLoad(entry.JsonText, out var mapPreset, out var zones, out var objectives, out _, out _))
                {
                    card.MapPreset = mapPreset;
                    card.Zones = zones;
                    card.Objectives = objectives;
                }
            }
            else if (MissionSettingsPresetIO.TryLoad(entry.JsonText, out var missionName, out var missionParameters, out var scoringConditions,
                    out var additionalConditions, out var baseSupply, out var supplyPerRound, out var roundLength, out var engagementScale, out _))
            {
                if (!string.IsNullOrEmpty(missionName))
                {
                    card.DisplayName = missionName;
                }
                card.MissionParameters = missionParameters;
                card.ScoringConditions = scoringConditions;
                card.AdditionalConditions = additionalConditions;
                card.BaseSupply = baseSupply;
                card.SupplyPerRound = supplyPerRound;
                card.RoundLength = roundLength;
                card.EngagementScale = engagementScale;
            }
        }

        /// <summary>BoardNetworkSync가 상대 카드 데이터 방송을 완성했을 때
        /// (씬과 무관하게 항상) 부른다. senderTeam은 그 방송을 원래 보낸
        /// 쪽의 팀 — [Rpc(SendTo.ClientsAndHost)]는 보낸 쪽 자신에게도
        /// 루프백되므로(이 프로젝트의 다른 모든 방송 동기화와 동일), 이게
        /// 내가 방금 보낸 내 방송이 되돌아온 것이면(senderTeam == 내 팀)
        /// 무시해야 한다 — 안 그러면 이미 Local*에 정확히 들고 있는 내
        /// 데이터가 실수로 Remote*(상대 자리)에도 덮어써진다(실제로 겪은
        /// 버그 — 클라이언트 화면에서 상대 카드가 자기 카드와 똑같이 보임).
        ///
        /// 로컬이 아직 준비 완료를 안 눌렀으면(LocalReady==false, Pool이
        /// 아직 없거나 이번 판 것이 아님) 그냥 Remote* 목록/플래그만
        /// 기록해두고, 나중에 BuildLocalPool()이 그걸 직접 읽어간다. 로컬이
        /// 이미 CardDraft에 가 있으면(Pool에 자리표시자가 이미 만들어져
        /// 있으면) 그 자리표시자를 즉시 실제 카드로 채운다.</summary>
        public static bool ApplyRemoteCardPrepJson(string senderTeam, string wrapperJson)
        {
            if (senderTeam == NetworkTeam.LocalTeam())
            {
                return true; // 내가 보낸 방송의 루프백 — 이미 Local*에 있으므로 아무것도 안 함.
            }

            object parsed;
            try
            {
                parsed = MiniJson.Parse(wrapperJson);
            }
            catch (Exception)
            {
                return false;
            }
            if (!(parsed is Dictionary<string, object> root))
            {
                return false;
            }

            RemoteDeployment.Clear();
            RemoteMission.Clear();
            ReadWireList(root, "deployment", RemoteDeployment);
            ReadWireList(root, "mission", RemoteMission);
            RemoteReady = true;

            if (LocalReady)
            {
                FillPlaceholders(senderTeam, "deployment", RemoteDeployment);
                FillPlaceholders(senderTeam, "mission", RemoteMission);
            }
            return true;
        }

        private static void FillPlaceholders(string team, string category, List<PresetEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                string id = $"{team}_{category}_{i}";
                var card = FindCard(id);
                if (card == null)
                {
                    // 이론상 항상 이미 자리표시자로 있어야 하지만(BuildLocalPool이
                    // 미리 만들어둠), 없으면 방어적으로 새로 추가한다.
                    card = new DraftCard { Id = id, Category = category, OwnerTeam = team };
                    Pool.Add(card);
                }
                ApplyEntryToCard(card, category, entries[i]);
            }
        }

        private static void ReadWireList(Dictionary<string, object> root, string key, List<PresetEntry> into)
        {
            if (!root.TryGetValue(key, out var raw) || !(raw is List<object> list))
            {
                return;
            }
            foreach (var item in list)
            {
                if (item is Dictionary<string, object> entry)
                {
                    into.Add(new PresetEntry
                    {
                        DisplayName = entry.TryGetValue("name", out var n) ? n as string ?? "" : "",
                        JsonText = entry.TryGetValue("json", out var j) ? j as string ?? "" : "",
                    });
                }
            }
        }

        public static DraftCard FindCard(string id)
        {
            foreach (var card in Pool)
            {
                if (card.Id == id)
                {
                    return card;
                }
            }
            return null;
        }
    }
}
