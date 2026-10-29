---
license: cc0-1.0
tags:
- onnx
- clap
- audio
- audio-embedding
- music
library_name: onnx
---

# CLAP HTSAT-base music model, fp32 ONNX bundle

Pre-converted ONNX Runtime artifacts of the [LAION-CLAP](https://github.com/LAION-AI/CLAP)
music checkpoint `music_audioset_epoch_15_esc_90.14.pt` (HTSAT-base audio
tower, RoBERTa text tower, non-fusion), packaged for
[muzaiten](https://codeberg.org/11xx/muzaiten)'s optional semantic-analysis
provider `muzaiten-features-clap`.

The upstream code and checkpoint are CC0-1.0; this bundle is a mechanical
conversion and carries the same license.

## Files

| File | Contents |
| :--- | :--- |
| `audio.onnx` | HTSAT-base audio tower plus projection; input `waveform` float32 `[batch, 480000]` (10 s at 48 kHz, mono), output L2-normalized `[batch, 512]`; mel spectrogram computed in-graph |
| `text.onnx` | RoBERTa-base text tower plus projection; inputs `input_ids`/`attention_mask` int64 `[batch, 77]`, output L2-normalized `[batch, 512]` |
| `tokenizer.json` | The exact RoBERTa tokenizer used by the checkpoint |
| `manifest.json` | SHA-256 and byte count of each artifact plus the source checkpoint hash (`fae3e9c0…`) and feature revision (`clap-htsat-base-audio-window-v1`) |

## Provenance and parity

Exported with muzaiten's converter
(`python -m muzaiten_features_clap.convert`, torch legacy exporter, opset 18)
from the SHA-256-pinned upstream checkpoint. Full-pipeline parity against the
reference `laion_clap` implementation: 68 audio cases with mean cosine 1.0
and minimum 0.9999989; text token ids and attention masks exactly equal with
cosine 1.0. Embeddings are interchangeable with the torch reference, so
muzaiten feature stores need no re-analysis.

`muzaiten-features-clap` downloads this bundle through `model download`,
rejects the manifest unless its identity matches the constants baked into the
provider release, and verifies every file hash while streaming. See
[muzaiten's semantic-analysis guide](https://codeberg.org/11xx/muzaiten/src/branch/master/docs/semantic-analysis.md)
for usage; direct consumers can load the graphs with plain `onnxruntime` and
`tokenizers`, replicating the int16 round-trip quantization and repeat-pad
preprocessing described in the provider source.
