"""Makes the text model ME ships for free-form search: CLAP's text tower with its weights stored in 8 bits, a little over a
quarter of the full model's size. Then checks it still agrees with the full model
on every vocabulary prompt.

Usage: python -I quantize.py <text.onnx> <tokenizer.json> <vocabulary.json> <out text-w8.onnx>
Needs onnxruntime==1.23.2, onnx==1.23.2 and tokenizers==0.23.3 (download.ps1 sets them up in model/venv)."""
import json
import os
import sys

import numpy as np
import onnx
import onnxruntime as ort
from onnxruntime.quantization import QuantType, quantize_dynamic
from onnxruntime.quantization.matmul_nbits_quantizer import DefaultWeightOnlyQuantConfig, MatMulNBitsQuantizer
from tokenizers import Tokenizer

full_path, tokenizer_path, vocabulary_path, out_path = sys.argv[1:5]

# The published model pads every prompt to 77 tokens; a dynamic length lets ME run only the tokens a caption has (about 8).
model = onnx.load(full_path)
for graph_input in model.graph.input:
    graph_input.type.tensor_type.shape.dim[1].dim_param = "tokens"
# The encoder's matrices: 8-bit weights in blocks of 32 (MatMulNBits), multiplied in 8 bits block by block (accuracy level 4):
# about 35 ms a phrase on four threads, against 240 ms unpacking weights to full precision each time. Plain dynamic int8 moved
# about a sixth of a phrase's top 50 songs; this moves about one in twenty.
config = DefaultWeightOnlyQuantConfig(block_size=32, is_symmetric=True, bits=8, accuracy_level=4, op_types_to_quantize=("MatMul",))
quantizer = MatMulNBitsQuantizer(model, block_size=32, is_symmetric=True, accuracy_level=4, algo_config=config,
                                 op_types_to_quantize=("MatMul",))
quantizer.process()
temp = out_path + ".matmul.onnx"
quantizer.model.save_model_to_file(temp, use_external_data_format=False)
# The word embeddings (a third of the model): 8-bit, dequantised as each token is looked up.
quantize_dynamic(temp, out_path, weight_type=QuantType.QInt8, op_types_to_quantize=["Gather"])
os.remove(temp)

tokenizer = Tokenizer.from_file(tokenizer_path)
options = ort.SessionOptions()
options.log_severity_level = 3
full = ort.InferenceSession(full_path, options, providers=["CPUExecutionProvider"])
small = ort.InferenceSession(out_path, options, providers=["CPUExecutionProvider"])


def embed(session, text):
    ids = tokenizer.encode(text).ids[:77]
    mask = [1] * len(ids) + [0] * (77 - len(ids))
    ids = ids + [1] * (77 - len(ids))
    if session is small:  # the app runs only the tokens a prompt has
        ids, mask = ids[:sum(mask)], mask[:sum(mask)]
    v = session.run(None, {"input_ids": np.array([ids], np.int64), "attention_mask": np.array([mask], np.int64)})[0][0]
    return v / np.linalg.norm(v)


vocabulary = json.load(open(vocabulary_path, encoding="utf-8"))
prompts = []
for category in vocabulary["categories"]:
    for term in category["terms"]:
        prompts += term.get("prompts") or [t.replace("{}", term["text"]) for t in category["templates"]]
for phrase in ["sea shanty", "walking in the city", "a medieval tavern", "fireworks", "underwater", "sad robot"]:
    prompts += [f"{phrase} music", f"a {phrase} song", f"the sound of {phrase}"]
cosines = np.array([float(embed(full, p) @ embed(small, p)) for p in prompts])
print(f"{len(prompts)} prompts: cosine to the full model mean {cosines.mean():.5f}, min {cosines.min():.5f}")
if cosines.min() < 0.99:
    sys.exit("The 8-bit text model drifted too far from the full one")
