using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    /// <summary>출구 하나의 상태. 화면은 이 셋을 글자가 아니라 그림으로 구분한다 (SCR-07).</summary>
    public enum OutletState
    {
        /// <summary>허용 목록 없음 — 지정되지 않은 아이템들이 라운드로빈으로 흐른다.</summary>
        All,
        /// <summary>허용 목록 있음 — 목록에 든 아이템만 흐른다.</summary>
        Only,
        /// <summary>막힘 — 아무것도 보내지 않고 다음 출구로 넘긴다.</summary>
        Blocked,
    }
}
