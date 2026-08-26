<div align="center">

# CORE DAWN

### 자원을 모으고, 공장을 확장하고, 새벽까지 살아남아라.

Unity 6 기반의 **1인칭 공장 건설 × 디펜스** 게임 프로젝트

[![Unity](https://img.shields.io/badge/Unity-6000.3.18f1-000000?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/)
[![C#](https://img.shields.io/badge/C%23-Gameplay-512BD4?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![URP](https://img.shields.io/badge/URP-17.3.0-222C37?style=for-the-badge&logo=unity&logoColor=white)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/manual/index.html)
[![Status](https://img.shields.io/badge/Status-In_Development-F59E0B?style=for-the-badge)](#프로젝트-상태)

<sub>한국공학대학교 「레벨업 2026 Summer」 게임공학과 소모임 프로젝트</sub>

</div>

---

## 게임 소개

**Core Dawn**은 탐사와 자원 수집, 생산 라인 자동화, 거점 방어를 하나의 흐름으로 엮은 1인칭 게임입니다. 낮 동안 자원을 확보하고 공장을 성장시킨 뒤, 몰려오는 위협으로부터 설비와 생존 기반을 지켜내는 경험을 목표로 개발하고 있습니다.

> **Explore → Gather → Automate → Defend → Expand**

## 핵심 요소

| | 시스템 | 설명 |
|:---:|---|---|
| ⛏️ | **자원 수집** | 월드를 탐색하고 생산에 필요한 자원을 확보합니다. |
| 🏭 | **공장 자동화** | 건물을 배치하고 레시피를 연결해 생산 흐름을 구축합니다. |
| 🔫 | **1인칭 전투** | 다양한 무기와 전투 시스템으로 적의 공세에 대응합니다. |
| 🌊 | **웨이브 디펜스** | 거점과 생산 시설을 노리는 몬스터의 공격을 방어합니다. |
| 🌱 | **성장과 확장** | 아이템과 설비를 발전시키며 활동 영역을 넓혀갑니다. |

## 기술 스택

- **Engine** — Unity `6000.3.18f1` (Unity 6.3 LTS)
- **Language** — C#
- **Rendering** — Universal Render Pipeline `17.3.0`, Shader Graph
- **Input** — Unity Input System `1.19.0`
- **Core packages** — Splines, Newtonsoft Json, Unity AI Inference

## 시작하기

### 요구 사항

- [Unity Hub](https://unity.com/download)
- Unity Editor `6000.3.18f1`
- Git

### 프로젝트 실행

```bash
git clone https://github.com/Jun1188/CoreDawn.git
cd CoreDawn
```

1. Unity Hub에서 **Add → Add project from disk**를 선택합니다.
2. 내려받은 `CoreDawn` 폴더를 추가합니다.
3. Unity Editor `6000.3.18f1`로 프로젝트를 엽니다.
4. 패키지 임포트와 스크립트 컴파일이 끝날 때까지 기다립니다.
5. `Assets/Scenes/Title.unity` 씬을 열고 Play 버튼을 누릅니다.

> 다른 Unity 버전으로 열면 에셋 또는 패키지가 재임포트될 수 있으므로 동일한 에디터 버전을 권장합니다.

## 프로젝트 구조

```text
CoreDawn/
├─ Assets/
│  ├─ Art/              # 아트 리소스
│  ├─ Data/             # 건물, 아이템, 레시피, 웨이브 데이터
│  ├─ Input/            # Input System 설정
│  ├─ Prefabs/          # 플레이어, 몬스터, 건물, UI 프리팹
│  ├─ Scenes/           # 타이틀, 월드, 부트스트랩 및 테스트 씬
│  └─ Scripts/          # 에디터·런타임·테스트 코드
├─ Packages/            # Unity 패키지 의존성
└─ ProjectSettings/     # 프로젝트 및 빌드 설정
```

## 주요 씬

| 씬 | 역할 |
|---|---|
| `Assets/Scenes/Title.unity` | 게임 진입 및 타이틀 |
| `Assets/Scenes/World.unity` | 메인 월드 |
| `Assets/Scenes/Bootstrap/Systems.unity` | 공통 시스템 초기화 |
| `Assets/Scenes/Bootstrap/Combat.unity` | 전투 시스템 구성 |
| `Assets/Scenes/Bootstrap/Factory.unity` | 공장 시스템 구성 |
| `Assets/Scenes/Bootstrap/GameUI.unity` | 게임 UI 구성 |

## 프로젝트 상태

현재 **개발 진행 중**입니다. 주요 게임 시스템과 콘텐츠는 개발 과정에서 변경될 수 있습니다.

## 기여 방법

1. 작업 목적에 맞는 브랜치를 생성합니다.
2. 변경 범위를 작게 유지하고 관련 씬·프리팹·스크립트를 함께 확인합니다.
3. Unity가 생성한 `.meta` 파일을 누락하지 않습니다.
4. 변경 내용과 테스트 방법을 적어 Pull Request를 생성합니다.

---

<div align="center">

**Core Dawn** · TeamProj2606

Made with Unity

</div>

