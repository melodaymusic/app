"""Checks the vocabulary tool's token ids against Hugging Face tokenizers (the reference laion_clap uses).
Usage: python -I check_tokens.py <tokenizer.json> <tokens.json>. Skips (exit 0) when tokenizers isn't installed."""
import json
import sys

try:
    from tokenizers import Tokenizer
except ImportError:
    print("tokenizers not installed; skipping the tokenizer parity check")
    sys.exit(0)

tokenizer = Tokenizer.from_file(sys.argv[1])
with open(sys.argv[2], encoding="utf-8") as f:
    expected = json.load(f)
bad = [p for p, ids in expected.items() if tokenizer.encode(p).ids != ids]
for prompt in bad:
    print(f"MISMATCH: {prompt!r}: tool {expected[prompt]} vs reference {tokenizer.encode(prompt).ids}")
print(f"Tokenizer parity: {len(expected) - len(bad)}/{len(expected)} prompts identical")
sys.exit(1 if bad else 0)
