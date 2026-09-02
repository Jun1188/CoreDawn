using System.Runtime.CompilerServices;

// 테스트는 정의를 심 내부 통로(internal set 등)로 조립한다 — 같은 어셈블리였을 때의 접근을 유지.
[assembly: InternalsVisibleTo("CoreDawn.Tests")]
[assembly: InternalsVisibleTo("CoreDawn.Tests.Editor")]
