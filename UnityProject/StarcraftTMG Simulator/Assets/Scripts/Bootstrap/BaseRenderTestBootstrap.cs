using TmgBoard;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 임시 확인용 — BoardManager + Base + RadialMenu + InputDialog 전체 CRUD
/// 흐름(데미지/제거/복제/이름변경/메모)이 제대로 동작하는지 눈으로 확인하기
/// 위한 부트스트랩. 확인 끝나면 지운다(실제 게임 화면 구성은 별도로 만든다).
/// </summary>
public static class BaseRenderTestBootstrap
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

        var boardGo = new GameObject("BoardManager");
        var board = boardGo.AddComponent<BoardManager>();
        board.Configure(baseLayerRect, radialMenu, damageDialog, renameDialog, memoDialog, guideline);

        board.SpawnBase(new Vector2(32f, 32f), new Color(1f, 0.15f, 0.15f, 0.85f), "테스트 원형", "A", new Vector2(-150f, 50f));

        var ellipse = board.SpawnBase(new Vector2(80f, 50f), new Color(0.15f, 0.35f, 1f, 0.85f), "테스트 타원", "B", new Vector2(50f, 50f));
        ellipse.RotationDegrees = 45f;
        ellipse.Refresh();

        board.SpawnBase(new Vector2(60f, 60f), new Color(0.6f, 0.6f, 0.6f, 0.85f), "변위", "neutral", new Vector2(-50f, -100f), true);

        // 5모델 분대 — 리딩+팔로워 워크플로우 테스트용(이동 4", 코헤런시 3").
        board.SpawnUnit(new Vector2(25f, 25f), new Color(1f, 0.6f, 0.1f, 0.85f), "테스트 분대", "A", 5,
                new Vector2(0f, 200f), 4f, 3f);
    }
}
