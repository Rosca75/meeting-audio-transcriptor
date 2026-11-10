# Speech test set

A small, fixed, reproducible speech test set (English, French, German) for
benchmarking the offline meeting-transcription engine: word error rate, speaker
diarisation, decoder coverage and long-file behaviour. Everything is built from
pinned public sources by one script; the built files are not committed.

## Rebuild

```
node bench/testset/fetch-testset.mjs            # download, verify, build; a second run is a no-op
node bench/testset/fetch-testset.mjs --verify   # hash the data folder (and the cache) against the manifest
node bench/testset/fetch-testset.mjs --long 120 --long-lang en   # extra long files on request
```

Needs Node 22 or later and `ffmpeg` (6 or later) on PATH; no package is installed.
Behind a proxy, start Node with `NODE_USE_ENV_PROXY=1` (and `NODE_EXTRA_CA_CERTS`
when the proxy re-signs TLS). Options: `--cache <dir>` (default `bench/.cache/`,
about 0.7 GB, git-ignored), `--out <dir>` (default `bench/data/`, git-ignored),
`--offline`, `--force`, and the maintainer steps `--pin` (fill the size and hash
of a new source in the lock) and `--plan` (re-choose the recipes, see below).

Files:

| File | What it is |
|---|---|
| `fetch-testset.mjs` | the builder |
| `testset.lock` | every source pinned by address, size and SHA-256; the licence and attribution of each dataset; the noise settings; the recipe of every clip and long file |
| `../data/manifest.json` | written by the build: per file its id, language, path, duration, speaker count, dataset, licence, reference and RTTM paths, bytes and SHA-256 |

A cold run (nothing cached, about 0.7 GB downloaded) takes about 2 to 5 minutes depending on the network; with a
warm cache the full build takes about 45 s on 4 cores; an up-to-date data
folder is verified in under a second.

### Determinism

Two runs produce byte-identical files (checked, also from a cold cache into
another folder). Every source is verified against its pinned hash before use;
decoding is ffmpeg to raw 16-bit samples; the WAV container (canonical 44-byte
header, no other chunk) is written by the script, not by a muxer; the silences
between utterances are listed in the lock in milliseconds, chosen once by the
script's seeded generator (mulberry32, seed 20261110) when a maintainer runs
`--plan`, so a build replays the lock and draws no random number; the noise is
ffmpeg's generator with a fixed seed; the AAC and H.264 copies use `-bitexact`
and one thread. The `.m4a`/`.mp4` bytes depend on the ffmpeg build's AAC and
x264 encoders, so a different ffmpeg can change those two files (and only those)
while the decoded audio stays equivalent; the WAV files do not depend on the
ffmpeg version as long as its FLAC and WAV decoders are exact, which they are.

## Layout of `bench/data/`

All audio is 16 kHz mono PCM16 WAV unless named otherwise.

```
clips/<lang>/<clip-id>.wav          1-2 minute clip
clips/<lang>/<clip-id>.ref.txt      reference transcript, one line, UTF-8, the dataset's own orthography
clips/<lang>/<clip-id>.rttm         ground-truth speakers (multi-speaker clips only)
long/<lang>-10min.wav (+ .ref.txt, .rttm)    meeting-style file, about 10 minutes
long/en-10min.m4a, long/en-10min.mp4         AAC-LC 64 kbit/s copies of en-10min.wav (mp4 adds a 64x64 black H.264 track)
long/en-30min.wav (+ .ref.txt, .rttm)        the 10-minute recipe looped to 30 minutes
noise/silence-60s.wav               digital silence
noise/noise-white-60s.wav           white noise at -40 dBFS RMS
noise/noise-pink-60s.wav            pink noise at -40 dBFS RMS
noise/noise-office-60s.wav          recorded office room tone (DEMAND OOFFICE, channel 1, first 60 s)
manifest.json
```

### Clips (16 clips, 5 to 6 per language)

| Language | Clips | Minutes | Contents |
|---|---|---|---|
| en | 6 | 9.1 | `en-single-01/02` (LibriSpeech, one speaker each), `en-multi-01` (LibriSpeech, 3 speakers), `en-ami-01/02` (AMI headset mix, 4 speakers, real meeting speech), `en-ami-sdm-01` (AMI distant microphone `Array1-01`, 4 speakers) |
| fr | 5 | 8.6 | `fr-single-01/02` (MLS, one speaker each), `fr-multi-01/02/03` (MLS, 2, 3 and 4 speakers) |
| de | 5 | 8.2 | `de-single-01/02` (MLS, one speaker each), `de-multi-01/02/03` (MLS, 2, 3 and 4 speakers) |

The constructed multi-speaker clips (`*-multi-*`, `*-10min`) concatenate whole
utterances of different speakers in turns (1-3 utterances per turn in clips,
2-6 in the long files, speakers never repeated back to back) with gaps of
0.3-1.5 s of digital silence. The RTTM has one segment per utterance, so a
segment includes the short silences the corpus keeps at the start and end of an
utterance.

### Long files

`en-10min` 603.7 s (6 speakers, 60 utterances, LibriSpeech), `fr-10min` 615.9 s
(5 speakers, 40 utterances, MLS), `de-10min` 614.0 s (6 speakers, 40 utterances,
MLS). Their utterances are disjoint from the clips of the same language.
`--long N` builds `<lang>-<N>min` by repeating the ten-minute recipe, with a one
second gap at each seam, until it reaches N minutes (it ends at the utterance
that crosses N minutes, so the file is up to one utterance longer), with the
reference and RTTM concatenated accordingly. `en-30min.wav` is built by default;
`--long 120` builds the two-hour files (about 230 MB each) when asked.

### AMI excerpts

The reference is built from the word-level manual annotation (`words/*.xml`) of
the corpus: every non-punctuation word that lies fully inside the window, from
all four speakers, in start-time order, with the annotation's punctuation and
capitalisation, and with its fillers and backchannels (`Mm-hmm`, `Uh`) and
truncated words. Overlapping speech therefore interleaves; the order of words
that overlap is by start time. Vocal sounds (laughs, coughs) are not in the
reference. RTTM segments merge consecutive words of one speaker that are at most
0.5 s apart; speaker labels are AMI's global speaker ids (`ami-FEE013`). Window
edges are the middle of a pause of at least 0.3 s found by the planner, so no
word is cut. The meetings (ES2004a, IS1009a) belong to the corpus' official
test partition.

## Sources and licences

All sources are free to redistribute and to use commercially under their
licence, and download without authentication. The lock pins each file by its
exact address (the Hugging Face files by commit `2e83e61823b4c47dcbcb1980bb88601274127609`
of `facebook/multilingual_librispeech`), size and SHA-256; the MLS archives'
hashes equal the hashes Hugging Face records for them.

| Source | Used for | Licence |
|---|---|---|
| LibriSpeech test-clean (OpenSLR 12) | English clips and long files | CC BY 4.0 |
| Multilingual LibriSpeech test (MLS), French and German, speaker archives `1406_1028`, `2154_2576`, `296_1028`, `5207_3078`, `4482_3103` (fr) and `1844_931`, `3040_2130`, `4705_6814`, `7456_6660`, `1054_869`, `7624_6902` (de) | French and German clips and long files | CC BY 4.0 (audio is LibriVox public domain) |
| AMI Meeting Corpus: headset mixes ES2004a and IS1009a, the Array1-01 channel of ES2004a, and the manual annotations 1.6.2 | English meeting excerpts | CC BY 4.0 |
| DEMAND, OOFFICE 16 kHz | `noise-office-60s.wav` | CC BY 4.0 |
| ffmpeg `anullsrc` / `anoisesrc` | silence, white and pink noise | generated, no third-party material |

### Attribution lines (for a NOTICE file)

- LibriSpeech: Vassil Panayotov, Guoguo Chen, Daniel Povey, Sanjeev Khudanpur, "LibriSpeech: an ASR corpus based on public domain audio books", ICASSP 2015, https://www.openslr.org/12, licensed CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Segments were concatenated with silence into clips.
- Multilingual LibriSpeech: Vineel Pratap, Qiantong Xu, Anuroop Sriram, Gabriel Synnaeve, Ronan Collobert, "MLS: A Large-Scale Multilingual Dataset for Speech Research", Interspeech 2020, https://www.openslr.org/94, https://huggingface.co/datasets/facebook/multilingual_librispeech, licensed CC BY 4.0. Segments were concatenated with silence into clips.
- AMI Meeting Corpus: Jean Carletta et al., "The AMI Meeting Corpus: A Pre-announcement", MLMI 2005, https://groups.inf.ed.ac.uk/ami/corpus/, licensed CC BY 4.0. Excerpts were cut from the recordings; reference text and RTTM derive from the word-level annotations.
- DEMAND: Joachim Thiemann, Nobutaka Ito, Emmanuel Vincent, "The Diverse Environments Multi-channel Acoustic Noise Database (DEMAND): A database of multichannel environmental noise recordings", Proceedings of Meetings on Acoustics 19, 2013, https://doi.org/10.5281/zenodo.1227121, licensed CC BY 4.0. The first 60 s of OOFFICE channel 1 were used unmodified.

## References and their limits

- `.ref.txt` keeps each dataset's own orthography: LibriSpeech is upper case
  without punctuation, MLS is lower case without punctuation, AMI is cased and
  punctuated. No dataset provides a separate normalised form, so there is no
  `.ref.norm.txt`; word error rate normalisation is the scorer's job.
- A multi-speaker reference is the utterances in playing order, speaker-free; the
  RTTM carries the speakers.

## Shortfalls

- **No real conversational French or German.** MLS is read audiobook speech, so
  the French and German multi-speaker material is constructed by concatenating
  different speakers' read utterances and has no fillers, backchannels, overlap
  or turn-taking dynamics. Candidates checked: SUMM-RE (French meetings, Hugging
  Face `linagora/SUMM-RE`) is CC BY-SA 4.0, a share-alike licence outside the
  allowed list; Multilingual TEDx is CC BY-NC-ND; Common Voice spontaneous
  speech is gated behind a login. VoxPopuli (CC0, spontaneous-ish parliamentary
  speech with speaker ids) exists only as roughly 950 MB parquet shards per
  split on Hugging Face (the original yearly tars are larger), which a
  dependency-free Node builder cannot read without a parquet and Opus reader, so
  it is not included; it is the first candidate if a parquet reader is added.
- English conversational speech is real (AMI, three excerpts, the third
  far-field). `en-ami-02` is 69 s because the meeting has few pauses to cut at.
- The synthetic multi-speaker files use digital-silence gaps; real meetings have
  room noise between turns. Mix `noise/` files under them if that matters.
- Only one recorded noise (office room tone) is included; no keyboard, HVAC or
  music-free babble recording from a clearly licensed source was added.
- The long-file references are the concatenated utterances of constructed
  material, so the 30- and 120-minute files repeat the same speech and are for
  throughput and memory, not for accuracy statistics.
