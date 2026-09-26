# :electric_plug: Mimic - Conduit

Client-side C# Windows application that handles the connection between the LCU and the mobile website by acting as a "passthrough" for messages. It uses [WebSockets](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API) to communicate with a central hub server that is responsible for tunneling connections to the phones. A locally stored signed JWT is used to identify the computer through its code. All messages except for the discovery/registration messages are encrypted with a key that only the mobile connection and Conduit knows, for extra security. Conduit detects the League client by querying the list of active processes periodically.

## Development

Simply opening the `MimicConduit.sln` file in [Visual Studio](https://www.visualstudio.com) should install all dependencies via NuGet and be ready to go. Packaging for release is as simple as choosing the release target and building, since Fody.Costura will automatically include the required .dlls in the resulting exe.

## Releases

GitHub Actions (`.github/workflows/conduit.yml`) builds Conduit on Windows for every push that changes it, checks that launch at startup works (`Tests/check-autostart.ps1`), and attaches `Conduit.exe` to the run as an artifact.

To publish a release:

1. Set `VERSION` in `Program.cs` to the new version, for example `2.4.0`, and commit it to master.
2. Tag that commit and push the tag: `git tag v2.4.0` and `git push origin v2.4.0`.

The workflow checks that the tag matches `VERSION`, builds, and publishes a GitHub Release with `Conduit.exe`, its SHA-256 in `Conduit.exe.sha256`, and notes listing the pull requests since the last release.

## Tests

`Tests/run.sh` runs the autopick engine against a fake League client with Mono, no Windows needed. `Tests/Replay.cs` replays a champ select recorded with `tools/lcu-dump.ps1 -Record` through autopick.

## License

The conduit component of Mimic is released under the [MIT](https://github.com/molenzwiebel/Mimic/blob/master/LICENSE) license. See the index README for more info.
