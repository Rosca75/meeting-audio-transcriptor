## WER (%) per group, 28 s chunks
| cand | en-read | en-ami | fr | de | RTFx(acc run) |
|---|---|---|---|---|---|
| A | 12.63 | 39.48 | 13.65 | 13.64 | 25.6 |
| B | 4.07 | 18.20 | 6.55 | 10.54 | 21.6 |
| C | 1.58 | 36.63 | 3.79 | 8.45 | 127.1 |
| D | 2.54 | 24.00 | 5.67 | 11.59 | 23.9 |
| E | 2.35 | 28.33 | 5.85 | 9.24 | 3.1 |

## Speed runs (en-10min)
| tag | workers x intra | spin | chunk | wall s | RTFx | load s | peak RSS MB | in-run s | load1 |
|---|---|---|---|---|---|---|---|---|---|
| sp-A-1x4-c28-r1 | 1x4 | True | 28 | 41.2 | 14.6 | 3.4 | 1484 | {'mel': 1, 'enc': 37, 'dec': 3, 'dec_loop_total': 3} | 2.88 |
| sp-A-1x4-c28-r2 | 1x4 | True | 28 | 42.0 | 14.4 | 2.7 | 1461 | {'mel': 1, 'enc': 38, 'dec': 3, 'dec_loop_total': 3} | 3.41 |
| sp-A-2x2-c28-r1 | 2x2 | True | 28 | 32.9 | 18.4 | 2.4 | 1620 | {'mel': 1, 'enc': 62, 'dec': 3, 'dec_loop_total': 3} | 3.82 |
| sp-A-2x2-c28-r2 | 2x2 | True | 28 | 33.3 | 18.1 | 2.7 | 1643 | {'mel': 1, 'enc': 62, 'dec': 3, 'dec_loop_total': 3} | 3.25 |
| sp-A-4x1-c10-r1 | 4x1 | True | 10 | 22.9 | 26.3 | 2.7 | 1459 | {'mel': 1, 'enc': 86, 'dec': 3, 'dec_loop_total': 3} | 2.24 |
| sp-A-4x1-c10-r2 | 4x1 | True | 10 | 22.8 | 26.5 | 2.7 | 1438 | {'mel': 1, 'enc': 86, 'dec': 3, 'dec_loop_total': 3} | 2.45 |
| sp-A-4x1-c20-r1 | 4x1 | True | 20 | 26.7 | 22.6 | 2.8 | 1760 | {'mel': 1, 'enc': 100, 'dec': 3, 'dec_loop_total': 3} | 2.74 |
| sp-A-4x1-c20-r2 | 4x1 | True | 20 | 21.6 | 27.9 | 2.4 | 1666 | {'mel': 1, 'enc': 82, 'dec': 3, 'dec_loop_total': 3} | 3.64 |
| sp-A-4x1-c28-nospin-r1 | 4x1 | False | 28 | 29.0 | 20.8 | 2.9 | 1852 | {'mel': 1, 'enc': 109, 'dec': 4, 'dec_loop_total': 4} | 2.95 |
| sp-A-4x1-c28-nospin-r2 | 4x1 | False | 28 | 23.3 | 26.0 | 2.9 | 1815 | {'mel': 1, 'enc': 87, 'dec': 3, 'dec_loop_total': 3} | 1.8 |
| sp-A-4x1-c28-nospin-r3 | 4x1 | False | 28 | 22.8 | 26.5 | 2.8 | 1936 | {'mel': 1, 'enc': 84, 'dec': 3, 'dec_loop_total': 3} | 3.35 |
| sp-A-4x1-c28-nospin-r4 | 4x1 | False | 28 | 23.8 | 25.3 | 2.7 | 1875 | {'mel': 1, 'enc': 89, 'dec': 3, 'dec_loop_total': 3} | 3.24 |
| sp-A-4x1-c28-r1 | 4x1 | True | 28 | 27.5 | 21.9 | 2.7 | 1918 | {'mel': 1, 'enc': 102, 'dec': 3, 'dec_loop_total': 4} | 3.51 |
| sp-A-4x1-c28-r2 | 4x1 | True | 28 | 22.5 | 26.9 | 2.7 | 1911 | {'mel': 1, 'enc': 83, 'dec': 3, 'dec_loop_total': 3} | 3.85 |
| sp-A-4x1-c28-r3 | 4x1 | True | 28 | 22.7 | 26.6 | 2.8 | 1918 | {'mel': 1, 'enc': 84, 'dec': 3, 'dec_loop_total': 3} | 3.45 |
| sp-A-4x1-c28-r4 | 4x1 | True | 28 | 21.7 | 27.9 | 2.7 | 1870 | {'mel': 1, 'enc': 81, 'dec': 3, 'dec_loop_total': 3} | 3.21 |
| sp-A-8x1-c28-r1 | 8x1 | True | 28 | 23.4 | 25.9 | 2.8 | 2443 | {'mel': 2, 'enc': 166, 'dec': 5, 'dec_loop_total': 6} | 3.37 |
| sp-A-8x1-c28-r2 | 8x1 | True | 28 | 24.7 | 24.4 | 2.6 | 2470 | {'mel': 2, 'enc': 175, 'dec': 6, 'dec_loop_total': 6} | 4.19 |
| sp-B-1x4-c28-r1 | 1x4 | True | 28 | 46.8 | 12.9 | 10.4 | 2803 | {'mel': 1, 'enc': 38, 'dec': 8, 'dec_loop_total': 8} | 5.09 |
| sp-B-1x4-c28-r2 | 1x4 | True | 28 | 44.3 | 13.6 | 3.6 | 2803 | {'mel': 1, 'enc': 36, 'dec': 8, 'dec_loop_total': 8} | 1.1 |
| sp-B-2x2-c28-r1 | 2x2 | True | 28 | 53.7 | 11.2 | 3.5 | 2879 | {'mel': 1, 'enc': 95, 'dec': 11, 'dec_loop_total': 11} | 3.31 |
| sp-B-2x2-c28-r2 | 2x2 | True | 28 | 40.9 | 14.7 | 3.6 | 2875 | {'mel': 1, 'enc': 73, 'dec': 8, 'dec_loop_total': 8} | 4.62 |
| sp-B-4x1-c28-nospin-r1 | 4x1 | False | 28 | 26.9 | 22.4 | 3.7 | 2986 | {'mel': 1, 'enc': 96, 'dec': 8, 'dec_loop_total': 8} | 3.61 |
| sp-B-4x1-c28-nospin-r2 | 4x1 | False | 28 | 28.6 | 21.1 | 3.7 | 2993 | {'mel': 1, 'enc': 102, 'dec': 8, 'dec_loop_total': 8} | 3.32 |
| sp-B-4x1-c28-nospin-r3 | 4x1 | False | 28 | 35.2 | 17.2 | 3.5 | 2913 | {'mel': 1, 'enc': 127, 'dec': 10, 'dec_loop_total': 10} | 3.75 |
| sp-B-4x1-c28-nospin-r4 | 4x1 | False | 28 | 37.0 | 16.3 | 3.9 | 2988 | {'mel': 1, 'enc': 130, 'dec': 10, 'dec_loop_total': 10} | 6.5 |
| sp-B-4x1-c28-r1 | 4x1 | True | 28 | 30.2 | 20.0 | 3.8 | 2991 | {'mel': 1, 'enc': 108, 'dec': 9, 'dec_loop_total': 9} | 2.3 |
| sp-B-4x1-c28-r2 | 4x1 | True | 28 | 44.0 | 13.7 | 4.4 | 3165 | {'mel': 1, 'enc': 158, 'dec': 13, 'dec_loop_total': 13} | 2.85 |
| sp-B-4x1-c28-r3 | 4x1 | True | 28 | 40.9 | 14.8 | 8.2 | 2997 | {'mel': 1, 'enc': 143, 'dec': 11, 'dec_loop_total': 11} | 6.05 |
| sp-B-4x1-c28-r4 | 4x1 | True | 28 | 35.7 | 16.9 | 3.7 | 2976 | {'mel': 1, 'enc': 127, 'dec': 10, 'dec_loop_total': 10} | 4.56 |
| sp-B-8x1-c28-r1 | 8x1 | True | 28 | 29.2 | 20.6 | 3.8 | 3209 | {'mel': 2, 'enc': 199, 'dec': 15, 'dec_loop_total': 15} | 3.31 |
| sp-B-8x1-c28-r2 | 8x1 | True | 28 | 29.0 | 20.8 | 3.5 | 3317 | {'mel': 2, 'enc': 202, 'dec': 15, 'dec_loop_total': 15} | 4.48 |
| sp-C-1x4-c28-r1 | 1x4 | True | 28 | 16.0 | 37.7 | 2.1 | 1370 | {'enc': 16} | 3.8 |
| sp-C-1x4-c28-r2 | 1x4 | True | 28 | 7.2 | 83.7 | 2.2 | 1370 | {'enc': 7} | 3.06 |
| sp-C-2x2-c28-r1 | 2x2 | True | 28 | 7.3 | 82.6 | 2.2 | 1418 | {'enc': 15} | 3.49 |
| sp-C-2x2-c28-r2 | 2x2 | True | 28 | 6.9 | 87.8 | 2.4 | 1425 | {'enc': 14} | 3.14 |
| sp-C-4x1-c10-r1 | 4x1 | True | 10 | 5.9 | 101.5 | 2.2 | 1367 | {'enc': 23} | 3.14 |
| sp-C-4x1-c10-r2 | 4x1 | True | 10 | 6.0 | 100.3 | 2.2 | 1397 | {'enc': 24} | 2.88 |
| sp-C-4x1-c20-r1 | 4x1 | True | 20 | 5.5 | 110.0 | 2.0 | 1403 | {'enc': 22} | 2.82 |
| sp-C-4x1-c20-r2 | 4x1 | True | 20 | 5.8 | 103.9 | 2.1 | 1397 | {'enc': 23} | 2.78 |
| sp-C-4x1-c28-nospin-r1 | 4x1 | False | 28 | 5.2 | 116.7 | 2.1 | 1436 | {'enc': 20} | 3.28 |
| sp-C-4x1-c28-nospin-r2 | 4x1 | False | 28 | 5.2 | 116.3 | 2.1 | 1461 | {'enc': 20} | 3.24 |
| sp-C-4x1-c28-nospin-r3 | 4x1 | False | 28 | 6.2 | 96.7 | 2.2 | 1482 | {'enc': 24} | 3.55 |
| sp-C-4x1-c28-nospin-r4 | 4x1 | False | 28 | 4.8 | 126.6 | 2.1 | 1468 | {'enc': 18} | 3.73 |
| sp-C-4x1-c28-r1 | 4x1 | True | 28 | 4.8 | 126.3 | 2.0 | 1456 | {'enc': 18} | 3.21 |
| sp-C-4x1-c28-r2 | 4x1 | True | 28 | 6.4 | 94.5 | 2.0 | 1469 | {'enc': 25} | 3.11 |
| sp-C-4x1-c28-r3 | 4x1 | True | 28 | 5.4 | 111.9 | 2.2 | 1444 | {'enc': 21} | 4.42 |
| sp-C-4x1-c28-r4 | 4x1 | True | 28 | 6.2 | 97.1 | 2.2 | 1472 | {'enc': 24} | 3.84 |
| sp-C-4x1-c40-r1 | 4x1 | True | 40 | 6.2 | 97.0 | 2.0 | 1558 | {'enc': 24} | 2.52 |
| sp-C-4x1-c40-r2 | 4x1 | True | 40 | 5.8 | 103.9 | 2.1 | 1528 | {'enc': 22} | 2.67 |
| sp-C-8x1-c28-r1 | 8x1 | True | 28 | 6.2 | 96.8 | 2.1 | 1571 | {'enc': 45} | 2.23 |
| sp-C-8x1-c28-r2 | 8x1 | True | 28 | 4.8 | 124.9 | 2.3 | 1598 | {'enc': 36} | 2.56 |
| sp-D-single-r1 | 4x1 | True | 28 | 25.1 | 24.0 | 2.6 | 1942 | {'mel': 1, 'enc': 92, 'dec': 3, 'dec_loop_total': 3} | 5.06 |
| sp-E-1x4-r1 | 1x4 | True | 28 | 247.4 | 2.4 | 0.0 | 4964 | {'all': 247} | 4.4 |

## Chunk length: WER % on the three 10-min files (all three micro / en / fr / de)
- A chunk 10: all 17.06 | de-10min 20.46 | en-10min 14.53 | fr-10min 16.69
- A chunk 20: all 13.67 | de-10min 16.16 | en-10min 15.41 | fr-10min 9.71
- A chunk 28: all 13.39 | de-10min 14.65 | en-10min 13.78 | fr-10min 11.87
- B chunk 28: all 6.75 | de-10min 10.43 | en-10min 4.62 | fr-10min 5.73
- C chunk 10: all 5.17 | de-10min 10.35 | en-10min 1.97 | fr-10min 3.91
- C chunk 20: all 4.18 | de-10min 8.84 | en-10min 1.36 | fr-10min 3.00
- C chunk 28: all 4.33 | de-10min 9.32 | en-10min 1.43 | fr-10min 2.93
- C chunk 40: all 4.37 | de-10min 9.39 | en-10min 1.43 | fr-10min 3.00
- D chunk 28: all 6.58 | de-10min 12.82 | en-10min 2.38 | fr-10min 5.45
- E chunk 28: all 5.79 | de-10min 10.19 | en-10min 2.51 | fr-10min 5.31

## Noise outputs
- A noise-office-60s: 0 words: ''
- A noise-pink-60s: 0 words: ''
- A noise-white-60s: 0 words: ''
- A silence-60s: 0 words: ''
- B noise-office-60s: 0 words: ''
- B noise-pink-60s: 0 words: ''
- B noise-white-60s: 0 words: ''
- B silence-60s: 0 words: ''
- C noise-office-60s: 0 words: ''
- C noise-pink-60s: 0 words: ''
- C noise-white-60s: 0 words: ''
- C silence-60s: 3 words: 'Et. Et. .'
- D noise-office-60s: 0 words: ''
- D noise-pink-60s: 0 words: ''
- D noise-white-60s: 0 words: ''
- D silence-60s: 0 words: ''
- E noise-office-60s: 3 words: 'so so so'
- E noise-pink-60s: 5 words: 'Thank you. Thank you. so'
- E noise-white-60s: 3 words: 'so so so'
- E silence-60s: 3 words: 'you you you'

## Probes
- A: AMI filler words ref 59 vs hyp 13; digit-words ref 0 vs hyp 2; English stopwords in fr/de hyps {'fr': 7, 'de': 8}; punct chars 1105, upper-case chars 1141; <unk> 0
- B: AMI filler words ref 59 vs hyp 26; digit-words ref 0 vs hyp 1; English stopwords in fr/de hyps {'fr': 0, 'de': 10}; punct chars 1190, upper-case chars 1181; <unk> 0
- C: AMI filler words ref 59 vs hyp 12; digit-words ref 0 vs hyp 0; English stopwords in fr/de hyps {'fr': 0, 'de': 8}; punct chars 976, upper-case chars 963; <unk> None
- D: AMI filler words ref 59 vs hyp 15; digit-words ref 0 vs hyp 4; English stopwords in fr/de hyps {'fr': 0, 'de': 7}; punct chars 1172, upper-case chars 1106; <unk> 0
- E: AMI filler words ref 59 vs hyp 12; digit-words ref 0 vs hyp 8; English stopwords in fr/de hyps {'fr': 0, 'de': 7}; punct chars 375, upper-case chars 345; <unk> None
