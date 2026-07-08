# Configuration

Open **Settings** (title-bar button) to set:

| Setting            | Default                       |
|--------------------|-------------------------------|
| API key            | *(stored DPAPI-encrypted)*    |
| Base URL           | `https://api.openai.com/v1`   |
| Chat model         | `gpt-4o-mini`                 |
| System prompt      | *(assistant persona)*         |
| Speech-to-text     | `whisper-1`                   |
| Text-to-speech     | `gpt-4o-mini-tts`             |
| TTS voice          | `alloy`                       |
| Offline TTS        | off (uses Windows SAPI)       |

Settings persist to `%APPDATA%\raven_ai\settings.json`. Only the encrypted key blob is stored;
never the plaintext.
