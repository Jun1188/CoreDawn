@echo off
REM Unity 에디터를 에이전트 자동화에 맞게 띄운다.
REM
REM -automated 없이 (예: Unity Hub에서) 띄우면 unity-pipeline이 경고를 낸다:
REM   "Editor is not in automated mode. Modal Pop up might break continuous command workflow."
REM 모달 창(에셋 임포트 확인, 저장 여부 묻기 등)이 뜨면 CLI 명령이 응답 없이 멈춰버리기 때문이다.
REM
REM 사용법: 프로젝트 루트에서 그냥 실행하거나, 바로가기를 만들어 두면 된다.

setlocal
set "UNITY_BIN=%LOCALAPPDATA%\Unity\bin"
if not exist "%UNITY_BIN%\unity.exe" (
    echo [오류] unity CLI를 찾지 못했습니다: %UNITY_BIN%\unity.exe
    exit /b 1
)

pushd "%~dp0.."
"%UNITY_BIN%\unity.exe" open . --args "-automated"
popd
endlocal
