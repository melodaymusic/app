# Meloday Engine vocabulary

The Meloday Engine describes songs in words (mood, energy, genre, instruments, vocals and moments) from their sound alone, and finds songs by description ("rainy day jazz"). Code: `src/MelodayMusic.Core/Audio/SoundVocabulary.cs` and `src/MelodayMusic.App/Services/MelodayEngine.cs`.

## How it works

ME's model is CLAP (LAION's music checkpoint). CLAP was trained on audio and text together, so a song's 512-number fingerprint
and a phrase's text vector live in one space: the closer they point, the better the words fit the sound. ME has only ever used
the audio half. This feature adds the text half, mostly ahead of time:

1. **Vocabulary (offline).** `tools/engine/vocabulary/vocabulary.json` is a curated list of 187 words in nine groups (mood, energy,
   genre, style, era, instruments, vocals, texture, moments) with about 1,370 aliases, the everyday words and phrases search maps
   onto them (`chill`, `bangers`, `3am`, `no vocals`, `k-pop`…). Mood words use Plex's (AllMusic) mood names so they can stand in
   for missing Plex moods; energy words carry a mix energy band (Low, Medium, High). Era is marked `"describe": false`: search
   uses it, but the Sound Fingerprint never labels a song with a decade.
2. **Text vectors (offline).** `build.ps1` downloads the CLAP text encoder that matches ME's audio model (same checkpoint, same
   pinned ONNX conversion; ~500 MB, SHA-256 checked, cached in the git-ignored `model/` folder), tokenises each word's prompts
   with a C# copy of RoBERTa's tokenizer (checked token for token against Hugging Face `tokenizers`), and averages each word's
   prompt vectors ("melancholy music", "a melancholy song", "this song sounds melancholy").
3. **The asset.** The result is `src/MelodayMusic.App/Assets/Engine/descriptors.json` (≈350 KB): one normalised vector per word plus
   the hashes of the text model, tokenizer, checkpoint and vocabulary it came from. The app refuses it unless its SHA-256 is the
   one pinned in `SoundVocabulary.AssetHash`. Rebuilding is reproducible: the same source gives the same file, byte for byte.
4. **Describing a song (in the app).** For each group, the song's fingerprint is compared with each word (cosine), calibrated,
   and turned into confidences with a softmax within the group (sharpness 30). The top three words per group are kept.

### Calibration

Raw CLAP prompts lean toward music in general by different amounts: uncalibrated, about seven songs in ten "sound" like
*Falsetto*. So each word is measured against the listener's own library: its average score over the analysed songs (up to 4,000,
evenly picked) is subtracted, relative to its group's average, scaled by the group's `calibration` (0 = raw, 1 = fully centred)
and by how much there is to go on (n / (n + 20) songs). The strengths were tuned on 12,000 analysed songs: genre stays almost raw
(0.25; it agrees with Plex's genre best that way, top three matching 65% of the time), vocals are centred hardest (0.75), the rest
sit at 0.5. ME relearns the calibration when the library's analysed songs change by a twentieth.

A group whose top word reaches 35% is said outright; below that it is only a lean (shown grey).

### Which words made it

The first 112 words were tuned by hand. Every word after them was kept only once it ranked a 39,000-song library well against
that library's own data: genre tags for styles (scored by AUC, the chance a tagged song ranks above an untagged one; the
original genre words score a median of 0.78), release years for decades, song titles for moments like Christmas, and related
genres as a rougher check for instruments and textures. A word also had to rank songs differently from every existing word
(correlation under about 0.96). Words that failed but have an honest stand-in became its aliases (k-pop → Pop, smooth jazz →
Jazz, groovy → Funk, spacey → Hypnotic); the rest were left out. CLAP can't tell the 2000s, 2010s and 2020s apart in a mostly
recent library, and most voice qualities (auto-tune, raspy, deep) didn't hold up, so none of those are words.

### Description search

`SoundVocabulary.Query` reads a search into the words ME knows: labels and aliases, longest match first, ignoring case, accents,
apostrophes, hyphens and filler such as "music" or "for the", and forgiving word forms ("chilling", "rainy days"). "Not", "no",
"without" and the like flip the next word ("chill, not sad"). Every analysed song is scored by each word's calibrated z-score (how
far above the library's usual for that word, in its own spread), averaged so a broad word can't drown out a specific one, with a
flipped word pulling away. The Search page shows the best twelve as **Sounds Like Rainy Day · Jazz**.

### Words outside the vocabulary

Runs of unknown words ("sea shanty", "walking in the city") go to the text side in the app: `text-w8.onnx`, the same text model
with its weights stored in 8 bits, made from the full model by `quantize.py` and restored with the other models by
`tools/engine/download.ps1` (deterministic; pinned by SHA-256). Each run is read as three captions ("sea shanty music", "a sea
shanty song", "the sound of sea shanty"), because CLAP reads a bare phrase as speech, and becomes one more search word, calibrated
on the library being searched. It agrees with the full model to a cosine of 0.996 or better on every vocabulary prompt, keeps
about 95% of a phrase's top 50 songs, and reads a phrase in about 30 ms. ME stays out (leaving the search to Plex) when a word is
spelled out in more than three word pieces or has no vowel ("xqzt"), when more than six words are unknown, and when the search
names an artist, album, song or playlist Plex found.

## Where it shows

- **Sound Fingerprint** (track menu): a **What ME Hears** section, one row per group, each word with its confidence.
- **Search**: the **Sounds Like** section described above.
- **Mixes**: a niche mix with an energy band uses the opening's tempo where ME found a steady beat and the energy ME hears
  otherwise. A niche mix with a mood favours songs ME hears in that mood when Plex has no mood for them, and slightly
  disfavours songs ME clearly hears in another.

## Limits

Zero-shot words are only as good as CLAP's training captions; free-form phrases even more so ("cyberpunk" doesn't land). Common sounds are reliable (energy, dance music, rap, piano,
synthesizers, Latin); rarer or easily confused ones are weaker (plucked guitars can read as banjo or accordion). Words CLAP
confuses with commoner sounds got prompts that carry their usual context, which helped markedly; the rest is why each word shows
its confidence and weak rows are greyed. Changing the vocabulary means rerunning `build.ps1` and updating the pinned hash.

## Licensing

The LAION-CLAP code and the `music_audioset_epoch_15_esc_90.14.pt` checkpoint (audio and text towers) are CC0-1.0, as is the
muzaiten ONNX conversion. The text tower was initialised from RoBERTa-base (MIT). The app ships derived vectors, the 8-bit text
model and the unmodified tokenizer. See `Assets/THIRD-PARTY-NOTICES.txt`.
