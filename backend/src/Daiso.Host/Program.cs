using Daiso.Host;

// 띄우는 순서와 표준 출력 약속은 docs/SECURITY.md "서버를 띄우는 순서", 실행 방법은 같은 폴더 README.md
var app = DaisoHost.Build(DaisoHostOptions.FromEnvironment());

await app.RunAsync().ConfigureAwait(false);
