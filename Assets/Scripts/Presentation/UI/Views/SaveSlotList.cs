using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using CoreDawn.Save;

namespace CoreDawn.UI
{
    /// <summary>
    /// 세이브 슬롯 목록을 그리고 선택을 관리하는 부품.
    ///
    /// 두 곳에서 같은 목록이 필요하다: 게임 안의 일시정지 메뉴(SaveLoadPanelView)와
    /// 타이틀 화면(TitleScreenView). 타이틀 씬에는 InputManager가 없어 UITKPopup을 쓸 수 없으므로
    /// 두 화면의 구조가 다를 수밖에 없는데, 목록의 생김새와 규칙까지 갈라지면
    /// 한쪽만 고쳐지는 일이 생긴다. 그 부분만 여기로 모았다.
    /// </summary>
    public class SaveSlotList
    {
        readonly List<VisualElement> _rows = new();

        /// <summary>지금 고른 슬롯 id. 아무것도 안 골랐으면 null.</summary>
        public string Selected { get; private set; }

        /// <summary>선택이 바뀔 때마다 발화 — 화면이 실행 버튼의 활성 상태를 갱신한다.</summary>
        public event Action SelectionChanged;

        public void Clear()
        {
            _rows.Clear();
            Selected = null;
        }

        /// <summary>
        /// 컨테이너를 비우고 슬롯 줄을 다시 채운다.
        /// </summary>
        /// <param name="saveMode">
        /// true면 저장용 — 손으로 만든 슬롯만 고를 수 있다(빈 칸 포함, 자동 저장 제외).
        /// false면 불러오기용 — 내용이 있는 슬롯만 고를 수 있다(자동 저장 포함).
        /// 자동 저장을 손으로 덮어쓰지 못하게 막는 이유는 그것이 "직전으로 돌아갈 곳"이기 때문이다.
        /// </param>
        /// <returns>고를 수 있는 슬롯 수. 0이면 화면이 안내 문구를 띄운다.</returns>
        public int Rebuild(VisualElement container, bool saveMode)
        {
            if (container == null) return 0;

            container.Clear();
            _rows.Clear();

            var manager = SaveManager.Instance;
            if (manager == null) return 0;

            int usable = 0;

            foreach (var (slotId, meta) in manager.ManualSlots())
                if (AddRow(container, slotId, meta, manual: true, saveMode)) usable++;

            foreach (var (slotId, meta) in manager.AutoSlots())
                if (AddRow(container, slotId, meta, manual: false, saveMode)) usable++;

            // 다시 그린 뒤에도 고른 슬롯이 여전히 존재하면 선택 표시를 유지한다
            if (Selected != null) Highlight();

            return usable;
        }

        bool AddRow(VisualElement container, string slotId, SaveMeta meta, bool manual, bool saveMode)
        {
            bool empty = meta == null || meta.IsEmpty;
            bool selectable = saveMode ? manual : !empty;

            var row = new VisualElement { userData = slotId };
            row.AddToClassList("ui-row");
            row.AddToClassList("slot-row");
            if (empty) row.AddToClassList("slot-row--empty");
            if (!selectable) row.AddToClassList("ui-row--locked");

            var left = new VisualElement();
            left.AddToClassList("slot-row__left");

            var name = new Label(manual ? $"슬롯 {Number(slotId)}" : $"자동 저장 {Number(slotId)}");
            name.AddToClassList("slot-row__name");
            left.Add(name);

            var sub = new Label(empty ? "— 비어 있음 —" : $"{meta.SceneName} · {meta.SavedAtLocalText}");
            sub.AddToClassList("slot-row__scene");
            left.Add(sub);

            row.Add(left);

            if (!empty)
            {
                var right = new VisualElement();
                right.AddToClassList("slot-row__right");

                var day = new Label($"Day {meta.DayNumber} · Tier {meta.CoreTier}");
                day.AddToClassList("slot-row__day");
                right.Add(day);

                var play = new Label(meta.PlaytimeText);
                play.AddToClassList("slot-row__time");
                right.Add(play);

                row.Add(right);
            }

            if (selectable) row.RegisterCallback<ClickEvent>(_ => Select(slotId));

            _rows.Add(row);
            container.Add(row);
            return selectable;
        }

        void Select(string slotId)
        {
            Selected = slotId;
            Highlight();
            SelectionChanged?.Invoke();
        }

        void Highlight()
        {
            foreach (var row in _rows)
                row.EnableInClassList("ui-row--selected", (string)row.userData == Selected);
        }

        /// <summary>"slot_02" → "2". 화면에는 파일 이름이 아니라 사람이 읽는 번호를 보여준다.</summary>
        static string Number(string slotId)
        {
            int i = slotId.LastIndexOf('_');
            return i >= 0 ? slotId[(i + 1)..].TrimStart('0') : slotId;
        }
    }
}
