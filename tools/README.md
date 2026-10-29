# Tools

Scripts and small programs used while developing Meloday Music. None of them ship with the app.

| Path | Purpose |
|---|---|
| `MelodayMusic.DevCli` | Command-line client for trying the Plex integration against a real server. |
| `engine/download.ps1` | Downloads the Meloday Engine's audio model and checks its SHA-256. |
| `engine/vocabulary` | Rebuilds `Assets/Engine/descriptors.json` from `vocabulary.json` with the CLAP text encoder. |
| `engine/Probe.csproj` | Times the audio model on a sample input. |
| `projectm` | Build script and C bridge for the bundled projectM visualizer. See its README. |
| `ui-smoke` | WinUI smoke tests, compiled into the app only with `-p:MelodayUiSmoke=true`. |
| `polish` | Scripts that list the app's UI surfaces and build contact sheets from screenshots. |
| `covers/prepare-genre-covers.py` | Converts genre artwork sources into `Assets/Genres`. |
| `promo` | Renders the promo video. See its README. |
| `audit-personal-credentials.ps1` | Checks the source and a publish folder for your own saved API keys before a release. |
