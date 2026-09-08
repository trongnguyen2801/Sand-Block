Run the pure authoring/validation checks with the .NET 8 SDK:

    dotnet run --project Tests/BlockAuthoring/Tests.csproj

Override `-p:UnityEditorPath=/path/to/Unity.app` if needed. No NuGet dependencies.
The harness uses the real Unity CoreModule and production authoring sources, with a
30-cell simulator constant for data-only checks. It does not simulate Unity physics,
IMGUI events, or native JSON serialization; those require Unity Play/Edit Mode.
