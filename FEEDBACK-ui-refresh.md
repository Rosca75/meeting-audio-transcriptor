# FEEDBACK: choices made while implementing the interface refresh

One numbered entry per choice the request did not settle. A working document, not authoritative.
The owner asked for an autonomous run, so every choice below was taken by the author and is open to review.

## Counts

Base at `e38df2b`: page 84, Engine 814, App 133, Shell 111, Guards 22, Cli 86, all passing.

## Choices

1. **What the gear opens.** Chose a settings panel that replaces the main view inside the same window, with a Done button. Alternative: a panel that expands below the inputs, which would need the window to grow or to scroll. Why: the window keeps one size and never scrolls.
2. **What sits behind the gear.** Language, output formats, speaker separation, speaker count, speaker names and raw files. The main view holds the recording, the output folder and Start alone, as requested. The language is therefore one click away; say so if it should come back to the main view.
3. **The flag for English.** Chose the United Kingdom flag. Alternative: the United States flag. Each flag keeps its language name as tooltip and screen-reader label.
4. **Flags and logo are drawn by the page itself** as inline SVG built by a script module, with no image file. Why: Windows does not draw flag emoji, and the package embeds only the page's scripts and styles.
5. **The logo is an original mark** (a small orange tile with a waveform), not the PwC logo. Why: the PwC logo is a trademark asset that was not supplied. Supply the file if the real logo is wanted.
6. **Text on orange.** The primary button is bright orange `#FD5108` with black text. Why: white on that orange is about 3.3 to 1, under the 4.5 to 1 the page charter requires. Alternative: white on the darker `#C23A01`.
7. **Dark scheme kept**, drawn from the same palette (the black and grey columns with the orange accents).
8. **How names reach speakers.** The name in field k goes to Speaker k, and speakers are numbered in order of first appearance, so the first name belongs to whoever speaks first. An empty field keeps `Speaker k`. Name fields appear only when a number of speakers is entered. Ids (`S1`, `S2`) do not change; the JSON `label` carries the name.
9. **Name rules.** At most 40 characters, no control character, no `<` or `>` (they would break a subtitle voice tag), no two equal names ignoring case. The host validates and the page prevents the same.
10. **The command line gets no name option** in this change. Recommended follow-up if wanted: `--speaker-names "Ann,Bob"`.
11. **"Choose a recording first."** in the output folder line stays; only the three sentences named in the request were removed.

## Deviations from the plan

## Left for the owner

