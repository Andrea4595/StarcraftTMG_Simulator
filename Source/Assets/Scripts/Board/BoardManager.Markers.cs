using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 마커(활성화/점령/아이콘) ───────────────────────────────────

        // 멀티플레이어 중 배치된 마커만 등록됨(id는 BoardNetworkSync가
        // 발급) — 삭제/이동 방송이 도착했을 때 어느 GameObject인지 찾는 용도.
        // 필드 자체는 BoardManager.cs의 _networkedMarkers(NetworkIdentityRegistry)로
        // 옮겼다(2026-09-02, 리팩토링 Phase 1) — Load.cs/UndoRedo.cs/
        // MidGameHandoff.cs도 직접 쓰던 사실상 전역 상태였기 때문.

        /// <summary>markerLayer 아래 실제 마커 GameObject를 전부 순회한다 — 배치
        /// 미리보기(고스트)는 항상 제외. Save.cs(BuildMarkersTree)/UndoRedo.cs
        /// (CaptureBoardSnapshot)/MidGameHandoff.cs(BackfillMarkerNetworkIds)가
        /// 각자 markerLayer.childCount를 훑으며 이 고스트 제외 체크를 복붙하던
        /// 걸 여기로 모았다(2026-09-02, 리팩토링 Phase 2). UndoRedo.cs의
        /// ClearLiveBoardState는 대상이 아니다 — 그쪽은 자식을 그 자리에서
        /// DestroyImmediate하며 역순으로 도는 파괴 전용 루프라 이 정방향
        /// 읽기 전용 순회와 성격이 달라 그대로 뒀다.</summary>
        private IEnumerable<GameObject> EnumerateRealMarkers()
        {
            if (markerLayer == null)
            {
                yield break;
            }
            for (int i = 0; i < markerLayer.childCount; i++)
            {
                var markerGo = markerLayer.GetChild(i).gameObject;
                if (_markerPlacementPreview != null && markerGo == _markerPlacementPreview.gameObject)
                {
                    continue;
                }
                yield return markerGo;
            }
        }

        // 활성화/점령 마커 상태 순환의 연속 클릭 합치기(Composite, 2026-09-04
        // 추가, 사용자 요청) 용 — 마커별 "그 연속 편집이 시작되기 전" 상태를
        // 기억해둔다. NetworkMarkerId는 솔로 플레이 중엔 모든 마커가 -1로
        // 겹치므로 쓸 수 없어(ScoreboardPanel._roundStreakBase 등과 달리
        // 팀/키가 아니라 마커 인스턴스 자체를 구분해야 함), 대신 마커
        // GameObject의 Unity 인스턴스 id(GetInstanceID, 이 세션 동안은 마커가
        // 파괴/재생성되지 않는 한 안정적)를 키로 쓴다 — 활성화/점령 마커
        // 공용(인스턴스 id는 전역에서 겹치지 않으므로 하나로 충분).
        private readonly Dictionary<int, string> _markerStateStreakBase = new();

        // 드래그를 끝낸 쪽이 아닌 다른 클라이언트가 이동 방송을 받았을 때,
        // 순간이동 대신 부드럽게 그 자리로 움직이게 하는 진행 중 트윈 목록.
        private const float MarkerMoveTweenDuration = 0.2f;
        private struct MarkerMoveTween
        {
            public MarkerBase Marker;
            public Vector2 From;
            public Vector2 To;
            public float StartTime;
        }
        private readonly List<MarkerMoveTween> _markerMoveTweens = new();

        /// <summary>되돌리기 조작 리스트에 보여줄 마커 종류 이름 — MarkerBarEntries에
        /// 이미 있는 한글 이름을 그대로 재사용한다.</summary>
        private static string GetMarkerKindLabel(string kind)
        {
            foreach (var entry in MarkerBarEntries)
            {
                if (entry.Kind == kind)
                {
                    return entry.Label;
                }
            }
            return "마커";
        }

        /// <summary>구체 타입(ActivationMarker/CaptureMarker/IconMarker)만 보고
        /// 종류 이름을 알아낸다 — MarkerBase 자체엔 종류 문자열이 없다.</summary>
        private static string DescribeMarkerKind(MarkerBase marker)
        {
            switch (marker)
            {
                case ActivationMarker:
                    return GetMarkerKindLabel("activation");
                case CaptureMarker:
                    return GetMarkerKindLabel("capture");
                case IconMarker icon:
                    return GetMarkerKindLabel(icon.Kind);
                default:
                    return "마커";
            }
        }

        /// <summary>ActivationMarker.State 원시값("movement"/"assault"/"done")을
        /// 되돌리기 라벨에 쓸 한글 표시로 바꾼다(사용자 요청, 2026-09-04 —
        /// "서클형 조작기들도 숫자 조작기처럼 {초기값} -> {최종값} 형태로
        /// 기록해줘"). BoardManager.MissionObjectives.DescribeRingColorState와
        /// 같은 자리의 헬퍼.</summary>
        private static string DescribeActivationState(string state)
        {
            switch (state)
            {
                case "movement":
                    return "이동";
                case "assault":
                    return "돌격";
                case "done":
                    return "완료";
                default:
                    return state;
            }
        }

        /// <summary>CaptureMarker.ColorState 원시값("white"/"red"/"blue")을
        /// 되돌리기 라벨에 쓸 한글 표시로 바꾼다(사용자 요청, 2026-09-04) —
        /// red/blue는 색 이름이 아니라 A/B팀 점령을 뜻한다(CaptureMarker.
        /// ResolveColor 참고). 미션 목표 마커(BoardManager.MissionObjectives.
        /// DescribeRingColorState)와 달리 "inactive" 상태가 없어 white를
        /// "활성"이 아니라 "미점령"으로 표시한다 — 이 마커 자체가 항상
        /// 활성 상태이고(비활성 개념이 없음) white는 그냥 아직 어느 팀도
        /// 점령하지 않은 상태를 뜻하기 때문.</summary>
        private static string DescribeCaptureColorState(string state)
        {
            switch (state)
            {
                case "white":
                    return "미점령";
                case "red":
                    return "A 점령";
                case "blue":
                    return "B 점령";
                default:
                    return state;
            }
        }

        /// <summary>리플레이 모드 진입 시 이 바 전체를 숨기고 별도의 항상-켜진
        /// 캔버스에 나가기/GIF 버튼만 새로 짓는다(BoardManager.Replay.cs) —
        /// 이 바는 메인 캔버스(EnterReplayMode가 끄는 그 라캐스터) 아래에
        /// 있어서, 라캐스터를 끈 뒤에는 여기 새 버튼을 지어도 클릭이 전혀
        /// 안 먹는다(라캐스터 비활성은 캔버스 전체의 입력 라우팅을 막지,
        /// 자식이 나중에 생겼는지는 상관없다) — 그래서 자식만 지우고
        /// 다시 짓는 게 아니라 이 바 자체를 숨기는 방식을 쓴다.</summary>
        private RectTransform _markerBarRect;

        /// <summary>화면 맨 아래를 가로지르는 바 — 마커 종류별 버튼은 중앙에
        /// 모아두고, 스크린샷 버튼은 우측 하단에 고정한다. 누르면
        /// StartMarkerPlacement()로 배치 모드에 들어간다. Godot판
        /// _build_marker_bar() 포팅이지만 배치는 새로 짰다(원래는 화면 하단
        /// 중앙에 붕 뜬 작은 패널이었다 — 스코어보드 바와 같은 "화면 끝까지
        /// 가로지르는 바 + 그 안에 독립적으로 앵커된 구역들" 방식으로
        /// 바꿨다).</summary>
        private void BuildMarkerBar()
        {
            var canvasParent = GetCanvasParent();

            var barGo = new GameObject("MarkerBar", typeof(RectTransform));
            barGo.transform.SetParent(canvasParent, false);
            var barRect = (RectTransform)barGo.transform;
            _markerBarRect = barRect;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, GameConstants.MarkerBarHeight);

            var bg = barGo.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);

            // 마커 아이콘들 — 화면 정중앙에 모아둔다(예전과 같은 자리).
            var iconsGo = new GameObject("MarkerIcons", typeof(RectTransform));
            iconsGo.transform.SetParent(barRect, false);
            var iconsRect = (RectTransform)iconsGo.transform;
            iconsRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconsRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconsRect.pivot = new Vector2(0.5f, 0.5f);
            iconsRect.anchoredPosition = Vector2.zero;

            var layout = iconsGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = iconsGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var entry in MarkerBarEntries)
            {
                Texture icon = entry.Kind switch
                {
                    "activation" => _activationMarkerPrefab != null ? _activationMarkerPrefab.texture : null,
                    "capture" => _captureMarkerPrefab != null ? _captureMarkerPrefab.texture : null,
                    // 블라스트 템플릿은 프리팹 없이 코드로 짓는 마커라(아래
                    // CreateBlastTemplateMarker) 마커바 아이콘도 여기서 직접
                    // 텍스처를 불러온다 — 실제 배치되는 도형(BlastTemplate)과는
                    // 다른, 버튼 전용 축소판(BlastTemplateButton)을 쓴다.
                    "blast" => Resources.Load<Texture2D>("UI/BlastTemplateButton"),
                    _ => _iconMarkerPrefabsByKind != null && _iconMarkerPrefabsByKind.TryGetValue(entry.Kind, out var p) ? p.texture : null,
                };
                CreateMarkerBarButton(iconsRect, entry.Kind, icon);
            }

            CreateMarkerHintLabel(barRect);
            CreateScreenshotButton(barRect);
            CreateFitButton(barRect);
            CreateDiceButton(barRect);
            CreateRolloffButton(barRect);
            CreateUndoHistoryButton(barRect);
            CreateExitButton(barRect);
            CreateSaveButton(barRect);
            CreateMultiplayerButton(barRect);
        }

        /// <summary>바 맨 왼쪽 — 처음 화면(Entry)으로 돌아가기(사용자 요청).
        /// 클릭하면 바로 나가지 않고 확인 창을 먼저 띄운다(exitConfirmDialog,
        /// ConfigureExit로 주입됨) — 확인하면 진행 중이던 판을 버리고
        /// 처음 화면으로 돌아간다(BoardManager.cs의 OnExitConfirmed).</summary>
        private void CreateExitButton(Transform parent)
        {
            var go = new GameObject("ExitButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/ExitButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (exitConfirmDialog != null)
                {
                    exitConfirmDialog.Open("진행 중인 게임을 종료하고\n처음 화면으로 돌아갈까요?\n(현재 진행 상황은 저장되지 않습니다)");
                }
            });
        }

        /// <summary>"나가기" 버튼 바로 오른쪽 — 진행 중인 게임을 이름을 물어본
        /// 뒤 Saves/ 폴더에 저장한다(사용자 요청, BoardManager.Save.cs의
        /// SaveGame). 프리셋 저장(SaveButton.png)과 같은 아이콘을 재사용해
        /// "저장"이라는 시각 언어를 통일했다.</summary>
        private void CreateSaveButton(Transform parent)
        {
            var go = new GameObject("SaveButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f + GameConstants.MarkerBarHeight, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/SaveButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (saveNameDialog != null)
                {
                    saveNameDialog.Open("저장 이름", "");
                }
            });
        }

        /// <summary>"저장" 버튼 바로 오른쪽(사용자 요청, 2026-09-02 — 처음엔
        /// 맨 왼쪽에 뒀다가 이 자리로 재배치) — 진행 중인 게임을 그대로 둔 채
        /// (씬 이동 없이) 곧장 호스팅을 시작하고 참가 코드를 띄운다
        /// (MultiplayerConnectDialog.Instance, GameFlowBootstrap이 영구
        /// 컴포넌트로 승격해둔 것 — 그 인스턴스 하나를 어느 씬에서든 그대로
        /// 재사용한다). 상대가 접속해 2명이 되면 CardPrep으로 보내지 않고
        /// 지금 보드 상태를 그대로 넘겨받아 둘 다 GameBoard에서 합류한다
        /// (MultiplayerConnectDialog.OnClientConnected/BoardManager.
        /// BroadcastFullStateForMidGameJoin 참고). 2026-09-04까지는 Open()으로
        /// Choice("호스트로 시작"/"참가 코드로 접속")→HostChoice("새 게임"/
        /// "이어하기")를 다 거치게 했는데, 이미 진행 중인 게임에서 누르는
        /// 버튼이라 "새 게임을 할지 참가할지"를 되묻는 게 무의미했다(사용자
        /// 지적) — OpenAndStartHosting()(원래 "이어하기"로 저장 불러온 뒤
        /// BoardManager.Start()가 자동으로 부르던 것과 같은 메서드)을 직접
        /// 불러 그 두 단계를 건너뛴다. 이 버튼은 이미 연결 중일 땐
        /// interactable=false로 막히므로(UpdateMultiplayerButtonState) 항상
        /// "아직 미연결" 상태에서만 눌린다 — "참가 코드로 접속" 경로는 이
        /// 버튼으로는 애초에 의미가 없다(진행 중인 보드를 버리고 남의
        /// 게임에 들어가는 셈이라).</summary>
        private RawImage _multiplayerButtonIcon;
        private Image _multiplayerButtonBorder;
        private Button _multiplayerButton;

        private void CreateMultiplayerButton(Transform parent)
        {
            var go = new GameObject("MultiplayerButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f + GameConstants.MarkerBarHeight * 2f, 0f);
            float size = GameConstants.MarkerBarHeight - 8f;
            rect.sizeDelta = new Vector2(size, size);

            // 연결 중 표시(사용자 요청, 2026-09-04) — 얇은 초록 테두리 +
            // 아이콘 어둡게 + 조작 불가. 전용 프레임 스프라이트 없이, 버튼
            // 루트 자신의 배경 Image를 테두리 색으로 깔고(평소엔 alpha 0으로
            // 안 보임) 그보다 살짝 작은 아이콘을 자식으로 그 위에 올려서
            // 가장자리 두께만큼만 테두리처럼 보이게 만든다(자식이 부모보다
            // 나중에 그려지는 UGUI 순서를 이용).
            const float BorderThickness = 2f;
            var border = go.AddComponent<Image>();
            border.color = new Color(0f, 0f, 0f, 0f);
            _multiplayerButtonBorder = border;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(size - BorderThickness * 2f, size - BorderThickness * 2f);
            var img = iconGo.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/MultiplayButton");
            _multiplayerButtonIcon = img;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = border;
            // Selectable의 기본 ColorTint 전환을 꺼둔다 — 안 그러면 그게
            // targetGraphic(border)의 색을 자기 나름대로(normal/disabled 등)
            // 계속 덧칠해서, 매 프레임 직접 칠하는 UpdateMultiplayerButtonState
            // 의 초록/투명 색과 서로 다퉈 깜빡이거나 잘못된 색으로 보일 수 있다.
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => MultiplayerConnectDialog.Instance?.OpenAndStartHosting());
            _multiplayerButton = btn;

            UpdateMultiplayerButtonState();
        }

        /// <summary>매 프레임 폴링(코루틴 없음, 이 프로젝트 관례) —
        /// BoardManager.cs의 Update()가 부른다. 연결 중이면 버튼을 초록
        /// 테두리 + 어두운 아이콘 + 비활성으로, 아니면 평소 모습으로.</summary>
        internal void UpdateMultiplayerButtonState()
        {
            if (_multiplayerButton == null)
            {
                return;
            }
            bool connected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            _multiplayerButton.interactable = !connected;
            _multiplayerButtonBorder.color = connected ? new Color(0.2f, 0.9f, 0.3f, 1f) : new Color(0f, 0f, 0f, 0f);
            if (_multiplayerButtonIcon != null)
            {
                _multiplayerButtonIcon.color = connected ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white;
            }
        }

        /// <summary>마커 아이콘 오른쪽에 조작법을 띄워주는 라벨(사용자 요청) —
        /// iconsGo 자신의 ContentSizeFitter 안에 넣으면 라벨이 나타날 때마다
        /// 그 그룹 전체가 재중앙정렬되며 아이콘들이 화면에서 좌우로 밀리는
        /// 문제가 있어서, 독립적으로 배치하고 아이콘 그룹의 폭을 직접
        /// 계산해서(HorizontalLayoutGroup과 같은 식) 그 오른쪽 끝에 맞춘다 —
        /// 그래야 아이콘 위치는 항상 고정이다.</summary>
        private void CreateMarkerHintLabel(Transform parent)
        {
            float iconSize = GameConstants.MarkerBarHeight - 8f;
            int count = MarkerBarEntries.Length;
            float iconsGroupWidth = 20f /* padding L+R */ + count * iconSize + Mathf.Max(count - 1, 0) * 6f /* spacing */;

            var go = new GameObject("MarkerHintLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(iconsGroupWidth / 2f + 12f, 0f);
            rect.sizeDelta = new Vector2(420f, GameConstants.MarkerBarHeight - 12f);

            _markerHintLabel = go.AddComponent<TextMeshProUGUI>();
            _markerHintLabel.fontSize = 13f;
            _markerHintLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            _markerHintLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _markerHintLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _markerHintLabel.raycastTarget = false;
            _markerHintLabel.text = "";
        }

        private void CreateScreenshotButton(Transform parent)
        {
            var go = new GameObject("ScreenshotButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/ScreenshotButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(TakeScreenshot);
        }

        /// <summary>스크린샷 버튼 바로 왼쪽 — 조작(팬/줌 상태)에 그대로 남는
        /// "지도 화면에 맞추기"(FitMapToView, BoardManager.PanZoom.cs). 예전
        /// 스크린샷이 찍기 직전에만 잠깐 적용하고 되돌리던 뷰를, 사용자
        /// 요청으로 일반 조작 버튼 하나로 분리한 것 — "고정"이 아니라 한 번
        /// 위치를 잡아줄 뿐이라 그 뒤엔 평소처럼 팬/줌할 수 있다.</summary>
        private void CreateFitButton(Transform parent)
        {
            var go = new GameObject("FitButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - (GameConstants.MarkerBarHeight - 8f) - 6f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/FitButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(FitMapToView);
        }

        /// <summary>Fit 버튼 바로 왼쪽 — 주사위 굴리기 툴(DiceRollDialog)을
        /// 연다. 보드 상태와 무관한 독립 창이라 여기선 그냥 여는 것만 한다.</summary>
        private void CreateDiceButton(Transform parent)
        {
            var go = new GameObject("DiceButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - 2f * (GameConstants.MarkerBarHeight - 8f) - 12f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/DiceButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (diceRollDialog != null)
                {
                    diceRollDialog.Open();
                }
            });
        }

        /// <summary>다이스 버튼 바로 왼쪽(사용자 지정) — 롤 오프 모달(RolloffDialog)을
        /// 연다. 보드 상태와 무관한 독립 창이라 여기선 그냥 여는 것만 한다.</summary>
        private void CreateRolloffButton(Transform parent)
        {
            var go = new GameObject("RolloffButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - 3f * (GameConstants.MarkerBarHeight - 8f) - 18f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/RolloffButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (rolloffDialog != null)
                {
                    rolloffDialog.Open();
                }
            });
        }

        /// <summary>롤 오프 버튼 바로 왼쪽 — 되돌리기 모달(UndoHistoryDialog)을
        /// 연다(2026-09-01 신설 — 예전 Ctrl+Z 단축키를 대체). 보드 상태와
        /// 무관한 독립 창이라 여기선 그냥 여는 것만 한다.</summary>
        private void CreateUndoHistoryButton(Transform parent)
        {
            var go = new GameObject("UndoHistoryButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - 4f * (GameConstants.MarkerBarHeight - 8f) - 24f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/UndoButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (undoHistoryDialog != null)
                {
                    undoHistoryDialog.Open(this);
                }
            });
        }

        private void CreateMarkerBarButton(Transform parent, string kind, Texture icon)
        {
            var go = new GameObject($"MarkerBtn_{kind}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => StartMarkerPlacement(kind));

            var hover = go.AddComponent<MarkerButtonHoverHandler>();
            string hint = MarkerControlHints.TryGetValue(kind, out var h) ? h : "";
            hover.OnEnter = () =>
            {
                if (_markerHintLabel != null)
                {
                    _markerHintLabel.text = hint;
                }
            };
            hover.OnExit = () =>
            {
                if (_markerHintLabel != null)
                {
                    _markerHintLabel.text = "";
                }
            };
        }

        /// <summary>마커바 버튼 하나에만 붙어서 마우스 진입/이탈을 알려준다 —
        /// Button 자체는 클릭만 다루므로, 호버로 조작법을 보여주려면
        /// IPointerEnterHandler/IPointerExitHandler를 직접 구현해야 한다
        /// (TacticalCardClickHandler와 같은 패턴).</summary>
        private class MarkerButtonHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public System.Action OnEnter;
            public System.Action OnExit;

            public void OnPointerEnter(PointerEventData eventData)
            {
                OnEnter?.Invoke();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                OnExit?.Invoke();
            }
        }

        private void StartMarkerPlacement(string kind)
        {
            if (_unitMoveActive)
            {
                return;
            }
            _placingMarkerKind = kind;
            ShowMarkerPlacementPreview(kind);
        }

        private void ShowMarkerPlacementPreview(string kind)
        {
            ClearMarkerPlacementPreview();
            var marker = CreateMarkerObject(kind, markerLayer);
            marker.raycastTarget = false;
            var c = marker.color;
            c.a *= 0.5f;
            marker.color = c;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                marker.Center = kind == "blast" ? ResolveBlastTemplateCenter(mouseLocal) : mouseLocal;
            }
            _markerPlacementPreview = marker;
        }

        private void ClearMarkerPlacementPreview()
        {
            if (_markerPlacementPreview != null)
            {
                Destroy(_markerPlacementPreview.gameObject);
                _markerPlacementPreview = null;
            }
        }

        /// <summary>kind에 맞는 마커 프리팹을 parent 아래에 인스턴스화한다
        /// (아직 위치/이벤트 연결은 안 함 — 배치 미리보기/실제 배치 양쪽에서
        /// 공용으로 쓴다). 텍스처/크기는 프리팹에 이미 채워져 있다.</summary>
        private MarkerBase CreateMarkerObject(string kind, Transform parent)
        {
            switch (kind)
            {
                case "activation":
                    return _activationMarkerPrefab != null ? Instantiate(_activationMarkerPrefab, parent, false) : null;
                case "capture":
                    return _captureMarkerPrefab != null ? Instantiate(_captureMarkerPrefab, parent, false) : null;
                case "blast":
                    return CreateBlastTemplateMarker(parent);
                default:
                    return _iconMarkerPrefabsByKind != null && _iconMarkerPrefabsByKind.TryGetValue(kind, out var prefab)
                            ? Instantiate(prefab, parent, false)
                            : null;
            }
        }

        /// <summary>블라스트 템플릿은 다른 아이콘 마커들과 달리 Resources/Markers/
        /// 프리팹이 없다 — 지름 5"(고정 크기)짜리 원형 도형이라 크기를 프리팹에
        /// 미리 박아둘 필요 없이 여기서 바로 계산해서 짓는다(ExitButton 등
        /// 마커바 버튼들을 코드로 짓는 것과 같은 방식). IconMarker를 그대로
        /// 쓰되 kind만 코드로 채운다(SetKind — 프리팹 인스펙터가 없으므로).
        /// 텍스처는 불투명한 원이라 알파를 낮춰서 그 아래 유닛/베이스 강조
        /// 테두리(BoardManager.BlastTemplate.cs)가 비쳐 보이게 한다.</summary>
        private static readonly Vector2 BlastTemplateSizeMm = new Vector2(
                GameConstants.MmPerInch * 5f, GameConstants.MmPerInch * 5f);

        private MarkerBase CreateBlastTemplateMarker(Transform parent)
        {
            var go = new GameObject("BlastTemplateMarker", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var marker = go.AddComponent<IconMarker>();
            marker.SetKind("blast");
            marker.texture = Resources.Load<Texture2D>("UI/BlastTemplate");
            marker.RectTransform.sizeDelta = BlastTemplateSizeMm;
            marker.color = new Color(1f, 1f, 1f, 0.4f);
            return marker;
        }

        /// <summary>배치 모드 중 마우스 클릭 처리 — 지도 위 왼쪽 버튼이면 실제로
        /// 배치하고, 그 외 버튼이면 배치를 취소한다. 어느 쪽이든 배치 모드는
        /// 끝난다. Godot판 스페이스바 처리 바로 아래의 _placing_marker_kind
        /// 분기 포팅. UI(마커바 버튼 등) 위 클릭은 여기서 손대지 않는다 — 예를
        /// 들어 다른 마커 버튼을 눌러 종류를 바꾸는 클릭은 그 버튼의
        /// onClick(StartMarkerPlacement)이 처리하는데, 여기서도 같은 클릭을
        /// "배치 모드 종료"로 취급해버리면 막 시작된 새 배치가 같은 프레임에
        /// 취소돼버린다.</summary>
        internal void HandleMarkerPlacementInput()
        {
            // IsOverMissionObjective()/IsOverBaseOrMarker() 예외: 미션 목표
            // 마커의 3인치 점령 링, 유닛(Base), 이미 깔려있는 마커 전부
            // raycastTarget이라 그 위 클릭이 "UI 위"로 잡혀서 그 자리에는
            // 새 마커를 배치할 수조차 없던 버그(사용자 보고 — 미션 목표는
            // 2026-09-02, 유닛/마커는 2026-09-06 — "BT를 배치할 때 유닛을
            // 클릭하면 유닛 이동으로 처리되며 배치가 안 됨", "마커 위에
            // 마커를 배치하려 하면 기존 마커를 옮기려 함") —
            // HandlePendingDeploymentInput의 같은 수정과 동일한 패턴. 유닛/
            // 마커 쪽 드래그 자체가 먼저 시작돼버리는 문제는 여기가 아니라
            // OnDragRequested/OnMarkerDragRequested의 IsPlacingMarker 가드가
            // 막는다 — 여긴 그 뒤 폴링 클릭이 "UI 위"로 막히지 않게 할
            // 뿐이다.
            if ((!IsPointerOverUi() || IsOverMissionObjective() || IsOverBaseOrMarker()) && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
            {
                if (Input.GetMouseButtonDown(0) && TryGetLocalMouse(out var mouseLocal))
                {
                    PlaceMarker(_placingMarkerKind, mouseLocal);
                }
                _placingMarkerKind = "";
                ClearMarkerPlacementPreview();
                return;
            }

            if (TryGetLocalMouse(out var hoverLocal) && _markerPlacementPreview != null)
            {
                _markerPlacementPreview.Center = _placingMarkerKind == "blast"
                        ? ResolveBlastTemplateCenter(hoverLocal)
                        : hoverLocal;
            }
        }

        /// <summary>블라스트 템플릿 전용 스냅 — 마우스가 어떤 모델의 베이스
        /// 위(타원 안)에 있으면 그 모델의 정확한 중심으로 스냅하고, 아니면
        /// 마우스 위치를 그대로 쓴다(사용자 요청, 2026-09-06). FindBaseAtPoint를
        /// 재사용한다 — BoardManager.Memo.cs의 호버 판정과 정확히 같은 기준
        /// (회전된 타원 안쪽)이라 "이 유닛에 호버되면 스냅된다"는 감각이
        /// 일관된다. RefreshBlastTemplateHighlights(BoardManager.BlastTemplate.cs)의
        /// "주 목표" 판정도 같은 FindBaseAtPoint(marker.Center)를 쓰므로,
        /// 스냅된 자리는 항상 그 모델이 주 목표로 강조된다.</summary>
        private Vector2 ResolveBlastTemplateCenter(Vector2 mouseLocal)
        {
            var snapped = FindBaseAtPoint(mouseLocal);
            return snapped != null ? snapped.Center : mouseLocal;
        }

        /// <summary>IsOverMissionObjective()와 같은 목적의 예외 판정 —
        /// 마우스가 유닛(Base) 또는 이미 깔려있는 마커 위여도 그게 "진짜 UI
        /// 패널"이 아니라 지도 위 조각일 뿐임을 나타낸다. HandleMarkerPlacementInput
        /// (배치 클릭)과 HandlePanAndZoom(팬/줌, BoardManager.PanZoom.cs)
        /// 양쪽에서 쓴다.</summary>
        private bool IsOverBaseOrMarker()
        {
            return TryGetLocalMouse(out var local) && (FindBaseAtPoint(local) != null || IsPointOverAnyMarker(local));
        }

        /// <summary>point(baseLayer 기준 mm 좌표)가 실제로 깔려있는 마커
        /// 아무거나의 사각 영역 안에 있는지 — 마커는 회전을 지원하지 않으므로
        /// (Center만 있고 회전 없음) 축 정렬 사각형 판정으로 충분하다. 실제
        /// UGUI 레이캐스터가 RawImage에 대해 판정하는 방식과도 같다.</summary>
        private bool IsPointOverAnyMarker(Vector2 point)
        {
            foreach (var markerGo in EnumerateRealMarkers())
            {
                if (!markerGo.TryGetComponent<MarkerBase>(out var marker))
                {
                    continue;
                }
                Vector2 half = marker.RectTransform.sizeDelta / 2f;
                Vector2 offset = point - marker.Center;
                if (Mathf.Abs(offset.x) <= half.x && Mathf.Abs(offset.y) <= half.y)
                {
                    return true;
                }
            }
            return false;
        }

        private void PlaceMarker(string kind, Vector2 point)
        {
            // 멀티플레이어 연결 중이면 호스트에게 배치를 요청하고 끝 —
            // 실제 마커 생성은 방송(PlaceMarkerRpc)이 도착했을 때
            // SpawnLocalMarkerVisual이 처리한다(호스트 자신도 이 경로를
            // 탄다). 되돌리기는 여기서 먼저 커밋해둔다 — CommitUndoTransaction이
            // 상대에게도 같은 항목을 방송하므로(BoardManager.UndoRedo.cs),
            // 마커가 실제로 화면에 나타나기 살짝 전에 스택에 먼저 올라가는
            // 셈이지만 그 시차는 무시할 수준이다.
            PerformNetworkedMutation(this, $"{GetMarkerKindLabel(kind)} 배치", "마커 배치",
                    () => BoardNetworkSync.Instance.RequestPlaceMarkerServerRpc(kind, point),
                    () =>
                    {
                        var marker = CreateMarkerObject(kind, markerLayer);
                        marker.Center = ClampMarkerToMap(marker, point);
                        WireMarkerEvents(marker, kind);
                    });
        }

        private void WireMarkerEvents(MarkerBase marker, string kind)
        {
            marker.DragRequested += OnMarkerDragRequested;
            switch (kind)
            {
                case "activation":
                    marker.RightClicked += OnActivationMarkerRightClicked;
                    break;
                case "capture":
                    marker.RightClicked += OnCaptureMarkerRightClicked;
                    break;
                default:
                    marker.RightClicked += OnIconMarkerRightClicked;
                    break;
            }
        }

        /// <summary>BoardNetworkSync.PlaceMarkerRpc가 방송을 받았을 때
        /// 호출한다(호스트 자신도 포함) — 완전히 로컬로(1인용 PlaceMarker와
        /// 같은 경로) 마커를 만든다. NetworkObject로 마커 자체를 스폰하지
        /// 않는 이유는 BoardNetworkSync.cs 클래스 주석 참고(NGO가
        /// NetworkObject를 일반 UI Transform 밑으로 재부모화하는 걸 막음).
        /// id는 호스트가 발급한 것 — 나중에 삭제 방송이 도착했을 때 이
        /// GameObject를 다시 찾는 데 쓴다.</summary>
        internal void SpawnLocalMarkerVisual(string kind, Vector2 point, int id)
        {
            var marker = CreateMarkerObject(kind, markerLayer);
            if (marker == null)
            {
                Debug.LogError($"[BoardManager] 마커 프리팹을 못 찾음: {kind}");
                return;
            }
            marker.Center = ClampMarkerToMap(marker, point);
            marker.NetworkMarkerId = id;
            _networkedMarkers.Set(id, marker);
            WireMarkerEvents(marker, kind);
        }

        /// <summary>BoardNetworkSync.DeleteMarkerRpc가 방송을 받았을 때
        /// 호출한다(호스트 자신도 포함, 삭제를 요청한 쪽도 포함 — 요청자가
        /// 직접 지우지 않고 이 방송을 거쳐서 지운다).</summary>
        internal void DeleteLocalMarkerVisual(int id)
        {
            if (!_networkedMarkers.TryGet(id, out var marker))
            {
                Debug.LogError($"[BoardManager] 삭제할 마커를 못 찾음(id={id})");
                return;
            }
            _networkedMarkers.Remove(id);
            _markerMoveTweens.RemoveAll(t => t.Marker == marker);
            Destroy(marker.gameObject);
        }

        /// <summary>BoardNetworkSync.SetMarkerStateRpc가 방송을 받았을 때
        /// 호출한다(호스트 자신도 포함) — 활성화 마커(이동→돌격→완료)와
        /// 점령 마커(색 순환) 양쪽 다 이걸로 처리한다. 마커 자체가
        /// NetworkObject가 아니라 타입 정보를 따로 안 보내므로, 여기서
        /// 실제 타입을 보고 판별한다.</summary>
        internal void SetLocalMarkerState(int id, string state)
        {
            if (!_networkedMarkers.TryGet(id, out var marker))
            {
                Debug.LogError($"[BoardManager] 상태를 바꿀 마커를 못 찾음(id={id})");
                return;
            }
            switch (marker)
            {
                case ActivationMarker activationMarker:
                    activationMarker.SetState(state);
                    break;
                case CaptureMarker captureMarker:
                    captureMarker.SetColorState(state);
                    break;
                default:
                    Debug.LogError($"[BoardManager] 상태 순환을 지원하지 않는 마커 타입(id={id})");
                    break;
            }
        }

        /// <summary>BoardNetworkSync.MoveMarkerRpc가 방송을 받았을 때
        /// 호출한다(드래그를 끝낸 쪽 자신도 포함 — 이미 그 자리에 있으므로
        /// 트윈이 사실상 아무 효과가 없다). 실시간 방송이 아니라 드래그
        /// 종료 시점 최종 위치 하나만 오므로(2026-08-30 사용자 요청 — 실시간일
        /// 필요 없다), 순간이동 대신 짧게 트윈해서 부드럽게 도착시킨다.</summary>
        internal void AnimateLocalMarkerMove(int id, Vector2 to)
        {
            if (!_networkedMarkers.TryGet(id, out var marker))
            {
                Debug.LogError($"[BoardManager] 이동시킬 마커를 못 찾음(id={id})");
                return;
            }
            _markerMoveTweens.RemoveAll(t => t.Marker == marker);
            _markerMoveTweens.Add(new MarkerMoveTween
            {
                Marker = marker,
                From = marker.Center,
                To = to,
                StartTime = Time.time,
            });
        }

        /// <summary>매 프레임 BoardManager.Update()에서 호출 — 진행 중인 마커
        /// 이동 트윈을 전진시킨다. 삭제된 마커(Destroy됨)는 Unity의 null
        /// 비교 오버로드 덕에 여기서 그냥 걸러진다.</summary>
        internal void UpdateMarkerMoveTweens()
        {
            for (int i = _markerMoveTweens.Count - 1; i >= 0; i--)
            {
                var tween = _markerMoveTweens[i];
                if (tween.Marker == null)
                {
                    _markerMoveTweens.RemoveAt(i);
                    continue;
                }
                float t = Mathf.Clamp01((Time.time - tween.StartTime) / MarkerMoveTweenDuration);
                float eased = t * t * (3f - 2f * t); // smoothstep
                tween.Marker.Center = Vector2.LerpUnclamped(tween.From, tween.To, eased);
                if (t >= 1f)
                {
                    _markerMoveTweens.RemoveAt(i);
                }
            }
        }

        /// <summary>markerLayer는 baseLayer와 같은 중심-원점 mm 좌표계이므로
        /// (mapSizeMm의 절반이 지도 중심) 그 기준으로 클램프한다.</summary>
        private Vector2 ClampMarkerToMap(MarkerBase marker, Vector2 desiredCenter)
        {
            Vector2 half = marker.RectTransform.sizeDelta / 2f;
            Vector2 mapHalf = mapSizeMm / 2f;
            return new Vector2(
                    Mathf.Clamp(desiredCenter.x, -mapHalf.x + half.x, mapHalf.x - half.x),
                    Mathf.Clamp(desiredCenter.y, -mapHalf.y + half.y, mapHalf.y - half.y));
        }

        private void OnMarkerDragRequested(MarkerBase piece)
        {
            if (IsPlacingMarker)
            {
                // 새 마커를 배치하는 중 클릭이 하필 이미 깔려있는 마커 위였어도
                // 그 마커를 옮기지 않는다(사용자 보고, 2026-09-06 — "마커 위에
                // 마커를 배치하려 하면 기존 마커를 옮기려 해서 배치가 안 됨").
                // 여기서 드래그를 시작해버리면 다음 프레임부터 BoardInputController.
                // RunFrame의 IsDraggingMarker 분기가 IsPlacingMarker보다 먼저
                // 걸려 배치 자체가 영영 처리되지 않는다(BoardManager.cs의
                // OnDragRequested·_pendingDeploymentDef/HasDisplacementQueue
                // 가드와 같은 이유).
                return;
            }
            BeginUndoTransaction($"{DescribeMarkerKind(piece)} 이동");
            _draggingMarker = piece;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                _markerDragOffset = piece.Center - mouseLocal;
            }
            piece.transform.SetAsLastSibling();
        }

        internal void HandleMarkerDragInput()
        {
            if (Input.GetMouseButtonUp(0))
            {
                var marker = _draggingMarker;
                _draggingMarker = null;
                // 드래그 자체는(반응성 때문에) 언제나 로컬로 실시간 진행됐다 —
                // 여기선 그 최종 위치만 상대에게 알린다. 매 프레임 방송하지
                // 않는 이유는 실시간일 필요가 없다는 사용자 판단(2026-08-30) —
                // 대신 받는 쪽은 AnimateLocalMarkerMove로 부드럽게 그 자리로
                // 움직여준다.
                if (marker.NetworkMarkerId >= 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    if (BoardNetworkSync.Instance == null)
                    {
                        Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 마커 이동 요청을 못 보냄");
                    }
                    else
                    {
                        BoardNetworkSync.Instance.RequestMoveMarkerServerRpc(marker.NetworkMarkerId, marker.Center);
                    }
                }
                CommitUndoTransaction();
                return;
            }
            if (TryGetLocalMouse(out var mouseLocal))
            {
                // 블라스트 템플릿은 쥔 지점과 무관하게 항상 마우스 아래 유닛의
                // 정확한 중심으로 스냅한다(그 외에는 일반 마커처럼 쥔 지점
                // 오프셋을 유지) — ShowMarkerPlacementPreview의 배치 스냅과
                // 같은 규칙(사용자 요청).
                var desired = _draggingMarker is IconMarker draggingIcon && draggingIcon.Kind == "blast"
                        ? ResolveBlastTemplateCenter(mouseLocal)
                        : mouseLocal + _markerDragOffset;
                _draggingMarker.Center = ClampMarkerToMap(_draggingMarker, desired);
            }
        }

        private void OnActivationMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            var marker = (ActivationMarker)piece;
            if (shiftHeld)
            {
                PerformNetworkedMutation(this, $"{DescribeMarkerKind(marker)} 삭제", "마커 삭제",
                        () => BoardNetworkSync.Instance.RequestDeleteMarkerServerRpc(marker.NetworkMarkerId),
                        () => Destroy(marker.gameObject));
                return;
            }
            // 우클릭은 이동 → 돌격 → 완료를 계속 순환한다. 연속으로
            // 눌러도(사용자 요청 — Composite) 되돌리기 목록엔 한 항목만
            // 남는다.
            int idx = System.Array.IndexOf(ActivationMarker.StateSequence, marker.State);
            string nextState = ActivationMarker.StateSequence[(idx + 1) % ActivationMarker.StateSequence.Length];
            string compositeKey = $"activationMarker:{marker.GetInstanceID()}";
            bool composite = IsTopUndoEntryComposite(compositeKey);
            string baseState = composite && _markerStateStreakBase.TryGetValue(marker.GetInstanceID(), out var b) ? b : marker.State;
            _markerStateStreakBase[marker.GetInstanceID()] = baseState;

            PerformNetworkedMutation(this, $"{DescribeMarkerKind(marker)} {DescribeActivationState(baseState)} -> {DescribeActivationState(nextState)}", "마커 상태 변경",
                    () => BoardNetworkSync.Instance.RequestSetMarkerStateServerRpc(marker.NetworkMarkerId, nextState),
                    () => marker.SetState(nextState),
                    compositeKey: compositeKey);
        }

        private void OnCaptureMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            var marker = (CaptureMarker)piece;
            if (shiftHeld)
            {
                PerformNetworkedMutation(this, $"{DescribeMarkerKind(marker)} 삭제", "마커 삭제",
                        () => BoardNetworkSync.Instance.RequestDeleteMarkerServerRpc(marker.NetworkMarkerId),
                        () => Destroy(marker.gameObject));
                return;
            }
            // 우클릭은 흰색 → 빨간색 → 파란색을 계속 순환한다. 색 순환에
            // 종료 지점이 없어서(활성화 마커처럼 마지막에 사라지는 게 아님)
            // 삭제는 별도 입력으로 뺐다. 연속으로 눌러도(사용자 요청 —
            // Composite) 되돌리기 목록엔 한 항목만 남는다.
            int idx = System.Array.IndexOf(CaptureMarker.ColorSequence, marker.ColorState);
            string nextState = CaptureMarker.ColorSequence[(idx + 1) % CaptureMarker.ColorSequence.Length];
            string compositeKey = $"captureMarker:{marker.GetInstanceID()}";
            bool composite = IsTopUndoEntryComposite(compositeKey);
            string baseState = composite && _markerStateStreakBase.TryGetValue(marker.GetInstanceID(), out var b) ? b : marker.ColorState;
            _markerStateStreakBase[marker.GetInstanceID()] = baseState;

            PerformNetworkedMutation(this, $"{DescribeMarkerKind(marker)} {DescribeCaptureColorState(baseState)} -> {DescribeCaptureColorState(nextState)}", "마커 상태 변경",
                    () => BoardNetworkSync.Instance.RequestSetMarkerStateServerRpc(marker.NetworkMarkerId, nextState),
                    () => marker.SetColorState(nextState),
                    compositeKey: compositeKey);
        }

        private void OnIconMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 순환 없이 우클릭 한 번으로 바로 삭제(shift 여부는 상관없다).
            PerformNetworkedMutation(this, $"{DescribeMarkerKind(piece)} 삭제", "마커 삭제",
                    () => BoardNetworkSync.Instance.RequestDeleteMarkerServerRpc(piece.NetworkMarkerId),
                    () => Destroy(piece.gameObject));
        }

    }
}
