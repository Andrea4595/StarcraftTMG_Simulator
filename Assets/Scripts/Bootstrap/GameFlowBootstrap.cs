using System.Collections.Generic;
using TmgBoard;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 실제 Unity 씬 전환에 맞춰 각 씬의 UI를 코드로 짓는 부트스트랩 — 이전
/// GameFlowTestBootstrap이 같은 씬 안에서 캔버스만 바꿔치기하던 임시 방식을
/// 대체한다. 씬 파일 자체는 카메라 하나만 있는 빈 씬이고(에디터에서 손으로
/// 만든 게 아니라 기존 SampleScene을 복사한 최소 구성), 실제 UI 구성은 전부
/// 여기서 한다 — 이 프로젝트의 "UI는 코드로 짓는다" 기존 방침을 그대로 유지.
///
/// 흐름(2026-08-30 재구성 — "배치/미션 셋업"이 "제작"으로 바뀌고, 실제 게임
/// 시작 흐름에서 빠지면서 완전히 선형이 됐다): Entry → Selection(배치/미션
/// 프리셋을 자유 순서로 고름, 둘 다 고르면 자동 진행) → TerrainSetup(그
/// 배치 프리셋을 참고로 지형만 매 게임 새로 배치) → GameBoard. MapAuthoring/
/// MissionAuthoring은 이 흐름과 무관하게 Entry 한쪽 구석 버튼으로만 들어가는
/// 별도 "프리셋 제작" 화면(완료하면 Entry로 돌아감) — 예전 GameFlowState의
/// "맵/미션 중 어느 쪽을 먼저 끝냈는지" 교차-씬 추적이 여기선 필요 없다
/// (Selection 하나가 양쪽을 다 처리하므로).
///
/// EventSystem은 씬을 넘나들며 살아있어야 하므로 DontDestroyOnLoad로 한 번만
/// 만든다. MapData/MissionSettingsData(Data/)는 static 클래스라 씬 전환과
/// 무관하게 그대로 유지된다 — 별도 전달 장치가 필요 없다.
/// </summary>
public static class GameFlowBootstrap
{
    private const string EntrySceneName = GameConstants.EntrySceneName;
    private const string MapAuthoringSceneName = GameConstants.MapAuthoringSceneName;
    private const string MissionAuthoringSceneName = GameConstants.MissionAuthoringSceneName;
    private const string SelectionSceneName = GameConstants.SelectionSceneName;
    private const string CardPrepSceneName = GameConstants.CardPrepSceneName;
    private const string CardDraftSceneName = GameConstants.CardDraftSceneName;
    private const string TerrainSetupSceneName = GameConstants.TerrainSetupSceneName;
    private const string LoadGameSceneName = GameConstants.LoadGameSceneName;
    private const string GameBoardSceneName = GameConstants.GameBoardSceneName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        EnsureEventSystem();
        EnsureNetworkManager();
        EnsureMultiplayerConnectDialog();
        EnsureDisconnectNotice();
        EnsureChatController();
        SceneManager.sceneLoaded += (scene, mode) => HandleSceneLoaded(scene);
        // sceneLoaded 이벤트는 앱 시작 시 최초로 로드된 씬에는 발생하지
        // 않으므로(Unity의 알려진 동작), 지금 이미 떠 있는 씬은 직접 처리한다.
        HandleSceneLoaded(SceneManager.GetActiveScene());
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<StandaloneInputModule>();
        Object.DontDestroyOnLoad(eventSystemGo);
    }

    /// <summary>NGO는 씬을 넘나들며 살아있는 단일 NetworkManager를 필요로
    /// 한다 — EventSystem과 같은 이유로 여기서 한 번만 만든다. 실제 전송은
    /// Relay를 거치므로 UnityTransport를 붙여둔다(MultiplayerConnectDialog가
    /// SetHostRelayData/SetClientRelayData로 접속 직전에 채운다).</summary>
    private static void EnsureNetworkManager()
    {
        if (Object.FindFirstObjectByType<NetworkManager>() != null)
        {
            return;
        }

        var networkManagerGo = new GameObject("NetworkManager");
        var transport = networkManagerGo.AddComponent<UnityTransport>();
        var networkManager = networkManagerGo.AddComponent<NetworkManager>();
        // AddComponent로 방금 만든 인스턴스는 NetworkConfig가 아직 비어있다
        // (씬/프리팹에서 역직렬화될 때만 자동으로 채워지는 필드) — 직접 만들어야 한다.
        if (networkManager.NetworkConfig == null)
        {
            networkManager.NetworkConfig = new NetworkConfig();
        }
        networkManager.NetworkConfig.NetworkTransport = transport;
        RegisterNetworkPrefabs(networkManager);
        Object.DontDestroyOnLoad(networkManagerGo);
    }

    /// <summary>BoardNetworkSync(RPC 창구, Assets/Resources/Multiplayer/)는
    /// 클라이언트에 복제되려면 스폰 시점에 이미 등록된 네트워크 프리팹이어야
    /// 한다 — DefaultNetworkPrefabs.asset(에디터 자동 등록) 목록에 기대지
    /// 않고 코드로 명시 등록한다. 마커(ActivationMarker 등)는 NetworkObject를
    /// 갖고 있지만(향후를 위해 남겨둠) 실제로는 NGO 스폰을 안 쓰므로(NGO가
    /// NetworkObject를 UI 마커 레이어 밑으로 재부모화하는 걸 막아서 —
    /// BoardNetworkSync.cs 참고) 여기 등록할 필요가 없다.</summary>
    private static void RegisterNetworkPrefabs(NetworkManager networkManager)
    {
        string[] networkResourcePaths =
        {
            "Multiplayer/BoardNetworkSync",
        };

        foreach (var path in networkResourcePaths)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[GameFlowBootstrap] 네트워크 프리팹을 못 찾음: {path}");
                continue;
            }
            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[GameFlowBootstrap] 네트워크 프리팹에 NetworkObject가 없음: {path}");
                continue;
            }
            networkManager.AddNetworkPrefab(prefab);
        }
    }

    /// <summary>상대방 이탈 알림(DisconnectNoticeController)은 CardPrep/
    /// CardDraft/TerrainSetup/GameBoard 어느 씬에서든 떠야 하므로 EventSystem/
    /// NetworkManager와 같은 방식으로 여기서 한 번만 만들고 DontDestroyOnLoad로
    /// 유지한다 — 자기 자신의 Canvas를 따로 둬서 현재 씬의 캔버스와 무관하게
    /// 뜬다. sortingOrder를 높게 잡아 항상 다른 씬 캔버스보다 위에 그려지게 한다.</summary>
    /// <summary>MultiplayerConnectDialog는 원래 Entry 화면에만 있었지만
    /// (2026-09-01), GameBoard 마커바의 "같이 하기" 버튼처럼 다른 씬에서도
    /// 열 수 있어야 해서(사용자 요청, 2026-09-02) EventSystem/NetworkManager와
    /// 같은 방식으로 여기서 한 번만 만들고 DontDestroyOnLoad + `Instance`
    /// 정적 참조로 어디서든 열 수 있게 승격했다. sortingOrder는 DisconnectNotice
    /// (100)보다 낮게 잡아 — 만약 언젠가 두 모달이 동시에 뜨는 경우가 생기면
    /// (지금은 안 일어남) 이탈 알림이 항상 위에 오도록.</summary>
    private static void EnsureMultiplayerConnectDialog()
    {
        if (Object.FindFirstObjectByType<MultiplayerConnectDialog>() != null)
        {
            return;
        }

        var canvasGo = new GameObject("MultiplayerConnectDialog_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasGo.AddComponent<MultiplayerConnectDialog>();
        Object.DontDestroyOnLoad(canvasGo);
    }

    private static void EnsureDisconnectNotice()
    {
        if (Object.FindFirstObjectByType<DisconnectNoticeController>() != null)
        {
            return;
        }

        var canvasGo = new GameObject("DisconnectNotice_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasGo.AddComponent<DisconnectNoticeController>();
        Object.DontDestroyOnLoad(canvasGo);
    }

    /// <summary>채팅(2026-09-04 신설) — 멀티가 진행되는 모든 화면(CardPrep/
    /// CardDraft/TerrainSetup/GameBoard)에서 다 떠야 해서(사용자 요청)
    /// MultiplayerConnectDialog/DisconnectNoticeController와 같은 방식으로
    /// 앱 시작 시 한 번만 만들고 DontDestroyOnLoad + Instance로 승격했다.
    /// sortingOrder는 그 둘(90/100)보다 낮게 잡는다 — 채팅/토스트는 화면
    /// 구석의 비독점 표시라 모달과 겹칠 일이 거의 없지만, 혹시 겹치면
    /// 모달이 항상 위에 오는 게 자연스럽다.</summary>
    private static void EnsureChatController()
    {
        if (Object.FindFirstObjectByType<ChatController>() != null)
        {
            return;
        }

        var canvasGo = new GameObject("Chat_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasGo.AddComponent<ChatController>();
        Object.DontDestroyOnLoad(canvasGo);
    }

    private static void HandleSceneLoaded(Scene scene)
    {
        // 지도(정사각형)가 화면(와이드) 비율에 안 맞아 남는 여백에 Unity 기본
        // 카메라 배경색(파란빛)이 그대로 비쳐 보이는 문제 — 씬마다 카메라가
        // 새로 생기므로 로드될 때마다 다시 맞춰준다.
        if (Camera.main != null)
        {
            Camera.main.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 1f);
        }

        switch (scene.name)
        {
            case EntrySceneName:
                BuildEntry();
                break;
            case MapAuthoringSceneName:
                BuildMapAuthoring();
                break;
            case MissionAuthoringSceneName:
                BuildMissionAuthoring();
                break;
            case SelectionSceneName:
                BuildSelection();
                break;
            case CardPrepSceneName:
                BuildCardPrep();
                break;
            case CardDraftSceneName:
                BuildCardDraft();
                break;
            case TerrainSetupSceneName:
                BuildTerrainSetup();
                break;
            case LoadGameSceneName:
                BuildLoadGame();
                break;
            case GameBoardSceneName:
                BuildGameBoard();
                break;
        }
    }

    /// <summary>화면 중앙 "혼자 하기"(→ Selection)/"같이 하기"(→
    /// MultiplayerConnectDialog) 큰 버튼 두 개 + 오른쪽 아래 구석의
    /// "배치/미션 프리셋 제작" 작은 버튼 두 개(→ 각 Authoring 화면,
    /// 완료하면 다시 Entry로).</summary>
    private static void BuildEntry()
    {
        var canvasGo = new GameObject("Entry_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var entry = canvasGo.AddComponent<EntryController>();
        // "혼자 하기"는 솔로 흐름(2026-08-31 재구성, 2026-09-01 이름 변경).
        // "같이 하기"는 MultiplayerConnectDialog를 연다 — 그 다이얼로그가
        // 호스트/클라이언트 연결이 완성되는 즉시(양쪽 다 연결됨) 자동으로
        // CardPrep으로 넘어간다(사용자 지정 — "호스트에게 연결되는 즉시").
        // 그 다이얼로그는 이제 EnsureMultiplayerConnectDialog()가 앱 시작 시
        // 한 번만 만든 영구 인스턴스라(2026-09-02) 여기서 새로 짓지 않고
        // Instance를 그대로 연결한다.
        entry.StartGamePicked += () => SceneManager.LoadScene(SelectionSceneName);
        entry.LoadGamePicked += () => SceneManager.LoadScene(LoadGameSceneName);
        entry.MapAuthoringPicked += () => SceneManager.LoadScene(MapAuthoringSceneName);
        entry.MissionAuthoringPicked += () => SceneManager.LoadScene(MissionAuthoringSceneName);
        entry.MultiplayerPicked += () => MultiplayerConnectDialog.Instance?.Open();
    }

    /// <summary>"이어하기" 화면 — Saves/ 폴더의 저장 파일 목록. 고르면 그
    /// 파싱된 내용을 GameLoadRequest에 담아두고 곧바로 GameBoard로 간다
    /// (Selection/TerrainSetup을 건너뛴다 — 저장 파일 자체가 지도/미션/
    /// 라이브 상태를 전부 담고 있다).</summary>
    private static void BuildLoadGame()
    {
        var canvasGo = new GameObject("LoadGame_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var loadGame = canvasGo.AddComponent<LoadGameController>();
        loadGame.SaveGamePicked += tree =>
        {
            GameLoadRequest.PendingData = tree;
            SceneManager.LoadScene(GameBoardSceneName);
        };
        loadGame.BackRequested += () => SceneManager.LoadScene(EntrySceneName);
    }

    /// <summary>배치 프리셋 제작 화면(예전 "맵 셋업") — 지도 크기/배치구역/
    /// 미션 목표. 지형은 없다(TerrainSetup으로 분리됨). "완료 ▶"는 그냥
    /// Entry로 돌아간다 — 프리셋으로 남기려면 화면 안의 "프리셋 저장"을
    /// 따로 눌러야 한다.</summary>
    private static void BuildMapAuthoring()
    {
        var canvasGo = new GameObject("MapAuthoring_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var mapAuthoring = canvasGo.AddComponent<MapAuthoringController>();
        mapAuthoring.BackRequested += () => SceneManager.LoadScene(EntrySceneName);
    }

    /// <summary>미션 프리셋 제작 화면(예전 "미션 셋업") — 미션 이름/파라미터/
    /// 점수 획득 조건/추가 조건/서플라이·라운드 공식/전투 규모. "완료 ▶"는
    /// 배치 제작 화면과 마찬가지로 그냥 Entry로 돌아간다.</summary>
    private static void BuildMissionAuthoring()
    {
        var canvasGo = new GameObject("MissionAuthoring_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var missionAuthoring = canvasGo.AddComponent<MissionAuthoringController>();
        missionAuthoring.BackRequested += () => SceneManager.LoadScene(EntrySceneName);
    }

    /// <summary>실제 게임 시작 흐름의 첫 단계 — 배치/미션 프리셋을 골라
    /// MapData/MissionSettingsData를 채운다. 둘 다 고르면 자동으로
    /// TerrainSetup으로 넘어간다.</summary>
    private static void BuildSelection()
    {
        var canvasGo = new GameObject("Selection_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var selection = canvasGo.AddComponent<SelectionController>();
        selection.BothPicked += () => SceneManager.LoadScene(TerrainSetupSceneName);
        selection.BackRequested += () => SceneManager.LoadScene(EntrySceneName);
    }

    /// <summary>멀티 전용 "카드 준비" 화면 — 각자 화면에서 배치 프리셋 2장 +
    /// 미션 프리셋 2장을 고른다. 양쪽 다 고르면(DraftState.BothReady) 각자
    /// 독립적으로 CardDraft로 넘어간다(CardPrepController 자신이 처리 —
    /// 여기선 뒤로가기만 배선한다).</summary>
    private static void BuildCardPrep()
    {
        var canvasGo = new GameObject("CardPrep_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var cardPrep = canvasGo.AddComponent<CardPrepController>();
        cardPrep.BackRequested += () => SceneManager.LoadScene(EntrySceneName);
    }

    /// <summary>멀티 전용 "카드 드래프트" 화면 — 양쪽 8장을 모아 보여주고
    /// 롤오프/자유 밴·픽. "다음"을 누르면 CardDraftController 자신이
    /// MapData/MissionSettingsData를 채우고 TerrainSetup으로 넘어간다.</summary>
    private static void BuildCardDraft()
    {
        var canvasGo = new GameObject("CardDraft_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        canvasGo.AddComponent<CardDraftController>();
    }

    /// <summary>Selection에서 고른 배치 프리셋을 참고로 지형만 매 게임 새로
    /// 배치한다. "게임 시작 ▶"을 누르면 MapData.TerrainPieces가 채워지고
    /// GameBoard로 넘어간다.</summary>
    private static void BuildTerrainSetup()
    {
        var canvasGo = new GameObject("TerrainSetup_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var terrainSetup = canvasGo.AddComponent<TerrainSetupController>();
        terrainSetup.Completed += () => SceneManager.LoadScene(GameBoardSceneName);
        terrainSetup.BackRequested += () => SceneManager.LoadScene(SelectionSceneName);
    }

    private static void BuildGameBoard()
    {
        // 저장된 게임을 불러오는 중이면(GameLoadRequest, LoadGame 화면에서
        // 세팅됨) — 아래에서 짓는 스코어보드/페이즈바 등이 전부 자기 build
        // 시점에 MapData/MissionSettingsData/MatchState/TeamColors를 읽으므로,
        // 반드시 그것들보다 먼저 채워야 한다(Selection→TerrainSetup 흐름이
        // MapData를 미리 채워두는 것과 완전히 같은 원리). 라이브 상태(유닛/
        // 마커/예비대/택티컬 카드)는 이 씬의 BoardManager 자신이 Start() 맨
        // 끝에서 마저 채운다(BoardManager.Load.cs) — 그건 baseLayer 등
        // BoardManager 자신의 Start()가 지어야 할 것들이 다 지어진 뒤라야
        // 안전해서 여기서 할 수 없다.
        if (GameLoadRequest.PendingData != null)
        {
            GameSaveIO.ApplyLoadedStaticState(GameLoadRequest.PendingData);
        }

        var canvasGo = new GameObject("Base_Test_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // MapArea: baseLayer/guideline/memoOverlay를 감싸는 단일 트랜스폼.
        // BoardManager가 패닝/줌 때 이 하나만 움직이고 스케일한다. 중앙점
        // 앵커/피벗(0.5,0.5)을 써서, 그 안을 꽉 채우는 baseLayer의 중심이
        // 곧 mapArea의 중심이 되게 한다 — Base 조각들의 anchoredPosition이
        // "지도 중심으로부터의 mm 오프셋"이라는 기존 의미가 그대로 유지된다.
        var mapAreaGo = new GameObject("MapArea", typeof(RectTransform));
        mapAreaGo.transform.SetParent(canvasGo.transform, false);
        var mapAreaRect = (RectTransform)mapAreaGo.transform;
        mapAreaRect.anchorMin = new Vector2(0.5f, 0.5f);
        mapAreaRect.anchorMax = new Vector2(0.5f, 0.5f);
        mapAreaRect.pivot = new Vector2(0.5f, 0.5f);

        // 지도 배경 — 플레이 가능 영역이 어디부터 어디까지인지 시각적으로
        // 보여준다(Godot판 _map_background와 동일한 색). mapArea의 맨 아래(첫
        // 자식)에 둬서 다른 모든 레이어보다 뒤에 깔린다. raycastTarget은 반드시
        // false로 둬야 한다 — true였으면 지도 전체를 덮는 이 배경이
        // IsPointerOverUi()에 "UI 위"로 잡혀서 배치/마커 클릭이 전부 막혔을 것이다.
        var mapBackground = new GameObject("MapBackground").AddComponent<Image>();
        mapBackground.color = new Color(0.15f, 0.18f, 0.15f, 1f);
        mapBackground.raycastTarget = false;
        mapBackground.transform.SetParent(mapAreaRect, false);
        var mapBackgroundRect = (RectTransform)mapBackground.transform;
        mapBackgroundRect.anchorMin = Vector2.zero;
        mapBackgroundRect.anchorMax = Vector2.one;
        mapBackgroundRect.offsetMin = Vector2.zero;
        mapBackgroundRect.offsetMax = Vector2.zero;

        // 지형 — 물리 없는 순수 시각 참고용(사용자와 합의된 방침)이라 지도
        // 배경 바로 위, 범위 표시/베이스 조각들보다는 아래에 깐다.
        var terrainLayerGo = new GameObject("TerrainLayer", typeof(RectTransform));
        terrainLayerGo.transform.SetParent(mapAreaRect, false);
        var terrainLayerRect = (RectTransform)terrainLayerGo.transform;
        terrainLayerRect.anchorMin = Vector2.zero;
        terrainLayerRect.anchorMax = Vector2.one;
        terrainLayerRect.offsetMin = Vector2.zero;
        terrainLayerRect.offsetMax = Vector2.zero;

        // 범위 표시(채우기+점선 테두리)는 지도보다는 앞에, 그러나 실제 베이스
        // 조각들보다는 뒤에 그려져야 한다 — mapArea 자식 중 BaseLayer보다
        // 먼저(=아래에) 추가한다. fill이 outline보다 더 아래(Godot판과 동일).
        var rangeFillLayer = new GameObject("RangeFillLayer").AddComponent<RangeOverlay>();
        rangeFillLayer.Mode = RangeOverlayMode.Fill;
        rangeFillLayer.transform.SetParent(mapAreaRect, false);
        var rangeFillRect = (RectTransform)rangeFillLayer.transform;
        rangeFillRect.anchorMin = Vector2.zero;
        rangeFillRect.anchorMax = Vector2.one;
        rangeFillRect.offsetMin = Vector2.zero;
        rangeFillRect.offsetMax = Vector2.zero;

        var rangeOutlineLayer = new GameObject("RangeOutlineLayer").AddComponent<RangeOverlay>();
        rangeOutlineLayer.Mode = RangeOverlayMode.Outline;
        rangeOutlineLayer.transform.SetParent(mapAreaRect, false);
        var rangeOutlineRect = (RectTransform)rangeOutlineLayer.transform;
        rangeOutlineRect.anchorMin = Vector2.zero;
        rangeOutlineRect.anchorMax = Vector2.one;
        rangeOutlineRect.offsetMin = Vector2.zero;
        rangeOutlineRect.offsetMax = Vector2.zero;

        var baseLayerGo = new GameObject("BaseLayer", typeof(RectTransform));
        baseLayerGo.transform.SetParent(mapAreaRect, false);
        var baseLayerRect = (RectTransform)baseLayerGo.transform;
        baseLayerRect.anchorMin = Vector2.zero;
        baseLayerRect.anchorMax = Vector2.one;
        baseLayerRect.offsetMin = Vector2.zero;
        baseLayerRect.offsetMax = Vector2.zero;

        // 마커(활성화/점령/아이콘) 레이어 — 베이스 조각들보다는 위, 가이드라인/
        // 메모/측정 오버레이보다는 아래(Godot판 marker_layer와 같은 순서).
        var markerLayerGo = new GameObject("MarkerLayer", typeof(RectTransform));
        markerLayerGo.transform.SetParent(mapAreaRect, false);
        var markerLayerRect = (RectTransform)markerLayerGo.transform;
        markerLayerRect.anchorMin = Vector2.zero;
        markerLayerRect.anchorMax = Vector2.one;
        markerLayerRect.offsetMin = Vector2.zero;
        markerLayerRect.offsetMax = Vector2.zero;

        var scoreboard = new GameObject("Scoreboard").AddComponent<ScoreboardPanel>();
        scoreboard.transform.SetParent(canvasGo.transform, false);

        var phaseBar = new GameObject("PhaseBar").AddComponent<PhaseBar>();
        phaseBar.transform.SetParent(canvasGo.transform, false);

        var radialMenu = new GameObject("RadialMenu").AddComponent<RadialMenu>();
        radialMenu.transform.SetParent(canvasGo.transform, false);

        var damageDialog = new GameObject("DamageDialog").AddComponent<InputDialog>();
        damageDialog.transform.SetParent(canvasGo.transform, false);
        var memoDialog = new GameObject("MemoDialog").AddComponent<InputDialog>();
        memoDialog.transform.SetParent(canvasGo.transform, false);
        var rangeInputDialog = new GameObject("RangeInputDialog").AddComponent<RangeInputDialog>();
        rangeInputDialog.transform.SetParent(canvasGo.transform, false);
        var rosterFileDialog = new GameObject("RosterFileDialog").AddComponent<RosterFileDialog>();
        rosterFileDialog.transform.SetParent(canvasGo.transform, false);
        var diceRollDialog = new GameObject("DiceRollDialog").AddComponent<DiceRollDialog>();
        diceRollDialog.transform.SetParent(canvasGo.transform, false);
        var rolloffDialog = new GameObject("RolloffDialog").AddComponent<RolloffDialog>();
        rolloffDialog.transform.SetParent(canvasGo.transform, false);
        var undoHistoryDialog = new GameObject("UndoHistoryDialog").AddComponent<UndoHistoryDialog>();
        undoHistoryDialog.transform.SetParent(canvasGo.transform, false);
        var weaponProfileDialog = new GameObject("WeaponProfileDialog").AddComponent<WeaponProfileDialog>();
        weaponProfileDialog.transform.SetParent(canvasGo.transform, false);
        var exitConfirmDialog = new GameObject("ExitConfirmDialog").AddComponent<ConfirmDialog>();
        exitConfirmDialog.transform.SetParent(canvasGo.transform, false);
        var missionInfoDialog = new GameObject("MissionInfoDialog").AddComponent<MissionInfoDialog>();
        missionInfoDialog.transform.SetParent(canvasGo.transform, false);
        var saveNameDialog = new GameObject("SaveNameDialog").AddComponent<InputDialog>();
        saveNameDialog.transform.SetParent(canvasGo.transform, false);
        var emotePickerPanel = new GameObject("EmotePickerPanel").AddComponent<EmotePickerPanel>();
        emotePickerPanel.transform.SetParent(canvasGo.transform, false);

        var guideline = new GameObject("Guideline").AddComponent<GuidelineOverlay>();
        guideline.transform.SetParent(mapAreaRect, false);
        var guidelineRect = (RectTransform)guideline.transform;
        guidelineRect.anchorMin = Vector2.zero;
        guidelineRect.anchorMax = Vector2.one;
        guidelineRect.offsetMin = Vector2.zero;
        guidelineRect.offsetMax = Vector2.zero;

        var memoOverlay = new GameObject("MemoOverlay").AddComponent<MemoOverlay>();
        memoOverlay.transform.SetParent(mapAreaRect, false);
        var memoOverlayRect = (RectTransform)memoOverlay.transform;
        memoOverlayRect.anchorMin = Vector2.zero;
        memoOverlayRect.anchorMax = Vector2.one;
        memoOverlayRect.offsetMin = Vector2.zero;
        memoOverlayRect.offsetMax = Vector2.zero;

        // 거리 재기(스페이스바) 선/라벨은 다른 map-space 레이어보다 위에
        // 그려져야 하므로 맨 마지막(가장 위)에 추가한다.
        var measureLayer = new GameObject("MeasureLayer").AddComponent<MeasureOverlay>();
        measureLayer.transform.SetParent(mapAreaRect, false);
        var measureLayerRect = (RectTransform)measureLayer.transform;
        measureLayerRect.anchorMin = Vector2.zero;
        measureLayerRect.anchorMax = Vector2.one;
        measureLayerRect.offsetMin = Vector2.zero;
        measureLayerRect.offsetMax = Vector2.zero;

        var boardGo = new GameObject("BoardManager");
        var board = boardGo.AddComponent<BoardManager>();
        board.Configure(baseLayerRect, mapAreaRect, radialMenu, damageDialog, memoDialog, guideline, memoOverlay,
                rangeInputDialog, rangeOutlineLayer, rangeFillLayer, measureLayer);

        // 마커는 Resources/Markers/*.prefab에서 불러온다 — 텍스처/크기/kind는
        // 프리팹에 이미 채워져 있으므로 여기선 인스턴스화용 참조만 가져온다.
        var iconMarkerPrefabs = new[]
        {
            Resources.Load<IconMarker>("Markers/IconMarker_Movement"),
            Resources.Load<IconMarker>("Markers/IconMarker_Assault"),
            Resources.Load<IconMarker>("Markers/IconMarker_Combat"),
            Resources.Load<IconMarker>("Markers/IconMarker_Buff"),
            Resources.Load<IconMarker>("Markers/IconMarker_Debuff"),
        };
        board.ConfigureMarkers(markerLayerRect,
                Resources.Load<ActivationMarker>("Markers/ActivationMarker"),
                Resources.Load<CaptureMarker>("Markers/CaptureMarker"),
                iconMarkerPrefabs);
        board.ConfigureRoster(rosterFileDialog);
        board.ConfigureTerrain(terrainLayerRect);
        board.ConfigureDiceRoll(diceRollDialog);
        board.ConfigureRolloff(rolloffDialog);
        board.ConfigureUndoHistory(undoHistoryDialog);
        board.ConfigureWeaponProfile(weaponProfileDialog);
        board.ConfigureExit(exitConfirmDialog);
        board.ConfigureSave(saveNameDialog);
        board.ConfigureEmote(emotePickerPanel);
        scoreboard.SetBoardManager(board);
        scoreboard.SetMissionInfoDialog(missionInfoDialog);

        if (MapData.HasData && GameConstants.MapSizePresets.TryGetValue(MapData.MapPreset, out var mapSize))
        {
            board.SetMapSizeMm(mapSize);
        }
    }
}
