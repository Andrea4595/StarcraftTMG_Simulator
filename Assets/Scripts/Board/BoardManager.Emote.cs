using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 맵 이모트(2026-09-02 신설, Phase 4 "소통 기능" 첫 항목) ────────
        // 빈 땅 우클릭 → 이모트 선택 다이얼(EmotePickerPanel) → 고르면 그
        // 클릭 지점에 잠깐 떴다 사라지는 이모지. 텍스트메시프로 기본 예제
        // 스프라이트 애셋 EmojiOne(표정 이모지 16종, Resources/Sprite Assets/
        // EmojiOne)을 그대로 재사용한다 — 마커/유닛과 달리 실제 보드 상태가
        // 아니므로(순수 시각 효과) 되돌리기 기록도, 저장 파일도, 클릭한
        // 사람 자신의 화면과 상대 화면을 구분하는 별도 처리도 필요 없다 —
        // 매번 클릭한 좌표+스프라이트 번호만 방송하고, 그걸 받은 모두가
        // (자기 자신 포함) 완전히 로컬로 새 이모트를 하나 만든다.

        private const float EmoteSizeMm = 60f;
        private const float EmoteVisibleDurationSeconds = 2.8f; // 사용자 요청으로 2배(원래 1.4초).
        private const float EmoteFadeDurationSeconds = 0.6f;

        // 화면 밖(팀 패널/상단바/마커바 바깥이 아니라, 그 안쪽의 실제 지도가
        // 보이는 영역 밖) 이모트를 위한 화면 가장자리 표시(2026-09-03,
        // 사용자 요청으로 재설계) — 이모지 원본이 화면 밖에 있으면 그 자리에
        // 보이지 않으므로, 가시 영역 가장자리에 "이모지 아이콘 + 그 옆에서
        // 실제 배치 방향을 가리키는 화살표" 한 쌍을 대신 띄운다. Arrow.png는
        // 기본적으로 "오른쪽"을 가리키는 모양이라고 사용자가 확인해줬다.
        // 팬/줌 때문에 이모트의 화면상 위치가 매 프레임 바뀔 수 있으므로,
        // 이 한 쌍은 스폰 시점에 한 번만 계산하지 않고 UpdateEmoteFades()에서
        // 매 프레임 새로 계산/생성/갱신/파괴한다.
        private const float OffScreenArrowSize = 28f;
        private const float OffScreenArrowMargin = 24f; // 화면 가장자리에서 안쪽으로 들어오는 여유.
        private const float EdgeIconSize = 40f; // 화면 밖일 때 대신 보이는 이모지 아이콘 크기(캔버스 px).
        private const float EdgeIndicatorGap = 8f; // 아이콘과 화살표 사이 간격.

        // 이모트 생존 기간(보이는 구간+페이드 구간) 전체를 0~1로 정규화한
        // 시간을 기준으로 X/Y 스케일을 독립적으로 계산하는 커브(사용자 요청,
        // "메커니즘만" — 실제 곡선 모양은 사용자가 직접 튜닝). BoardManager는
        // 씬에 저장 안 되고 매번 코드로 AddComponent되는 컴포넌트라
        // [SerializeField]로는 편집한 값이 저장될 자리가 없다(RolloffDialog의
        // scaleCurve와 같은 이유) — 대신 별도 ScriptableObject 자산
        // (Resources/EmoteScaleCurveSettings.asset)에서 읽어온다. 그 자산이
        // 없으면 코드 안 기본값(항상 1, 무변화)으로 방어적으로 대체한다.
        private AnimationCurve _emoteScaleCurveX = AnimationCurve.Constant(0f, 1f, 1f);
        private AnimationCurve _emoteScaleCurveY = AnimationCurve.Constant(0f, 1f, 1f);
        private bool _emoteCurveSettingsLoaded;

        private TMP_SpriteAsset _emojiSpriteAsset;
        private Vector2 _pendingEmoteBoardPoint;

        private sealed class EmoteFade
        {
            public GameObject Go;
            public RectTransform Rect;
            public CanvasGroup Group;
            public int SpriteIndex;
            public GameObject ArrowGo; // 화면 안이면 null — 매 프레임 필요할 때만 생성.
            public CanvasGroup ArrowGroup;
            public GameObject EdgeIconGo; // 화면 안이면 null — 화살표와 짝을 이뤄 생성/파괴.
            public CanvasGroup EdgeIconGroup;
            public float SpawnTime;
            public float HideTime;
        }
        private readonly List<EmoteFade> _emoteFades = new List<EmoteFade>();

        /// <summary>BoardManager.cs Update()의 빈 땅 우클릭 처리가 부른다 —
        /// 실제로 어디에 이모트를 놓을지는 다이얼에서 하나 고른 뒤에야
        /// 정해지므로(OnEmoteChosen), 그때까지 클릭 지점을 들고 있는다.</summary>
        private void OpenEmotePicker(Vector2 boardPoint)
        {
            if (emotePickerPanel == null)
            {
                return;
            }
            _pendingEmoteBoardPoint = boardPoint;
            emotePickerPanel.Open(Input.mousePosition);
        }

        /// <summary>EmotePickerPanel.EmoteChosen 콜백 — 멀티 연결 중이면 방송
        /// 요청만 하고(로컬 반영은 그 방송이 되돌아오는 걸 거친다, 마커/유닛과
        /// 같은 패턴), 미연결이면 바로 로컬로 만든다.</summary>
        private void OnEmoteChosen(int spriteIndex)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 이모트 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestPlaceEmoteServerRpc(spriteIndex, _pendingEmoteBoardPoint);
            }
            else
            {
                SpawnLocalEmote(spriteIndex, _pendingEmoteBoardPoint);
            }
        }

        /// <summary>BoardNetworkSync.PlaceEmoteRpc가 방송을 받았을 때(요청한
        /// 쪽 자신도 포함) 호출한다 — 완전히 로컬로만 만든다(NetworkObject
        /// 아님, 마커와 같은 이유). 되돌리기 기록도, _pieces/마커 레이어
        /// 등록도 전혀 안 한다 — 순수 시각 효과라 다른 어떤 상태에도
        /// 영향을 주지 않는다. 이 이모트가 지금 이 화면의 실제 지도 가시
        /// 영역(팬/줌은 로컬 전용이라 사람마다 다름) 밖에 놓였다면,
        /// UpdateEmoteFades()가 매 프레임 화면 가장자리에 이모지 아이콘과
        /// 그 방향을 가리키는 화살표를 함께 띄워준다(사용자 요청).</summary>
        internal void SpawnLocalEmote(int spriteIndex, Vector2 boardPoint)
        {
            if (_emojiSpriteAsset == null)
            {
                _emojiSpriteAsset = Resources.Load<TMP_SpriteAsset>("Sprite Assets/EmojiOne");
            }
            if (!_emoteCurveSettingsLoaded)
            {
                _emoteCurveSettingsLoaded = true;
                var curveSettings = Resources.Load<EmoteScaleCurveSettings>("EmoteScaleCurveSettings");
                if (curveSettings != null)
                {
                    _emoteScaleCurveX = curveSettings.scaleCurveX;
                    _emoteScaleCurveY = curveSettings.scaleCurveY;
                }
            }

            var go = new GameObject("Emote", typeof(RectTransform));
            go.transform.SetParent(baseLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = boardPoint;
            rect.sizeDelta = new Vector2(EmoteSizeMm, EmoteSizeMm);
            go.transform.SetAsLastSibling(); // 다른 조각들 위에 보이도록.

            var group = go.AddComponent<CanvasGroup>();

            var label = go.AddComponent<TextMeshProUGUI>();
            label.spriteAsset = _emojiSpriteAsset;
            label.text = $"<sprite index={spriteIndex}>";
            label.fontSize = EmoteSizeMm * 0.9f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            _emoteFades.Add(new EmoteFade
            {
                Go = go,
                Rect = rect,
                Group = group,
                SpriteIndex = spriteIndex,
                ArrowGo = null,
                ArrowGroup = null,
                EdgeIconGo = null,
                EdgeIconGroup = null,
                SpawnTime = Time.time,
                HideTime = Time.time + EmoteVisibleDurationSeconds,
            });
        }

        /// <summary>이모트의 실제 화면 좌표를, 팀 패널/상단바/마커바를 뺀 진짜
        /// 지도 가시 영역(그 바 폭들은 GameConstants 상수 그대로 — ScreenshotToast
        /// 등 다른 곳도 같은 상수로 그 영역을 피해 배치한다)과 비교한다. 그
        /// 영역 밖이면 원본 이모지는 화면에 안 보이므로, 캔버스 중심에서
        /// 이모트 쪽으로의 방향을 계산해서 가시 영역 가장자리에 닿는 지점
        /// (약간 안쪽으로 들여서)에 화살표를 놓고, 그 화살표 바로 안쪽(화면
        /// 중심 쪽)에 이모지 아이콘을 나란히 띄운다(사용자 요청 — "이모지가
        /// 화면 안쪽까지 들어와서 보이게" + "화살표는 이모지 옆에서 배치된
        /// 위치를 가리키게"). 화살표는 그 방향을 향하도록 돌린다. 팬/줌은
        /// 사람마다 로컬로 다르고 매 프레임 바뀔 수 있으므로(기존 아키텍처
        /// 방침), 이 판정은 UpdateEmoteFades()가 매 프레임 각자 자기 화면
        /// 기준으로 새로 계산해서 호출한다 — 그래서 네트워크 코드가 전혀
        /// 필요 없다(방송으로 온 이모트든 로컬로 만든 이모트든 매 프레임 이
        /// 함수가 자기 화면 기준으로 아이콘+화살표를 만들거나, 갱신하거나,
        /// 더 이상 필요 없으면 없앤다).</summary>
        private void UpdateOffScreenIndicator(EmoteFade e)
        {
            if (!(GetCanvasParent() is RectTransform canvasRect))
            {
                return;
            }

            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, e.Go.transform.position);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out var local))
            {
                return;
            }

            float left = canvasRect.rect.xMin + GameConstants.PendingPanelWidth;
            float right = canvasRect.rect.xMax - GameConstants.PendingPanelWidth;
            float bottom = canvasRect.rect.yMin + GameConstants.MarkerBarHeight;
            float top = canvasRect.rect.yMax - GameConstants.TopBarHeight;

            if (local.x >= left && local.x <= right && local.y >= bottom && local.y <= top)
            {
                // 지금 화면에 이미 보인다 — 아이콘/화살표가 떠 있었다면 없앤다.
                DestroyOffScreenIndicator(e);
                return;
            }

            Vector2 center = new Vector2((left + right) / 2f, (bottom + top) / 2f);
            Vector2 dir = local - center;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            dir.Normalize();

            float halfW = Mathf.Max(1f, (right - left) / 2f - OffScreenArrowMargin);
            float halfH = Mathf.Max(1f, (top - bottom) / 2f - OffScreenArrowMargin);
            float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            Vector2 arrowPos = center + dir * t;
            // Arrow.png는 기본적으로 오른쪽(+X)을 향한다(사용자 확인) —
            // atan2가 그대로 "+X축 기준" 각도이므로 추가 보정 없이 쓴다.
            float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            // 아이콘은 화살표보다 화면 중심 쪽으로 한 칸 안쪽에 나란히 둔다 —
            // [아이콘][화살표] 순서로, 화살표가 실제 배치 방향(더 바깥쪽,
            // 화면 밖)을 가리키는 모양이 되도록.
            float inwardOffset = OffScreenArrowSize / 2f + EdgeIndicatorGap + EdgeIconSize / 2f;
            Vector2 iconPos = arrowPos - dir * inwardOffset;

            if (e.ArrowGo == null)
            {
                e.ArrowGo = new GameObject("EmoteArrow", typeof(RectTransform));
                e.ArrowGo.transform.SetParent(canvasRect, false);
                var newArrowRect = (RectTransform)e.ArrowGo.transform;
                newArrowRect.anchorMin = new Vector2(0.5f, 0.5f);
                newArrowRect.anchorMax = new Vector2(0.5f, 0.5f);
                newArrowRect.pivot = new Vector2(0.5f, 0.5f);
                newArrowRect.sizeDelta = new Vector2(OffScreenArrowSize, OffScreenArrowSize);

                var img = e.ArrowGo.AddComponent<RawImage>();
                img.texture = Resources.Load<Texture2D>("UI/Arrow");
                img.raycastTarget = false;

                e.ArrowGroup = e.ArrowGo.AddComponent<CanvasGroup>();
            }
            e.ArrowGo.transform.SetAsLastSibling();

            var arrowRect = (RectTransform)e.ArrowGo.transform;
            arrowRect.anchoredPosition = arrowPos;
            arrowRect.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

            if (e.EdgeIconGo == null)
            {
                e.EdgeIconGo = new GameObject("EmoteEdgeIcon", typeof(RectTransform));
                e.EdgeIconGo.transform.SetParent(canvasRect, false);
                var newIconRect = (RectTransform)e.EdgeIconGo.transform;
                newIconRect.anchorMin = new Vector2(0.5f, 0.5f);
                newIconRect.anchorMax = new Vector2(0.5f, 0.5f);
                newIconRect.pivot = new Vector2(0.5f, 0.5f);
                newIconRect.sizeDelta = new Vector2(EdgeIconSize, EdgeIconSize);

                var iconLabel = e.EdgeIconGo.AddComponent<TextMeshProUGUI>();
                iconLabel.spriteAsset = _emojiSpriteAsset;
                iconLabel.text = $"<sprite index={e.SpriteIndex}>";
                iconLabel.fontSize = EdgeIconSize * 0.9f;
                iconLabel.alignment = TextAlignmentOptions.Center;
                iconLabel.raycastTarget = false;

                e.EdgeIconGroup = e.EdgeIconGo.AddComponent<CanvasGroup>();
            }
            e.EdgeIconGo.transform.SetAsLastSibling();
            e.ArrowGo.transform.SetAsLastSibling(); // 화살표가 아이콘보다 위(마지막)에 오도록 다시 한 번.

            var iconRect = (RectTransform)e.EdgeIconGo.transform;
            iconRect.anchoredPosition = iconPos;
        }

        /// <summary>화면 밖 표시(아이콘+화살표)를 파괴하고 참조를 비운다 —
        /// 원본 이모지가 다시 화면 안으로 들어왔을 때, 또는 이모트 자체가
        /// 완전히 사라질 때 둘 다에서 쓰인다.</summary>
        private static void DestroyOffScreenIndicator(EmoteFade e)
        {
            if (e.ArrowGo != null)
            {
                Destroy(e.ArrowGo);
                e.ArrowGo = null;
                e.ArrowGroup = null;
            }
            if (e.EdgeIconGo != null)
            {
                Destroy(e.EdgeIconGo);
                e.EdgeIconGo = null;
                e.EdgeIconGroup = null;
            }
        }

        /// <summary>매 프레임 폴링 — ScreenshotToast/UndoToast와 같은 이유로
        /// 이 프로젝트는 코루틴을 안 쓴다. 알파 페이드, 화면 밖 아이콘/화살표
        /// 생성/갱신/제거, X/Y 독립 스케일 커브를 모두 매 프레임 갱신하고,
        /// 다 사라진 이모트(및 그 아이콘/화살표)는 파괴하고 목록에서 뺀다.</summary>
        private void UpdateEmoteFades()
        {
            float totalLifetime = EmoteVisibleDurationSeconds + EmoteFadeDurationSeconds;
            for (int i = _emoteFades.Count - 1; i >= 0; i--)
            {
                var e = _emoteFades[i];
                if (e.Go == null)
                {
                    _emoteFades.RemoveAt(i);
                    continue;
                }
                float fadeElapsed = Time.time - e.HideTime;
                if (fadeElapsed >= EmoteFadeDurationSeconds)
                {
                    Destroy(e.Go);
                    DestroyOffScreenIndicator(e);
                    _emoteFades.RemoveAt(i);
                    continue;
                }

                float alpha = fadeElapsed <= 0f ? 1f : 1f - fadeElapsed / EmoteFadeDurationSeconds;
                e.Group.alpha = alpha;

                float normalized = totalLifetime > 0f
                    ? Mathf.Clamp01((Time.time - e.SpawnTime) / totalLifetime)
                    : 1f;
                Vector3 scale = new Vector3(
                    _emoteScaleCurveX.Evaluate(normalized),
                    _emoteScaleCurveY.Evaluate(normalized),
                    1f);
                e.Rect.localScale = scale;

                UpdateOffScreenIndicator(e);
                if (e.ArrowGroup != null)
                {
                    e.ArrowGroup.alpha = alpha;
                }
                if (e.EdgeIconGroup != null)
                {
                    e.EdgeIconGroup.alpha = alpha;
                    ((RectTransform)e.EdgeIconGo.transform).localScale = scale;
                }
            }
        }
    }
}
