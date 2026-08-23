using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 우상단 안내 카드의 그리기·연출 담당.
///
/// MonoBehaviour가 아닌 이유: 이 카드는 <see cref="GameplayHUDView"/>가 이미 들고 있는
/// UIDocument 안에 산다. 컴포넌트로 만들면 GameUI_UITK.prefab을 고쳐야 하는데,
/// 씬·프리팹을 건드리지 않는 것이 이 시스템의 배치 전제다(팀원의 씬 병합과 충돌 회피).
/// 그래서 <see cref="TutorialManager"/>가 이 객체를 들고 UXML 요소만 빌려 쓴다.
///
/// <b>연출</b>: 왼쪽에서 미끄러져 들어오고, 끝나면 오른쪽으로 미끄러져 나간다. 안내가 바뀔 때는
/// 나간 뒤 <see cref="BreathSeconds"/>만큼 쉬었다가 다음 것이 들어온다 — 글자만 즉시 갈아끼우면
/// 바뀐 줄도 모르고 지나간다. USS transition이 아니라 DOTween을 쓰는 이유는 이 세 박자
/// (나감 → 쉼 → 들어옴)를 한 시퀀스로 묶어야 하기 때문이고, 프로젝트가 이미 DOTween을 쓴다.
/// </summary>
public sealed class TutorialHUD
{
    /// <summary>들어오는 데 걸리는 시간. 눈이 따라올 만큼 길되 조작을 막지는 않는 길이.</summary>
    const float EnterSeconds = 0.40f;
    const float ExitSeconds = 0.28f;

    /// <summary>안내와 안내 사이의 빈 시간. 이게 없으면 카드가 깜빡인 것처럼만 보인다.</summary>
    const float BreathSeconds = 0.55f;

    /// <summary>들어올 때 왼쪽으로 물러나 있는 거리(px).</summary>
    const float EnterFromLeft = 140f;

    /// <summary>나갈 때 오른쪽으로 밀려나는 거리(px). 카드 폭 + 여백보다 커야 화면 밖으로 사라진다.</summary>
    const float ExitToRight = 520f;

    /// <summary>
    /// 안내가 바뀌라는 지시가 떨어진 뒤 새 카드가 다 들어와 읽을 수 있게 되기까지의 시간.
    /// <see cref="TutorialManager"/>가 완료 판정을 미루는 데 쓴다 — 아직 들어오지도 않은 카드를
    /// 완료로 찍으면 플레이어는 그런 안내가 있었는지도 모른다.
    /// </summary>
    public const float LeadInSeconds = ExitSeconds + BreathSeconds + EnterSeconds;

    VisualElement _card;
    VisualElement _keys;
    Label _tag;
    Label _stepLabel;
    Label _body;

    Sequence _seq;
    bool _visible;
    float _x;

    static bool _warnedMissingElement;

    /// <summary>패널에서 떨어져 나갔으면(HUD 재활성·씬 전환) 다시 붙여야 한다.</summary>
    public bool IsBound => _card != null && _card.panel != null;

    public bool TryBind()
    {
        var view = Object.FindFirstObjectByType<GameplayHUDView>(FindObjectsInactive.Include);
        if (view == null) return false;

        var doc = view.GetComponent<UIDocument>();
        var root = doc != null ? doc.rootVisualElement : null;
        if (root == null) return false;   // HUD가 아직 안 켜졌다 — 다음 틱에 다시

        var card = root.Q("tutorial");
        if (card == null)
        {
            if (!_warnedMissingElement)
            {
                _warnedMissingElement = true;
                Debug.LogWarning("[Tutorial] GameplayHUD.uxml에 name=\"tutorial\" 요소가 없습니다 — 안내 카드가 뜨지 않습니다.");
            }
            return false;
        }

        // 새 요소에 붙는 것이므로 옛 요소를 물고 있던 트윈은 여기서 끊는다
        Kill();

        _card = card;
        _tag = _card.Q<Label>("tut-tag");
        _stepLabel = _card.Q<Label>("tut-step");
        _body = _card.Q<Label>("tut-body");
        _keys = _card.Q("tut-keys");

        _visible = false;
        _card.style.display = DisplayStyle.None;
        return true;
    }

    /// <summary>
    /// 지금 떠 있는 카드를 내보내고, 한 박자 쉰 뒤 <paramref name="next"/>를 들여보낸다.
    /// <paramref name="next"/>가 null이면 내보내기만 한다(튜토리얼 종료).
    /// </summary>
    public void PlayTransition(TutorialStepSO next, int index, int total)
    {
        if (!IsBound) return;

        Kill();

        // 일시정지(timeScale 0) 중에도 흘러야 한다 — 정지 화면에서 카드가 반쯤 걸린 채 멈추면 고장으로 보인다
        _seq = DOTween.Sequence().SetUpdate(true);

        if (_visible)
        {
            _seq.Append(TweenX(ExitToRight, ExitSeconds).SetEase(Ease.InCubic));
            _seq.Join(TweenOpacity(0f, ExitSeconds));
            _seq.AppendCallback(() =>
            {
                if (!IsBound) return;
                _card.style.display = DisplayStyle.None;
                _visible = false;
            });
        }

        _seq.AppendInterval(BreathSeconds);

        if (next == null) return;

        _seq.AppendCallback(() =>
        {
            if (!IsBound) return;
            ApplyContent(next, index, total);

            _x = -EnterFromLeft;
            ApplyX();
            _card.style.opacity = 0f;
            _card.style.display = DisplayStyle.Flex;
            _visible = true;
        });

        // 시작값(-EnterFromLeft, opacity 0)은 바로 위 콜백이 넣는다. DOTween은 시퀀스에서
        // 자기 차례가 왔을 때 getter를 읽어 시작값을 잡으므로 콜백 → 트윈 순서가 보장된다.
        // (단 Goto로 시간을 건너뛰면 콜백이 생략되니, 그렇게 조사할 때는 방향이 달라 보인다)
        _seq.Append(TweenX(0f, EnterSeconds).SetEase(Ease.OutCubic));
        _seq.Join(TweenOpacity(1f, EnterSeconds));
    }

    /// <summary>연출 없이 즉시 감춘다 — 씬 전환·강제 종료용.</summary>
    public void HideImmediate()
    {
        Kill();
        if (!IsBound) return;

        _card.style.display = DisplayStyle.None;
        _visible = false;
    }

    public void Kill()
    {
        if (_seq != null) { _seq.Kill(); _seq = null; }
    }

    // ───────────────────────── 내부 ─────────────────────────

    Tweener TweenX(float target, float duration)
        => DOTween.To(() => _x, v => { _x = v; ApplyX(); }, target, duration).SetUpdate(true);

    Tweener TweenOpacity(float target, float duration)
        => DOTween.To(() => IsBound ? _card.resolvedStyle.opacity : 0f,
                      v => { if (IsBound) _card.style.opacity = v; },
                      target, duration).SetUpdate(true);

    void ApplyX()
    {
        if (IsBound) _card.style.translate = new Translate(_x, 0f);
    }

    /// <param name="index">1부터 시작하는 현재 스텝 번호.</param>
    void ApplyContent(TutorialStepSO step, int index, int total)
    {
        if (_tag != null) _tag.text = string.IsNullOrEmpty(step.tag) ? "GUIDE" : step.tag;
        if (_stepLabel != null) _stepLabel.text = total > 0 ? $"{index}/{total}" : "";
        if (_body != null) _body.text = step.body ?? "";
        RebuildKeys(step.keyHints);
    }

    /// <summary>
    /// 키캡은 기존 <c>.ui-key__cap</c>(키의 물리적 두께까지 표현된 컴포넌트)을 그대로 쓴다 —
    /// 튜토리얼 전용 키 스타일을 새로 만들면 같은 것이 화면에 두 벌 생긴다.
    /// </summary>
    void RebuildKeys(string[] hints)
    {
        if (_keys == null) return;

        _keys.Clear();

        bool any = hints != null && hints.Length > 0;
        _keys.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        if (!any) return;

        foreach (var h in hints)
        {
            if (string.IsNullOrWhiteSpace(h)) continue;
            var cap = new Label(h) { pickingMode = PickingMode.Ignore };
            cap.AddToClassList("ui-key__cap");
            _keys.Add(cap);
        }
    }
}
