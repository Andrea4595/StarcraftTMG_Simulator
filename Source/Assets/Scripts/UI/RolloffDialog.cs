using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// "롤 오프" 모달(2026-08-31 신설) — 게임 화면 마커바의 RolloffButton.png
    /// (DiceButton 바로 왼쪽)로 연다. 왼쪽에 플레이어 A, 오른쪽에 플레이어 B의
    /// 큰 사각형이 하나씩 뜨고, 각 사각형은 언제든 클릭해서 1~6 사이의 눈을
    /// 다시 굴릴 수 있다(동점이면 다시 눌러서 재굴림) — 그 이상의 규칙(승패
    /// 판정 등)은 이 프로젝트의 "매뉴얼 시뮬레이터" 철학대로 다루지 않는다.
    ///
    /// ConfirmDialog/MissionInfoDialog와 같은 진짜 모달(배경 전체를 덮고
    /// 바깥 클릭/자기 버튼으로만 닫힘)이다 — DiceRollDialog(무기 프로필과
    /// 동시에 봐야 해서 일부러 비독점 창으로 만든 것)와는 다른 부류. 보드/
    /// BoardManager 상태와 완전히 무관한 독립 컴포넌트라 어느 씬에서든 그대로
    /// 갖다 쓸 수 있다 — 사용자가 명시한 대로, 나중에 멀티플레이용 배치구역/
    /// 미션 선택(누가 먼저 고를지 정하는 롤 오프) 화면에서도 재사용할 예정.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RolloffDialog : MonoBehaviour, IPointerDownHandler
    {
        private const float SquareSize = 160f;
        private const float SquareGap = 40f;

        // "살짝" 채도/명도를 낮춘다(사용자 지정) — 로스터 토큰처럼 큰 폭으로
        // 톤다운하는 게 아니라 가벼운 조정이라, 미션 목표 토큰과 같은 수치를
        // 그대로 재사용한다(GameConstants.MissionObjectiveSaturationFactor/
        // ValueFactor — "로스터 토큰보다는 가볍게"라는 그쪽 주석과 의도가 같음).
        private static readonly float SquareSaturationFactor = GameConstants.MissionObjectiveSaturationFactor;
        private static readonly float SquareValueFactor = GameConstants.MissionObjectiveValueFactor;

        // 새로 굴릴 때마다 흰색으로 번쩍였다가 팀 색으로 가라앉는다(사용자
        // 요청 — 눈금이 갱신됐다는 걸 알 수 있게). 이 프로젝트는 코루틴을
        // 안 쓰므로(CoherencyWarning/ScreenshotToast 등과 같은 기존 방침)
        // Time.time 기반 보간을 Update()에서 매 프레임 계산한다.
        private const float FlashDurationSeconds = 0.3f;

        // 크기 1.15 → 1 변화의 진행 모양 — X축은 경과 시간이 아니라 진행률
        // t(0=시작, 1=0.3초 뒤 끝) 그대로다. 실제 크기는
        // "1.15 - 0.15 * curve(t)"로 계산한다(사용자 지정 공식) — curve가
        // 0→1로 갈수록(기본은 EaseInOut) 크기가 1.15→1로 수렴한다.
        // RolloffDialog 자체는 씬에 저장 안 되고 매번 코드로 새로 만들어지는
        // 컴포넌트라 [SerializeField]로는 편집한 값이 저장될 자리가 없다
        // (사용자 지적) — 대신 별도 ScriptableObject 자산(Resources/
        // RolloffCurveSettings.asset)에서 읽어온다. 그 자산이 없으면(아직 안
        // 만들었거나 지워진 경우) 코드 안 기본값으로 방어적으로 대체한다.
        private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        private const float FlashScalePeak = 1.15f;

        private float _flashStartA = -1f;
        private float _flashStartB = -1f;

        // 재굴림 쿨다운(사용자 요청, 2026-09-09) — 각 사각형 독립(둘 중
        // 하나를 굴려도 다른 쪽엔 영향 없음). ApplyRoll에서만 갱신한다(굴린
        // 쪽 자신도 방송 루프백으로 여길 거치므로) — 그래서 클릭한 쪽/받은
        // 쪽 양쪽 화면에서 자연히 똑같은 시각에 쿨다운이 시작된다. 사용자
        // 지적대로 "값 갱신이 일어날 때 걸면" 되므로 별도 동기화 RPC가
        // 필요 없다 — 굴림 자체가 이미 동기화돼 있어서(RequestRollDiceServerRpc
        // → ApplyRemoteRoll → ApplyRoll, 마커/유닛과 같은 방송 루프백 패턴)
        // 쿨다운 시작 시각도 공짜로 양쪽에서 일치한다.
        private const float RollCooldownSeconds = 1f;
        private float _lastRollTimeA = -1000f;
        private float _lastRollTimeB = -1000f;

        // 호버 확대(사용자 요청, 2026-09-09) — 지금 실제로 굴릴 수 있을 때만
        // (쿨다운 중이 아닐 때만) 살짝 커진다. 쿨다운(1초)이 항상 반짝임
        // 애니메이션(0.3초)보다 오래가므로 "쿨다운 아님=false"인 최종
        // 판정 자체는 반짝임 진행 중에도 항상 맞지만, UpdateHoverScale은
        // 그래도 flashStart가 살아있는 동안은 아예 손을 떼도록 따로
        // 가드한다 — 안 그러면 매 프레임 무조건 스케일을 쓰는 이 함수가
        // UpdateFlash가 그 프레임에 계산해둔 중간값(확대→수렴 애니메이션)을
        // 곧바로 되덮어써서 반짝임 연출 자체가 안 보이게 된다.
        private const float HoverScale = 1.08f;
        private HoverTracker _hoverA;
        private HoverTracker _hoverB;

        private Texture2D _squareTexture;
        private Texture2D[] _diceTextures;

        private RawImage _faceA;
        private RawImage _faceB;

        private void Awake()
        {
            var curveSettings = Resources.Load<RolloffCurveSettings>("RolloffCurveSettings");
            if (curveSettings != null)
            {
                scaleCurve = curveSettings.scaleCurve;
            }

            _squareTexture = Resources.Load<Texture2D>("UI/Square");
            _diceTextures = new[]
            {
                Resources.Load<Texture2D>("UI/Dice1"),
                Resources.Load<Texture2D>("UI/Dice2"),
                Resources.Load<Texture2D>("UI/Dice3"),
                Resources.Load<Texture2D>("UI/Dice4"),
                Resources.Load<Texture2D>("UI/Dice5"),
                Resources.Load<Texture2D>("UI/Dice6"),
            };

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(SquareSize * 2f + SquareGap + 80f, SquareSize + 110f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            panelGo.AddComponent<PanelBlocker>();

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleLabel = CreateLabel(panelGo.transform, "롤 오프", 18f, FontStyles.Bold);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.GetComponent<LayoutElement>().preferredHeight = 24f;

            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(panelGo.transform, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = SquareGap;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandHeight = false;
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = SquareSize;

            _faceA = CreateSquare(row.transform, "A");
            _faceB = CreateSquare(row.transform, "B");

            var closeGo = new GameObject("Close", typeof(RectTransform));
            closeGo.transform.SetParent(panelGo.transform, false);
            var closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.preferredHeight = 36f;
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);
            var closeLabel = CreateLabel(closeGo.transform, "닫기", 16f, FontStyles.Normal);
            var closeLabelRect = (RectTransform)closeLabel.transform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;
            Destroy(closeLabel.GetComponent<LayoutElement>());
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.raycastTarget = false;

            gameObject.SetActive(false);
        }

        /// <summary>멀티 연결 중이면(2026-08-31 추가) 로컬에서 바로 열지
        /// 않고 방송 요청만 한다 — 실제로 여는 건 그 요청이 되돌아오는
        /// 방송(BoardNetworkSync.SetRolloffOpenRpc → ApplyRemoteSetOpen)을
        /// 거쳐서, 나를 포함한 양쪽 모두에서 동시에 일어난다(사용자 지정 —
        /// 한쪽이 열면 둘 다 뜨고, 한쪽이 닫으면 둘 다 닫힘).</summary>
        public void Open()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[RolloffDialog] BoardNetworkSync.Instance가 없음 — 창 열기 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestSetRolloffOpenServerRpc(true);
                return;
            }
            OpenLocal();
        }

        public void Close()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[RolloffDialog] BoardNetworkSync.Instance가 없음 — 창 닫기 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestSetRolloffOpenServerRpc(false);
                return;
            }
            CloseLocal();
        }

        /// <summary>BoardNetworkSync.SetRolloffOpenRpc가 방송을 받았을 때
        /// 호출한다(요청한 쪽 자신도 포함, 롤 결과 방송과 같은 루프백).</summary>
        public void ApplyRemoteSetOpen(bool open)
        {
            if (open)
            {
                OpenLocal();
            }
            else
            {
                CloseLocal();
            }
        }

        private void OpenLocal()
        {
            _flashStartA = -1f;
            _flashStartB = -1f;
            ResetSquare(_faceA, "A");
            ResetSquare(_faceB, "B");
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void CloseLocal()
        {
            gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Close();
        }

        private void Update()
        {
            UpdateFlash(_faceA, "A", ref _flashStartA);
            UpdateFlash(_faceB, "B", ref _flashStartB);
            UpdateHoverScale(_faceA, _hoverA, "A", _flashStartA);
            UpdateHoverScale(_faceB, _hoverB, "B", _flashStartB);
        }

        private bool IsOnCooldown(string team)
        {
            float lastRollTime = team == "A" ? _lastRollTimeA : _lastRollTimeB;
            return Time.time - lastRollTime < RollCooldownSeconds;
        }

        /// <summary>호버 중 + 지금 굴릴 수 있음(쿨다운 아님) 둘 다일 때만
        /// 살짝 키운다. flashStart가 아직 살아있으면(반짝임 애니메이션
        /// 진행 중) 아예 손대지 않는다 — UpdateFlash가 매 프레임 직접 계산한
        /// 스케일을 이 함수가 무조건 1로 덮어써버리면 반짝임의 확대→수렴
        /// 연출이 안 보이게 된다. 쿨다운(1초)이 항상 반짝임(0.3초)보다
        /// 길므로, 그 창 밖(flashStart<0)에서는 이미 쿨다운도 유지되고
        /// 있어 grow가 자연히 false로 나온다 — 이 가드는 그 사이 짧은
        /// 구간에서 값을 두 번 덮어쓰지 않기 위한 것.</summary>
        private void UpdateHoverScale(RawImage face, HoverTracker hover, string team, float flashStart)
        {
            if (flashStart >= 0f)
            {
                return;
            }
            bool grow = hover != null && hover.IsHovering && !IsOnCooldown(team);
            face.rectTransform.localScale = Vector3.one * (grow ? HoverScale : 1f);
        }

        /// <summary>색상(흰색→팀 색)과 크기(1.15→1) 둘 다 같은 진행률
        /// t(0~1, 0.3초에 걸쳐 0에서 1로)로 움직인다 — RollSquare가 색을
        /// 흰색/크기를 확대로 찍어두고 시작 시각만 남겨두면, 여기서 매 프레임
        /// 다시 그린다. 크기는 "1.15 - 0.15 * scaleCurve(t)"(사용자 지정
        /// 공식) — scaleCurve(t)가 0→1로 갈수록 크기가 1.15→1로 수렴한다.
        /// 다 가라앉으면(t≥1) 정확히 목표 색/크기(1)로 고정하고 타이머를
        /// 꺼서(-1) 더 이상 계산하지 않는다.</summary>
        private void UpdateFlash(RawImage face, string team, ref float flashStart)
        {
            if (flashStart < 0f)
            {
                return;
            }
            float elapsed = Time.time - flashStart;
            if (elapsed >= FlashDurationSeconds)
            {
                face.color = MutedTeamColor(team);
                face.rectTransform.localScale = Vector3.one;
                flashStart = -1f;
                return;
            }
            float t = elapsed / FlashDurationSeconds;
            face.color = Color.Lerp(Color.white, MutedTeamColor(team), t);
            float scale = FlashScalePeak - (FlashScalePeak - 1f) * scaleCurve.Evaluate(t);
            face.rectTransform.localScale = Vector3.one * scale;
        }

        private static Color MutedTeamColor(string team)
        {
            var raw = GameConstants.TeamColors.TryGetValue(team, out var c) ? c : Color.white;
            return GameConstants.Muted(raw, SquareSaturationFactor, SquareValueFactor);
        }

        /// <summary>사각형 하나(RawImage 한 장) — 굴리기 전엔 UI/Square(빈
        /// 사각형), 굴린 뒤엔 UI/DiceN으로 텍스처만 바꾼다. 둘 다 항상 같은
        /// (톤다운된) 팀 색으로 칠해서, 어느 상태든 "이 사각형은 이 팀 색"이
        /// 유지된다 — 클릭할 때마다 새로 굴려서 다시 칠한다(횟수 제한 없음,
        /// 사용자 지정 — 동점이면 다시 굴려야 하니까). 팀 이름 라벨은 안 붙인다
        /// (사용자 지정 — 색만으로 충분).</summary>
        private RawImage CreateSquare(Transform parent, string team)
        {
            var squareGo = new GameObject($"Square_{team}", typeof(RectTransform));
            squareGo.transform.SetParent(parent, false);
            var squareRect = (RectTransform)squareGo.transform;
            squareRect.sizeDelta = new Vector2(SquareSize, SquareSize);
            var face = squareGo.AddComponent<RawImage>();
            face.texture = _squareTexture;

            var btn = squareGo.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.onClick.AddListener(() => RollSquare(face, team));

            var hover = squareGo.AddComponent<HoverTracker>();
            if (team == "A")
            {
                _hoverA = hover;
            }
            else
            {
                _hoverB = hover;
            }

            return face;
        }

        private void ResetSquare(RawImage face, string team)
        {
            face.texture = _squareTexture;
            face.color = MutedTeamColor(team);
            face.rectTransform.localScale = Vector3.one;
            var hover = team == "A" ? _hoverA : _hoverB;
            if (hover != null)
            {
                hover.IsHovering = false;
            }
        }

        /// <summary>클릭한 사각형을 다시 굴린다 — 멀티 연결 중이면(2026-08-31
        /// 추가) 로컬에서 바로 칠하지 않고 방금 굴린 값을 방송 요청만 한다.
        /// 실제 반영은 그 요청이 되돌아오는 방송(BoardNetworkSync.SetDiceRpc
        /// → ApplyRemoteRoll)을 거쳐서 일어난다 — 마커 배치와 같은 "방송 후
        /// 로컬 반영" 패턴. 서버가 다시 굴리지 않고 클릭한 쪽이 굴린 값을
        /// 그대로 전달만 하는 이유는 이 프로젝트의 "매뉴얼 시뮬레이터" 철학
        /// (판정은 안 함, 결과 공유만) 그대로.</summary>
        private void RollSquare(RawImage face, string team)
        {
            if (IsOnCooldown(team))
            {
                // 연타 스팸 방지(사용자 요청, 2026-09-09) — 어느 쪽이 눌렀는지는
                // 안 가린다(누구나 아무 사각형이나 계속 굴릴 수 있음, 그대로
                // 유지), 같은 사각형을 너무 빨리 다시 누르는 것만 막는다.
                return;
            }
            int value = Random.Range(1, 7);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[RolloffDialog] BoardNetworkSync.Instance가 없음 — 롤 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestRollDiceServerRpc(team, value);
                return;
            }
            ApplyRoll(team, value);
        }

        /// <summary>BoardNetworkSync.SetDiceRpc가 방송을 받았을 때 호출한다
        /// (굴린 쪽 자신도 포함, 마커/유닛 방송과 동일한 루프백).</summary>
        public void ApplyRemoteRoll(string team, int value)
        {
            ApplyRoll(team, value);
        }

        private void ApplyRoll(string team, int value)
        {
            var face = team == "A" ? _faceA : _faceB;
            face.texture = _diceTextures[value - 1];
            face.color = Color.white; // 흰색으로 번쩍이는 시작점 — Update()의 UpdateFlash가 여기서부터 팀 색으로 가라앉힌다.
            if (team == "A")
            {
                _flashStartA = Time.time;
                _lastRollTimeA = Time.time;
            }
            else
            {
                _flashStartB = Time.time;
                _lastRollTimeB = Time.time;
            }
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>();
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>패널 위 클릭이 배경의 OnPointerDown(닫기)으로 새어나가지
        /// 않게 막기만 하는 투명 핸들러 — ConfirmDialog.cs와 같은 패턴.</summary>
        private class PanelBlocker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData eventData)
            {
                eventData.Use();
            }
        }

        /// <summary>사각형 위 마우스 호버 여부만 기록하는 컴포넌트 — 실제
        /// 확대 여부(쿨다운까지 감안)는 Update()의 UpdateHoverScale이
        /// 매 프레임 판단한다.</summary>
        private class HoverTracker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public bool IsHovering;
            public void OnPointerEnter(PointerEventData eventData) => IsHovering = true;
            public void OnPointerExit(PointerEventData eventData) => IsHovering = false;
        }
    }
}
