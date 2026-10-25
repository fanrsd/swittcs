# Swittcs

Mechanical-keyboard sounds for every keystroke on Windows. A small .NET 8 tray app: pick a switch, set the volume, type.

- **Sound**: 14 switch profiles, volume up to 300% (a soft limiter rounds off peaks instead of hard-clipping), on/off.
- **Typing test**: 15/30/60 s timed test with English or Indonesian words, live WPM and accuracy.
- Clicking the tray icon opens the window; closing it keeps the app running in the tray.
- Global keyboard hook (`WH_KEYBOARD_LL`), sound through an NAudio/WASAPI mixer. Nothing is recorded or sent anywhere: the hook only picks which sample to play.

## Build

Needs Windows and the .NET 8 SDK.

```powershell
dotnet run --project client
dotnet publish client -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Sound profiles

Profiles live in `Assets/Sounds/<name>/` and are embedded into `Swittcs.dll`, so an install has no loose audio folder.
Each file is named `{group}_{down|up}_NN.wav`:

- `group` is one of `alpha`, `number`, `space`, `enter`, `backspace`, `tab`, `arrow`, `modifier`, `escape`, `function`, `caps_lock`, `shift`, `control`, `option` (Alt), `command` (Win).
- Several files for the same group and phase play in turn (round-robin).
- A missing group falls back: `escape`, `function`, `caps_lock`, `shift`, `control`, `option` and `command` use `modifier`; everything else uses `alpha`.

To add a profile, drop a folder in `Assets/Sounds/` and rebuild. Only add recordings whose license allows redistribution, and list them in `Assets/Sounds/THIRD_PARTY_NOTICES.txt`.

## License

Code: [MIT](LICENSE). The bundled sound packs come from [Mechvibes](https://github.com/hainguyents13/mechvibes) and [kbsim](https://github.com/tplai/kbsim), both MIT; see `Assets/Sounds/THIRD_PARTY_NOTICES.txt`.
