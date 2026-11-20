# AI playlists and saved song notes

Applies to Magic Music Wall **0.9.10**.

This guide covers the current AI assistant, including the song/folder picker, incremental results and early-stop option.

The assistant recommends music already in your library. It does not download songs, use Spotify, or send audio files for listening. AI is optional; ordinary playback works without it.

## Connect a provider

1. Open **Setup > AI**.
2. Choose **LM Studio**, **OpenAI**, **Claude**, **DeepSeek**, or **OpenAI-compatible**.
3. Check the API base URL. LM Studio defaults to `http://localhost:1234/v1`. Start its local server and load a chat model first. For a server on another PC, use that server's address.
4. Enter your API key if required. LM Studio often does not require one. A ChatGPT or Claude website subscription/login does not configure API access here.
5. Click **Connect / load models**, then select a model, or type its exact model ID. Loading models checks connectivity; it does not verify that every listed model supports chat.
6. Click **Save settings**.

Use a chat/instruction model, not an embedding model. For the development setup, the tested local model is `qwen3.8-27b-heretic-abliterated-uncensored`. Other models may need different context capacity or provider settings. No automatic provider fallback occurs.

Keys are encrypted using Windows protection for the current Windows account. Leaving the key blank keeps the saved key only for the same provider and endpoint. Changing either clears that association; enter the appropriate key. **Remove saved AI key** clears it when you save. Moving the app data to another Windows account may require re-entering the key.

## Ask for a playlist

1. Click **AI playlist assistant** in the player's sidebar.
2. Choose **Entire music library**, **Current queue**, a saved playlist, or **Selected songs / folders**. Click **Choose songs / folders…** to check songs or whole folders (including subfolders) from your indexed library. Use **Open** to enter a folder, **Up** to go back, or search across titles/artists/albums. The picker never browses unimported disk contents. Selected collection songs/folders are carried into the assistant when it opens.
3. Enter a request, for example:
   - `Find songs with gentle guitar and relaxing rhythms.`
   - `Choose only songs tagged with a release year in the 1980s.`
   - `Suggest quiet songs from this playlist for dinner.`
4. Set **Max songs** (1–100).
5. Leave **Stop when enough matches are found** checked for an early finish, or uncheck it to review the whole scope and keep the best matches. Click **Recommend** to review a list without changing playback.
6. Review the reasons. All results are initially selected. Select the songs you want, then choose **Add selected to queue** or **Play selected**.

**Recommend & play** explicitly requests immediate playback after a complete, successful review and replaces the queue. **Play selected** also replaces the queue. Normal player shuffle and output settings still apply. A textual request that says "play" does not bypass the Recommend preview button. Save the resulting queue as a playlist with the existing playlist controls.

The chosen scope is reviewed in batches of up to 60. Matches appear in the list after each completed batch. In early-stop mode, review ends when the requested number of distinct matches has been found (or the scope is exhausted). Full-review mode can replace earlier matches with higher-scoring results. Large libraries can take a long time and cloud providers may charge for many requests. Progress identifies the current batch. A temporary popup shows the animated activity bar and search progress. It closes when the request finishes, fails or is cancelled. Closing the popup cancels the search; partial results never automatically play. The assistant uses one page scrollbar for its controls and results. The explanation under Recommend is also boxed for readability. **Cancel** stops the request and keeps completed matches visible for manual review/queueing. Closing the window also cancels. Neither action automatically plays partial results. Playback actions are disabled while a review is running. Batch scores are AI judgments, not a calibrated measure of similarity.

The app rejects unknown song IDs, removes duplicate recommendations and rechecks availability before queueing. It cannot guarantee the model's musical judgments or interpretation of your request. If nothing matches, an empty list is valid.

## Improve the evidence

Saved file tags, MusicBrainz genres/instrument credits and existing local tempo estimates help the assistant. Use **Music constellation** to run optional sound analysis and MusicBrainz lookup to enrich your library. Tempo alone does not prove a mood or an instrument. Missing years and missing instrumentation reduce confidence. The assistant is instructed to distinguish inference from saved evidence.

## Look up and save song information

Open a song's existing information window (also available from the queue) and click **AI song notes…**. Alternatively, select exactly one recommendation and click **Song notes / web lookup**.

- **Web lookup (MusicBrainz)** opens the existing sourced lookup and review window. Accepted information is saved in the library.
- **Generate & save notes** asks the configured AI for listening notes based on the saved metadata and its knowledge. It has no live browser and has not listened to the song. It separates saved evidence, AI interpretation and unknown details.
- Reopening the notes window reads the saved notes without another API request. Generating again replaces that song's prior AI note after success. Cancel/failure preserves the existing note.

Notes include the provider, model and date. They are stored as separate JSON files in `ai-song-notes` under the app's data folder, not embedded in your audio files. Include this folder when backing up app data. Notes for songs removed from the library remain in the backup/data folder but are not used for absent songs. Notes whose title or artist changed are not reused for recommendations until regenerated. There is no automatic background scan or ongoing AI charge.

Later recommendations can use a bounded excerpt of saved AI notes, explicitly labeled unverified. These notes never replace MusicBrainz evidence or write file tags.

## Privacy and troubleshooting

Requests include your question, song IDs, titles, artists, albums, years, genres, instrument credits, saved AI-note excerpts and available tempo/beat estimates. The application does not include media paths, audio files or provider keys in the inventory. Avoid putting secrets into song tags or prompts. Cloud providers receive this data; with LM Studio, it goes to the server address you configure. Local HTTP is allowed for LM Studio; cloud/custom providers require HTTPS.

If connection fails, check the server, URL, model and key. HTTP 401/403 usually indicates access configuration; 429 may indicate provider limits. No detailed provider response body is displayed because it could echo private input. Invalid or truncated AI output is rejected; try another chat model or a simpler request. There is no silent fallback to a cloud service.

The development checks include a live LM Studio recommendation and song-note request using synthetic metadata. OpenAI, Claude and DeepSeek adapters are implemented but have not been tested with paid live accounts in this change.

Provider references: [LM Studio compatibility](https://lmstudio.ai/docs/developer/openai-compat), [OpenAI Chat Completions](https://developers.openai.com/api/reference/chat-completions/overview), [Claude Messages](https://platform.claude.com/docs/en/api/messages/create), [DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/).
