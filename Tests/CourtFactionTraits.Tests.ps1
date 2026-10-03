$ErrorActionPreference = 'Stop'
# Link production math into the harness; the retired four-faction matrix is no longer authoritative.
dotnet run --project (Join-Path $PSScriptRoot 'CourtRedesignHarness/CourtRedesignHarness.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Court redesign regression tests failed.' }
