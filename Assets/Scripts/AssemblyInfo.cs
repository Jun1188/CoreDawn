using System.Runtime.CompilerServices;

// 테스트가 게임 계층의 internal 멤버에 접근할 수 있게 — 같은 어셈블리(Assembly-CSharp)였을 때의 접근 유지.
[assembly: InternalsVisibleTo("CoreDawn.Tests")]
[assembly: InternalsVisibleTo("CoreDawn.Tests.Editor")]
