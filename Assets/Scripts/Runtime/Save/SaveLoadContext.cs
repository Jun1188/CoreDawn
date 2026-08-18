using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로드가 진행 중이라는 사실을 씬 전체에 알리는 전역 신호.
///
/// 필요한 이유: 이 프로젝트의 여러 시스템이 Awake/Start에서 무조건 초기 상태를 만든다 —
/// 플레이어에게 시작 아이템을 주고(PlayerInventoryHolder), 상자에 내용물을 채우고(Inventory),
/// 코어를 자동 설치하고(FactoryBootstrap), 둥지가 보스를 스폰한다(MonsterNest).
/// 세이브를 불러오는 중이라면 이것들이 전부 중복·오염이 되므로 각 지점에서 이 플래그를 보고 건너뛴다.
///
/// 씬 로드 → Awake/Start(시딩 억제) → 한 프레임 뒤 복원 순서로 진행되며,
/// 복원이 끝나면 <see cref="Finish"/>가 플래그를 내린다.
/// </summary>
public static class SaveLoadContext
{
    /// <summary>지금 세이브를 복원하는 중인가. 시딩·자동생성 코드는 이게 true면 아무것도 하지 않아야 한다.</summary>
    public static bool IsRestoring { get; private set; }

    /// <summary>씬 로드가 끝나면 적용할 세이브. 씬 전환을 넘어 살아남아야 해서 static에 둔다.</summary>
    public static SaveFile Pending { get; private set; }

    /// <summary>복원 중 GUID → 씬 인스턴스. 둥지가 자기 보스를 다시 찾는 등 소수의 상호 참조 재연결용.</summary>
    public static readonly Dictionary<string, Object> ByGuid = new();

    public static void BeginRestore(SaveFile file)
    {
        Pending = file;
        IsRestoring = true;
        ByGuid.Clear();
    }

    public static void Finish()
    {
        Pending = null;
        IsRestoring = false;
        ByGuid.Clear();
    }

    public static void Register(string guid, Object obj)
    {
        if (!string.IsNullOrEmpty(guid) && obj != null) ByGuid[guid] = obj;
    }

    public static T Resolve<T>(string guid) where T : Object
        => !string.IsNullOrEmpty(guid) && ByGuid.TryGetValue(guid, out var o) ? o as T : null;

    /// <summary>
    /// 도메인 리로드를 끈 환경(Enter Play Mode Options)에서는 static이 플레이를 넘어 살아남는다.
    /// 이전 플레이가 복원 도중 중단됐다면 플래그가 켜진 채로 남아 새 플레이의 시딩을 통째로 막는다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Pending = null;
        IsRestoring = false;
        ByGuid.Clear();
    }
}
