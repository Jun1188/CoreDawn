# 회귀 체크리스트

기능 작업이나 리팩터 뒤에 "게임이 아직 게임인가"를 확인하는 순서. 자동 스위트 → 플레이 루프 순으로, 위에서 아래로.
기준 씬은 `Assets/Scenes/World.unity`(모든 기능이 모여 있는 검증장). 부팅은 `Boot.unity`(로딩 게이트)를 거쳐 World로 온다.

## 0. 컴파일

```
unity command recompile → recompile_status   # "failed":false, error CS 0
```

## 1. 자동 스위트 (에디트 모드, 씬 없이)

| 스위트 | 기대 | 다루는 것 |
|---|---|---|
| `NestTests.RunAll` | 4/4 | 둥지 무적·보스 사망·영구 파괴·세이브 복원 |
| `ResourceNodeTests.RunAll` | 6/6 | 공장 색인·배치 판정·채굴 주기·광맥 밖·2×2 채굴기·누적 채굴량 |
| `TurretTests.RunAll` | 9/9 | 리드 수식·사격·탄 굶기·사거리·선회·오라·지뢰·세이브·차폐 |
| `WaveTests.RunAll` | 11/11 | 점수식·출구 선택·버스트·명단·자극·진입로 무리·종료·둥지 전멸·복원 |
| `WeaponTests.RunAll` | 7/7 | 자동 재장전·연사 간격·펠릿·탄종 전환·근접·교체 취소·세이브 |
| `SaveNbtTests.RunAll` | 7/7 | NBT 왕복·생략·숫자 폭·JSON 다리·파일·옛 슬롯 거절·DeepEquals |

한 번에 돌리기(eval):

```csharp
string rep; var sb = new System.Text.StringBuilder();
foreach (var name in new[] { "SaveNbtTests", "NestTests", "ResourceNodeTests", "TurretTests", "WaveTests", "WeaponTests" }) {
    var tt = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } }).First(x => x.Name == name);
    var args = new object[] { null }; var ok = tt.GetMethod("RunAll").Invoke(null, args); rep = (string)args[0];
    sb.Append(rep.Split('\n')[0] + " || "); if (!(bool)ok) sb.Append(rep);
}
return sb.ToString();
```

모두 `SimWorld` + `Step(n)`으로 돈다(고정 20Hz). 공장 테스트의 `RunSim`은 10Hz 공장 틱 수 × 2 스텝.

## 2. 플레이 스모크 (World 씬)

`editor_play` → World가 뜰 때까지(Boot 경유, 보통 10~30초) → 확인 → **`editor_stop` 뒤 `editor_status`의 `playMode`가 `stopped`인지 반드시 확인**(멈춘 척하는 경우가 있었다 — 그 위에서 리컴파일하면 도메인 리로드로 상태가 깨진다).

- 팩 로드: 콘솔/Editor.log에 `[PackAssets] coredawn: glb 33/33 · 재질 31/31 · 아이콘 29/29 · 소리 47/47 로드`, `[ViewSchema]`·`[Interact]` 오류 0.
- 심 시계: `SimHost.Sim.TickCount`가 초당 20 증가(두 번 재서 나눈다). `SystemCount`는 효과·몬스터·플레이어·공장 + 주야(TimeManager) ≥ 5.
- 엔티티: `SimHost.Sim.Entities.Count` > 0, `SimRunner.Monsters.Monsters.Count` > 0(둥지 방어자).
- `FactoryScenarioTests`(플레이 중 컴포넌트): `new GameObject("fs").AddComponent(FactoryScenarioTests)` → Editor.log `[FactoryScenarioTests] 완료: 16/16 통과`. (`get_console_logs`는 최근 100건만 주므로 Editor.log를 grep.)

## 3. 플레이 루프 (새 게임 → 채굴 → 건설 → 밤 → 세이브/로드)

손으로 하거나 eval로 흉내 낸다. 각 단계에서 보는 것:

1. **새 게임** — `SaveManager.Instance.NewGame()` 또는 타이틀에서. World 로드, 코어 자동 설치, `[게임 시작] 1일차 낮` 로그, 오류 0.
2. **채굴** — 광맥 위 손 채굴 / 채굴기 배치: `ResourceDepositModule` 누적 채굴량 증가, 아이템이 가방(`InventoryModule.RoleMain`)에 들어온다.
3. **건설** — 벨트·제련로·보관소 배치(`PlacementBridge.Place(def, cell, pos, rot[, ports, shape])`):
   - 벨트가 돌고(모프 루프, 롤러 구간 색 정상, 경계에서 오그라들지 않음) 아이템이 흐른다.
   - E 프롬프트가 `view.interact`대로: 설비 "〈이름〉 열기", 분배기 "필터 설정", 코어 "코어에 자원 납품", 포탑 "탄약함 열기", 보관소 "보관함 열기", **벨트·합류기·채굴기는 없음**.
   - 배치 미리보기(고스트)가 유효/무효 색으로 바뀌고 설치 뒤 실물 재질로 돌아온다.
4. **밤** — `TimeManager.Instance.Cycle.Advance(Cycle.PhaseRemaining + 0.001f)`로 당기거나 기다린다. `[⚠️ 경고] 밤이 되었습니다!`, 웨이브 시작(`SimRunner.Waves.Active`), 몬스터가 스폰·이동(보간 부드러움), 포탑이 쏜다. 자동 저장 `auto_*` 슬롯 생성.
5. **세이브/로드** — `SaveManager.Instance.DebugRoundTripDiff()` → `[Save][왕복] 통과 — 모듈 7개 전부 일치`. `Save("slot_x")` → `Load("slot_x")`(Boot 경유 재로드) → `[Save] 불러오기 완료`, 다시 `DebugRoundTripDiff()` 통과, 건물 수·컨테이너·플레이어 무기 유지. 파일은 `saves/<slot>/save.nbt.gz`(v6) + `meta.json`.
6. **아침** — 웨이브 전멸 또는 시간 경과로 `[☀️ 알림] 아침이 밝았습니다!`, 건축 다시 허용.

## 4. 자주 깨지는 자리 (특성화된 것)

- 벨트 glb를 다시 내보내면 `export_glb.py`의 `morph_normal` + `glb_trim_morph_loop.py`를 거쳐야 한다(색·루프 경계).
- 팩 `view.type`/`view.interact`/`sfx` 자리 이름은 로드 때 검증된다 — 오류 로그가 곧 회귀.
- 에디터 미리보기가 직전 플레이의 `PackAssets` 정적 캐시로 "우연히" 보일 수 있다 — 리컴파일 직후 새 도메인에서 확인.
- 메모리는 에디터 프로파일러 값에 프로파일러 녹화·에디터 RT가 섞인다 — 게임 판단은 빌드 스냅샷으로.
