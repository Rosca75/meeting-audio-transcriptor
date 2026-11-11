# FEEDBACK: choices made while implementing the interface refresh

One numbered entry per choice the request did not settle. A working document, not authoritative.
The owner asked for an autonomous run, so every choice below was taken by the author and is open to review.

## Counts

Base at `e38df2b`: page 84, Engine 814, App 133, Shell 111, Guards 22, Cli 86, all passing.
End on the merged tip: page 113, build tools 37, Engine 821, App 155, Shell 111, Cli 86, Guards 22, all passing.

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
12. **One new help line.** With no number of speakers entered, the names area reads "Enter the number of speakers to name them." Why: the naming feature is otherwise undiscoverable. Owner question: keep it? Recommended: keep. It is one line in the page's copy file.
13. **A name equal to another speaker's default label is refused** (for example naming speaker 1 "Speaker 2"), since two speakers would share a label in the files.
14. **The version moved beside the title** and the footer went, which freed 28 pixels of height.
15. **Three labels were shortened for width**: "Recording language", "Number of speakers", "Also write raw files".
16. **The window is 560 by 440 in both hosts**, and that is also its minimum. 560 by 420 fits the listed states, but a three-line host sentence would then scroll.
17. **Focus on the orange Start button is a black ring** (white in the dark scheme), since an orange ring vanishes on orange.
18. **The names are not recorded in the JSON settings block**; they appear as each speaker's label.
19. **File names and paths are cut in the middle** so the extension stays visible; the full text is the tooltip.

## Deviations from the plan

- The agent tool's built-in worktree isolation was refused on this machine (a path-case check), so the two batches ran in hand-made worktrees beside the repository.
- The demo transcriber writes no speaker label, so the demo window does not show names in files; the names in files are proven by the service test that reads the five written files.
- The light scheme's progress bar is 2.56 to 1 against its track, under 3 to 1 for a graphic; the stage and percentage are written as text beside it. Every text pair is above 4.5 to 1.
- With a five-file summary and a host error shown together, the file list scrolls inside itself by 9 pixels; the window itself never scrolls.

## Left for the owner

- Look at the window once on the laptop, in light and dark, and on a display scaled above 100 percent (it was measured here at 100 percent only).
- The Linux window tests run on Linux alone; the hosted CI run of this push is their proof.
- A real transcription with names on a real recording was not run in this session; the unit and service tests cover the path.
- Two empty worktrees under `.claude/worktrees` (`agent-a008d33e7b60a902a`, `agent-a2dc4efef5a3341f0`) are left from the refused isolation attempts; the session was not permitted to remove them.
- Supply the official logo file if it should replace the original mark, and say whether the command line should accept speaker names.
