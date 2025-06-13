If you change the library source (anything under the repo root other than samples, tests or documentation), also bump the <Version> in event-modeling.fsproj.
Always run `dotnet build -c Release` and `dotnet run --project tests/EventModeling.Tests/EventModeling.Tests.fsproj` before committing.
