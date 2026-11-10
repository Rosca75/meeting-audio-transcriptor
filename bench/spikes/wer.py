# THROWAWAY RESEARCH SPIKE (phase 1 candidate benchmark). Not production code.
# Measures: word error rate with the one normalisation every candidate is scored by.
# Run: python wer.py REF.txt HYP.txt   (prints S D I N WER)
#
# Normalisation (to be re-implemented identically in C# later):
#   1. Unicode NFKC.
#   2. Lower-case (str.lower()).
#   3. Every character that is not a Unicode letter or digit (categories L* and N*... digits
#      = Nd only is NOT used: str.isalnum() is used, i.e. letters and any numeric char) becomes a space.
#      Apostrophes and hyphens are therefore word separators ("l'homme" -> "l homme").
#   4. Collapse whitespace; split on spaces.
#   No number normalisation, no filler removal.
# WER = (S + D + I) / N, word-level Levenshtein alignment (unit costs), N = reference words.
# Micro-average over a group = sum of errors / sum of reference words.
import sys
import unicodedata


def normalise(text: str) -> list[str]:
    t = unicodedata.normalize("NFKC", text).lower()
    t = "".join(c if c.isalnum() else " " for c in t)
    return t.split()


def align(ref: list[str], hyp: list[str]):
    """Return (S, D, I) from a minimum-edit-distance alignment (row-wise DP with backtrace counts)."""
    n, m = len(ref), len(hyp)
    # dp[j] = (cost, S, D, I) for the current row; ties prefer substitution/match, then deletion.
    prev = [(j, 0, 0, j) for j in range(m + 1)]
    for i in range(1, n + 1):
        cur = [(i, 0, i, 0)]
        r = ref[i - 1]
        for j in range(1, m + 1):
            c, s, d, ins = prev[j - 1]
            if r == hyp[j - 1]:
                best = (c, s, d, ins)
            else:
                best = (c + 1, s + 1, d, ins)
            c2, s2, d2, i2 = prev[j]          # deletion of ref word
            if c2 + 1 < best[0]:
                best = (c2 + 1, s2, d2 + 1, i2)
            c3, s3, d3, i3 = cur[j - 1]       # insertion of hyp word
            if c3 + 1 < best[0]:
                best = (c3 + 1, s3, d3, i3 + 1)
            cur.append(best)
        prev = cur
    _, s, d, i = prev[m]
    return s, d, i


def score(ref_text: str, hyp_text: str) -> dict:
    ref, hyp = normalise(ref_text), normalise(hyp_text)
    s, d, i = align(ref, hyp)
    n = len(ref)
    return {"S": s, "D": d, "I": i, "N": n, "wer": (s + d + i) / n if n else float("nan")}


def micro(rows: list[dict]) -> dict:
    s = sum(r["S"] for r in rows); d = sum(r["D"] for r in rows)
    i = sum(r["I"] for r in rows); n = sum(r["N"] for r in rows)
    return {"S": s, "D": d, "I": i, "N": n, "wer": (s + d + i) / n if n else float("nan")}


if __name__ == "__main__":
    r = score(open(sys.argv[1], encoding="utf-8").read(), open(sys.argv[2], encoding="utf-8").read())
    print(r)
