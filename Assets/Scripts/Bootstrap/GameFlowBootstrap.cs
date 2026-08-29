using System.Collections.Generic;
using TmgBoard;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 실제 Unity 씬 전환(MissionSetup 씬 ↔ GameBoard 씬)에 맞춰 각 씬의 UI를
/// 코드로 짓는 부트스트랩 — 이전 GameFlowTestBootstrap이 같은 씬 안에서
/// 캔버스만 바꿔치기하던 임시 방식을 대체한다. 씬 파일 자체는 카메라 하나만
/// 있는 빈 씬이고(에디터에서 손으로 만든 게 아니라 기존 SampleScene을
/// 복사한 최소 구성), 실제 UI 구성은 전부 여기서 한다 — 이 프로젝트의
/// "UI는 코드로 짓는다" 기존 방침을 그대로 유지.
///
/// EventSystem은 씬을 넘나들며 살아있어야 하므로 DontDestroyOnLoad로 한 번만
/// 만든다. MissionData(Data/MissionData.cs)는 static 클래스라 씬 전환과
/// 무관하게 그대로 유지된다 — 별도 전달 장치가 필요 없다.
/// </summary>
public static class GameFlowBootstrap
{
    private const string MissionSetupSceneName = GameConstants.MissionSetupSceneName;
    private const string GameBoardSceneName = GameConstants.GameBoardSceneName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        EnsureEventSystem();
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
            case MissionSetupSceneName:
                BuildMissionSetup();
                break;
            case GameBoardSceneName:
                BuildGameBoard();
                break;
        }
    }

    private static void BuildMissionSetup()
    {
        var canvasGo = new GameObject("MissionSetup_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // MissionSetupController는 자기 GameObject의 RectTransform(캔버스 직속,
        // 화면 전체를 덮음)을 직접 기준으로 UI를 짓는다 — 별도 참조 주입이 없다.
        var missionSetup = canvasGo.AddComponent<MissionSetupController>();
        missionSetup.StartGameRequested += () => SceneManager.LoadScene(GameBoardSceneName);
    }

    private static void BuildGameBoard()
    {
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
        var weaponProfileDialog = new GameObject("WeaponProfileDialog").AddComponent<WeaponProfileDialog>();
        weaponProfileDialog.transform.SetParent(canvasGo.transform, false);
        var exitConfirmDialog = new GameObject("ExitConfirmDialog").AddComponent<ConfirmDialog>();
        exitConfirmDialog.transform.SetParent(canvasGo.transform, false);

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

        // 마커 텍스처는 Resources/Tokens/*.png(Godot의 Tokens/ 폴더에서 그대로
        // 복사)에서 불러온다 — Resources.Load는 임포트 텍스처 타입(Sprite냐
        // Default냐)을 안 가려서, 별도 임포트 설정을 손대지 않아도 된다.
        var iconTextures = new Dictionary<string, Texture2D>
        {
            { "movement", Resources.Load<Texture2D>("Tokens/movement") },
            { "assault", Resources.Load<Texture2D>("Tokens/assault") },
            { "combat", Resources.Load<Texture2D>("Tokens/combat") },
            { "buff", Resources.Load<Texture2D>("Tokens/buff") },
            { "debuff", Resources.Load<Texture2D>("Tokens/debuff") },
        };
        board.ConfigureMarkers(markerLayerRect,
                Resources.Load<Texture2D>("Tokens/activated-movement"),
                Resources.Load<Texture2D>("Tokens/activated-assault"),
                Resources.Load<Texture2D>("Tokens/activated-done"),
                Resources.Load<Texture2D>("Tokens/flag"),
                iconTextures);
        board.ConfigureRoster(rosterFileDialog);
        board.ConfigureTerrain(terrainLayerRect);
        board.ConfigureDiceRoll(diceRollDialog);
        board.ConfigureWeaponProfile(weaponProfileDialog);
        board.ConfigureExit(exitConfirmDialog);
        scoreboard.SetBoardManager(board);

        if (MissionData.HasData && GameConstants.MapSizePresets.TryGetValue(MissionData.MapPreset, out var mapSize))
        {
            board.SetMapSizeMm(mapSize);
        }
    }
}
