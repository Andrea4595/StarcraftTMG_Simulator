using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 블라스트 템플릿(지름 5" 타격 범위) ──────────────────────────
        // 사용자 요청(2026-09-06): 마커바에서 배치하는 원형 타격 범위 마커.
        // 마커 자체(위치/이동/삭제)는 일반 아이콘 마커와 완전히 같은 경로를
        // 타므로(BoardManager.Markers.cs의 "blast" kind 분기 — 되돌리기/저장/
        // 멀티플레이어 동기화가 전부 그대로 딸려온다) 여기서는 "지금 이
        // 범위 안에 어떤 유닛이 들어와 있는가"만 매 프레임 다시 계산해서
        // Base.BlastPrimary/BlastSecondary로 반영한다. 이 판정 자체는
        // 저장/동기화 대상이 아니다 — 마커 위치 + 유닛 위치(둘 다 이미
        // 동기화됨)로부터 각 클라이언트가 로컬로 그대로 다시 계산할 수
        // 있는 파생 상태이기 때문(BoardManager.Range.cs의 RefreshRangeOverlays와
        // 같은 성격).
        //
        // 아래 두 매 프레임 함수(RefreshBlastTemplateHighlights/
        // UpdateBlastTemplateFx)는 블라스트 템플릿이 하나도 없어도 매
        // 프레임 무조건 불린다(BoardInputController.RunFrame) — 그래서
        // 임시 컬렉션을 그때그때 new로 만들지 않고 필드로 재사용한다
        // (사용자 보고, 2026-09-06 — 렉 심해짐, Document/profiler.png).
        private const float BlastTemplateRadiusMm = GameConstants.MmPerInch * 5f / 2f;

        private readonly List<Base> _blastHighlightedPrimary = new List<Base>();
        private readonly List<Base> _blastHighlightedSecondary = new List<Base>();
        private readonly List<Vector2> _blastTemplateCentersScratch = new List<Vector2>();
        private readonly HashSet<Base> _blastPrimariesScratch = new HashSet<Base>();
        private readonly HashSet<Unit> _blastPrimaryUnitsScratch = new HashSet<Unit>();

        /// <summary>BoardInputController.RunFrame이 매 프레임 부른다(배치/드래그
        /// 여부와 무관하게 — 정지해 있는 블라스트 템플릿도 다른 유닛이 그
        /// 범위로 걸어 들어오면 강조가 갱신돼야 하므로). 배치 미리보기(고스트)도
        /// 포함한다 — 실제로 놓기 전부터 무엇이 걸리는지 보여주는 게 이
        /// 기능의 목적이라(사용자 설명: "범위 안에 있는 유닛들을 강조").</summary>
        internal void RefreshBlastTemplateHighlights()
        {
            foreach (var b in _blastHighlightedPrimary)
            {
                if (b)
                {
                    b.BlastPrimary = false;
                }
            }
            foreach (var b in _blastHighlightedSecondary)
            {
                if (b)
                {
                    b.BlastSecondary = false;
                }
            }
            _blastHighlightedPrimary.Clear();
            _blastHighlightedSecondary.Clear();

            if (baseLayer == null)
            {
                return;
            }

            _blastTemplateCentersScratch.Clear();
            foreach (var markerGo in EnumerateRealMarkers())
            {
                if (markerGo.TryGetComponent<IconMarker>(out var icon) && icon.Kind == "blast")
                {
                    _blastTemplateCentersScratch.Add(icon.Center);
                }
            }
            if (_markerPlacementPreview is IconMarker previewIcon && previewIcon.Kind == "blast")
            {
                _blastTemplateCentersScratch.Add(previewIcon.Center);
            }
            if (_blastTemplateCentersScratch.Count == 0)
            {
                return;
            }

            // 1단계: 원의 중심이 정확히 그 모델의 타원 안에 들어가는 모델을
            // "주 목표"로, 그 모델이 속한 유닛을 "주 목표 유닛"으로 삼는다
            // (스냅된 자리라면 항상 여기 걸린다 — ResolveBlastTemplateCenter/
            // FindBaseAtPoint 참고).
            _blastPrimariesScratch.Clear();
            _blastPrimaryUnitsScratch.Clear();
            foreach (var center in _blastTemplateCentersScratch)
            {
                var primary = FindBaseAtPoint(center);
                if (primary != null)
                {
                    _blastPrimariesScratch.Add(primary);
                    if (primary.Unit != null)
                    {
                        _blastPrimaryUnitsScratch.Add(primary.Unit);
                    }
                }
            }
            foreach (var primary in _blastPrimariesScratch)
            {
                primary.BlastPrimary = true;
                _blastHighlightedPrimary.Add(primary);
            }

            // 2단계: 주 목표 자신을 뺀 나머지 모델 중, 베이스 일부라도 범위
            // 원과 겹치는 모델을 찾는다. EllipseOffsetPolygonAt(반경만큼
            // 바깥으로 부풀린 타원)에 원의 중심점이 들어있는지로 판정하면
            // "타원 테두리까지의 거리 <= 반경"과 같은 뜻이 된다 —
            // RangeOverlay가 사거리 다각형을 그릴 때 쓰는 것과 같은 근사.
            // 겹치는 모델이 주 목표와 같은 유닛 소속이면 주 목표와 똑같이
            // 흰색(BlastPrimary)으로, 다른 유닛이면 주황색(BlastSecondary)으로
            // 표시한다(사용자 요청, 2026-09-06).
            for (int i = 0; i < baseLayer.childCount; i++)
            {
                if (!baseLayer.GetChild(i).TryGetComponent<Base>(out var model) || _blastPrimariesScratch.Contains(model))
                {
                    continue;
                }
                var offsetPolygon = EllipseMath.EllipseOffsetPolygonAt(
                        model.Center, model.SizeMm, model.RotationRadians, BlastTemplateRadiusMm);
                bool inRange = false;
                foreach (var center in _blastTemplateCentersScratch)
                {
                    if (EllipseMath.PointInConvexPolygon(center, offsetPolygon))
                    {
                        inRange = true;
                        break;
                    }
                }
                if (!inRange)
                {
                    continue;
                }
                if (model.Unit != null && _blastPrimaryUnitsScratch.Contains(model.Unit))
                {
                    model.BlastPrimary = true;
                    _blastHighlightedPrimary.Add(model);
                }
                else
                {
                    model.BlastSecondary = true;
                    _blastHighlightedSecondary.Add(model);
                }
            }
        }

        // ── 블라스트 템플릿 펄스 원 FX ───────────────────────────────────
        // 사용자 요청(2026-09-06, 실사 폭발 이미지 대신 도형적인 연출로) —
        // BT가 깔려있는 동안 그 자리에서 반복적으로 채워진 원이 커지며
        // 옅어지는 펄스를 재생한다. 타이밍/커브는 BlastFxSettings 자산에서
        // 읽는다(EmoteScaleCurveSettings와 같은 이유 — BoardManager는 코드로
        // AddComponent되는 컴포넌트라 [SerializeField]로는 편집한 값이 저장될
        // 자리가 없다). 순수 시각 효과라 이모트와 같은 이유로 되돌리기/저장/
        // 네트워크 동기화 대상이 아니다 — 각 클라이언트가 "지금 blast 마커가
        // 있다"는 이미 동기화된 사실만 보고 완전히 로컬로 재생한다.

        private float _blastFxSpawnIntervalSeconds = 0.3f;
        private float _blastFxLifetimeSeconds = 0.3f;
        private float _blastFxMaxScaleMultiplier = 1.1f;
        private AnimationCurve _blastFxSizeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        private AnimationCurve _blastFxAlphaCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        private bool _blastFxSettingsLoaded;

        private sealed class BlastPulseInstance
        {
            public BlastPulseFx Fx;
            public MarkerBase Marker;
            public float SpawnTime;
        }
        private readonly List<BlastPulseInstance> _blastPulses = new List<BlastPulseInstance>();

        // 오브젝트 풀 — 처음엔 하위 캔버스로 격리만 하면 될 줄 알았는데
        // 여전히 스폰마다 렉이 났다(사용자 보고, 2026-09-06) — Instantiate/
        // Destroy 자체가 uGUI에서 캔버스 격리와 별개로 비싸다(CanvasRenderer
        // 네이티브 리소스 할당/해제, GC). 그래서 새로 만들지 않고 다 쓴
        // 펄스를 비활성화만 해뒀다가 재사용한다 — 이러면 실제 Instantiate는
        // (동시에 필요했던 최대 개수만큼) 처음 몇 번만 일어나고 그 뒤로는
        // 전혀 없다.
        private readonly Stack<BlastPulseFx> _blastPulsePool = new Stack<BlastPulseFx>();

        // 마커 인스턴스별로 "다음 펄스를 언제 새로 만들지"를 들고 있는다 —
        // 마커가 사라지면(삭제/씬 전환) 다음 갱신 때 자연히 걸러진다(아래
        // 정리 루프). liveBlastMarkers/staleBlastFxTimers는 매 프레임 재사용하는
        // 스크래치 컬렉션(위 주석 참고).
        private readonly Dictionary<MarkerBase, float> _blastFxNextSpawnTime = new Dictionary<MarkerBase, float>();
        private readonly HashSet<MarkerBase> _liveBlastMarkersScratch = new HashSet<MarkerBase>();
        private readonly List<MarkerBase> _staleBlastFxTimersScratch = new List<MarkerBase>();

        /// <summary>펄스들을 담는 전용 하위 캔버스 — markerLayer의 첫 자식으로
        /// 딱 한 번만 만든다. 펄스가 0.3초 간격으로 계속 생성/소멸하며 매번
        /// SetVerticesDirty를 요구하는데, 별도 Canvas 컴포넌트 없이 그냥
        /// markerLayer 밑에 바로 매달면 이 변화가 markerLayer가 속한 메인
        /// 캔버스 전체(유닛/마커/라벨 전부)의 배치를 매번 다시 계산하게
        /// 만든다 — 중첩 Canvas는 유니티 UI가 배치를 독립적으로 다시 계산하는
        /// 단위라, 펄스 쪽 변화가 나머지 보드에 전혀 영향을 안 주게 격리해준다
        /// (사용자 보고, 2026-09-06 — 렉 심해짐. Document/profiler.png에서
        /// GfxDeviceD3D12.WaitForLastPresentation.WaitForGPU가 프레임 시간의
        /// 88% 이상을 차지하는 게 확인됨 — 매 스폰마다 SetAsFirstSibling으로
        /// markerLayer 전체를 재정렬하던 것도 같은 이유로 없앴다, 이젠 이
        /// 레이어를 만들 때 딱 한 번만 순서를 정한다). 레이캐스트 대상이
        /// 없으므로 GraphicRaycaster는 안 붙인다.</summary>
        private RectTransform _blastPulseLayer;

        private RectTransform EnsureBlastPulseLayer()
        {
            if (_blastPulseLayer != null)
            {
                return _blastPulseLayer;
            }
            var go = new GameObject("BlastPulseLayer", typeof(RectTransform));
            go.transform.SetParent(markerLayer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling(); // markerLayer 안의 다른 마커들보다 아래 — 여기서 딱 한 번만.
            go.AddComponent<Canvas>();
            _blastPulseLayer = rect;
            return _blastPulseLayer;
        }

        /// <summary>BoardInputController.RunFrame이 매 프레임 부른다. 배치
        /// 미리보기(고스트)는 대상이 아니다 — 마우스를 따라다니는 반투명
        /// 고스트에까지 펄스가 겹치면 배치 중 산만해 보일 수 있어서(사용자
        /// 설명 범위 밖의 판단, 필요하면 나중에 켜면 됨), 실제로 놓인
        /// 마커만 펄스를 재생한다.</summary>
        internal void UpdateBlastTemplateFx()
        {
            if (!_blastFxSettingsLoaded)
            {
                _blastFxSettingsLoaded = true;
                var settings = Resources.Load<BlastFxSettings>("BlastFxSettings");
                if (settings != null)
                {
                    _blastFxSpawnIntervalSeconds = settings.spawnIntervalSeconds;
                    _blastFxLifetimeSeconds = settings.lifetimeSeconds;
                    _blastFxMaxScaleMultiplier = settings.maxScaleMultiplier;
                    _blastFxSizeCurve = settings.sizeCurve;
                    _blastFxAlphaCurve = settings.alphaCurve;
                }
            }

            _liveBlastMarkersScratch.Clear();
            if (markerLayer != null)
            {
                foreach (var markerGo in EnumerateRealMarkers())
                {
                    if (markerGo.TryGetComponent<IconMarker>(out var icon) && icon.Kind == "blast")
                    {
                        _liveBlastMarkersScratch.Add(icon);
                    }
                }
            }

            // 사라진 마커의 예약 타이머는 정리한다(안 그러면 딕셔너리가
            // 계속 자라난다).
            if (_blastFxNextSpawnTime.Count > 0)
            {
                _staleBlastFxTimersScratch.Clear();
                foreach (var kv in _blastFxNextSpawnTime)
                {
                    if (kv.Key == null || !_liveBlastMarkersScratch.Contains(kv.Key))
                    {
                        _staleBlastFxTimersScratch.Add(kv.Key);
                    }
                }
                foreach (var marker in _staleBlastFxTimersScratch)
                {
                    _blastFxNextSpawnTime.Remove(marker);
                }
            }

            foreach (var marker in _liveBlastMarkersScratch)
            {
                if (!_blastFxNextSpawnTime.TryGetValue(marker, out var nextSpawn))
                {
                    // 처음 보는(막 놓인) 마커는 곧바로 첫 펄스를 띄운다.
                    nextSpawn = Time.time;
                }
                if (Time.time >= nextSpawn)
                {
                    SpawnBlastPulse(marker);
                    nextSpawn = Time.time + Mathf.Max(_blastFxSpawnIntervalSeconds, 0.01f);
                }
                _blastFxNextSpawnTime[marker] = nextSpawn;
            }

            for (int i = _blastPulses.Count - 1; i >= 0; i--)
            {
                var pulse = _blastPulses[i];
                if (pulse.Fx == null)
                {
                    _blastPulses.RemoveAt(i);
                    continue;
                }
                float lifetime = Mathf.Max(_blastFxLifetimeSeconds, 0.01f);
                float t = (Time.time - pulse.SpawnTime) / lifetime;
                if (t >= 1f)
                {
                    // Destroy 대신 비활성화 후 풀에 반납 — 재부모화 없이(계속
                    // _blastPulseLayer 밑에 그대로) 끄기만 하면 되므로, 끄는
                    // 순서를 신경 쓸 필요도 없다(재부모화가 있는 풀링에서만
                    // "끄고 나서 옮기기" 순서가 중요).
                    pulse.Fx.gameObject.SetActive(false);
                    _blastPulsePool.Push(pulse.Fx);
                    _blastPulses.RemoveAt(i);
                    continue;
                }
                if (pulse.Marker != null)
                {
                    pulse.Fx.RectTransform.anchoredPosition = pulse.Marker.Center;
                }
                // sizeCurve는 절대 크기가 아니라 0(=템플릿 원래 크기)~1(=
                // maxScaleMultiplier배)로 정규화된 진행값이다(사용자 확인,
                // 2026-09-06 — "v가 0일 때 5", v가 1일 때 5"x배율"). 실제
                // 지름은 그 값으로 두 크기 사이를 보간해서 구한다.
                float baseDiameterMm = BlastTemplateRadiusMm * 2f;
                float maxDiameterMm = baseDiameterMm * _blastFxMaxScaleMultiplier;
                float diameterMm = Mathf.LerpUnclamped(baseDiameterMm, maxDiameterMm, _blastFxSizeCurve.Evaluate(t));
                float alpha = _blastFxAlphaCurve.Evaluate(t);
                pulse.Fx.SetSizeAndAlpha(diameterMm, alpha);
            }
        }

        private void SpawnBlastPulse(MarkerBase marker)
        {
            BlastPulseFx fx;
            if (_blastPulsePool.Count > 0)
            {
                fx = _blastPulsePool.Pop();
                fx.gameObject.SetActive(true);
            }
            else
            {
                var layer = EnsureBlastPulseLayer();
                var go = new GameObject("BlastPulseFx", typeof(RectTransform));
                go.transform.SetParent(layer, false);
                fx = go.AddComponent<BlastPulseFx>();
            }
            fx.color = Color.white;
            fx.RectTransform.anchoredPosition = marker.Center;
            _blastPulses.Add(new BlastPulseInstance { Fx = fx, Marker = marker, SpawnTime = Time.time });
        }
    }
}
