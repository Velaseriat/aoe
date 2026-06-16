# AOE - Voice Dictation

Push-to-talk voice dictation across two Windows machines, transcribed by a self-hosted
[Speaches](https://github.com/speaches-ai/speaches) (faster-whisper) server.

- **Alpha** - your workstation (screen + keyboard). Holds the PTT key, talks to Speaches, and
  types the transcript into whatever app is focused.
- **Beta** - the audio box (mic + speakers). Captures the mic on command and streams it to Alpha.
- **miku** - GPU server (4070 Super) running Speaches for speech-to-text.

```
Hold PTT (F24) on Alpha
  -> Alpha tells Beta to record
  -> Beta streams mic audio (16 kHz mono PCM) -> Alpha
Release PTT
  -> Alpha builds a WAV, POSTs to Speaches (/v1/audio/transcriptions)
  -> transcript pasted into the focused app
```

## Projects

| Project | Runs on | Role |
|---------|---------|------|
| `Aoe.Protocol` | shared | TCP framing + control/audio message models |
| `Aoe.Beta` | Beta | mic capture (NAudio), control server, tray |
| `Aoe.Alpha` | Alpha | PTT keyboard hook, Speaches client, text injection, tray |

## Requirements

- **.NET 8 SDK** (https://dotnet.microsoft.com/download)
- **Windows** on both Alpha and Beta
- A reachable **Speaches** server (see below)

## Build

```powershell
dotnet build Aoe.sln -c Release
```

Or publish self-contained single-exe per agent:

```powershell
dotnet publish Aoe.Beta  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish Aoe.Alpha -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Configure Alpha

Edit `Aoe.Alpha/appsettings.json` (copied next to the exe):

```json
{
  "BetaHost": "192.168.1.50",
  "BetaPort": 38473,
  "SpeachesBaseUrl": "http://<miku-ip>:8000/v1",
  "SpeachesModel": "deepdml/faster-whisper-large-v3-turbo-ct2",
  "SpeachesApiKey": null,
  "Language": "en",
  "PushToTalkVk": 135,
  "InjectViaClipboard": true
}
```

- `PushToTalkVk` 135 = `0x87` = **VK_F24**. Bind your ROG Chakram joystick direction to F24 in
  Armoury Crate (hold = key down, release = key up).
- `InjectViaClipboard` true pastes via Ctrl+V (reliable); false types characters directly.

Beta listens on `AOE_PORT` if set, otherwise port 38473.

## Run

1. On **Beta**: launch `Aoe.Beta.exe` (tray shows "listening" then "connected"/"recording").
2. On **Alpha**: launch `Aoe.Alpha.exe` (tray shows "ready" when connected to Beta).
3. Hold your PTT key, speak, release. The transcript is pasted into the focused window.

Local single-machine test: set `BetaHost` to `127.0.0.1` and run both on one PC.

## Speaches on miku

```bash
# verify GPU is visible to Docker
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi

# run Speaches (CUDA)
docker run --gpus=all --name speaches -p 8000:8000 \
  -v hf-hub-cache:/home/ubuntu/.cache/huggingface/hub -d \
  ghcr.io/speaches-ai/speaches:latest-cuda

# smoke test
curl -X POST http://localhost:8000/v1/audio/transcriptions \
  -F file=@test.wav -F model=deepdml/faster-whisper-large-v3-turbo-ct2
```

Open TCP 8000 on miku and TCP 38473 on Beta in their firewalls.

## Notes & roadmap

- v1 is whole-utterance: it transcribes once on release. Streaming/partial results are a future
  enhancement (Speaches supports SSE).
- Beta keeps a speaker-playback path reserved for a future **assistant mode** (a second joystick
  direction): STT -> LLM -> TTS (`/v1/audio/speech`) -> Beta speakers.
- The original Rust prototype lives in git history at commit `ad71102`.
