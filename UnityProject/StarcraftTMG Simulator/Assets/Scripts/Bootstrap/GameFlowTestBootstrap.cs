using TmgBoard;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 임시 확인용 — 미션 설정 화면(지도 크기 선택 + 배치구역 그리기) → "게임
/// 시작" → 게임 보드(BoardManager) 핸드오프 전체 흐름을 눈으로 확인하기
/// 위한 부트스트랩. 확인 끝나면 지운다(실제 게임 화면 구성/씬 전환은 별도로
/// 만든다). 이전의 BaseRenderTestBootstrap을 대체한다 — 보드 단독 스모크
/// 테스트 내용은 BuildGameBoard()로 그대로 옮겨왔다.
///
/// Godot판은 별도 씬(mission_setup.tscn ↔ game_board.tscn) 전환이지만,
/// 여기선 아직 씬 관리가 없어서 같은 씬 안에서 미션 설정 캔버스를 지우고
/// 보드 캔버스를 새로 짓는 것으로 대신한다.
/// </summary>
public static class GameFlowTestBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();
        }

        BuildMissionSetup();
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
        missionSetup.StartGameRequested += () =>
        {
            Object.Destroy(canvasGo);
            BuildGameBoard();
        };
    }

    private static void BuildGameBoard()
    {
        var canvasGo = new GameObject("Base_Test_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var baseLayerGo = new GameObject("BaseLayer", typeof(RectTransform));
        baseLayerGo.transform.SetParent(canvasGo.transform, false);
        var baseLayerRect = (RectTransform)baseLayerGo.transform;
        baseLayerRect.anchorMin = Vector2.zero;
        baseLayerRect.anchorMax = Vector2.one;
        baseLayerRect.offsetMin = Vector2.zero;
        baseLayerRect.offsetMax = Vector2.zero;

        var radialMenu = new GameObject("RadialMenu").AddComponent<RadialMenu>();
        radialMenu.transform.SetParent(canvasGo.transform, false);

        var damageDialog = new GameObject("DamageDialog").AddComponent<InputDialog>();
        damageDialog.transform.SetParent(canvasGo.transform, false);
        var renameDialog = new GameObject("RenameDialog").AddComponent<InputDialog>();
        renameDialog.transform.SetParent(canvasGo.transform, false);
        var memoDialog = new GameObject("MemoDialog").AddComponent<InputDialog>();
        memoDialog.transform.SetParent(canvasGo.transform, false);

        var guideline = new GameObject("Guideline").AddComponent<GuidelineOverlay>();
        guideline.transform.SetParent(canvasGo.transform, false);
        var guidelineRect = (RectTransform)guideline.transform;
        guidelineRect.anchorMin = Vector2.zero;
        guidelineRect.anchorMax = Vector2.one;
        guidelineRect.offsetMin = Vector2.zero;
        guidelineRect.offsetMax = Vector2.zero;

        var memoOverlay = new GameObject("MemoOverlay").AddComponent<MemoOverlay>();
        memoOverlay.transform.SetParent(canvasGo.transform, false);
        var memoOverlayRect = (RectTransform)memoOverlay.transform;
        memoOverlayRect.anchorMin = Vector2.zero;
        memoOverlayRect.anchorMax = Vector2.one;
        memoOverlayRect.offsetMin = Vector2.zero;
        memoOverlayRect.offsetMax = Vector2.zero;

        var boardGo = new GameObject("BoardManager");
        var board = boardGo.AddComponent<BoardManager>();
        board.Configure(baseLayerRect, radialMenu, damageDialog, renameDialog, memoDialog, guideline, memoOverlay);
        if (MissionData.HasData && GameConstants.MapSizePresets.TryGetValue(MissionData.MapPreset, out var mapSize))
        {
            board.SetMapSizeMm(mapSize);
        }

        board.SpawnBase(new Vector2(32f, 32f), new Color(1f, 0.15f, 0.15f, 0.85f), "테스트 원형", "A", new Vector2(-150f, 50f));

        var ellipse = board.SpawnBase(new Vector2(80f, 50f), new Color(0.15f, 0.35f, 1f, 0.85f), "테스트 타원", "B", new Vector2(50f, 50f));
        ellipse.RotationDegrees = 45f;
        ellipse.Memo = "메모 호버 테스트 — 마우스를 올려보세요";
        ellipse.Refresh();

        board.SpawnBase(new Vector2(60f, 60f), new Color(0.6f, 0.6f, 0.6f, 0.85f), "변위", "neutral", new Vector2(-50f, -100f), true);

        // 5모델 분대 — 리딩+팔로워 워크플로우 테스트용(이동 4", 코헤런시 3").
        board.SpawnUnit(new Vector2(25f, 25f), new Color(1f, 0.6f, 0.1f, 0.85f), "테스트 분대", "A", 5,
                new Vector2(0f, 200f), 4f, 3f);

        // 예비대 목록 테스트 — 미션 설정에서 A팀 배치구역을 그렸다면, 예비대
        // 분대를 배치할 때 그 구역이 폴백 대신 실제 밴드로 보인다.
        board.AddPendingUnit(new PendingUnitDef
        {
            Name = "예비대 분대",
            Team = "A",
            ModelCount = 3,
            SizeMm = new Vector2(25f, 25f),
            FillColor = new Color(1f, 0.15f, 0.15f, 0.85f),
            MoveInch = 4f,
            CoherencyInch = 3f,
        });
        board.AddPendingUnit(new PendingUnitDef
        {
            Name = "예비대 단일",
            Team = "B",
            ModelCount = 1,
            SizeMm = new Vector2(32f, 32f),
            FillColor = new Color(0.15f, 0.35f, 1f, 0.85f),
            MoveInch = 6f,
        });
    }
}
